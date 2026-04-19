using Backend.Contracts;

namespace Backend.Services;

public static class GnuCashInvoiceMapper
{
    public static GnuCashInvoiceDto ToDto(InvoiceData data, string? customerEmail)
    {
        var lastDay = new DateTime(data.Year, data.Month, DateTime.DaysInMonth(data.Year, data.Month));
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
                UnitPrice = data.RatePerKwh,
                LineAmount = s.Amount
            }).ToList()
        };
    }

    private static string SessionLineDescription(SessionData s)
    {
        var station = string.IsNullOrWhiteSpace(s.ChargingStationName) ? $"Station {s.StationId}" : s.ChargingStationName!;
        return $"{station} · {s.Start:yyyy-MM-dd HH:mm}";
    }
}
