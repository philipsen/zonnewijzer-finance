using Backend.Models;

namespace Backend.Services
{
    public interface IPdfService
    {
        byte[] GenerateInvoicePdf(InvoiceData invoiceData);
    }

    public class InvoiceData
    {
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string? Street { get; set; }
        public string? HouseNumber { get; set; }
        public string? PostalCode { get; set; }
        public string? PlaceName { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public string InvoiceNumber { get; set; } = string.Empty;
        public decimal TotalKwh { get; set; }
        public decimal RatePerKwh { get; set; }
        public decimal TotalAmount { get; set; }
        public List<SessionData> Sessions { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
        public string? PaymentUrl { get; set; }
        public bool IsPaid { get; set; }
    }

    public class SessionData
    {
        /// <summary>Which charge card (token) the session was billed on; included when a user has multiple cards.</summary>
        public int ChargeCardId { get; set; }
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public int StationId { get; set; }
        public string? StationSerialNumber { get; set; }
        public string? Connector { get; set; }
        public string? ChargingStationName { get; set; }
        public decimal ChargedKwh { get; set; }
        public decimal Amount { get; set; }
    }
}