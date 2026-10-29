using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace NEXMC.Services;

public static class LauncherDetector
{
    public static IReadOnlyList<LauncherOption> Detect(UiSettings settings)
    {
        var results = new List<LauncherOption>();
        var indexByDirectory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appDataMinecraft = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        void Add(string? name, string? directory, string? executable, bool autoDetected,
            bool allowMissingDirectory = false, bool isCustom = false)
        {
            string? fullDirectory = null;
            if (!string.IsNullOrWhiteSpace(directory))
            {
                try { fullDirectory = Path.GetFullPath(directory); }
                catch { return; }
            }

            string? fullExecutable = null;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                try { fullExecutable = Path.GetFullPath(executable); }
                catch { fullExecutable = null; }
            }

            if (string.IsNullOrWhiteSpace(fullDirectory))
            {
                fullDirectory = FindMinecraftDirectory(fullExecutable);
            }
            if (string.IsNullOrWhiteSpace(fullDirectory))
            {
                return;
            }

            var directoryExists = Directory.Exists(fullDirectory);
            if (!allowMissingDirectory && !directoryExists &&
                (fullExecutable is null || !File.Exists(fullExecutable)))
            {
                return;
            }

            var displayName = string.IsNullOrWhiteSpace(name)
                ? DeriveLauncherName(fullExecutable, fullDirectory)
                : name.Trim();
            if (!directoryExists)
            {
                displayName += "（目录待创建）";
            }

            if (indexByDirectory.TryGetValue(fullDirectory, out var existingIndex))
            {
                var existing = results[existingIndex];
                var preferNew = isCustom && !existing.IsCustom ||
                                (isCustom == existing.IsCustom &&
                                 existing.ExecutablePath is null && fullExecutable is not null);
                var mergedExecutable = existing.ExecutablePath ?? fullExecutable;
                var mergedName = preferNew ? displayName : existing.Name;
                if (!preferNew && !isCustom && !existing.IsCustom &&
                    !mergedName.Contains(displayName, StringComparison.CurrentCultureIgnoreCase))
                {
                    mergedName = $"{mergedName} / {displayName}";
                }

                results[existingIndex] = existing with
                {
                    Name = mergedName,
                    ExecutablePath = mergedExecutable,
                    LauncherVersion = existing.LauncherVersion ?? GetLauncherVersion(mergedExecutable),
                    InstalledVersionCount = CountInstalledVersions(fullDirectory),
                    IsCustom = existing.IsCustom || isCustom,
                    AutoDetected = existing.AutoDetected && autoDetected
                };
                return;
            }

            indexByDirectory[fullDirectory] = results.Count;
            results.Add(new LauncherOption($"launcher-{results.Count}", displayName, LauncherKind.Generic,
                fullDirectory, fullExecutable, autoDetected)
            {
                LauncherVersion = GetLauncherVersion(fullExecutable),
                InstalledVersionCount = CountInstalledVersions(fullDirectory),
                IsCustom = isCustom
            });
        }

        // 用户手动添加的目录优先，允许目录暂时不存在；安装时由内核按标准 .minecraft 结构创建。
        foreach (var custom in settings.CustomLaunchers)
        {
            if (custom is null) continue;
            Add(custom.Name, custom.Directory, custom.ExecutablePath, autoDetected: false,
                allowMissingDirectory: true, isCustom: true);
        }

        // 只有检测到真实启动器程序时才加入列表；普通 .minecraft 目录不再伪装成“版本未知”的启动器。
        // 用户仍可通过“添加启动器”手动加入任意标准目录。

