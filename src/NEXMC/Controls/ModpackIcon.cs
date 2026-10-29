using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Imazen.WebP;

namespace NEXMC.Controls;

/// <summary>
/// 异步加载整合包封面，支持 PNG/JPEG 与 Modrinth 常用的 WebP。
/// 失败时保持透明，让底下的首字母占位继续显示，不阻塞列表滚动。
/// </summary>
public sealed class ModpackIcon : Image
{
    private const int MemoryCacheLimit = 512;
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly SemaphoreSlim DownloadGate = new(4, 4);
    private static readonly ConcurrentDictionary<string, ImageSource> MemoryCache = new(StringComparer.Ordinal);
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXMC", "IconCache");

    private CancellationTokenSource? _cancellation;

    public static readonly DependencyProperty IconUrlProperty = DependencyProperty.Register(
        nameof(IconUrl),
        typeof(string),
        typeof(ModpackIcon),
        new PropertyMetadata(null, OnIconUrlChanged));

    public ModpackIcon()
    {
        Stretch = Stretch.UniformToFill;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public string? IconUrl
    {
        get => (string?)GetValue(IconUrlProperty);
        set => SetValue(IconUrlProperty, value);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        return client;
    }

    private static async void OnIconUrlChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not ModpackIcon icon)
        {
            return;
        }

        icon._cancellation?.Cancel();
        icon._cancellation?.Dispose();
        icon._cancellation = null;

        var url = (args.NewValue as string)?.Trim();
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            icon.Source = null;
            return;
        }

        if (MemoryCache.TryGetValue(url, out var cached))
        {
            icon.Source = cached;
            return;
        }

        icon.Source = null;
        var cancellation = new CancellationTokenSource();
        icon._cancellation = cancellation;
        var source = await LoadAsync(url, cancellation.Token);
        if (!cancellation.IsCancellationRequested && icon.IconUrl == url)
        {
            icon.Source = source;
        }
    }

    private static async Task<ImageSource?> LoadAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await ReadBytesAsync(url, cancellationToken);
            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }

            var source = DecodeImage(bytes);
            if (source is not null)
            {
                if (MemoryCache.Count >= MemoryCacheLimit)
                {
                    MemoryCache.Clear();
                }
                MemoryCache[url] = source;
            }
            return source;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            // 封面失败不影响列表；保留首字母占位。
            return null;
        }
    }

    private static async Task<byte[]?> ReadBytesAsync(string url, CancellationToken cancellationToken)
    {
        var cachePath = Path.Combine(CacheDirectory, Hash(url));
        try
        {
            if (File.Exists(cachePath))
            {
                return await File.ReadAllBytesAsync(cachePath, cancellationToken);
            }
        }
        catch
        {
            // 缓存损坏时重新下载。
        }

        await DownloadGate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var bytes = await Http.GetByteArrayAsync(url, cancellationToken);
                    try
                    {
                        Directory.CreateDirectory(CacheDirectory);
                        await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);
                    }
                    catch
                    {
                        // 磁盘缓存失败不影响本次显示。
                    }
                    return bytes;
                }
                catch (HttpRequestException) when (attempt == 0)
                {
                    await Task.Delay(800, cancellationToken);
                }
            }
        }
        finally
        {
            DownloadGate.Release();
        }
        return null;
    }

    public static ImageSource? DecodeImage(byte[] bytes)
    {
        if (IsWebP(bytes))
        {
            using var bitmap = new SimpleDecoder().DecodeFromBytes(bytes, bytes.LongLength);
            if (bitmap is null)
            {
                return null;
            }
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;
            return CreateBitmap(stream);
        }

        using var imageStream = new MemoryStream(bytes);
        return CreateBitmap(imageStream);
    }

    private static BitmapImage? CreateBitmap(Stream stream)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 256;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsWebP(byte[] bytes) =>
        bytes.Length >= 12 &&
        bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
        bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
