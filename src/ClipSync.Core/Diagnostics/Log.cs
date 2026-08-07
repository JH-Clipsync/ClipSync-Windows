using System.Diagnostics;
using System.Text;

namespace ClipSync.Core.Diagnostics;

// ============================================================
// 极简日志：控制台 + Debug 输出 + 按天滚动的本地文件。
//
// 用自己写的而不是引第三方库：整个客户端只需要"能把一行字带上时间写下来"，
// 加一个日志框架的依赖不值得。文件写入失败时静默降级，日志永远不该
// 把主流程搞崩。
// ============================================================
public static class Log
{
    private static readonly object Gate = new();
    private static string? _dir;

    /// <summary>日志目录。App 启动时指过来；没指就只写控制台。</summary>
    public static void UseDirectory(string dir)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(dir);
                _dir = dir;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Log] 无法创建日志目录 {dir}: {ex.Message}");
                _dir = null;
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        Console.WriteLine(line);
        Debug.WriteLine(line);

        var dir = _dir;
        if (dir is null) return;
        lock (Gate)
        {
            try
            {
                var path = Path.Combine(dir, $"clipsync-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 磁盘满 / 权限不足都不该影响同步功能，丢掉这一行就好
            }
        }
    }
}
