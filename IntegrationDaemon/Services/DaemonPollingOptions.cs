namespace IntegrationDaemon.Services;

public class SmappeeDaemonOptions
{
    public const string SectionName = "SmappeeDaemon";
    public bool Enabled { get; set; } = true;
    public int PollIntervalMinutes { get; set; } = 5;
    public int LookbackHours { get; set; } = 24;
}

public class SigenergyDaemonOptions
{
    public const string SectionName = "SigenergyDaemon";
    public bool Enabled { get; set; } = true;
    public int PollIntervalMinutes { get; set; } = 5;
}
