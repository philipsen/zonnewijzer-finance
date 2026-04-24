using Backend;
using Xunit;

namespace Backend.Tests;

public class ChargeCardImportIdsTests
{
    [Fact]
    public void NormalizeTokenKey_TrimsAndStripsParenthetical()
    {
        Assert.Equal("047D9322B62290", ChargeCardImportIds.NormalizeTokenKey("047D9322B62290 (Cock van Bruggen)"));
        Assert.Equal("047D9322B62290", ChargeCardImportIds.NormalizeTokenKey("  047D9322B62290  "));
    }
}
