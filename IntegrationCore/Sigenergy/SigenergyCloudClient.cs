using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq;

namespace Backend.Services.Sigenergy;

public class SigenergyCloudClient : ISigenergyCloudClient
{
    public const string HttpClientName = "Sigenergy";
    private const string VsCodeRestClientUserAgent = "vscode-restclient";
    private static readonly TimeSpan SigenCallCacheTtl = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly SigenergyOptions _options;
    private readonly ILogger<SigenergyCloudClient> _logger;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private readonly SemaphoreSlim _energyFlowLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenValidUntil = DateTimeOffset.MinValue;
    private SigenergyEnergyFlowData? _cachedEnergyFlow;
    private DateTimeOffset _energyFlowCachedAt = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public SigenergyCloudClient(
        IHttpClientFactory httpClientFactory,
        IOptions<SigenergyOptions> options,
        ILogger<SigenergyCloudClient> logger)
    {
        _http = httpClientFactory.CreateClient(HttpClientName);
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SigenergyEnergyFlowData> GetEnergyFlowAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("Sigenergy is not configured.");

        var now = DateTimeOffset.UtcNow;
        var cached = _cachedEnergyFlow;
        if (cached is not null && now - _energyFlowCachedAt < SigenCallCacheTtl)
            return cached;

        await _energyFlowLock.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            cached = _cachedEnergyFlow;
            if (cached is not null && now - _energyFlowCachedAt < SigenCallCacheTtl)
                return cached;

            var token = await GetValidAccessTokenAsync(cancellationToken);
            var id = WebUtility.UrlEncode(_options.SystemId.Trim());
            var path = $"/openapi/systems/{id}/energyFlow";
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Version = HttpVersion.Version20;
            req.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.UserAgent.ParseAdd(VsCodeRestClientUserAgent);

            var (response, payload) = await SendAndLogAsync(req, "energyFlow", cancellationToken);
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"Sigenergy energyFlow request failed ({(int)response.StatusCode}): {ExtractErrorSummary(payload)}");

