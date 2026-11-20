using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NEXMC.Services;

/// <summary>网盘解析失败。RequiresLogin 为 true 时，界面应引导用户登录后重试。</summary>
public sealed class NetdiskException : Exception
{
    public NetdiskException(string message, int code = 0, bool requiresLogin = false, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        RequiresLogin = requiresLogin;
    }

    public int Code { get; }
    public bool RequiresLogin { get; }
}

public sealed record NetdiskShareFile(
    string Fid,
    string Name,
    long Size,
    bool IsDirectory,
    string ParentFid,
    string FidToken);

public sealed record ResolvedNetdiskFile(
    string FileName,
    long Size,
    string DownloadUrl,
    string Referer,
    string UserAgent,
    string Cookie,
    NetdiskPlatform Platform,
    Func<CancellationToken, Task>? Cleanup = null);

/// <summary>
/// 夸克 / UC 分享链接直链解析。实现是独立的 C# 代码，不包含 YunX-Desktop 的源代码或素材；
/// 网盘接口行为参考公开抓包与 YunX-Desktop（AGPL-3.0）的使用结果，见 NOTICE。
/// </summary>
public sealed class NetdiskResolver
{
    private const string QuarkApiBase = "https://drive-pc.quark.cn";
    private const string QuarkUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "quark-cloud-drive/2.5.20 Chrome/100.0.4896.160 Electron/18.3.5.12-a038f7b798 Safari/537.36 Channel/pckk_other_ch";
    private const string UcApiBase = "https://pc-api.uc.cn";
    private const string UcUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    private const string UcGuestUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "uc-cloud-drive/2.5.20 Chrome/100.0.4896.160 Electron/18.3.5.4-b478491100 Safari/537.36 Channel/pckk_other_ch";
    private const string TempFolderName = "NEXMC临时转存";
    private const int MaxDirectoryDepth = 4;

    private static readonly HttpClient ApiClient = CreateClient(TimeSpan.FromSeconds(35), true);
    private static readonly HttpClient DownloadClient = CreateClient(Timeout.InfiniteTimeSpan, true);

