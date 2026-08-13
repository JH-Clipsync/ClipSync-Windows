using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipSync.App.UI;

using ClipSync.App.Services;
using ClipSync.Core.Crypto;
using ClipSync.Core.Net;
using ClipSync.Core.Protocol;
using ClipSync.Core.Storage;

// ============================================================
// HomeView：主页（集中式控制台，与 Mac 端 HomeView.swift 对齐）
// - 顶部：连接状态卡
// - 账号卡：服务器地址 + 用户名 + 密码 + 连接按钮
// - 加密卡：E2EE 开关 + 同步密码 + 指纹
// - 同步卡：剪贴板自动同步 + 弹窗显示内容开关
// - 信息卡：短信/剪贴板计数 + 最近一条消息
// ============================================================
public class HomeView
{
    public FrameworkElement Root { get; }

    private readonly MainWindow _window;

    private readonly TextBlock _statusText;
    private readonly Border _statusCard;
    private readonly TextBlock _serverHint;
    private readonly StackPanel _authErrorPanel;
    private readonly TextBlock _authError;
    private readonly Button _authErrorGoBtn;
    private readonly Button _connectBtn;

    private readonly TextBlock _smsCount;
    private readonly TextBlock _clipCount;
    private readonly Panel _latestContainer;

    private readonly Border _onboardingBanner;

    public HomeView(MainWindow window)
    {
        _window = window;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            Padding = new Thickness(20),
        };

        var col = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        scroll.Content = col;

        // 0) 首次设置横幅：未配置完成前置顶引导
        _onboardingBanner = BuildOnboardingBanner();
        col.Children.Add(_onboardingBanner);
        AddSpacer(col, 16);

        // 1) 状态卡
        (_statusCard, _statusText, _serverHint, _authErrorPanel, _authError, _authErrorGoBtn, _connectBtn) = BuildStatusCard();
        col.Children.Add(_statusCard);
        AddSpacer(col, 16);

        // 2) 信息区（短信/剪贴板计数 + 最近消息）
        (_smsCount, _clipCount, _latestContainer) = BuildInfoSection(col);

        Root = scroll;

        // 连接按钮事件
        _connectBtn.Click += async (_, _) =>
        {
            if (WSClient.Shared.State == ConnectionState.Connected
                || WSClient.Shared.State == ConnectionState.Connecting)
            {
                WSClient.Shared.Disconnect();
                return;
            }

            // 未配置账号密码时不直接连接，给出提示并提供跳转入口
            if (!SettingsStore.Shared.HasCredentials)
            {
                ShowAuthError(
                    SettingsStore.Shared.Username.Length == 0
                        ? "请先设置用户名和密码后再连接。"
                        : "请先设置登录密码后再连接。",
                    showGoSettings: true);
                return;
            }

            ShowAuthError("", showGoSettings: false);
            await WSClient.Shared.ConnectAsync(SettingsStore.Shared);
        };

        // 实时刷新：WS 状态 / 历史变化
        WSClient.Shared.PropertyChanged += Ws_PropertyChanged;
        HistoryStore.Shared.Changed += RefreshInfo;
        SettingsStore.Shared.PropertyChanged += Settings_PropertyChanged;

