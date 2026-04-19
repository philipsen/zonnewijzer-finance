using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Contracts;
using Microsoft.Extensions.Options;

namespace Backend.Services.GnuCash;

public sealed class GnuCashSyncClient : IGnuCashSyncClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly GnuCashSyncOptions _options;
    private readonly ILogger<GnuCashSyncClient> _logger;

    public GnuCashSyncClient(HttpClient httpClient, IOptions<GnuCashSyncOptions> options, ILogger<GnuCashSyncClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GnuCashInsertResponse?> InsertInvoicesAsync(GnuCashInsertRequest request, string? correlationId, bool dryRun, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _logger.LogWarning("GnuCashSync:BaseUrl is not configured; skipping Python call");
            return null;
        }

        var baseUri = _options.BaseUrl.TrimEnd('/');
        var path = dryRun ? "/invoices/insert?dry_run=true" : "/invoices/insert";
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUri}{path}");
        req.Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            req.Headers.TryAddWithoutValidation("X-GnuCash-Sync-Key", _options.ApiKey);
        if (!string.IsNullOrWhiteSpace(correlationId))
            req.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

        using var resp = await _httpClient.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("GnuCash insert HTTP {Status}: {Body}", (int)resp.StatusCode, body);
            throw new InvalidOperationException($"GnuCash service returned {(int)resp.StatusCode}: {body}");
        }

        return JsonSerializer.Deserialize<GnuCashInsertResponse>(body, JsonOptions);
    }

    public async Task<GnuCashEnsureCustomerResponse?> EnsureCustomerAsync(
        GnuCashEnsureCustomerRequest request,
        string? correlationId,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _logger.LogWarning("GnuCashSync:BaseUrl is not configured; skipping Python call");
            return null;
        }

        var baseUri = _options.BaseUrl.TrimEnd('/');
        var path = dryRun ? "/customers/ensure?dry_run=true" : "/customers/ensure";
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUri}{path}");
        req.Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            req.Headers.TryAddWithoutValidation("X-GnuCash-Sync-Key", _options.ApiKey);
        if (!string.IsNullOrWhiteSpace(correlationId))
            req.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

        using var resp = await _httpClient.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("GnuCash ensure customer HTTP {Status}: {Body}", (int)resp.StatusCode, body);
            throw new InvalidOperationException($"GnuCash service returned {(int)resp.StatusCode}: {body}");
        }

        return JsonSerializer.Deserialize<GnuCashEnsureCustomerResponse>(body, JsonOptions);
    }

    public async Task<GnuCashWipeAppResponse?> WipeAppBusinessAsync(string? correlationId, bool dryRun, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _logger.LogWarning("GnuCashSync:BaseUrl is not configured; skipping Python call");
            return null;
        }

        var baseUri = _options.BaseUrl.TrimEnd('/');
        var path = dryRun ? "/gnucash/wipe-app?dry_run=true" : "/gnucash/wipe-app";
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUri}{path}");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            req.Headers.TryAddWithoutValidation("X-GnuCash-Sync-Key", _options.ApiKey);
        if (!string.IsNullOrWhiteSpace(correlationId))
            req.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

        using var resp = await _httpClient.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("GnuCash wipe-app HTTP {Status}: {Body}", (int)resp.StatusCode, body);
            throw new InvalidOperationException($"GnuCash service returned {(int)resp.StatusCode}: {body}");
        }

        return JsonSerializer.Deserialize<GnuCashWipeAppResponse>(body, JsonOptions);
    }
}
