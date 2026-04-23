namespace Backend.Services.Smappee;

public class SmappeeBackgroundSyncOptions
{
    public const string SectionName = "Smappee:BackgroundSync";

    /// <summary>When false, the hourly sync worker does nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Delay between sync runs (wall clock).</summary>
    public int IntervalHours { get; set; } = 1;

    /// <summary>Calendar days to sync back from today (Europe/Brussels), inclusive of a window matching the manual UI default (~90).</summary>
    public int LookbackDays { get; set; } = 90;

    /// <summary>Wait this long after startup before the first run (avoids competing with migrations/seed).</summary>
    public int InitialDelaySeconds { get; set; } = 15;
}
