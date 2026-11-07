namespace Backend.Services
{
    public interface IMollieService
    {
        /// <summary>
        /// Creates a Mollie payment link. Unlike an iDEAL payment, the link stays valid until it is paid;
        /// the 15-minute iDEAL session starts only when the customer opens it.
        /// </summary>
        Task<MolliePaymentResult> CreatePaymentAsync(decimal amount, string description, string invoiceNumber, string redirectUrl, string webhookUrl);
        Task<string> GetPaymentStatusAsync(string molliePaymentId);

        /// <summary>
        /// Loads a Payments API object (<c>tr_</c>) from a webhook. Payment links report the underlying attempt id, not the link id.
        /// </summary>
        Task<MollieWebhookPayment?> GetWebhookPaymentAsync(string molliePaymentId);
    }

    public record MolliePaymentResult(string PaymentId, string CheckoutUrl, string Status);

    public record MollieWebhookPayment(string Status, string? Description);
}
