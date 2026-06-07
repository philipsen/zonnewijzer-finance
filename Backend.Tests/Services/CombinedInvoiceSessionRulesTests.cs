using Backend.Services;

namespace Backend.Tests.Services;

public class CombinedInvoiceSessionRulesTests
{
    [Fact]
    public void IncludeSession_RequiresEndBeforeRangeEnd()
    {
        const long rangeStart = 1_000;
        const long rangeEnd = 2_000;

        Assert.True(CombinedInvoiceSessionRules.IncludeSession(1_500, 1_900, rangeStart, rangeEnd));
        Assert.False(CombinedInvoiceSessionRules.IncludeSession(1_500, 2_000, rangeStart, rangeEnd));
        Assert.False(CombinedInvoiceSessionRules.IncludeSession(1_500, 2_500, rangeStart, rangeEnd));
        Assert.False(CombinedInvoiceSessionRules.IncludeSession(1_500, null, rangeStart, rangeEnd));
    }

    [Fact]
    public void IncludeSession_RequiresStartInRange()
    {
        const long rangeStart = 1_000;
        const long rangeEnd = 2_000;

        Assert.False(CombinedInvoiceSessionRules.IncludeSession(900, 1_500, rangeStart, rangeEnd));
        Assert.False(CombinedInvoiceSessionRules.IncludeSession(2_000, 2_500, rangeStart, rangeEnd));
    }
}
