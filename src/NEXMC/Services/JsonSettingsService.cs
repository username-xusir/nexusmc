using System.IO;
using System.Text.Json;

namespace NEXMC.Services;

public sealed class CustomLauncherSetting
{
    public string Name { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
}

public sealed class UiSettings
{
    // 旧版字段仅用于读取旧设置文件，不再在界面中单独展示。
    public string Pcl2Directory { get; set; } = string.Empty;
    public string HmclDirectory { get; set; } = string.Empty;
    public string Pcl2Exe { get; set; } = string.Empty;
    public string HmclExe { get; set; } = string.Empty;
    public List<CustomLauncherSetting> CustomLaunchers { get; set; } = new();
    public string GameDownloadSource { get; set; } = "auto";
    public string DefaultModpackSource { get; set; } = "全部来源";
    public bool FallbackModpackSources { get; set; } = true;
    public int DownloadThreads { get; set; } = 16;
    public int DownloadSpeedLimitMb { get; set; } = 0;
    public int RetryAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 10;
    public bool DownloadOnly { get; set; }
    public string DownloadDirectory { get; set; } = string.Empty;
}

public sealed class JsonSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NEXMC",
        "ui-settings.json");

    public UiSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new UiSettings();
        }

        var json = File.ReadAllText(SettingsPath);
        var settings = JsonSerializer.Deserialize<UiSettings>(json) ?? new UiSettings();
        settings.CustomLaunchers ??= new List<CustomLauncherSetting>();
        settings.CustomLaunchers.RemoveAll(item => item is null);
        foreach (var custom in settings.CustomLaunchers)
        {
            custom.Name ??= string.Empty;
            custom.Directory ??= string.Empty;
            custom.ExecutablePath ??= string.Empty;
        }
        return settings;
    }

    public void Save(UiSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("无法确定设置文件目录。");
        Directory.CreateDirectory(directory);

        var temporaryPath = SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, SettingsPath, true);
    }
}
