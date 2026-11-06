using Backend.Services;

namespace Backend.Tests.Services;

public class PdfServiceTests
{
    [Fact]
    public void GenerateInvoicePdf_CombinedInvoice_GeneratesWithoutPaymentUrl()
    {
        var service = new PdfService();
        var data = new InvoiceData
        {
            UserId = 1,
            UserName = "Test User",
            Year = 2026,
            Month = 5,
            MonthName = "April – Mei 2026",
            InvoiceNumber = "INV-2026-04-05-U1",
            TotalKwh = 10,
            RatePerKwh = 0.25m,
            TotalAmount = 2.5m,
            UsePaymentRequestNotice = true,
            PaymentUrl = null,
            GeneratedAt = DateTime.UtcNow
        };

        var pdf = service.GenerateInvoicePdf(data);

        Assert.NotEmpty(pdf);
        Assert.Equal(0x25, pdf[0]); // PDF magic byte '%'
    }

    [Fact]
    public void GenerateInvoicePdf_WithMollieUrl_IncludesPaymentQr()
    {
        var service = new PdfService();
        var data = new InvoiceData
        {
            UserId = 1,
            UserName = "Test User",
            Year = 2026,
            Month = 10,
            MonthName = "oktober 2026",
            InvoiceNumber = "INV-2026-10-U1",
            TotalKwh = 10,
            RatePerKwh = 0.25m,
            TotalAmount = 2.5m,
            UsePaymentRequestNotice = true,
            PaymentUrl = "https://www.mollie.com/checkout/select-method/test",
            GeneratedAt = DateTime.UtcNow
        };

        var pdf = service.GenerateInvoicePdf(data);

        Assert.NotEmpty(pdf);
        Assert.Equal(0x25, pdf[0]);
        Assert.Contains("/Subtype /Image", System.Text.Encoding.ASCII.GetString(pdf));
    }

    [Fact]
    public void GenerateInvoicePdf_WithCorrection_Generates()
    {
        var service = new PdfService();
        var data = new InvoiceData
        {
            UserId = 1,
            UserName = "Test User",
            Year = 2026,
            Month = 11,
            MonthName = "November 2026",
            InvoiceNumber = "261201 zonnewijzer U1-1",
            TotalKwh = 10,
            RatePerKwh = 0.22m,
            EnergyAmount = 2.20m,
            TotalAmount = 0.70m,
            Corrections =
            [
                new InvoiceCorrectionLine
                {
                    Date = new DateOnly(2026, 11, 4),
                    Description = "Credit for outage",
                    Amount = -1.50m
                }
            ],
            UsePaymentRequestNotice = true,
            GeneratedAt = DateTime.UtcNow
        };

        var pdf = service.GenerateInvoicePdf(data);

        Assert.NotEmpty(pdf);
        Assert.Equal(0x25, pdf[0]);
    }
}
