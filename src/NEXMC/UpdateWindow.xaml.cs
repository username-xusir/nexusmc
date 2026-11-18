using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NEXMC.Services;

namespace NEXMC;

public partial class UpdateWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly UpdateInfo _info;
    private readonly UpdateService _service = new();
    private readonly CancellationTokenSource _downloadCancellation = new();
    private bool _downloading;

    public UpdateWindow(UpdateInfo info)
    {
        InitializeComponent();
        _info = info;
        NewVersionText.Text = $"发现新版本 {info.Version}";
        ReleaseTitleText.Text = string.IsNullOrWhiteSpace(info.Title) ? "NEXMC 更新" : info.Title;
        FallbackVersionText.Text = info.Version;
        BuildReleaseNotes();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadBannerAsync();
    }

    private async Task LoadBannerAsync()
    {
        try
        {
            var local = TryLoadLocalBanner(_info.Version);
            if (local is not null)
            {
                ShowBanner(local);
                return;
            }

            var bytes = await _service.TryDownloadBannerAsync(_info.BannerUrl, _downloadCancellation.Token);
            if (bytes is not null)
            {
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                ShowBanner(bitmap);
            }
        }
        catch (OperationCanceledException)
        {
            // 窗口关闭或取消下载。
        }
        catch
        {
            // 横幅失败不影响更新内容显示。
        }
    }

    private static BitmapImage? TryLoadLocalBanner(string version)
    {
        foreach (var name in new[] { $"{version}-dark.png", $"{version}.png" })
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/assets/updates/{name}", UriKind.Absolute);
                var resource = Application.GetResourceStream(uri);
                if (resource is null) continue;
                using var stream = resource.Stream;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                // 尝试下一张兜底图。
            }
        }
        return null;
    }

    private void ShowBanner(ImageSource source)
    {
        BannerImage.Source = source;
        BannerImage.Visibility = Visibility.Visible;
        BannerFallback.Visibility = Visibility.Collapsed;
    }

    private void BuildReleaseNotes()
    {
        var lines = UpdateService.ParseReleaseNotes(_info.Body);
        if (lines.Count == 0)
        {
            ReleaseNotesPanel.Children.Add(new TextBlock
            {
                Text = "这个版本没有填写更新说明。",
                Style = (Style)FindResource("MutedText")
            });
            return;
        }

        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case ReleaseNoteKind.Heading:
                    ReleaseNotesPanel.Children.Add(new TextBlock
                    {
                        Text = line.Text,
                        FontSize = 15,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 10, 0, 5),
                        TextWrapping = TextWrapping.Wrap
                    });
                    break;
                case ReleaseNoteKind.Bullet:
                    ReleaseNotesPanel.Children.Add(new TextBlock
                    {
                        Text = line.Text,
                        FontSize = 12.5,
                        Margin = new Thickness(0, 3, 0, 3),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)FindResource("TextBrush")
                    });
                    break;
                case ReleaseNoteKind.Code:
                    ReleaseNotesPanel.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(244, 244, 244)),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(9, 5, 9, 5),
                        Margin = new Thickness(0, 3, 0, 3),
                        Child = new TextBlock
                        {
                            Text = line.Text,
                            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
                            FontSize = 11.5,
                            TextWrapping = TextWrapping.Wrap
                        }
                    });
                    break;
                case ReleaseNoteKind.Blank:
                    ReleaseNotesPanel.Children.Add(new Border { Height = 5 });
                    break;
                default:
                    ReleaseNotesPanel.Children.Add(new TextBlock
                    {
                        Text = line.Text,
                        FontSize = 12.5,
                        Margin = new Thickness(0, 2, 0, 2),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)FindResource("TextBrush")
                    });
                    break;
            }
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_downloading) return;
        _downloading = true;
        UpdateButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        StatusText.Text = "正在下载更新包…";

        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NEXMC", "Updates");
            var progress = new Progress<double>(value =>
            {
                DownloadProgress.Value = Math.Clamp(value, 0, 100);
                StatusText.Text = $"正在下载更新包… {value:F0}%";
            });
            var installer = await _service.DownloadInstallerAsync(
                _info, directory, progress, _downloadCancellation.Token);

            DownloadProgress.Value = 100;
            StatusText.Text = "校验通过，正在启动安装程序…";
            StartInstaller(installer);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消更新。";
            ResetButtons();
        }
        catch (Exception exception)
        {
            StatusText.Text = "更新失败：" + exception.GetBaseException().Message;
            ResetButtons();
        }
    }

    private void ResetButtons()
    {
        _downloading = false;
        UpdateButton.IsEnabled = true;
        LaterButton.IsEnabled = true;
        DownloadProgress.Visibility = Visibility.Collapsed;
    }

    private static void StartInstaller(string installer)
    {
        // 给当前进程留出退出时间，避免安装程序覆盖正在使用的文件。
        var escaped = installer.Replace("\"", "\\\"");
        Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c timeout /t 2 >nul & start \"\" \"{escaped}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        _downloadCancellation.Cancel();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _downloadCancellation.Cancel();
        Close();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        _downloadCancellation.Cancel();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
