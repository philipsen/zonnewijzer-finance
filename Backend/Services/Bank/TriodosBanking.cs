using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Backend.Services.Bank;

public sealed record TriodosLoginView(string SessionId, string Status, string? QrImage, string? Message);

public interface ITriodosBanking
{
    Task<TriodosLoginView> StartAsync(CancellationToken cancellationToken);
    Task<TriodosLoginView?> PollAsync(string sessionId, CancellationToken cancellationToken);
    Task<string> DownloadCsvAsync(string sessionId, CancellationToken cancellationToken);
    void Cancel(string sessionId);
}

public sealed class TriodosException : Exception
{
    public int StatusCode { get; }

    public TriodosException(string message, int statusCode = 502) : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Talks to Triodos Internet Banking the way the website does: QR login with the
/// Triodos app, then a CSV transaction export. Cookies stay on the login session
/// and are dropped when the import finishes or the QR expires.
/// </summary>
public sealed class TriodosSessionService : ITriodosBanking
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan QrRefreshAfter = TimeSpan.FromSeconds(45);
    private readonly ConcurrentDictionary<string, TriodosLogin> _sessions = new();
    private readonly ILogger<TriodosSessionService> _logger;

    public TriodosSessionService(ILogger<TriodosSessionService> logger)
    {
        _logger = logger;
    }

    public async Task<TriodosLoginView> StartAsync(CancellationToken cancellationToken)
    {
        Sweep();
        var login = new TriodosLogin(TriodosBankingClient.CreateClient());
        try
        {
            var (uuid, qr) = await TriodosBankingClient.StartLoginAsync(login.Http, cancellationToken);
            login.Uuid = uuid;
            login.Qr = qr;
            login.QrAt = DateTimeOffset.UtcNow;
            _sessions[login.Id] = login;
            _logger.LogInformation("Started Triodos app login {SessionId}", login.Id);
            return View(login);
        }
        catch
        {
            login.Dispose();
            throw;
        }
    }

    public async Task<TriodosLoginView?> PollAsync(string sessionId, CancellationToken cancellationToken)
    {
        Sweep();
        if (!_sessions.TryGetValue(sessionId, out var login))
            return null;

        await login.Gate.WaitAsync(cancellationToken);
        try
        {
            if (Expired(login))
            {
                login.Status = "expired";
                login.Message = "The Triodos QR expired. Start again.";
                return View(login);
            }

            if (login.Status is "ready" or "expired" or "cancelled" or "error")
                return View(login);

            var raw = await TriodosBankingClient.LoginStatusAsync(login.Http, login.Uuid!, cancellationToken);
            login.Status = TriodosBankingClient.MapLoginStatus(raw);
            login.Message = login.Status switch
            {
                "scanned" => "Confirm the login in the Triodos app.",
                "expired" => "The Triodos QR expired. Start again.",
                "cancelled" => "The login was cancelled in the Triodos app.",
                "error" => "Triodos did not accept the login. Start again.",
                _ => null
            };

            if (login.Status == "waiting" && DateTimeOffset.UtcNow - login.QrAt > QrRefreshAfter)
            {
                login.Qr = await TriodosBankingClient.QrPngAsync(login.Http, login.Uuid!, cancellationToken);
                login.QrAt = DateTimeOffset.UtcNow;
            }

            return View(login);
        }
        catch (TriodosException ex)
        {
            login.Status = "error";
            login.Message = ex.Message;
            return View(login);
        }
        finally
        {
            login.Gate.Release();
        }
    }

    public async Task<string> DownloadCsvAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var login))
            throw new TriodosException("The Triodos login expired. Start again.", StatusCodes.Status404NotFound);

        await login.Gate.WaitAsync(cancellationToken);
        try
        {
            if (login.Status != "ready")
                throw new TriodosException("Confirm the login in the Triodos app first.", StatusCodes.Status409Conflict);

            var until = DateOnly.FromDateTime(DateTime.UtcNow);
            var from = new DateOnly(until.Year, until.Month, 1).AddMonths(-2);
            var csv = await TriodosBankingClient.DownloadCsvAsync(login.Http, from, until, cancellationToken);
            _logger.LogInformation("Downloaded Triodos transactions for session {SessionId}", login.Id);
            return csv;
        }
        finally
        {
            login.Gate.Release();
            _sessions.TryRemove(sessionId, out _);
            login.Dispose();
        }
    }

    public void Cancel(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var login))
            login.Dispose();
    }

    private void Sweep()
    {
        foreach (var pair in _sessions)
        {
            if (!Expired(pair.Value) || !pair.Value.Gate.Wait(0))
                continue;
            try
            {
                if (_sessions.TryRemove(pair.Key, out var login))
                    login.Dispose();
            }
            finally
            {
                if (_sessions.ContainsKey(pair.Key))
                    pair.Value.Gate.Release();
            }
        }
    }

    private static bool Expired(TriodosLogin login) =>
        DateTimeOffset.UtcNow - login.Created > SessionLifetime;

    private static TriodosLoginView View(TriodosLogin login) =>
        new(login.Id, login.Status, login.Status == "waiting" ? Convert.ToBase64String(login.Qr) : null, login.Message);

    private sealed class TriodosLogin : IDisposable
    {
        public TriodosLogin(HttpClient http) => Http = http;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public HttpClient Http { get; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public string? Uuid { get; set; }
        public string Status { get; set; } = "waiting";
        public string? Message { get; set; }
        public byte[] Qr { get; set; } = [];
        public DateTimeOffset QrAt { get; set; }
        public DateTimeOffset Created { get; } = DateTimeOffset.UtcNow;
        public void Dispose()
        {
            Http.Dispose();
            Gate.Dispose();
        }
    }
}

