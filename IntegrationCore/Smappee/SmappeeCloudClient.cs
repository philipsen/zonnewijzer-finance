using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Backend.Services.Smappee;

public class SmappeeCloudClient : ISmappeeCloudClient
{
    private readonly HttpClient _http;
    private readonly SmappeeOptions _options;
    private readonly ILogger<SmappeeCloudClient> _logger;
    private readonly string _normalizedApiBase;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenValidUntil = DateTimeOffset.MinValue;

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
        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(_cachedToken) && now < _tokenValidUntil.AddSeconds(-30))
            return _cachedToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(_cachedToken) && now < _tokenValidUntil.AddSeconds(-30))
                return _cachedToken;

            var missing = _options.GetMissingCredentialFields();
            if (missing.Count > 0)
            {
                _logger.LogError(
                    "Smappee OAuth: required fields are null or empty. Missing: {Missing}. Set Smappee__* or Smappee:… in configuration.",
                    string.Join(", ", missing));
                throw new InvalidOperationException(
                    "Smappee OAuth: " + string.Join(", ", missing) + " not set. Use Smappee__ClientId, Smappee__ClientSecret, Smappee__Username, Smappee__Password (or SMAPPEE_* env names).");
            }

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
            using var tokenReq = new HttpRequestMessage(HttpMethod.Post, tokenUri) { Content = content };
            using var response = await SendAndLogSmappeeAsync(tokenReq, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Smappee token request failed: {Status} {Body}. TokenEndpoint was {TokenEndpoint}.",
                    response.StatusCode,
                    Truncate(payload),
                    tokenUri);
                throw new InvalidOperationException(
                    $"Smappee authentication failed ({(int)response.StatusCode}). " +
                    $"Check Smappee token URL (tried {tokenUri}) and credentials in production env.");
            }

            var token = JsonSerializer.Deserialize<SmappeeTokenResponse>(payload, JsonOptions);
            if (string.IsNullOrEmpty(token?.AccessToken))
                throw new InvalidOperationException("Smappee token response missing access_token.");

            _cachedToken = token.AccessToken;
            var ttlSeconds = token.ExpiresInSeconds.GetValueOrDefault(3600);
            if (ttlSeconds <= 0) ttlSeconds = 3600;
            _tokenValidUntil = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds);
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<IReadOnlyList<SmappeeRfidDto>> GetWhitelistedRfidsAsync(int serviceLocationId, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"servicelocation/{serviceLocationId}/rfids");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAndLogSmappeeAsync(req, cancellationToken);
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
        bool? active = null,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        return await GetChargingSessionsWithTokenAsync(
            token,
            serviceLocationId,
            rangeStartMs,
            rangeEndMs,
            active,
            cancellationToken);
    }

    public async Task<(IReadOnlyList<SmappeeSessionDto> Active, IReadOnlyList<SmappeeSessionDto> Completed)>
        GetChargingSessionsActiveAndCompletedAsync(
            int serviceLocationId,
            long rangeStartMs,
            long? rangeEndMs,
            CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var active = await GetChargingSessionsWithTokenAsync(
            token, serviceLocationId, rangeStartMs, rangeEndMs, true, cancellationToken);
        var completed = await GetChargingSessionsWithTokenAsync(
            token, serviceLocationId, rangeStartMs, rangeEndMs, false, cancellationToken);
        return (active, completed);
    }

    public async Task<(IReadOnlyList<SmappeeSessionDto> Active, IReadOnlyList<SmappeeSessionDto> Completed, IReadOnlyList<string> ChargerSerialsAtPark)>
        GetChargingLiveDataAsync(
            int serviceLocationId,
            long rangeStartMs,
            long? rangeEndMs,
            CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var active = await GetChargingSessionsWithTokenAsync(
            token, serviceLocationId, rangeStartMs, rangeEndMs, true, cancellationToken);
        var completed = await GetChargingSessionsWithTokenAsync(
            token, serviceLocationId, rangeStartMs, rangeEndMs, false, cancellationToken);
        var serials = await GetChargerSerialsRegisteredAtParkWithTokenAsync(token, serviceLocationId, cancellationToken);
        return (active, completed, serials);
    }

    public async Task<(string? AppName, IReadOnlyList<SmappeeServiceLocationRowDto> ServiceLocations)> GetServiceLocationsListAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var req = new HttpRequestMessage(HttpMethod.Get, "servicelocation");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAndLogSmappeeAsync(req, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Smappee servicelocation list failed: {Status} {Body}",
                response.StatusCode,
                Truncate(payload));
            throw new InvalidOperationException(
                $"Smappee service locations request failed ({(int)response.StatusCode}).");
        }

        var (appName, rows) = ParseServiceLocationsListPayload(payload);
        return (appName, rows);
    }

    public Task<SmappeeServiceLocationInfoDto> GetServiceLocationInfoAsync(
        int serviceLocationId,
        CancellationToken cancellationToken = default) =>
        GetServiceLocationInfoCoreAsync(null, serviceLocationId, cancellationToken);

    public Task<SmappeeServiceLocationInfoDto> GetServiceLocationInfoAsync(
        string accessToken,
        int serviceLocationId,
        CancellationToken cancellationToken = default) =>
        GetServiceLocationInfoCoreAsync(accessToken, serviceLocationId, cancellationToken);

    private async Task<SmappeeServiceLocationInfoDto> GetServiceLocationInfoCoreAsync(
        string? accessTokenOrNull,
        int serviceLocationId,
        CancellationToken cancellationToken)
    {
        var token = accessTokenOrNull ?? await GetAccessTokenAsync(cancellationToken);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"servicelocation/{serviceLocationId}/info");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAndLogSmappeeAsync(req, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Smappee servicelocation info failed for {ServiceLocationId}: {Status} {Body}",
                serviceLocationId,
                response.StatusCode,
                Truncate(payload));
            throw new InvalidOperationException(
                $"Smappee service location info request failed for {serviceLocationId} ({(int)response.StatusCode}).");
        }

        var dto = JsonSerializer.Deserialize<SmappeeServiceLocationInfoDto>(payload, JsonOptions);
        if (dto == null)
            throw new InvalidOperationException($"Smappee service location info for {serviceLocationId} had an empty body.");

        return dto;
    }

    private async Task<IReadOnlyList<string>> GetChargerSerialsRegisteredAtParkWithTokenAsync(
        string token,
        int serviceLocationId,
        CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "servicelocation");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAndLogSmappeeAsync(req, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Smappee servicelocation list failed: {Status} {Body}",
                response.StatusCode,
                Truncate(payload));
            return Array.Empty<string>();
        }

        try
        {
            var (_, rows) = ParseServiceLocationsListPayload(payload);
            if (rows.Count == 0)
                return Array.Empty<string>();

            return SmappeeServiceLocationChargerSerials.SelectForChargingPark(rows, serviceLocationId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Smappee servicelocation list JSON");
            return Array.Empty<string>();
        }
    }

    private static string BuildChargingSessionsQuery(long rangeStartMs, long? rangeEndMs, bool? active)
    {
        var q = new StringBuilder();
        q.Append("?range=");
        q.Append(rangeStartMs);
        if (rangeEndMs.HasValue)
        {
            q.Append(',');
            q.Append(rangeEndMs.Value);
        }

        if (active.HasValue)
            q.Append(active.Value ? "&active=true" : "&active=false");

        return q.ToString();
    }

    private async Task<IReadOnlyList<SmappeeSessionDto>> GetChargingSessionsWithTokenAsync(
        string token,
        int serviceLocationId,
        long rangeStartMs,
        long? rangeEndMs,
        bool? active,
        CancellationToken cancellationToken)
    {
        var query = BuildChargingSessionsQuery(rangeStartMs, rangeEndMs, active);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"chargingparks/{serviceLocationId}/sessions{query}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAndLogSmappeeAsync(req, cancellationToken);
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

    private List<SmappeeSessionDto> ParseSessionArray(JsonElement array)
    {
        var result = new List<SmappeeSessionDto>();
        foreach (var el in array.EnumerateArray())
        {
            try
            {
                var dto = JsonSerializer.Deserialize<SmappeeSessionDto>(el.GetRawText(), JsonOptions);
                if (dto != null)
                    result.Add(dto);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping a Smappee session row: invalid JSON for session DTO");
            }
        }

        return result;
    }

    private string ResolveOAuthTokenBase()
    {
        if (!string.IsNullOrWhiteSpace(_options.OAuthBaseUrl))
            return _options.OAuthBaseUrl.Trim();

        return SmappeeUrl.ApiV3ToOAuthV1Base(_normalizedApiBase);
    }

    private (string? AppName, List<SmappeeServiceLocationRowDto> Rows) ParseServiceLocationsListPayload(string payload)
    {
        try
        {
            var text = string.IsNullOrWhiteSpace(payload) ? "{}" : payload;
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                var rows = JsonSerializer.Deserialize<List<SmappeeServiceLocationRowDto>>(root.GetRawText(), JsonOptions)
                    ?? new List<SmappeeServiceLocationRowDto>();
                return (null, rows);
            }

            var dto = JsonSerializer.Deserialize<SmappeeServiceLocationsListResponse>(root.GetRawText(), JsonOptions);
            return (dto?.AppName, dto?.ServiceLocations ?? new List<SmappeeServiceLocationRowDto>());
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Smappee servicelocation list JSON parse failed");
            throw new InvalidOperationException("Smappee service locations response was not valid JSON.");
        }
    }

    private async Task<HttpResponseMessage> SendAndLogSmappeeAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var target = SmappeeRequestUrlForLog(request);
        var sw = Stopwatch.StartNew();
        var response = await _http.SendAsync(request, cancellationToken);
        sw.Stop();
        _logger.LogInformation(
            "Smappee {Method} {Url} -> {Status} in {Ms}ms",
            request.Method,
            target,
            (int)response.StatusCode,
            sw.ElapsedMilliseconds);
        return response;
    }

    private string SmappeeRequestUrlForLog(HttpRequestMessage request)
    {
        if (request.RequestUri == null)
            return "";
        if (request.RequestUri.IsAbsoluteUri)
            return request.RequestUri.ToString();
        if (_http.BaseAddress != null)
            return new Uri(_http.BaseAddress, request.RequestUri).ToString();
        return request.RequestUri.ToString();
    }

    private static string Truncate(string s, int max = 500) =>
        s.Length <= max ? s : s[..max] + "…";
}
