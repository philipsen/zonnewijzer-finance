namespace Backend.Contracts;

public sealed class GnuCashCombinedInsertBody
{
    public int StartYear { get; set; }
    public int StartMonth { get; set; }
    public int EndYear { get; set; }
    public int EndMonth { get; set; }

    /// <summary>Optional: insert only for this app user. Null = all users with activity in the range.</summary>
    public int? AppUserId { get; set; }

    /// <summary>When true, the Python service runs with dry_run and does not commit to GnuCash.</summary>
    public bool DryRun { get; set; }
}
