using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NEXMC.Services;

namespace NEXMC;

public partial class App : Application
{
    public static INexmcService? Backend { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Trace("OnStartup begin");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        Exit += (_, _) => Trace($"Application exit, MainWindow={MainWindow?.GetType().FullName ?? "null"}");

        try
        {
            var serviceType = typeof(App).Assembly.GetType("NEXMC.Services.KernelService", false);
            if (serviceType is null)
            {
                ShowKernelConnectionError(
                    "未找到 NEXMC.Services.KernelService。\n\n请在 UI 项目中提供带公共无参构造函数的 KernelService，并实现 INexmcService。应用不会使用演示后端代替真实内核。",
                    null);
                return;
            }

            if (!typeof(INexmcService).IsAssignableFrom(serviceType))
            {
                ShowKernelConnectionError("KernelService 未实现 INexmcService，无法连接真实下载内核。", null);
                return;
            }

            if (Activator.CreateInstance(serviceType) is not INexmcService service)
            {
                ShowKernelConnectionError("KernelService 实例化失败，未获得有效的 INexmcService。", null);
                return;
            }

            Backend = service;
            Trace("KernelService initialized");
            var mainWindow = new MainWindow(service);
            MainWindow = mainWindow;
            mainWindow.Show();
            Trace("MainWindow shown");
        }
        catch (Exception exception)
        {
            var actualException = exception is TargetInvocationException { InnerException: not null }
                ? exception.InnerException
                : exception;
            Trace($"Startup exception: {actualException}");
            ShowKernelConnectionError("初始化真实下载内核时发生异常。", actualException);
        }
    }

    private void ShowKernelConnectionError(string message, Exception? exception)
    {
        var details = exception is null ? message : $"{message}{Environment.NewLine}{Environment.NewLine}{exception}";
        var window = new KernelConnectionWindow(details);
        MainWindow = window;
        window.Show();
        Trace("KernelConnectionWindow shown");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Trace("Application OnExit");
        base.OnExit(e);
        Environment.Exit(e.ApplicationExitCode);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Trace($"Dispatcher exception: {e.Exception}");
        MessageBox.Show(
            $"界面操作发生未处理异常：{Environment.NewLine}{Environment.NewLine}{e.Exception}",
            "NEXMC 错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void Trace(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXMC");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "startup.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never prevent application startup.
        }
    }
}
