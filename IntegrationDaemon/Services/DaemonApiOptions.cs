namespace IntegrationDaemon.Services;

public class DaemonApiOptions
{
    public const string SectionName = "DaemonApi";
    public bool Enabled { get; set; } = true;
    public string? ApiKey { get; set; }
}