    private static HttpClient CreateClient(TimeSpan timeout, bool allowRedirect)
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = allowRedirect,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        return new HttpClient(handler) { Timeout = timeout };
    }

    public async Task<ResolvedNetdiskFile> ResolveAsync(ParsedNetdiskLink link, string? cookie,
        IProgress<string>? status, CancellationToken ct, string? preferredName = null)
    {
        if (link is null) throw new ArgumentNullException(nameof(link));
        cookie = NormalizeCookie(cookie);

        return link.Platform switch
        {
            NetdiskPlatform.Direct => await ResolveDirectAsync(link, status, ct).ConfigureAwait(false),
            NetdiskPlatform.Quark => await ResolveQuarkAsync(link, cookie, status, ct, preferredName).ConfigureAwait(false),
            NetdiskPlatform.Uc => await ResolveUcAsync(link, cookie, status, ct, preferredName).ConfigureAwait(false),
            _ => throw new NetdiskException(
                $"已识别为{link.DisplayName}，但 NEXMC 当前只自动处理夸克 / UC 网盘。可以点击“打开网盘下载”在浏览器中继续。")
        };
    }

    public async Task<bool> ValidateCookieAsync(NetdiskPlatform platform, string? cookie, CancellationToken ct)
    {
        cookie = NormalizeCookie(cookie);
        if (string.IsNullOrWhiteSpace(cookie)) return false;

        var (url, userAgent, origin, referer) = platform switch
        {
            NetdiskPlatform.Quark => ("https://pan.quark.cn/account/info", QuarkUserAgent,
                "https://pan.quark.cn", "https://pan.quark.cn/"),
            NetdiskPlatform.Uc => ("https://drive.uc.cn/account/info", UcUserAgent,
                "https://drive.uc.cn", "https://drive.uc.cn/"),
            _ => (string.Empty, string.Empty, string.Empty, string.Empty)
        };
        if (string.IsNullOrWhiteSpace(url)) return false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            request.Headers.TryAddWithoutValidation("Origin", origin);
            request.Headers.TryAddWithoutValidation("Referer", referer);
            using var response = await ApiClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var root = ParseJson(text);
            var status = Int(root, "status");
            return status == 0 || status == 200;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task DownloadAsync(ResolvedNetdiskFile file, string destination,
        IProgress<(double Progress, string Message)>? progress, CancellationToken ct)
    {
        if (file is null) throw new ArgumentNullException(nameof(file));
        if (string.IsNullOrWhiteSpace(file.DownloadUrl))
            throw new NetdiskException("网盘没有返回下载直链。");

        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("下载目标路径无效。", nameof(destination));
        Directory.CreateDirectory(directory);

        var partial = destination + ".part";
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                TryDelete(partial);
                using var request = CreateDownloadRequest(file);
                using var response = await DownloadClient.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var code = (int)response.StatusCode;
                    if (code is 401 or 403)
                        throw new NetdiskException("网盘直链被拒绝，登录状态可能已失效，请重新登录。", code, true);
                    throw new NetdiskException($"下载失败：HTTP {code}", code);
                }

                var total = response.Content.Headers.ContentLength ?? file.Size;
                var buffer = new byte[128 * 1024];
                long received = 0;
                await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write,
                    FileShare.None, 128 * 1024, useAsync: true))
                {
                    var lastReport = DateTime.UtcNow;
                    while (true)
                    {
                        var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)
                            .AsTask()
                            .WaitAsync(TimeSpan.FromSeconds(90), ct)
                            .ConfigureAwait(false);
                        if (read == 0) break;
                        await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        received += read;
                        var now = DateTime.UtcNow;
                        if (now - lastReport >= TimeSpan.FromMilliseconds(250))
                        {
                            lastReport = now;
                            var percent = total > 0 ? Math.Clamp(received * 100.0 / total, 0, 99.9) : 0;
                            progress?.Report((percent, $"正在下载：{FormatBytes(received)}" +
                                (total > 0 ? $" / {FormatBytes(total)}" : string.Empty)));
                        }
                    }

                    await target.FlushAsync(ct).ConfigureAwait(false);
                }

                if (received == 0)
                    throw new NetdiskException("网盘返回了空文件。");
                if (file.Size > 0 && received < file.Size)
                    throw new NetdiskException($"下载不完整：应为 {FormatBytes(file.Size)}，实际 {FormatBytes(received)}。");
                File.Move(partial, destination, overwrite: true);
                progress?.Report((100, $"下载完成：{Path.GetFileName(destination)}"));
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                TryDelete(partial);
                throw;
            }
            catch (NetdiskException exception) when (exception.RequiresLogin)
            {
                TryDelete(partial);
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
                TryDelete(partial);
                if (attempt < 3)
                {
                    progress?.Report((0, $"下载中断，{attempt}/3 次尝试失败，正在重试：{Short(exception)}"));
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                }
            }
        }

        throw new NetdiskException("网盘下载连续失败：" + Short(lastError), inner: lastError);
    }

    private static HttpRequestMessage CreateDownloadRequest(ResolvedNetdiskFile file)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, file.DownloadUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", file.UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", file.Referer);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        if (!string.IsNullOrWhiteSpace(file.Cookie))
            request.Headers.TryAddWithoutValidation("Cookie", file.Cookie);
        return request;
    }

    private async Task<ResolvedNetdiskFile> ResolveDirectAsync(ParsedNetdiskLink link,
        IProgress<string>? status, CancellationToken ct)
    {
        status?.Report("检测到直链文件，正在读取文件信息…");
        using var head = new HttpRequestMessage(HttpMethod.Head, link.OriginalUrl);
        head.Headers.TryAddWithoutValidation("User-Agent", "NEXMC/1.1.3");
        using var headResponse = await ApiClient.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        var fileName = headResponse.Content.Headers.ContentDisposition?.FileNameStar
            ?? headResponse.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? Uri.UnescapeDataString(Path.GetFileName(new Uri(link.OriginalUrl).AbsolutePath));
        var size = headResponse.Content.Headers.ContentLength ?? 0;
        if (headResponse.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "modpack.zip";
            return new ResolvedNetdiskFile(fileName, size, link.OriginalUrl, string.Empty,
                "NEXMC/1.1.3", string.Empty, NetdiskPlatform.Direct);
        }

        // 部分直链服务器不支持 HEAD；用 1 字节 Range 重新确认。
        using var probe = new HttpRequestMessage(HttpMethod.Get, link.OriginalUrl);
        probe.Headers.TryAddWithoutValidation("User-Agent", "NEXMC/1.1.3");
        probe.Headers.TryAddWithoutValidation("Range", "bytes=0-0");
        using var probeResponse = await ApiClient.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!probeResponse.IsSuccessStatusCode)
        {
            throw new NetdiskException($"直链不可用：HTTP {(int)probeResponse.StatusCode}");
        }
        fileName = probeResponse.Content.Headers.ContentDisposition?.FileNameStar
            ?? probeResponse.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? fileName;
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "modpack.zip";
        size = probeResponse.Content.Headers.ContentRange?.Length ?? size;
        return new ResolvedNetdiskFile(fileName, size, link.OriginalUrl, string.Empty,
            "NEXMC/1.1.3", string.Empty, NetdiskPlatform.Direct);
    }

    private async Task<ResolvedNetdiskFile> ResolveQuarkAsync(ParsedNetdiskLink link, string cookie,
        IProgress<string>? status, CancellationToken ct, string? preferredName)
    {
        status?.Report("正在读取夸克分享信息…");
        var token = await GetQuarkShareTokenAsync(link, cookie, ct).ConfigureAwait(false);
        status?.Report($"正在读取分享文件：{token.Title}");
        var files = await ListQuarkFilesAsync(link.ShareId, token.Stoken, cookie, "0", ct).ConfigureAwait(false);
        var archive = await FindArchiveAsync(link.ShareId, token.Stoken, files, cookie,
            NetdiskPlatform.Quark, 0, string.Empty, preferredName, ct).ConfigureAwait(false);
        if (archive is null)
            throw new NetdiskException("分享里没有找到 .zip / .mrpack 整合包文件。");

        status?.Report($"正在获取直链：{archive.Name}");
        try
        {
            return await GetQuarkDownloadAsync(link.ShareId, token.Stoken, archive, cookie, ct, status)
                .ConfigureAwait(false);
        }
        catch (NetdiskException exception) when (!string.IsNullOrWhiteSpace(cookie) && !exception.RequiresLogin)
        {
            // 少数分享不允许免转存取链；登录后回退到“临时转存 → 取链 → 下载后清理”。
            status?.Report("免转存取链失败，正在转存到临时目录后重试…");
            return await GetQuarkTransferDownloadAsync(link.ShareId, token.Stoken, archive, cookie, status, ct)
                .ConfigureAwait(false);
        }
    }

    private async Task<ResolvedNetdiskFile> ResolveUcAsync(ParsedNetdiskLink link, string cookie,
        IProgress<string>? status, CancellationToken ct, string? preferredName)
    {
        status?.Report("正在读取 UC 网盘分享信息…");
        var token = await GetUcShareTokenAsync(link, cookie, ct).ConfigureAwait(false);
        status?.Report($"正在读取分享文件：{token.Title}");
        var files = await ListUcFilesAsync(link.ShareId, token.Stoken, cookie, "0", ct).ConfigureAwait(false);
        var archive = await FindArchiveAsync(link.ShareId, token.Stoken, files, cookie,
            NetdiskPlatform.Uc, 0, string.Empty, preferredName, ct).ConfigureAwait(false);
        if (archive is null)
            throw new NetdiskException("分享里没有找到 .zip / .mrpack 整合包文件。");

        status?.Report($"正在获取直链：{archive.Name}");
        return await GetUcDownloadAsync(link.ShareId, token.Stoken, archive, cookie, ct).ConfigureAwait(false);
    }

    private sealed record ArchiveCandidate(NetdiskShareFile File, string Path);

    private async Task<NetdiskShareFile?> FindArchiveAsync(string shareId, string stoken,
        IReadOnlyList<NetdiskShareFile> files, string cookie, NetdiskPlatform platform, int depth,
        string path, string? preferredName, CancellationToken ct)
    {
        var candidates = new List<ArchiveCandidate>();
        await CollectArchivesAsync(shareId, stoken, files, cookie, platform, depth, path,
            preferredName, candidates, ct).ConfigureAwait(false);
        return candidates
            .OrderByDescending(candidate => ScoreArchive(candidate, preferredName))
            .ThenByDescending(candidate => candidate.File.Size)
            .Select(candidate => candidate.File)
            .FirstOrDefault();
    }

    private async Task CollectArchivesAsync(string shareId, string stoken,
        IReadOnlyList<NetdiskShareFile> files, string cookie, NetdiskPlatform platform, int depth,
        string path, string? preferredName, List<ArchiveCandidate> candidates, CancellationToken ct)
    {
        foreach (var file in files.Where(item => !item.IsDirectory))
        {
            ct.ThrowIfCancellationRequested();
            if (IsArchive(file.Name))
                candidates.Add(new ArchiveCandidate(file, path + "/" + file.Name));
        }

        if (depth >= MaxDirectoryDepth) return;
        var directories = files
            .Where(item => item.IsDirectory && !ShouldSkipDirectory(item.Name))
            .OrderByDescending(item => ScoreDirectoryName(item.Name, preferredName))
            .Take(16)
            .ToList();
        foreach (var directory in directories)
        {
            ct.ThrowIfCancellationRequested();
            var children = platform == NetdiskPlatform.Quark
                ? await ListQuarkFilesAsync(shareId, stoken, cookie, directory.Fid, ct).ConfigureAwait(false)
                : await ListUcFilesAsync(shareId, stoken, cookie, directory.Fid, ct).ConfigureAwait(false);
            await CollectArchivesAsync(shareId, stoken, children, cookie, platform, depth + 1,
                path + "/" + directory.Name, preferredName, candidates, ct).ConfigureAwait(false);
        }
    }

    private static int ScoreDirectoryName(string name, string? preferredName)
    {
        if (string.IsNullOrWhiteSpace(preferredName)) return 0;
        var score = 0;
        var normalizedName = NormalizeName(name);
        var normalizedPreferred = NormalizeName(preferredName);
        if (normalizedPreferred.Length > 0 && normalizedName.Contains(normalizedPreferred, StringComparison.Ordinal))
            score += 100;
        foreach (Match match in Regex.Matches(preferredName, @"\d+(?:\.\d+)+"))
        {
            if (Regex.IsMatch(name, Regex.Escape(match.Value) + @"(?!\.\d)", RegexOptions.IgnoreCase))
                score += 60 + match.Value.Length;
            else if (name.Contains(match.Value, StringComparison.OrdinalIgnoreCase))
                score += 20;
        }
        if (name.Contains("客户端", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("client", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (name.Contains("服务端", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("server", StringComparison.OrdinalIgnoreCase)) score -= 40;
        if (name.Contains("更新日志", StringComparison.OrdinalIgnoreCase)) score -= 100;
        return score;
    }

    private static int ScoreArchive(ArchiveCandidate candidate, string? preferredName)
    {
        var path = candidate.Path;
        var score = 0;
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var normalized = NormalizeName(preferredName);
            if (normalized.Length > 0 && NormalizeName(path).Contains(normalized, StringComparison.Ordinal))
                score += 200;
            foreach (Match match in Regex.Matches(preferredName, @"\d+(?:\.\d+)+"))
            {
                if (Regex.IsMatch(path, Regex.Escape(match.Value) + @"(?!\.\d)", RegexOptions.IgnoreCase))
                    score += 120 + match.Value.Length * 2;
                else if (path.Contains(match.Value, StringComparison.OrdinalIgnoreCase))
                    score += 30;
            }
        }

        if (path.Contains("整合包", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("modpack", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (candidate.File.Name.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (path.Contains("客户端", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("client", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (path.Contains("服务端", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("server", StringComparison.OrdinalIgnoreCase)) score -= 40;
        if (path.Contains("java", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("jdk", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("runtime", StringComparison.OrdinalIgnoreCase)) score -= 500;
        return score;
    }

    private static string NormalizeName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool ShouldSkipDirectory(string name) =>
        name.Equals("Java", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("jdk", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("jre", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("runtime", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("启动器", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("launcher", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("更新日志", StringComparison.OrdinalIgnoreCase);

    private static bool IsArchive(string name)
    {
        var extension = Path.GetExtension(name);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(string Stoken, string Title)> GetQuarkShareTokenAsync(ParsedNetdiskLink link,
        string cookie, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["pwd_id"] = link.ShareId,
            ["passcode"] = link.Password ?? string.Empty,
            ["support_visit_limit_private_share"] = true
        };
        var root = await PostJsonAsync(
            $"{QuarkApiBase}/1/clouddrive/share/sharepage/token?pr=ucpro&fr=pc",
            body.ToJsonString(), cookie, QuarkUserAgent, ct).ConfigureAwait(false);
        var data = DataOrThrow(root, "获取夸克分享凭证失败");
        var stoken = String(data, "stoken");
        if (string.IsNullOrWhiteSpace(stoken)) throw new NetdiskException("夸克没有返回分享凭证，可能需要提取码。");
        return (stoken, String(data, "title"));
    }

    private async Task<IReadOnlyList<NetdiskShareFile>> ListQuarkFilesAsync(string shareId, string stoken,
        string cookie, string parentFid, CancellationToken ct)
    {
        var url = new StringBuilder($"{QuarkApiBase}/1/clouddrive/share/sharepage/detail?pr=ucpro&fr=pc");
        url.Append("&pwd_id=").Append(Uri.EscapeDataString(shareId));
        url.Append("&stoken=").Append(Uri.EscapeDataString(stoken));
        url.Append("&pdir_fid=").Append(Uri.EscapeDataString(parentFid));
        url.Append("&ver=2&force=0&_page=1&_size=100&_fetch_banner=0&_fetch_share=0");
        url.Append("&fetch_relate_conversation=0&_fetch_total=1&_sort=file_type:asc,file_name:asc");
        using var request = new HttpRequestMessage(HttpMethod.Get, url.ToString());
        AddHeaders(request, cookie, QuarkUserAgent, "https://pan.quark.cn", "https://pan.quark.cn/");
        var root = await SendJsonAsync(request, ct).ConfigureAwait(false);
        var data = DataOrThrow(root, "读取夸克分享文件失败");
        var array = data["list"] as JsonArray ?? new JsonArray();
        return array.OfType<JsonObject>().Select(item => new NetdiskShareFile(
            String(item, "fid"),
            String(item, "file_name"),
            Long(item, "size"),
            Bool(item, "dir"),
            String(item, "pdir_fid"),
            String(item, "share_fid_token"))).Where(file => !string.IsNullOrWhiteSpace(file.Fid)).ToList();
    }

    private async Task<ResolvedNetdiskFile> GetQuarkDownloadAsync(string shareId, string stoken,
        NetdiskShareFile file, string cookie, CancellationToken ct, IProgress<string>? status)
    {
        var usingAccount = !string.IsNullOrWhiteSpace(cookie);
        var body = new JsonObject
        {
            ["fids"] = new JsonArray(file.Fid),
            ["fids_token"] = new JsonArray(file.FidToken),
            ["pwd_id"] = shareId,
            ["stoken"] = stoken,
            ["speedup_session"] = string.Empty,
            ["token"] = string.Empty
        };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{QuarkApiBase}/1/clouddrive/file/download?pr=ucpro&fr=pc&sys=win32&ve=3.23.2");
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        AddHeaders(request, cookie, QuarkUserAgent, null, null);
        using var response = await ApiClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        var guestCookie = ExtractResponseCookies(response);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var root = ParseJson(text);
        var code = Int(root, "code");
        var httpStatus = Int(root, "status");
        if (!response.IsSuccessStatusCode || code != 0 || (httpStatus != 0 && httpStatus != 200))
        {
            var message = String(root, "message");
            if (code is 31001 or 23018)
            {
                throw new NetdiskException(
                    usingAccount
                        ? "夸克登录状态已失效，请重新登录夸克网盘。"
                        : "夸克分享需要登录后才能下载（游客只允许约 50MB 的文件）。",
                    code, requiresLogin: true);
            }
            throw new NetdiskException(message.Length > 0
                ? message
                : $"夸克取链失败：HTTP {(int)response.StatusCode}", code);
        }

        var array = root["data"] as JsonArray;
        var item = array?.OfType<JsonObject>().FirstOrDefault();
        var downloadUrl = String(item, "download_url");
        if (string.IsNullOrWhiteSpace(downloadUrl)) throw new NetdiskException("夸克没有返回下载直链。");
        var fileName = String(item, "file_name");
        if (string.IsNullOrWhiteSpace(fileName)) fileName = file.Name;
        var size = Long(item, "size");
        if (size <= 0) size = file.Size;
        var effectiveCookie = (usingAccount ? MergeCookies(cookie, guestCookie) : guestCookie) ?? string.Empty;
        status?.Report("已获取夸克直链，准备下载…");
        return new ResolvedNetdiskFile(fileName, size, downloadUrl, "https://pan.quark.cn/",
            QuarkUserAgent, effectiveCookie, NetdiskPlatform.Quark);
    }

    private async Task<(string Stoken, string Title)> GetUcShareTokenAsync(ParsedNetdiskLink link,
        string cookie, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["pwd_id"] = link.ShareId,
            ["passcode"] = link.Password ?? string.Empty,
            ["share_for_transfer"] = true
        };
        var root = await PostJsonAsync(
            $"{UcApiBase}/1/clouddrive/share/sharepage/token?pr=UCBrowser&fr=pc",
            body.ToJsonString(), cookie, UcUserAgent, ct).ConfigureAwait(false);
        var data = DataOrThrow(root, "获取 UC 分享凭证失败");
        var stoken = String(data, "stoken");
        if (string.IsNullOrWhiteSpace(stoken)) throw new NetdiskException("UC 没有返回分享凭证，可能需要提取码。");
        return (stoken, String(data, "title"));
    }

    private async Task<IReadOnlyList<NetdiskShareFile>> ListUcFilesAsync(string shareId, string stoken,
        string cookie, string parentFid, CancellationToken ct)
    {
        var url = new StringBuilder($"{UcApiBase}/1/clouddrive/transfer_share/detail?entry=ft&fr=pc&pr=UCBrowser");
        url.Append("&pwd_id=").Append(Uri.EscapeDataString(shareId));
        url.Append("&pdir_fid=").Append(Uri.EscapeDataString(parentFid));
        url.Append("&fetch_file_list=1&passcode=&_page=1&_size=100&_fetch_total=1&_fetch_task=1&_fetch_share=1&_sort=");
        url.Append("&stoken=").Append(Uri.EscapeDataString(stoken));
        using var request = new HttpRequestMessage(HttpMethod.Get, url.ToString());
        AddHeaders(request, cookie, UcUserAgent, "https://fast.uc.cn", "https://fast.uc.cn/");
        var root = await SendJsonAsync(request, ct).ConfigureAwait(false);
        var data = DataOrThrow(root, "读取 UC 分享文件失败");
        var array = data["list"] as JsonArray ?? data["detail_info"]?["list"] as JsonArray ?? new JsonArray();
        return array.OfType<JsonObject>().Select(item => new NetdiskShareFile(
            String(item, "fid"),
            String(item, "file_name"),
            Long(item, "size"),
            Bool(item, "dir"),
            String(item, "pdir_fid"),
            String(item, "share_fid_token"))).Where(file => !string.IsNullOrWhiteSpace(file.Fid)).ToList();
    }

    private async Task<ResolvedNetdiskFile> GetUcDownloadAsync(string shareId, string stoken,
        NetdiskShareFile file, string cookie, CancellationToken ct)
    {
        var usingAccount = !string.IsNullOrWhiteSpace(cookie);
        var body = new JsonObject
        {
            ["fids"] = new JsonArray(file.Fid),
            ["pwd_id"] = shareId,
            ["stoken"] = stoken,
            ["fids_token"] = new JsonArray(file.FidToken)
        };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{UcApiBase}/1/clouddrive/file/download?entry=ft&fr=pc&pr=UCBrowser");
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        AddHeaders(request, cookie, usingAccount ? UcUserAgent : UcGuestUserAgent, null, null);
        if (!usingAccount)
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua",
                "\"Not=A?Brand\";v=\"99\", \"Chromium\";v=\"100\", \"Google Chrome\";v=\"100\"");
        using var response = await ApiClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        var guestCookie = ExtractResponseCookies(response);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var root = ParseJson(text);
        var code = Int(root, "code");
        var statusCode = Int(root, "status");
        if (!response.IsSuccessStatusCode || code != 0 || (statusCode != 0 && statusCode != 200))
        {
            var message = String(root, "message");
            if (code is 31001 or 23018)
                throw new NetdiskException("UC 网盘需要登录后才能下载。", code, requiresLogin: true);
            throw new NetdiskException(message.Length > 0
                ? message
                : $"UC 取链失败：HTTP {(int)response.StatusCode}", code);
        }

        var array = root["data"] as JsonArray;
        var item = array?.OfType<JsonObject>().FirstOrDefault();
        var downloadUrl = String(item, "download_url");
        if (string.IsNullOrWhiteSpace(downloadUrl)) throw new NetdiskException("UC 没有返回下载直链。");
        var fileName = String(item, "file_name");
        if (string.IsNullOrWhiteSpace(fileName)) fileName = file.Name;
        var size = Long(item, "size");
        if (size <= 0) size = file.Size;
        var effectiveCookie = (usingAccount ? MergeCookies(cookie, guestCookie) : guestCookie) ?? string.Empty;
        return new ResolvedNetdiskFile(fileName, size, downloadUrl, "https://drive.uc.cn/",
            usingAccount ? UcUserAgent : UcGuestUserAgent, effectiveCookie, NetdiskPlatform.Uc);
    }

    private async Task<ResolvedNetdiskFile> GetQuarkTransferDownloadAsync(string shareId, string stoken,
        NetdiskShareFile file, string cookie, IProgress<string>? status, CancellationToken ct)
    {
        var rootFid = await EnsureQuarkTempFolderAsync(cookie, ct).ConfigureAwait(false);
        var subName = "nexmc_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "_" + Random.Shared.Next(1000, 9999);
        var subFid = await CreateQuarkFolderAsync(subName, rootFid, cookie, ct).ConfigureAwait(false);
        status?.Report("正在转存到临时目录…");
        var saveBody = new JsonObject
        {
            ["pwd_id"] = shareId,
            ["stoken"] = stoken,
            ["pdir_fid"] = file.ParentFid,
            ["to_pdir_fid"] = subFid,
            ["fid_list"] = new JsonArray(file.Fid),
            ["fid_token_list"] = new JsonArray(file.FidToken),
            ["scene"] = "link"
        };
        var saveRoot = await PostJsonAsync(
            $"{QuarkApiBase}/1/clouddrive/share/sharepage/save?pr=ucpro&fr=pc",
            saveBody.ToJsonString(), cookie, QuarkUserAgent, ct).ConfigureAwait(false);
        var saveData = DataOrThrow(saveRoot, "夸克转存失败");
        var taskId = String(saveData, "task_id");
        if (string.IsNullOrWhiteSpace(taskId)) throw new NetdiskException("夸克转存没有返回任务 ID。");

        string? savedFid = null;
        for (var attempt = 0; attempt < 15 && savedFid is null; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            var url = $"{QuarkApiBase}/1/clouddrive/task?pr=ucpro&fr=pc&task_id={Uri.EscapeDataString(taskId)}&retry_index=0";
            using var taskRequest = new HttpRequestMessage(HttpMethod.Get, url);
            AddHeaders(taskRequest, cookie, QuarkUserAgent, null, null);
            var taskRoot = await SendJsonAsync(taskRequest, ct).ConfigureAwait(false);
            var data = taskRoot["data"] as JsonObject;
            var finished = Long(data, "finished_at") > 0 || Int(data, "status") == 2 || Int(data, "task_status") == 2;
            if (!finished) continue;
            var array = data?["save_as"]?["save_as_top_fids"] as JsonArray;
            savedFid = array?.FirstOrDefault()?.GetValue<string>();
        }
        if (string.IsNullOrWhiteSpace(savedFid)) throw new NetdiskException("夸克转存超时，请稍后重试。");

        status?.Report("转存完成，正在获取直链…");
        var downloadBody = new JsonObject { ["fids"] = new JsonArray(savedFid!) };
        var downloadRoot = await PostJsonAsync(
            $"{QuarkApiBase}/1/clouddrive/file/download?pr=ucpro&fr=pc&sys=win32&ve=3.23.2",
            downloadBody.ToJsonString(), cookie, QuarkUserAgent, ct).ConfigureAwait(false);
        var item = (downloadRoot["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault()
            ?? throw new NetdiskException("夸克没有返回转存后的下载直链。");
        var downloadUrl = String(item, "download_url");
        if (string.IsNullOrWhiteSpace(downloadUrl)) throw new NetdiskException("夸克没有返回转存后的下载直链。");
        var fileName = String(item, "file_name");
        if (string.IsNullOrWhiteSpace(fileName)) fileName = file.Name;
        var size = Long(item, "size");
        if (size <= 0) size = file.Size;

        return new ResolvedNetdiskFile(fileName, size, downloadUrl, "https://pan.quark.cn/",
            QuarkUserAgent, cookie, NetdiskPlatform.Quark,
            async token => await DeleteQuarkFolderAsync(subFid, cookie, token).ConfigureAwait(false));
    }

    private async Task<string> EnsureQuarkTempFolderAsync(string cookie, CancellationToken ct)
    {
        using var listRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{QuarkApiBase}/1/clouddrive/file?pr=ucpro&fr=pc&pdir_fid=0&page=1&size=100");
        AddHeaders(listRequest, cookie, QuarkUserAgent, null, null);
        var listRoot = await SendJsonAsync(listRequest, ct).ConfigureAwait(false);
        var data = DataOrThrow(listRoot, "读取夸克个人网盘失败");
        var existing = (data["list"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(item => Bool(item, "dir") && String(item, "file_name") == TempFolderName);
        if (existing is not null) return String(existing, "fid");
        return await CreateQuarkFolderAsync(TempFolderName, "0", cookie, ct).ConfigureAwait(false);
    }

    private async Task<string> CreateQuarkFolderAsync(string name, string parentFid, string cookie,
        CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["pdir_fid"] = parentFid,
            ["file_name"] = name,
            ["dir_path"] = string.Empty,
            ["dir_init_lock"] = false
        };
        var root = await PostJsonAsync($"{QuarkApiBase}/1/clouddrive/file?pr=ucpro&fr=pc",
            body.ToJsonString(), cookie, QuarkUserAgent, ct).ConfigureAwait(false);
        var data = DataOrThrow(root, "创建夸克临时目录失败");
        var fid = String(data, "fid");
        if (string.IsNullOrWhiteSpace(fid)) throw new NetdiskException("创建夸克临时目录失败。");
        return fid;
    }

    private async Task DeleteQuarkFolderAsync(string fid, string cookie, CancellationToken ct)
    {
        try
        {
            var body = new JsonObject
            {
                ["action_type"] = 2,
                ["filelist"] = new JsonArray(fid),
                ["exclude_fids"] = new JsonArray()
            };
            await PostJsonAsync($"{QuarkApiBase}/1/clouddrive/file/delete?pr=ucpro&fr=pc&uc_param_str=",
                body.ToJsonString(), cookie, QuarkUserAgent, ct).ConfigureAwait(false);
        }
        catch
        {
            // 临时目录清理失败不能影响已经完成的下载。
        }
    }

    private static async Task<JsonObject> PostJsonAsync(string url, string body, string cookie,
        string userAgent, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        AddHeaders(request, cookie, userAgent, null, null);
        return await SendJsonAsync(request, ct).ConfigureAwait(false);
    }

    private static void AddHeaders(HttpRequestMessage request, string cookie, string userAgent,
        string? origin, string? referer)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        if (!string.IsNullOrWhiteSpace(cookie))
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (!string.IsNullOrWhiteSpace(origin))
            request.Headers.TryAddWithoutValidation("Origin", origin);
        if (!string.IsNullOrWhiteSpace(referer))
            request.Headers.TryAddWithoutValidation("Referer", referer);
    }

    private static async Task<JsonObject> SendJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await ApiClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            JsonObject? errorRoot = null;
            try { errorRoot = JsonNode.Parse(text) as JsonObject; } catch { }
            var message = String(errorRoot, "message");
            var apiCode = Int(errorRoot, "code");
            if (!string.IsNullOrWhiteSpace(message))
            {
                throw new NetdiskException(message, apiCode,
                    requiresLogin: apiCode is 31001 or 23018);
            }
            var code = (int)response.StatusCode;
            throw new NetdiskException($"网盘接口返回 HTTP {code}。", code,
                requiresLogin: code is 401 or 403);
        }
        return ParseJson(text);
    }

    private static JsonObject ParseJson(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject
                ?? throw new NetdiskException("网盘接口返回了无法识别的数据。");
        }
        catch (NetdiskException) { throw; }
        catch (Exception exception)
        {
            throw new NetdiskException("网盘接口返回了非 JSON 数据，可能触发了风控。" +
                "请稍后重试，或登录后重试。", inner: exception);
        }
    }

    private static JsonObject DataOrThrow(JsonObject root, string fallback)
    {
        var status = Int(root, "status");
        var code = Int(root, "code");
        if (status != 0 && status != 200)
        {
            var message = String(root, "message");
            var requiresLogin = code is 31001 or 23018;
            throw new NetdiskException(message.Length > 0 ? message : fallback, code, requiresLogin);
        }
        if (code != 0)
        {
            var message = String(root, "message");
            throw new NetdiskException(message.Length > 0 ? message : fallback, code,
                requiresLogin: code is 31001 or 23018);
        }
        return root["data"] as JsonObject ?? new JsonObject();
    }

    private static string? ExtractResponseCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        return NetdiskCookieHeader.FromSetCookies(values);
    }

    private static string MergeCookies(string? first, string? second) =>
        NetdiskCookieHeader.Merge(first, second);

    private static string NormalizeCookie(string? cookie) => (cookie ?? string.Empty).Trim();

    private static string String(JsonObject? obj, string name) =>
        obj?[name]?.GetValue<string>() ?? string.Empty;

    private static long Long(JsonObject? obj, string name)
    {
        var node = obj?[name];
        if (node is null) return 0;
        try { return node.GetValue<long>(); }
        catch
        {
            return long.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }
    }

    private static int Int(JsonObject? obj, string name) => (int)Math.Clamp(Long(obj, name), int.MinValue, int.MaxValue);

    private static bool Bool(JsonObject? obj, string name)
    {
        var node = obj?[name];
        if (node is null) return false;
        try { return node.GetValue<bool>(); }
        catch
        {
            if (bool.TryParse(node.ToString(), out var value)) return value;
            return long.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
                   number != 0;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string Short(Exception? exception)
    {
        if (exception is null) return "未知错误";
        var text = exception.GetBaseException().Message;
        return text.Length <= 120 ? text : text[..117] + "...";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}
