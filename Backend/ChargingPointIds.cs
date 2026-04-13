namespace Backend;

/// <summary>
/// A charging point is identified by station serial number + connector (from the export).
/// Maps to a stable int used as <see cref="Models.ChargingSession.StationId"/> for uniqueness and billing.
/// </summary>
public static class ChargingPointIds
{
    /// <summary>
    /// Deterministic positive int from serial + connector (stable across imports).
    /// </summary>
    public static int ChargingPointIdFromSerialAndConnector(string serialRaw, string connectorRaw)
    {
        var serial = serialRaw.Trim().Trim('"');
        var connector = connectorRaw.Trim().Trim('"');
        var key = serial + "\u241E" + connector;
        unchecked
        {
            uint h = 2166136261u;
            foreach (var c in key)
                h = (h ^ c) * 16777619u;
            var v = (int)(h & 0x7FFFFFFF);
            return v == 0 ? 1 : v;
        }
    }
}
