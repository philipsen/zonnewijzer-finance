using Backend.Models;

namespace Backend.Services.Smappee;

internal static class SmappeeChargingSessionMerge
{
    /// <summary>
    /// Merges active and completed Smappee session lists by id. When the same id appears in both,
    /// prefers the row with an end time (<see cref="SmappeeSessionDto.To"/>).
    /// </summary>
    public static List<SmappeeSessionDto> MergeActiveAndCompleted(
        IReadOnlyList<SmappeeSessionDto> active,
        IReadOnlyList<SmappeeSessionDto> completed)
    {
        var byId = new Dictionary<long, SmappeeSessionDto>();
        foreach (var dto in active)
            AddOrPreferWithEnd(byId, dto);

        foreach (var dto in completed)
            AddOrPreferWithEnd(byId, dto);

        return byId.Values.ToList();
    }

    /// <summary>
    /// When a charge card has multiple open sessions, only the latest may still be ongoing.
    /// Closes older open rows at the start of the next session on the same card.
    /// </summary>
    public static int InferCloseFromNextSessionOnSameCard(IReadOnlyList<ChargingSession> openSessions)
    {
        if (openSessions.Count == 0)
            return 0;

        var closed = 0;
        foreach (var group in openSessions.GroupBy(s => s.ChargeCardId))
        {
            var ordered = group.OrderBy(s => s.FromTimestamp).ToList();
            if (ordered.Count <= 1)
                continue;

            for (var i = 0; i < ordered.Count - 1; i++)
            {
                var current = ordered[i];
                var next = ordered[i + 1];
                if (current.ToTimestamp.HasValue)
                    continue;

                current.ToTimestamp = next.FromTimestamp;
                current.Status = "STOPPED";
                if (current.Duration is null or <= 0)
                {
                    var durationSeconds = (int)Math.Max(0, (next.FromTimestamp - current.FromTimestamp) / 1000);
                    current.Duration = durationSeconds;
                }

                closed++;
            }
        }

        return closed;
    }

    private static void AddOrPreferWithEnd(Dictionary<long, SmappeeSessionDto> byId, SmappeeSessionDto dto)
    {
        if (dto.Id <= 0)
            return;

        if (!byId.TryGetValue(dto.Id, out var existing))
        {
            byId[dto.Id] = dto;
            return;
        }

        if (dto.To.HasValue && !existing.To.HasValue)
            byId[dto.Id] = dto;
    }
}
