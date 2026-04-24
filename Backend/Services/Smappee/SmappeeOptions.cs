namespace Backend.Services.Smappee;

public class SmappeeOptions
{
    public const string SectionName = "Smappee";

    /// <summary>REST API base (e.g. <c>https://app1pub.smappee.net/dev/v3</c>).</summary>
    public string BaseUrl { get; set; } = "https://app1pub.smappee.net/dev/v3";

    /// <summary>OAuth token base (e.g. <c>https://app1pub.smappee.net/dev/v1</c>). If empty, <see cref="BaseUrl"/> with <c>/v3</c> replaced by <c>/v1</c> is used.</summary>
    public string OAuthBaseUrl { get; set; } = "";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>Service location id for charging park sessions and RFID whitelist.</summary>
    public int ServiceLocationId { get; set; }
    /// <summary>Connector index when Smappee session payload has no connector (serial + this → StationId).</summary>
    public string DefaultConnector { get; set; } = "1";
}
