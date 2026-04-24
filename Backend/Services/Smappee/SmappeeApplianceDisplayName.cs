namespace Backend.Services.Smappee;

/// <summary>Smappee often appends <c> - 1</c> for the first connector; we hide that in UI when it is redundant.</summary>
public static class SmappeeApplianceDisplayName
{
    public static string? NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;
        var t = name.TrimEnd();
        if (t.Length >= 4 && t.EndsWith(" - 1", StringComparison.Ordinal))
        {
            var cut = t[..^4].TrimEnd();
            return string.IsNullOrEmpty(cut) ? null : cut;
        }

        return t;
    }
}
