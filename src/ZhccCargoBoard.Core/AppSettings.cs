using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZhccCargoBoard.Core;

public sealed class AppSettings
{
    /// <summary>自动刷新间隔（分钟），30 为默认。</summary>
    public int RefreshMinutes { get; set; } = 30;

    /// <summary>本地数据保留天数。</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>导出时“航班日期”列的格式。</summary>
    public string DateFormat { get; set; } = "yyyy-mm-dd";

    /// <summary>关闭窗口时最小化到托盘（而不是退出）。</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>启动后直接缩到托盘。</summary>
    public bool StartMinimized { get; set; }

    public double WindowWidth { get; set; } = 1360;

    public double WindowHeight { get; set; } = 860;

    public bool WindowMaximized { get; set; }

    public string? LastExportDirectory { get; set; }

    /// <summary>导出时附加的信息列（默认全选）。</summary>
    public List<string> ExtraExportColumns { get; set; } = Export.ExportColumns.DefaultExtraKeys.ToList();

    [JsonIgnore]
    public TimeSpan RefreshInterval => TimeSpan.FromMinutes(Math.Clamp(RefreshMinutes, 1, 24 * 60));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AppSettings Load(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"读取设置失败，使用默认设置：{ex.Message}");
        }

        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"保存设置失败：{ex.Message}");
        }
    }

    private void Normalize()
    {
        RefreshMinutes = Math.Clamp(RefreshMinutes, 1, 24 * 60);
        RetentionDays = Math.Clamp(RetentionDays, 1, 3650);
        if (string.IsNullOrWhiteSpace(DateFormat))
        {
            DateFormat = "yyyy-mm-dd";
        }
        ExtraExportColumns ??= Export.ExportColumns.DefaultExtraKeys.ToList();
        ExtraExportColumns = ExtraExportColumns
            .Where(k => Export.ExportColumns.ExtraKeys.Contains(k))
            .Distinct()
            .ToList();
    }
}
