using System.Text;

namespace ZhccCargoBoard.Core;

/// <summary>极简文件日志：按天一个文件，保留 14 天。</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _currentFile;
    private static DateTime _currentDate;

    public static string LogDirectory => AppPaths.LogDirectory;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var now = DateTime.Now;
                if (_currentFile is null || _currentDate.Date != now.Date)
                {
                    _currentDate = now;
                    _currentFile = Path.Combine(LogDirectory, $"app-{now:yyyyMMdd}.log");
                }
                File.AppendAllText(
                    _currentFile,
                    $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不能影响主流程
        }
    }

    public static void Prune(int keepDays = 14)
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return;
            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "app-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // ignore
        }
    }
}
