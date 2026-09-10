namespace Backend.Contracts;

public sealed class SendMonthInvoicesBody
{
    public int Year { get; set; }
    public int Month { get; set; }
}