                var parsed = ParseSigenEnergyEnvelopeOrThrow(payload);
                _cachedEnergyFlow = parsed;
                _energyFlowCachedAt = DateTimeOffset.UtcNow;
                return parsed;
            }
        }
        catch (Exception ex) when (IsRpcFail(ex.Message))
        {
            _logger.LogWarning(
                ex,
                "Sigenergy energyFlow returned 'rpc fail'; continuing without cooldown hold-back.");
            throw;
        }
        finally
        {
            _energyFlowLock.Release();
        }
    }

    private async Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrEmpty(_cachedToken) && now < _tokenValidUntil.AddSeconds(-30))
            return _cachedToken;

        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (!string.IsNullOrEmpty(_cachedToken) && now < _tokenValidUntil.AddSeconds(-30))
                return _cachedToken;

            var (token, until) = await LoginAsync(cancellationToken);
            _cachedToken = token;
            _tokenValidUntil = until;
            return token;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private async Task<(string Token, DateTimeOffset ValidUntil)> LoginAsync(CancellationToken cancellationToken)
    {
        var bodyObj = new { username = _options.Username, password = _options.Password };
        var body = JsonSerializer.Serialize(bodyObj, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/openapi/auth/login/password");
        req.Version = HttpVersion.Version20;
        req.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.UserAgent.ParseAdd(VsCodeRestClientUserAgent);

        var (response, payload) = await SendAndLogAsync(req, "login", cancellationToken);
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Sigenergy login failed ({(int)response.StatusCode}).");
            return ParseLoginResponseForTokenOrThrow(payload);
        }
    }

    private async Task<(HttpResponseMessage Response, string Payload)> SendAndLogAsync(
        HttpRequestMessage req,
        string operation,
        CancellationToken cancellationToken)
    {
        var requestHeaders = DumpHeaders(req.Headers);
        var requestContentHeaders = req.Content is null ? "" : DumpHeaders(req.Content.Headers);
        var requestBody = req.Content is null ? "" : await req.Content.ReadAsStringAsync(cancellationToken);

        var sw = Stopwatch.StartNew();
        var requestUrl = ResolveRequestUrl(req);
        var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        sw.Stop();

        var isRpcFail = HasRpcFailMessage(payload);
        var isErrorResponse = !response.IsSuccessStatusCode;

        if (isRpcFail || isErrorResponse)
        {
            var responseHeaders = DumpHeaders(response.Headers);
            var responseContentHeaders = DumpHeaders(response.Content.Headers);
            _logger.LogWarning(
                "Sigenergy {Reason} ({Operation}) {Method} {Url} -> {Status} in {Ms}ms\nRequestHeaders:\n{RequestHeaders}\nRequestContentHeaders:\n{RequestContentHeaders}\nRequestBody:\n{RequestBody}\nResponseHeaders:\n{ResponseHeaders}\nResponseContentHeaders:\n{ResponseContentHeaders}\nResponseBody:\n{ResponseBody}",
                isRpcFail ? "rpc-fail response" : "error response",
                operation,
                req.Method,
                requestUrl,
                (int)response.StatusCode,
                sw.ElapsedMilliseconds,
                requestHeaders,
                string.IsNullOrWhiteSpace(requestContentHeaders) ? "(none)" : requestContentHeaders,
                string.IsNullOrWhiteSpace(requestBody) ? "(empty)" : requestBody,
                responseHeaders,
                string.IsNullOrWhiteSpace(responseContentHeaders) ? "(none)" : responseContentHeaders,
                string.IsNullOrWhiteSpace(payload) ? "(empty)" : payload);
        }
        else
        {
            _logger.LogInformation(
                "Sigenergy inbound response ({Operation}) {Method} {Url} -> {Status} in {Ms}ms",
                operation,
                req.Method,
                requestUrl,
                (int)response.StatusCode,
                sw.ElapsedMilliseconds);
        }

        return (response, payload);
    }

    private string ResolveRequestUrl(HttpRequestMessage req)
    {
        if (req.RequestUri is null)
            return "(null)";
        if (req.RequestUri.IsAbsoluteUri)
            return req.RequestUri.ToString();
        if (_http.BaseAddress is not null)
            return new Uri(_http.BaseAddress, req.RequestUri).ToString();
        return req.RequestUri.ToString();
    }

    private static string DumpHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var lines = headers
            .Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")
            .ToArray();
        return lines.Length == 0 ? "(none)" : string.Join('\n', lines);
    }

    private static bool IsRpcFail(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        message.Contains("rpc fail", StringComparison.OrdinalIgnoreCase);

    private static bool HasRpcFailMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("msg", out var msgElement) &&
                msgElement.ValueKind == JsonValueKind.String)
            {
                var msg = msgElement.GetString();
                return string.Equals(msg, "rpc fail", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (JsonException)
        {
            // Ignore parse issues and fall back to non-verbose logging.
        }

        return false;
    }

    private static string ExtractErrorSummary(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return "(empty response body)";
        var normalized = payload.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 180 ? normalized : normalized[..180] + "...";
    }

    public static (string Token, DateTimeOffset ValidUntil) ParseLoginResponseForTokenOrThrow(string payload)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Sigenergy login response is not valid JSON.", ex);
        }

        if (root?["code"] is JsonValue codeVal && codeVal.TryGetValue<int>(out var code) && code != 0)
        {
            var msg = root["msg"]?.ToString() ?? "error";
            throw new InvalidOperationException($"Sigenergy login reported code {code}: {msg}");
        }

        var token = FindBearerToken(root) ?? throw new InvalidOperationException("Sigenergy login response did not contain a bearer token.");
        var validUntil = FindExpiryFromLogin(root) ?? GetJwtExpiry(token) ?? DateTimeOffset.UtcNow.AddMinutes(30);
        if (validUntil <= DateTimeOffset.UtcNow)
            validUntil = DateTimeOffset.UtcNow.AddMinutes(5);

        return (token, validUntil);
    }

    private static string? FindBearerToken(JsonNode? node, int depth = 0)
    {
        if (node is null || depth > 8 || node is not JsonObject o2)
            return null;

        foreach (var key in new[] { "accessToken", "authToken", "access_token", "id_token", "token" })
        {
            if (o2[key] is JsonValue v && v.TryGetValue<string>(out var s) && IsLikelyBearer(s))
                return s;
        }

        if (o2["data"] is JsonValue jv && jv.TryGetValue<string>(out var dataString) && !string.IsNullOrWhiteSpace(dataString))
        {
            var trimmed = dataString.Trim();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                try
                {
                    var inner = JsonNode.Parse(dataString);
                    var t = FindBearerToken(inner, depth + 1);
                    if (t != null) return t;
                }
                catch (JsonException) { }
            }
        }

        foreach (var kv in o2)
        {
            var t = FindBearerToken(kv.Value, depth + 1);
            if (t != null) return t;
        }

        return null;
    }

    private static bool IsLikelyBearer(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length < 20)
            return false;
        if (s.Count(c => c == '.') == 2 && s.StartsWith("eyJ", StringComparison.Ordinal))
            return true;
        if (s.Length >= 32 && !s.Contains('"'))
            return true;
        return false;
    }

    private static DateTimeOffset? FindExpiryFromLogin(JsonNode? root, int depth = 0)
    {
        if (root is null || depth > 6)
            return null;

        if (root is JsonObject o)
        {
            foreach (var key in new[] { "expiresIn", "expires_in", "validTime", "valid_time" })
            {
                if (o[key] is JsonValue v)
                {
                    if (v.TryGetValue<int>(out var sec) && sec > 0)
                        return DateTimeOffset.UtcNow.AddSeconds(Math.Min(sec, 86400 * 30));
                    if (v.TryGetValue<double>(out var dsec) && dsec > 0)
                        return DateTimeOffset.UtcNow.AddSeconds(Math.Min((int)dsec, 86400 * 30));
                }
            }

            if (o["data"] is JsonValue jv && jv.TryGetValue<string>(out var ds) &&
                (ds.TrimStart().StartsWith('{') || ds.TrimStart().StartsWith('[')))
            {
                try
                {
                    var inner = JsonNode.Parse(ds);
                    var n = FindExpiryFromLogin(inner, depth + 1);
                    if (n.HasValue) return n;
                }
                catch (JsonException) { }
            }

            if (o["expireTime"] is JsonValue ex && ex.TryGetValue<long>(out var unix) && unix > 1_000_000_000L)
            {
                if (unix > 10_000_000_000L)
                    return DateTimeOffset.FromUnixTimeMilliseconds(unix);
                return DateTimeOffset.FromUnixTimeSeconds(unix);
            }

            foreach (var p in o)
            {
                var n = FindExpiryFromLogin(p.Value, depth + 1);
                if (n.HasValue) return n;
            }
        }

        return null;
    }

    private static DateTimeOffset? GetJwtExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return null;
            var payload = parts[1];
            var json = Base64UrlDecodeToString(payload);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var e))
                return DateTimeOffset.FromUnixTimeSeconds(e);
        }
        catch
        {
            return null;
        }
        return null;
    }

    private static string Base64UrlDecodeTo(string base64url)
    {
        var s = base64url.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return s;
    }

    private static string Base64UrlDecodeToString(string base64url) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(Base64UrlDecodeTo(base64url)));

    public static SigenergyEnergyFlowData ParseSigenEnergyEnvelopeOrThrow(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : "error";
            throw new InvalidOperationException($"Sigenergy API error code: {code.GetInt32()} {msg}");
        }

        if (!root.TryGetProperty("data", out var dataEl))
            throw new InvalidOperationException("Sigenergy response missing data.");

        string? dataString;
        if (dataEl.ValueKind == JsonValueKind.String)
            dataString = dataEl.GetString();
        else
            dataString = dataEl.GetRawText();

        if (string.IsNullOrWhiteSpace(dataString))
            throw new InvalidOperationException("Sigenergy response data is empty.");

        if (dataString!.TrimStart().StartsWith('"'))
        {
            var unquoted = JsonSerializer.Deserialize<string>(dataString, JsonOptions);
            dataString = unquoted ?? dataString;
        }

        var data = JsonSerializer.Deserialize<SigenergyEnergyFlowData>(dataString!, JsonOptions);
        if (data is null)
            throw new InvalidOperationException("Could not parse Sigenergy energy data.");
        return data;
    }
}
