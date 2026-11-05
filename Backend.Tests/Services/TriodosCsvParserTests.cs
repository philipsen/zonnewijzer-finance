using Backend.Services.Bank;

namespace Backend.Tests.Services;

public class TriodosCsvParserTests
{
    private const string Example =
        "\"06-10-2026\",\"NL40TRIO0321247604\",\"44,47\",\"Credit\",\"R. en M. Hendriks B.V.\",\"RABONL2U NL22RABO0129901962\",\"ET\",\"59 20016328  INV-2026-08-U14 INV-2026-08-U14 Laadkosten auto T. van Leeuwen - Aug-2026 2627 AN Delft\",\"1.592,97\"";

    [Fact]
    public void ParsesHeaderlessTriodosLine()
    {
        var parsed = TriodosCsvParser.Parse(Example);

        Assert.Equal(0, parsed.Skipped);
        var row = Assert.Single(parsed.Transactions);
        Assert.Equal(new DateOnly(2026, 10, 6), row.BookedOn);
        Assert.Equal("NL40TRIO0321247604", row.AccountIban);
        Assert.Equal(44.47m, row.Amount);
        Assert.Equal("Credit", row.Direction);
        Assert.Equal("R. en M. Hendriks B.V.", row.CounterpartyName);
        Assert.Equal("RABONL2U", row.CounterpartyBic);
        Assert.Equal("NL22RABO0129901962", row.CounterpartyIban);
        Assert.Equal("ET", row.TransactionCode);
        Assert.Contains("INV-2026-08-U14", row.Description);
        Assert.Equal(1592.97m, row.BalanceAfter);
        Assert.Equal(64, row.Fingerprint.Length);
    }

    [Fact]
    public void SameLine_HasTheSameFingerprint()
    {
        var first = Assert.Single(TriodosCsvParser.Parse(Example).Transactions);
        var second = Assert.Single(TriodosCsvParser.Parse(Example + "\n" + Example).Transactions.Take(1));
        var again = TriodosCsvParser.Parse(Example).Transactions[0];

        Assert.Equal(first.Fingerprint, again.Fingerprint);
        Assert.Equal(2, TriodosCsvParser.Parse(Example + "\r\n" + Example).Transactions.Count);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }
}
