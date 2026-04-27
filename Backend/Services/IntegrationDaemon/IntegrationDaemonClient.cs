using System.Net.Http.Json;
using Backend.Models;
using Microsoft.Extensions.Options;

namespace Backend.Services.IntegrationDaemon;

public sealed class IntegrationDaemonClient : IIntegrationDaemonClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<IntegrationDaemonOptions> _options;
    private readonly ILogger<IntegrationDaemonClient> _logger;

    public IntegrationDaemonClient(
        HttpClient httpClient,
        IOptionsMonitor<IntegrationDaemonOptions> options,
        ILogger<IntegrationDaemonClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public Task<SmappeeUpdateTriggerResult> TriggerSmappeeUpdateAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
        => TriggerAsync("/api/smappee/update", new SmappeeUpdateRequest { From = from, To = to }, cancellationToken);

    public Task<SmappeeUpdateTriggerResult> TriggerSmappeeServiceLocationsRefreshAsync(CancellationToken cancellationToken)
        => TriggerAsync("/api/smappee/update-service-locations", null, cancellationToken);

    public Task<SigenergyUpdateTriggerResult> TriggerSigenergyUpdateAsync(CancellationToken cancellationToken)
        => TriggerSigenergyAsync(cancellationToken);

    public async Task<SmappeeServiceLocationsResult> GetSmappeeServiceLocationsAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            return new SmappeeServiceLocationsResult(
                Success: false,
                Enabled: false,
                Error: "Integration daemon trigger is disabled in backend configuration.",
                ServiceLocations: Array.Empty<SmappeeServiceLocationListItemResponse>(),
                FetchedAtUtc: null);
        }

        try
        {
            var endpoint = BuildEndpoint(options.BaseUrl, "/api/smappee/service-locations");
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
                request.Headers.Add("x-api-key", options.ApiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Integration daemon service-locations fetch failed with status {StatusCode}: {Body}. Endpoint={Endpoint}, apiKeyConfigured={ApiKeyConfigured}, apiKeyLength={ApiKeyLength}",
                    (int)response.StatusCode,
                    body,
                    endpoint,
                    !string.IsNullOrWhiteSpace(options.ApiKey),
                    options.ApiKey?.Length ?? 0);
                return new SmappeeServiceLocationsResult(
                    Success: false,
                    Enabled: true,
                    Error: $"Daemon service-locations failed with HTTP {(int)response.StatusCode}.",
                    ServiceLocations: Array.Empty<SmappeeServiceLocationListItemResponse>(),
                    FetchedAtUtc: null);
            }

            var payload = await response.Content.ReadFromJsonAsync<DaemonServiceLocationsResponse>(cancellationToken: cancellationToken);
            return new SmappeeServiceLocationsResult(
                Success: true,
                Enabled: true,
                Error: null,
                ServiceLocations: payload?.ServiceLocations ?? new List<SmappeeServiceLocationListItemResponse>(),
                FetchedAtUtc: payload?.FetchedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Smappee service locations via integration daemon");
            return new SmappeeServiceLocationsResult(
                Success: false,
                Enabled: true,
                Error: "Could not reach integration daemon service-locations endpoint.",
                ServiceLocations: Array.Empty<SmappeeServiceLocationListItemResponse>(),
                FetchedAtUtc: null);
        }
    }

    private async Task<SmappeeUpdateTriggerResult> TriggerAsync(
        string path,
        SmappeeUpdateRequest? requestBody,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            return new SmappeeUpdateTriggerResult(
                Success: false,
                Enabled: false,
                FetchedAtUtc: null,
                Error: "Integration daemon trigger is disabled in backend configuration.");
        }

        try
        {
            var endpoint = BuildEndpoint(options.BaseUrl, path);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = requestBody == null ? null : JsonContent.Create(requestBody)
            };
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
                request.Headers.Add("x-api-key", options.ApiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Integration daemon update failed with status {StatusCode}: {Body}. Endpoint={Endpoint}, apiKeyConfigured={ApiKeyConfigured}, apiKeyLength={ApiKeyLength}",
                    (int)response.StatusCode,
                    body,
                    endpoint,
                    !string.IsNullOrWhiteSpace(options.ApiKey),
                    options.ApiKey?.Length ?? 0);

                return new SmappeeUpdateTriggerResult(
                    Success: false,
                    Enabled: true,
                    FetchedAtUtc: null,
                    Error: $"Daemon update failed with HTTP {(int)response.StatusCode}.");
            }

            var responsePayload = await response.Content.ReadFromJsonAsync<DaemonUpdateResponse>(cancellationToken: cancellationToken);
            return new SmappeeUpdateTriggerResult(
                Success: responsePayload?.Ok ?? true,
                Enabled: true,
                FetchedAtUtc: responsePayload?.FetchedAtUtc,
                Error: responsePayload?.Error,
                CardsAdded: responsePayload?.ChargeCards?.Added ?? 0,
                CardsUpdated: responsePayload?.ChargeCards?.Updated ?? 0,
                SessionsAdded: responsePayload?.Sessions?.Added ?? 0,
                SessionsUpdated: responsePayload?.Sessions?.Updated ?? 0,
                SessionsSkipped: responsePayload?.Sessions?.Skipped ?? 0,
                SessionsErrors: responsePayload?.Sessions?.Errors ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger Smappee update via integration daemon");
            return new SmappeeUpdateTriggerResult(
                Success: false,
                Enabled: true,
                FetchedAtUtc: null,
                Error: "Could not reach integration daemon update endpoint.");
        }
    }

    private async Task<SigenergyUpdateTriggerResult> TriggerSigenergyAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            return new SigenergyUpdateTriggerResult(
                Success: false,
                Enabled: false,
                FetchedAtUtc: null,
                Error: "Integration daemon trigger is disabled in backend configuration.");
        }

        try
        {
            var endpoint = BuildEndpoint(options.BaseUrl, "/api/sigenergy/update");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
                request.Headers.Add("x-api-key", options.ApiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Integration daemon Sigenergy update failed with status {StatusCode}: {Body}. Endpoint={Endpoint}, apiKeyConfigured={ApiKeyConfigured}, apiKeyLength={ApiKeyLength}",
                    (int)response.StatusCode,
                    body,
                    endpoint,
                    !string.IsNullOrWhiteSpace(options.ApiKey),
                    options.ApiKey?.Length ?? 0);

                return new SigenergyUpdateTriggerResult(
                    Success: false,
                    Enabled: true,
                    FetchedAtUtc: null,
                    Error: $"Daemon Sigenergy update failed with HTTP {(int)response.StatusCode}.");
            }

            var payload = await response.Content.ReadFromJsonAsync<DaemonSigenergyUpdateResponse>(cancellationToken: cancellationToken);
            return new SigenergyUpdateTriggerResult(
                Success: payload?.Ok ?? true,
                Enabled: true,
                FetchedAtUtc: payload?.FetchedAtUtc,
                Error: payload?.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger Sigenergy update via integration daemon");
            return new SigenergyUpdateTriggerResult(
                Success: false,
                Enabled: true,
                FetchedAtUtc: null,
                Error: "Could not reach integration daemon Sigenergy update endpoint.");
        }
    }

    private static string BuildEndpoint(string? baseUrl, string path)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl)
            ? "http://localhost:7071"
            : baseUrl.Trim().TrimEnd('/');
        return $"{root}{path}";
    }

    private sealed class DaemonUpdateResponse
    {
        public bool Ok { get; set; }
        public DateTimeOffset? FetchedAtUtc { get; set; }
        public string? Error { get; set; }
        public DaemonSyncCounters? ChargeCards { get; set; }
        public DaemonSyncCounters? Sessions { get; set; }
    }

    private sealed class DaemonSyncCounters
    {
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
    }

    private sealed class SmappeeUpdateRequest
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
    }

    private sealed class DaemonServiceLocationsResponse
    {
        public List<SmappeeServiceLocationListItemResponse>? ServiceLocations { get; set; }
        public DateTimeOffset? FetchedAt { get; set; }
    }

    private sealed class DaemonSigenergyUpdateResponse
    {
        public bool Ok { get; set; }
        public DateTimeOffset? FetchedAtUtc { get; set; }
        public string? Error { get; set; }
    }
}
