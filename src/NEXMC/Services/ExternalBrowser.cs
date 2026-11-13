using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace NEXMC.Services;

/// <summary>
/// 打开外部链接。系统没有默认浏览器（例如 Windows 10 卸载了 Edge）时，
/// 自动退回 NEXMC 自带的内置浏览器，而不是让调用方直接报错。
/// </summary>
public static class ExternalBrowser
{
    public static bool Open(string? url, Window? owner = null)
    {
        if (!TryNormalize(url, out var uri))
        {
            return false;
        }

        // BBSMC 的页面和验证流程默认使用内置浏览器，避免系统浏览器缺失或 Cookie 不共享。
        if (IsBbsmcHost(uri))
        {
            return OpenEmbedded(uri, owner);
        }

        var forceInternal = string.Equals(
            Environment.GetEnvironmentVariable("NEXMC_FORCE_INTERNAL_BROWSER"), "1",
            StringComparison.OrdinalIgnoreCase);
        if (!forceInternal && HasDefaultBrowser())
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                return true;
            }
            catch
            {
                // 关联损坏、浏览器被卸载或系统没有可用默认程序时走内置浏览器。
            }
        }

        return OpenEmbedded(uri, owner);
    }

    /// <summary>BBSMC 页面默认走 NEXMC 内置浏览器，不依赖系统默认浏览器。</summary>
    public static bool OpenBuiltIn(string? url, Window? owner = null)
    {
        if (!TryNormalize(url, out var normalized))
        {
            return false;
        }

        return OpenEmbedded(normalized, owner);
    }

    public static bool TryOpenSystem(string? url)
    {
        if (!TryNormalize(url, out var uri) || !HasDefaultBrowser())
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool OpenEmbedded(string url, Window? owner)
    {
        try
        {
            var window = new InternalBrowserWindow(url) { Owner = owner };
            window.Show();
            window.Activate();
            return true;
        }
        catch (Exception exception)
        {
            try { Clipboard.SetText(url); } catch { }
            MessageBox.Show(
                $"系统没有可用的默认浏览器，内置浏览器也启动失败。链接已复制到剪贴板：{Environment.NewLine}{Environment.NewLine}{url}{Environment.NewLine}{Environment.NewLine}{exception.GetBaseException().Message}",
                "NEXMC 浏览器", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
    }

    private static bool TryNormalize(string? url, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        normalized = uri.AbsoluteUri;
        return true;
    }

    private static bool IsBbsmcHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Host.Equals("bbsmc.net", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".bbsmc.net", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasDefaultBrowser()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            return !string.IsNullOrWhiteSpace(key?.GetValue("ProgId")?.ToString());
        }
        catch
        {
            return false;
        }
    }
}
