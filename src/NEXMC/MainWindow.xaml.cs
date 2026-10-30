using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using NEXMC.Models;
using NEXMC.Services;

namespace NEXMC;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly INexmcService _service;
    private readonly JsonSettingsService _settingsService = new();
    private readonly ObservableCollection<MinecraftVersion> _minecraftVersions = new();
    private readonly ObservableCollection<LoaderSelection> _loaderSelections = new();
    private readonly ObservableCollection<ModpackProject> _modpackProjects = new();
    private readonly ObservableCollection<ModpackVersion> _modpackVersions = new();
    private readonly ObservableCollection<InstallTaskItem> _tasks = new();
    private readonly ObservableCollection<LauncherChoice> _launcherChoices = new();
    private readonly ObservableCollection<CustomLauncherSetting> _customLaunchers = new();
    private readonly CancellationTokenSource _closingCancellation = new();
    private ICollectionView? _minecraftVersionView;
    private const int ModpackPageSize = 100;

    private UiSettings _settings;
    private int _modpackSearchOffset;
    private int _modpackSearchTotal;
    private bool _modpackSearchHasMore;
    private bool _modpackSearchHasRun;
    private bool _modpackBrowseLoaded;
    private string _modpackFilterKey = string.Empty;
    private string? _bbsmcHelpUrl;
    private bool _captchaWindowOpen;
    private InstallRequest? _captchaRetryRequest;
    private InstallTaskItem? _captchaRetryTask;
    private double _modpackListScrollOffset;
    private CancellationTokenSource? _versionsCancellation;
    private CancellationTokenSource? _loadersCancellation;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _modpackVersionsCancellation;
    private bool _versionsBusy;
    private bool _launchersBusy;
    private bool _loadersBusy;
    private bool _searchBusy;
    private bool _modpackVersionsBusy;
    private bool _gameInstallBusy;
    private bool _modpackInstallBusy;
    private int _activeInstalls;
    private bool _launcherPrompted;
    private bool _isClosing;
    private string? _localModpackPath;
    private string _suggestedGameName = string.Empty;
    private string _suggestedModpackName = string.Empty;
    private readonly string? _settingsLoadError;

    public MainWindow(INexmcService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _settings = new UiSettings();
        InitializeComponent();
        Width = Math.Min(1180, SystemParameters.WorkArea.Width - 48);
        Height = Math.Min(820, SystemParameters.WorkArea.Height - 48);

        _minecraftVersionView = CollectionViewSource.GetDefaultView(_minecraftVersions);
        _minecraftVersionView.Filter = FilterMinecraftVersion;
        MinecraftVersionComboBox.ItemsSource = _minecraftVersionView;
        LoaderVersionComboBox.ItemsSource = _loaderSelections;
        ModpackProjectsList.ItemsSource = _modpackProjects;
        ModpackVersionsList.ItemsSource = _modpackVersions;
        TaskItemsControl.ItemsSource = _tasks;
        LauncherTargetComboBox.ItemsSource = _launcherChoices;
        CustomLauncherList.ItemsSource = _customLaunchers;

        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.6";
        BrandVersionText.Text = $"v{version}";
        FooterVersionText.Text = $"NEXMC {version} · Fluent 2";

        try
        {
            _settings = _settingsService.Load();
        }
        catch (Exception exception)
        {
            _settingsLoadError = exception.ToString();
            _settings = new UiSettings();
        }

        LoadSettingsIntoControls();
        UpdateTaskEmptyState();
        UpdateInstallInputHints();
        UpdateGameInstallState();
        UpdateModpackInstallState();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ScheduleVerificationCapture();
        if (_settingsLoadError is not null)
        {
            ShowStatus($"设置读取失败，当前使用默认显示值。未自动覆盖原设置文件。{Environment.NewLine}{_settingsLoadError}", true);
        }
        else
        {
            ShowStatus("界面已就绪，正在获取真实 Minecraft 版本。", false);
        }

        await RefreshLaunchersAsync();
        if (_launcherChoices.Count == 0) PromptAddLauncherIfNeeded();
        await RefreshVersionsAsync();
    }

    private async void RefreshLaunchers_Click(object sender, RoutedEventArgs e) => await RefreshLaunchersAsync();

    private async void ScheduleVerificationCapture()
    {
        var path = Environment.GetEnvironmentVariable("NEXMC_CAPTURE_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (Environment.GetEnvironmentVariable("NEXMC_CAPTURE_PAGE") is { Length: > 0 } page)
            {
                if (page.Equals("Modpack", StringComparison.OrdinalIgnoreCase)) ModpackNav.IsChecked = true;
                else if (page.Equals("Tasks", StringComparison.OrdinalIgnoreCase)) TasksNav.IsChecked = true;
                else if (page.Equals("Settings", StringComparison.OrdinalIgnoreCase)) SettingsNav.IsChecked = true;
            }
            var delay = int.TryParse(Environment.GetEnvironmentVariable("NEXMC_CAPTURE_DELAY_MS"), out var value)
                ? Math.Clamp(value, 0, 60000)
                : 3500;
            await Task.Delay(delay);
            if (Environment.GetEnvironmentVariable("NEXMC_CAPTURE_BBSMC_HELP") == "1")
            {
                var captchaUrl = Environment.GetEnvironmentVariable("NEXMC_CAPTURE_CAPTCHA_URL");
                ShowBbsmcCaptchaHelp(string.IsNullOrWhiteSpace(captchaUrl)
                    ? "https://bbsmc.net/modpack/ft-wt/version/Xpt3XOj8"
                    : captchaUrl);
            }
            UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            var width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
            var height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
            var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var stream = File.Create(path);
            encoder.Save(stream);
            if (Environment.GetEnvironmentVariable("NEXMC_CAPTURE_EXIT") == "1") Close();
        }
        catch (Exception exception)
        {
            ShowError("界面自检截图", exception);
        }
    }

    private async Task RefreshLaunchersAsync()
    {
        if (_launchersBusy || _isClosing) return;
        _launchersBusy = true;
        LauncherTargetStateText.Text = "正在扫描启动器…";
        try
        {
            var options = await _service.DetectLaunchersAsync(_closingCancellation.Token);
            _launcherChoices.Clear();
            if (options.Count > 1)
                _launcherChoices.Add(new LauncherChoice($"全部已识别启动器（{options.Count} 个）", options));
            foreach (var option in options)
                _launcherChoices.Add(new LauncherChoice(option.DisplayLabel, new[] { option }));
            LauncherTargetComboBox.SelectedIndex = options.Count == 0 ? -1 : 0;
            LauncherTargetStateText.Text = options.Count == 0
                ? "未找到启动器。点击顶部 + 添加启动器。"
                : $"已识别 {options.Count} 个安装目标";
            if (LauncherSettingsStateText is not null)
            {
                var hasHmcl = options.Any(item => item.Name.Contains("HMCL", StringComparison.OrdinalIgnoreCase));
                var names = string.Join("、", options.Take(6).Select(item => item.Name));
                if (options.Count > 6) names += " 等";
                var summary = options.Count == 0
                    ? "当前没有可用安装目标；请添加启动器。"
                    : $"已检测到 {options.Count} 个安装目标：{names}。";
                if (!hasHmcl)
                {
                    summary += " 未检测到 HMCL：如果已下载，请点“添加启动器”选择 HMCL 目录或程序。";
                }
                LauncherSettingsStateText.Text = summary;
            }
            UpdateGameInstallState();
            UpdateModpackInstallState();
        }
        catch (OperationCanceledException) when (_closingCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LauncherTargetStateText.Text = "启动器扫描失败，请检查设置中的游戏目录。";
            ShowError("扫描启动器", exception);
        }
        finally
        {
            _launchersBusy = false;
        }
    }

    private void PromptAddLauncherIfNeeded()
    {
        if (_launcherPrompted || _isClosing)
        {
            return;
        }

        _launcherPrompted = true;
        ShowStatus("没有找到启动器。请添加一个标准 .minecraft 目录作为安装目标。", false);
        _ = AddLauncherAsync();
    }

    private async void AddLauncher_Click(object sender, RoutedEventArgs e) => await AddLauncherAsync();

    private async Task AddLauncherAsync()
    {
        if (_isClosing) return;
        var dialog = new AddLauncherWindow(_customLaunchers.Select(item => item.Directory)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Launcher is null) return;

        var added = dialog.Launcher;
        var normalized = NormalizePathOrOriginal(added.Directory);
        var existing = _customLaunchers.FirstOrDefault(item =>
            string.Equals(NormalizePathOrOriginal(item.Directory),
                normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _customLaunchers.Add(added);
        }
        else
        {
            existing.Name = added.Name;
            existing.Directory = added.Directory;
            existing.ExecutablePath = added.ExecutablePath;
            CustomLauncherList.Items.Refresh();
        }

        SyncCustomLaunchersToSettings();
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception exception)
        {
            ShowError("保存启动器", exception);
            return;
        }

        await RefreshLaunchersAsync();
        SelectLauncherByDirectory(added.Directory);
        UpdateCustomLauncherState();
        ShowStatus($"已添加启动器：{added.Name} · {added.Directory}", false);
    }

    private void SelectLauncherByDirectory(string directory)
    {
        var normalized = NormalizePathOrOriginal(directory);
        foreach (var choice in _launcherChoices)
        {
            if (choice.Targets.Any(target =>
                    string.Equals(NormalizePathOrOriginal(target.Directory),
                        normalized, StringComparison.OrdinalIgnoreCase)))
            {
                LauncherTargetComboBox.SelectedItem = choice;
                return;
            }
        }
    }

    private static string NormalizePathOrOriginal(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return path.Trim(); }
    }

    private async void RemoveLauncher_Click(object sender, RoutedEventArgs e)
    {
        if (CustomLauncherList.SelectedItem is not CustomLauncherSetting selected) return;
        var result = MessageBox.Show(this,
            $"从 NEXMC 的启动器列表中移除以下条目？{Environment.NewLine}{Environment.NewLine}" +
            $"{selected.Name}{Environment.NewLine}{selected.Directory}{Environment.NewLine}{Environment.NewLine}" +
            "不会删除游戏目录或启动器文件。",
            "移除启动器", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;

        _customLaunchers.Remove(selected);
        SyncCustomLaunchersToSettings();
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception exception)
        {
            ShowError("保存启动器", exception);
            return;
        }

        await RefreshLaunchersAsync();
        UpdateCustomLauncherState();
        ShowStatus("已从启动器列表移除；游戏文件未删除。", false);
    }

    private void OpenSelectedLauncher_Click(object sender, RoutedEventArgs e)
    {
        if (CustomLauncherList.SelectedItem is not CustomLauncherSetting selected) return;
        InstallCompleteWindow.OpenLauncher(selected.ExecutablePath, selected.Name);
    }

    private void CustomLauncherList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCustomLauncherState();
    }

    private void UpdateCustomLauncherState()
    {
        if (RemoveLauncherButton is null || OpenSelectedLauncherButton is null ||
            CustomLauncherEmptyText is null)
        {
            return;
        }

        var hasSelection = CustomLauncherList.SelectedItem is CustomLauncherSetting;
        RemoveLauncherButton.IsEnabled = hasSelection;
        OpenSelectedLauncherButton.IsEnabled = hasSelection &&
            CustomLauncherList.SelectedItem is CustomLauncherSetting { ExecutablePath.Length: > 0 };
        CustomLauncherList.Visibility = _customLaunchers.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        CustomLauncherEmptyText.Visibility = _customLaunchers.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SyncCustomLaunchersToSettings()
    {
        _settings.CustomLaunchers = _customLaunchers.Select(item => new CustomLauncherSetting
        {
            Name = item.Name,
            Directory = item.Directory,
            ExecutablePath = item.ExecutablePath
        }).ToList();
    }

    private IReadOnlyList<LauncherOption> SelectedLaunchers()
    {
        var choice = LauncherTargetComboBox.SelectedItem as LauncherChoice
            ?? throw new ArgumentException("请先选择一个启动器安装目标。");
        return choice.Targets;
    }

    private static string? FindVersionConflict(IReadOnlyList<LauncherOption> targets, string instanceName)
    {
        foreach (var target in targets)
        {
            var versionDirectory = Path.Combine(target.Directory, "versions", instanceName);
            if (File.Exists(Path.Combine(versionDirectory, instanceName + ".json")) ||
                (Directory.Exists(versionDirectory) &&
                 Directory.EnumerateFileSystemEntries(versionDirectory).Any()))
            {
                return versionDirectory;
            }
        }
        return null;
    }

    private bool IsDownloadOnlyMode =>
        string.Equals((DownloadModeComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
            "local", StringComparison.OrdinalIgnoreCase);

    private string DownloadOnlyDirectory => (LocalDownloadDirectoryTextBox?.Text ?? string.Empty).Trim();

    private IReadOnlyList<LauncherOption> RequestTargets()
    {
        if (!IsDownloadOnlyMode)
        {
            return SelectedLaunchers();
        }

        var directory = DownloadOnlyDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("仅下载模式需要先在设置里选择本地保存文件夹。");
        }

        // 目录创建交给后台的 DownloadOnlyAsync；这里只做路径校验，避免网络盘/移动盘在 UI 线程卡住。
        return new[]
        {
            new LauncherOption("local-download", "仅下载到本地", LauncherKind.Generic, directory, null, false)
            {
                IsDownloadTarget = true
            }
        };
    }

    private void DownloadMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateDownloadModeUi();
        UpdateGameInstallState();
        UpdateModpackInstallState();
    }

    private void LocalDownloadDirectory_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateGameInstallState();
        UpdateModpackInstallState();
    }

    private void UpdateDownloadModeUi()
    {
        var local = IsDownloadOnlyMode;
        if (LocalDownloadDirectoryTextBox is not null) LocalDownloadDirectoryTextBox.IsEnabled = local;
        if (BrowseLocalDownloadButton is not null) BrowseLocalDownloadButton.IsEnabled = local;
    }

    private void BrowseLocalDownload_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择仅下载模式的保存文件夹", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        LocalDownloadDirectoryTextBox.Text = dialog.FolderName;
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string page })
        {
            return;
        }

        GamePage.Visibility = page == "Game" ? Visibility.Visible : Visibility.Collapsed;
        ModpackPage.Visibility = page == "Modpack" ? Visibility.Visible : Visibility.Collapsed;
        TasksPage.Visibility = page == "Tasks" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;

        if (page == "Modpack" && !_modpackBrowseLoaded && !_searchBusy && !_isClosing)
        {
            _modpackBrowseLoaded = true;
            ModpackSearchStateText.Text = "正在加载全部整合包…";
            _ = SearchModpacksAsync(false);
        }

        if (page == "Settings")
        {
            UpdateCustomLauncherState();
            SettingsPage.ScrollToTop();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                Keyboard.ClearFocus();
                SettingsPage.ScrollToTop();
            }));
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeGlyphText is null || MaximizeButton is null)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyphText.Text = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 鼠标已经在拖动过程中释放。
            }
        }
    }

    private void CloseErrorBanner_Click(object sender, RoutedEventArgs e) => ClearErrorBanner();

    private void ShowErrorBanner(string message)
    {
        if (ErrorBanner is null || ErrorBannerText is null)
        {
            return;
        }

        ErrorBannerText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
        _bbsmcHelpUrl = null;
        if (ErrorOpenBbsmcButton is not null) ErrorOpenBbsmcButton.Visibility = Visibility.Collapsed;
        if (ErrorChooseLocalButton is not null) ErrorChooseLocalButton.Visibility = Visibility.Collapsed;
    }

    private void ClearErrorBanner()
    {
        if (ErrorBanner is not null)
        {
            ErrorBanner.Visibility = Visibility.Collapsed;
        }
        _bbsmcHelpUrl = null;
    }

    private void ShowBbsmcCaptchaHelp(string? projectUrl)
    {
        if (ErrorBanner is null || ErrorBannerText is null ||
            ErrorOpenBbsmcButton is null || ErrorChooseLocalButton is null)
        {
            return;
        }

        _bbsmcHelpUrl = Uri.TryCreate(projectUrl, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.AbsoluteUri
            : "https://bbsmc.net/";
        ErrorBannerText.Text = BbsmcCaptchaHelper.BuildMessage();
        ErrorOpenBbsmcButton.Visibility = Visibility.Visible;
        ErrorChooseLocalButton.Visibility = Visibility.Visible;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private bool ShowBbsmcHelpIfNeeded(InstallRequest request, string failureText, InstallTaskItem? task = null)
    {
        if (!BbsmcCaptchaHelper.IsCaptchaFailure(failureText) ||
            (string.IsNullOrWhiteSpace(request.ModpackProjectId) &&
             string.IsNullOrWhiteSpace(request.LocalModpackPath)))
        {
            return false;
        }

        // 验证通过后浏览器会把文件下载到本地；此时用本地包自动补装失败的目标，
        // 不再让用户看着一个已经失败的任务手动点第二次安装。
        var failedTargetIds = task?.Targets
            .Where(item => item.Success != true)
            .Select(item => item.Option.Id)
            ?? Enumerable.Empty<string>();
        _captchaRetryRequest = request with
        {
            Targets = InstallRetryRequestFactory.SelectTargets(request, failedTargetIds)
        };
        _captchaRetryTask = task;

        var project = request.ModpackProjectId is null
            ? null
            : _modpackProjects.FirstOrDefault(item =>
                string.Equals(item.Id, request.ModpackProjectId, StringComparison.OrdinalIgnoreCase));
        var url = project?.ProjectUrl;
        if (!string.IsNullOrWhiteSpace(url) &&
            url.Contains("/modpack/", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(request.ModpackVersionId))
        {
            url = url.TrimEnd('/') + "/version/" + Uri.EscapeDataString(request.ModpackVersionId);
        }
        ShowBbsmcCaptchaHelp(url);
        return true;
    }

    private void ErrorOpenBbsmc_Click(object sender, RoutedEventArgs e) => OpenBbsmcCaptchaWindow();

    private async void OpenBbsmcCaptchaWindow()
    {
        if (_captchaWindowOpen || _isClosing)
        {
            return;
        }

        _captchaWindowOpen = true;
        try
        {
            var retryRequest = _captchaRetryRequest;
            var retryTask = _captchaRetryTask;
            _captchaRetryRequest = null;
            _captchaRetryTask = null;
            var url = string.IsNullOrWhiteSpace(_bbsmcHelpUrl) ? "https://bbsmc.net/" : _bbsmcHelpUrl;
            var window = new BbsmcCaptchaWindow(url) { Owner = this };
            var result = window.ShowDialog();
            if (result == true && !string.IsNullOrWhiteSpace(window.DownloadedPath) && File.Exists(window.DownloadedPath))
            {
                if (ApplyLocalModpackPath(window.DownloadedPath))
                {
                    if (retryRequest is not null && !_isClosing)
                    {
                        await StartCaptchaRetryAsync(retryRequest, window.DownloadedPath, retryTask);
                    }
                    else
                    {
                        ShowStatus("BBSMC 文件已下载完成，已切换为本地整合包；确认实例名后点击“安装整合包”。", false);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            ShowError("打开 BBSMC 验证窗口", exception);
        }
        finally
        {
            _captchaWindowOpen = false;
        }
    }

    private async Task StartCaptchaRetryAsync(InstallRequest retryRequest, string downloadedPath,
        InstallTaskItem? retryTask)
    {
        var localRequest = InstallRetryRequestFactory.CreateLocalRetry(
            retryRequest, downloadedPath, retryRequest.Targets.Select(target => target.Id));
        InstallTaskItem task;
        // 全部目标都失败时复用原任务，让它从“失败”变为“重新安装”再变为成功；
        // 如果已有目标成功，则不重置成功目标，另建一个只包含失败目标的任务。
        if (retryTask is not null && !retryTask.Cancellation.IsCancellationRequested &&
            retryTask.Targets.All(target => target.Success != true))
        {
            task = retryTask;
            task.MarkRetried();
        }
        else
        {
            var title = $"{localRequest.InstanceName} · {Path.GetFileName(downloadedPath)}" +
                        (localRequest.DownloadOnly ? " · 仅下载" : "");
            task = new InstallTaskItem(title, localRequest.Targets, localRequest.DownloadOnly);
            AddTask(task);
        }
        TasksNav.IsChecked = true;
        ShowTasksPage();
        ShowStatus("BBSMC 验证通过，文件已下载，正在自动继续安装…", false);
        await RunInstallAsync(task, localRequest);
    }

    private void ErrorChooseLocal_Click(object sender, RoutedEventArgs e)
    {
        if (BrowseLocalModpack())
        {
            ClearErrorBanner();
        }
    }

    private async void RefreshVersions_Click(object sender, RoutedEventArgs e) => await RefreshVersionsAsync();

    private async Task RefreshVersionsAsync()
    {
        if (_versionsBusy || _isClosing)
        {
            return;
        }

        _versionsBusy = true;
        ClearErrorBanner();
        _versionsCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_closingCancellation.Token);
        _versionsCancellation = cancellation;
        RefreshVersionsButton.IsEnabled = false;
        RefreshVersionsButton.Content = "刷新中…";
        MinecraftVersionComboBox.IsEnabled = false;
        _loadersCancellation?.Cancel();
        _minecraftVersions.Clear();
        _loaderSelections.Clear();
        MinecraftVersionComboBox.SelectedItem = null;
        LoaderVersionComboBox.SelectedItem = null;
        VersionStateText.Text = "正在从真实版本源加载，请稍候…";
        ShowStatus("正在刷新 Minecraft 版本。", false);
        UpdateGameInstallState();

        try
        {
            var versions = await _service.GetMinecraftVersionsAsync(true, cancellation.Token);
            foreach (var version in versions)
            {
                _minecraftVersions.Add(version);
            }

            _minecraftVersionView?.Refresh();
            UpdateVersionCategoryState();
            var firstVisible = _minecraftVersionView?.Cast<MinecraftVersion>().FirstOrDefault();
            MinecraftVersionComboBox.IsEnabled = firstVisible is not null;
            if (firstVisible is not null)
            {
                MinecraftVersionComboBox.SelectedItem = firstVisible;
            }
            ShowStatus(versions.Count == 0 ? "版本源未返回条目。" : $"Minecraft 版本刷新完成，共 {versions.Count} 项。", false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_isClosing)
            {
                VersionStateText.Text = "版本刷新已取消。";
                ShowStatus("版本刷新已取消。", false);
            }
        }
        catch (Exception exception)
        {
            VersionStateText.Text = "版本加载失败，请根据错误信息检查后重试。";
            ShowError("刷新 Minecraft 版本", exception);
        }
        finally
        {
            if (ReferenceEquals(_versionsCancellation, cancellation))
            {
                _versionsCancellation = null;
                _versionsBusy = false;
                RefreshVersionsButton.IsEnabled = !_isClosing;
                RefreshVersionsButton.Content = "刷新版本";
            }
            cancellation.Dispose();
            UpdateGameInstallState();
        }
    }

    private bool FilterMinecraftVersion(object item)
    {
        if (item is not MinecraftVersion version)
        {
            return false;
        }

        var category = (VersionCategoryComboBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "全部版本";
        return category == "全部版本" || version.Category == category;
    }

    private void VersionCategory_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_minecraftVersionView is null || MinecraftVersionComboBox is null || VersionStateText is null)
        {
            return;
        }

        _minecraftVersionView.Refresh();
        UpdateVersionCategoryState();
        if (MinecraftVersionComboBox.SelectedItem is not MinecraftVersion selected ||
            !_minecraftVersionView.Contains(selected))
        {
            var first = _minecraftVersionView.Cast<MinecraftVersion>().FirstOrDefault();
            MinecraftVersionComboBox.SelectedItem = first;
            MinecraftVersionComboBox.IsEnabled = first is not null;
        }
        UpdateGameInstallState();
    }

    private void UpdateVersionCategoryState()
    {
        if (VersionStateText is null)
        {
            return;
        }

        if (_minecraftVersions.Count == 0)
        {
            VersionStateText.Text = "尚未加载版本，点击“刷新版本”获取真实列表。";
            return;
        }

        var category = (VersionCategoryComboBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "全部版本";
        var visible = _minecraftVersionView?.Cast<MinecraftVersion>().Count() ?? 0;
        VersionStateText.Text = $"已加载 {_minecraftVersions.Count} 个真实版本；{category}：{visible} 个。";
    }

    private async void MinecraftVersion_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MinecraftVersionComboBox.SelectedItem is MinecraftVersion version &&
            (string.IsNullOrWhiteSpace(GameInstanceNameTextBox.Text) || GameInstanceNameTextBox.Text == _suggestedGameName))
        {
            _suggestedGameName = version.Id;
            GameInstanceNameTextBox.Text = version.Id;
        }
        await LoadLoadersAsync();
    }

    private async void LoaderKind_Changed(object sender, SelectionChangedEventArgs e) => await LoadLoadersAsync();

    private async Task LoadLoadersAsync()
    {
        if (LoaderVersionComboBox is null || LoaderKindComboBox is null) return;
        _loadersCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_closingCancellation.Token);
        _loadersCancellation = cancellation;
        _loadersBusy = true;
        _loaderSelections.Clear();
        LoaderVersionComboBox.SelectedItem = null;
        LoaderVersionComboBox.IsEnabled = false;
        UpdateGameInstallState();

        var version = MinecraftVersionComboBox.SelectedItem as MinecraftVersion;
        var kind = (LoaderKindComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        if (version is null || kind.Length == 0 || _isClosing)
        {
            _loadersBusy = false;
            if (ReferenceEquals(_loadersCancellation, cancellation))
            {
                _loadersCancellation = null;
            }
            cancellation.Dispose();
            UpdateGameInstallState();
            return;
        }

        ShowStatus($"正在获取 {version.Id} 的 {kind} 加载器版本。", false);
        try
        {
            var loaders = await _service.GetLoadersAsync(version.Id, kind, cancellation.Token);
            foreach (var loader in loaders)
            {
                _loaderSelections.Add(loader);
            }

            LoaderVersionComboBox.IsEnabled = loaders.Count > 0;
            if (loaders.Count > 0)
            {
                LoaderVersionComboBox.SelectedIndex = 0;
            }
            ShowStatus(loaders.Count == 0
                ? $"{version.Id} 未返回可用的 {kind} 版本。"
                : $"已加载 {loaders.Count} 个 {kind} 版本。", false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 新选择或关闭窗口会取消旧请求，无需弹出错误。
        }
        catch (Exception exception)
        {
            ShowError($"加载 {kind} 版本", exception);
        }
        finally
        {
            if (ReferenceEquals(_loadersCancellation, cancellation))
            {
                _loadersCancellation = null;
                _loadersBusy = false;
            }
            cancellation.Dispose();
            UpdateGameInstallState();
        }
    }

    private void InstallInput_Changed(object sender, RoutedEventArgs e)
    {
        UpdateInstallInputHints();
        UpdateGameInstallState();
        UpdateModpackSelectionSummary();
        UpdateModpackInstallState();
    }

    private void UpdateInstallInputHints()
    {
        if (GameNameValidationText is null || ModpackNameValidationText is null)
        {
            return;
        }

        UpdateInstallInputHint(GameNameValidationText, GameInstanceNameTextBox?.Text);
        UpdateInstallInputHint(ModpackNameValidationText, ModpackInstanceNameTextBox?.Text);
    }

    private void UpdateInstallInputHint(TextBlock target, string? value)
    {
        var error = InstallInputValidator.GetInstanceNameError(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            target.Text = "实例名称不能为空；不能包含路径字符，不能使用 Windows 保留名。";
            target.Foreground = (Brush)FindResource("MutedBrush");
        }
        else if (error is not null)
        {
            target.Text = "名称不可用：" + error;
            target.Foreground = (Brush)FindResource("TextBrush");
        }
        else
        {
            target.Text = "名称可用。安装完成后会再校验一次实际文件。";
            target.Foreground = (Brush)FindResource("MutedBrush");
        }
    }

    private void UpdateGameInstallState()
    {
        if (GameInstallButton is null)
        {
            return;
        }

        var hasVersion = MinecraftVersionComboBox.SelectedItem is MinecraftVersion;
        var kind = (LoaderKindComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        var hasLoader = kind.Length == 0 || LoaderVersionComboBox.SelectedItem is LoaderSelection;
        var downloadOnly = IsDownloadOnlyMode;
        var hasTarget = downloadOnly
            ? !string.IsNullOrWhiteSpace(DownloadOnlyDirectory) && Path.IsPathFullyQualified(DownloadOnlyDirectory)
            : LauncherTargetComboBox.SelectedItem is LauncherChoice;
        var nameValid = InstallInputValidator.GetInstanceNameError(GameInstanceNameTextBox?.Text) is null;
        GameInstallButton.IsEnabled = !_gameInstallBusy && !_versionsBusy && !_loadersBusy && hasTarget &&
            hasVersion && hasLoader && nameValid && !_isClosing;
        GameInstallButton.Content = downloadOnly ? "开始下载" : "开始安装";
    }

    private async void GameInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_gameInstallBusy)
        {
            return;
        }

        _gameInstallBusy = true;
        try
        {
            var request = BuildGameInstallRequest();
            var task = new InstallTaskItem(
                $"{request.InstanceName} · Minecraft {request.MinecraftVersion}" + (request.DownloadOnly ? " · 仅下载" : ""),
                request.Targets, request.DownloadOnly);
            AddTask(task);
            _gameInstallBusy = false;
            UpdateGameInstallState();
            TasksNav.IsChecked = true;
            ShowTasksPage();
            await RunInstallAsync(task, request);
        }
        catch (Exception exception)
        {
            ShowError("准备游戏安装", exception);
        }
        finally
        {
            _gameInstallBusy = false;
            UpdateGameInstallState();
        }
    }

    private InstallRequest BuildGameInstallRequest()
    {
        if (MinecraftVersionComboBox.SelectedItem is not MinecraftVersion version)
        {
            throw new ArgumentException("请先刷新并选择一个 Minecraft 版本。");
        }

        var instanceName = InstallInputValidator.ValidateInstanceName(GameInstanceNameTextBox.Text);
        var kind = (LoaderKindComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        LoaderSelection? loader = null;
        if (kind.Length > 0)
        {
            loader = LoaderVersionComboBox.SelectedItem as LoaderSelection
                ?? throw new ArgumentException($"请选择一个 {kind} 加载器版本。");
        }

        return new InstallRequest(instanceName, version.Id, kind.Length == 0 ? null : kind,
            loader?.Version, null, null, null, RequestTargets(),
            IsDownloadOnlyMode, IsDownloadOnlyMode ? DownloadOnlyDirectory : null);
    }

    private async void SearchModpacks_Click(object sender, RoutedEventArgs e) => await SearchModpacksAsync(false);

    private async void LoadMoreModpacks_Click(object sender, RoutedEventArgs e) => await SearchModpacksAsync(true);

    private async void ModpackSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SearchModpacksAsync(false);
        }
    }

    private void ModpackSourceButton_Click(object sender, RoutedEventArgs e)
    {
        ModpackSourcePopup.IsOpen = !ModpackSourcePopup.IsOpen;
    }

    private void ModpackSourceDone_Click(object sender, RoutedEventArgs e)
    {
        ModpackSourcePopup.IsOpen = false;
    }

    private void ModpackSourceSelectAll_Click(object sender, RoutedEventArgs e)
    {
        SourceBbsmcCheckBox.IsChecked = true;
        SourceModrinthCheckBox.IsChecked = true;
        SourceCurseForgeCheckBox.IsChecked = true;
    }

    private async void ModpackSourceCheck_Changed(object sender, RoutedEventArgs e)
    {
        UpdateModpackSourceButton();
        if (IsLoaded && _modpackSearchHasRun && !_searchBusy &&
            CurrentModpackFilterKey() != _modpackFilterKey)
        {
            await SearchModpacksAsync(false);
        }
    }

    private async void ModpackCategory_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && _modpackSearchHasRun && !_searchBusy &&
            CurrentModpackFilterKey() != _modpackFilterKey)
        {
            await SearchModpacksAsync(false);
        }
    }

    private IReadOnlyList<string> SelectedModpackSources()
    {
        var sources = new List<string>(3);
        if (SourceBbsmcCheckBox?.IsChecked == true) sources.Add("BBSMC");
        if (SourceModrinthCheckBox?.IsChecked == true) sources.Add("Modrinth");
        if (SourceCurseForgeCheckBox?.IsChecked == true) sources.Add("CurseForge");
        return sources;
    }

    private void UpdateModpackSourceButton()
    {
        if (ModpackSourceButton is null) return;
        var sources = SelectedModpackSources();
        ModpackSourceButton.Content = ModpackSources.Display(sources);
        ModpackSourceButton.ToolTip = sources.Count == 0
            ? "至少勾选一个来源"
            : "已勾选：" + string.Join("、", sources);
        if (SearchModpacksButton is not null)
        {
            SearchModpacksButton.IsEnabled = !_searchBusy && !_isClosing && sources.Count > 0;
        }
    }

    private void ApplyDefaultModpackSource(string? source)
    {
        var selected = ModpackSources.Parse(source);
        if (SourceBbsmcCheckBox is not null)
        {
            SourceBbsmcCheckBox.IsChecked = selected.Contains("BBSMC", StringComparer.OrdinalIgnoreCase);
            SourceModrinthCheckBox.IsChecked = selected.Contains("Modrinth", StringComparer.OrdinalIgnoreCase);
            SourceCurseForgeCheckBox.IsChecked = selected.Contains("CurseForge", StringComparer.OrdinalIgnoreCase);
        }
        UpdateModpackSourceButton();
    }

    private string CurrentModpackFilterKey() =>
        ModpackSources.Join(SelectedModpackSources()) + "|" +
        SelectedComboBoxTag(ModpackCategoryComboBox, string.Empty) + "|" +
        (ModpackGameVersionTextBox.Text ?? string.Empty).Trim() + "|" +
        (ModpackSearchTextBox.Text ?? string.Empty).Trim();

    private async Task SearchModpacksAsync(bool append)
    {
        if (_searchBusy || _isClosing || (append && (!_modpackSearchHasMore || _modpackProjects.Count == 0)))
        {
            return;
        }
        if (SelectedModpackSources().Count == 0)
        {
            ModpackSearchStateText.Text = "请至少勾选一个来源";
            ModpackProjectsEmptyText.Text = "在「来源」里至少勾选一个下载来源。";
            ModpackProjectsEmptyText.Visibility = Visibility.Visible;
            return;
        }

        _searchBusy = true;
        ClearErrorBanner();
        _searchCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_closingCancellation.Token);
        _searchCancellation = cancellation;
        SearchModpacksButton.IsEnabled = false;
        SearchModpacksButton.Content = append ? "加载中…" : "搜索中…";
        LoadMoreModpacksButton.IsEnabled = false;
        ModpackSearchStateText.Text = append ? "正在加载更多…" : "正在搜索";
        if (!append)
        {
            ModpackProjectsEmptyText.Text = "正在查询真实项目来源…";
            ModpackProjectsEmptyText.Visibility = Visibility.Visible;
            _modpackProjects.Clear();
            _modpackVersions.Clear();
            ModpackProjectsList.SelectedItem = null;
            _modpackSearchOffset = 0;
            _modpackSearchTotal = 0;
            _modpackSearchHasMore = false;
        }
        else
        {
            ModpackProjectsEmptyText.Visibility = Visibility.Collapsed;
        }
        UpdateModpackInstallState();

        try
        {
            var selectedSources = SelectedModpackSources();
            if (selectedSources.Count == 0)
            {
                throw new InvalidOperationException("请至少勾选一个整合包来源。");
            }
            var category = (ModpackCategoryComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
            var gameVersion = string.IsNullOrWhiteSpace(ModpackGameVersionTextBox.Text)
                ? null
                : ModpackGameVersionTextBox.Text.Trim();
            _modpackFilterKey = ModpackSources.Join(selectedSources) + "|" + category + "|" +
                                (gameVersion ?? string.Empty) + "|" + ModpackSearchTextBox.Text.Trim();
            var offset = append ? _modpackSearchOffset : 0;
            var perSourceLimit = Math.Max(10, (int)Math.Ceiling(ModpackPageSize / (double)selectedSources.Count));
            var warnings = new List<string>();
            var receivedSources = 0;
            var totalHits = 0;
            ModpackSearchStateText.Text = selectedSources.Count > 1
                ? $"正在同时查询 {selectedSources.Count} 个来源；先返回的结果先显示…"
                : "正在查询来源…";
            await foreach (var chunk in _service.SearchAllModpacksIncrementalAsync(
                               ModpackSearchTextBox.Text.Trim(), selectedSources, gameVersion, category, offset,
                               ModpackPageSize, cancellation.Token))
            {
                receivedSources++;
                totalHits += chunk.TotalHits;
                if (!string.IsNullOrWhiteSpace(chunk.Warning))
                {
                    warnings.Add(chunk.Warning);
                }
                foreach (var project in chunk.Projects)
                {
                    if (_modpackProjects.All(existing =>
                            !string.Equals(existing.Source + "|" + existing.Id,
                                project.Source + "|" + project.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        _modpackProjects.Add(project);
                    }
                }

                ModpackSearchStateText.Text =
                    $"已显示 {_modpackProjects.Count:N0} 个；已返回 {receivedSources}/{selectedSources.Count} 个来源…";
                if (_modpackProjects.Count > 0)
                {
                    ModpackProjectsEmptyText.Visibility = Visibility.Collapsed;
                }
            }

            if (_modpackProjects.Count == 0 && warnings.Count == selectedSources.Count)
            {
                throw new InvalidOperationException("所选整合包来源均失败：" + string.Join(" | ", warnings));
            }

            _modpackSearchOffset = offset + perSourceLimit;
            _modpackSearchTotal = Math.Max(Math.Max(_modpackSearchTotal, totalHits), _modpackProjects.Count);
            _modpackSearchHasMore = _modpackProjects.Count > 0 && _modpackSearchOffset < _modpackSearchTotal;
            var warningText = warnings.Count > 0 ? $"；{warnings.Count} 个来源暂不可用" : string.Empty;
            ModpackSearchStateText.Text =
                $"{ModpackSources.Display(selectedSources)}：已显示 {_modpackProjects.Count:N0} / 共 {_modpackSearchTotal:N0} 个{warningText}";
            LoadMoreModpacksButton.Visibility = _modpackSearchHasMore ? Visibility.Visible : Visibility.Collapsed;
            ModpackProjectsEmptyText.Text = _modpackProjects.Count == 0
                ? "没有找到符合条件的项目，请调整关键词、分类或来源。"
                : string.Empty;
            ModpackProjectsEmptyText.Visibility = _modpackProjects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _modpackSearchHasRun = true;
            var summary = _modpackProjects.Count == 0
                ? "整合包搜索没有返回项目。"
                : $"整合包搜索完成，共 {_modpackSearchTotal:N0} 个，当前显示 {_modpackProjects.Count:N0} 个。";
            if (warnings.Count > 0) summary += $" 部分来源失败：{string.Join("；", warnings)}";
            ShowStatus(summary, false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_isClosing)
            {
                ModpackSearchStateText.Text = "搜索已取消";
                if (!append && _modpackProjects.Count == 0)
                {
                    ModpackProjectsEmptyText.Text = "搜索已取消。";
                }
            }
        }
        catch (Exception exception)
        {
            ModpackSearchStateText.Text = append ? "加载更多失败" : "搜索失败";
            if (!append || _modpackProjects.Count == 0)
            {
                ModpackProjectsEmptyText.Text = "搜索失败，请根据顶部错误提示重试。";
                ModpackProjectsEmptyText.Visibility = Visibility.Visible;
            }
            ShowError(append ? "加载更多整合包" : "搜索整合包", exception);
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation = null;
                _searchBusy = false;
                SearchModpacksButton.IsEnabled = !_isClosing && SelectedModpackSources().Count > 0;
                SearchModpacksButton.Content = "搜索";
                LoadMoreModpacksButton.IsEnabled = !_isClosing && _modpackSearchHasMore;
                if (!_isClosing && SelectedModpackSources().Count > 0 &&
                    CurrentModpackFilterKey() != _modpackFilterKey)
                {
                    // 搜索期间用户又改了来源/分类，完成后按最新条件再查一次。
                    _ = SearchModpacksAsync(false);
                }
            }
            cancellation.Dispose();
            UpdateModpackInstallState();
        }
    }

    private async void ModpackProject_Changed(object sender, SelectionChangedEventArgs e)
    {
        _modpackVersionsCancellation?.Cancel();
        _modpackVersions.Clear();
        ModpackVersionsList.SelectedItem = null;
        ModpackVersionsStateText.Text = string.Empty;
        ModpackVersionsEmptyText.Text = "该项目没有返回版本。";
        ModpackVersionsEmptyText.Visibility = Visibility.Collapsed;

        if (ModpackProjectsList.SelectedItem is not ModpackProject project)
        {
            SelectedModpackNameText.Text = "选择一个整合包";
            SelectModpackVersionsButton.IsEnabled = false;
            OpenModpackPageButton.IsEnabled = false;
            OpenModpackPageButton.ToolTip = "选择项目后可打开介绍页";
            ModpackDetailHintText.Text = "在整合包列表中点开一个项目。";
            ModpackDetailHintText.Visibility = Visibility.Visible;
            ShowModpackListView();
            UpdateModpackSelectionSummary();
            UpdateModpackInstallState();
            return;
        }

        ShowModpackVersionView();
        SelectedModpackNameText.Text = project.Name;
        SelectModpackVersionsButton.IsEnabled = true;
        SelectModpackVersionsButton.Content = "重新加载版本";
        OpenModpackPageButton.IsEnabled = Uri.TryCreate(project.ProjectUrl, UriKind.Absolute, out var projectUri) &&
            (projectUri.Scheme == Uri.UriSchemeHttp || projectUri.Scheme == Uri.UriSchemeHttps);
        OpenModpackPageButton.ToolTip = string.IsNullOrWhiteSpace(project.ProjectUrl) ? "该项目没有可打开的介绍页" : project.ProjectUrl;
        ClearLocalModpackSelection();
        // 每次切换到另一个整合包都重新用该项目原名作为默认实例名；
        // 用户上一次改名不应带到下一个整合包。
        _suggestedModpackName = SuggestInstanceName(project.Name);
        ModpackInstanceNameTextBox.Text = _suggestedModpackName;
        ModpackDetailHintText.Text = "正在加载版本…";
        ModpackDetailHintText.Visibility = Visibility.Visible;
        UpdateModpackSelectionSummary();
        UpdateModpackInstallState();
        await LoadModpackVersionsAsync(project);
    }

    private void ShowModpackListView()
    {
        ModpackListView.Visibility = Visibility.Visible;
        ModpackVersionView.Visibility = Visibility.Collapsed;

        // 版本页回来时列表会被重新布局；等布局完成后再恢复原来的滚动位置。
        var offset = _modpackListScrollOffset;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (ModpackListView.Visibility != Visibility.Visible) return;
            ModpackProjectsList.UpdateLayout();
            FindScrollViewer(ModpackProjectsList)?.ScrollToVerticalOffset(offset);
        }));
    }

    private void ShowModpackVersionView()
    {
        // 只从列表页进入版本页时记录位置；版本页内部再次调用不能把记录覆盖成 0。
        if (ModpackListView.Visibility == Visibility.Visible)
        {
            var viewer = FindScrollViewer(ModpackProjectsList);
            if (viewer is not null) _modpackListScrollOffset = viewer.VerticalOffset;
        }
        ModpackListView.Visibility = Visibility.Collapsed;
        ModpackVersionView.Visibility = Visibility.Visible;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer scrollViewer) return scrollViewer;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, index));
            if (found is not null) return found;
        }
        return null;
    }

    private void BackToModpackList_Click(object sender, RoutedEventArgs e)
    {
        ModpackProjectsList.SelectedItem = null;
        ModpackVersionsList.SelectedItem = null;
        ClearLocalModpackSelection();
        ShowModpackListView();
        UpdateModpackInstallState();
    }

    private async void SelectModpackVersions_Click(object sender, RoutedEventArgs e)
    {
        if (ModpackProjectsList.SelectedItem is not ModpackProject project || _modpackVersionsBusy)
        {
            return;
        }

        ModpackDetailHintText.Text = "正在加载版本…";
        ModpackDetailHintText.Visibility = Visibility.Visible;
        SelectModpackVersionsButton.Content = "重新加载版本";
        await LoadModpackVersionsAsync(project);
    }

    private async Task LoadModpackVersionsAsync(ModpackProject project)
    {
        _modpackVersionsCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_closingCancellation.Token);
        _modpackVersionsCancellation = cancellation;
        _modpackVersionsBusy = true;
        _modpackVersions.Clear();
        ModpackVersionsList.SelectedItem = null;
        ModpackVersionsStateText.Text = "正在加载…";
        ModpackVersionsEmptyText.Visibility = Visibility.Collapsed;
        ModpackDetailHintText.Text = "正在加载真实版本列表…";
        ModpackDetailHintText.Visibility = Visibility.Visible;
        SelectModpackVersionsButton.IsEnabled = false;
        UpdateModpackInstallState();

        try
        {
            var versions = await _service.GetModpackVersionsAsync(project.Id, project.Source, cancellation.Token);
            foreach (var version in versions)
            {
                _modpackVersions.Add(version);
            }

            ModpackVersionsStateText.Text = $"{versions.Count} 个版本";
            ModpackDetailHintText.Visibility = Visibility.Collapsed;
            ModpackVersionsEmptyText.Text = "该项目未返回版本。";
            ModpackVersionsEmptyText.Visibility = versions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (versions.Count > 0)
            {
                ModpackVersionsList.SelectedItem = versions.FirstOrDefault(version => version.IsDirectDownload)
                    ?? versions[0];
            }
            ShowStatus($"已加载 {project.Name} 的 {versions.Count} 个版本。", false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 项目切换会取消旧请求。
        }
        catch (Exception exception)
        {
            ModpackVersionsStateText.Text = "加载失败";
            ModpackVersionsEmptyText.Visibility = Visibility.Collapsed;
            ModpackDetailHintText.Text = "版本加载失败，请根据顶部错误提示重试。";
            ModpackDetailHintText.Visibility = Visibility.Visible;
            ShowError("加载整合包版本", exception);
        }
        finally
        {
            if (ReferenceEquals(_modpackVersionsCancellation, cancellation))
            {
                _modpackVersionsCancellation = null;
                _modpackVersionsBusy = false;
            }
            SelectModpackVersionsButton.IsEnabled = ModpackProjectsList.SelectedItem is ModpackProject;
            cancellation.Dispose();
            UpdateModpackSelectionSummary();
            UpdateModpackInstallState();
        }
    }

    private void BrowseLocalModpack_Click(object sender, RoutedEventArgs e) => BrowseLocalModpack();

    private bool BrowseLocalModpack()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择本地整合包",
            Filter = "整合包文件 (*.mrpack;*.zip)|*.mrpack;*.zip|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        return ApplyLocalModpackPath(dialog.FileName);
    }

    private bool ApplyLocalModpackPath(string path)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ShowError("选择本地整合包", new ArgumentException("仅支持 .mrpack 或 .zip 文件。"));
            return false;
        }

        _localModpackPath = path;
        LocalModpackPathText.Text = "本地包：" + Path.GetFileName(_localModpackPath);
        LocalModpackPathText.ToolTip = _localModpackPath;
        ClearLocalModpackButton.IsEnabled = true;
        ModpackProjectsList.SelectedItem = null;
        _modpackVersions.Clear();
        ShowModpackVersionView();
        SelectedModpackNameText.Text = Path.GetFileName(_localModpackPath);
        ModpackVersionsStateText.Text = "本地包";
        ModpackVersionsEmptyText.Visibility = Visibility.Collapsed;
        ModpackDetailHintText.Text = "当前使用本地整合包，无需选择在线版本。点击“下载并安装”后命名并安装。";
        ModpackDetailHintText.Visibility = Visibility.Visible;
        SelectModpackVersionsButton.IsEnabled = false;
        OpenModpackPageButton.IsEnabled = false;
        _suggestedModpackName = SuggestInstanceName(Path.GetFileNameWithoutExtension(_localModpackPath));
        ModpackInstanceNameTextBox.Text = _suggestedModpackName;
        ClearErrorBanner();
        ModpackNav.IsChecked = true;
        ShowStatus("已选择本地整合包，安装时将交由真实内核解析。", false);
        UpdateModpackSelectionSummary();
        UpdateModpackInstallState();
        return true;
    }

    private void ClearLocalModpack_Click(object sender, RoutedEventArgs e)
    {
        ClearLocalModpackSelection();
        ShowModpackListView();
    }

    private void ClearLocalModpackSelection()
    {
        _localModpackPath = null;
        LocalModpackPathText.Text = "本地包：未选择";
        LocalModpackPathText.ToolTip = null;
        ClearLocalModpackButton.IsEnabled = false;
        if (ModpackProjectsList.SelectedItem is null)
        {
            ModpackDetailHintText.Text = "在左侧选择一个整合包，查看详情和可用版本。";
            ModpackDetailHintText.Visibility = Visibility.Visible;
        }
        UpdateModpackSelectionSummary();
        UpdateModpackInstallState();
    }

    private void UpdateModpackSelectionSummary()
    {
        if (ModpackSelectionSummaryText is null)
        {
            return;
        }

        if (_localModpackPath is not null)
        {
            ModpackSelectionSummaryText.Text = $"本地包：{Path.GetFileName(_localModpackPath)}";
            return;
        }

        if (ModpackProjectsList.SelectedItem is not ModpackProject project)
        {
            ModpackSelectionSummaryText.Text = "在左侧点开一个整合包，选择版本后下载；也可以从右上角导入本地包。";
            return;
        }

        ModpackSelectionSummaryText.Text = ModpackVersionsList.SelectedItem is ModpackVersion version
            ? $"已选：{project.Name} · {version.Display}" +
              (version.IsDirectDownload
                  ? (IsDownloadOnlyMode ? "（直链，可下载到本地）" : "（直链，可下载安装）")
                  : version.HasExternalUrl
                      ? "（仅网盘，点击右侧按钮打开网盘）"
                      : "（仅网盘且无链接，将打开项目介绍页）")
            : $"已选项目：{project.Name}；请选择一个版本。";
    }

    private static string SuggestInstanceName(string raw)
    {
        var name = (raw ?? string.Empty).Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        name = name.Replace("..", "_").TrimEnd(' ', '.');
        return InstallInputValidator.GetInstanceNameError(name) is null && name.Length > 0
            ? name
            : "整合包实例";
    }

    private void UpdateModpackInstallState()
    {
        if (ModpackInstallButton is null)
        {
            return;
        }

        var project = ModpackProjectsList.SelectedItem as ModpackProject;
        var selectedVersion = ModpackVersionsList.SelectedItem as ModpackVersion;
        var canInstall = _localModpackPath is not null || selectedVersion?.IsDirectDownload == true;
        var canOpenExternal = _localModpackPath is null && selectedVersion is not null &&
            (selectedVersion.HasExternalUrl || !string.IsNullOrWhiteSpace(project?.ProjectUrl));
        var downloadOnly = IsDownloadOnlyMode;
        var hasTarget = downloadOnly
            ? !string.IsNullOrWhiteSpace(DownloadOnlyDirectory) && Path.IsPathFullyQualified(DownloadOnlyDirectory)
            : LauncherTargetComboBox.SelectedItem is LauncherChoice;
        var nameValid = InstallInputValidator.GetInstanceNameError(ModpackInstanceNameTextBox?.Text) is null;
        var actionEnabled = canInstall
            ? hasTarget && nameValid
            : canOpenExternal;
        ModpackInstallButton.IsEnabled = !_modpackVersionsBusy &&
            actionEnabled && !_isClosing;
        ModpackInstallButton.Content = canInstall
            ? (downloadOnly ? "仅下载到本地" : "下载并安装")
            : selectedVersion is null
                ? "下载并安装"
                : selectedVersion.HasExternalUrl ? "打开网盘下载" : "打开项目下载页";
        ModpackInstallButton.ToolTip = canInstall
            ? (downloadOnly
                ? "只把整合包文件保存到设置里的本地文件夹，不写入启动器。"
                : "选择启动器后下载并安装，安装前会让你命名实例。")
            : "该版本仅提供网盘；点击后由系统浏览器打开网盘或项目介绍页。";
        if (ModpackInstallHintText is not null)
        {
            ModpackInstallHintText.Text = downloadOnly ? "仅下载到本地文件夹" : "安装到所选启动器";
        }
        if (SelectModpackVersionsButton is not null)
        {
            SelectModpackVersionsButton.IsEnabled = !_modpackVersionsBusy &&
                ModpackProjectsList.SelectedItem is ModpackProject;
        }
    }

    private async void ModpackInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_modpackInstallBusy)
        {
            return;
        }

        var project = ModpackProjectsList.SelectedItem as ModpackProject;
        var selectedVersion = ModpackVersionsList.SelectedItem as ModpackVersion;
        if (_localModpackPath is null && selectedVersion is not null && !selectedVersion.IsDirectDownload)
        {
            OpenModpackNetdisk(project, selectedVersion);
            return;
        }

        var hasOnlineVersion = project is not null && selectedVersion?.IsDirectDownload == true;
        if (_localModpackPath is null && !hasOnlineVersion)
        {
            ShowError("下载整合包", new InvalidOperationException(
                project is null
                    ? "请先在整合包列表中点开一个项目并选择直链可用版本，或从右上角导入本地包。"
                    : "请选择一个直链可用的整合包版本；仅网盘版本会改为打开网盘下载。"));
            return;
        }

        var defaultName = SuggestInstanceName(ModpackInstanceNameTextBox.Text);
        var nameDialog = new ModpackNameWindow(defaultName) { Owner = this };
        if (nameDialog.ShowDialog() != true)
        {
            return;
        }
        ModpackInstanceNameTextBox.Text = nameDialog.InstanceName;

        try
        {
            if (IsDownloadOnlyMode)
            {
                // 仅下载模式只落盘压缩包，不检查启动器版本目录；目标文件夹在这里一并校验。
                RequestTargets();
            }
            else
            {
                var targets = SelectedLaunchers();
                var conflict = await Task.Run(() => FindVersionConflict(targets, nameDialog.InstanceName));
                if (conflict is not null)
                {
                    ShowError("命名整合包", new InvalidOperationException(
                        $"目标中已存在同名版本，请换一个实例名，或先清理旧版本：{conflict}"));
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            ShowError("检查整合包名称", exception);
            return;
        }

        await StartModpackInstallAsync();
    }

    private async Task StartModpackInstallAsync()
    {
        try
        {
            var request = BuildModpackInstallRequest();
            var sourceName = request.LocalModpackPath is not null
                ? Path.GetFileName(request.LocalModpackPath)
                : (ModpackProjectsList.SelectedItem as ModpackProject)?.Name ?? "在线整合包";
            var task = new InstallTaskItem(
                $"{request.InstanceName} · {sourceName}" + (request.DownloadOnly ? " · 仅下载" : ""),
                request.Targets, request.DownloadOnly);
            AddTask(task);
            _modpackInstallBusy = false;
            UpdateModpackInstallState();
            TasksNav.IsChecked = true;
            ShowTasksPage();
            await RunInstallAsync(task, request);
        }
        catch (Exception exception)
        {
            ShowError("准备整合包安装", exception);
        }
        finally
        {
            _modpackInstallBusy = false;
            UpdateModpackInstallState();
        }
    }

    private InstallRequest BuildModpackInstallRequest()
    {
        var instanceName = InstallInputValidator.ValidateInstanceName(ModpackInstanceNameTextBox.Text);

        if (_localModpackPath is not null)
        {
            if (!File.Exists(_localModpackPath))
            {
                throw new FileNotFoundException("本地整合包文件不存在，请重新选择。", _localModpackPath);
            }
            var extension = Path.GetExtension(_localModpackPath);
            if (!extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("本地整合包仅支持 .mrpack 或 .zip 文件。");
            }
            return new InstallRequest(instanceName, string.Empty, null, null, null, null,
                _localModpackPath, RequestTargets(),
                IsDownloadOnlyMode, IsDownloadOnlyMode ? DownloadOnlyDirectory : null);
        }

        if (ModpackProjectsList.SelectedItem is not ModpackProject project)
        {
            throw new ArgumentException("请选择一个在线整合包项目，或选择本地整合包。");
        }
        if (ModpackVersionsList.SelectedItem is not ModpackVersion version)
        {
            throw new ArgumentException("请选择要安装的整合包版本。");
        }
        if (!version.IsDirectDownload)
        {
            throw new ArgumentException("该版本仅提供网盘地址，没有可直链安装的文件。请选择直链可用版本。");
        }

        return new InstallRequest(instanceName, version.GameVersion, null, null, project.Id, version.Id,
            null, RequestTargets(),
            IsDownloadOnlyMode, IsDownloadOnlyMode ? DownloadOnlyDirectory : null);
    }

    private async Task RunInstallAsync(InstallTaskItem task, InstallRequest request)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_closingCancellation.Token, task.Cancellation.Token);
        var progress = new Progress<TargetProgress>(task.Report);
        var active = Interlocked.Increment(ref _activeInstalls);
        if (active > 1)
        {
            task.MarkQueued();
        }
        try
        {
            _captchaRetryRequest = null;
            _captchaRetryTask = null;
            ClearErrorBanner();
            ShowStatus($"{(request.DownloadOnly ? "正在下载" : "正在安装")}：{task.Title}", false);
            // 内核入口里可能有同步的设置锁和目录操作；放到后台线程，避免点击下载后 UI 线程被堵死。
            await Task.Run(() => _service.InstallAsync(request, progress, cancellation.Token), cancellation.Token);
            if (task.Cancellation.IsCancellationRequested)
            {
                task.CompleteCancellation();
                ShowStatus($"已取消：{task.Title}", false);
                return;
            }

            task.Complete();
            UpdateTaskEmptyState();

            if (task.Targets.All(item => item.Success == true))
            {
                ShowStatus($"{(request.DownloadOnly ? "下载完成" : "安装成功")}：{task.Title}", false);
            }
            else
            {
                var details = task.ResultDetails();
                ShowStatus($"{(request.DownloadOnly ? "下载未全部完成" : "安装未全部成功")}：{task.Title}{Environment.NewLine}{details}", true);
                if (ShowBbsmcHelpIfNeeded(request, details, task))
                {
                    // 只要命中 BBSMC 人机验证就自动弹窗，即使其他启动器已安装成功。
                    Dispatcher.BeginInvoke(new Action(OpenBbsmcCaptchaWindow));
                }
            }

            ShowTaskCompletion(task, request);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            task.CompleteCancellation();
            ShowStatus($"已取消：{task.Title}", false);
        }
        catch (Exception exception)
        {
            task.Fail(exception);
            var details = task.ResultDetails();
            ShowStatus($"{(request.DownloadOnly ? "下载失败" : "安装失败")}：{exception.GetBaseException().Message}{Environment.NewLine}{details}", true);
            if (ShowBbsmcHelpIfNeeded(request, exception + Environment.NewLine + details, task))
            {
                Dispatcher.BeginInvoke(new Action(OpenBbsmcCaptchaWindow));
            }
            ShowTaskCompletion(task, request);
        }
        finally
        {
            Interlocked.Decrement(ref _activeInstalls);
            UpdateTaskEmptyState();
        }
    }

    private void ShowTaskCompletion(InstallTaskItem task, InstallRequest request)
    {
        if (_isClosing || !task.Targets.Any(item => item.Success == true))
        {
            return;
        }

        if (request.DownloadOnly)
        {
            var directory = request.DownloadDirectory ?? task.Targets.FirstOrDefault()?.Directory;
            if (!string.IsNullOrWhiteSpace(directory))
            {
                InstallCompleteWindow.OpenDirectory(directory);
            }
            return;
        }

        ShowInstallCompleteWindow(task);
    }

    private void ShowInstallCompleteWindow(InstallTaskItem task)
    {
        var window = new InstallCompleteWindow(task) { Owner = this };
        // 安装已经完成，不再用模态窗口锁住主界面；即使用户把结果窗口移到屏幕外，主窗口仍可操作。
        window.Show();
    }

    private void AddTask(InstallTaskItem task)
    {
        _tasks.Insert(0, task);
        UpdateTaskEmptyState();
    }

    private void ShowTasksPage()
    {
        GamePage.Visibility = Visibility.Collapsed;
        ModpackPage.Visibility = Visibility.Collapsed;
        TasksPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Collapsed;
        TasksNav.IsChecked = true;
    }

    private void CancelTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InstallTaskItem task })
        {
            task.RequestCancel();
            UpdateTaskEmptyState();
            ShowStatus($"正在取消：{task.Title}", false);
        }
    }

    private void CancelAllTasks_Click(object sender, RoutedEventArgs e)
    {
        foreach (var task in _tasks.Where(item => item.IsRunning))
        {
            task.RequestCancel();
        }
        UpdateTaskEmptyState();
        ShowStatus("已请求取消所有进行中的任务。", false);
    }

    private void UpdateTaskEmptyState()
    {
        if (TasksEmptyState is not null)
        {
            TasksEmptyState.Visibility = _tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (CancelAllTasksButton is not null)
        {
            CancelAllTasksButton.IsEnabled = _tasks.Any(item => item.CanCancel);
        }
    }

    private void LoadSettingsIntoControls()
    {
        _customLaunchers.Clear();
        foreach (var custom in _settings.CustomLaunchers)
        {
            if (!string.IsNullOrWhiteSpace(custom.Directory))
            {
                _customLaunchers.Add(custom);
            }
        }

        SelectComboBoxTag(DownloadModeComboBox, _settings.DownloadOnly ? "local" : "install", "install");
        LocalDownloadDirectoryTextBox.Text = _settings.DownloadDirectory;
        UpdateDownloadModeUi();
        SelectComboBoxTag(GameDownloadSourceComboBox, _settings.GameDownloadSource, "auto");
        SelectComboBoxTag(DefaultModpackSourceComboBox, _settings.DefaultModpackSource, "全部来源");
        ApplyDefaultModpackSource(_settings.DefaultModpackSource);
        FallbackModpackCheckBox.IsChecked = _settings.FallbackModpackSources;
        SelectComboBoxTag(DownloadThreadsComboBox, _settings.DownloadThreads.ToString(), "16");
        SelectComboBoxTag(DownloadSpeedComboBox, _settings.DownloadSpeedLimitMb.ToString(), "0");
        SelectComboBoxTag(RetryAttemptsComboBox, _settings.RetryAttempts.ToString(), "3");
        SelectComboBoxTag(RetryDelayComboBox, _settings.RetryDelaySeconds.ToString(), "10");

        SettingsStateText.Text = $"设置文件：{_settingsService.SettingsPath}";
        UpdateCustomLauncherState();
    }

    private static void SelectComboBoxTag(ComboBox comboBox, string? tag, string fallbackTag)
    {
        if (comboBox is null) return;
        var match = comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
        match ??= comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag?.ToString(), fallbackTag, StringComparison.OrdinalIgnoreCase));
        comboBox.SelectedItem = match ?? comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private static string SelectedComboBoxTag(ComboBox comboBox, string fallback)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
    }

    private void OpenDataDirectory_Click(object sender, RoutedEventArgs e)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXMC");
        Directory.CreateDirectory(directory);
        InstallCompleteWindow.OpenDirectory(directory);
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXMC");
        var log = Path.Combine(directory, "errors.log");
        if (!File.Exists(log))
        {
            ShowStatus("还没有错误日志；安装失败后会写入 errors.log。", false);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(log) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError("打开日志", exception);
        }
    }

    private void OpenBbsmc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://bbsmc.net/")
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowError("打开 BBSMC", exception);
        }
    }

    /// <summary>仅网盘版本：优先打开网盘链接；没有网盘时退回项目介绍页，不绕过任何登录或验证。</summary>
    private void OpenModpackNetdisk(ModpackProject? project, ModpackVersion version)
    {
        var hasCloudLink = version.HasExternalUrl;
        var url = hasCloudLink ? version.ExternalUrl : project?.ProjectUrl;
        if (!TryOpenHttpUrl(url, hasCloudLink ? "打开网盘下载" : "打开项目下载页"))
        {
            return;
        }

        ShowStatus(hasCloudLink
            ? $"该版本仅提供网盘，已在浏览器打开：{version.Display}"
            : $"该版本没有可用网盘链接，已打开项目介绍页：{project?.Name}", false);
    }

    private bool TryOpenHttpUrl(string? url, string title)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowError(title, new InvalidOperationException("当前版本没有可打开的网盘或项目地址。"));
            return false;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception exception)
        {
            ShowError(title, exception);
            return false;
        }
    }

    private void OpenModpackPage_Click(object sender, RoutedEventArgs e)
    {
        if (ModpackProjectsList.SelectedItem is not ModpackProject project ||
            !Uri.TryCreate(project.ProjectUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowError("打开整合包介绍页", new InvalidOperationException("当前项目没有可打开的介绍页地址。"));
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowError("打开整合包介绍页", exception);
        }
    }

    private void OpenHallOfFame_Click(object sender, RoutedEventArgs e)
    {
        var window = new HallOfFameWindow { Owner = this };
        window.ShowDialog();
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.DownloadOnly = IsDownloadOnlyMode;
            _settings.DownloadDirectory = DownloadOnlyDirectory;
            if (_settings.DownloadOnly &&
                (string.IsNullOrWhiteSpace(_settings.DownloadDirectory) ||
                 !Path.IsPathFullyQualified(_settings.DownloadDirectory)))
            {
                throw new ArgumentException("仅下载模式需要选择一个完整的本地保存文件夹。");
            }

            _settings.GameDownloadSource = SelectedComboBoxTag(GameDownloadSourceComboBox, "auto");
            _settings.DefaultModpackSource = SelectedComboBoxTag(DefaultModpackSourceComboBox, "全部来源");
            _settings.FallbackModpackSources = FallbackModpackCheckBox.IsChecked == true;
            _settings.DownloadThreads = int.TryParse(SelectedComboBoxTag(DownloadThreadsComboBox, "16"), out var threads)
                ? Math.Clamp(threads, 1, 64)
                : 16;
            _settings.DownloadSpeedLimitMb = int.TryParse(SelectedComboBoxTag(DownloadSpeedComboBox, "0"), out var speed)
                ? Math.Clamp(speed, 0, 100)
                : 0;
            _settings.RetryAttempts = int.TryParse(SelectedComboBoxTag(RetryAttemptsComboBox, "3"), out var attempts)
                ? Math.Clamp(attempts, 1, 10)
                : 3;
            _settings.RetryDelaySeconds = int.TryParse(SelectedComboBoxTag(RetryDelayComboBox, "10"), out var delay)
                ? Math.Clamp(delay, 1, 120)
                : 10;
            SyncCustomLaunchersToSettings();
            _settingsService.Save(_settings);
            SettingsStateText.Text = "设置已保存，正在应用到内核…";
            ShowStatus("设置已保存，正在应用到内核…", false);
            if (_service is KernelService kernelService)
            {
                // 不占用 UI 线程等待内核锁；即使安装正在进行，保存按钮也会立刻有反馈。
                await Task.Run(() => kernelService.UpdateSettings(_settings));
            }
            SettingsStateText.Text = $"已保存到：{_settingsService.SettingsPath}";
            ShowStatus("设置已保存并应用；正在重新扫描启动器。", false);
            if (SaveSettingsButton is not null) SaveSettingsButton.Content = "已保存 ✓";
            await RefreshLaunchersAsync();
            await Task.Delay(1200);
            if (SaveSettingsButton is not null) SaveSettingsButton.Content = "保存设置";
        }
        catch (Exception exception)
        {
            SettingsStateText.Text = "设置保存失败，原错误已显示。";
            if (SaveSettingsButton is not null) SaveSettingsButton.Content = "保存设置";
            ShowError("保存设置", exception);
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        if (_isClosing || StatusText is null)
        {
            return;
        }
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? (Brush)FindResource("DangerBrush")
            : (Brush)FindResource("MutedBrush");
        StatusBorder.Background = new SolidColorBrush(isError ? Color.FromRgb(235, 235, 235) : Color.FromRgb(242, 242, 242));
        if (isError)
        {
            ShowErrorBanner(message);
        }
    }

    private void ShowError(string action, Exception exception)
    {
        var reason = exception.GetBaseException().Message;
        ShowStatus($"{action}失败：{reason}", true);
        if (!_captchaWindowOpen && BbsmcCaptchaHelper.IsCaptchaFailure(exception.ToString()))
        {
            // 只要命中 BBSMC 人机验证，就自动弹内置浏览器，不需要用户再点按钮。
            var projectUrl = (ModpackProjectsList?.SelectedItem as ModpackProject)?.ProjectUrl;
            ShowBbsmcCaptchaHelp(projectUrl);
            Dispatcher.BeginInvoke(new Action(OpenBbsmcCaptchaWindow));
        }
        try
        {
            var directory = Path.GetDirectoryName(_settingsService.SettingsPath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "errors.log"),
                $"{DateTimeOffset.Now:O} {action}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch
        {
            // Error reporting must not hide the original failure.
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _isClosing = true;
        _closingCancellation.Cancel();
        _versionsCancellation?.Cancel();
        _loadersCancellation?.Cancel();
        _searchCancellation?.Cancel();
        _modpackVersionsCancellation?.Cancel();
        foreach (var task in _tasks.Where(item => item.IsRunning))
        {
            task.RequestCancel();
        }
    }
}
