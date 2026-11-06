using System.Globalization;
using System.Text.RegularExpressions;

namespace Backend.Services.Bank;

public sealed record InvoiceCandidate(
    int UserId,
    string UserName,
    int Year,
    int Month,
    string InvoiceNumber,
    decimal TotalAmount,
    bool IsPaid);

public static class BankMatchKind
{
    public const string Certain = "Certain";
    public const string Suggested = "Suggested";
    public const string Partial = "Partial";
    public const string Split = "Split";
    public const string AlreadyPaid = "AlreadyPaid";
}

public sealed record BankMatchShare(
    int UserId,
    string UserName,
    int Year,
    int Month,
    string InvoiceNumber,
    decimal Amount);

public sealed record BankMatchProposal(
    string? Kind,
    int? UserId,
    string? UserName,
    int? Year,
    int? Month,
    string? InvoiceNumber,
    decimal? InvoiceAmount,
    IReadOnlyList<BankMatchShare>? Shares = null);

/// <summary>
/// Matches one incoming bank description to an invoice.
/// Through September 2026 the printed number is <c>INV-yyyy-MM-U{userId}</c>.
/// From October 2026 it is <c>yyMMdd zonnewijzer U{userId}-{index}</c>.
/// </summary>
public static partial class BankInvoiceMatcher
{
    public static BankMatchProposal Match(string? description, decimal amount, IReadOnlyList<InvoiceCandidate> invoices)
    {
        var text = description ?? string.Empty;
        var references = ExtractReferences(text);
        if (references.Count > 0)
            return MatchReferences(references, amount, invoices);

        return MatchByNameAndMonth(text, amount, invoices);
    }

    private static BankMatchProposal MatchReferences(
        IReadOnlyList<InvoiceRef> references,
        decimal amount,
        IReadOnlyList<InvoiceCandidate> invoices)
    {
        var resolved = new List<InvoiceCandidate>();
        foreach (var reference in references)
        {
            var invoice = Resolve(reference, invoices);
            if (invoice == null)
                return None();
            if (resolved.All(existing => !SameInvoice(existing, invoice)))
                resolved.Add(invoice);
        }

        if (resolved.Count > 1)
            return MatchSeveral(resolved, amount);

        var match = resolved[0];
        if (match.IsPaid)
            return Proposal(BankMatchKind.AlreadyPaid, match);
        if (match.TotalAmount == amount)
            return Proposal(BankMatchKind.Certain, match);
        if (amount > 0 && amount < match.TotalAmount)
            return Proposal(BankMatchKind.Partial, match);

        return None();
    }

