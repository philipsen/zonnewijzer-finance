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
    decimal? InvoiceAmount,
    int? MatchId = null,
    decimal AllocatedAmount = 0,
    decimal UnallocatedAmount = 0,
    IReadOnlyList<BankAllocation>? Allocations = null,
    IReadOnlyList<BankMatchShare>? Shares = null);

public sealed record BankAllocation(
    int MatchId,
    int AppUserId,
    string? UserName,
    int Year,
    int Month,
    string InvoiceNumber,
    decimal Amount);

public sealed record BankReview(
    IReadOnlyList<BankReviewRow> Open,
    IReadOnlyList<BankReviewRow> Confirmed,
    IReadOnlyList<BankReviewRow> Ignored);

public sealed record BankImportedTransaction(
    DateOnly BookedOn,
    decimal Amount,
    string Direction,
    string CounterpartyName,
    string Description,
    bool AlreadyImported);

public sealed record BankImportOutcome(
    string? Error,
    int Imported,
    int Duplicates,
    int Skipped,
    BankReview? Review,
    IReadOnlyList<BankImportedTransaction>? Transactions = null);

public sealed class ClearBankTransactionsRequest
{
    public bool Confirm { get; set; }
}

public sealed class ConfirmBankMatchRequest
{
    public int AppUserId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal? Amount { get; set; }
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

