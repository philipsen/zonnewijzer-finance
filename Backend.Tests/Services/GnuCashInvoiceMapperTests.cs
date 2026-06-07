using Backend.Services;

namespace Backend.Tests.Services;

public class GnuCashInvoiceMapperTests
{
    [Fact]
    public void ToDto_UsesPerSessionUnitPriceWhenRatesDiffer()
    {
        var data = new InvoiceData
        {
            UserId = 1,
            UserName = "Test",
            Year = 2026,
            Month = 5,
            InvoiceNumber = "INV-2026-04-05-U1",
            RatePerKwh = 0.25m,
            Sessions =
            [
                new SessionData { ChargeCardId = 1, StationId = 1, Start = DateTime.UtcNow, ChargedKwh = 10m, Amount = 2m },
                new SessionData { ChargeCardId = 1, StationId = 1, Start = DateTime.UtcNow, ChargedKwh = 10m, Amount = 3m }
            ]
        };

        var dto = GnuCashInvoiceMapper.ToDto(data, "test@example.com");

        Assert.Equal(2, dto.Lines.Count);
        Assert.NotEqual(dto.Lines[0].UnitPrice, dto.Lines[1].UnitPrice);
        Assert.Equal(0.20m / 1.21m, dto.Lines[0].UnitPrice, 4);
        Assert.Equal(0.30m / 1.21m, dto.Lines[1].UnitPrice, 4);
    }
}
