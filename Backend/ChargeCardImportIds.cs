using System.Globalization;

namespace Backend;

/// <summary>Normalizes RFID / token strings for matching Smappee <c>value</c> and session <c>rfid</c>.</summary>
public static class ChargeCardImportIds
{
    public static string NormalizeTokenKey(string raw)
    {
        var t = raw.Trim().Trim('"');
        var p = t.IndexOf(" (", StringComparison.Ordinal);
        return p <= 0 ? t : t[..p].Trim();
    }
}
