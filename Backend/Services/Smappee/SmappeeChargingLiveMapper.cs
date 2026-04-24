using System.Globalization;
using Backend.Models;

namespace Backend.Services.Smappee;

/// <summary>Maps two Smappee session lists (active and completed) into per–charging-point DTOs.</summary>
public static class SmappeeChargingLiveMapper
{
    public const int LookbackDays = 7;
    public const int RecentlyFinishedHours = 2;

    public static SmappeeChargingLiveResponse Build(
        int serviceLocationId,
        IReadOnlyList<SmappeeSessionDto> active,
        IReadOnlyList<SmappeeSessionDto> completed,
        long rangeStartMs,
        long rangeEndMsExclusive,
        DateTimeOffset fetchedAt,
        DateTimeOffset nowUtc,
        IReadOnlyDictionary<string, string>? stationDisplayNamesBySerial = null,
        IReadOnlyList<string>? knownSerialNumbersFromHistory = null,
        IReadOnlyList<string>? expectedChargerSerials = null)
    {
        var recentCutoffMs = new DateTimeOffset(nowUtc.UtcDateTime.AddHours(-RecentlyFinishedHours)).ToUnixTimeMilliseconds();

        var activesByKey = new Dictionary<string, SmappeeSessionDto>(StringComparer.Ordinal);
        foreach (var s in active)
        {
            var k = PointKey(s);
            if (!activesByKey.TryGetValue(k, out var existing) || PreferActiveSession(s, existing))
                activesByKey[k] = s;
        }

        var bestCompletedByKey = new Dictionary<string, SmappeeSessionDto>(StringComparer.Ordinal);
        foreach (var s in completed)
        {
            var k = PointKey(s);
            if (!bestCompletedByKey.TryGetValue(k, out var cur))
            {
                bestCompletedByKey[k] = s;
                continue;
            }

            if (IsMoreRecentCompleted(s, cur))
                bestCompletedByKey[k] = s;
        }

        var allKeys = new HashSet<string>(activesByKey.Keys, StringComparer.Ordinal);
        allKeys.UnionWith(bestCompletedByKey.Keys);

        var points = new List<ChargingPointLiveDto>();
        foreach (var k in allKeys)
        {
            activesByKey.TryGetValue(k, out var live);
            bestCompletedByKey.TryGetValue(k, out var lastDone);
            var dto = ToPoint(live, lastDone, recentCutoffMs, stationDisplayNamesBySerial);
            if (dto != null)
                points.Add(dto);
        }

        AddEnsuredAvailableSerials(
            points,
            stationDisplayNamesBySerial,
            knownSerialNumbersFromHistory,
            expectedChargerSerials);

        points.Sort(ComparePoints);

        return new SmappeeChargingLiveResponse
        {
            FetchedAt = fetchedAt,
            ServiceLocationId = serviceLocationId,
            Points = points,
            RangeStartMs = rangeStartMs,
            RangeEndMsExclusive = rangeEndMsExclusive
        };
    }

    private static int ComparePoints(ChargingPointLiveDto a, ChargingPointLiveDto b)
    {
        var oa = Order(a.UiState);
        var ob = Order(b.UiState);
        if (oa != ob) return oa.CompareTo(ob);
        var nameA = a.StationName ?? "";
        var nameB = b.StationName ?? "";
        var nc = string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
        if (nc != 0) return nc;
        return string.Compare(a.SerialNumber, b.SerialNumber, StringComparison.OrdinalIgnoreCase);
    }

    private static int Order(string ui) => ui switch
    {
        "charging" or "preparing" or "suspended" => 0,
        "finished_recent" => 1,
        "available" => 2,
        _ => 3
    };

    /// <summary>One card per charger serial (single-connector sites). Orphans without serial use session id.</summary>
    internal static string PointKey(SmappeeSessionDto s)
    {
        var ser = s.SerialNumberNormalized();
        if (string.IsNullOrEmpty(ser))
            return "\u241E\u241E" + s.Id.ToString(CultureInfo.InvariantCulture);
        return ser;
    }

    private static bool PreferActiveSession(SmappeeSessionDto incoming, SmappeeSessionDto existing)
    {
        var rank = ActiveStatusRank(incoming.Status) - ActiveStatusRank(existing.Status);
        if (rank != 0)
            return rank > 0;
        var inFrom = SmappeeSessionTimestamps.ToStoredMilliseconds(incoming.From);
        var exFrom = SmappeeSessionTimestamps.ToStoredMilliseconds(existing.From);
        if (inFrom != exFrom)
            return inFrom > exFrom;
        return incoming.Id > existing.Id;
    }

