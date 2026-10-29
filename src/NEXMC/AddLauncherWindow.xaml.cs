using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NEXMC.Services;

namespace NEXMC;

public partial class AddLauncherWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly HashSet<string> _existingDirectories;

    public AddLauncherWindow(IEnumerable<string> existingDirectories)
    {
        InitializeComponent();
        _existingDirectories = new HashSet<string>(existingDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path =>
            {
                try { return NormalizeDirectory(path); }
                catch { return path.Trim(); }
            }), StringComparer.OrdinalIgnoreCase);
    }

    public CustomLauncherSetting? Launcher { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        NameTextBox.Focus();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择启动器的 .minecraft 游戏目录",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        DirectoryTextBox.Text = dialog.FolderName;
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            NameTextBox.Text = LauncherDetector.DeriveLauncherName(null, dialog.FolderName);
        }
    }

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择启动器 EXE（可选）",
            Filter = "Windows 可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        ExecutableTextBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            NameTextBox.Text = LauncherDetector.DeriveLauncherName(dialog.FileName, null);
        }

        if (string.IsNullOrWhiteSpace(DirectoryTextBox.Text))
        {
            var folder = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            var beside = Path.Combine(folder, ".minecraft");
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
            DirectoryTextBox.Text = Directory.Exists(beside) ? beside : appData;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var directory = DirectoryTextBox.Text.Trim();
        var executable = ExecutableTextBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(executable))
        {
            if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            {
                SetError("启动器 EXE 不存在，请清空该栏或重新选择。");
                return;
            }
            executable = Path.GetFullPath(executable);
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            if (string.IsNullOrWhiteSpace(executable))
            {
                SetError("请选择启动器 EXE，或填写标准 .minecraft 游戏目录（二选一即可）。");
                DirectoryTextBox.Focus();
                return;
            }

            // 游戏目录是可选项：不填时按 EXE 位置自动寻找，找不到就选 EXE 旁的 .minecraft。
            directory = LauncherDetector.FindMinecraftDirectory(executable) ??
                        Path.Combine(Path.GetDirectoryName(executable)!, ".minecraft");
            DirectoryTextBox.Text = directory;
        }

        if (!Path.IsPathFullyQualified(directory))
        {
            SetError("游戏目录必须是完整的绝对路径。");
            DirectoryTextBox.Focus();
            return;
        }

        string fullDirectory;
        try
        {
            fullDirectory = NormalizeDirectory(directory);
        }
        catch (Exception exception)
        {
            SetError("目录格式无效：" + exception.GetBaseException().Message);
            return;
        }

        if (_existingDirectories.Contains(fullDirectory))
        {
            SetError("这个游戏目录已经添加过，请勿重复添加。");
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = LauncherDetector.DeriveLauncherName(executable, fullDirectory);
        }

        if (!Directory.Exists(fullDirectory))
        {
            var result = MessageBox.Show(this,
                $"目录尚不存在：{Environment.NewLine}{fullDirectory}{Environment.NewLine}{Environment.NewLine}" +
                "仍然添加吗？安装时会按标准 .minecraft 结构创建。",
                "添加启动器", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (result != MessageBoxResult.OK) return;
        }

        Launcher = new CustomLauncherSetting
        {
            Name = name,
            Directory = fullDirectory,
            ExecutablePath = executable
        };
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SetError(string message)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource("DangerBrush");
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
}
