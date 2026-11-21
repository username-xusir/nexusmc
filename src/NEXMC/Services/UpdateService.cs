using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace NEXMC.Services;

public sealed record UpdateInfo(
    string Version,
    string TagName,
    string Title,
    string Body,
    string InstallerUrl,
    string InstallerName,
    string Sha256Url,
    string Sha256,
    string? BannerUrl,
    DateTimeOffset? PublishedAt);

public sealed record UpdateCheckResult(
    bool HasUpdate,
    UpdateInfo? Update,
    string Message,
    bool IsError = false);

public enum ReleaseNoteKind { Heading, Bullet, Body, Blank, Code }

public sealed record ReleaseNoteLine(string Text, ReleaseNoteKind Kind);

/// <summary>
/// 读取 GitHub Release 并下载安装包。只使用公开仓库，不需要服务器。
/// </summary>
public sealed class UpdateService
{
    public const string Repository = "username-xusir/nexusmc";
    private const string LatestReleaseUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    private const string CacheFileName = "update-cache.json";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private static readonly HttpClient ApiClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly HttpClient DownloadClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    static UpdateService()
    {
        ApiClient.DefaultRequestHeaders.UserAgent.ParseAdd("NEXMC-AutoUpdate");
        ApiClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        ApiClient.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        DownloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("NEXMC-AutoUpdate");
    }

