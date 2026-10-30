using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NEXMC.Services;

namespace NEXMC.Models;

public sealed class InstallTargetTaskItem : INotifyPropertyChanged
{
    private double _progress;
    private string _message = "等待开始";
    private bool? _success;
    private bool _isCancelled;
    private bool _isQueued;

    public InstallTargetTaskItem(LauncherOption target, bool downloadOnly = false)
    {
        Option = target;
        IsDownloadOnly = downloadOnly;
    }

    public LauncherOption Option { get; }
    public bool IsDownloadOnly { get; }
    public string TargetName => Option.DisplayLabel;
    public string Directory => Option.Directory;
    public string StatusText => Success switch
    {
        true => IsDownloadOnly ? "下载成功" : "安装成功",
        false => IsDownloadOnly ? "下载失败" : "安装失败",
        null when _isCancelled => "已取消",
        null when _isQueued => "排队中",
        _ => "进行中"
    };

    public double Progress
    {
        get => _progress;
        private set { _progress = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
    }

    public string ProgressText => $"{Progress:F0}%";

    public string Message
    {
        get => _message;
        private set { _message = value; OnPropertyChanged(); }
    }

    public bool? Success
    {
        get => _success;
        private set
        {
            _success = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsProgressVisible));
            OnPropertyChanged(nameof(IsSuccess));
        }
    }

    /// <summary>完成（成功/失败/取消）后隐藏进度条，避免留下一条满格却不再变化的进度。</summary>
    public bool IsProgressVisible => Success is null && !_isCancelled;
    public bool IsSuccess => Success == true;

    public void MarkQueued()
    {
        if (Success is not null) return;
        _isQueued = true;
        Message = "已加入队列，等待前面的任务完成…";
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(StatusText));
    }

    public void Report(TargetProgress update)
    {
        Progress = update.Success == true ? 100 : Math.Clamp(update.Progress * 100, 0, 100);
        Message = string.IsNullOrWhiteSpace(update.Message) ? "正在处理" : update.Message;
        _isCancelled = false;
        _isQueued = false;
        Success = update.Success;
        OnPropertyChanged(nameof(StatusText));
    }

    public void MarkMissingResult()
    {
        if (Success is null)
        {
            Message = "内核未返回该目标的完成结果。";
            Success = false;
        }
    }

    public void MarkFailed(string message)
    {
        if (Success is null)
        {
            Message = message;
            Success = false;
        }
    }

    public void MarkCancelled()
    {
        if (Success is null)
        {
            _isCancelled = true;
            Message = "已取消";
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsProgressVisible));
            OnPropertyChanged(nameof(IsSuccess));
        }
    }

    /// <summary>BBSMC 验证完成后，把已经失败的目标恢复为“等待重新安装”。</summary>
    public void MarkRetried()
    {
        _isCancelled = false;
        _isQueued = false;
        Progress = 0;
        Success = null;
        Message = "验证通过，正在重新安装…";
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsProgressVisible));
        OnPropertyChanged(nameof(IsSuccess));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class InstallTaskItem : INotifyPropertyChanged
{
    private string _overallStatus = "准备安装";
    private bool _isRunning = true;
    private bool _isCancellationRequested;

    public InstallTaskItem(string title, IReadOnlyList<LauncherOption> targets, bool downloadOnly = false)
    {
        Title = title;
        StartedAt = DateTimeOffset.Now;
        IsDownloadOnly = downloadOnly;
        foreach (var target in targets)
        {
            Targets.Add(new InstallTargetTaskItem(target, downloadOnly));
        }
    }

    public bool IsDownloadOnly { get; }
    public string Title { get; }
    public DateTimeOffset StartedAt { get; }
    public string StartedAtText => StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public ObservableCollection<InstallTargetTaskItem> Targets { get; } = new();
    public CancellationTokenSource Cancellation { get; } = new();

    public string OverallStatus
    {
        get => _overallStatus;
        private set { _overallStatus = value; OnPropertyChanged(); }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCancel));
        }
    }

    public bool CanCancel => IsRunning && !_isCancellationRequested;
    public bool IsAllSuccess => !IsRunning && Targets.Count > 0 && Targets.All(item => item.Success == true);

    public void Report(TargetProgress update)
    {
        var target = Targets.FirstOrDefault(item =>
            string.Equals(item.Option.Id, update.Target, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.TargetName, update.Target, StringComparison.CurrentCultureIgnoreCase) ||
            string.Equals(item.TargetName, update.Target, StringComparison.OrdinalIgnoreCase));
        target?.Report(update);
        if (IsRunning && target is not null)
        {
            OverallStatus = IsDownloadOnly ? "正在下载" : "正在安装";
        }
    }

    public void MarkQueued()
    {
        if (!IsRunning) return;
        OverallStatus = "排队中";
        foreach (var target in Targets) target.MarkQueued();
    }

    /// <summary>把失败任务重新置为运行态，用于 BBSMC 验证完成后的自动续装。</summary>
    public void MarkRetried()
    {
        _isCancellationRequested = false;
        IsRunning = true;
        OverallStatus = "验证通过，正在重新安装…";
        foreach (var target in Targets) target.MarkRetried();
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(IsAllSuccess));
    }

    public void RequestCancel()
    {
        if (!IsRunning || _isCancellationRequested)
        {
            return;
        }

        _isCancellationRequested = true;
        OverallStatus = "正在取消…";
        OnPropertyChanged(nameof(CanCancel));
        Cancellation.Cancel();
    }

    public void Complete()
    {
        foreach (var target in Targets)
        {
            target.MarkMissingResult();
        }

        IsRunning = false;
        OnPropertyChanged(nameof(IsAllSuccess));
        var succeeded = Targets.Count(item => item.Success == true);
        OverallStatus = succeeded == Targets.Count
            ? (IsDownloadOnly ? "全部下载完成" : "全部目标安装成功")
            : succeeded > 0
                ? (IsDownloadOnly ? "部分下载完成" : "部分目标安装成功")
                : (IsDownloadOnly ? "下载失败" : "安装失败");
    }

    public void Fail(Exception exception)
    {
        foreach (var target in Targets)
        {
            target.MarkFailed(exception.Message);
        }

        IsRunning = false;
        OnPropertyChanged(nameof(IsAllSuccess));
        var succeeded = Targets.Count(item => item.Success == true);
        OverallStatus = succeeded == Targets.Count
            ? "目标已完成，但内核随后返回异常"
            : succeeded > 0 ? "部分目标安装成功" : "安装失败";
    }

    public void CompleteCancellation()
    {
        foreach (var target in Targets)
        {
            target.MarkCancelled();
        }

        IsRunning = false;
        OnPropertyChanged(nameof(IsAllSuccess));
        var succeeded = Targets.Count(item => item.Success == true);
        OverallStatus = succeeded == Targets.Count
            ? "目标已完成，随后请求取消"
            : succeeded > 0 ? "已取消，部分目标已完成" : "已取消";
    }

    public string ResultDetails() => string.Join(Environment.NewLine,
        Targets.Select(item => $"{item.TargetName}{Environment.NewLine}  {item.Directory}{Environment.NewLine}  {item.StatusText}：{item.Message}"));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
