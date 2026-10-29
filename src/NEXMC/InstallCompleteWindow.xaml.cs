using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NEXMC.Models;
using NEXMC.Services;

namespace NEXMC;

public sealed record InstallSuccessTarget(string TargetName, string Directory, string LauncherPath);

public partial class InstallCompleteWindow : Wpf.Ui.Controls.FluentWindow
{
    public InstallCompleteWindow(InstallTaskItem task)
    {
        InitializeComponent();
        SummaryText.Text = task.Targets.All(item => item.Success == true)
            ? $"{task.Title} 已全部安装成功。请重启 PCL/HMCL/其他启动器以刷新版本列表；也可以先打开目录检查。"
            : $"{task.Title} 有部分目标安装成功。以下仅列出真实成功的目标；请重启对应启动器刷新版本列表。";

        SuccessItemsControl.ItemsSource = task.Targets
            .Where(item => item.Success == true)
            .Select(item => new InstallSuccessTarget(item.TargetName, item.Directory, item.Option.ExecutablePath ?? string.Empty))
            .ToList();
    }

    private void OpenDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InstallSuccessTarget target })
        {
            OpenDirectory(target.Directory);
        }
    }

    private void OpenLauncher_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InstallSuccessTarget target })
        {
            OpenLauncher(target.LauncherPath, target.TargetName);
        }
    }

    internal static void OpenDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"安装目录不存在：{path}");
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError("打开目录", exception);
        }
    }

    internal static void OpenLauncher(string path, string targetName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new FileNotFoundException($"尚未为 {targetName} 设置启动器 EXE，请到“设置”页的启动器列表添加。");
            }
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"{targetName} 启动器不存在：{path}");
            }

            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
            });
        }
        catch (Exception exception)
        {
            ShowError($"打开 {targetName}", exception);
        }
    }

    private static void ShowError(string action, Exception exception) => MessageBox.Show(
        $"{action}失败：{Environment.NewLine}{Environment.NewLine}{exception}",
        "NEXMC 错误", MessageBoxButton.OK, MessageBoxImage.Error);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