    public static Version GetCurrentVersion()
    {
        var overrideValue = Environment.GetEnvironmentVariable("NEXMC_UPDATE_TEST_CURRENT");
        if (Version.TryParse(overrideValue, out var testVersion)) return Normalize(testVersion);

        var assembly = typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);
        return Normalize(assembly);
    }

    public Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default) =>
        CheckAsync(currentVersion, false, cancellationToken);

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, bool force,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string json;
            var fixture = Environment.GetEnvironmentVariable("NEXMC_UPDATE_TEST_RELEASE");
            if (!string.IsNullOrWhiteSpace(fixture) && File.Exists(fixture))
            {
                json = await File.ReadAllTextAsync(fixture, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var cache = LoadCache();
                var now = DateTimeOffset.UtcNow;
                if (!force && cache is not null && now - cache.CheckedAt < CacheTtl)
                {
                    json = cache.Json;
                }
                else
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
                    if (cache is not null && !string.IsNullOrWhiteSpace(cache.ETag))
                    {
                        request.Headers.TryAddWithoutValidation("If-None-Match", cache.ETag);
                    }

                    using var response = await ApiClient.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.NotModified && cache is not null)
                    {
                        json = cache.Json;
                        cache.CheckedAt = now;
                        SaveCache(cache);
                    }
                    else if (!response.IsSuccessStatusCode)
                    {
                        return new UpdateCheckResult(false, null, DescribeHttpFailure(response), true);
                    }
                    else
                    {
                        json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        SaveCache(new UpdateCache
                        {
                            Json = json,
                            ETag = response.Headers.ETag?.Tag ?? "",
                            CheckedAt = now
                        });
                    }
                }
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? "" : "";
            if (!TryParseVersion(tag, out var latestVersion))
            {
                return new UpdateCheckResult(false, null, "最新 Release 没有可识别的版本号。", true);
            }

            if (latestVersion <= Normalize(currentVersion))
            {
                return new UpdateCheckResult(false, null,
                    $"当前已是最新版本 {Normalize(currentVersion).ToString(3)}。");
            }

            var title = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : tag;
            var body = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? "" : "";
            var publishedAt = root.TryGetProperty("published_at", out var publishedElement) &&
                              DateTimeOffset.TryParse(publishedElement.GetString(), out var published)
                ? published
                : (DateTimeOffset?)null;

            var assets = new List<GitHubAsset>();
            if (root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsElement.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(name) && Uri.TryCreate(url, UriKind.Absolute, out _))
                    {
                        assets.Add(new GitHubAsset(name, url));
                    }
                }
            }

            var versionText = latestVersion.ToString(3);
            var installer = assets.FirstOrDefault(asset =>
                                asset.Name.StartsWith("NEXMC-Setup-", StringComparison.OrdinalIgnoreCase) &&
                                asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                asset.Name.Contains(versionText, StringComparison.OrdinalIgnoreCase))
                            ?? assets.FirstOrDefault(asset =>
                                asset.Name.StartsWith("NEXMC-Setup-", StringComparison.OrdinalIgnoreCase) &&
                                asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (installer is null)
            {
                return new UpdateCheckResult(false, null, "最新 Release 没有找到 NEXMC 安装包。", true);
            }

            var shaAsset = assets.FirstOrDefault(asset =>
                asset.Name.EndsWith(".sha256.txt", StringComparison.OrdinalIgnoreCase) &&
                (asset.Name.Contains(versionText, StringComparison.OrdinalIgnoreCase) ||
                 asset.Name.StartsWith("NEXMC-Setup-", StringComparison.OrdinalIgnoreCase)));
            var sha = "";
            if (shaAsset is not null)
            {
                try
                {
                    var text = await ApiClient.GetStringAsync(shaAsset.Url, cancellationToken).ConfigureAwait(false);
                    sha = ExtractSha256(text);
                }
                catch
                {
                    // 缺少校验文件时仍然允许显示更新，但安装前会再次要求校验。
                }
            }

            var banner = assets.FirstOrDefault(asset =>
                             asset.Name.Contains("banner", StringComparison.OrdinalIgnoreCase) &&
                             asset.Name.Contains("dark", StringComparison.OrdinalIgnoreCase) &&
                             new[] { ".png", ".jpg", ".jpeg", ".webp" }.Any(extension =>
                                 asset.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                         ?? assets.FirstOrDefault(asset =>
                             asset.Name.Contains("banner", StringComparison.OrdinalIgnoreCase) &&
                             new[] { ".png", ".jpg", ".jpeg", ".webp" }.Any(extension =>
                                 asset.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)));
            var bannerUrl = banner?.Url;
            if (string.IsNullOrWhiteSpace(bannerUrl))
            {
                bannerUrl = BuildFallbackBannerUrl(tag, versionText);
            }

            var info = new UpdateInfo(
                versionText,
                tag,
                string.IsNullOrWhiteSpace(title) ? $"NEXMC {versionText}" : title,
                body,
                installer.Url,
                installer.Name,
                shaAsset?.Url ?? "",
                sha,
                bannerUrl,
                publishedAt);

            return new UpdateCheckResult(true, info, $"发现新版本 {versionText}。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new UpdateCheckResult(false, null,
                "检查更新失败：" + exception.GetBaseException().Message, true);
        }
    }

    internal static string BuildFallbackBannerUrl(string tag, string version)
    {
        var tagName = string.IsNullOrWhiteSpace(tag) ? "v" + version : tag.Trim();
        return $"https://raw.githubusercontent.com/{Repository}/{Uri.EscapeDataString(tagName)}" +
               $"/assets/updates/{Uri.EscapeDataString(version)}-dark.png";
    }

    internal static string DescribeHttpFailure(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var retryAt = GetRetryAt(response);
            return retryAt is null
                ? "GitHub 暂时限制了更新查询，请稍后再试。"
                : $"GitHub 更新查询配额已用完，将在 {retryAt.Value:HH:mm} 恢复。";
        }

        return $"检查更新失败：HTTP {(int)response.StatusCode}";
    }

    private static DateTimeOffset? GetRetryAt(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return DateTimeOffset.Now + delta;
        }
        if (response.Headers.RetryAfter?.Date is { } date)
        {
            return date.ToLocalTime();
        }
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
            long.TryParse(values.FirstOrDefault(), out var epoch))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epoch).ToLocalTime();
        }
        return null;
    }

    private static string CachePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXMC", CacheFileName);

    private static UpdateCache? LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            var cache = JsonSerializer.Deserialize<UpdateCache>(File.ReadAllText(CachePath));
            return cache is { Json.Length: > 0 } ? cache : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCache(UpdateCache cache)
    {
        try
        {
            var path = CachePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(cache));
            File.Move(temp, path, true);
        }
        catch
        {
            // 缓存失败不影响更新检查。
        }
    }

    public async Task<string> DownloadInstallerAsync(UpdateInfo info, string destinationDirectory,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(info.Sha256))
        {
            throw new InvalidOperationException("该 Release 没有提供 SHA-256 校验文件，已取消自动更新。");
        }

        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.Combine(destinationDirectory, info.InstallerName);
        var partial = destination + ".part";
        try
        {
            using var response = await DownloadClient.GetAsync(info.InstallerUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = File.Create(partial))
            {
                var buffer = new byte[81920];
                long received = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    if (total > 0) progress?.Report(received * 100.0 / total);
                }
            }

            var actual = await ComputeSha256Async(partial, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partial);
                throw new InvalidDataException($"更新包校验失败。期望 {info.Sha256}，实际 {actual}。");
            }

            File.Move(partial, destination, overwrite: true);
            progress?.Report(100);
            return destination;
        }
        catch
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch { }
            throw;
        }
    }

    public async Task<byte[]?> TryDownloadBannerAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var response = await ApiClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return bytes.Length is > 0 and <= 8 * 1024 * 1024 ? bytes : null;
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<ReleaseNoteLine> ParseReleaseNotes(string? markdown)
    {
        var lines = new List<ReleaseNoteLine>();
        if (string.IsNullOrWhiteSpace(markdown)) return lines;
        var inCode = false;
        foreach (var raw in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                if (lines.Count > 0 && lines[^1].Kind != ReleaseNoteKind.Blank)
                    lines.Add(new ReleaseNoteLine("", ReleaseNoteKind.Blank));
                continue;
            }
            if (inCode)
            {
                lines.Add(new ReleaseNoteLine(line.TrimEnd(), ReleaseNoteKind.Code));
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                lines.Add(new ReleaseNoteLine(trimmed.TrimStart('#').Trim(), ReleaseNoteKind.Heading));
            }
            else if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                     trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                lines.Add(new ReleaseNoteLine("• " + CleanInline(trimmed[2..]), ReleaseNoteKind.Bullet));
            }
            else
            {
                lines.Add(new ReleaseNoteLine(CleanInline(trimmed), ReleaseNoteKind.Body));
            }
        }
        while (lines.Count > 0 && lines[^1].Kind == ReleaseNoteKind.Blank) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static string CleanInline(string text)
    {
        return text.Replace("**", "").Replace("`", "").Trim();
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var value = tag.Trim().TrimStart('v', 'V');
        var dash = value.IndexOf('-');
        if (dash >= 0) value = value[..dash];
        if (Version.TryParse(value, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }
        version = new Version();
        return false;
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static string ExtractSha256(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, "[a-fA-F0-9]{64}");
        return match.Success ? match.Value.ToLowerInvariant() : "";
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed class UpdateCache
    {
        public string Json { get; set; } = "";
        public string ETag { get; set; } = "";
        public DateTimeOffset CheckedAt { get; set; }
    }

    private sealed record GitHubAsset(string Name, string Url);
}
