using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using NEXMC.Services;
using Xunit;

namespace NEXMC.Tests;

public sealed class UpdateServiceTests
{
    private static readonly object EnvironmentLock = new();

    [Fact]
    public void ReleaseNotesKeepHeadingsBulletsAndCode()
    {
        var lines = UpdateService.ParseReleaseNotes("""
            # 1.0.11
            自动更新

            - 检查 Release
            - 校验 SHA-256

            ```text
            NEXMC-Setup-1.0.11.exe
            ```
            """);

        Assert.Contains(lines, line => line.Kind == ReleaseNoteKind.Heading && line.Text == "1.0.11");
        Assert.Contains(lines, line => line.Kind == ReleaseNoteKind.Bullet && line.Text.Contains("检查 Release"));
        Assert.Contains(lines, line => line.Kind == ReleaseNoteKind.Code && line.Text.Contains("NEXMC-Setup"));
    }

    [Fact]
    public void NewerPrereleaseTagIsDetectedFromFixture()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "nexmc-update-test-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(fixture, """
            {
              "tag_name": "v1.0.11",
              "name": "NEXMC 1.0.11",
              "body": "# 更新\n- 自动更新",
              "published_at": "2026-11-18T12:00:00Z",
              "assets": [
                {
                  "name": "NEXMC-Setup-1.0.11.exe",
                  "browser_download_url": "https://github.com/username-xusir/nexusmc/releases/download/v1.0.11/NEXMC-Setup-1.0.11.exe"
                }
              ]
            }
            """);

        try
        {
            UpdateCheckResult result;
            lock (EnvironmentLock)
            {
                Environment.SetEnvironmentVariable("NEXMC_UPDATE_TEST_RELEASE", fixture);
                try
                {
                    result = new UpdateService().CheckAsync(new Version(1, 0, 10)).GetAwaiter().GetResult();
                }
                finally
                {
                    Environment.SetEnvironmentVariable("NEXMC_UPDATE_TEST_RELEASE", null);
                }
            }

            Assert.True(result.HasUpdate);
            Assert.NotNull(result.Update);
            Assert.Equal("1.0.11", result.Update!.Version);
            Assert.Contains("NEXMC-Setup-1.0.11.exe", result.Update.InstallerName);
        }
        finally
        {
            File.Delete(fixture);
        }
    }

    [Fact]
    public void SameOrOlderVersionDoesNotOfferUpdate()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "nexmc-update-test-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(fixture, """
            {
              "tag_name": "v1.0.10",
              "name": "NEXMC 1.0.10",
              "body": "old",
              "assets": [
                {
                  "name": "NEXMC-Setup-1.0.10.exe",
                  "browser_download_url": "https://github.com/username-xusir/nexusmc/releases/download/v1.0.10/NEXMC-Setup-1.0.10.exe"
                }
              ]
            }
            """);

        try
        {
            UpdateCheckResult result;
            lock (EnvironmentLock)
            {
                Environment.SetEnvironmentVariable("NEXMC_UPDATE_TEST_RELEASE", fixture);
                try
                {
                    result = new UpdateService().CheckAsync(new Version(1, 0, 10)).GetAwaiter().GetResult();
                }
                finally
                {
                    Environment.SetEnvironmentVariable("NEXMC_UPDATE_TEST_RELEASE", null);
                }
            }

            Assert.False(result.HasUpdate);
            Assert.Contains("最新版本", result.Message);
        }
        finally
        {
            File.Delete(fixture);
        }
    }

    [Fact]
    public void ForbiddenResponseReportsRateLimitResetTime()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", "1795172109");

        var message = UpdateService.DescribeHttpFailure(response);

        Assert.Contains("配额已用完", message);
        Assert.Contains("恢复", message);
    }

    [Fact]
    public void TooManyRequestsHonorsRetryAfter()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));

        var message = UpdateService.DescribeHttpFailure(response);

        Assert.Contains("配额已用完", message);
        Assert.Contains("恢复", message);
    }
}
