using NEXMC.Models;
using NEXMC.Services;
using Xunit;

namespace NEXMC.Tests;

public sealed class BbsmcAndProgressTests
{
    [Theory]
    [InlineData("<script src=\"/_guard/html.js?js=captcha_html\"></script>")]
    [InlineData("BBSMC CDN 人机验证拦截，需在浏览器下载后导入。")]
    public void CaptchaFailuresAreDetected(string text)
    {
        Assert.True(BbsmcCaptchaHelper.IsCaptchaFailure(text));
    }

    [Fact]
    public void OrdinaryNetworkFailureIsNotCaptcha()
    {
        Assert.False(BbsmcCaptchaHelper.IsCaptchaFailure("连接超时"));
    }

    [Fact]
    public void DuplicateLauncherNamesReceiveIndependentResultsById()
    {
        var targets = new[]
        {
            new LauncherOption("pcl-a", "PCL2", LauncherKind.PCL2, @"C:\games\a", null, false),
            new LauncherOption("pcl-b", "PCL2", LauncherKind.PCL2, @"C:\games\b", null, false)
        };
        var task = new InstallTaskItem("duplicate-name test", targets);

        task.Report(new TargetProgress("pcl-a", 1, "安装完成，校验通过", true));
        task.Report(new TargetProgress("pcl-b", 1, "安装完成，校验通过", true));
        task.Complete();

        Assert.All(task.Targets, target => Assert.True(target.Success));
        Assert.Equal("全部目标安装成功", task.OverallStatus);
        Assert.Contains(@"C:\games\a", task.ResultDetails());
        Assert.Contains(@"C:\games\b", task.ResultDetails());
    }

    [Fact]
    public void MissingTargetResultIsStillReported()
    {
        var targets = new[]
        {
            new LauncherOption("pcl-a", "PCL2", LauncherKind.PCL2, @"C:\games\a", null, false),
            new LauncherOption("pcl-b", "PCL2", LauncherKind.PCL2, @"C:\games\b", null, false)
        };
        var task = new InstallTaskItem("missing result test", targets);

        task.Report(new TargetProgress("pcl-a", 1, "安装完成，校验通过", true));
        task.Complete();

        Assert.True(task.Targets[0].Success);
        Assert.False(task.Targets[1].Success);
        Assert.Equal("部分目标安装成功", task.OverallStatus);
    }
}
