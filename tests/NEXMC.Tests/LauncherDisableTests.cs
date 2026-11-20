using System.IO;
using NEXMC.Services;
using Xunit;

namespace NEXMC.Tests;

public class LauncherDisableTests
{
    [Fact]
    public void DisabledLauncherIsHiddenFromDetection()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nexmc-launcher-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new UiSettings();
            settings.CustomLaunchers.Add(new CustomLauncherSetting
            {
                Name = "Test Launcher",
                Directory = directory,
                ExecutablePath = string.Empty
            });

            var enabled = LauncherDetector.Detect(settings);
            Assert.Contains(enabled, item =>
                string.Equals(item.Directory, directory, StringComparison.OrdinalIgnoreCase));

            settings.DisabledLaunchers.Add(directory);
            var disabled = LauncherDetector.Detect(settings);
            Assert.DoesNotContain(disabled, item =>
                string.Equals(item.Directory, directory, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }
}
