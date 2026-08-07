namespace ClipSync.Core.Storage;

/// <summary>
/// 本机数据目录。Windows 下落在 %APPDATA%\ClipSync；在 macOS/Linux 上跑单测时
/// SpecialFolder.ApplicationData 会指向 ~/.config，同样可用。
/// </summary>
public static class AppPaths
{
    private static string? _overrideRoot;

    /// <summary>测试可以把数据目录指到临时路径，避免污染真实配置。</summary>
    public static void OverrideRoot(string root) => _overrideRoot = root;

    public static string Root
    {
        get
        {
            if (_overrideRoot is not null) return _overrideRoot;
            var baseDir = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create);
            if (string.IsNullOrEmpty(baseDir))
            {
                baseDir = Path.GetTempPath();
            }
            return Path.Combine(baseDir, "ClipSync");
        }
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string HistoryFile => Path.Combine(Root, "history.json");
    public static string LogDirectory => Path.Combine(Root, "logs");

    public static void EnsureRoot() => Directory.CreateDirectory(Root);
}
