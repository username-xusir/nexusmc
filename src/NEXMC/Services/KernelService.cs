using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using PCL;

namespace NEXMC.Services;

/// <summary>Maps the UI contract to the PCL-backed NEXMC kernel facade.</summary>
public sealed class KernelService : INexmcService
{
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(180);
    private readonly ConcurrentDictionary<string, string> _projectSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSettingsService _settingsService = new();
    private int _installAttempts = InstallRetryPolicy.DefaultMaxAttempts;
    private TimeSpan _retryDelay = InstallRetryPolicy.DefaultRetryDelay;
    private bool _fallbackModpackSources = true;

    public KernelService()
    {
        var settings = _settingsService.Load();
        KernelFacade.Initialize();
        ApplyDownloadSettings(settings);
    }

    public void UpdateSettings(UiSettings settings)
    {
        ApplyDownloadSettings(settings ?? new UiSettings());
    }

    private void ApplyDownloadSettings(UiSettings settings)
    {
        _installAttempts = Math.Clamp(settings.RetryAttempts, 1, 10);
        _retryDelay = TimeSpan.FromSeconds(Math.Clamp(settings.RetryDelaySeconds, 1, 120));
        _fallbackModpackSources = settings.FallbackModpackSources;

        var (downloadSource, versionSource) = settings.GameDownloadSource?.Trim().ToLowerInvariant() switch
        {
            "official" => (2, 2),
            "mirror" => (0, 0),
            // 自动模式优先官方源：BMCLAPI 对新版本资源可能返回 404/302，
            // 而内核在资源下载失败后不一定能可靠切回官方源。
            _ => (2, 2)
        };
        var speedSetting = settings.DownloadSpeedLimitMb switch
        {
            <= 0 => 42,
            <= 5 => 21,
            <= 10 => 31,
            <= 20 => 41,
            _ => 42
        };
        KernelFacade.ApplyDownloadPreferences(downloadSource, versionSource,
            Math.Clamp(settings.DownloadThreads, 1, 64), speedSetting);
        PCL.Settings.Set("ToolDownloadMod", 0); // CurseForge 不要求 API Key，统一走 MCIMirror。
    }

    public async Task<IReadOnlyList<LauncherOption>> DetectLaunchersAsync(CancellationToken ct = default)
    {
        var settings = _settingsService.Load();
        return await Task.Run(() => LauncherDetector.Detect(settings), ct);
    }

    public async Task<IReadOnlyList<MinecraftVersion>> GetMinecraftVersionsAsync(bool includeSnapshots, CancellationToken ct = default)
    {
        return await Task.Run(() => KernelFacade.GetMinecraftVersions(includeSnapshots)
            .Select(v => new MinecraftVersion(v.Id, v.Type, v.ReleaseTime, MinecraftVersionClassifier.Classify(v.Id, v.Type))).ToList(), ct);
    }

    public async Task<IReadOnlyList<LoaderSelection>> GetLoadersAsync(string minecraftVersion, string kind, CancellationToken ct = default)
    {
        return await Task.Run(() => KernelFacade.GetLoaders(minecraftVersion, kind)
            .Select(v => new LoaderSelection(v.Kind, v.Version)).ToList(), ct);
    }

    public Task<ModpackSearchPage> SearchModpacksAsync(string query, string source, string? gameVersion, string? category, int offset, int limit, CancellationToken ct = default)
        => SearchModpacksAsync(query, ModpackSources.Parse(source), gameVersion, category, offset, limit, ct);

    public async Task<ModpackSearchPage> SearchModpacksAsync(string query, IReadOnlyList<string> sources, string? gameVersion, string? category, int offset, int limit, CancellationToken ct = default)
    {
        var selected = NormalizeSources(sources);
        if (selected.Count == 0)
        {
            throw new ArgumentException("请至少选择一个整合包来源。");
        }

        if (selected.Count == 1)
        {
            var source = selected[0];
            try
            {
                var page = await Task.Run(() => KernelFacade.SearchModpacksPage(query, source, gameVersion ?? string.Empty,
                    category ?? string.Empty, offset, limit), ct);
                var result = page.Projects.Select(p => new ModpackProject(p.Id, p.Name, p.Description, p.Source,
                    p.IconUrl ?? string.Empty, p.Downloads, p.Website ?? string.Empty, p.Tags ?? new List<string>())).ToList();
                foreach (var project in result) _projectSources[project.Id] = project.Source;
                return new ModpackSearchPage(result, page.TotalHits, offset + result.Count);
            }
            catch (Exception exception) when (_fallbackModpackSources && exception is not OperationCanceledException)
            {
                // 不回退到用户未勾选的来源，避免“排除 BBSMC”后又被偷偷查回来。
                return new ModpackSearchPage(Array.Empty<ModpackProject>(), 0, offset)
                {
                    Warnings = new[] { $"{source}: {exception.GetBaseException().Message}" }
                };
            }
        }

        return await Task.Run(() => SearchSelectedModpacks(query, selected, gameVersion, category, offset, limit), ct);
    }

