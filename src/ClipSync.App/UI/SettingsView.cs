using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClipSync.Core.Net;
using ClipSync.Core.Storage;
using Microsoft.Win32;

namespace ClipSync.App.UI;

/// <summary>
/// 内嵌设置视图：账号、端到端加密、同步偏好、通用设置。
/// 设计为 UserControl，挂在 MainWindow 侧边栏导航里，不再以弹窗形式出现。
/// </summary>
public sealed class SettingsView : System.Windows.Controls.UserControl
{
    private TextBox _serverInput;
    private TextBox _usernameInput;
    private PasswordInput _passwordInput;
    private CheckBox _e2eeToggle;
    private PasswordInput _syncPasswordInput;
    private CheckBox _autoClipToggle;
    private CheckBox _autoStartToggle;
    private CheckBox _showContentToggle;
    private CheckBox _minimizeToTrayToggle;
    private TextBlock _resolvedHint;
    private TextBlock _encryptionStatus;
    private TextBlock _saveHint;
    private bool _loading;
    private System.Windows.Threading.DispatcherTimer? _autoSaveTimer;

    public FrameworkElement Root => this;

    public SettingsView()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = AppColors.PageBackgroundBrush,
            Padding = new Thickness(28, 24, 28, 24),
        };

        var col = new StackPanel();

        col.Children.Add(BuildGroup("账号", BuildAccountSection()));
        col.Children.Add(BuildSpacer(18));
        col.Children.Add(BuildGroup("端到端加密", BuildEncryptionSection()));
        col.Children.Add(BuildSpacer(18));
        col.Children.Add(BuildGroup("同步与行为", BuildBehaviorSection()));
        col.Children.Add(BuildSpacer(20));

        // 底部操作栏：保存按钮（右对齐）+ 保存提示（左对齐）
        // 注意：LastChildFill 必须为 false，否则最后一个子元素会被拉伸填满剩余空间，
        // 导致按钮的 HorizontalAlignment=Right 失效。
        var actionBar = new DockPanel
        {
            LastChildFill = false,
            Margin = new Thickness(0, 0, 0, 4),
        };

        _saveHint = new TextBlock
        {
            FontSize = 12,
            Foreground = AppColors.SuccessBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        DockPanel.SetDock(_saveHint, Dock.Left);
        actionBar.Children.Add(_saveHint);

        var saveBtn = new Button
        {
            Content = "保存设置",
            Width = 110,
            Height = 36,
            Style = (Style)Application.Current.FindResource("PrimaryButtonStyle"),
        };
        DockPanel.SetDock(saveBtn, Dock.Right);
        actionBar.Children.Add(saveBtn);

        col.Children.Add(actionBar);

        scroll.Content = col;
        Content = scroll;

        // 加载当前设置
        _loading = true;
        LoadFromStore();
        _loading = false;

        _serverInput.TextChanged += (_, _) => UpdateResolvedHint();
        _e2eeToggle.Checked += (_, _) => UpdateEncryptionStatus();
        _e2eeToggle.Unchecked += (_, _) => UpdateEncryptionStatus();
        _syncPasswordInput.PasswordChanged += (_, _) => UpdateEncryptionStatus();

        // 所有输入框失焦时静默自动保存（不弹"已保存"提示，不重连）
        _serverInput.LostFocus += InputLostFocus;
        _usernameInput.LostFocus += InputLostFocus;
        _passwordInput.LostFocus += InputLostFocus;
        _syncPasswordInput.LostFocus += InputLostFocus;
        // 开关项切换时立即自动保存
        _e2eeToggle.Checked += (_, _) => ScheduleAutoSave();
        _e2eeToggle.Unchecked += (_, _) => ScheduleAutoSave();
        _autoClipToggle.Checked += (_, _) => ScheduleAutoSave();
        _autoClipToggle.Unchecked += (_, _) => ScheduleAutoSave();
        _autoStartToggle.Checked += (_, _) => ScheduleAutoSave();
        _autoStartToggle.Unchecked += (_, _) => ScheduleAutoSave();
        _showContentToggle.Checked += (_, _) => ScheduleAutoSave();
        _showContentToggle.Unchecked += (_, _) => ScheduleAutoSave();
        _minimizeToTrayToggle.Checked += (_, _) => ScheduleAutoSave();
        _minimizeToTrayToggle.Unchecked += (_, _) => ScheduleAutoSave();

        saveBtn.Click += OnSave;
    }

    private void InputLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loading) ScheduleAutoSave();
    }

    /// <summary>防抖自动保存：400ms 内多次失焦只落一次盘。</summary>
    private void ScheduleAutoSave()
    {
        if (_loading) return;
        _autoSaveTimer?.Stop();
        _autoSaveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer?.Stop();
            ApplyChanges(reconnectIfNeeded: false);
        };
        _autoSaveTimer.Start();
    }

    public void Refresh() => LoadFromStore();

    private void LoadFromStore()
    {
        _serverInput.Text = SettingsStore.Shared.ServerUrl;
        _usernameInput.Text = SettingsStore.Shared.Username;
        _passwordInput.Password = SettingsStore.Shared.Password;
        _e2eeToggle.IsChecked = SettingsStore.Shared.E2eeEnabled;
        _syncPasswordInput.Password = SettingsStore.Shared.SyncPassword;
        _autoClipToggle.IsChecked = SettingsStore.Shared.AutoSyncClipboard;
        _autoStartToggle.IsChecked = SettingsStore.Shared.AutoStart;
        _showContentToggle.IsChecked = SettingsStore.Shared.ShowContent;
        _minimizeToTrayToggle.IsChecked = SettingsStore.Shared.MinimizeToTrayOnClose;
        UpdateResolvedHint();
        UpdateEncryptionStatus();
    }

    private FrameworkElement BuildAccountSection()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        panel.Children.Add(BuildLabel("服务器地址"));
        _serverInput = MakeInput();
        panel.Children.Add(_serverInput);

        _resolvedHint = new TextBlock
        {
            FontSize = 11,
            Foreground = AppColors.Gray500Brush,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Courier New"),
        };
        panel.Children.Add(_resolvedHint);

        panel.Children.Add(BuildLabel("用户名", new Thickness(0, 18, 0, 0)));
        _usernameInput = MakeInput();
        panel.Children.Add(_usernameInput);

        panel.Children.Add(BuildLabel("密码", new Thickness(0, 18, 0, 0)));
        _passwordInput = new PasswordInput();
        panel.Children.Add(_passwordInput);

        panel.Children.Add(new TextBlock
        {
            Text = "留空则保持原密码不变；需要修改密码时直接输入新密码。",
            FontSize = 11,
            Foreground = AppColors.Gray500Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });

        // "保存并连接"按钮：填完账号密码一键保存并回到主页发起连接
        var connectBtn = new Button
        {
            Content = "保存并连接",
            Width = 120,
            Height = 34,
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Application.Current.FindResource("PrimaryButtonStyle"),
        };
        connectBtn.Click += OnSaveAndConnect;
        panel.Children.Add(connectBtn);

        return panel;
    }

    private FrameworkElement BuildEncryptionSection()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        _e2eeToggle = new CheckBox
        {
            Content = new TextBlock
            {
                Text = "启用端到端加密",
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = AppColors.Gray900Brush,
            },
        };
        panel.Children.Add(_e2eeToggle);

        panel.Children.Add(new TextBlock
        {
            Text = "开启后，消息内容会在本机加密后再发送到服务端。两端必须使用同一个同步密码。",
            FontSize = 11,
            Foreground = AppColors.Gray500Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 6, 0, 14),
        });

        panel.Children.Add(BuildLabel("同步密码"));
        _syncPasswordInput = new PasswordInput();
        panel.Children.Add(_syncPasswordInput);

        panel.Children.Add(new TextBlock
        {
            Text = "留空将使用内置默认密码（各端通用，强度低于自设密码）。",
            FontSize = 11,
            Foreground = AppColors.Gray500Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });

        _encryptionStatus = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(_encryptionStatus);

        return panel;
    }

    private FrameworkElement BuildBehaviorSection()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        _autoClipToggle = BuildCheck("自动同步剪贴板", "电脑复制的内容实时推送到手机");
        panel.Children.Add(_autoClipToggle);
        _showContentToggle = BuildCheck("弹窗显示消息内容", "关闭后通知只显示占位，保护隐私");
        panel.Children.Add(_showContentToggle);
        _minimizeToTrayToggle = BuildCheck("关闭窗口时收进托盘", "点右上角 × 不退出程序，托盘图标保持运行");
        panel.Children.Add(_minimizeToTrayToggle);
        _autoStartToggle = BuildCheck("开机自动启动", "登录 Windows 后后台自动运行");
        panel.Children.Add(_autoStartToggle);
        return panel;
    }

    private static Border BuildGroup(string title, FrameworkElement content)
    {
        var card = new Border
        {
            Background = AppColors.WhiteBrush,
            CornerRadius = new CornerRadius(12),
            BorderBrush = AppColors.CardBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22),
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = AppColors.Gray900Brush,
            Margin = new Thickness(0, 0, 0, 14),
        });
        stack.Children.Add(content);
        card.Child = stack;
        return card;
    }

    private static Border BuildSpacer(double h) => new() { Height = h, Background = Brushes.Transparent };

    private static TextBlock BuildLabel(string text, Thickness? margin = null) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.Medium,
        Foreground = AppColors.Gray700Brush,
        Margin = margin ?? new Thickness(0, 0, 0, 8),
    };

    private static TextBox MakeInput() => new()
    {
        Height = 36,
        Padding = new Thickness(10, 0, 10, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
        FontSize = 13,
        BorderBrush = AppColors.InputBorderBrush,
        Background = AppColors.WhiteBrush,
        BorderThickness = new Thickness(1),
        Foreground = AppColors.Gray900Brush,
    };

    private static CheckBox BuildCheck(string title, string desc) => new()
    {
        Margin = new Thickness(0, 0, 0, 16),
        Content = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 13,
                    FontWeight = FontWeights.Medium,
                    Foreground = AppColors.Gray900Brush,
                },
                new TextBlock
                {
                    Text = desc,
                    FontSize = 11,
                    Foreground = AppColors.Gray500Brush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0),
                },
            },
        },
    };

    private void UpdateResolvedHint()
    {
        var s = _serverInput.Text;
        if (string.IsNullOrWhiteSpace(s)) { _resolvedHint.Text = ""; return; }
        var n = Core.Net.ServerAddress.Normalize(s);
        var ws = Core.Net.ServerAddress.WsBase(s);
        _resolvedHint.Text = $"HTTP: {n}    WebSocket: {ws}";
    }

    private void UpdateEncryptionStatus()
    {
        var pwd = _syncPasswordInput.Password;
        var enabled = _e2eeToggle.IsChecked == true;
        if (!enabled)
        {
            _encryptionStatus.Text = "加密已关闭：消息以明文传输";
            _encryptionStatus.Foreground = AppColors.Gray500Brush;
        }
        else if (string.IsNullOrEmpty(pwd))
        {
            _encryptionStatus.Text = "⚠️ 未填同步密码，将使用内置默认密码";
            _encryptionStatus.Foreground = AppColors.WarningBrush;
        }
        else
        {
            _encryptionStatus.Text = $"密钥指纹：{Core.Crypto.PayloadCipher.Fingerprint(pwd)}";
            _encryptionStatus.Foreground = AppColors.SuccessBrush;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var changed = ApplyChanges(reconnectIfNeeded: true);
        ShowSaveHint(changed);
    }

    private async void OnSaveAndConnect(object sender, RoutedEventArgs e)
    {
        ApplyChanges(reconnectIfNeeded: false);

        // 校验账号密码是否齐全
        if (!SettingsStore.Shared.HasCredentials)
        {
            _saveHint.Text = "请先填写用户名和密码";
            _saveHint.Foreground = AppColors.DangerBrush;
            _saveHint.Visibility = Visibility.Visible;
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (_, _) => { t.Stop(); _saveHint.Visibility = Visibility.Collapsed; };
            t.Start();
            return;
        }

        // 断开旧连接（如果在连），用新凭据重新连接
        if (WSClient.Shared.State is ConnectionState.Connected or ConnectionState.Connecting)
        {
            WSClient.Shared.Disconnect();
            await Task.Delay(200);
        }
        _ = WSClient.Shared.ConnectAsync(SettingsStore.Shared);

        // 回到主页
        NavigateHome();
    }

    /// <summary>
    /// 把当前 UI 值写入 SettingsStore。
    /// </summary>
    /// <param name="reconnectIfNeeded">true=影响连接的字段变化时自动断开重连；false=只保存不重连。</param>
    /// <returns>是否有任何字段发生了变化。</returns>
    private bool ApplyChanges(bool reconnectIfNeeded)
    {
        var oldServer = SettingsStore.Shared.ServerUrl;
        var oldUser = SettingsStore.Shared.Username;
        var oldPwd = SettingsStore.Shared.Password;
        var oldE2ee = SettingsStore.Shared.E2eeEnabled;
        var oldSyncPwd = SettingsStore.Shared.SyncPassword;
        var oldAutoClip = SettingsStore.Shared.AutoSyncClipboard;
        var oldAutoStart = SettingsStore.Shared.AutoStart;
        var oldShowContent = SettingsStore.Shared.ShowContent;
        var oldMinTray = SettingsStore.Shared.MinimizeToTrayOnClose;

        SettingsStore.Shared.ServerUrl = _serverInput.Text;
        SettingsStore.Shared.Username = _usernameInput.Text;

        var newPwd = _passwordInput.Password;
        if (!string.IsNullOrEmpty(newPwd))
        {
            SettingsStore.Shared.Password = newPwd;
        }

        SettingsStore.Shared.E2eeEnabled = _e2eeToggle.IsChecked == true;
        SettingsStore.Shared.SyncPassword = _syncPasswordInput.Password;
        SettingsStore.Shared.AutoSyncClipboard = _autoClipToggle.IsChecked == true;
        SettingsStore.Shared.AutoStart = _autoStartToggle.IsChecked == true;
        SettingsStore.Shared.ShowContent = _showContentToggle.IsChecked == true;
        SettingsStore.Shared.MinimizeToTrayOnClose = _minimizeToTrayToggle.IsChecked == true;

        UpdateAutoStart(SettingsStore.Shared.AutoStart);

        var changed = oldServer != SettingsStore.Shared.ServerUrl
            || oldUser != SettingsStore.Shared.Username
            || oldPwd != SettingsStore.Shared.Password
            || oldE2ee != SettingsStore.Shared.E2eeEnabled
            || oldSyncPwd != SettingsStore.Shared.SyncPassword
            || oldAutoClip != SettingsStore.Shared.AutoSyncClipboard
            || oldAutoStart != SettingsStore.Shared.AutoStart
            || oldShowContent != SettingsStore.Shared.ShowContent
            || oldMinTray != SettingsStore.Shared.MinimizeToTrayOnClose;

        if (reconnectIfNeeded && changed)
        {
            var connAffecting = oldServer != SettingsStore.Shared.ServerUrl
                || oldUser != SettingsStore.Shared.Username
                || oldPwd != SettingsStore.Shared.Password
                || oldE2ee != SettingsStore.Shared.E2eeEnabled
                || oldSyncPwd != SettingsStore.Shared.SyncPassword;
            if (connAffecting && WSClient.Shared.State == ConnectionState.Connected)
            {
                _ = ReconnectAsync();
            }
        }

        return changed;
    }

    private static async Task ReconnectAsync()
    {
        WSClient.Shared.Disconnect();
        await Task.Delay(200);
        await WSClient.Shared.ConnectAsync(SettingsStore.Shared);
    }

    private void ShowSaveHint(bool changed)
    {
        _saveHint.Text = changed ? "✓ 设置已保存" : "✓ 无变化";
        _saveHint.Foreground = AppColors.SuccessBrush;
        _saveHint.Visibility = Visibility.Visible;
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        t.Tick += (_, _) => { t.Stop(); _saveHint.Visibility = Visibility.Collapsed; };
        t.Start();
    }

    /// <summary>通过主窗口回到主页。</summary>
    private static void NavigateHome()
    {
        if (Application.Current?.MainWindow is MainWindow mw)
        {
            mw.NavigateHome();
        }
    }

    private static void UpdateAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (key is null) return;
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? System.IO.Path.Combine(AppContext.BaseDirectory, "ClipSync.App.exe");
            if (enabled) key.SetValue("ClipSync", $"\"{exe}\"");
            else key.DeleteValue("ClipSync", throwOnMissingValue: false);
        }
        catch { }
    }
}
