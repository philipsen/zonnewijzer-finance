using System.Net;
using System.Text;
using Backend.Services.Bank;

namespace Backend.Tests.Services;

public class TriodosBankingClientTests
{
    [Fact]
    public void MapLoginStatus_FollowsTheAppQrStates()
    {
        Assert.Equal("waiting", TriodosBankingClient.MapLoginStatus("REQUESTED"));
        Assert.Equal("scanned", TriodosBankingClient.MapLoginStatus("SCANNED"));
        Assert.Equal("ready", TriodosBankingClient.MapLoginStatus("AUTHORISED"));
        Assert.Equal("expired", TriodosBankingClient.MapLoginStatus("EXPIRED"));
        Assert.Equal("cancelled", TriodosBankingClient.MapLoginStatus("CANCELLED"));
    }

    [Fact]
    public void ProductUuids_ReadsNestedAccountIds()
    {
        const string json = """
            {"groups":[{"accounts":[
              {"productUuid":"11111111-1111-1111-1111-111111111111","iban":"NL40 TRIO 0321 2476 04"},
              {"productUuid":"22222222-2222-2222-2222-222222222222","iban":"NL12TRIO0123456789"}
            ]}]}
            """;

        var ids = TriodosBankingClient.ProductUuids(json);

        Assert.Equal(["11111111-1111-1111-1111-111111111111"], ids);
    }

    [Fact]
    public void ProductUuids_ReadsTheIbanNestedUnderTheAccount()
    {
        const string json = """
            {"accounts":[{"name":"Betaalrekening","productUuid":"11111111-1111-1111-1111-111111111111","account":{"iban":"NL40 TRIO 0321 2476 04"}}]}
            """;

        Assert.Equal(["11111111-1111-1111-1111-111111111111"], TriodosBankingClient.ProductUuids(json));
    }

    [Fact]
    public async Task DownloadCsv_ListsTheAccountsWhenTheCooperativeAccountIsMissing()
    {
        var handler = new ScriptHandler(Json("""
            {"accounts":[
              {"name":"Spaarrekening","productUuid":"22222222-2222-2222-2222-222222222222","account":{"iban":"NL12TRIO0123456789"}},
              {"name":"Beleggen","productId":"77"}
            ]}
            """));

        var error = await Assert.ThrowsAsync<TriodosException>(() => TriodosBankingClient.DownloadCsvAsync(
            Client(handler),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 11, 9),
            CancellationToken.None));

