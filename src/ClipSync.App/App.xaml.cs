using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

using ClipSync.App.Services;
using ClipSync.App.UI;
using ClipSync.Core.Diagnostics;
using ClipSync.Core.Net;
using ClipSync.Core.Protocol;
using ClipSync.Core.Storage;

namespace ClipSync.App;

// ============================================================
// App.xaml.cs —— 应用入口
// - 单实例守卫（已有进程在跑就唤起它并退出自己）
// - Dispatcher 注入（WSClient / HistoryStore 需要把集合更新投到 UI 线程）
// - 日志目录绑定
// - 托盘图标 + 菜单
// - 启动时自动连接（账号密码或 token 已填的前提下）
// - 监听消息 → 落历史 + 写剪贴板 + 弹 Toast
// ============================================================
public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private WinForms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        // [Debug] 启动分段心跳：完全不走 Log 系统 / 不走 Dispatcher，纯 File 写
        // 用于诊断"App 瞬间退出却没有任何日志"的问题
        static string TracePath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData), "ClipSync");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "startup-trace.log");
        }
        static void Trace(string msg)
        {
            try { File.AppendAllText(TracePath(), $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
            catch { /* try best */ }
        }
        Trace("======== START ========");

        // 0) 全局异常兜底（务必放在最前面，不依赖任何其它组件）
        //    所有未处理异常都会写到 crash.log，排障时直接看这份
        Trace("STEP 0/12 AttachGlobalExceptionHandlers…");
        AttachGlobalExceptionHandlers();
        Trace("STEP 0/12 ok");

        Trace("base.OnStartup…");
        base.OnStartup(e);
        Trace("base.OnStartup ok");

        // 1) 单实例守卫
        Trace("STEP 1/12 EnsureSingleInstance…");
        EnsureSingleInstance(Trace);
        Trace("STEP 1/12 ok (我是主实例)");

        // 2) 日志与数据目录
        Trace("STEP 2/12 目录初始化…");
        AppPaths.EnsureRoot();
        Directory.CreateDirectory(AppPaths.LogDirectory);
        Log.UseDirectory(AppPaths.LogDirectory);
        Log.Info("[App] 启动 ClipSync Windows");
        Trace("STEP 2/12 ok");

        // 3) Dispatcher 注入：WSClient 和 HistoryStore 都要把回调投到 UI 线程
        Trace("STEP 3/12 Dispatcher 注入…");
        var dispatcher = Dispatcher.CurrentDispatcher;
        Action<Action> dispatch = action =>
        {
            if (dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        };
        WSClient.Shared.UseDispatcher(dispatch);
        HistoryStore.Shared.UseDispatcher(dispatch);
        Trace("STEP 3/12 ok");

        // 4) 剪贴板监听绑定
        Trace("STEP 4/12 剪贴板监听绑定…");
        ClipboardMonitor.Shared.Bind(WSClient.Shared);
        Trace("STEP 4/12 ok");

        // 5) 托盘
        Trace("STEP 5/12 创建托盘图标…");
        SetupTrayIcon();
        Trace("STEP 5/12 ok");

        // 6) 消息处理：落历史 + 写剪贴板 + 弹 Toast
        Trace("STEP 6/12 绑定消息接收…");
        WSClient.Shared.MessageReceived += OnMessageReceived;
        Trace("STEP 6/12 ok");

        // 7) 账号密码已填 → 自动连接
        Trace("STEP 7/12 读取设置 & 自动连接评估…");
        var s = SettingsStore.Shared;
        if ((s.HasCredentials || s.Token.Length > 0))
        {
            _ = WSClient.Shared.ConnectAsync(s);
        }
        Trace($"STEP 7/12 ok (HasCred={s.HasCredentials}, HasToken={s.Token.Length > 0})");

        // 8) 剪贴板自动同步开关
        Trace("STEP 8/12 剪贴板自动同步…");
        if (s.AutoSyncClipboard)
        {
            ClipboardMonitor.Shared.IsEnabled = true;
            ClipboardMonitor.Shared.Start();
        }
        Trace($"STEP 8/12 ok (AutoSync={s.AutoSyncClipboard})");

        // 9) 开机自启：启动时把 settings 同步到注册表（防止注册表被手清的不一致）
        Trace("STEP 9/12 开机自启注册表同步…");
        AutoStartService.ApplySavedSetting(s.AutoStart);
        Trace("STEP 9/12 ok");

        // 10) 设置变化：同步开关 → 启停剪贴板监听 / 自启注册表
        Trace("STEP 10/12 绑定设置变更监听…");
        s.PropertyChanged += Settings_PropertyChanged;
        Trace("STEP 10/12 ok");

        // 11) 首次启动：不再弹模态向导，直接打开主窗口，在主窗口界面内完成引导设置
        Trace($"STEP 11/12 首次启动检查（OnboardingCompleted={s.OnboardingCompleted}）…");
        Trace("STEP 11/12 ok — 引导设置已内嵌到主窗口");

        // 12) 打开主窗口（首次启动也直接进主窗口，HomeView 会显示首次设置横幅）
        Trace("STEP 12/12 打开主窗口…");
        OpenMainWindow();
        // 如果有已保存的凭据/token，后台自动尝试连接
        if (s.HasCredentials || s.Token.Length > 0)
        {
            Trace("STEP 12a 有历史凭据，后台自动连接…");
            _ = WSClient.Shared.ConnectAsync(s);
        }
        Trace("STEP 12/12 ok — 启动完成 ✓");
    }

    // ============================================================
    // 全局异常兜底：所有未处理异常 → 写 crash.log 到数据目录
    // 三重网：UI 线程 / 任意线程 / Task 内部未观察异常
    // 用纯 File.AppendAllText 而不是 Log 系统，避免异常时 Log 本身都没初始化 / 没 flush
    // ============================================================
    private void AttachGlobalExceptionHandlers()
    {
        static string CrashLogPath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData), "ClipSync");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "crash.log");
        }

        static void WriteCrash(string tag, Exception ex)
        {
            try
            {
                var content =
                    $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] =====\n" +
                    $"{ex}\n" +
                    $"(Inner) {ex.InnerException}\n\n";
                File.AppendAllText(CrashLogPath(), content);
            }
            catch { /* 绝不能在异常处理器里再抛异常 */ }
        }

        // 1) UI 线程：未捕获的 Dispatcher 异常（实例事件，订阅 this）
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrash("Dispatcher", args.Exception);
            args.Handled = false; // 让进程正常终止，避免半残状态继续跑
        };

        // 2) 任意线程：终极兜底（最容易命中）
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteCrash($"AppDomain(isTerminating={args.IsTerminating})", ex);
            else
                WriteCrash("AppDomain-nonException",
                    new Exception(args.ExceptionObject?.ToString() ?? "(null)"));
        };

        // 3) Task/async void 里被吞掉的 Unobserved 异常
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrash("UnobservedTask", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        try { _trayIcon?.Dispose(); } catch { }
        try { HistoryStore.Shared.SaveNow(); } catch { }
        try { SettingsStore.Shared.SaveNow(); } catch { }
        base.OnExit(e);
    }

    // ============================================================
    // 窗口图标（任务栏 / Alt+Tab / 窗口标题栏左上角显示）
    // - 和托盘 GetTrayIcon() 同一份资源：pack://application:,,,/Resources/app.ico
    // - 返回最大尺寸帧（一般是 256x256），避免高 DPI 下任务栏/缩略图糊
    // ============================================================
    internal static System.Windows.Media.ImageSource? GetWindowIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.RelativeOrAbsolute);
            var sri = GetResourceStream(uri);
            if (sri?.Stream is null) return null;
            using (sri.Stream)
            {
                var decoder = System.Windows.Media.Imaging.IconBitmapDecoder.Create(
                    sri.Stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                System.Windows.Media.Imaging.BitmapFrame? best = null;
                foreach (var f in decoder.Frames)
                {
                    if (best is null || f.PixelWidth * f.PixelHeight > best.PixelWidth * best.PixelHeight)
                        best = f;
                }
                if (best is not null && best.CanFreeze) best.Freeze();
                return best;
            }
        }
        catch { return null; }
    }

    // ============================================================
    // 单实例
    // ============================================================
    private static void EnsureSingleInstance(Action<string> trace)
    {
        var mutexName = @"Global\ClipSync-Windows-SingleInstance-Mutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out var createdNew);
        trace($"  mutex createdNew={createdNew}");
        if (createdNew)
        {
            // 我是主实例：注册一个隐藏的消息窗口，接收后续实例发来的唤起消息
            // （即使主窗口被关闭、程序只在托盘里运行，MainWindowHandle 为 0，
            //  这个消息窗口仍然能收到广播，从而可靠地唤起主窗口）
            _instanceListener = new InstanceMessageWindow();
            return;
        }

        trace("  createdNew=false → 已有实例在跑，广播唤起消息后自行 Exit(0)");
        // 通知已运行的实例把窗口拉到前台
        try
        {
            var msg = NativeMethods.RegisterWindowMessage("ClipSync_Windows_ShowInstance");
            if (msg != 0)
            {
                NativeMethods.PostMessage(new IntPtr(NativeMethods.HWND_BROADCAST), msg, IntPtr.Zero, IntPtr.Zero);
                trace($"  已广播唤起消息 msg=0x{msg:X}");
            }
        }
        catch (Exception ex)
        {
            trace($"  广播唤起消息失败: {ex.Message}");
            // 回退：尝试用 MainWindowHandle 唤起
            try
            {
                var existing = System.Diagnostics.Process.GetProcessesByName(
                    Path.GetFileNameWithoutExtension(System.Diagnostics.Process.GetCurrentProcess().ProcessName))
                    .FirstOrDefault(p => p.Id != Environment.ProcessId);
                if (existing is not null && existing.MainWindowHandle != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(existing.MainWindowHandle, 9);
                    NativeMethods.SetForegroundWindow(existing.MainWindowHandle);
                }
            }
            catch { }
        }

        trace("  Environment.Exit(0)");
        Environment.Exit(0);
    }

    /// <summary>隐藏的仅消息窗口，用于接收第二个实例发来的"唤起我"广播。</summary>
    private sealed class InstanceMessageWindow : System.Windows.Forms.NativeWindow
    {
        private static readonly uint ShowMsg =
            NativeMethods.RegisterWindowMessage("ClipSync_Windows_ShowInstance");

        public InstanceMessageWindow()
        {
            var cp = new System.Windows.Forms.CreateParams
            {
                // HWND_MESSAGE：仅消息窗口，不显示、不在任务栏，专门收消息
                Parent = new IntPtr(NativeMethods.HWND_MESSAGE),
            };
            CreateHandle(cp);
        }

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == ShowMsg && ShowMsg != 0)
            {
                // 在 UI 线程上唤起主窗口
                Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (Current is App app) app.OpenMainWindow();
                });
            }
            base.WndProc(ref m);
        }
    }

    private static InstanceMessageWindow? _instanceListener;

    // ============================================================
    // 托盘
    // ============================================================
    private void SetupTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Visible = true,
            Text = "ClipSync",
            Icon = GetTrayIcon(),
        };

        var menu = new WinForms.ContextMenuStrip();

        var statusItem = new WinForms.ToolStripMenuItem(StatusText(WSClient.Shared.State))
        {
            Enabled = false,
        };
        menu.Items.Add(statusItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var openItem = new WinForms.ToolStripMenuItem("打开主窗口");
        openItem.Click += (_, _) => OpenMainWindow();
        menu.Items.Add(openItem);

        var reconnectItem = new WinForms.ToolStripMenuItem("重新连接");
        reconnectItem.Click += async (_, _) =>
            await WSClient.Shared.ConnectAsync(SettingsStore.Shared);
        var disconnectItem = new WinForms.ToolStripMenuItem("断开连接");
        disconnectItem.Click += (_, _) => WSClient.Shared.Disconnect();

        UpdateConnectMenu(reconnectItem, disconnectItem);
        menu.Items.Add(reconnectItem);
        menu.Items.Add(disconnectItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        var quitItem = new WinForms.ToolStripMenuItem("退出");
        quitItem.Click += (_, _) =>
        {
            _trayIcon!.Visible = false;
            Shutdown();
        };
        menu.Items.Add(quitItem);

        _trayIcon.ContextMenuStrip = menu;

        // 左键：开主窗口；右键：菜单
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left) OpenMainWindow();
        };

        // 状态变化 → 更新托盘文字 + 菜单
        WSClient.Shared.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WSClient.Shared.State))
            {
                Dispatcher.BeginInvoke(() =>
                {
                    statusItem.Text = StatusText(WSClient.Shared.State);
                    _trayIcon!.Text = $"ClipSync - {StatusText(WSClient.Shared.State)}";
                    UpdateConnectMenu(reconnectItem, disconnectItem);
                });
            }
        };
    }

    private static string TrayIconPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData), "ClipSync");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "app.ico");
    }

    private static Drawing.Icon GetTrayIcon()
    {
        // ============================================================
        // 核心修复：NotifyIcon.Icon 必须持有独立、不被 dispose 的 Icon 句柄。
        // 之前用 "pack 资源流 + using + Clone"，底层 Stream 关闭后 Clone 出来的
        // Icon 在某些 DPI/任务栏场景下句柄会失效，导致图标"放到托盘后自己消失"。
        // 解决：把 pack 资源一次性落地到 %APPDATA%\ClipSync\app.ico，
        //       NotifyIcon 直接从这个磁盘文件 new Icon(path) 加载，生命周期和进程一样长。
        // ============================================================
        try
        {
            var path = TrayIconPath();
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                var uri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.RelativeOrAbsolute);
                var sri = Application.GetResourceStream(uri);
                if (sri?.Stream is not null)
                {
                    using (sri.Stream)
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        sri.Stream.CopyTo(fs);
                    }
                }
            }

            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                // 64x64 与 48x48 之间挑最接近系统托盘图标的大小；DPI 缩放时也会自动选合适的帧
                return new Drawing.Icon(path, new Drawing.Size(64, 64));
            }
        }
        catch { /* 写盘/读盘失败，走下一级兜底 */ }

        // 兜底：用内存生成一个简易图标：48x48 紫色背景 + 白色 C
        try
        {
            using var bmp = new Drawing.Bitmap(48, 48);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Drawing.Color.Transparent);
                using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(99, 102, 241));
                g.FillEllipse(brush, 2, 2, 44, 44);
                using var pen = new Drawing.Pen(Drawing.Color.White, 3);
                g.DrawEllipse(pen, 2, 2, 44, 44);
                using var f = new Drawing.Font("Segoe UI", 16, Drawing.FontStyle.Bold);
                var sf = new Drawing.StringFormat
                {
                    Alignment = Drawing.StringAlignment.Center,
                    LineAlignment = Drawing.StringAlignment.Center,
                };
                g.DrawString("C", f, Drawing.Brushes.White, new Drawing.RectangleF(0, 0, 48, 48), sf);
            }
            return Drawing.Icon.FromHandle(bmp.GetHicon());
        }
        catch
        {
            return Drawing.SystemIcons.Information;
        }
    }

    private static string StatusText(ConnectionState state) => state switch
    {
        ConnectionState.Connected => "✅ 已连接",
        ConnectionState.Connecting => "🔄 连接中…",
        _ => "⚠️ 未连接",
    };

    private static void UpdateConnectMenu(WinForms.ToolStripMenuItem reconnect, WinForms.ToolStripMenuItem disconnect)
    {
        reconnect.Visible = WSClient.Shared.State != ConnectionState.Connected;
        disconnect.Visible = WSClient.Shared.State == ConnectionState.Connected;
    }

    // ============================================================
    // 主窗口
    // ============================================================
    public void OpenMainWindow()
    {
        if (_mainWindow is not null)
        {
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Show();
            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
            return;
        }

        _mainWindow = new MainWindow();
        _mainWindow.Closed += (_, _) => _mainWindow = null;
        _mainWindow.Show();
    }

    // ============================================================
    // 设置变化
    // ============================================================
    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsStore.AutoSyncClipboard))
        {
            if (SettingsStore.Shared.AutoSyncClipboard)
            {
                ClipboardMonitor.Shared.IsEnabled = true;
                ClipboardMonitor.Shared.Start();
            }
            else
            {
                ClipboardMonitor.Shared.IsEnabled = false;
                ClipboardMonitor.Shared.Stop();
            }
        }
        else if (e.PropertyName == nameof(SettingsStore.AutoStart))
        {
            // 用户切换了自启开关 → 同步到 HKCU Run 注册表
            AutoStartService.Apply(SettingsStore.Shared.AutoStart);
        }
    }

    // ============================================================
    // 收到新消息：落盘 + 写剪贴板 + 弹 Toast
    // ============================================================
    private void OnMessageReceived(SyncMessage msg)
    {
        // 1) 落盘到本地历史
        HistoryStore.Shared.Append(msg);

        // 2) 剪贴板类：自动写入本机剪贴板
        if (msg.IsClipboard)
        {
            ClipboardWriter.Apply(msg.Payload);
        }

        // 3) 弹 Toast
        Dispatcher.BeginInvoke(() => ToastManager.Shared.Show(msg));
    }
}

/// <summary>P/Invoke 用于把已存在的实例拉到前台。</summary>
internal static class NativeMethods
{
    public const int HWND_BROADCAST = 0xFFFF;
    public const int HWND_MESSAGE = -3;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    public static extern uint RegisterWindowMessage(string lpString);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
