namespace Backend.Services.IntegrationDaemon;

public interface IIntegrationDaemonClient
{
    Task<SmappeeUpdateTriggerResult> TriggerSmappeeUpdateAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
    Task<SigenergyUpdateTriggerResult> TriggerSigenergyUpdateAsync(CancellationToken cancellationToken);
}

public sealed record SmappeeUpdateTriggerResult(
    bool Success,
    bool Enabled,
    DateTimeOffset? FetchedAtUtc,
    string? Error,
    int CardsAdded = 0,
    int CardsUpdated = 0,
    int SessionsAdded = 0,
    int SessionsUpdated = 0,
    int SessionsSkipped = 0,
    int SessionsErrors = 0);

public sealed record SigenergyUpdateTriggerResult(
    bool Success,
    bool Enabled,
    DateTimeOffset? FetchedAtUtc,
    string? Error);