    public IAsyncEnumerable<ModpackSourceChunk> SearchAllModpacksIncrementalAsync(
        string query, string? gameVersion, string? category, int offset, int limit,
        CancellationToken ct = default)
        => SearchAllModpacksIncrementalAsync(query, ModpackSources.Individual, gameVersion, category, offset, limit, ct);

    public async IAsyncEnumerable<ModpackSourceChunk> SearchAllModpacksIncrementalAsync(
        string query, IReadOnlyList<string> sources, string? gameVersion, string? category, int offset, int limit,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var selected = NormalizeSources(sources);
        if (selected.Count == 0)
        {
            yield return new ModpackSourceChunk("未选来源", Array.Empty<ModpackProject>(), 0, "请至少勾选一个来源。");
            yield break;
        }

        var perSourceLimit = Math.Max(10, (int)Math.Ceiling(limit / (double)selected.Count));
        var pending = new List<Task<ModpackSourceChunk>>();
        foreach (var source in selected)
        {
            pending.Add(Task.Run(() =>
            {
                try
                {
                    var page = KernelFacade.SearchModpacksPage(query, source, gameVersion ?? string.Empty,
                        category ?? string.Empty, offset, perSourceLimit);
                    var projects = page.Projects.Select(p => new ModpackProject(p.Id, p.Name, p.Description, p.Source,
                        p.IconUrl ?? string.Empty, p.Downloads, p.Website ?? string.Empty,
                        p.Tags ?? new List<string>())).ToList();
                    return new ModpackSourceChunk(source, projects, page.TotalHits, null);
                }
                catch (Exception exception)
                {
                    return new ModpackSourceChunk(source, Array.Empty<ModpackProject>(), 0,
                        $"{source}: {exception.GetBaseException().Message}");
                }
            }, ct));
        }

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var completed = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(completed);
            var chunk = await completed.ConfigureAwait(false);
            foreach (var project in chunk.Projects)
            {
                _projectSources[project.Id] = project.Source;
            }
            yield return chunk;
        }
    }

    private static IReadOnlyList<string> NormalizeSources(IEnumerable<string>? sources)
    {
        var selected = (sources ?? Array.Empty<string>())
            .Where(ModpackSources.IsKnown)
            .Select(item => ModpackSources.Individual.First(known =>
                known.Equals(item?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return selected;
    }

    private ModpackSearchPage SearchSelectedModpacks(string query, IReadOnlyList<string> sources,
        string? gameVersion, string? category, int offset, int limit)
    {
        var perSourceLimit = Math.Max(10, (int)Math.Ceiling(limit / (double)sources.Count));
        var projects = new List<ModpackProject>();
        var warnings = new List<string>();
        var totalHits = 0;

        foreach (var source in sources)
        {
            try
            {
                var page = KernelFacade.SearchModpacksPage(query, source, gameVersion ?? string.Empty,
                    category ?? string.Empty, offset, perSourceLimit);
                totalHits += page.TotalHits;
                projects.AddRange(page.Projects.Select(p => new ModpackProject(p.Id, p.Name, p.Description, p.Source,
                    p.IconUrl ?? string.Empty, p.Downloads, p.Website ?? string.Empty, p.Tags ?? new List<string>())));
            }
            catch (Exception exception)
            {
                warnings.Add($"{source}: {exception.GetBaseException().Message}");
            }
        }

        if (projects.Count == 0 && warnings.Count == sources.Count)
        {
            throw new InvalidOperationException("所选整合包来源均失败：" + string.Join(" | ", warnings));
        }

        var result = projects
            .GroupBy(project => project.Source + "|" + project.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(project => project.Downloads)
            .ThenBy(project => project.Name, StringComparer.CurrentCulture)
            .ToList();
        foreach (var project in result) _projectSources[project.Id] = project.Source;
        return new ModpackSearchPage(result, Math.Max(totalHits, result.Count), offset + perSourceLimit)
        {
            Warnings = warnings
        };
    }

    public async Task<IReadOnlyList<ModpackVersion>> GetModpackVersionsAsync(string projectId, string source, CancellationToken ct = default)
    {
        _projectSources[projectId] = source;
        return await Task.Run(() => KernelFacade.GetModpackFiles(projectId, source)
            .Select(v => new ModpackVersion(v.Id, v.Display, v.GameVersion, v.Source, v.IsDirectDownload)
            {
                ExternalUrl = v.ExternalUrl ?? string.Empty
            }).ToList(), ct);
    }

    public async Task InstallAsync(InstallRequest request, IProgress<TargetProgress> progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        InstallInputValidator.ValidateInstanceName(request.InstanceName);
        InstallInputValidator.ValidateTargets(request.Targets);

        var isModpack = !string.IsNullOrWhiteSpace(request.LocalModpackPath) || !string.IsNullOrWhiteSpace(request.ModpackProjectId);
        var source = string.Empty;
        if (isModpack && string.IsNullOrWhiteSpace(request.LocalModpackPath))
        {
            if (string.IsNullOrWhiteSpace(request.ModpackProjectId) || string.IsNullOrWhiteSpace(request.ModpackVersionId))
                throw new ArgumentException("在线整合包缺少项目或版本 ID。");
            if (!_projectSources.TryGetValue(request.ModpackProjectId, out source!))
                throw new InvalidOperationException("整合包来源信息已失效，请重新搜索并选择版本。");
        }

        if (request.DownloadOnly)
        {
            await DownloadOnlyAsync(request, source, isModpack, progress, ct);
            return;
        }

        await Task.Run(async () =>
        {
            var failures = new List<string>();
            foreach (var target in request.Targets)
            {
                ct.ThrowIfCancellationRequested();
                var versionFolder = Path.Combine(target.Directory, "versions", request.InstanceName);
                var hadEntriesBefore = VersionFolderHasEntries(versionFolder);

                try
                {
                    await InstallRetryPolicy.RunAsync(
                        async attempt =>
                        {
                            progress.Report(new TargetProgress(target.Id, 0,
                                attempt == 1
                                    ? $"准备安装到 {target.Directory}"
                                    : $"第 {attempt}/{_installAttempts} 次尝试安装到 {target.Directory}", null));

                            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            var progressTimestamp = DateTime.UtcNow.Ticks;
                            void Report(double value, string message)
                            {
                                Interlocked.Exchange(ref progressTimestamp, DateTime.UtcNow.Ticks);
                                progress.Report(new TargetProgress(target.Id, value, message, null));
                            }

                            var installTask = Task.Run(() =>
                            {
                                if (isModpack)
                                {
                                    KernelFacade.InstallModpack(request.InstanceName, request.ModpackProjectId ?? string.Empty,
                                        request.ModpackVersionId ?? string.Empty, source, request.LocalModpackPath ?? string.Empty,
                                        target.Directory, Report, attemptCancellation.Token);
                                }
                                else
                                {
                                    if (string.IsNullOrWhiteSpace(request.MinecraftVersion))
                                        throw new ArgumentException("Minecraft 版本不能为空。");
                                    KernelFacade.InstallVanilla(request.InstanceName, request.MinecraftVersion,
                                        request.LoaderKind ?? string.Empty, request.LoaderVersion ?? string.Empty,
                                        target.Directory, Report, attemptCancellation.Token);
                                }
                            }, CancellationToken.None);

                            while (!installTask.IsCompleted)
                            {
                                var wait = Task.Delay(TimeSpan.FromSeconds(5), ct);
                                if (await Task.WhenAny(installTask, wait).ConfigureAwait(false) == installTask)
                                {
                                    break;
                                }

                                ct.ThrowIfCancellationRequested();
                                var idle = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref progressTimestamp));
                                if (idle < StallTimeout)
                                {
                                    continue;
                                }

                                // 内核偶尔会卡在无响应的连接上；主动中断，让上层按重试策略重来。
                                attemptCancellation.Cancel();
                                try
                                {
                                    await installTask.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None)
                                        .ConfigureAwait(false);
                                }
                                catch
                                {
                                    // 中断异常由下面的 TimeoutException 统一表达。
                                }
                                throw new TimeoutException(
                                    $"连续 {StallTimeout.TotalSeconds:F0} 秒没有下载进度，已中断卡住的下载。");
                            }

                            await installTask.ConfigureAwait(false);

                            progress.Report(new TargetProgress(target.Id, 1, "正在校验安装文件", null));
                            InstallVerifier.Verify(request.InstanceName, target.Directory);
                            progress.Report(new TargetProgress(target.Id, 1, "安装完成，校验通过", true));
                            return true;
                        },
                        async (attempt, exception) =>
                        {
                            if (!hadEntriesBefore) TryDeleteDirectory(versionFolder);
                            progress.Report(new TargetProgress(target.Id, 0,
                                $"第 {attempt} 次安装失败：{ShortFailure(exception)}；" +
                                $"{_retryDelay.TotalSeconds:F0} 秒后进行第 {attempt + 1}/{_installAttempts} 次尝试。",
                                null));
                            await Task.CompletedTask;
                        },
                        ct,
                        exception => !hadEntriesBefore &&
                                     !InstallRetryPolicy.IsNonRetryable(exception) &&
                                     !BbsmcCaptchaHelper.IsCaptchaFailure(exception.ToString()),
                        _retryDelay,
                        _installAttempts);
                }
                catch (OperationCanceledException)
                {
                    if (!hadEntriesBefore) TryDeleteDirectory(versionFolder);
                    progress.Report(new TargetProgress(target.Id, 0, "已取消", false));
                    throw;
                }
                catch (Exception exception)
                {
                    if (!hadEntriesBefore) TryDeleteDirectory(versionFolder);
                    var failureMessage = BbsmcCaptchaHelper.IsCaptchaFailure(exception.ToString())
                        ? BbsmcCaptchaHelper.BuildShortMessage()
                        : exception.Message;
                    failures.Add($"{target.Name}: {failureMessage}");
                    progress.Report(new TargetProgress(target.Id, 0, failureMessage, false));
                }
            }
            if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        }, ct);
    }

    /// <summary>仅下载到本地：整合包只保存压缩包，原版下载为可携带的本地 .minecraft 目录。</summary>
    private static async Task DownloadOnlyAsync(InstallRequest request, string source, bool isModpack,
        IProgress<TargetProgress> progress, CancellationToken ct)
    {
        var directory = (request.DownloadDirectory ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new ArgumentException("请先在设置里选择“仅下载到本地”的保存文件夹。");
        Directory.CreateDirectory(directory);

        var targetId = request.Targets.FirstOrDefault()?.Id ?? "local-download";
        await Task.Run(async () =>
        {
            try
            {
                progress.Report(new TargetProgress(targetId, 0, "准备下载到本地", null));
                if (isModpack)
                {
                    string archive;
                    if (!string.IsNullOrWhiteSpace(request.LocalModpackPath))
                    {
                        archive = request.LocalModpackPath;
                        if (!File.Exists(archive)) throw new FileNotFoundException("本地整合包文件不存在", archive);
                    }
                    else
                    {
                        archive = KernelFacade.DownloadModpackArchive(request.ModpackProjectId ?? string.Empty,
                            request.ModpackVersionId ?? string.Empty, source,
                            (value, message) => progress.Report(new TargetProgress(targetId, value * 0.9, message, null)), ct);
                    }

                    ct.ThrowIfCancellationRequested();
                    var extension = Path.GetExtension(archive);
                    if (string.IsNullOrWhiteSpace(extension)) extension = ".mrpack";
                    var destination = UniquePath(Path.Combine(directory, request.InstanceName + extension));
                    progress.Report(new TargetProgress(targetId, 0.95, "正在保存整合包文件", null));
                    File.Copy(archive, destination, overwrite: false);
                    progress.Report(new TargetProgress(targetId, 1, $"已保存：{destination}", true));
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(request.MinecraftVersion))
                        throw new ArgumentException("Minecraft 版本不能为空。");
                    var root = Path.Combine(directory, request.InstanceName);
                    Directory.CreateDirectory(root);
                    KernelFacade.InstallVanilla(request.InstanceName, request.MinecraftVersion,
                        request.LoaderKind ?? string.Empty, request.LoaderVersion ?? string.Empty, root,
                        (value, message) => progress.Report(new TargetProgress(targetId, value, message, null)), ct);
                    ct.ThrowIfCancellationRequested();
                    InstallVerifier.Verify(request.InstanceName, root);
                    progress.Report(new TargetProgress(targetId, 1,
                        $"已下载到本地：{Path.Combine(root, "versions", request.InstanceName)}", true));
                }
            }
            catch (OperationCanceledException)
            {
                progress.Report(new TargetProgress(targetId, 0, "已取消", false));
                throw;
            }
            catch (Exception exception)
            {
                var message = BbsmcCaptchaHelper.IsCaptchaFailure(exception.ToString())
                    ? BbsmcCaptchaHelper.BuildShortMessage()
                    : exception.Message;
                progress.Report(new TargetProgress(targetId, 0, message, false));
                throw;
            }
        }, ct);
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name}-{index}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private static string ShortFailure(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message.Length <= 160 ? message : message[..157] + "...";
    }

    private static bool VersionFolderHasEntries(string path)
    {
        try
        {
            return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch
        {
            // 无法读取时按“已有内容”处理，避免误删用户目录。
            return true;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // 清理失败时仍让下一次尝试进行，内核会再次检查目录状态并给出真实错误。
        }
    }
}
