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
}
