namespace Backend.Services.Smappee;

/// <summary>Normalizes Smappee public API bases. Env often has only the host (e.g. SMAPPEE_BASE_URL without /dev/v3), which
/// would otherwise produce a wrong token URL: <c>https://app1pub.smappee.net/oauth2/token</c> instead of
/// <c>…/dev/v1/oauth2/token</c>.</summary>
public static class SmappeeUrl
{
    public const string DefaultApiBaseV3 = "https://app1pub.smappee.net/dev/v3";

    /// <summary>Ensures API calls use a <c>/dev/v3</c> path when the host is Smappee public cloud and no <c>/dev/</c> segment is present.</summary>
    public static string NormalizeApiBase(string? baseUrl)
    {
        var b = (baseUrl ?? "").Trim();
        if (string.IsNullOrEmpty(b))
            return DefaultApiBaseV3;
        if (b.Contains("/dev/", StringComparison.OrdinalIgnoreCase))
            return b.TrimEnd('/');
        if ((b.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || b.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            && b.Contains("smappee.net", StringComparison.OrdinalIgnoreCase))
            return b.TrimEnd('/') + "/dev/v3";
        return b.TrimEnd('/');
    }

    /// <summary>OAuth token host path is v1; derive from a normalized v3 API base (see Smappee docs / <see cref="SmappeeCloudClient"/>).</summary>
    public static string ApiV3ToOAuthV1Base(string apiBaseNormalized)
    {
        var a = (apiBaseNormalized ?? "").Trim();
        if (a.Contains("/v3", StringComparison.OrdinalIgnoreCase))
            return a.Replace("/v3", "/v1", StringComparison.OrdinalIgnoreCase);
        if (a.Contains("/v1", StringComparison.OrdinalIgnoreCase))
            return a;
        if (a.Contains("smappee.net", StringComparison.OrdinalIgnoreCase) && !a.Contains("/dev/", StringComparison.OrdinalIgnoreCase))
            return a.TrimEnd('/') + "/dev/v1";
        return a.TrimEnd('/') + "/dev/v1";
    }
}
