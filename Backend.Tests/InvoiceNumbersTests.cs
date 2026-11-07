using Backend.Services;

namespace Backend.Tests;

public class InvoiceNumbersTests
{
    [Fact]
    public void TryParseBilledPeriod_ReadsCurrentInvoiceNumber()
    {
        var description = $"Charging invoice {InvoiceNumbers.Format(2026, 12, 5, 2)} – Ada – December 2026";

        var parsed = InvoiceNumbers.TryParseBilledPeriod(description, out var userId, out var year, out var month);

        Assert.True(parsed);
        Assert.Equal(5, userId);
        Assert.Equal(2026, year);
        Assert.Equal(12, month);
    }

    [Fact]
    public void TryParseBilledPeriod_ReadsLegacyInvoiceNumber()
    {
        var parsed = InvoiceNumbers.TryParseBilledPeriod(
            "Charging invoice INV-2024-03-U2 – P – March 2024",
            out var userId,
            out var year,
            out var month);

        Assert.True(parsed);
        Assert.Equal(2, userId);
        Assert.Equal(2024, year);
        Assert.Equal(3, month);
    }
}
