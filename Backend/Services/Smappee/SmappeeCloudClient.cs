using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Backend.Services.Smappee;

public class SmappeeCloudClient : ISmappeeCloudClient
{
    private readonly HttpClient _http;
    private readonly SmappeeOptions _options;
    private readonly ILogger<SmappeeCloudClient> _logger;
    private readonly string _normalizedApiBase;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SmappeeCloudClient(HttpClient http, Microsoft.Extensions.Options.IOptions<SmappeeOptions> options, ILogger<SmappeeCloudClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _normalizedApiBase = SmappeeUrl.NormalizeApiBase(_options.BaseUrl);
        _http.BaseAddress = new Uri(_normalizedApiBase.TrimEnd('/') + "/");
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var missing = _options.GetMissingCredentialFields();
        if (missing.Count > 0)
        {
            _logger.LogError(
                "Smappee OAuth: required fields are null or empty. Missing: {Missing}. Set Smappee__* or Smappee:… in configuration.",
                string.Join(", ", missing));
            throw new InvalidOperationException(
                "Smappee OAuth: " + string.Join(", ", missing) + " not set. Use Smappee__ClientId, Smappee__ClientSecret, Smappee__Username, Smappee__Password (or SMAPPEE_* env names).");
        }

        // Token endpoint is on API v1; data calls use v3 (see Smappee docs / REST clients).
        var tokenUri = new Uri($"{ResolveOAuthTokenBase().TrimEnd('/')}/oauth2/token");
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret ?? "",
            ["username"] = _options.Username,
            ["password"] = _options.Password
        };
        using var content = new FormUrlEncodedContent(body);
        using var response = await _http.PostAsync(tokenUri, content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Smappee token request failed: {Status} {Body}. " +
                "TokenEndpoint was {TokenEndpoint} (Smappee:OAuthBaseUrl empty: {WithoutOAuthOverride}; " +
                "normalized API base has /v3: {BaseUrlLikeV3}). " +
                "Config snapshot: client_id length {ClientIdLen}, client_secret {SecretPresent}, username length {UsernameLen}. " +
                "Smappee:BaseUrl (raw) was {BaseUrlRaw}; normalized API base is {ApiBaseNorm}.",
                response.StatusCode,
                Truncate(payload),
                tokenUri,
                string.IsNullOrWhiteSpace(_options.OAuthBaseUrl),
                _normalizedApiBase.Contains("/v3", StringComparison.OrdinalIgnoreCase),
                _options.ClientId?.Length ?? 0,
                string.IsNullOrEmpty(_options.ClientSecret) ? "empty" : "set",
                _options.Username?.Length ?? 0,
                _options.BaseUrl ?? "(null)",
                _normalizedApiBase);
            throw new InvalidOperationException(
                $"Smappee authentication failed ({(int)response.StatusCode}). " +
                $"Check Smappee token URL (tried {tokenUri}) and credentials in production env.");
        }

        var token = JsonSerializer.Deserialize<SmappeeTokenResponse>(payload, JsonOptions);
        if (string.IsNullOrEmpty(token?.AccessToken))
            throw new InvalidOperationException("Smappee token response missing access_token.");

        return token.AccessToken;
    }

    public async Task<IReadOnlyList<SmappeeRfidDto>> GetWhitelistedRfidsAsync(int serviceLocationId, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"servicelocation/{serviceLocationId}/rfids");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(req, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Smappee RFIDs request failed: {Status} {Body}", response.StatusCode, Truncate(payload));
            throw new InvalidOperationException($"Smappee RFIDs request failed ({(int)response.StatusCode}).");
        }

        var list = JsonSerializer.Deserialize<List<SmappeeRfidDto>>(payload, JsonOptions);
        return list ?? new List<SmappeeRfidDto>();
    }

    public async Task<IReadOnlyList<SmappeeSessionDto>> GetChargingSessionsAsync(
        int serviceLocationId,
        long rangeStartMs,
        long? rangeEndMs,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var rangeQuery = rangeEndMs.HasValue
            ? $"?range={rangeStartMs},{rangeEndMs.Value}"
            : $"?range={rangeStartMs}";
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"chargingparks/{serviceLocationId}/sessions{rangeQuery}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(req, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Smappee sessions request failed: {Status} {Body}", response.StatusCode, Truncate(payload));
            throw new InvalidOperationException($"Smappee sessions request failed ({(int)response.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "[]" : payload);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
            return ParseSessionArray(doc.RootElement);

        if (doc.RootElement.TryGetProperty("sessions", out var sessionsEl) && sessionsEl.ValueKind == JsonValueKind.Array)
            return ParseSessionArray(sessionsEl);

        _logger.LogWarning("Unexpected Smappee sessions JSON shape");
        return Array.Empty<SmappeeSessionDto>();
    }

    private static List<SmappeeSessionDto> ParseSessionArray(JsonElement array)
    {
        var result = new List<SmappeeSessionDto>();
        foreach (var el in array.EnumerateArray())
        {
            var dto = JsonSerializer.Deserialize<SmappeeSessionDto>(el.GetRawText(), JsonOptions);
            if (dto != null)
                result.Add(dto);
        }

        return result;
    }

    private string ResolveOAuthTokenBase()
    {
        if (!string.IsNullOrWhiteSpace(_options.OAuthBaseUrl))
            return _options.OAuthBaseUrl.Trim();

        return SmappeeUrl.ApiV3ToOAuthV1Base(_normalizedApiBase);
    }

    private static string Truncate(string s, int max = 500) =>
        s.Length <= max ? s : s[..max] + "…";
}
