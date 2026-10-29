using System.Text.Json;
using NEXMC.Services;
using Xunit;

namespace NEXMC.Tests;

public sealed class InstallVerifierTests
{
    [Fact]
    public void CompleteInstallLayoutPasses()
    {
        var root = CreateRoot();
        try
        {
            var version = Path.Combine(root, "versions", "test-pack");
            Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version, "test-pack.json"),
                JsonSerializer.Serialize(new { id = "test-pack", mainClass = "net.minecraft.client.main.Main" }));
            File.WriteAllBytes(Path.Combine(version, "test-pack.jar"), new byte[] { 1, 2, 3 });
            Directory.CreateDirectory(Path.Combine(root, "libraries", "org", "example"));
            File.WriteAllText(Path.Combine(root, "libraries", "org", "example", "lib.jar"), "x");
            Directory.CreateDirectory(Path.Combine(root, "assets", "indexes"));
            File.WriteAllText(Path.Combine(root, "assets", "indexes", "1.21.json"), "{}");

            InstallVerifier.Verify("test-pack", root);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void MissingJarFailsVerification()
    {
        var root = CreateRoot();
        try
        {
            var version = Path.Combine(root, "versions", "test-pack");
            Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version, "test-pack.json"),
                JsonSerializer.Serialize(new { id = "test-pack", mainClass = "net.minecraft.client.main.Main" }));
            Directory.CreateDirectory(Path.Combine(root, "libraries"));
            File.WriteAllText(Path.Combine(root, "libraries", "lib.jar"), "x");
            Directory.CreateDirectory(Path.Combine(root, "assets", "indexes"));
            File.WriteAllText(Path.Combine(root, "assets", "indexes", "1.21.json"), "{}");

            var exception = Assert.Throws<InvalidOperationException>(() => InstallVerifier.Verify("test-pack", root));
            Assert.Contains("JAR", exception.Message);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "NEXMC-Verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响测试结论。
        }
    }
}
