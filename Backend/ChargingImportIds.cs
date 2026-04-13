using System.Globalization;

namespace Backend;

/// <summary>
/// Maps export CSV token strings to charging user ids (sessions and charging-user profiles share this id).
/// The id is derived from the token text before <c> (</c>: hexadecimal RFID is parsed to an int; otherwise decimal digits or FNV fallback.
/// </summary>
public static class ChargingImportIds
{
    public static string NormalizeTokenKey(string raw)
    {
        var t = raw.Trim().Trim('"');
        var p = t.IndexOf(" (", StringComparison.Ordinal);
        return p <= 0 ? t : t[..p].Trim();
    }

    /// <summary>
    /// Human-readable name from the token column, e.g. <c>047D9322B62290 (Cock van Bruggen)</c> → <c>Cock van Bruggen</c>.
    /// </summary>
    public static string? ParseDisplayNameInParentheses(string rawTokenColumn)
    {
        if (string.IsNullOrWhiteSpace(rawTokenColumn))
            return null;
        var t = rawTokenColumn.Trim().Trim('"');
        var open = t.IndexOf(" (", StringComparison.Ordinal);
        if (open < 0)
            return null;
        var close = t.LastIndexOf(')');
        if (close < open + 2)
            return null;
        var inner = t.AsSpan(open + 2, close - open - 2).Trim();
        return inner.Length == 0 ? null : inner.ToString();
    }

    /// <summary>
    /// Positive int id from the RFID / token key (first segment of the export token column).
    /// Hex keys (typical) use the full hex value modulo <see cref="int.MaxValue"/>; 0 maps to 1.
    /// </summary>
    public static int ChargingUserIdFromTokenKey(string tokenKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenKey);
        var key = tokenKey.Trim();
        if (key.Length > 0 && IsAsciiHexDigits(key))
        {
            if (ulong.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var ul))
            {
                var mod = ul % int.MaxValue;
                var v = (int)mod;
                return v == 0 ? 1 : v;
            }
        }

        if (key.Length > 0 && key.All(char.IsAsciiDigit))
        {
            if (ulong.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec))
            {
                var mod = dec % int.MaxValue;
                var v = (int)mod;
                return v == 0 ? 1 : v;
            }
        }

        return ChargingUserIdFromTokenKeyFnv(tokenKey);
    }

    private static bool IsAsciiHexDigits(string s)
    {
        foreach (var c in s)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }

        return true;
    }

    private static int ChargingUserIdFromTokenKeyFnv(string tokenKey)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (var c in tokenKey)
                h = (h ^ c) * 16777619u;
            var v = (int)(h & 0x7FFFFFFF);
            return v == 0 ? 1 : v;
        }
    }
}
