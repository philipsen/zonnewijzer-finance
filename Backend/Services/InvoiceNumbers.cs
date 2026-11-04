namespace Backend.Services;

/// <summary>
/// Invoice number: <c>YYMMDD zonnewijzer &lt;user id&gt;-&lt;index&gt;</c>.
/// The date is the last day of the billed month. The index is that user's 1-based invoice sequence.
/// </summary>
public static class InvoiceNumbers
{
    public static DateTime PeriodEnd(int year, int month) =>
        new(year, month, DateTime.DaysInMonth(year, month));

    public static string Format(int year, int month, int userId, int index) =>
        $"{PeriodEnd(year, month):yyMMdd} zonnewijzer {userId}-{index}";

    public static int IndexForMonth(IEnumerable<(int Year, int Month)> billedMonths, int year, int month)
    {
        var earlier = billedMonths
            .Select(m => (m.Year, m.Month))
            .Distinct()
            .Count(m => m.Year < year || (m.Year == year && m.Month < month));
        return earlier + 1;
    }
}
