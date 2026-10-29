using System.IO;

namespace NEXMC.Services;

public static class InstallInputValidator
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string ValidateInstanceName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        var error = GetInstanceNameError(name);
        if (error is not null)
        {
            throw new ArgumentException(error);
        }

        return name;
    }

    public static string? GetInstanceNameError(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return "请输入实例名称。";
        }

        if (name is "." or ".." || name.Contains("..", StringComparison.Ordinal))
        {
            return "实例名称不能是“.”、“..”或包含“..”。";
        }

        if (name.Any(character => char.IsControl(character) || Path.GetInvalidFileNameChars().Contains(character)))
        {
            return "实例名称包含路径分隔符或其他 Windows 不允许的字符。";
        }

        if (name.EndsWith(' ') || name.EndsWith('.'))
        {
            return "实例名称不能以空格或句点结尾。";
        }

        var baseName = name.Split('.')[0];
        if (ReservedNames.Contains(baseName))
        {
            return "实例名称不能使用 Windows 保留设备名。";
        }

        return null;
    }

    public static string ValidateTargetDirectory(string? value, string targetName)
    {
        var directory = value?.Trim() ?? string.Empty;
        if (directory.Length == 0)
        {
            throw new ArgumentException($"请先在“设置”页填写 {targetName} 的 .minecraft 目录。");
        }

        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException($"{targetName} 目录必须是完整的绝对路径。");
        }

        return directory;
    }

    public static IReadOnlyList<string> ValidateTargets(IReadOnlyList<LauncherOption>? targets)
    {
        if (targets is null || targets.Count == 0)
        {
            throw new ArgumentException("请选择至少一个安装目标启动器。");
        }

        var directories = new List<string>(targets.Count);
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            if (target is null)
            {
                throw new ArgumentException("安装目标不能为空。");
            }

            var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                ValidateTargetDirectory(target.Directory, target.Name)));
            if (!unique.Add(fullDirectory))
            {
                throw new ArgumentException($"安装目标目录重复：{target.Directory}。请只选择一个对应目标。");
            }

            directories.Add(fullDirectory);
        }

        return directories;
    }
}
