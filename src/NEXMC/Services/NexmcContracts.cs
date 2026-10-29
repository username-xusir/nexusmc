namespace NEXMC.Services;

public enum LauncherKind { PCL2, HMCL, Generic }

public static class ModpackSources
{
    public const string All = "全部来源";
    public static readonly string[] Individual = { "BBSMC", "Modrinth", "CurseForge" };

    public static bool IsAll(string? source) =>
        string.Equals(source?.Trim(), All, StringComparison.OrdinalIgnoreCase);

    public static bool IsKnown(string? source) =>
        Individual.Contains(source?.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>把逗号分隔或「全部来源」解析为去重后的已知来源列表。</summary>
    public static IReadOnlyList<string> Parse(string? source)
    {
        if (IsAll(source))
        {
            return Individual.ToArray();
        }

        return (source ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsKnown)
            .Select(item => Individual.First(known => known.Equals(item, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string Join(IEnumerable<string>? sources)
    {
        var selected = (sources ?? Array.Empty<string>())
            .Where(IsKnown)
            .Select(item => Individual.First(known => known.Equals(item, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return selected.Length == Individual.Length ? All : string.Join(",", selected);
    }

    public static string Display(IReadOnlyCollection<string>? sources)
    {
        if (sources is null || sources.Count == 0) return "未选来源";
        if (sources.Count == Individual.Length) return "全部来源";
        if (sources.Count == 1) return sources.First();
        return $"{sources.Count} 个来源";
    }
}

public sealed record LauncherOption(
    string Id,
    string Name,
    LauncherKind Kind,
    string Directory,
    string? ExecutablePath,
    bool AutoDetected)
{
    public string? LauncherVersion { get; init; }
    public int InstalledVersionCount { get; init; }
    public bool IsCustom { get; init; }
    public bool IsDownloadTarget { get; init; }
    public string DirectoryDisplay => Directory;
    public string LauncherVersionText => string.IsNullOrWhiteSpace(LauncherVersion) ? "未设置 EXE" : LauncherVersion;
    /// <summary>通用显示：只显示启动器名称和已安装版本数量，不拼接任何本机目录名。</summary>
    public string DisplayLabel => IsDownloadTarget ? Name : $"{Name} · {InstalledVersionCount} 个版本";
    public override string ToString() => $"{DisplayLabel} · {Directory}";
}

public sealed record LauncherChoice(string Name, IReadOnlyList<LauncherOption> Targets)
{
    public string DisplayLabel => Name;
    public string DirectoryDisplay => Targets.Count == 1
        ? Targets[0].Directory
        : $"{Targets.Count} 个独立安装目录";
    public override string ToString() => Name;
}

public sealed record MinecraftVersion(string Id, string Type, string ReleaseTime, string Category);

public sealed record LoaderSelection(string Kind, string Version);

public sealed record ModpackProject(
    string Id,
    string Name,
    string Description,
    string Source,
    string IconUrl,
    long Downloads,
    string ProjectUrl,
    IReadOnlyList<string> Tags)
{
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "N" : Name.Trim()[..1].ToUpperInvariant();
    public string TagSummary => string.Join(" · ", Tags.Take(4));
    public string SourceAndDownloads => $"{Source} · 下载量 {Downloads:N0}";
}

public sealed record ModpackSearchPage(IReadOnlyList<ModpackProject> Projects, int TotalHits, int Offset)
{
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record ModpackSourceChunk(
    string Source,
    IReadOnlyList<ModpackProject> Projects,
    int TotalHits,
    string? Warning);

public sealed record ModpackVersion(string Id, string Display, string GameVersion, string Source, bool IsDirectDownload)
{
    public string ExternalUrl { get; init; } = string.Empty;
    public bool HasExternalUrl => !string.IsNullOrWhiteSpace(ExternalUrl);
    public string InstallModeText => IsDirectDownload ? "直链可用" : HasExternalUrl ? "仅网盘 · 可打开网盘" : "仅网盘 · 无链接";
}

public sealed record InstallRequest(
    string InstanceName,
    string MinecraftVersion,
    string? LoaderKind,
    string? LoaderVersion,
    string? ModpackProjectId,
    string? ModpackVersionId,
    string? LocalModpackPath,
    IReadOnlyList<LauncherOption> Targets,
    bool DownloadOnly = false,
    string? DownloadDirectory = null);

public sealed record TargetProgress(string Target, double Progress, string Message, bool? Success);

public interface INexmcService
{
    Task<IReadOnlyList<LauncherOption>> DetectLaunchersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MinecraftVersion>> GetMinecraftVersionsAsync(bool includeSnapshots, CancellationToken ct = default);
    Task<IReadOnlyList<LoaderSelection>> GetLoadersAsync(string minecraftVersion, string kind, CancellationToken ct = default);
    Task<ModpackSearchPage> SearchModpacksAsync(string query, string source, string? gameVersion, string? category, int offset, int limit, CancellationToken ct = default);
    Task<ModpackSearchPage> SearchModpacksAsync(string query, IReadOnlyList<string> sources, string? gameVersion, string? category, int offset, int limit, CancellationToken ct = default);
    IAsyncEnumerable<ModpackSourceChunk> SearchAllModpacksIncrementalAsync(string query, string? gameVersion, string? category, int offset, int limit, CancellationToken ct = default);
    IAsyncEnumerable<ModpackSourceChunk> SearchAllModpacksIncrementalAsync(string query, IReadOnlyList<string> sources, string? gameVersion, string? category, int offset, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<ModpackVersion>> GetModpackVersionsAsync(string projectId, string source, CancellationToken ct = default);
    Task InstallAsync(InstallRequest request, IProgress<TargetProgress> progress, CancellationToken ct = default);
}
