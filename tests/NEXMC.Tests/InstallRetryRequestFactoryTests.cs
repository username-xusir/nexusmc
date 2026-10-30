using NEXMC.Services;
using Xunit;

namespace NEXMC.Tests;

public sealed class InstallRetryRequestFactoryTests
{
    private static InstallRequest Request(bool downloadOnly = false) => new(
        "My Pack",
        "1.20.1",
        null,
        null,
        "bbsmc-project",
        "bbsmc-version",
        null,
        new[]
        {
            new LauncherOption("pcl", "PCL", LauncherKind.PCL2, @"C:\pcl\.minecraft", null, false),
            new LauncherOption("hmcl", "HMCL", LauncherKind.HMCL, @"C:\hmcl\.minecraft", null, false)
        },
        downloadOnly,
        downloadOnly ? @"C:\downloads" : null);

    [Fact]
    public void SelectsOnlyFailedTargets()
    {
        var targets = InstallRetryRequestFactory.SelectTargets(Request(), new[] { "hmcl" });
        Assert.Single(targets);
        Assert.Equal("hmcl", targets[0].Id);
    }

    [Fact]
    public void KeepsAllTargetsWhenFailureListIsEmpty()
    {
        var targets = InstallRetryRequestFactory.SelectTargets(Request(), Array.Empty<string>());
        Assert.Equal(2, targets.Length);
    }

    [Fact]
    public void LocalRetryDropsOnlineIdsAndPreservesDownloadMode()
    {
        var request = Request(downloadOnly: true);
        var retry = InstallRetryRequestFactory.CreateLocalRetry(request, @"C:\cache\pack.mrpack", new[] { "pcl" });

        Assert.Null(retry.ModpackProjectId);
        Assert.Null(retry.ModpackVersionId);
        Assert.Equal(@"C:\cache\pack.mrpack", retry.LocalModpackPath);
        Assert.True(retry.DownloadOnly);
        Assert.Equal(@"C:\downloads", retry.DownloadDirectory);
        Assert.Single(retry.Targets);
        Assert.Equal("pcl", retry.Targets[0].Id);
    }
}
