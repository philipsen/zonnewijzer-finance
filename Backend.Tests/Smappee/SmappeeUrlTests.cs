using Backend.Services.Smappee;

namespace Backend.Tests.Smappee;

public class SmappeeUrlTests
{
    [Fact]
    public void NormalizeApiBase_BareHost_appends_dev_v3()
    {
        var n = SmappeeUrl.NormalizeApiBase("https://app1pub.smappee.net");
        Assert.Equal("https://app1pub.smappee.net/dev/v3", n);
    }

    [Fact]
    public void NormalizeApiBase_FullV3_unchanged()
    {
        var n = SmappeeUrl.NormalizeApiBase("https://app1pub.smappee.net/dev/v3");
        Assert.Equal("https://app1pub.smappee.net/dev/v3", n);
    }

    [Fact]
    public void ApiV3ToOAuthV1_produces_v1_path()
    {
        var o = SmappeeUrl.ApiV3ToOAuthV1Base("https://app1pub.smappee.net/dev/v3");
        Assert.Equal("https://app1pub.smappee.net/dev/v1", o);
    }

    [Fact]
    public void EndToEnd_bareHost_tokenBase_is_dev_v1()
    {
        var api = SmappeeUrl.NormalizeApiBase("https://app1pub.smappee.net");
        var oauth = SmappeeUrl.ApiV3ToOAuthV1Base(api);
        Assert.Equal("https://app1pub.smappee.net/dev/v1", oauth);
    }
}