internal static class TriodosBankingClient
{
    public static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.All
        };
        var http = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("https://bankieren.triodos.nl/"),
            Timeout = TimeSpan.FromSeconds(40)
        };
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Triodos-Administration", "TBNL");
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Triodos-Accept-Language", "nl-NL");
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Triodos-Channel", "BANKING_WEB");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://bankieren.triodos.nl");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://bankieren.triodos.nl/ib-seam/angularIB/");
        return http;
    }

    public static async Task<(string Uuid, byte[] Qr)> StartLoginAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var init = await SendAsync(http, HttpMethod.Get, "banking/v4/login/init", null, "application/json", cancellationToken);
        if (!init.IsSuccessStatusCode)
            throw new TriodosException("Triodos login could not be started.");

        using var started = await SendAsync(http, HttpMethod.Post, "banking/v4/cross-channel-login/start", "", "application/json", cancellationToken);
        var startBody = await started.Content.ReadAsStringAsync(cancellationToken);
        if (!started.IsSuccessStatusCode)
            throw new TriodosException("Triodos login could not be started.");

        var uuid = ReadString(startBody, "crossChannelLoginUUID");
        if (string.IsNullOrWhiteSpace(uuid))
            throw new TriodosException("Triodos login could not be started.");

        var qr = await QrPngAsync(http, uuid, cancellationToken);
        return (uuid, qr);
    }

    public static async Task<string> LoginStatusAsync(HttpClient http, string uuid, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { crossChannelLoginUUID = uuid });
        using var response = await SendAsync(http, HttpMethod.Post, "banking/v4/cross-channel-login/status", body, "application/json", cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new TriodosException("Triodos login could not be checked.");
        return ReadString(text, "status") ?? "";
    }

    public static async Task<byte[]> QrPngAsync(HttpClient http, string uuid, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { crossChannelLoginUUID = uuid });
        using var response = await SendAsync(http, HttpMethod.Post, "banking/v4/cross-channel-login/generate-qr", body, "image/png", cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode || bytes.Length < 8 || bytes[0] != 0x89)
            throw new TriodosException("Triodos did not return a login QR.");
        return bytes;
    }

    public static string MapLoginStatus(string? raw) => raw switch
    {
        "REQUESTED" => "waiting",
        "SCANNED" => "scanned",
        "AUTHORISED" => "ready",
        "EXPIRED" => "expired",
        "CANCELLED" => "cancelled",
        _ => "error"
    };

    public static async Task<string> DownloadCsvAsync(HttpClient http, DateOnly from, DateOnly until, CancellationToken cancellationToken)
    {
        var accounts = await GetJsonAsync(http, "banking/v4/accounts/list", cancellationToken);
        var uuids = ProductUuids(accounts);
        if (uuids.Count == 0)
            throw new TriodosException("Triodos returned no accounts to export.");

        var formats = await GetJsonAsync(http, "banking/v4/transaction-download/formats", cancellationToken);
        var format = CsvFormatKey(formats);
        if (format == null)
            throw new TriodosException("Triodos did not offer a CSV export.");

        var existing = DownloadRequests(await GetJsonAsync(http, "banking/v4/transaction-download/requests", cancellationToken))
            .Select(row => row.Id)
            .ToHashSet(StringComparer.Ordinal);
        var request = JsonSerializer.Serialize(new
        {
            downloadFormat = format,
            productUuids = uuids,
            fromDate = from.ToString("yyyy-MM-dd"),
            untilDate = until.ToString("yyyy-MM-dd")
        });
        using var created = await SendAsync(http, HttpMethod.Post, "banking/v4/transaction-download/requests", request, "application/json", cancellationToken);
        var createdBody = await created.Content.ReadAsStringAsync(cancellationToken);
        if (!created.IsSuccessStatusCode)
            throw new TriodosException("Triodos could not create the transaction export.");

        var createdId = string.IsNullOrWhiteSpace(createdBody) ? null : ReadString(createdBody, "id");
        var deadline = DateTime.UtcNow.AddSeconds(60);
        string? readyId = null;
        while (readyId == null)
        {
            var rows = DownloadRequests(await GetJsonAsync(http, "banking/v4/transaction-download/requests", cancellationToken));
            var ours = rows.Where(row => row.Id == createdId || !existing.Contains(row.Id)).ToList();
            if (ours.Any(row => row.State is "PROBLEM" or "ERROR"))
                throw new TriodosException("Triodos could not create the transaction export.");
            readyId = ours.FirstOrDefault(row => row.State == "PROCESSED").Id;
            if (string.IsNullOrEmpty(readyId))
                readyId = null;
            if (DateTime.UtcNow >= deadline)
                throw new TriodosException("The Triodos export was not ready in time. Try again.");
            await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
        }

        foreach (var uuid in uuids)
        {
            var bytes = await GetFileAsync(http, $"banking/v4/transaction-download/{Uri.EscapeDataString(uuid)}/requests/{Uri.EscapeDataString(readyId)}", cancellationToken);
            if (bytes == null)
                continue;
            var csv = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes).TrimStart('\uFEFF');
            if (!string.IsNullOrWhiteSpace(csv))
                return csv;
        }

        throw new TriodosException("Triodos did not return the CSV file.");
    }

    internal static IReadOnlyList<string> ProductUuids(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var ids = new List<string>();
        foreach (var obj in Walk(doc.RootElement))
        {
            var uuid = FirstString(obj, "productUuid", "productUUID");
            if (!string.IsNullOrWhiteSpace(uuid) && !ids.Contains(uuid, StringComparer.Ordinal))
                ids.Add(uuid);
        }
        return ids;
    }

    internal static string? CsvFormatKey(string json)
    {
        using var doc = JsonDocument.Parse(json);
        string? fallback = null;
        foreach (var obj in Walk(doc.RootElement))
        {
            var key = FirstString(obj, "key", "code", "format");
            if (string.IsNullOrWhiteSpace(key))
                continue;
            var label = FirstString(obj, "viewValue", "name", "description") ?? "";
            var haystack = key + " " + label;
            if (haystack.Contains("CAMT", StringComparison.OrdinalIgnoreCase) ||
                haystack.Contains("MT940", StringComparison.OrdinalIgnoreCase) ||
                haystack.Contains("QIF", StringComparison.OrdinalIgnoreCase) ||
                haystack.Contains("XLS", StringComparison.OrdinalIgnoreCase) ||
                haystack.Contains("PDF", StringComparison.OrdinalIgnoreCase))
                continue;
            if (haystack.Contains("CSV", StringComparison.OrdinalIgnoreCase))
                return key;
            fallback ??= key;
        }
        return fallback != null && fallback.Contains("CSV", StringComparison.OrdinalIgnoreCase) ? fallback : null;
    }

    internal static IReadOnlyList<(string Id, string State)> DownloadRequests(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        var rows = new List<(string, string)>();
        foreach (var obj in Walk(doc.RootElement))
        {
            var id = FirstString(obj, "id", "downloadRequestID");
            var state = FirstString(obj, "state", "status");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(state))
                rows.Add((id, state));
        }
        return rows;
    }

    private static async Task<string> GetJsonAsync(HttpClient http, string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(http, HttpMethod.Get, path, null, "application/json", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new TriodosException("Triodos could not list the transactions to export.");
        return body;
    }

    private static async Task<byte[]?> GetFileAsync(HttpClient http, string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(http, HttpMethod.Get, path, null, "application/octet-stream", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode || bytes.Length == 0 || bytes[0] == (byte)'{')
            return null;
        if (bytes.Length > 2_000_000)
            throw new TriodosException("The Triodos export is too large to import.");
        return bytes;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpMethod method,
        string path,
        string? json,
        string accept,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        request.Headers.TryAddWithoutValidation("X-Triodos-Administration", "TBNL");
        request.Headers.TryAddWithoutValidation("X-Triodos-Accept-Language", "nl-NL");
        request.Headers.TryAddWithoutValidation("X-Triodos-Channel", "BANKING_WEB");
        request.Headers.Accept.ParseAdd(accept);
        if (json != null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await http.SendAsync(request, cancellationToken);
    }

    private static string? ReadString(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
        return null;
    }

    private static string? FirstString(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }
        return null;
    }

    private static IEnumerable<JsonElement> Walk(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var property in element.EnumerateObject())
            {
                foreach (var child in Walk(property.Value))
                    yield return child;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var child in Walk(item))
                    yield return child;
            }
        }
    }
}
