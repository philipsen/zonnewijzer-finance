using Backend.Services.Smappee;

namespace Backend.Tests.Smappee;

public class SmappeeApplianceDisplayNameTests
{
    [Theory]
    [InlineData("Paal 2 - 1", "Paal 2")]
    [InlineData("Paal 2 - 1 ", "Paal 2")]
    [InlineData("Only", "Only")]
    [InlineData("Level - 12", "Level - 12")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void NormalizeName_StripsTrailingConnectorOne(string? input, string? expected)
    {
        Assert.Equal(expected, SmappeeApplianceDisplayName.NormalizeName(input));
    }
}
