using Backend.Contracts;

namespace Backend.Services;

public static class GnuCashInvoiceMapper
{
    /// <summary>Same convention as PDF invoices: energy rate and session amounts include 21% BTW.</summary>
    private const decimal VatInclusiveMultiplier = 1.21m;

    /// <summary>Per-kWh and line euros sent to GnuCash must be ex-VAT; BTW is applied via the book tax table.</summary>
    private static decimal EnergyUnitPriceExclVat(decimal pricePerKwhInclVat) =>
        Math.Round(pricePerKwhInclVat / VatInclusiveMultiplier, 6, MidpointRounding.AwayFromZero);

    public static GnuCashInvoiceDto ToDto(InvoiceData data, string? customerEmail)
    {
        var lastDay = new DateTime(data.Year, data.Month, DateTime.DaysInMonth(data.Year, data.Month));
        var unitExclVat = EnergyUnitPriceExclVat(data.RatePerKwh);
        return new GnuCashInvoiceDto
        {
            InvoiceId = data.InvoiceNumber,
            CustomerId = $"CU-{data.UserId}",
            CustomerName = data.UserName,
            Street = data.Street,
            HouseNumber = data.HouseNumber,
            PostalCode = data.PostalCode,
            PlaceName = data.PlaceName,
            Email = customerEmail,
            Year = data.Year,
            Month = data.Month,
            InvoiceDateOpened = lastDay.ToString("yyyy-MM-dd"),
            Currency = "EUR",
            MolliePaid = data.IsPaid,
            Lines = data.Sessions.Select(s => new GnuCashInvoiceLineDto
            {
                Description = SessionLineDescription(s),
                Quantity = s.ChargedKwh,
                UnitPrice = unitExclVat,
                LineAmount = Math.Round(s.ChargedKwh * unitExclVat, 2, MidpointRounding.AwayFromZero)
            }).ToList()
        };
    }

    private static string SessionLineDescription(SessionData s)
    {
        var station = string.IsNullOrWhiteSpace(s.ChargingStationName) ? $"Station {s.StationId}" : s.ChargingStationName!;
        return $"{station} · {s.Start:yyyy-MM-dd HH:mm}";
    }
}