        Refresh();
    }

    public void Refresh()
    {
        UpdateStatusCard();
        UpdateOnboardingBanner();
        RefreshInfo();
    }

    public void RefreshClipboardToggle()
    {
        // 设置统一在 SettingsView 里修改，这里不再持有本地开关引用
    }

    // ============================================================
    // 状态卡
    // ============================================================
    private (Border card, TextBlock status, TextBlock hint, StackPanel errorPanel, TextBlock error, Button goBtn, Button btn) BuildStatusCard()
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0x63, 0x66, 0xF1)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x63, 0x66, 0xF1)),
            BorderThickness = new Thickness(1),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 左：图标
        var iconCircle = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(26),
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0x10, 0xB9, 0x81)),
            Margin = new Thickness(0, 0, 14, 0),
            Child = new TextBlock
            {
                Text = "✓",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Tag = "iconCircle",
        };
        Grid.SetColumn(iconCircle, 0);
        grid.Children.Add(iconCircle);

        // 中：状态文字
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var status = new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
        };
        var hint = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Margin = new Thickness(0, 4, 0, 0),
            FontFamily = new FontFamily("Consolas, Courier New"),
        };
        var error = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        // 错误提示行：错误文字 + 右侧"去设置"按钮（点了跳转到设置页填账号密码）
        var goBtn = new Button
        {
            Content = "去设置",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(10, 3, 10, 3),
            Background = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        goBtn.Template = RoundCornerBtnTemplate();
        goBtn.Click += (_, _) => _window.NavigateToSettings();

        var errorPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 3, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        errorPanel.Children.Add(error);
        errorPanel.Children.Add(goBtn);

        mid.Children.Add(status);
        mid.Children.Add(hint);
        mid.Children.Add(errorPanel);
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        // 右：按钮
        var btn = new Button
        {
            Content = "连接",
            Padding = new Thickness(16, 7, 16, 7),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        btn.Template = RoundCornerBtnTemplate();
        Grid.SetColumn(btn, 2);
        grid.Children.Add(btn);

        card.Child = grid;
        return (card, status, hint, errorPanel, error, goBtn, btn);
    }

    private void UpdateStatusCard()
    {
        var st = WSClient.Shared.State;
        var s = _statusCard;

        // 图标 + 配色
        if (_statusCard.Child is Grid g && g.Children[0] is Border icon)
        {
            switch (st)
            {
                case ConnectionState.Connected:
                    icon.Background = new SolidColorBrush(Color.FromArgb(0x26, 0x10, 0xB9, 0x81));
                    if (icon.Child is TextBlock tb) { tb.Text = "✓"; tb.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); }
                    s.Background = new SolidColorBrush(Color.FromArgb(0x0F, 0x10, 0xB9, 0x81));
                    s.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x10, 0xB9, 0x81));
                    // 首次连接成功后自动标记设置完成，横幅消失
                    if (!SettingsStore.Shared.OnboardingCompleted)
                    {
                        SettingsStore.Shared.OnboardingCompleted = true;
                        UpdateOnboardingBanner();
                    }
                    break;
                case ConnectionState.Connecting:
                    icon.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xF5, 0x9E, 0x0B));
                    if (icon.Child is TextBlock tb2) { tb2.Text = "↻"; tb2.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); }
                    s.Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xF5, 0x9E, 0x0B));
                    s.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xF5, 0x9E, 0x0B));
                    break;
                default:
                    icon.Background = new SolidColorBrush(Color.FromArgb(0x20, 0x6B, 0x72, 0x80));
                    if (icon.Child is TextBlock tb3) { tb3.Text = "×"; tb3.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)); }
                    s.Background = new SolidColorBrush(Color.FromArgb(0x08, 0x00, 0x00, 0x00));
                    s.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0x00, 0x00, 0x00));
                    break;
            }
        }

        _statusText.Text = st switch
        {
            ConnectionState.Connected => "已连接",
            ConnectionState.Connecting => "连接中…",
            _ => "未连接",
        };

        var norm = ServerAddress.Normalize(SettingsStore.Shared.ServerUrl);
        _serverHint.Text = string.IsNullOrEmpty(norm) ? "未填写服务器地址" : norm;

        var err = WSClient.Shared.AuthError;
        if (!string.IsNullOrEmpty(err) && st == ConnectionState.Disconnected)
        {
            // 连接返回的错误：缺少账号密码时提供"去设置"按钮，其他错误只显示文字
            var needsSettings = !SettingsStore.Shared.HasCredentials;
            ShowAuthError(err, showGoSettings: needsSettings);
        }
        else if (st != ConnectionState.Disconnected)
        {
            ShowAuthError("", showGoSettings: false);
        }

        // 按钮文案
        switch (st)
        {
            case ConnectionState.Connecting:
                _connectBtn.Content = "取消";
                _connectBtn.Background = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
                _connectBtn.IsEnabled = true;
                break;
            case ConnectionState.Connected:
                _connectBtn.Content = "断开";
                _connectBtn.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                _connectBtn.IsEnabled = true;
                break;
            default:
                _connectBtn.Content = "连接";
                _connectBtn.Background = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));
                // 始终可点：未配置时点击会提示并引导去设置页，而不是把按钮置灰
                _connectBtn.IsEnabled = true;
                break;
        }
    }

    /// <summary>在状态卡上显示/隐藏错误提示行。</summary>
    /// <param name="message">错误文字，空串表示隐藏。</param>
    /// <param name="showGoSettings">是否显示右侧"去设置"按钮。</param>
    private void ShowAuthError(string message, bool showGoSettings)
    {
        if (string.IsNullOrEmpty(message))
        {
            _authErrorPanel.Visibility = Visibility.Collapsed;
            _authErrorGoBtn.Visibility = Visibility.Collapsed;
            return;
        }

        _authError.Text = message;
        _authErrorPanel.Visibility = Visibility.Visible;
        _authErrorGoBtn.Visibility = showGoSettings ? Visibility.Visible : Visibility.Collapsed;
    }

    // ============================================================
    // 账号卡
    // ============================================================
    private (TextBox server, TextBox username, PasswordBox password, Image? badge) BuildServerCard(Panel parent)
    {
        var (card, body) = CardContainer("账号", Colors.DodgerBlue);
        parent.Children.Add(card);

        // 服务器
        body.Children.Add(LabeledInput(
            "🌐",
            out var serverInput,
            "服务器地址，例如 wss://www.95qw.com/clipsync 或 192.168.1.10:8080",
            isPassword: false));

        var resolvedHint = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            FontFamily = new FontFamily("Consolas, Courier New"),
            Margin = new Thickness(26, 2, 0, 6),
        };
        body.Children.Add(resolvedHint);
        var serverTextBox = (TextBox)serverInput;
        serverTextBox.TextChanged += (_, _) =>
        {
            var n = ServerAddress.Normalize(serverTextBox.Text);
            var ws = ServerAddress.WsBase(serverTextBox.Text);
            resolvedHint.Text = string.IsNullOrEmpty(n)
                ? "请填写服务器地址"
                : $"HTTP 基址：{n}，WebSocket 基址：{ws}";
        };
        var n0 = ServerAddress.Normalize(SettingsStore.Shared.ServerUrl);
        var ws0 = ServerAddress.WsBase(SettingsStore.Shared.ServerUrl);
        resolvedHint.Text = string.IsNullOrEmpty(n0)
            ? "请填写服务器地址"
            : $"HTTP 基址：{n0}，WebSocket 基址：{ws0}";

        // 用户名
        body.Children.Add(LabeledInput(
            "👤",
            out var usernameInput,
            "用户名",
            isPassword: false));

        // 密码
        body.Children.Add(LabeledInput(
            "🔒",
            out FrameworkElement? passwordInput,
            "密码",
            isPassword: true));

        var credsHint = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Margin = new Thickness(0, 6, 0, 0),
            Text = "填写账号密码后点「连接」，会自动校验并取得 Token",
        };
        credsHint.SetBinding(System.Windows.UIElement.VisibilityProperty, new System.Windows.Data.Binding
        {
            Source = SettingsStore.Shared,
            Path = new PropertyPath(nameof(SettingsStore.HasCredentials)),
            Converter = new BoolToVisibilityConverter { Invert = true },
        });
        body.Children.Add(credsHint);

        return ((TextBox)serverInput, (TextBox)usernameInput, (PasswordBox)passwordInput, null);
    }

    // ============================================================
    // 加密卡
    // ============================================================
    private (CheckBox e2ee, PasswordBox syncPwd, TextBlock status, TextBlock decryptErr) BuildEncryptionCard(Panel parent)
    {
        var (card, body) = CardContainer("端到端加密", Colors.Purple);
        parent.Children.Add(card);

        var e2ee = new CheckBox
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = "🛡️ ", Margin = new Thickness(0,0,6,0), Foreground = Brushes.Purple },
                    new TextBlock { Text = "启用端到端加密", FontSize = 13, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center },
                },
            },
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 13,
        };
        body.Children.Add(e2ee);

        var desc = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Margin = new Thickness(0, 0, 0, 8),
            Text = "用同步密码加密内容，服务端只转发密文",
        };
        body.Children.Add(desc);

        // 同步密码输入
        var spContainer = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
        spContainer.SetBinding(System.Windows.UIElement.VisibilityProperty, new System.Windows.Data.Binding
        {
            Source = e2ee,
            Path = new PropertyPath(nameof(CheckBox.IsChecked)),
            Converter = new BoolToVisibilityConverter(),
        });
        var gridWrap = new Grid();
        gridWrap.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        gridWrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new TextBlock
        {
            Text = "🔑",
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 18,
        };
        Grid.SetColumn(icon, 0);
        gridWrap.Children.Add(icon);
        var syncPwd = new PasswordBox
        {
            PasswordChar = '•',
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Grid.SetColumn(syncPwd, 1);
        gridWrap.Children.Add(syncPwd);
        spContainer.Children.Add(gridWrap);
        var pwdHint = new TextBlock
        {
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Margin = new Thickness(26, 2, 0, 0),
            Text = "两端填写同一密码才能互相解密；留空则使用内置默认密码",
        };
        spContainer.Children.Add(pwdHint);
        body.Children.Add(spContainer);

        // 加密状态文字
        var status = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        body.Children.Add(status);

        var decryptErr = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        body.Children.Add(decryptErr);

        return (e2ee, syncPwd, status, decryptErr);
    }

    private void UpdateEncryptionStatus()
    {
        // 加密状态已迁移到 SettingsView，主页面不再展示
    }

    // ============================================================
    // 同步卡（保留占位，实际设置已迁移到 SettingsView）
    // ============================================================
    private (CheckBox autoClip, CheckBox autoStart, CheckBox showContent) BuildSyncCard(Panel parent)
    {
        var (card, body) = CardContainer("同步", Colors.SeaGreen);
        parent.Children.Add(card);

        var autoClip = new CheckBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = "📋 ", Margin = new Thickness(0,0,6,0) },
                    new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = "自动同步剪贴板", FontSize = 13, FontWeight = FontWeights.Medium },
                            new TextBlock { Text = "电脑复制的内容实时推送到手机", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)) },
                        },
                    },
                },
            },
        };
        body.Children.Add(autoClip);

        var div1 = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0x00, 0x00)),
            Margin = new Thickness(0, 4, 0, 10),
        };
        body.Children.Add(div1);

        // 开机自启：写 HKCU Run，不需要管理员权限
        var autoStart = new CheckBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = "🚀 ", Margin = new Thickness(0,0,6,0) },
                    new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = "开机自动启动", FontSize = 13, FontWeight = FontWeights.Medium },
                            new TextBlock { Text = "登录 Windows 后后台自动运行", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)) },
                        },
                    },
                },
            },
        };
        body.Children.Add(autoStart);

        var div2 = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0x00, 0x00)),
            Margin = new Thickness(0, 4, 0, 10),
        };
        body.Children.Add(div2);

        var showContent = new CheckBox
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = "🔔 ", Margin = new Thickness(0,0,6,0) },
                    new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = "显示消息内容", FontSize = 13, FontWeight = FontWeights.Medium },
                            new TextBlock { Text = "关闭后弹窗只显示占位提示", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)) },
                        },
                    },
                },
            },
        };
        body.Children.Add(showContent);

        return (autoClip, autoStart, showContent);
    }

    // ============================================================
    // 信息区 + 最近消息
    // ============================================================
    private (TextBlock sms, TextBlock clip, Panel latestPanel) BuildInfoSection(Panel parent)
    {
        // 计数卡
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sms = StatTile("短信", "0", Colors.RoyalBlue, "💬");
        Grid.SetColumn(sms, 0);
        row.Children.Add(sms);

        var clip = StatTile("剪贴板", "0", Colors.DarkSlateBlue, "📋");
        Grid.SetColumn(clip, 2);
        row.Children.Add(clip);

        // 找出 tile 里的数值 TextBlock
        // StatTile 结构：card(Border) → .Child = row(StackPanel) → [1] = labels(StackPanel) → [0] = value(TextBlock)
        var smsCount = (TextBlock)((StackPanel)((StackPanel)((Border)sms).Child).Children[1]).Children[0];
        var clipCount = (TextBlock)((StackPanel)((StackPanel)((Border)clip).Child).Children[1]).Children[0];

        parent.Children.Add(row);
        AddSpacer(parent, 16);

        // 最近消息标题
        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        headerRow.Children.Add(new TextBlock
        {
            Text = "🕒  最近消息",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
        });
        parent.Children.Add(headerRow);

        var container = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        parent.Children.Add(container);

        return (smsCount, clipCount, container);
    }

    private static Border StatTile(string title, string value, Color color, string icon)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x0D, 0x00, 0x00, 0x00)),
            Padding = new Thickness(10),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var iconBg = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, color.R, color.G, color.B)),
            Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock
            {
                Text = icon,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        row.Children.Add(iconBg);
        var labels = new StackPanel();
        labels.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
        });
        labels.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
        });
        row.Children.Add(labels);
        var filler = new Border
        {
            Width = 1,
        };
        Grid.SetColumn(filler, 1);
        row.Children.Add(filler);
        card.Child = row;
        return card;
    }

    private void RefreshInfo()
    {
        _smsCount.Text = HistoryStore.Shared.SmsCount.ToString();
        _clipCount.Text = HistoryStore.Shared.ClipboardCount.ToString();

        _latestContainer.Children.Clear();
        var msg = HistoryStore.Shared.Messages.FirstOrDefault();
        if (msg is null)
        {
            _latestContainer.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(0x0A, 0x00, 0x00, 0x00)),
                Padding = new Thickness(10),
                Child = new TextBlock
                {
                    Text = "暂无消息",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                },
            });
            return;
        }

        _latestContainer.Children.Add(BuildLatestRow(msg));
    }

    private Border BuildLatestRow(SyncMessage msg)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0x00, 0x00, 0x00)),
            Padding = new Thickness(10),
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var isSms = LooksLikeSms(msg);
        var icon = new TextBlock
        {
            Text = isSms ? "💬" : "📋",
            FontSize = 13,
            Width = 22,
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1)),
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var content = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = isSms ? "短信" : "剪贴板",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 0, 6, 0),
        });
        var phone = SenderPhone(msg);
        if (phone is not null)
        {
            titleRow.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(999),
                Background = new SolidColorBrush(Color.FromArgb(0x12, 0x00, 0x00, 0x00)),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = phone,
                    FontSize = 10,
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                },
            });
        }
        var spacer = new Border { Width = 1, Background = Brushes.Transparent };
        titleRow.Children.Add(spacer);
        titleRow.Children.Add(new TextBlock
        {
            Text = msg.Date.ToString("MM-dd HH:mm"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            HorizontalAlignment = HorizontalAlignment.Right,
        });
        content.Children.Add(titleRow);

        // 图片或文本
        if (msg.Content == MessageContent.Image && !string.IsNullOrEmpty(msg.Payload.Data))
        {
            try
            {
                var bytes = Convert.FromBase64String(msg.Payload.Data);
                using var ms = new MemoryStream(bytes);
                var bmp = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                bmp.Freeze();
                content.Children.Add(new Border
                {
                    Margin = new Thickness(0, 6, 0, 0),
                    MaxHeight = 120,
                    CornerRadius = new CornerRadius(8),
                    ClipToBounds = true,
                    Child = new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        MaxHeight = 120,
                    },
                });
            }
            catch { /* 图片解码失败就忽略 */ }
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = PreviewText(msg),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 0x11, 0x18, 0x27)),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }

        // 按钮行
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var code = ExtractedCode(msg);
        if (code is not null)
        {
            actions.Children.Add(MakePillBtn($"复制 {code}", primary: true, _ =>
            {
                ClipboardWriter.CopyText(code);
            }));
        }
        actions.Children.Add(MakePillBtn("复制", primary: code is null, _ =>
        {
            ClipboardWriter.Apply(msg.Payload);
        }));
        content.Children.Add(actions);

        Grid.SetColumn(content, 1);
        row.Children.Add(content);
        card.Child = row;
        return card;
    }

    private static Button MakePillBtn(string text, bool primary, Action<object?> onClick)
    {
        var b = new Button
        {
            Content = text,
            FontSize = 11,
            FontWeight = primary ? FontWeights.Medium : FontWeights.Regular,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Background = primary
                ? new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1))
                : new SolidColorBrush(Color.FromArgb(0xFF, 0xF3, 0xF4, 0xF6)),
            Foreground = primary ? Brushes.White : Brushes.Black,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.Click += (s, e) => onClick(s);
        b.Template = RoundCornerBtnTemplate();
        return b;
    }

    private static ControlTemplate RoundCornerBtnTemplate()
    {
        // 修复：去掉 TargetName="bd"，避免 FrameworkElementFactory NameScope 在 Seal 时崩溃。
        // hover/press 时直接改 Button（TemplatedParent）的 Opacity，视觉效果与改 Border 等价。
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        borderFactory.SetValue(Border.BackgroundProperty,
            new TemplateBindingExtension(Control.BackgroundProperty));
        borderFactory.SetValue(Border.PaddingProperty,
            new TemplateBindingExtension(Control.PaddingProperty));
        borderFactory.SetValue(System.Windows.UIElement.SnapsToDevicePixelsProperty, true);

        var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        presenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(presenterFactory);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = borderFactory };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = 0.9 });
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = 0.75 });
        template.Triggers.Add(pressed);
        return template;
    }

    // ============================================================
    // 通用辅助
    // ============================================================
    private static (Border card, StackPanel body) CardContainer(string title, Color accent)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0x00, 0x00, 0x00)),
            Padding = new Thickness(14),
        };
        var body = new StackPanel();
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
        };
        header.Children.Add(new Border
        {
            Width = 4,
            Height = 16,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(accent),
            Margin = new Thickness(0, 3, 8, 0),
        });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        body.Children.Add(header);
        card.Child = body;
        return (card, body);
    }

    // ============================================================
    // 首次设置横幅：直接嵌入主窗口，替代原来的模态安装向导
    // ============================================================
    private Border BuildOnboardingBanner()
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0x63, 0x66, 0xF1)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x63, 0x66, 0xF1)),
            BorderThickness = new Thickness(1),
            Visibility = SettingsStore.Shared.OnboardingCompleted
                ? Visibility.Collapsed
                : Visibility.Visible,
        };

        var col = new StackPanel();
        var title = new TextBlock
        {
            Text = "欢迎使用 ClipSync 👋",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
        };
        var desc = new TextBlock
        {
            Text = "请完成下方账号与加密设置，然后点击「连接」。首次连接成功后，此提示将自动消失。",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        col.Children.Add(title);
        col.Children.Add(desc);
        card.Child = col;
        return card;
    }

    private void UpdateOnboardingBanner()
    {
        _onboardingBanner.Visibility = SettingsStore.Shared.OnboardingCompleted
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static FrameworkElement LabeledInput(
        string icon,
        out FrameworkElement inputControl,
        string placeholder,
        bool isPassword)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconTb = new TextBlock
        {
            Text = icon,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Width = 18,
        };
        Grid.SetColumn(iconTb, 0);
        grid.Children.Add(iconTb);

        if (isPassword)
        {
            var pb = new PasswordBox
            {
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 0, 10, 0),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderThickness = new Thickness(1),
                PasswordChar = '•',
                Margin = new Thickness(0, 0, 0, 8),
            };
            // 用 ToolTip 代替 placeholder（WPF PasswordBox 不支持 placeholder）
            pb.ToolTip = placeholder;
            Grid.SetColumn(pb, 1);
            grid.Children.Add(pb);
            inputControl = pb;
        }
        else
        {
            var tb = new TextBox
            {
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 0, 10, 0),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8),
            };
            // ToolTip 显示占位
            tb.ToolTip = placeholder;
            Grid.SetColumn(tb, 1);
            grid.Children.Add(tb);
            inputControl = tb;
        }
        return grid;
    }

    private static void AddSpacer(Panel p, double h)
    {
        p.Children.Add(new Border { Height = h });
    }

    // ============================================================
    // 消息派生属性（与 Mac 端 HomeView 一致）
    // ============================================================
    private static bool LooksLikeSms(SyncMessage msg)
    {
        if (msg.IsSms) return true;
        var raw = msg.Payload.Text ?? msg.Payload.Preview ?? "";
        return SmsPayloadSanitizer.HasSmsMarkers(raw);
    }

    private static string? SenderPhone(SyncMessage msg)
    {
        if (!LooksLikeSms(msg)) return null;
        if (!string.IsNullOrEmpty(msg.Payload.Sender)) return msg.Payload.Sender;
        var raw = msg.Payload.Text ?? msg.Payload.Preview ?? "";
        return SmsPayloadSanitizer.Sanitize(raw, null).Sender;
    }

    private static string? ExtractedCode(SyncMessage msg)
    {
        if (!LooksLikeSms(msg)) return null;
        var text = msg.Payload.Text ?? msg.Payload.Preview;
        if (string.IsNullOrEmpty(text)) return null;
        return SmsCodeExtractor.Extract(text);
    }

    private static string PreviewText(SyncMessage msg)
    {
        var raw = (string.IsNullOrEmpty(msg.Payload.Text) ? msg.Payload.Preview : msg.Payload.Text) ?? "";
        if (string.IsNullOrEmpty(raw) && msg.Payload.Mime?.StartsWith("image/") == true) return "[图片]";
        if (string.IsNullOrEmpty(raw)) return "新消息";
        var cleaned = LooksLikeSms(msg)
            ? SmsPayloadSanitizer.Sanitize(raw, msg.Payload.Sender).Text
            : raw;
        return cleaned.Length <= 120 ? cleaned : cleaned[..120] + "…";
    }

    // ============================================================
    // 事件响应
    // ============================================================
    private void Ws_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WSClient.State)
            or nameof(WSClient.AuthError)
            or nameof(WSClient.DecryptFailure))
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                UpdateStatusCard();
                UpdateOnboardingBanner();
            });
        }
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsStore.HasCredentials)
            or nameof(SettingsStore.Token))
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                UpdateStatusCard();
                // 用户补全了账号密码后，清掉"请先设置…"的提示
                if (SettingsStore.Shared.HasCredentials)
                {
                    ShowAuthError("", showGoSettings: false);
                }
            });
        }
        if (e.PropertyName is nameof(SettingsStore.E2eeEnabled)
            or nameof(SettingsStore.SyncPassword))
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(UpdateEncryptionStatus);
        }
    }
}

/// <summary>bool → Visibility。True=Visible，Invert=True 时取反。</summary>
public class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var v = value is true;
        if (Invert) v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
