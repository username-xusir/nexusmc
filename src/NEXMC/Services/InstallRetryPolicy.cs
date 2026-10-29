using System.IO;

namespace NEXMC.Services;

/// <summary>统一安装/下载失败重试策略：默认最多 3 次尝试，每次失败后等待 10 秒。</summary>
public static class InstallRetryPolicy
{
    public const int DefaultMaxAttempts = 3;
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(10);

    public static async Task<T> RunAsync<T>(
        Func<int, Task<T>> attempt,
        Func<int, Exception, Task>? onRetry,
        CancellationToken cancellationToken,
        Func<Exception, bool>? shouldRetry = null,
        TimeSpan? delay = null,
        int maxAttempts = DefaultMaxAttempts)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        maxAttempts = Math.Clamp(maxAttempts, 1, 20);

        for (var attemptNumber = 1; ; attemptNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await attempt(attemptNumber).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (attemptNumber < maxAttempts &&
                                              (shouldRetry?.Invoke(exception) ?? true))
            {
                if (onRetry is not null)
                {
                    await onRetry(attemptNumber, exception).ConfigureAwait(false);
                }
                await Task.Delay(delay ?? DefaultRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static bool IsNonRetryable(Exception exception)
    {
        if (exception is ArgumentException or FileNotFoundException)
        {
            return true;
        }

        var message = exception.GetBaseException().Message;
        return message.Contains("已存在同名版本", StringComparison.Ordinal) ||
               message.Contains("目标版本目录不为空", StringComparison.Ordinal) ||
               message.Contains("实例名称", StringComparison.Ordinal) ||
               message.Contains("目录不能为空", StringComparison.Ordinal) ||
               message.Contains("目录必须是完整的绝对路径", StringComparison.Ordinal) ||
               message.Contains("文件不存在", StringComparison.Ordinal) ||
               message.Contains("没有可直接下载", StringComparison.Ordinal) ||
               message.Contains("人机验证", StringComparison.Ordinal) ||
               message.Contains("captcha_html", StringComparison.OrdinalIgnoreCase);
    }
}
