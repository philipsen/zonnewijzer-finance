using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Services.Sigenergy;
using Microsoft.Extensions.Options;

namespace IntegrationDaemon.Services;

public sealed class SigenergyDiagnosticsService
{
    private const string UserAgent = "vscode-restclient";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<SigenergyOptions> _options;

    public SigenergyDiagnosticsService(IHttpClientFactory httpClientFactory, IOptions<SigenergyOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<SigenergyDiagnosticsReport> RunAsync(CancellationToken cancellationToken)
    {
        var report = new SigenergyDiagnosticsReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            BaseUrl = _options.Value.BaseUrl,
            SystemId = _options.Value.SystemId
        };

        var missing = _options.Value.GetMissingSettings();
        if (missing.Count > 0)
        {
            report.Ok = false;
            report.Error = $"Sigenergy settings incomplete: {string.Join(", ", missing)}";
            return report;
        }

        var client = _httpClientFactory.CreateClient(SigenergyCloudClient.HttpClientName);

        StepResult loginStep;
        try
        {
            var loginBody = JsonSerializer.Serialize(new
            {
                username = _options.Value.Username,
                password = _options.Value.Password
            });

            using var loginReq = new HttpRequestMessage(HttpMethod.Post, "/openapi/auth/login/password")
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
                Content = new StringContent(loginBody, Encoding.UTF8, "application/json")
            };
            loginReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            loginReq.Headers.UserAgent.ParseAdd(UserAgent);

            loginStep = await ExecuteStepAsync(client, loginReq, "login", includeBodyPreview: true, cancellationToken);
        }
        catch (Exception ex)
        {
            report.Ok = false;
            report.Error = $"Login request failed before response: {ex.Message}";
            return report;
        }

        report.Steps.Add(loginStep);
        var token = TryExtractToken(loginStep.Response.BodyPreview);
        if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(loginStep.Response.BodyPreview))
            loginStep.Response.BodyPreview = RedactToken(loginStep.Response.BodyPreview, token);
        if (!loginStep.Response.IsSuccess || string.IsNullOrWhiteSpace(token))
        {
            report.Ok = false;
            report.Error = loginStep.Response.IsSuccess
                ? "Login succeeded but no bearer token found in response."
                : $"Login failed with HTTP {loginStep.Response.StatusCode}.";
            return report;
        }

        StepResult energyStep;
        try
        {
            var id = WebUtility.UrlEncode(_options.Value.SystemId.Trim());
            using var energyReq = new HttpRequestMessage(HttpMethod.Get, $"/openapi/systems/{id}/energyFlow")
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
            energyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            energyReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            energyReq.Headers.UserAgent.ParseAdd(UserAgent);

            energyStep = await ExecuteStepAsync(client, energyReq, "energyFlow", includeBodyPreview: true, cancellationToken);
        }
        catch (Exception ex)
        {
            report.Ok = false;
            report.Error = $"EnergyFlow request failed before response: {ex.Message}";
            return report;
        }

        report.Steps.Add(energyStep);
        report.Ok = energyStep.Response.IsSuccess;
        if (!report.Ok)
            report.Error = $"EnergyFlow failed with HTTP {energyStep.Response.StatusCode}.";
        return report;
    }

    private static async Task<StepResult> ExecuteStepAsync(
        HttpClient client,
        HttpRequestMessage request,
        string operation,
        bool includeBodyPreview,
        CancellationToken cancellationToken)
    {
        var resolvedUrl = ResolveUrl(client, request);
        var requestHeaders = RedactHeaders(request.Headers, request.Content?.Headers);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new StepResult
        {
            Operation = operation,
            Request = new RequestInfo
            {
                Method = request.Method.Method,
                Url = resolvedUrl,
                Headers = requestHeaders
            },
            Response = new ResponseInfo
            {
                StatusCode = (int)response.StatusCode,
                IsSuccess = response.IsSuccessStatusCode,
                Headers = FlattenHeaders(response.Headers, response.Content.Headers),
                BodyLength = body.Length,
                BodySha256 = ComputeSha256(body),
                BodyPreview = includeBodyPreview ? Truncate(body, 1000) : null
            }
        };
    }

    private static string ResolveUrl(HttpClient client, HttpRequestMessage request)
    {
        if (request.RequestUri is null)
            return "(null)";
        if (request.RequestUri.IsAbsoluteUri)
            return request.RequestUri.ToString();
        if (client.BaseAddress is not null)
            return new Uri(client.BaseAddress, request.RequestUri).ToString();
        return request.RequestUri.ToString();
    }

    private static Dictionary<string, string> RedactHeaders(
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>>? contentHeaders)
    {
        var all = FlattenHeaders(headers, contentHeaders);
        foreach (var key in all.Keys.ToList())
        {
            if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                all[key] = "<redacted>";
        }

        return all;
    }

    private static Dictionary<string, string> FlattenHeaders(
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>>? contentHeaders)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in headers)
            map[h.Key] = string.Join(", ", h.Value);

        if (contentHeaders is not null)
        {
            foreach (var h in contentHeaders)
                map[h.Key] = string.Join(", ", h.Value);
        }

        return map;
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static string Truncate(string text, int maxChars)
    {
        if (text.Length <= maxChars)
            return text;
        return text[..maxChars] + "... <truncated>";
    }

    private static string RedactToken(string payload, string token)
        => payload.Replace(token, "<redacted-token>", StringComparison.Ordinal);

    private static string? TryExtractToken(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            var root = JsonNode.Parse(payload);
            return FindBearerToken(root);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindBearerToken(JsonNode? node, int depth = 0)
    {
        if (node is null || depth > 8 || node is not JsonObject o)
            return null;

        foreach (var key in new[] { "accessToken", "authToken", "access_token", "id_token", "token" })
        {
            if (o[key] is JsonValue v && v.TryGetValue<string>(out var s) && IsLikelyBearer(s))
                return s;
        }

        if (o["data"] is JsonValue dataValue && dataValue.TryGetValue<string>(out var dataString) && !string.IsNullOrWhiteSpace(dataString))
        {
            var trimmed = dataString.Trim();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                try
                {
                    var inner = JsonNode.Parse(dataString);
                    var token = FindBearerToken(inner, depth + 1);
                    if (token is not null)
                        return token;
                }
                catch (JsonException)
                {
                    // ignored
                }
            }
        }

        foreach (var kv in o)
        {
            var token = FindBearerToken(kv.Value, depth + 1);
            if (token is not null)
                return token;
        }

        return null;
    }

    private static bool IsLikelyBearer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 20)
            return false;
        if (value.Count(c => c == '.') == 2 && value.StartsWith("eyJ", StringComparison.Ordinal))
            return true;
        return value.Length >= 32 && !value.Contains('"');
    }
}

public sealed class SigenergyDiagnosticsReport
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string? BaseUrl { get; set; }
    public string? SystemId { get; set; }
    public List<StepResult> Steps { get; } = new();
}

public sealed class StepResult
{
    public string Operation { get; set; } = "";
    public RequestInfo Request { get; set; } = new();
    public ResponseInfo Response { get; set; } = new();
}

public sealed class RequestInfo
{
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ResponseInfo
{
    public int StatusCode { get; set; }
    public bool IsSuccess { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int BodyLength { get; set; }
    public string BodySha256 { get; set; } = "";
    public string? BodyPreview { get; set; }
}
