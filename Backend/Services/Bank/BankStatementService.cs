using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Bank;

public sealed record BankReviewRow(
    int Id,
    DateOnly BookedOn,
    decimal Amount,
    string CounterpartyName,
    string? CounterpartyIban,
    string Description,
    string? SuggestionKind,
    int? AppUserId,
    string? UserName,
    int? Year,
    int? Month,
    string? InvoiceNumber,
    decimal? InvoiceAmount);

public sealed record BankReview(
    IReadOnlyList<BankReviewRow> Open,
    IReadOnlyList<BankReviewRow> Confirmed,
    IReadOnlyList<BankReviewRow> Ignored);

public sealed record BankImportOutcome(
    string? Error,
    int Imported,
    int Duplicates,
    int Skipped,
    BankReview? Review);

public sealed class ConfirmBankMatchRequest
{
    public int AppUserId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
}

public sealed class BankStatementService
{
    private readonly ApplicationDbContext _context;
    private readonly InvoicePaymentCatalog _catalog;

    public BankStatementService(ApplicationDbContext context, InvoicePaymentCatalog catalog)
    {
        _context = context;
        _catalog = catalog;
    }

    public async Task<BankReview> GetReviewAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await _catalog.LoadAsync(cancellationToken);
        var transactions = await _context.BankTransactions
            .AsNoTracking()
            .OrderByDescending(transaction => transaction.BookedOn)
            .ThenByDescending(transaction => transaction.Id)
            .ToListAsync(cancellationToken);
        var matches = await _context.InvoiceBankMatches
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var names = await _context.Users
            .AsNoTracking()
            .Select(user => new { user.Id, user.Name })
            .ToDictionaryAsync(user => user.Id, user => user.Name, cancellationToken);

