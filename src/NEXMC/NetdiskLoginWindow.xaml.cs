using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using NEXMC.Services;

namespace NEXMC;

/// <summary>网盘登录窗口。优先使用应用内 WebView2，登录后直接读取 Cookie。</summary>
public partial class NetdiskLoginWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly ParsedNetdiskLink _link;
    private readonly NetdiskCookieStore _store;
    private readonly NetdiskResolver _resolver;
    private bool _initialized;

    public NetdiskLoginWindow(ParsedNetdiskLink link, NetdiskCookieStore store, NetdiskResolver resolver)
    {
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        InitializeComponent();
        Title = $"登录{link.DisplayName} · NEXMC";
        TitleText.Text = $"登录{link.DisplayName}";
        SubtitleText.Text = "登录只用于读取你自己的分享直链。Cookie 使用 Windows DPAPI 加密保存在本机，不会上传。";
        HintText.Text = link.Platform == NetdiskPlatform.Quark
            ? "登录夸克账号后点击右侧按钮；Cookie 需要包含 __pus 和 __puus。"
            : "登录 UC 账号后点击右侧按钮；Cookie 需要包含 __pus 和 __puus。";
    }

    public string? Cookie { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await InitializeAsync();

    private async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NEXMC", "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                StatusText.Text = args.IsSuccess
                    ? "页面已加载。请完成登录，然后点击“我已登录，读取 Cookie”。"
                    : $"页面加载失败：{args.WebErrorStatus}";
            };
            Browser.CoreWebView2.Navigate(_link.LoginUrl);
            StatusText.Text = "正在打开网盘登录页…";
        }
        catch (Exception exception)
        {
            Browser.Visibility = Visibility.Collapsed;
            ManualPanel.Visibility = Visibility.Visible;
            StatusText.Text = "内置浏览器不可用，请使用手动 Cookie 方式。";
            HintText.Text = exception.GetBaseException().Message;
        }
    }

    private async void ReadCookie_Click(object sender, RoutedEventArgs e)
    {
        if (ReadCookieButton is null) return;
        ReadCookieButton.IsEnabled = false;
        try
        {
            var cookie = ManualPanel.Visibility == Visibility.Visible || Browser.CoreWebView2 is null
                ? NormalizeManualCookie(ManualCookieBox.Text)
                : await ReadBrowserCookiesAsync();
            if (string.IsNullOrWhiteSpace(cookie))
            {
                StatusText.Text = "没有读到 Cookie。请确认已经登录，或手动粘贴完整 Cookie。";
                return;
            }

            if (!HasRequiredKeys(cookie))
            {
                StatusText.Text = "Cookie 缺少 __pus 或 __puus，登录可能尚未完成。请刷新页面后重试。";
                return;
            }

            StatusText.Text = "正在向网盘确认登录状态…";
            if (!await _resolver.ValidateCookieAsync(_link.Platform, cookie, CancellationToken.None))
            {
                StatusText.Text = "网盘没有确认登录状态。请确认页面显示已登录后重试。";
                return;
            }

            _store.Save(_link.Platform, cookie);
            Cookie = cookie;
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = "读取 Cookie 失败：" + exception.GetBaseException().Message;
        }
        finally
        {
            if (ReadCookieButton is not null) ReadCookieButton.IsEnabled = true;
        }
    }

    private async Task<string> ReadBrowserCookiesAsync()
    {
        if (Browser.CoreWebView2 is null) return string.Empty;
        var uris = _link.Platform == NetdiskPlatform.Quark
            ? new[] { "https://pan.quark.cn/", "https://drive-pc.quark.cn/" }
            : new[] { "https://drive.uc.cn/", "https://pc-api.uc.cn/" };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var uri in uris)
        {
            IReadOnlyList<CoreWebView2Cookie> cookies;
            try { cookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync(uri); }
            catch { continue; }
            foreach (var cookie in cookies)
            {
                if (string.IsNullOrWhiteSpace(cookie.Name) || cookie.Value is null) continue;
                values[cookie.Name] = cookie.Value;
            }
        }
        return string.Join("; ", values.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static string NormalizeManualCookie(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
            value = value["Cookie:".Length..].Trim();
        return string.Join("; ", value
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Contains('=', StringComparison.Ordinal)));
    }

    private static bool HasRequiredKeys(string cookie) =>
        cookie.Contains("__pus=", StringComparison.Ordinal) &&
        cookie.Contains("__puus=", StringComparison.Ordinal);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
