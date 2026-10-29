using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace NEXMC;

public partial class BbsmcCaptchaWindow : Window
{
    private readonly string _url;
    private readonly string _downloadFolder;
    private bool _initialized;
    private string? _downloadPath;

    public BbsmcCaptchaWindow(string url)
    {
        InitializeComponent();
        _url = url;
        _downloadFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NEXMC", "Downloads", "BBSMC");
    }

    public string? DownloadedPath => _downloadPath;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeBrowserAsync();
    }

    private async Task InitializeBrowserAsync()
    {
        if (_initialized)
        {
            Browser.CoreWebView2?.Navigate(_url);
            return;
        }

        try
        {
            Directory.CreateDirectory(_downloadFolder);
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NEXMC", "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;
            Browser.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            Browser.CoreWebView2.NavigationStarting += (_, _) => StatusText.Text = "正在加载 BBSMC 页面…";
            Browser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                StatusText.Text = args.IsSuccess
                    ? "请在页面完成人机验证，然后点击页面上的下载按钮。"
                    : $"页面加载失败：{args.WebErrorStatus}";
            };
            Browser.CoreWebView2.DocumentTitleChanged += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(Browser.CoreWebView2.DocumentTitle))
                {
                    StatusText.Text = "页面已加载；请在页面完成人机验证，然后点击页面上的下载按钮。";
                }
            };
            _initialized = true;
            Browser.CoreWebView2.Navigate(_url);
            StatusText.Text = "页面已打开；如出现人机验证，请完成验证后点击页面上的下载按钮。";
        }
        catch (Exception exception)
        {
            StatusText.Text = "内嵌浏览器初始化失败，请点击“外部浏览器打开”完成验证并下载。";
            Browser.Visibility = Visibility.Collapsed;
            var details = new TextBlock
            {
                Text = exception.GetBaseException().Message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(14, 8, 14, 8)
            };
            if (Content is Grid grid)
            {
                Grid.SetRow(details, 1);
                grid.Children.Add(details);
            }
        }
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        Browser.CoreWebView2?.Navigate(e.Uri);
    }

    private void CoreWebView2_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_downloadFolder);
            var suggested = Path.GetFileName(e.ResultFilePath);
            if (string.IsNullOrWhiteSpace(suggested) || suggested is "." or "..")
            {
                suggested = "BBSMC-download.zip";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                suggested = suggested.Replace(invalid, '_');
            }

            var target = Path.Combine(_downloadFolder, suggested);
            if (File.Exists(target))
            {
                target = Path.Combine(_downloadFolder,
                    Path.GetFileNameWithoutExtension(suggested) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + Path.GetExtension(suggested));
            }

            e.ResultFilePath = target;
            e.Handled = true;
            var operation = e.DownloadOperation;
            StatusText.Text = $"正在下载 {Path.GetFileName(target)}…";
            operation.BytesReceivedChanged += (_, _) =>
            {
                if (operation.TotalBytesToReceive > 0)
                {
                    var percent = operation.BytesReceived * 100.0 / operation.TotalBytesToReceive;
                    StatusText.Text = $"正在下载 {Path.GetFileName(target)}：{percent:F0}%";
                }
            };
            operation.StateChanged += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (operation.State == CoreWebView2DownloadState.Completed)
                    {
                        _downloadPath = target;
                        StatusText.Text = "下载完成，正在切换为本地整合包…";
                        DialogResult = true;
                        Close();
                    }
                    else if (operation.State == CoreWebView2DownloadState.Interrupted)
                    {
                        StatusText.Text = $"下载被中断：{operation.InterruptReason}";
                    }
                });
            };
        }
        catch (Exception exception)
        {
            StatusText.Text = "无法保存下载文件：" + exception.GetBaseException().Message;
        }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            await InitializeBrowserAsync();
            return;
        }

        Browser.CoreWebView2?.Navigate(_url);
    }

    private void External_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true });
            StatusText.Text = "已在外部浏览器打开；下载完成后可用“选择本地整合包”导入。";
        }
        catch (Exception exception)
        {
            StatusText.Text = "打开外部浏览器失败：" + exception.GetBaseException().Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
