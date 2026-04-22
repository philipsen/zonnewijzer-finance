namespace Backend.Services.GnuCash;

public sealed class GnuCashSyncOptions
{
    public const string SectionName = "GnuCashSync";

    /// <summary>Base URL of the Python gnucash-sync service (e.g. http://127.0.0.1:8787).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Shared secret; sent as X-GnuCash-Sync-Key.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
