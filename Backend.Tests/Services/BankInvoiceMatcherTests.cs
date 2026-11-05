using Backend.Services;
using Backend.Services.Bank;

namespace Backend.Tests.Services;

public class BankInvoiceMatcherTests
{
    private const string ExampleDescription =
        "59 20016328  INV-2026-08-U14 INV-2026-08-U14 Laadkosten auto T. van Leeuwen - Aug-2026 2627 AN Delft";

    private static InvoiceCandidate August(decimal amount, bool paid = false) =>
        new(14, "T. van Leeuwen", 2026, 8, "260831 zonnewijzer 14-1", amount, paid);

    [Fact]
    public void LegacyNumber_ThroughSeptember_MatchesUserAndAmount()
    {
        var proposal = BankInvoiceMatcher.Match(ExampleDescription, 44.47m, [August(44.47m)]);

        Assert.Equal(BankMatchKind.Certain, proposal.Kind);
        Assert.Equal(14, proposal.UserId);
        Assert.Equal(2026, proposal.Year);
        Assert.Equal(8, proposal.Month);
    }

    [Fact]
    public void LegacyNumber_WrongAmount_IsUnmatched()
    {
        var proposal = BankInvoiceMatcher.Match(ExampleDescription, 10m, [August(44.47m)]);

        Assert.Null(proposal.Kind);
    }

    [Fact]
    public void OctoberNumber_MatchesNewStyle()
    {
        var number = InvoiceNumbers.Format(2026, 10, 14, 1);
        var invoice = new InvoiceCandidate(14, "T. van Leeuwen", 2026, 10, number, 12.50m, false);

        var proposal = BankInvoiceMatcher.Match($"Betaling {number}", 12.50m, [invoice]);

        Assert.Equal("261031 zonnewijzer 14-1", number);
        Assert.Equal(BankMatchKind.Certain, proposal.Kind);
        Assert.Equal(10, proposal.Month);
    }

    [Fact]
    public void October_DoesNotTreatLegacyNumberAsCertain()
    {
        var invoice = new InvoiceCandidate(
            14, "T. van Leeuwen", 2026, 10, InvoiceNumbers.Format(2026, 10, 14, 1), 12.50m, false);

        var proposal = BankInvoiceMatcher.Match("INV-2026-10-U14", 12.50m, [invoice]);

        Assert.Null(proposal.Kind);
        Assert.False(InvoiceNumbers.UsesLegacyFormat(2026, 10));
        Assert.True(InvoiceNumbers.UsesLegacyFormat(2026, 9));
    }

    [Fact]
    public void TwoDifferentInvoiceNumbers_AreUnmatched()
    {
        var invoices = new[]
        {
            August(44.47m),
            new InvoiceCandidate(14, "T. van Leeuwen", 2026, 7, "260731 zonnewijzer 14-1", 44.47m, false)
        };

        var proposal = BankInvoiceMatcher.Match("INV-2026-08-U14 INV-2026-07-U14", 44.47m, invoices);

        Assert.Null(proposal.Kind);
    }

    [Fact]
    public void NameAndMonth_WithoutInvoiceNumber_IsSuggested()
    {
        var proposal = BankInvoiceMatcher.Match(
            "Laadkosten auto T. van Leeuwen - Aug-2026",
            44.47m,
            [August(44.47m)]);

        Assert.Equal(BankMatchKind.Suggested, proposal.Kind);
        Assert.Equal(14, proposal.UserId);
    }

    [Fact]
    public void PaidInvoice_IsReportedAsAlreadyPaid()
    {
        var proposal = BankInvoiceMatcher.Match(ExampleDescription, 44.47m, [August(44.47m, paid: true)]);

        Assert.Equal(BankMatchKind.AlreadyPaid, proposal.Kind);
    }
}