        return BuildReview(transactions, matches, names, catalog);
    }

    public async Task<BankImportOutcome> ImportAsync(string csv, CancellationToken cancellationToken = default)
    {
        var parsed = TriodosCsvParser.Parse(csv);
        if (parsed.Transactions.Count == 0)
        {
            return new BankImportOutcome(
                "No Triodos payments found. Use the headerless CSV export from Triodos.",
                0,
                0,
                parsed.Skipped,
                null);
        }

        var existing = await _context.BankTransactions
            .Select(transaction => transaction.Fingerprint)
            .ToListAsync(cancellationToken);
        var seen = existing.ToHashSet(StringComparer.Ordinal);
        var imported = 0;
        var duplicates = 0;

        foreach (var row in parsed.Transactions)
        {
            if (!seen.Add(row.Fingerprint))
            {
                duplicates++;
                continue;
            }

            _context.BankTransactions.Add(new BankTransaction
            {
                BookedOn = row.BookedOn,
                AccountIban = row.AccountIban,
                Amount = row.Amount,
                Direction = row.Direction,
                CounterpartyName = row.CounterpartyName,
                CounterpartyBic = row.CounterpartyBic,
                CounterpartyIban = row.CounterpartyIban,
                TransactionCode = row.TransactionCode,
                Description = row.Description,
                BalanceAfter = row.BalanceAfter,
                Fingerprint = row.Fingerprint,
                ReviewStatus = BankTransaction.StatusOpen,
                CreatedAt = DateTime.UtcNow
            });
            imported++;
        }

        if (imported > 0)
            await _context.SaveChangesAsync(cancellationToken);

        var review = await GetReviewAsync(cancellationToken);
        return new BankImportOutcome(null, imported, duplicates, parsed.Skipped, review);
    }

    public async Task<(string? Error, BankReview? Review)> ConfirmAsync(
        int transactionId,
        ConfirmBankMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Month is < 1 or > 12 || request.Year is < 2000 or > 2100)
            return ("Choose a valid invoice period.", null);

        var transaction = await _context.BankTransactions
            .FirstOrDefaultAsync(row => row.Id == transactionId, cancellationToken);
        if (transaction == null)
            return ("Payment not found.", null);
        if (!string.Equals(transaction.Direction, BankTransaction.DirectionCredit, StringComparison.Ordinal))
            return ("Only incoming payments can be matched to an invoice.", null);
        if (transaction.ReviewStatus == BankTransaction.StatusConfirmed)
            return ("This payment is already matched.", null);

        var catalog = await _catalog.LoadAsync(cancellationToken);
        var invoice = catalog.FirstOrDefault(candidate =>
            candidate.UserId == request.AppUserId
            && candidate.Year == request.Year
            && candidate.Month == request.Month);
        if (invoice == null)
            return ("No invoice for that user and month.", null);

        var taken = await _context.InvoiceBankMatches
            .AnyAsync(match =>
                match.AppUserId == request.AppUserId
                && match.Year == request.Year
                && match.Month == request.Month
                && match.BankTransactionId != transactionId,
                cancellationToken);
        if (taken || invoice.IsPaid)
            return ("That invoice is already paid.", null);

        var proposal = BankInvoiceMatcher.Match(transaction.Description, transaction.Amount, catalog);
        var kind = proposal.Kind is BankMatchKind.Certain or BankMatchKind.Suggested
            && proposal.UserId == request.AppUserId
            && proposal.Year == request.Year
            && proposal.Month == request.Month
            ? proposal.Kind
            : InvoiceBankMatch.KindManual;

        var existing = await _context.InvoiceBankMatches
            .FirstOrDefaultAsync(match => match.BankTransactionId == transactionId, cancellationToken);
        if (existing == null)
        {
            _context.InvoiceBankMatches.Add(new InvoiceBankMatch
            {
                BankTransactionId = transactionId,
                AppUserId = invoice.UserId,
                Year = invoice.Year,
                Month = invoice.Month,
                InvoiceNumber = invoice.InvoiceNumber,
                MatchKind = kind,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.AppUserId = invoice.UserId;
            existing.Year = invoice.Year;
            existing.Month = invoice.Month;
            existing.InvoiceNumber = invoice.InvoiceNumber;
            existing.MatchKind = kind;
        }

        transaction.ReviewStatus = BankTransaction.StatusConfirmed;
        await _context.SaveChangesAsync(cancellationToken);
        return (null, await GetReviewAsync(cancellationToken));
    }

    public async Task<(string? Error, BankReview? Review)> IgnoreAsync(int transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.BankTransactions
            .FirstOrDefaultAsync(row => row.Id == transactionId, cancellationToken);
        if (transaction == null)
            return ("Payment not found.", null);
        if (transaction.ReviewStatus == BankTransaction.StatusConfirmed)
            return ("Undo the match before ignoring this payment.", null);

        transaction.ReviewStatus = BankTransaction.StatusIgnored;
        await _context.SaveChangesAsync(cancellationToken);
        return (null, await GetReviewAsync(cancellationToken));
    }

    public async Task<(string? Error, BankReview? Review)> ReopenAsync(int transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.BankTransactions
            .FirstOrDefaultAsync(row => row.Id == transactionId, cancellationToken);
        if (transaction == null)
            return ("Payment not found.", null);

        var matches = await _context.InvoiceBankMatches
            .Where(match => match.BankTransactionId == transactionId)
            .ToListAsync(cancellationToken);
        _context.InvoiceBankMatches.RemoveRange(matches);
        transaction.ReviewStatus = BankTransaction.StatusOpen;
        await _context.SaveChangesAsync(cancellationToken);
        return (null, await GetReviewAsync(cancellationToken));
    }

    private static BankReview BuildReview(
        IReadOnlyList<BankTransaction> transactions,
        IReadOnlyList<InvoiceBankMatch> matches,
        IReadOnlyDictionary<int, string> names,
        IReadOnlyList<InvoiceCandidate> catalog)
    {
        var matchByTransaction = matches.ToDictionary(match => match.BankTransactionId);
        var open = new List<BankReviewRow>();
        var confirmed = new List<BankReviewRow>();
        var ignored = new List<BankReviewRow>();

        foreach (var transaction in transactions)
        {
            if (!string.Equals(transaction.Direction, BankTransaction.DirectionCredit, StringComparison.Ordinal))
                continue;

            if (transaction.ReviewStatus == BankTransaction.StatusConfirmed
                && matchByTransaction.TryGetValue(transaction.Id, out var match))
            {
                names.TryGetValue(match.AppUserId, out var name);
                confirmed.Add(RowFromMatch(transaction, match, name));
                continue;
            }

            var proposal = BankInvoiceMatcher.Match(transaction.Description, transaction.Amount, catalog);
            var row = RowFromProposal(transaction, proposal);
            if (transaction.ReviewStatus == BankTransaction.StatusIgnored)
                ignored.Add(row);
            else
                open.Add(row);
        }

        return new BankReview(open, confirmed, ignored);
    }

    private static BankReviewRow RowFromMatch(BankTransaction transaction, InvoiceBankMatch match, string? userName) =>
        new(
            transaction.Id,
            transaction.BookedOn,
            transaction.Amount,
            transaction.CounterpartyName,
            transaction.CounterpartyIban,
            transaction.Description,
            match.MatchKind,
            match.AppUserId,
            userName,
            match.Year,
            match.Month,
            match.InvoiceNumber,
            transaction.Amount);

    private static BankReviewRow RowFromProposal(BankTransaction transaction, BankMatchProposal proposal) =>
        new(
            transaction.Id,
            transaction.BookedOn,
            transaction.Amount,
            transaction.CounterpartyName,
            transaction.CounterpartyIban,
            transaction.Description,
            proposal.Kind,
            proposal.UserId,
            proposal.UserName,
            proposal.Year,
            proposal.Month,
            proposal.InvoiceNumber,
            proposal.InvoiceAmount);
}
