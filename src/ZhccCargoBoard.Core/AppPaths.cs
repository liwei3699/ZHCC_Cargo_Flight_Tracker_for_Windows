namespace ZhccCargoBoard.Core;

/// <summary>
/// 程序的本地数据位置。默认 %LOCALAPPDATA%\ZhccCargoBoard（免安装、不需要管理员权限）。
/// 解析顺序：
///   1. 环境变量 ZHCC_DATA_DIR（最高优先级，测试/多开用）
///   2. exe 旁边已存在 data 文件夹 → 便携模式
///   3. %LOCALAPPDATA%\ZhccCargoBoard（默认）
///   4. exe 旁边新建 data（受限环境兜底：漫游配置、只读 TEMP、沙箱等）
///   5. 系统临时目录下的 ZhccCargoBoard
/// 3/4/5 都会先做一次真实写入探测，写不进去就换下一个。
/// </summary>
public static class AppPaths
{
    private static readonly string LocalAppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZhccCargoBoard");

    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string DatabaseFile => Path.Combine(DataDirectory, "data.db");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string DefaultExportDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "货机航班动态");

    /// <summary>数据是否放在 exe 同目录（便携模式 / 兜底目录）。</summary>
    public static bool IsPortable { get; } =
        !string.Equals(DataDirectory, LocalAppDataDirectory, StringComparison.OrdinalIgnoreCase);

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    private static string ResolveDataDirectory()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ZHCC_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Path.GetFullPath(fromEnv);
        }

        var portable = SafeCombine(AppContext.BaseDirectory, "data");

        // 便携模式：用户自己建了 data 文件夹，就跟着 exe 走
        if (Directory.Exists(portable))
        {
            return portable;
        }

        if (CanWrite(LocalAppDataDirectory))
        {
            return LocalAppDataDirectory;
        }

        if (CanWrite(portable))
        {
            return portable;
        }

        var temp = SafeCombine(Path.GetTempPath(), "ZhccCargoBoard");
        if (CanWrite(temp))
        {
            return temp;
        }

        return LocalAppDataDirectory;   // 都写不了，交给上层报错
    }

    private static string SafeCombine(string dir, string child)
    {
        try
        {
            return Path.Combine(dir, child);
        }
        catch
        {
            return child;
        }
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
