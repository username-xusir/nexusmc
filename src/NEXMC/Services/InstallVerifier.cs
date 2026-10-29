using System.IO;
using System.Text.Json;

namespace NEXMC.Services;

public static class InstallVerifier
{
    public static void Verify(string instanceName, string targetRoot)
    {
        var name = InstallInputValidator.ValidateInstanceName(instanceName);
        var root = InstallInputValidator.ValidateTargetDirectory(targetRoot, "安装目标");
        var versionDirectory = Path.Combine(root, "versions", name);
        var jsonPath = Path.Combine(versionDirectory, name + ".json");
        if (!File.Exists(jsonPath))
        {
            throw new InvalidOperationException($"安装校验失败：版本 JSON 不存在（{jsonPath}）。");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var rootElement = document.RootElement;
        if (!rootElement.TryGetProperty("id", out var id) || !string.Equals(id.GetString(), name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"安装校验失败：版本 JSON 的 id 不是 {name}。");
        }
        if (!rootElement.TryGetProperty("mainClass", out var mainClass) || string.IsNullOrWhiteSpace(mainClass.GetString()))
        {
            throw new InvalidOperationException("安装校验失败：版本 JSON 缺少 mainClass。");
        }

        var jarPath = Path.Combine(versionDirectory, name + ".jar");
        if (!File.Exists(jarPath) || new FileInfo(jarPath).Length == 0)
        {
            throw new InvalidOperationException($"安装校验失败：客户端 JAR 不存在或为空（{jarPath}）。");
        }

        var libraries = Path.Combine(root, "libraries");
        if (!Directory.Exists(libraries) || !Directory.EnumerateFiles(libraries, "*", SearchOption.AllDirectories).Any())
        {
            throw new InvalidOperationException("安装校验失败：libraries 目录为空。");
        }

        var assetIndexes = Path.Combine(root, "assets", "indexes");
        if (!Directory.Exists(assetIndexes) || !Directory.EnumerateFiles(assetIndexes, "*.json").Any())
        {
            throw new InvalidOperationException("安装校验失败：assets/indexes 中没有资源索引。");
        }
    }
}
