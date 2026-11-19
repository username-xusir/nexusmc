using NEXMC.Services;
using Xunit;

namespace NEXMC.Tests;

public class NetdiskLinkParserTests
{
    [Fact]
    public void ParsesQuarkShareAndPasswordFromText()
    {
        var link = NetdiskLinkParser.Parse("整合包：https://pan.quark.cn/s/76148f08445c 提取码：ab12");

        Assert.NotNull(link);
        Assert.Equal(NetdiskPlatform.Quark, link!.Platform);
        Assert.Equal("76148f08445c", link.ShareId);
        Assert.Equal("ab12", link.Password);
        Assert.True(link.SupportsAutoResolve);
    }

    [Fact]
    public void ParsesUcShare()
    {
        var link = NetdiskLinkParser.Parse("https://drive.uc.cn/s/abc123?pwd=9x8y");

        Assert.NotNull(link);
        Assert.Equal(NetdiskPlatform.Uc, link!.Platform);
        Assert.Equal("abc123", link.ShareId);
        Assert.Equal("9x8y", link.Password);
        Assert.True(link.SupportsAutoResolve);
    }

    [Fact]
    public void ParsesBaiduAndRemovesLeadingOneFromSurl()
    {
        var link = NetdiskLinkParser.Parse("https://pan.baidu.com/s/1AbCdEf?pwd=1234");

        Assert.NotNull(link);
        Assert.Equal(NetdiskPlatform.Baidu, link!.Platform);
        Assert.Equal("AbCdEf", link.ShareId);
        Assert.False(link.SupportsAutoResolve);
    }

    [Theory]
    [InlineData("https://www.123pan.com/s/abc12-DEF34", NetdiskPlatform.Pan123)]
    [InlineData("https://115.com/s/swabc123?password=abcd", NetdiskPlatform.Pan115)]
    [InlineData("https://pan.xunlei.com/s/VNabc_123?pwd=xy12", NetdiskPlatform.Xunlei)]
    [InlineData("https://www.lanzou.com/iabc123", NetdiskPlatform.Lanzou)]
    [InlineData("https://www.guangyapan.com/s/abc_123", NetdiskPlatform.GuangYa)]
    public void DetectsUnsupportedPlatformsWithoutClaimingAutoResolve(string url, NetdiskPlatform platform)
    {
        var link = NetdiskLinkParser.Parse(url);

        Assert.NotNull(link);
        Assert.Equal(platform, link!.Platform);
        Assert.False(link.SupportsAutoResolve);
        Assert.False(string.IsNullOrWhiteSpace(link.LoginUrl));
    }

    [Fact]
    public void DirectArchiveIsResolvableWithoutNetdiskLogin()
    {
        var link = NetdiskLinkParser.Parse("https://example.com/files/modpack.zip");

        Assert.NotNull(link);
        Assert.Equal(NetdiskPlatform.Direct, link!.Platform);
        Assert.True(link.SupportsAutoResolve);
        Assert.Empty(link.ShareId);
    }

    [Fact]
    public void NonArchiveWebPageIsNotTreatedAsDirect()
    {
        Assert.Null(NetdiskLinkParser.Parse("https://example.com/project/123"));
    }

    [Fact]
    public void ModpackVersionShowsAutoResolveModeForQuarkLink()
    {
        var version = new ModpackVersion("v1", "1.0", "1.20.1", "BBSMC", false)
        {
            ExternalUrl = "https://pan.quark.cn/s/abc123"
        };

        Assert.Equal("网盘 · 可自动解析", version.InstallModeText);
    }

    [Fact]
    public void ModpackVersionKeepsBrowserFallbackForUnsupportedNetdisk()
    {
        var version = new ModpackVersion("v1", "1.0", "1.20.1", "BBSMC", false)
        {
            ExternalUrl = "https://pan.baidu.com/s/1AbCdEf"
        };

        Assert.Equal("仅网盘 · 可打开网盘", version.InstallModeText);
    }
}
