namespace Backend.Services;

/// <summary>Invoice amounts are VAT-inclusive (21%); split matches PDF invoices.</summary>
public static class InvoiceVat
{
    public const decimal Rate = 0.21m;

    public static (decimal ExclVat, decimal VatAmount) SplitInclusive(decimal totalInclVat)
    {
        var excl = Math.Round(totalInclVat / (1 + Rate), 2, MidpointRounding.AwayFromZero);
        var vat = Math.Round(totalInclVat - excl, 2, MidpointRounding.AwayFromZero);
        return (excl, vat);
    }
}
