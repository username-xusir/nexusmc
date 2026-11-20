using NEXMC.Services;
using Xunit;

namespace NEXMC.Tests;

public sealed class NetdiskCookieHeaderTests
{
    [Fact]
    public void ResponseCookieOverridesOlderSessionCookie()
    {
        var responseCookie = NetdiskCookieHeader.FromSetCookies(new[]
        {
            "__puus=new-session; Path=/; HttpOnly",
            "__pugs=guest-token; Path=/",
            "ignored"
        });

        var merged = NetdiskCookieHeader.Merge("__puus=old-session; __pus=account", responseCookie);

        Assert.Contains("__puus=new-session", merged);
        Assert.DoesNotContain("__puus=old-session", merged);
        Assert.Contains("__pugs=guest-token", merged);
        Assert.Contains("__pus=account", merged);
    }

    [Fact]
    public void EmptySetCookieLeavesOriginalCookieUntouched()
    {
        var merged = NetdiskCookieHeader.Merge("__puus=old-session", null);

        Assert.Equal("__puus=old-session", merged);
    }
}
