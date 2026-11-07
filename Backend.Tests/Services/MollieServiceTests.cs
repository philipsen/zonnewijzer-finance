using System.Net;
using System.Text;
using Backend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Tests.Services;

public class MollieServiceTests
{
    [Fact]
    public async Task CreatePaymentAsync_CreatesPaymentLinkWithoutExpiry()
    {
        var handler = new RecordingHandler("""
            {
              "id": "pl_abc",
              "paidAt": null,
              "_links": {
                "paymentLink": { "href": "https://payment-links.mollie.com/payment/abc" }
              }
            }
            """);

        var result = await CreateService(handler).CreatePaymentAsync(12.5m, "Charging invoice 240401 zonnewijzer U2-1", "240401 zonnewijzer U2-1", "https://example/payments", "https://example/api/invoices/payments/webhook");

        Assert.Equal("pl_abc", result.PaymentId);
        Assert.Equal("https://payment-links.mollie.com/payment/abc", result.CheckoutUrl);
        Assert.Equal("open", result.Status);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.EndsWith("/payment-links", handler.Request.RequestUri!.AbsolutePath);
        Assert.Contains("\"value\":\"12.50\"", handler.Body);
        Assert.DoesNotContain("expiresAt", handler.Body);
        Assert.DoesNotContain("\"method\"", handler.Body);
    }

    [Fact]
    public async Task GetPaymentStatusAsync_PaidPaymentLink_ReturnsPaid()
    {
        var handler = new RecordingHandler("""
            { "id": "pl_abc", "paidAt": "2026-11-07T12:00:00+00:00" }
            """);

        var status = await CreateService(handler).GetPaymentStatusAsync("pl_abc");

        Assert.Equal("paid", status);
        Assert.EndsWith("/payment-links/pl_abc", handler.Request!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetPaymentStatusAsync_Payment_ReturnsStatusField()
    {
        var handler = new RecordingHandler("""{ "id": "tr_abc", "status": "expired" }""");

        var status = await CreateService(handler).GetPaymentStatusAsync("tr_abc");

        Assert.Equal("expired", status);
        Assert.EndsWith("/payments/tr_abc", handler.Request!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetWebhookPaymentAsync_ReturnsStatusAndDescription()
    {
        var handler = new RecordingHandler("""
            { "id": "tr_abc", "status": "paid", "description": "Charging invoice 240401 zonnewijzer U2-1" }
            """);

        var payment = await CreateService(handler).GetWebhookPaymentAsync("tr_abc");

        Assert.NotNull(payment);
        Assert.Equal("paid", payment.Status);
        Assert.Equal("Charging invoice 240401 zonnewijzer U2-1", payment.Description);
    }

    private static MollieService CreateService(RecordingHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Mollie")).Returns(() => new HttpClient(handler, disposeHandler: false));
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["Mollie:ApiKey"]).Returns("test_key");
        return new MollieService(factory.Object, config.Object, NullLogger<MollieService>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;

        public RecordingHandler(string response) => _response = response;

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
        }
    }
}
