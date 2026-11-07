using System.Text.RegularExpressions;

namespace Backend.Services;

/// <summary>
/// Invoice number: <c>YYMMDD zonnewijzer U&lt;user id&gt;-&lt;index&gt;</c>.
/// The date is the first day of the month after the billed month. The index is that user's 1-based invoice sequence.
/// </summary>
public static class InvoiceNumbers
{
    /// <summary>
    /// Invoices through September 2026 were printed as <c>INV-yyyy-MM-U{userId}</c>.
    /// October 2026 and later use <see cref="Format"/>.
    /// </summary>
    public static bool UsesLegacyFormat(int year, int month) =>
        year < 2026 || (year == 2026 && month <= 9);

    public static string Legacy(int year, int month, int userId) =>
        $"INV-{year}-{month:00}-U{userId}";

    public static DateTime InvoiceDate(int year, int month) =>
        new DateTime(year, month, 1).AddMonths(1);

    public static string Format(int year, int month, int userId, int index) =>
        $"{InvoiceDate(year, month):yyMMdd} zonnewijzer U{userId}-{index}";

    /// <summary>
    /// Reads the billed user and month out of a Mollie payment description that contains <see cref="Format"/> or <see cref="Legacy"/>.
    /// </summary>
    public static bool TryParseBilledPeriod(string? description, out int userId, out int year, out int month)
    {
        userId = 0;
        year = 0;
        month = 0;
        if (string.IsNullOrWhiteSpace(description))
            return false;

        var legacy = LegacyDescription.Match(description);
        if (legacy.Success
            && int.TryParse(legacy.Groups["year"].Value, out year)
            && int.TryParse(legacy.Groups["month"].Value, out month)
            && int.TryParse(legacy.Groups["user"].Value, out userId)
            && month is >= 1 and <= 12)
        {
            return true;
        }

        var current = CurrentDescription.Match(description);
        if (!current.Success
            || !int.TryParse(current.Groups["yy"].Value, out var yy)
            || !int.TryParse(current.Groups["mm"].Value, out var mm)
            || !int.TryParse(current.Groups["dd"].Value, out var dd)
            || !int.TryParse(current.Groups["user"].Value, out userId))
        {
            return false;
        }

        try
        {
            var billed = new DateTime(2000 + yy, mm, dd).AddMonths(-1);
            year = billed.Year;
            month = billed.Month;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static readonly Regex LegacyDescription = new(
        @"INV-(?<year>\d{4})-(?<month>\d{2})-U(?<user>\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CurrentDescription = new(
        @"(?<yy>\d{2})(?<mm>\d{2})(?<dd>\d{2}) zonnewijzer U(?<user>\d+)-\d+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static int IndexForMonth(IEnumerable<(int Year, int Month)> billedMonths, int year, int month)
    {
        var earlier = billedMonths
            .Select(m => (m.Year, m.Month))
            .Distinct()
            .Count(m => m.Year < year || (m.Year == year && m.Month < month));
        return earlier + 1;
    }
}
