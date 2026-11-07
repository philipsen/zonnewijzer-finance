using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Backend.Services
{
    public class MollieService : IMollieService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MollieService> _logger;

        private const string MollieApiBase = "https://api.mollie.com/v2";

        public MollieService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<MollieService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<MolliePaymentResult> CreatePaymentAsync(decimal amount, string description, string invoiceNumber, string redirectUrl, string webhookUrl)
        {
            var apiKey = RequireApiKey();

            // Payment links do not expire unless expiresAt is set. A Payments API iDEAL
            // checkout (pay.ideal.nl) expires about 15 minutes after it is created.
            if (description.Length > 255)
                description = description[..255];

            var body = new
            {
                amount = new
                {
                    currency = "EUR",
                    value = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                },
                description,
                redirectUrl,
                webhookUrl = string.IsNullOrWhiteSpace(webhookUrl) ? null : (object?)webhookUrl
            };

            var json = JsonSerializer.Serialize(body);
            var client = CreateClient(apiKey);

            var response = await client.PostAsync($"{MollieApiBase}/payment-links",
                new StringContent(json, Encoding.UTF8, "application/json"));

            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Mollie API error {StatusCode}: {Body}", response.StatusCode, responseBody);
                throw new InvalidOperationException($"Mollie payment link creation failed ({(int)response.StatusCode} {response.StatusCode}): {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var paymentId = root.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Mollie response missing payment link id");

            var checkoutUrl = root
                .GetProperty("_links")
                .GetProperty("paymentLink")
                .GetProperty("href")
                .GetString()
                ?? throw new InvalidOperationException("Mollie response missing payment link URL");

            var status = MapPaymentLinkStatus(root);

            _logger.LogInformation("Created Mollie payment link {PaymentId} for invoice {InvoiceNumber}", paymentId, invoiceNumber);

            return new MolliePaymentResult(paymentId, checkoutUrl, status);
        }

        public async Task<string> GetPaymentStatusAsync(string molliePaymentId)
        {
            if (molliePaymentId.StartsWith("pl_", StringComparison.Ordinal))
                return await GetPaymentLinkStatusAsync(molliePaymentId);

            var apiKey = RequireApiKey();
            var client = CreateClient(apiKey);

            var response = await client.GetAsync($"{MollieApiBase}/payments/{molliePaymentId}");
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Mollie get payment error {StatusCode}: {Body}", response.StatusCode, responseBody);
                throw new InvalidOperationException($"Mollie get payment failed: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement.GetProperty("status").GetString() ?? "unknown";
        }

        public async Task<MollieWebhookPayment?> GetWebhookPaymentAsync(string molliePaymentId)
        {
            if (!molliePaymentId.StartsWith("tr_", StringComparison.Ordinal))
                return null;

            var apiKey = RequireApiKey();
            var client = CreateClient(apiKey);

            var response = await client.GetAsync($"{MollieApiBase}/payments/{molliePaymentId}");
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Mollie get payment error {StatusCode}: {Body}", response.StatusCode, responseBody);
                throw new InvalidOperationException($"Mollie get payment failed: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            var status = root.GetProperty("status").GetString() ?? "unknown";
            string? description = null;
            if (root.TryGetProperty("description", out var descriptionElement)
                && descriptionElement.ValueKind == JsonValueKind.String)
            {
                description = descriptionElement.GetString();
            }

            return new MollieWebhookPayment(status, description);
        }

        private async Task<string> GetPaymentLinkStatusAsync(string paymentLinkId)
        {
            var apiKey = RequireApiKey();
            var client = CreateClient(apiKey);

            var response = await client.GetAsync($"{MollieApiBase}/payment-links/{paymentLinkId}");
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Mollie get payment link error {StatusCode}: {Body}", response.StatusCode, responseBody);
                throw new InvalidOperationException($"Mollie get payment link failed: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            return MapPaymentLinkStatus(doc.RootElement);
        }

        private static string MapPaymentLinkStatus(JsonElement root)
        {
            if (root.TryGetProperty("paidAt", out var paidAt) && paidAt.ValueKind == JsonValueKind.String)
                return "paid";

            if (root.TryGetProperty("archived", out var archived) && archived.ValueKind == JsonValueKind.True)
                return "expired";

            if (root.TryGetProperty("expiresAt", out var expiresAt)
                && expiresAt.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(expiresAt.GetString(), out var expiry)
                && expiry <= DateTimeOffset.UtcNow)
            {
                return "expired";
            }

            return "open";
        }

        private string RequireApiKey() =>
            _configuration["Mollie:ApiKey"]
            ?? throw new InvalidOperationException("Mollie API key is not configured");

        private HttpClient CreateClient(string apiKey)
        {
            var client = _httpClientFactory.CreateClient("Mollie");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return client;
        }
    }
}
