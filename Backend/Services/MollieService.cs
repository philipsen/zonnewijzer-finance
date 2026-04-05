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
            var apiKey = _configuration["Mollie:ApiKey"]
                ?? throw new InvalidOperationException("Mollie API key is not configured");

            var body = new
            {
                amount = new
                {
                    currency = "EUR",
                    value = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                },
                description = description,
                redirectUrl = redirectUrl,
                webhookUrl = string.IsNullOrWhiteSpace(webhookUrl) ? null : (object?)webhookUrl,
                metadata = new
                {
                    invoiceNumber = invoiceNumber
                }
            };

            var json = JsonSerializer.Serialize(body);
            var client = _httpClientFactory.CreateClient("Mollie");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await client.PostAsync($"{MollieApiBase}/payments",
                new StringContent(json, Encoding.UTF8, "application/json"));

            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Mollie API error {StatusCode}: {Body}", response.StatusCode, responseBody);
                throw new InvalidOperationException($"Mollie payment creation failed ({(int)response.StatusCode} {response.StatusCode}): {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var paymentId = root.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Mollie response missing payment id");

            var checkoutUrl = root
                .GetProperty("_links")
                .GetProperty("checkout")
                .GetProperty("href")
                .GetString()
                ?? throw new InvalidOperationException("Mollie response missing checkout URL");

            var status = root.GetProperty("status").GetString() ?? "open";

            _logger.LogInformation("Created Mollie payment {PaymentId} for invoice {InvoiceNumber}", paymentId, invoiceNumber);

            return new MolliePaymentResult(paymentId, checkoutUrl, status);
        }

        public async Task<string> GetPaymentStatusAsync(string molliePaymentId)
        {
            var apiKey = _configuration["Mollie:ApiKey"]
                ?? throw new InvalidOperationException("Mollie API key is not configured");

            var client = _httpClientFactory.CreateClient("Mollie");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

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
    }
}
