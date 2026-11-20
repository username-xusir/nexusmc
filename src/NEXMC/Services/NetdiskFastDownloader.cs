using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace NEXMC.Services;

/// <summary>
/// 网盘直链的快速分片下载路径：Range 分片 + 多连接并行 + 断点式重试。
/// 只在网盘版本上使用；失败时由调用方回退到 PCL2 下载内核。
/// 这是独立实现，不依赖云析的代码或运行时。
/// </summary>
public static class NetdiskFastDownloader
{
    private const long MinimumSize = 8L * 1024 * 1024;
    private const long UnitSize = 4L * 1024 * 1024;
    private const int MaxWorkers = 256;

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            MaxConnectionsPerServer = MaxWorkers,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// 尝试用快速分片路径下载。返回 false 表示服务器不支持 Range、文件太小或网络失败，
    /// 调用方应回退到 PCL2 内核。用户取消会抛出 OperationCanceledException。
    /// </summary>
    public static async Task<bool> TryDownloadAsync(
        string url,
        string destination,
        IReadOnlyDictionary<string, string> headers,
        long expectedSize,
        bool autoThreads,
        int configuredThreads,
        IProgress<(double Progress, string Message)>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(destination)) return false;
        try
        {
            var total = await ProbeSizeAsync(url, headers, ct).ConfigureAwait(false);
            if (total < MinimumSize) return false;
            if (expectedSize > 0 && total != expectedSize)
            {
                // 有些 CDN 的 Content-Length 与实际文件略有差异；相差太大时不冒险。
                if (Math.Abs(total - expectedSize) > 1024 * 1024) return false;
                total = expectedSize;
            }

            var workers = autoThreads
                ? await EstimateWorkersAsync(url, headers, total, ct).ConfigureAwait(false)
                : Math.Clamp(configuredThreads, 1, MaxWorkers);
            var unitCount = Math.Max(1, (int)Math.Min(int.MaxValue, (total + UnitSize - 1) / UnitSize));
            workers = Math.Clamp(workers, 1, Math.Min(MaxWorkers, unitCount));

            var directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(directory)) return false;
            Directory.CreateDirectory(directory);
            TryDelete(destination);

            using (var preallocate = new FileStream(destination, FileMode.Create, FileAccess.Write,
                       FileShare.Read, 4096, FileOptions.None))
            {
                preallocate.SetLength(total);
            }

            using var handle = File.OpenHandle(destination, FileMode.Open, FileAccess.Write, FileShare.Read);
            var ranges = new ConcurrentQueue<(long Start, long End)>();
            for (long start = 0; start < total; start += UnitSize)
            {
                ranges.Enqueue((start, Math.Min(total - 1, start + UnitSize - 1)));
            }

            long downloaded = 0;
            var failed = 0;
            var lastReportTicks = 0L;
            var stopwatch = Stopwatch.StartNew();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

