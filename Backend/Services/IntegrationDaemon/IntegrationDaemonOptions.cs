namespace Backend.Services.IntegrationDaemon;

public sealed class IntegrationDaemonOptions
{
    public const string SectionName = "IntegrationDaemon";

    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://localhost:7071";
    public string? ApiKey { get; set; }
}
