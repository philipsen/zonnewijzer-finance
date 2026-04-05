namespace Backend.Services
{
    public interface IMollieService
    {
        Task<MolliePaymentResult> CreatePaymentAsync(decimal amount, string description, string invoiceNumber, string redirectUrl, string webhookUrl);
        Task<string> GetPaymentStatusAsync(string molliePaymentId);
    }

    public record MolliePaymentResult(string PaymentId, string CheckoutUrl, string Status);
}
