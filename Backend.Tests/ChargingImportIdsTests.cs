using Backend;
using Xunit;

namespace Backend.Tests;

public class ChargingImportIdsTests
{
    [Fact]
    public void ParseDisplayNameInParentheses_ExtractsInnerText()
    {
        Assert.Equal("Cock van Bruggen", ChargingImportIds.ParseDisplayNameInParentheses("047D9322B62290 (Cock van Bruggen)"));
        Assert.Equal("Test User", ChargingImportIds.ParseDisplayNameInParentheses("047D9322B62290 (Test User)"));
    }

    [Fact]
    public void ParseDisplayNameInParentheses_NoBrackets_ReturnsNull()
    {
        Assert.Null(ChargingImportIds.ParseDisplayNameInParentheses("047D9322B62290"));
    }

    [Fact]
    public void ChargingUserIdFromTokenKey_HexRfid_MatchesModuloRule()
    {
        var key = ChargingImportIds.NormalizeTokenKey("047D9322B62290 (Test)");
        var id = ChargingImportIds.ChargingUserIdFromTokenKey(key);
        var ul = ulong.Parse(key, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        var expected = (int)(ul % int.MaxValue);
        Assert.Equal(expected == 0 ? 1 : expected, id);
    }

    [Fact]
    public void ChargingUserIdFromTokenKey_NonHex_UsesFnvFallback()
    {
        var id = ChargingImportIds.ChargingUserIdFromTokenKey("not-hex-token");
        Assert.True(id > 0);
    }
}
