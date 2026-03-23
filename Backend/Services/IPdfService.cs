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
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public string InvoiceNumber { get; set; } = string.Empty;
        public decimal TotalKwh { get; set; }
        public decimal RatePerKwh { get; set; }
        public decimal TotalAmount { get; set; }
        public List<SessionData> Sessions { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
    }

    public class SessionData
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public int StationId { get; set; }
        public decimal ChargedKwh { get; set; }
        public decimal Amount { get; set; }
    }
}