            async Task WorkerAsync()
            {
                while (!linked.IsCancellationRequested && ranges.TryDequeue(out var range))
                {
                    try
                    {
                        await DownloadRangeAsync(url, headers, handle, range.Start, range.End,
                            total, linked.Token,
                            bytes =>
                            {
                                var now = Interlocked.Read(ref downloaded) + bytes;
                                Interlocked.Exchange(ref downloaded, now);
                                var nowTicks = stopwatch.ElapsedMilliseconds;
                                var previous = Interlocked.Read(ref lastReportTicks);
                                if (nowTicks - previous >= 250 &&
                                    Interlocked.CompareExchange(ref lastReportTicks, nowTicks, previous) == previous)
                                {
                                    var pct = now * 100.0 / Math.Max(1, total);
                                    progress?.Report((Math.Clamp(pct, 0, 99.9),
                                        $"快速分片下载：{FormatBytes(now)} / {FormatBytes(total)}"));
                                }
                            }).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (FastPathUnsupportedException)
                    {
                        Interlocked.Exchange(ref failed, 1);
                        linked.Cancel();
                        return;
                    }
                    catch
                    {
                        Interlocked.Exchange(ref failed, 1);
                        linked.Cancel();
                        return;
                    }
                }
            }

            await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => WorkerAsync())).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (Volatile.Read(ref failed) != 0)
            {
                TryDelete(destination);
                return false;
            }

            var length = new FileInfo(destination).Length;
            if (length != total)
            {
                TryDelete(destination);
                return false;
            }

            progress?.Report((100, $"快速分片下载完成：{FormatBytes(total)}"));
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryDelete(destination);
            throw;
        }
        catch
        {
            TryDelete(destination);
            return false;
        }
    }

    private static async Task<long> ProbeSizeAsync(string url, IReadOnlyDictionary<string, string> headers,
        CancellationToken ct)
    {
        using var request = CreateRequest(url, headers);
        request.Headers.Range = new RangeHeaderValue(0, 0);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.PartialContent) return -1;
        return response.Content.Headers.ContentRange?.Length ?? -1;
    }

    private static async Task<int> EstimateWorkersAsync(string url, IReadOnlyDictionary<string, string> headers,
        long total, CancellationToken ct)
    {
        const int probeCount = 4;
        const long probeSize = 1024 * 1024;
        var sw = Stopwatch.StartNew();
        long bytes = 0;
        var tasks = Enumerable.Range(0, probeCount).Select(async index =>
        {
            var start = Math.Min(total - 1, index * probeSize);
            var end = Math.Min(total - 1, start + probeSize - 1);
            using var request = CreateRequest(url, headers);
            request.Headers.Range = new RangeHeaderValue(start, end);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.PartialContent) return 0L;
            var buffer = new byte[64 * 1024];
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            long count = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
            }
            return count;
        }).ToArray();

        try
        {
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            bytes = results.Sum();
        }
        catch
        {
            return 16;
        }
        var seconds = Math.Max(0.2, sw.Elapsed.TotalSeconds);
        var mbps = bytes / seconds / 1024 / 1024;
        return mbps switch
        {
            < 2 => 8,
            < 8 => 16,
            < 20 => 32,
            < 50 => 64,
            < 100 => 128,
            _ => 256
        };
    }

    private static async Task DownloadRangeAsync(string url, IReadOnlyDictionary<string, string> headers,
        Microsoft.Win32.SafeHandles.SafeFileHandle handle, long start, long end, long total,
        CancellationToken ct, Action<int> onBytes)
    {
        var offset = start;
        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = CreateRequest(url, headers);
            request.Headers.Range = new RangeHeaderValue(offset, end);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK)
                throw new FastPathUnsupportedException();
            if (response.StatusCode != HttpStatusCode.PartialContent)
                throw new IOException($"HTTP {(int)response.StatusCode}");
            var contentRange = response.Content.Headers.ContentRange;
            if (contentRange?.From is not long from || from != offset ||
                contentRange.Length is long length && length != total)
            {
                throw new FastPathUnsupportedException();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var buffer = new byte[128 * 1024];
            while (offset <= end)
            {
                ct.ThrowIfCancellationRequested();
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(60), ct)
                    .ConfigureAwait(false);
                if (read == 0) break;
                var take = (int)Math.Min(read, end - offset + 1);
                await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, take), offset, ct)
                    .ConfigureAwait(false);
                offset += take;
                onBytes(take);
            }

            if (offset > end) return;
            if (attempt == maxAttempts)
                throw new IOException("分片下载不完整");
        }
    }

    private static HttpRequestMessage CreateRequest(string url, IReadOnlyDictionary<string, string> headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers is not null)
        {
            foreach (var pair in headers)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) continue;
                if (pair.Key.Equals("Range", StringComparison.OrdinalIgnoreCase)) continue;
                request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            }
        }
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        return request;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
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

    private sealed class FastPathUnsupportedException : Exception
    {
    }
}
