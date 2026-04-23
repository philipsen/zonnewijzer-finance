namespace Backend.Contracts;

public sealed class GnuCashInsertBody
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int? ChargeCardId { get; set; }

    /// <summary>When true, the Python service runs with dry_run and does not commit to GnuCash.</summary>
    public bool DryRun { get; set; }
}