    private static int ActiveStatusRank(string? st)
    {
        if (string.IsNullOrEmpty(st))
            return 1;
        if (string.Equals(st, "CHARGING", StringComparison.OrdinalIgnoreCase))
            return 6;
        if (string.Equals(st, "SUSPENDED", StringComparison.OrdinalIgnoreCase))
            return 5;
        if (string.Equals(st, "STARTED", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (string.Equals(st, "STOPPING", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (string.Equals(st, "INITIAL", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 0;
    }

    private static bool IsMoreRecentCompleted(SmappeeSessionDto a, SmappeeSessionDto b)
    {
        var aTo = SmappeeSessionTimestamps.ToStoredMilliseconds(a.To);
        var bTo = SmappeeSessionTimestamps.ToStoredMilliseconds(b.To);
        if (aTo != bTo)
            return aTo > bTo;
        return a.Id > b.Id;
    }

    public static string? MaskRfid(string? rfid)
    {
        if (string.IsNullOrWhiteSpace(rfid))
            return null;
        var t = rfid.Trim();
        if (t.Length <= 4)
            return "****";
        return "…" + t[^4..];
    }

    /// <summary>
    /// Ensures serials from DB history, name-map keys, and <paramref name="expectedChargerSerials"/> appear as Available when missing from live session data.
    /// </summary>
    private static void AddEnsuredAvailableSerials(
        List<ChargingPointLiveDto> points,
        IReadOnlyDictionary<string, string>? stationDisplayNamesBySerial,
        IReadOnlyList<string>? knownSerialNumbersFromHistory,
        IReadOnlyList<string>? expectedChargerSerials)
    {
        var serialsToEnsure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void addMany(IEnumerable<string>? seq)
        {
            if (seq == null) return;
            foreach (var raw in seq)
            {
                var sn = (raw ?? "").Trim();
                if (!string.IsNullOrEmpty(sn))
                    serialsToEnsure.Add(sn);
            }
        }

        addMany(knownSerialNumbersFromHistory);
        addMany(expectedChargerSerials);
        if (stationDisplayNamesBySerial != null)
        {
            foreach (var kv in stationDisplayNamesBySerial)
            {
                var sn = (kv.Key ?? "").Trim();
                if (!string.IsNullOrEmpty(sn))
                    serialsToEnsure.Add(sn);
            }
        }

        if (serialsToEnsure.Count == 0)
            return;

        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in points)
            have.Add(p.SerialNumber.Trim());

        foreach (var sn in serialsToEnsure)
        {
            if (have.Contains(sn))
                continue;
            have.Add(sn);
            points.Add(new ChargingPointLiveDto
            {
                SerialNumber = sn,
                StationName = LookupStationName(sn, stationDisplayNamesBySerial),
                UiState = "available"
            });
        }
    }

    private static string? LookupStationName(string serial, IReadOnlyDictionary<string, string>? map)
    {
        if (map == null || map.Count == 0)
            return null;
        if (map.TryGetValue(serial, out var exact) && !string.IsNullOrWhiteSpace(exact))
            return exact.Trim();
        foreach (var kv in map)
        {
            if (serial.Equals(kv.Key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value))
                return kv.Value.Trim();
        }

        return null;
    }

    private static ChargingPointLiveDto? ToPoint(
        SmappeeSessionDto? live,
        SmappeeSessionDto? lastCompleted,
        long recentCutoffMs,
        IReadOnlyDictionary<string, string>? stationDisplayNamesBySerial)
    {
        var s = live ?? lastCompleted;
        if (s == null)
            return null;

        var serial = s.SerialNumberNormalized();
        if (string.IsNullOrEmpty(serial))
            return null;

        var fromMs = SmappeeSessionTimestamps.ToStoredMilliseconds(s.From);
        var toMs = SmappeeSessionTimestamps.ToStoredMilliseconds(s.To);
        var name = LookupStationName(serial, stationDisplayNamesBySerial);

        if (live != null)
        {
            var ui = MapActiveUiState(live.Status);
            return new ChargingPointLiveDto
            {
                SerialNumber = serial,
                StationName = name,
                UiState = ui,
                SmappeeStatus = live.Status,
                EnergyKwh = live.Energy,
                StartedAtUtc = UtcFromMs(fromMs),
                EndedAtUtc = toMs.HasValue ? UtcFromMs(toMs.Value) : null,
                RfidMasked = MaskRfid(live.Rfid)
            };
        }

        if (lastCompleted == null)
            return null;

        var toMs2 = SmappeeSessionTimestamps.ToStoredMilliseconds(lastCompleted.To);
        var finishedRecent = string.Equals(lastCompleted.Status, "STOPPED", StringComparison.OrdinalIgnoreCase)
            && toMs2.HasValue
            && toMs2.Value >= recentCutoffMs;

        if (finishedRecent)
        {
            return new ChargingPointLiveDto
            {
                SerialNumber = serial,
                StationName = name,
                UiState = "finished_recent",
                SmappeeStatus = lastCompleted.Status,
                EnergyKwh = lastCompleted.Energy,
                StartedAtUtc = SmappeeSessionTimestamps.ToStoredMilliseconds(lastCompleted.From) is { } f ? UtcFromMs(f) : null,
                EndedAtUtc = toMs2.HasValue ? UtcFromMs(toMs2.Value) : null,
                RfidMasked = MaskRfid(lastCompleted.Rfid)
            };
        }

        // Idle charger: do not surface energy / timestamps / RFID from an old session.
        return new ChargingPointLiveDto
        {
            SerialNumber = serial,
            StationName = name,
            UiState = "available"
        };
    }

    private static string MapActiveUiState(string? status)
    {
        if (string.IsNullOrEmpty(status))
            return "preparing";
        if (string.Equals(status, "CHARGING", StringComparison.OrdinalIgnoreCase))
            return "charging";
        if (string.Equals(status, "SUSPENDED", StringComparison.OrdinalIgnoreCase))
            return "suspended";
        if (string.Equals(status, "STARTED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "INITIAL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "STOPPING", StringComparison.OrdinalIgnoreCase))
            return "preparing";
        return "preparing";
    }

    private static DateTime? UtcFromMs(long ms)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
