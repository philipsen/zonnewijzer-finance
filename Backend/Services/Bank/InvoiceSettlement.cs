namespace Backend.Services.Bank;

public static class InvoiceSettlement
{
    /// <summary>
    /// A positive invoice is covered once matched money reaches it.
    /// A negative invoice is a refund and is covered once the matched amount is at least as negative.
    /// </summary>
    public static bool IsCoveredByBank(decimal totalAmount, decimal bankAmount) =>
        totalAmount > 0
            ? bankAmount >= totalAmount
            : totalAmount < 0 && bankAmount <= totalAmount;
}
