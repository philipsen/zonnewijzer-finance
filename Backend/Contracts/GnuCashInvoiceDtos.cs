namespace Backend.Contracts;

public sealed class GnuCashInsertRequest
{
    public int SchemaVersion { get; set; } = 1;
    public List<GnuCashInvoiceDto> Invoices { get; set; } = new();
}

public sealed class GnuCashInvoiceDto
{
    public string InvoiceId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? PostalCode { get; set; }
    public string? PlaceName { get; set; }
    public string? Email { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    /// <summary>ISO 8601 date (invoice date opened).</summary>
    public string InvoiceDateOpened { get; set; } = string.Empty;
    public string Currency { get; set; } = "EUR";
    public bool MolliePaid { get; set; }
    public List<GnuCashInvoiceLineDto> Lines { get; set; } = new();
}

public sealed class GnuCashInvoiceLineDto
{
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineAmount { get; set; }
}

public sealed class GnuCashInsertResponse
{
    public List<GnuCashInsertResultItem> Results { get; set; } = new();
}

public sealed class GnuCashInsertResultItem
{
    public string InvoiceId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Detail { get; set; }
}
