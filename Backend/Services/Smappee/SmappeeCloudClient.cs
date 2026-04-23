using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Backend.Services.Smappee;

public class SmappeeCloudClient : ISmappeeCloudClient
{
    private readonly HttpClient _http;
    private readonly SmappeeOptions _options;
    private readonly ILogger<SmappeeCloudClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SmappeeCloudClient(HttpClient http, Microsoft.Extensions.Options.IOptions<SmappeeOptions> options, ILogger<SmappeeCloudClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
            _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId)
            || string.IsNullOrWhiteSpace(_options.Username)
            || string.IsNullOrWhiteSpace(_options.Password))
        {
            _logger.LogWarning("Smappee OAuth: ClientId, Username, or Password is missing from configuration.");
            throw new InvalidOperationException(
                "Smappee OAuth: set Smappee:ClientId, Username, Password (and ClientSecret) via appsettings or environment variables.");
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
            _logger.LogWarning("Smappee token request failed: {Status} {Body}", response.StatusCode, Truncate(payload));
            throw new InvalidOperationException($"Smappee authentication failed ({(int)response.StatusCode}).");
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
            _logger.LogWarning("Smappee RFIDs request failed: {Status} {Body}", response.StatusCode, Truncate(payload));
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
            _logger.LogWarning("Smappee sessions request failed: {Status} {Body}", response.StatusCode, Truncate(payload));
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

        var api = _options.BaseUrl?.Trim() ?? "";
        if (api.Contains("/v3", StringComparison.OrdinalIgnoreCase))
            return api.Replace("/v3", "/v1", StringComparison.OrdinalIgnoreCase);

        return api;
    }

    private static string Truncate(string s, int max = 500) =>
        s.Length <= max ? s : s[..max] + "…";
}