    public async Task<(int Transactions, int Bindings)> ClearDownloadedAsync(CancellationToken cancellationToken = default)
    {
        var bindings = await _context.InvoiceBankMatches.ToListAsync(cancellationToken);
        var transactions = await _context.BankTransactions.ToListAsync(cancellationToken);
        _context.InvoiceBankMatches.RemoveRange(bindings);
        _context.BankTransactions.RemoveRange(transactions);
        if (bindings.Count > 0 || transactions.Count > 0)
            await _context.SaveChangesAsync(cancellationToken);
        return (transactions.Count, bindings.Count);
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
        var rows = parsed.Transactions
            .Where(row => row.AccountIban == TriodosCsvParser.CooperativeAccountIban)
            .ToList();
        var skipped = parsed.Skipped + parsed.Transactions.Count - rows.Count;
        if (rows.Count == 0)
        {
            return new BankImportOutcome(
                parsed.Transactions.Count == 0
                    ? "No Triodos payments found. Use the headerless CSV export from Triodos."
                    : $"No payments found for account {TriodosCsvParser.CooperativeAccountIban}.",
                0,
                0,
                skipped,
                null);
        }

        var existing = await _context.BankTransactions
            .Select(transaction => transaction.Fingerprint)
            .ToListAsync(cancellationToken);
        var seen = existing.ToHashSet(StringComparer.Ordinal);
        var imported = 0;
        var duplicates = 0;
        var downloaded = new List<BankImportedTransaction>(rows.Count);

        foreach (var row in rows)
        {
            var alreadyImported = !seen.Add(row.Fingerprint);
            downloaded.Add(new BankImportedTransaction(
                row.BookedOn,
                row.Amount,
                row.Direction,
                row.CounterpartyName,
                row.Description,
                alreadyImported));
            if (alreadyImported)
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
        return new BankImportOutcome(null, imported, duplicates, skipped, review, downloaded);
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

        var links = await _context.InvoiceBankMatches
            .Where(match => match.BankTransactionId == transactionId)
            .ToListAsync(cancellationToken);
        var unallocated = transaction.Amount - links.Sum(match => match.Amount);
        if (unallocated <= 0)
            return ("This payment is already matched.", null);

        var catalog = await _catalog.LoadAsync(cancellationToken);
        var invoice = catalog.FirstOrDefault(candidate =>
            candidate.UserId == request.AppUserId
            && candidate.Year == request.Year
            && candidate.Month == request.Month);
        if (invoice == null)
            return ("No invoice for that user and month.", null);

        var alreadyOnInvoice = await _context.InvoiceBankMatches
            .Where(match =>
                match.AppUserId == invoice.UserId
                && match.Year == invoice.Year
                && match.Month == invoice.Month)
            .SumAsync(match => (decimal?)match.Amount, cancellationToken) ?? 0m;
        var outstanding = invoice.IsPaid ? 0m : Math.Max(0m, invoice.TotalAmount - alreadyOnInvoice);

        decimal amount;
        if (request.Amount is decimal requested)
        {
            if (requested <= 0)
                return ("Enter an amount greater than zero.", null);
            if (requested > unallocated)
                return ("That is more than this payment has left.", null);
            amount = decimal.Round(requested, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            if (outstanding <= 0)
                return ("That invoice is already paid.", null);
            amount = Math.Min(unallocated, outstanding);
        }

        if (amount <= 0)
            return ("That invoice is already paid.", null);

        var proposal = BankInvoiceMatcher.Match(transaction.Description, transaction.Amount, catalog);
        var coversInvoice = proposal.Shares?.Any(share =>
                share.UserId == request.AppUserId
                && share.Year == request.Year
                && share.Month == request.Month) == true
            || (proposal.UserId == request.AppUserId
                && proposal.Year == request.Year
                && proposal.Month == request.Month);
        var kind = coversInvoice
            && proposal.Kind is BankMatchKind.Certain or BankMatchKind.Suggested or BankMatchKind.Partial or BankMatchKind.Split
            ? proposal.Kind
            : InvoiceBankMatch.KindManual;

        var sameInvoice = links.FirstOrDefault(match =>
            match.AppUserId == invoice.UserId
            && match.Year == invoice.Year
            && match.Month == invoice.Month);
        if (sameInvoice == null)
        {
            _context.InvoiceBankMatches.Add(new InvoiceBankMatch
            {
                BankTransactionId = transactionId,
                AppUserId = invoice.UserId,
                Year = invoice.Year,
                Month = invoice.Month,
                InvoiceNumber = invoice.InvoiceNumber,
                Amount = amount,
                MatchKind = kind,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            sameInvoice.Amount += amount;
            sameInvoice.MatchKind = kind;
            sameInvoice.InvoiceNumber = invoice.InvoiceNumber;
        }

        transaction.ReviewStatus = unallocated - amount <= 0
            ? BankTransaction.StatusConfirmed
            : BankTransaction.StatusOpen;
        await _context.SaveChangesAsync(cancellationToken);
        return (null, await GetReviewAsync(cancellationToken));
    }

    public async Task<(string? Error, BankReview? Review)> IgnoreAsync(int transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.BankTransactions
            .FirstOrDefaultAsync(row => row.Id == transactionId, cancellationToken);
        if (transaction == null)
            return ("Payment not found.", null);
        var linked = await _context.InvoiceBankMatches
            .AnyAsync(match => match.BankTransactionId == transactionId, cancellationToken);
        if (linked || transaction.ReviewStatus == BankTransaction.StatusConfirmed)
            return ("Undo the match before ignoring this payment.", null);

        transaction.ReviewStatus = BankTransaction.StatusIgnored;
        await _context.SaveChangesAsync(cancellationToken);
        return (null, await GetReviewAsync(cancellationToken));
    }

    public async Task<(string? Error, BankReview? Review)> ReopenAsync(
        int transactionId,
        int? matchId = null,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _context.BankTransactions
            .FirstOrDefaultAsync(row => row.Id == transactionId, cancellationToken);
        if (transaction == null)
            return ("Payment not found.", null);

        var matches = await _context.InvoiceBankMatches
            .Where(match => match.BankTransactionId == transactionId)
            .ToListAsync(cancellationToken);
        if (matchId is int selected)
        {
            var one = matches.FirstOrDefault(match => match.Id == selected);
            if (one == null)
                return ("That link was not found.", null);
            _context.InvoiceBankMatches.Remove(one);
        }
        else
        {
            _context.InvoiceBankMatches.RemoveRange(matches);
        }

        var removed = matchId ?? 0;
        var stillAllocated = matches
            .Where(match => matchId == null || match.Id != removed)
            .Sum(match => match.Amount);
        transaction.ReviewStatus = matchId != null && stillAllocated >= transaction.Amount
            ? BankTransaction.StatusConfirmed
            : BankTransaction.StatusOpen;
        await _context.SaveChangesAsync(cancellationToken);
        return (null, await GetReviewAsync(cancellationToken));
    }

    private static BankReview BuildReview(
        IReadOnlyList<BankTransaction> transactions,
        IReadOnlyList<InvoiceBankMatch> matches,
        IReadOnlyDictionary<int, string> names,
        IReadOnlyList<InvoiceCandidate> catalog)
    {
        var matchesByTransaction = matches
            .GroupBy(match => match.BankTransactionId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var open = new List<BankReviewRow>();
        var confirmed = new List<BankReviewRow>();
        var ignored = new List<BankReviewRow>();

        foreach (var transaction in transactions)
        {
            if (!string.Equals(transaction.Direction, BankTransaction.DirectionCredit, StringComparison.Ordinal))
                continue;

            matchesByTransaction.TryGetValue(transaction.Id, out var links);
            links ??= [];
            var allocated = links.Sum(match => match.Amount);
            var unallocated = transaction.Amount - allocated;
            if (links.Count > 0 && unallocated <= 0)
            {
                foreach (var match in links)
                {
                    names.TryGetValue(match.AppUserId, out var name);
                    confirmed.Add(RowFromMatch(transaction, match, name));
                }
                continue;
            }

            var proposal = BankInvoiceMatcher.Match(transaction.Description, unallocated > 0 ? unallocated : transaction.Amount, catalog);
            var row = RowFromProposal(transaction, proposal, names, links, unallocated);
            if (transaction.ReviewStatus == BankTransaction.StatusIgnored && links.Count == 0)
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
            match.Amount,
            match.Id,
            match.Amount,
            0,
            Array.Empty<BankAllocation>());

    private static BankReviewRow RowFromProposal(
        BankTransaction transaction,
        BankMatchProposal proposal,
        IReadOnlyDictionary<int, string> names,
        IReadOnlyList<InvoiceBankMatch> links,
        decimal unallocated)
    {
        var allocations = links
            .Select(match =>
            {
                names.TryGetValue(match.AppUserId, out var name);
                return new BankAllocation(
                    match.Id,
                    match.AppUserId,
                    name,
                    match.Year,
                    match.Month,
                    match.InvoiceNumber,
                    match.Amount);
            })
            .ToList();
        return new BankReviewRow(
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
            proposal.InvoiceAmount,
            null,
            transaction.Amount - unallocated,
            unallocated,
            allocations,
            proposal.Shares);
    }
}
