using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using NEXMC.Services;

namespace NEXMC;

/// <summary>没有系统默认浏览器时的内置浏览器：优先 WebView2，失败时退回系统兼容浏览器。</summary>
public partial class InternalBrowserWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly string _url;
    private bool _initialized;

    public InternalBrowserWindow(string url)
    {
        InitializeComponent();
        _url = url;
        AddressText.Text = url;
        AddressText.ToolTip = url;
    }

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
            Browser.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    Browser.CoreWebView2.Navigate(uri.AbsoluteUri);
                }
            };
            Browser.CoreWebView2.NavigationCompleted += (_, args) =>
                StatusText.Text = args.IsSuccess ? "页面已加载。" : $"页面加载失败：{args.WebErrorStatus}";
            Browser.Visibility = Visibility.Visible;
            LegacyBrowser.Visibility = Visibility.Collapsed;
            Browser.CoreWebView2.Navigate(_url);
            StatusText.Text = "正在加载页面…";
        }
        catch (Exception exception)
        {
            Browser.Visibility = Visibility.Collapsed;
            LegacyBrowser.Visibility = Visibility.Visible;
            try
            {
                LegacyBrowser.Navigate(_url);
                StatusText.Text = "WebView2 不可用，已切换到系统兼容浏览器。";
            }
            catch
            {
                StatusText.Text = "内置浏览器初始化失败：" + exception.GetBaseException().Message;
            }
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.Visibility == Visibility.Visible && Browser.CoreWebView2 is not null)
        {
            Browser.CoreWebView2.Reload();
        }
        else if (LegacyBrowser.Visibility == Visibility.Visible)
        {
            LegacyBrowser.Refresh();
        }
        else
        {
            _ = InitializeAsync();
        }
    }

    private void SystemBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (!ExternalBrowser.TryOpenSystem(_url))
        {
            try { Clipboard.SetText(_url); } catch { }
            StatusText.Text = "系统没有可用的默认浏览器，链接已复制到剪贴板。";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