        var candidates = new List<string>();
        foreach (var root in new[]
                 {
                     desktop,
                     Path.Combine(home, "Downloads"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
                 })
        {
            candidates.AddRange(FindExecutables(root, maxDepth: 2));
        }

        foreach (var executable in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var beside = FindMinecraftDirectory(executable);
            var fallback = beside ?? appDataMinecraft;
            Add(DeriveLauncherName(executable, fallback), fallback, executable, autoDetected: true);
        }

        // 不按本机目录名给启动器加后缀；两个 PCL 都显示为 PCL，靠目录提示和版本数量区分。
        return results
            .OrderByDescending(item => item.IsCustom)
            .ThenBy(item => item.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>从 EXE 或目录推导启动器显示名，不改变启动器自身文件。</summary>
    public static string DeriveLauncherName(string? executablePath, string? directory)
    {
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var fileName = Path.GetFileNameWithoutExtension(executablePath);
            if (fileName.Contains("Plain Craft Launcher", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("PCL", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("PCL", StringComparison.OrdinalIgnoreCase))
            {
                return "PCL";
            }
            if (fileName.Contains("HMCL", StringComparison.OrdinalIgnoreCase)) return "HMCL";
            if (fileName.Contains("BakaXL", StringComparison.OrdinalIgnoreCase)) return "BakaXL";
            if (fileName.Contains("Prism", StringComparison.OrdinalIgnoreCase)) return "Prism Launcher";
            if (fileName.Contains("MultiMC", StringComparison.OrdinalIgnoreCase)) return "MultiMC";
            if (fileName.Contains("MinecraftLauncher", StringComparison.OrdinalIgnoreCase)) return "Minecraft Launcher";

            var cleaned = CleanLauncherName(fileName);
            if (!string.IsNullOrWhiteSpace(cleaned)) return cleaned;
        }

        if (!string.IsNullOrWhiteSpace(directory))
        {
            var trimmed = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Path.GetFileName(trimmed).Equals(".minecraft", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = Path.GetDirectoryName(trimmed) ?? trimmed;
            }
            var cleaned = CleanLauncherName(Path.GetFileName(trimmed));
            if (!string.IsNullOrWhiteSpace(cleaned)) return cleaned;
        }

        return "自定义启动器";
    }

    public static string? FindMinecraftDirectory(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        var folder = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(folder)) return null;

        var beside = Path.Combine(folder, ".minecraft");
        if (Directory.Exists(beside)) return Path.GetFullPath(beside);
        if (Directory.Exists(Path.Combine(folder, "versions"))) return Path.GetFullPath(folder);

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        return Directory.Exists(appData) ? Path.GetFullPath(appData) : null;
    }

    private static IEnumerable<string> FindExecutables(string root, int maxDepth)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Queue<(string Directory, int Depth)>();
        pending.Enqueue((root, 0));
        var scanned = 0;
        while (pending.Count > 0 && scanned < 3000)
        {
            var (directory, depth) = pending.Dequeue();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.EnumerateFiles(directory, "*.jar", SearchOption.TopDirectoryOnly))
                    .ToArray();
            }
            catch { continue; }
            foreach (var file in files)
            {
                scanned++;
                if (IsLauncherExecutable(file)) yield return file;
            }
            if (depth >= maxDepth) continue;
            string[] children;
            try { children = Directory.GetDirectories(directory); }
            catch { continue; }
            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("packages", StringComparison.OrdinalIgnoreCase)) continue;
                pending.Enqueue((child, depth + 1));
            }
        }
    }

    private static bool IsLauncherExecutable(string path)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jar", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        return name.Contains("HMCL", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PCL", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Plain Craft Launcher", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("BakaXL", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Prism", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("MultiMC", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("MinecraftLauncher", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("TLauncher", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("SKLauncher", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("ATLauncher", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("GDLauncher", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Modrinth", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("CurseForge", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanLauncherName(string value)
    {
        value = value.Replace(" Launcher", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", " ").Trim();
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    }

    internal static string? GetLauncherVersion(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        // 文件名里的版本号通常更接近用户下载的版本，例如 HMCL-3.16.4.exe。
        var fileVersion = Regex.Match(Path.GetFileNameWithoutExtension(executablePath),
            @"(?<!\d)(\d+\.\d+(?:\.\d+){0,3})(?!\d)");
        if (fileVersion.Success)
        {
            return fileVersion.Groups[1].Value;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            var version = new[] { info.ProductVersion, info.FileVersion }
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && value != "0.0.0.0");
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version.Trim();
            }
        }
        catch
        {
            // 某些第三方启动器没有版本资源。
        }

        return null;
    }

    internal static int CountInstalledVersions(string minecraftDirectory)
    {
        try
        {
            var versions = Path.Combine(minecraftDirectory, "versions");
            if (!Directory.Exists(versions))
            {
                return 0;
            }

            return Directory.EnumerateDirectories(versions)
                .Count(directory => File.Exists(Path.Combine(directory, Path.GetFileName(directory) + ".json")));
        }
        catch
        {
            return 0;
        }
    }
}
