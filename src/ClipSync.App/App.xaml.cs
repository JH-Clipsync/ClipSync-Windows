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
        base.OnStartup(e);

        // 1) 单实例守卫
        EnsureSingleInstance();

        // 2) 日志与数据目录
        AppPaths.EnsureRoot();
        Directory.CreateDirectory(AppPaths.LogDirectory);
        Log.UseDirectory(AppPaths.LogDirectory);
        Log.Info("[App] 启动 ClipSync Windows");

        // 3) Dispatcher 注入：WSClient 和 HistoryStore 都要把回调投到 UI 线程
        var dispatcher = Dispatcher.CurrentDispatcher;
        Action<Action> dispatch = action =>
        {
            if (dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        };
        WSClient.Shared.UseDispatcher(dispatch);
        HistoryStore.Shared.UseDispatcher(dispatch);

        // 4) 剪贴板监听绑定
        ClipboardMonitor.Shared.Bind(WSClient.Shared);

        // 5) 托盘
        SetupTrayIcon();

        // 6) 消息处理：落历史 + 写剪贴板 + 弹 Toast
        WSClient.Shared.MessageReceived += OnMessageReceived;

        // 7) 账号密码已填 → 自动连接
        var s = SettingsStore.Shared;
        if ((s.HasCredentials || s.Token.Length > 0))
        {
            _ = WSClient.Shared.ConnectAsync(s);
        }

        // 8) 剪贴板自动同步开关
        if (s.AutoSyncClipboard)
        {
            ClipboardMonitor.Shared.IsEnabled = true;
            ClipboardMonitor.Shared.Start();
        }

        // 9) 设置变化：同步开关 → 启停剪贴板监听
        s.PropertyChanged += Settings_PropertyChanged;

        // 10) 打开主窗口（首次启动默认显示）
        OpenMainWindow();
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
    // 单实例
    // ============================================================
    private static void EnsureSingleInstance()
    {
        var mutexName = @"Global\ClipSync-Windows-SingleInstance-Mutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out var createdNew);
        if (createdNew) return;

        // 已经有实例在跑：唤醒它的主窗口，自己退出
        try
        {
            var existing = System.Diagnostics.Process.GetProcessesByName(
                Path.GetFileNameWithoutExtension(System.Diagnostics.Process.GetCurrentProcess().ProcessName))
                .FirstOrDefault(p => p.Id != Environment.ProcessId);
            if (existing is not null)
            {
                NativeMethods.ShowWindow(existing.MainWindowHandle, 9); // SW_RESTORE
                NativeMethods.SetForegroundWindow(existing.MainWindowHandle);
            }
        }
        catch { /* 找不到也无所谓，直接退出就好 */ }

        Environment.Exit(0);
    }

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

    private static Drawing.Icon GetTrayIcon()
    {
        // 优先从 exe 资源里取 App.ico；没有就用 SystemIcons.Information 兜底，
        // 任何情况下托盘上都能看到一个图标
        try
        {
            // 用内存生成一个简易图标：48x48 紫色背景 + 白色气泡
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
        if (e.PropertyName != nameof(SettingsStore.AutoSyncClipboard)) return;
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
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
