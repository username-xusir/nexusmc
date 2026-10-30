namespace NEXMC.Services;

/// <summary>把 BBSMC 验证窗口下载好的本地包转换成一次新的安装请求。</summary>
public static class InstallRetryRequestFactory
{
    public static LauncherOption[] SelectTargets(InstallRequest request, IEnumerable<string>? failedTargetIds)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ids = failedTargetIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = ids is { Count: > 0 }
            ? request.Targets.Where(target => ids.Contains(target.Id)).ToArray()
            : request.Targets.ToArray();

        // 如果任务没有保留失败目标信息，宁可重试全部目标，也不要把任务丢掉。
        return targets.Length > 0 ? targets : request.Targets.ToArray();
    }

    public static InstallRequest CreateLocalRetry(
        InstallRequest request,
        string downloadedPath,
        IEnumerable<string>? failedTargetIds)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadedPath);

        return request with
        {
            ModpackProjectId = null,
            ModpackVersionId = null,
            LocalModpackPath = downloadedPath,
            Targets = SelectTargets(request, failedTargetIds)
        };
    }
}
