using NEXMC.Services;
using Xunit;

namespace NEXMC.Tests;

public sealed class RetryAndSourceTests
{
    [Fact]
    public async Task RetryStopsAfterThreeAttemptsAndUsesDelay()
    {
        var attempts = 0;
        var failures = 0;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InstallRetryPolicy.RunAsync<int>(
                _ =>
                {
                    attempts++;
                    throw new InvalidOperationException("network down");
                },
                (_, _) =>
                {
                    failures++;
                    return Task.CompletedTask;
                },
                CancellationToken.None,
                delay: TimeSpan.Zero));

        Assert.Equal("network down", exception.Message);
        Assert.Equal(3, attempts);
        Assert.Equal(2, failures);
    }

    [Fact]
    public async Task RetryReturnsThirdAttemptResult()
    {
        var attempts = 0;
        var result = await InstallRetryPolicy.RunAsync(
            attempt =>
            {
                attempts++;
                return attempt == 3
                    ? Task.FromResult("ok")
                    : Task.FromException<string>(new IOException("temporary"));
            },
            (_, _) => Task.CompletedTask,
            CancellationToken.None,
            delay: TimeSpan.Zero);

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task NonRetryableFailureDoesNotRepeat()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            InstallRetryPolicy.RunAsync<int>(
                _ =>
                {
                    attempts++;
                    throw new ArgumentException("实例名称不可用");
                },
                (_, _) => Task.CompletedTask,
                CancellationToken.None,
                exception => !InstallRetryPolicy.IsNonRetryable(exception),
                TimeSpan.Zero));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void AllSourceAliasesAreRecognized()
    {
        Assert.True(ModpackSources.IsAll("全部来源"));
        Assert.True(ModpackSources.IsAll(" 全部来源 "));
        Assert.False(ModpackSources.IsAll("BBSMC"));
        Assert.Equal(3, ModpackSources.Individual.Length);
    }

    [Fact]
    public void SelectedSourcesCanExcludeBbsmc()
    {
        var selected = ModpackSources.Parse("Modrinth,CurseForge");
        Assert.Equal(new[] { "Modrinth", "CurseForge" }, selected);
        Assert.DoesNotContain("BBSMC", selected);
        Assert.Equal("2 个来源", ModpackSources.Display(selected));
        Assert.Equal("Modrinth,CurseForge", ModpackSources.Join(selected));
    }

    [Fact]
    public void LauncherDirectoryIsFoundBesideExecutable()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexmc-launcher-" + Guid.NewGuid().ToString("N"));
        try
        {
            var minecraft = Path.Combine(root, ".minecraft");
            Directory.CreateDirectory(minecraft);
            var executable = Path.Combine(root, "PCL.exe");
            File.WriteAllText(executable, string.Empty);
            Assert.Equal(minecraft, LauncherDetector.FindMinecraftDirectory(executable));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LauncherDisplayDoesNotContainMachineFolderNames()
    {
        var option = new LauncherOption("pcl", "PCL", LauncherKind.PCL2, @"C:\Games\.minecraft", null, false)
        {
            InstalledVersionCount = 7
        };
        Assert.Equal("PCL · 7 个版本", option.DisplayLabel);
        Assert.DoesNotContain("桌面", option.DisplayLabel);
        Assert.DoesNotContain("新建文件夹", option.DisplayLabel);
    }

    [Fact]
    public void CompletedTargetHidesProgressAndMarksSuccess()
    {
        var option = new LauncherOption("pcl", "PCL", LauncherKind.PCL2, @"C:\Games\.minecraft", null, false);
        var target = new NEXMC.Models.InstallTargetTaskItem(option);
        Assert.True(target.IsProgressVisible);
        Assert.False(target.IsSuccess);

        target.Report(new TargetProgress("pcl", 1, "安装完成", true));
        Assert.False(target.IsProgressVisible);
        Assert.True(target.IsSuccess);
        Assert.Equal("安装成功", target.StatusText);
    }

    [Fact]
    public void CompletedTaskExposesOverallSuccessCheckmark()
    {
        var option = new LauncherOption("pcl", "PCL", LauncherKind.PCL2, @"C:\Games\.minecraft", null, false);
        var task = new NEXMC.Models.InstallTaskItem("测试", new[] { option });
        task.Report(new TargetProgress("pcl", 1, "安装完成", true));
        task.Complete();
        Assert.True(task.IsAllSuccess);
        Assert.Equal("全部目标安装成功", task.OverallStatus);
    }

    [Fact]
    public void AllSourcesStillJoinToAllAlias()
    {
        var selected = ModpackSources.Parse("全部来源");
        Assert.Equal(3, selected.Count);
        Assert.Equal(ModpackSources.All, ModpackSources.Join(selected));
        Assert.Equal(ModpackSources.All, ModpackSources.Display(selected));
        Assert.Equal("未选来源", ModpackSources.Display(Array.Empty<string>()));
    }
}
