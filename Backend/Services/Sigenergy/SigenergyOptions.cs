namespace Backend.Services.Sigenergy;

public class SigenergyOptions
{
    public const string SectionName = "Sigenergy";

    /// <summary>REST API base (e.g. <c>https://api-eu.sigencloud.com</c>).</summary>
    public string BaseUrl { get; set; } = "https://api-eu.sigencloud.com";

    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>Plant / system id for <c>GET /openapi/systems/{id}/energyFlow</c>.</summary>
    public string SystemId { get; set; } = "";

    /// <summary>Optional usable battery capacity in kWh for deriving remaining battery energy from SOC.</summary>
    public double? UsableBatteryCapacityKwh { get; set; }

    public bool IsConfigured => GetMissingSettings().Count == 0;

    public IReadOnlyList<string> GetMissingSettings()
    {
        var missing = new List<string>(4);
        if (string.IsNullOrWhiteSpace(BaseUrl)) missing.Add(nameof(BaseUrl));
        if (string.IsNullOrWhiteSpace(Username)) missing.Add(nameof(Username));
        if (string.IsNullOrWhiteSpace(Password)) missing.Add(nameof(Password));
        if (string.IsNullOrWhiteSpace(SystemId)) missing.Add(nameof(SystemId));
        return missing;
    }
}