    private static BankMatchProposal MatchByNameAndMonth(
        string description,
        decimal amount,
        IReadOnlyList<InvoiceCandidate> invoices)
    {
        var months = MonthTokens(description);
        if (months.Count != 1)
            return None();

        var names = MatchingNames(description, invoices);
        if (names.Count != 1)
            return None();

        var (year, month) = months[0];
        var name = names[0];
        var invoice = invoices.FirstOrDefault(candidate =>
            candidate.Year == year
            && candidate.Month == month
            && string.Equals(candidate.UserName.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (invoice == null || invoice.TotalAmount != amount)
            return None();

        return Proposal(invoice.IsPaid ? BankMatchKind.AlreadyPaid : BankMatchKind.Suggested, invoice);
    }

    private static BankMatchProposal MatchSeveral(IReadOnlyList<InvoiceCandidate> invoices, decimal amount)
    {
        if (invoices.All(invoice => invoice.IsPaid))
            return Proposal(BankMatchKind.AlreadyPaid, invoices[0]);

        var open = invoices.Where(invoice => !invoice.IsPaid).ToList();
        if (open.Count == 1)
        {
            var only = open[0];
            if (only.TotalAmount == amount)
                return Proposal(BankMatchKind.Certain, only);
            if (amount > 0 && amount < only.TotalAmount)
                return Proposal(BankMatchKind.Partial, only);
            return None();
        }

        var sum = open.Sum(invoice => invoice.TotalAmount);
        if (open.Count < 2 || sum != amount)
            return None();

        var shares = open
            .Select(invoice => new BankMatchShare(
                invoice.UserId,
                invoice.UserName,
                invoice.Year,
                invoice.Month,
                invoice.InvoiceNumber,
                invoice.TotalAmount))
            .ToList();
        var first = shares[0];
        return new BankMatchProposal(
            BankMatchKind.Split,
            first.UserId,
            first.UserName,
            first.Year,
            first.Month,
            first.InvoiceNumber,
            sum,
            shares);
    }

    private static InvoiceCandidate? Resolve(InvoiceRef reference, IReadOnlyList<InvoiceCandidate> invoices)
    {
        if (reference.CurrentNumber != null)
        {
            return invoices.FirstOrDefault(candidate =>
                string.Equals(Normalize(candidate.InvoiceNumber), reference.CurrentNumber, StringComparison.Ordinal));
        }

        if (reference.Year is int year && reference.Month is int month && reference.UserId is int userId
            && InvoiceNumbers.UsesLegacyFormat(year, month))
        {
            return invoices.FirstOrDefault(candidate =>
                candidate.UserId == userId && candidate.Year == year && candidate.Month == month);
        }

        return null;
    }

    private static List<InvoiceRef> ExtractReferences(string description)
    {
        var refs = new List<InvoiceRef>();
        foreach (Match match in LegacyRef().Matches(description))
        {
            refs.Add(new InvoiceRef(
                int.Parse(match.Groups["user"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture),
                null));
        }

        foreach (Match match in CurrentRef().Matches(description))
        {
            refs.Add(new InvoiceRef(null, null, null, Normalize(match.Value)));
        }

        return refs;
    }

    private static List<(int Year, int Month)> MonthTokens(string description)
    {
        var found = new List<(int Year, int Month)>();
        foreach (Match match in MonthRef().Matches(description))
        {
            if (!MonthNames.TryGetValue(match.Groups["mon"].Value, out var month))
                continue;
            var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
            var token = (year, month);
            if (!found.Contains(token))
                found.Add(token);
        }

        return found;
    }

    private static List<string> MatchingNames(string description, IReadOnlyList<InvoiceCandidate> invoices)
    {
        var names = invoices
            .Select(candidate => candidate.UserName.Trim())
            .Where(name => name.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => description.Contains(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return names
            .Where(name => !names.Any(other =>
                other.Length > name.Length
                && other.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static string Normalize(string value)
    {
        var collapsed = Whitespace().Replace(value.Trim(), " ");
        return collapsed.ToLowerInvariant();
    }

    private static bool SameInvoice(InvoiceCandidate left, InvoiceCandidate right) =>
        left.UserId == right.UserId && left.Year == right.Year && left.Month == right.Month;

    private static BankMatchProposal Proposal(string kind, InvoiceCandidate invoice) =>
        new(kind, invoice.UserId, invoice.UserName, invoice.Year, invoice.Month, invoice.InvoiceNumber, invoice.TotalAmount);

    private static BankMatchProposal None() => new(null, null, null, null, null, null, null);

    private readonly record struct InvoiceRef(int? UserId, int? Year, int? Month, string? CurrentNumber);

    private static readonly Dictionary<string, int> MonthNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"] = 1, ["januari"] = 1, ["january"] = 1,
        ["feb"] = 2, ["februari"] = 2, ["february"] = 2,
        ["mrt"] = 3, ["maart"] = 3, ["mar"] = 3, ["march"] = 3,
        ["apr"] = 4, ["april"] = 4,
        ["mei"] = 5, ["may"] = 5,
        ["jun"] = 6, ["juni"] = 6, ["june"] = 6,
        ["jul"] = 7, ["juli"] = 7, ["july"] = 7,
        ["aug"] = 8, ["augustus"] = 8, ["august"] = 8,
        ["sep"] = 9, ["september"] = 9,
        ["okt"] = 10, ["oct"] = 10, ["oktober"] = 10, ["october"] = 10,
        ["nov"] = 11, ["november"] = 11,
        ["dec"] = 12, ["december"] = 12
    };

    [GeneratedRegex(@"INV-(?<year>\d{4})-(?<month>\d{2})-U(?<user>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyRef();

    [GeneratedRegex(@"(?<date>\d{6})\s+zonnewijzer\s+U(?<user>\d+)-(?<index>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurrentRef();

    [GeneratedRegex(@"\b(?<mon>januari|january|jan|februari|february|feb|maart|march|mrt|mar|april|apr|mei|may|juni|june|jun|juli|july|jul|augustus|august|aug|september|sep|oktober|october|okt|oct|november|nov|december|dec)\b[\s.\-]+(?<year>20\d{2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MonthRef();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
