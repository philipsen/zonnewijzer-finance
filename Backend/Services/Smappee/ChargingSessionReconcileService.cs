using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Smappee;

public interface IChargingSessionReconcileService
{
    Task<ChargingSessionReconcileResult> ReconcileOngoingAsync(
        string eventLogUserName,
        CancellationToken cancellationToken);
}

public sealed class ChargingSessionReconcileResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public DateOnly SyncFrom { get; init; }
    public DateOnly SyncTo { get; init; }
    public IReadOnlyList<OpenSessionSummary> OpenBefore { get; init; } = Array.Empty<OpenSessionSummary>();
    public IReadOnlyList<OpenSessionSummary> OpenAfter { get; init; } = Array.Empty<OpenSessionSummary>();
    public int CardsAdded { get; init; }
    public int CardsUpdated { get; init; }
    public int SessionsAdded { get; init; }
    public int SessionsUpdated { get; init; }
    public int SessionsInferredClosed { get; init; }
    public int SessionsSkipped { get; init; }
    public int SessionsErrors { get; init; }
}

public sealed class OpenSessionSummary
{
    public int Id { get; init; }
    public long? SmappeeSessionId { get; init; }
    public int ChargeCardId { get; init; }
    public string? ChargeCardLabel { get; init; }
    public DateTime StartUtc { get; init; }
}

public sealed class ChargingSessionReconcileService : IChargingSessionReconcileService
{
    private const int DefaultLookbackDays = 90;
    private const int MaxLookbackDays = 365;

    private readonly ApplicationDbContext _context;
    private readonly ISmappeeChargingSyncService _sync;

    public ChargingSessionReconcileService(
        ApplicationDbContext context,
        ISmappeeChargingSyncService sync)
    {
        _context = context;
        _sync = sync;
    }

    public async Task<ChargingSessionReconcileResult> ReconcileOngoingAsync(
        string eventLogUserName,
        CancellationToken cancellationToken)
    {
        var openBefore = await LoadOpenSessionSummariesAsync(cancellationToken);
        var (from, to) = ComputeSyncRange(openBefore);

        var sync = await _sync.SyncSmappeeCalendarAsync(from, to, eventLogUserName, cancellationToken);
        if (!sync.Success)
        {
            return new ChargingSessionReconcileResult
            {
                Success = false,
                ErrorMessage = sync.ErrorMessage,
                SyncFrom = from,
                SyncTo = to,
                OpenBefore = openBefore
            };
        }

        var openAfter = await LoadOpenSessionSummariesAsync(cancellationToken);
        return new ChargingSessionReconcileResult
        {
            Success = true,
            SyncFrom = from,
            SyncTo = to,
            OpenBefore = openBefore,
            OpenAfter = openAfter,
            CardsAdded = sync.CardsAdded,
            CardsUpdated = sync.CardsUpdated,
            SessionsAdded = sync.SessionsAdded,
            SessionsUpdated = sync.SessionsUpdated,
            SessionsInferredClosed = sync.SessionsInferredClosed,
            SessionsSkipped = sync.SessionsSkipped,
            SessionsErrors = sync.SessionsErrors
        };
    }

    internal static (DateOnly From, DateOnly To) ComputeSyncRange(IReadOnlyList<OpenSessionSummary> openSessions)
    {
        var brusselsTz = ResolveBrusselsTimeZone();
        var todayBrussels = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, brusselsTz));
        var from = todayBrussels.AddDays(-DefaultLookbackDays);
        var oldestAllowed = todayBrussels.AddDays(-MaxLookbackDays);

        if (openSessions.Count > 0)
        {
            var oldestOpen = openSessions
                .Select(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(s.StartUtc, brusselsTz)))
                .Min();
            if (oldestOpen < from)
                from = oldestOpen;
        }

        if (from < oldestAllowed)
            from = oldestAllowed;

        return (from, todayBrussels);
    }

    private async Task<IReadOnlyList<OpenSessionSummary>> LoadOpenSessionSummariesAsync(CancellationToken cancellationToken)
    {
        return await (
            from s in _context.ChargingSessions.AsNoTracking()
            join c in _context.ChargeCards.AsNoTracking() on s.ChargeCardId equals c.Id into cards
            from c in cards.DefaultIfEmpty()
            where !s.ToTimestamp.HasValue
            orderby s.FromTimestamp
            select new OpenSessionSummary
            {
                Id = s.Id,
                SmappeeSessionId = s.SmappeeSessionId,
                ChargeCardId = s.ChargeCardId,
                ChargeCardLabel = c != null ? (c.Comment ?? c.Value) : null,
                StartUtc = DateTimeOffset.FromUnixTimeMilliseconds(s.FromTimestamp).UtcDateTime
            }).ToListAsync(cancellationToken);
    }

    private static TimeZoneInfo ResolveBrusselsTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }
    }
}
