namespace Backend.Services.Smappee;

/// <summary>
/// Derives EV charger serials from GET <c>servicelocation</c>. The configured <c>serviceLocationId</c> (charging park)
/// may appear once as a site / gateway row that is not an EVSE; real chargers are other rows sharing <c>serviceLocationUuid</c>.
/// If Smappee returns several rows with the same id (e.g. EV Dual), every <c>deviceSerialNumber</c> on those rows is a charger.
/// </summary>
public static class SmappeeServiceLocationChargerSerials
{
    public static IReadOnlyList<string> SelectForChargingPark(
        IReadOnlyList<SmappeeServiceLocationRowDto> rows,
        int configuredServiceLocationId)
    {
        if (rows.Count == 0)
            return Array.Empty<string>();

        var sameConfiguredId = rows.Where(r => r.ServiceLocationId == configuredServiceLocationId).ToList();
        if (sameConfiguredId.Count > 1)
            return DistinctSerials(sameConfiguredId);

        if (sameConfiguredId.Count == 1)
        {
            var anchor = sameConfiguredId[0];
            var uuid = anchor.ServiceLocationUuid?.Trim();
            if (string.IsNullOrEmpty(uuid))
                return Array.Empty<string>();

            var siblings = rows
                .Where(r =>
                    string.Equals(r.ServiceLocationUuid?.Trim(), uuid, StringComparison.OrdinalIgnoreCase)
                    && r.ServiceLocationId != configuredServiceLocationId)
                .ToList();
            return DistinctSerials(siblings);
        }

        return Array.Empty<string>();
    }

    private static List<string> DistinctSerials(IEnumerable<SmappeeServiceLocationRowDto> source)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var r in source)
        {
            var s = r.DeviceSerialNumber?.Trim();
            if (string.IsNullOrEmpty(s) || !set.Add(s))
                continue;
            list.Add(s);
        }

        return list;
    }
}
