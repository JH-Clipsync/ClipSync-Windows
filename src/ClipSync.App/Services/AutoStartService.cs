using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ClipSync.App.Services;

using ClipSync.Core.Diagnostics;

// ============================================================
// AutoStartService：开机自启（写 HKCU\\…\\Run 注册表键）
// - 仅写 HKCU（当前用户），不需要管理员权限
// - 与 SettingsStore.AutoStart 双向同步：
//   · App 启动时 ApplySavedSetting()：把 settings 里的开关落到注册表
//     （防止用户手动清了注册表但 settings 里仍是 true 的不一致）
//   · 设置页切换开关 → Apply(value) → 同时写 settings + 注册表
// ============================================================
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "ClipSync";

    /// <summary>当前自启 exe 的完整命令行。
    /// 优先用 Process.GetCurrentProcess().MainModule.FileName（即使用户把 exe 改名也能正确拿到，兼容单文件发布），
    /// 不可用时退回到 AppContext.BaseDirectory 下找 ClipSync.App.exe / ClipSync.App.dll。</summary>
    private static string? GetExecutablePath()
    {
        try
        {
            var main = Process.GetCurrentProcess().MainModule;
            if (main is not null && !string.IsNullOrEmpty(main.FileName))
            {
                return main.FileName;
            }
        }
        catch { /* 受限环境拿不到 MainModule，忽略 */ }

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var exe = Path.Combine(baseDir, "ClipSync.App.exe");
            if (File.Exists(exe)) return exe;
            var dll = Path.Combine(baseDir, "ClipSync.App.dll");
            if (File.Exists(dll)) return dll;
        }
        catch { }

        return null;
    }

    /// <summary>查询注册表当前是否已登记自启。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key is null) return false;
            var v = key.GetValue(AppValueName) as string;
            return !string.IsNullOrEmpty(v);
        }
        catch (Exception ex)
        {
            Log.Warn($"[AutoStart] 读注册表失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>启用或禁用开机自启。返回 true 表示注册表操作成功。</summary>
    public static bool Apply(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                Log.Warn("[AutoStart] 无法打开 Run 注册表键");
                return false;
            }

            if (enable)
            {
                var exe = GetExecutablePath();
                if (string.IsNullOrEmpty(exe))
                {
                    Log.Warn("[AutoStart] 无法确定 exe 路径，放弃写入自启");
                    return false;
                }
                // 加引号：路径带空格时不被拆成多段参数
                key.SetValue(AppValueName, $"\"{exe}\"");
                Log.Info($"[AutoStart] 已写入开机自启: {exe}");
            }
            else
            {
                if (key.GetValue(AppValueName) is not null)
                {
                    key.DeleteValue(AppValueName, throwOnMissingValue: false);
                    Log.Info("[AutoStart] 已移除开机自启");
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"[AutoStart] 写注册表失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>启动时把 SettingsStore 的开关同步到注册表。
    /// 两种不一致的场景都会被修正：
    /// 1) settings=true 但注册表被手清 → 重新写入
    /// 2) settings=false 但注册表残留 → 清掉</summary>
    public static void ApplySavedSetting(bool autoStartInSettings)
    {
        var registryOn = IsEnabled();
        if (registryOn == autoStartInSettings) return;
        Apply(autoStartInSettings);
    }
}