        Assert.Contains("Accounts found: Spaarrekening NL12TRIO0123456789; Beleggen product 77.", error.Message);
    }

    [Fact]
    public void CsvFormatKey_PrefersCsvOverTheOtherExportFormats()
    {
        const string json = """
            {"downloadFormats":[
              {"key":"MT940","viewValue":"MT940"},
              {"key":"CAMT053","viewValue":"CAMT.053"},
              {"key":"CSV","viewValue":"CSV (kommagescheiden)"}
            ]}
            """;

        Assert.Equal("CSV", TriodosBankingClient.CsvFormatKey(json));
    }

    [Fact]
    public async Task StartLogin_PostsTheQrFlow()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };
        var handler = new ScriptHandler(
            Json("""{"serverTimeUTC":"2026-11-09T18:12:45Z"}"""),
            Json("""{"crossChannelLoginUUID":"b2251332-3a44-4582-a821-27e42e9b8618"}"""),
            Bytes(png, "image/png"));

        var (uuid, qr) = await TriodosBankingClient.StartLoginAsync(Client(handler), CancellationToken.None);

        Assert.Equal("b2251332-3a44-4582-a821-27e42e9b8618", uuid);
        Assert.Equal(png, qr);
        Assert.Equal(HttpMethod.Get, handler.Calls[0].Method);
        Assert.EndsWith("/banking/v4/login/init", handler.Calls[0].Path);
        Assert.EndsWith("/banking/v4/cross-channel-login/start", handler.Calls[1].Path);
        Assert.EndsWith("/banking/v4/cross-channel-login/generate-qr", handler.Calls[2].Path);
        Assert.Equal("TBNL", handler.Calls[1].Administration);
        Assert.Equal("Mozilla/5.0", handler.Calls[0].UserAgent);
        Assert.Equal("image/png", handler.Calls[2].Accept);
    }

    [Fact]
    public async Task DownloadCsv_RequestsTheCsvExportAndReadsTheProcessedFile()
    {
        const string csv = "\"06-10-2026\",\"NL40TRIO0321247604\",\"44,47\",\"Credit\",\"Name\",\"RABONL2U NL22RABO0129901962\",\"ET\",\"rent\",\"1.592,97\"";
        var handler = new ScriptHandler(
            Json("""{"accounts":[{"productUuid":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","iban":"NL40TRIO0321247604"},{"productUuid":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb","iban":"NL12TRIO0123456789"}]}"""),
            Json("""{"downloadFormats":[{"key":"CSV","viewValue":"CSV"},{"key":"MT940","viewValue":"MT940"}]}"""),
            Json("""{"downloadRequests":[]}"""),
            Json("""{"id":"req-9"}"""),
            Json("""{"downloadRequests":[{"id":"req-9","state":"PROCESSED"}]}"""),
            Bytes(Encoding.UTF8.GetBytes(csv), "application/octet-stream"));

        var downloaded = await TriodosBankingClient.DownloadCsvAsync(
            Client(handler),
            new DateOnly(2025, 5, 1),
            new DateOnly(2026, 11, 9),
            CancellationToken.None);

        Assert.Equal(csv, downloaded);
        var post = handler.Calls.Single(call => call.Method == HttpMethod.Post);
        Assert.EndsWith("/banking/v4/transaction-download/requests", post.Path);
        Assert.Contains("\"downloadFormat\":\"CSV\"", post.Body);
        Assert.Contains("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", post.Body);
        Assert.DoesNotContain("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", post.Body);
        Assert.Contains("\"fromDate\":\"2025-05-01\"", post.Body);
        Assert.Contains("\"untilDate\":\"2026-11-09\"", post.Body);
        Assert.EndsWith(
            "/banking/v4/transaction-download/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/requests/req-9",
            handler.Calls[^1].Path);
        Assert.Contains("text/csv", handler.Calls[^1].Accept);
    }

    [Fact]
    public async Task DownloadCsv_UsesTheProductIdFromTheProcessedRequest()
    {
        const string csv = "\"06-10-2026\",\"NL40TRIO0321247604\",\"44,47\",\"Credit\",\"Name\",\"RABONL2U NL22RABO0129901962\",\"ET\",\"rent\",\"1.592,97\"";
        var handler = new ScriptHandler(
            Json("""{"accounts":[{"productUuid":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","iban":"NL40TRIO0321247604","productId":"current-account"}]}"""),
            Json("""{"downloadFormats":[{"key":"CSV","viewValue":"CSV"}]}"""),
            Json("""{"downloadRequests":[]}"""),
            Json("""{"id":"req-9"}"""),
            Json("""{"downloadRequests":[{"id":"req-9","state":"PROCESSED","productId":"current-account"}]}"""),
            Json("""{"message":"not on this product"}"""),
            Bytes(Encoding.UTF8.GetBytes(csv), "text/csv"));

        var downloaded = await TriodosBankingClient.DownloadCsvAsync(
            Client(handler),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 11, 9),
            CancellationToken.None);

        Assert.Equal(csv, downloaded);
        var fileCalls = handler.Calls.Where(call => call.Path.Contains("/requests/req-9", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, fileCalls.Count);
        Assert.EndsWith("/transaction-download/current-account/requests/req-9", fileCalls[0].Path);
        Assert.EndsWith("/transaction-download/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/requests/req-9", fileCalls[1].Path);
    }

    [Fact]
    public async Task DownloadCsv_RequestsTheFileWithTheNumericAccountProductId()
    {
        const string csv = "\"06-10-2026\",\"NL40TRIO0321247604\",\"44,47\",\"Credit\",\"Name\",\"RABONL2U NL22RABO0129901962\",\"ET\",\"rent\",\"1.592,97\"";
        var handler = new ScriptHandler(
            Json("""{"accounts":[{"productUuid":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","productId":42,"iban":"NL40TRIO0321247604"}]}"""),
            Json("""{"downloadFormats":[{"key":"CSV","viewValue":"CSV"}]}"""),
            Json("""{"downloadRequests":[]}"""),
            Json("""{"id":"req-9"}"""),
            Json("""{"downloadRequests":[{"id":"req-9","state":"PROCESSED"}]}"""),
            Bytes(Encoding.UTF8.GetBytes(csv), "text/csv"));

        var downloaded = await TriodosBankingClient.DownloadCsvAsync(
            Client(handler),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 11, 9),
            CancellationToken.None);

        Assert.Equal(csv, downloaded);
        Assert.EndsWith("/transaction-download/42/requests/req-9", handler.Calls[^1].Path);
    }

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://bankieren.triodos.nl/") };

    private static Func<HttpRequestMessage, HttpResponseMessage> Json(string body) =>
        _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static Func<HttpRequestMessage, HttpResponseMessage> Bytes(byte[] body, string mediaType) =>
        _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType) }
            }
        };

    private sealed class ScriptHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;
        public List<(HttpMethod Method, string Path, string? Body, string? Accept, string? Administration, string? UserAgent)> Calls { get; } = [];

        public ScriptHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((
                request.Method,
                request.RequestUri!.AbsolutePath,
                body,
                request.Headers.Accept.Count > 0
                    ? string.Join(", ", request.Headers.Accept.Select(header => header.MediaType))
                    : null,
                request.Headers.TryGetValues("X-Triodos-Administration", out var values) ? values.Single() : null,
                request.Headers.TryGetValues("User-Agent", out var agent) ? agent.Single() : null));
            return _responses.Dequeue()(request);
        }
    }
}
