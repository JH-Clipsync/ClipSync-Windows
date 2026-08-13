using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace ClipSync.App.UI;

using ClipSync.App.Services;
using ClipSync.Core.Crypto;
using ClipSync.Core.Net;
using ClipSync.Core.Storage;

// ============================================================
// OnboardingWizard：首次启动配置向导（5 步）
// 对齐 Mac 端 onboarding 流程
//   Step 1. 欢迎 - 介绍功能
//   Step 2. 服务器 + 账密 - 支持"测试连接"
//   Step 3. 同步密码 - E2EE 开关 + 实时指纹
//   Step 4. 偏好设置 - 开机自启 / 自动同步 / 显示内容 / 托盘
//   Step 5. 完成 - 配置摘要 + "完成并连接"
// ============================================================
public class OnboardingWizard : Window
{
    private readonly SettingsStore _s = SettingsStore.Shared;

    private int _step;
    private const int TotalSteps = 5;

    private readonly Grid _rootGrid;
    private readonly Border _contentHost;
    private readonly Button _prevBtn;
    private readonly Button _nextBtn;
    private readonly TextBlock _pageIndicator;
    private readonly List<Border> _dots = new();

    // Step 2
    private TextBox? _s2Server;
    private TextBox? _s2User;
    private PasswordBox? _s2Pwd;
    private TextBlock? _s2Msg;

    // Step 3
    private CheckBox? _s3E2ee;
    private PasswordBox? _s3SyncPwd;
    private TextBlock? _s3Status;

    // Step 4
    private CheckBox? _s4AutoStart;
    private CheckBox? _s4AutoClip;
    private CheckBox? _s4Show;
    private CheckBox? _s4Tray;

    public OnboardingWizard()
    {
        Title = "ClipSync 安装向导";
        Width = 520;
        Height = 640;
        MinWidth = 480;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.White;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Icon = App.GetWindowIcon();
        ShowInTaskbar = true;

        _rootGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            }
        };
        Content = _rootGrid;

        // -------- Header --------
        // 【重要】BuildHeader 只调一次：既把 UI 放进视觉树，也把里面的 page TextBlock 引用拿出来
        // 调两次会导致 _dots 列表被重复填充（变成 10 个而不是 5 个），进度条颜色就错位了
        var headerBorder = (Border)BuildHeader();
        _rootGrid.Children.Add(headerBorder);
        var headerStack = (StackPanel)headerBorder.Child;
        var progressGrid = (Grid)headerStack.Children[2];
        _pageIndicator = (TextBlock)progressGrid.Children[1];

        // -------- Content --------
        _contentHost = new Border { Padding = new Thickness(28, 4, 28, 4) };
        Grid.SetRow(_contentHost, 1);
        _rootGrid.Children.Add(_contentHost);

        // -------- Footer --------
        var footer = new Border
        {
            Padding = new Thickness(28, 12, 28, 20),
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xF9, 0xFA, 0xFB)),
        };
        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _prevBtn = MakeBtn("上一步", secondary: true, disabled: true);
        _prevBtn.Click += (_, _) => { if (_step > 0) { _step--; Render(); } };
        _nextBtn = MakeBtn("下一步", secondary: false);
        _nextBtn.Click += async (_, _) => await Next();
        Grid.SetColumn(_prevBtn, 1);
        Grid.SetColumn(_nextBtn, 2);
        buttons.Children.Add(_prevBtn);
        buttons.Children.Add(_nextBtn);
        footer.Child = buttons;
        Grid.SetRow(footer, 2);
        _rootGrid.Children.Add(footer);

        Render();
    }

    // ============================================================
    // Header（标题 + 步骤点 + 页码）
    // ============================================================
    private UIElement BuildHeader()
    {
        var stack = new StackPanel { Margin = new Thickness(28, 24, 28, 12) };
        stack.Children.Add(new TextBlock
        {
            Text = "ClipSync 初次配置",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "2 分钟完成配置，即可跨设备同步剪贴板和短信验证码",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Margin = new Thickness(0, 4, 0, 16),
        });
        var progress = new Grid();
        progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dotsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < TotalSteps; i++)
        {
            var d = new Border
            {
                Width = 28, Height = 6, CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xE5, 0xE7, 0xEB)),
            };
            _dots.Add(d);
            dotsPanel.Children.Add(d);
        }
        var page = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(dotsPanel, 0);
        Grid.SetColumn(page, 1);
        progress.Children.Add(dotsPanel);
        progress.Children.Add(page);
        stack.Children.Add(progress);
        return new Border { Child = stack, Tag = "header-built" };
    }

    // ============================================================
    // 步骤流转
    // ============================================================
    private async Task Next()
    {
        if (!Commit()) return;
        if (_step == TotalSteps - 1)
        {
            _s.OnboardingCompleted = true;
            _s.SaveNow();
            if (_s.AutoStart) AutoStartService.Apply(true);
            DialogResult = true;
            Close();
            return;
        }
        _step++;
        await Task.CompletedTask;
        Render();
    }

    /// <summary>提交当前步骤数据到 Settings，并做校验。返回 true 通过。</summary>
    private bool Commit()
    {
        switch (_step)
        {
            case 1:
                if (_s2Server is not null) _s.ServerUrl = _s2Server.Text;
                if (_s2User is not null) _s.Username = _s2User.Text;
                if (_s2Pwd is not null) _s.Password = _s2Pwd.Password;
                var norm = ServerAddress.Normalize(_s.ServerUrl);
                if (norm.Length == 0) { S2Err("请填写服务器地址，例如 192.168.1.10:8080"); return false; }
                if (_s.Username.Length == 0 || _s.Password.Length == 0) { S2Err("请填写用户名和密码"); return false; }
                S2Clear();
                return true;
            case 2:
                if (_s3E2ee is not null) _s.E2eeEnabled = _s3E2ee.IsChecked == true;
                if (_s3SyncPwd is not null) _s.SyncPassword = _s3SyncPwd.Password;
                return true;
            case 3:
                if (_s4AutoStart is not null) _s.AutoStart = _s4AutoStart.IsChecked == true;
                if (_s4AutoClip is not null) _s.AutoSyncClipboard = _s4AutoClip.IsChecked == true;
                if (_s4Show is not null) _s.ShowContent = _s4Show.IsChecked == true;
                if (_s4Tray is not null) _s.MinimizeToTrayOnClose = _s4Tray.IsChecked == true;
                return true;
            default:
                return true;
        }
    }

    // ============================================================
    // 渲染当前步
    // ============================================================
    private void Render()
    {
        // dots
        for (var i = 0; i < _dots.Count; i++)
        {
            _dots[i].Background = new SolidColorBrush(i <= _step
                ? Color.FromRgb(0x63, 0x66, 0xF1)
                : Color.FromArgb(0xFF, 0xE5, 0xE7, 0xEB));
        }
        // 页码（找 header 里的 TextBlock）：第二次 Render 时 BuildHeader 不再调用，使用 header 对象内的 _pageIndicator 引用失效 → 直接从头找
        if (_rootGrid.Children[0] is Border h && h.Child is StackPanel sp && sp.Children[2] is Grid g && g.Children[1] is TextBlock tb)
        {
            tb.Text = $"{_step + 1} / {TotalSteps}";
        }

        _prevBtn.IsEnabled = _step > 0;
        _prevBtn.Opacity = _step > 0 ? 1.0 : 0.4;
        _nextBtn.Content = _step == TotalSteps - 1 ? "完成并连接" : "下一步";

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _step switch
            {
                0 => BuildStep1(),
                1 => BuildStep2(),
                2 => BuildStep3(),
                3 => BuildStep4(),
                4 => BuildStep5(),
                _ => new Border(),
            }
        };
        _contentHost.Child = scroll;
    }

    // -------- Step 1: 欢迎 --------
    private static Panel BuildStep1()
    {
        var col = new StackPanel();
        col.Children.Add(new Border
        {
            Width = 72, Height = 72, CornerRadius = new CornerRadius(36),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0x63, 0x66, 0xF1)),
            Margin = new Thickness(0, 4, 0, 20),
            Child = new TextBlock
            {
                Text = "🔗", FontSize = 30,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        });
        col.Children.Add(FeatureCard("📋", "跨设备剪贴板", "电脑复制的文字和图片，手机上直接粘贴，反之亦然"));
        col.Children.Add(FeatureCard("💬", "短信验证码直达", "手机收到短信验证码，电脑立刻弹窗显示，一键复制"));
        col.Children.Add(FeatureCard("🔒", "端到端加密", "所有消息经 AES-256-GCM 加密，服务端仅转发密文"));
        col.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 24, 0, 0),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            TextWrapping = TextWrapping.Wrap,
            Text = "点击「下一步」开始配置。请准备好服务器地址和由管理员创建的账号密码。",
        });
        return col;
    }

    private static Border FeatureCard(string icon, string title, string desc)
    {
        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xF9, 0xFA, 0xFB)),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0x63, 0x66, 0xF1)),
            Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock
            {
                Text = icon, FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        });
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock
        {
            Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
        });
        texts.Children.Add(new TextBlock
        {
            Text = desc, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Margin = new Thickness(0, 2, 0, 0),
        });
        row.Children.Add(texts);
        card.Child = row;
        return card;
    }

    // -------- Step 2: 服务器 + 账密 --------
    private Panel BuildStep2()
    {
        var col = new StackPanel();
        col.Children.Add(Heading("连接到服务器", "填写管理员提供的服务器地址和账号"));

        col.Children.Add(Label("🌐 服务器地址"));
        _s2Server = new TextBox
        {
            Height = 36, Text = _s.ServerUrl, FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 0, 12, 0),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1),
        };
        _s2Server.ToolTip = "例如 192.168.1.10:8080 或 clipsync.example.com";
        var srvHint = new TextBlock
        {
            Margin = new Thickness(0, 2, 0, 12), FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
        };
        void UpdateSrvHint(object? s, TextChangedEventArgs? _)
        {
            var n = ServerAddress.Normalize(_s2Server!.Text);
            srvHint.Text = string.IsNullOrEmpty(n) ? "请填写服务器地址" : $"将连接 {n}";
        }
        _s2Server.TextChanged += UpdateSrvHint;
        UpdateSrvHint(null, null!);
        col.Children.Add(_s2Server);
        col.Children.Add(srvHint);

        col.Children.Add(Label("👤 用户名"));
        _s2User = new TextBox
        {
            Height = 36, Text = _s.Username, FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 0, 12, 0),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12),
        };
        col.Children.Add(_s2User);

        col.Children.Add(Label("🔒 密码"));
        _s2Pwd = new PasswordBox
        {
            Height = 36, Password = _s.Password, FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 0, 12, 0),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1),
            PasswordChar = '•',
        };
        col.Children.Add(_s2Pwd);

        var testBtn = new Button
        {
            Content = "测试连接",
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(14, 7, 14, 7),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xF3, 0xF4, 0xF6)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1)),
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Template = BtnTpl(),
        };
        testBtn.Click += async (_, _) => await TestConnect();
        col.Children.Add(testBtn);

        _s2Msg = new TextBlock
        {
            FontSize = 12, Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
        };
        col.Children.Add(_s2Msg);

        return col;
    }

    private async Task TestConnect()
    {
        if (_s2Server is not null) _s.ServerUrl = _s2Server.Text;
        if (_s2User is not null) _s.Username = _s2User.Text;
        if (_s2Pwd is not null) _s.Password = _s2Pwd.Password;
        S2Clear();

        var norm = ServerAddress.Normalize(_s.ServerUrl);
        if (norm.Length == 0) { S2Err("请先填写服务器地址"); return; }
        if (!_s.HasCredentials) { S2Err("请先填写用户名和密码"); return; }
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var sess = await AuthClient.Shared.LoginAsync(norm, _s.Username, _s.Password, cts.Token);
            _s.Token = sess.Token;
            S2Ok($"✅ 连接成功！当前在线 {sess.OnlineDevices} 台设备");
        }
        catch (Exception ex)
        {
            S2Err(WSClient.DescribeLoginFailure(ex));
        }
    }

    private void S2Err(string msg)
    {
        if (_s2Msg is null) return;
        _s2Msg.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        _s2Msg.Text = "⚠️ " + msg;
        _s2Msg.Visibility = Visibility.Visible;
    }
    private void S2Ok(string msg)
    {
        if (_s2Msg is null) return;
        _s2Msg.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
        _s2Msg.Text = msg;
        _s2Msg.Visibility = Visibility.Visible;
    }
    private void S2Clear()
    {
        if (_s2Msg is null) return;
        _s2Msg.Visibility = Visibility.Collapsed;
        _s2Msg.Text = "";
    }

    // -------- Step 3: 同步密码 --------
    private Panel BuildStep3()
    {
        var col = new StackPanel();
        col.Children.Add(Heading("端到端加密设置", "同步密码只保存在本机，服务端无法读取"));

        _s3E2ee = new CheckBox
        {
            IsChecked = _s.E2eeEnabled,
            Margin = new Thickness(0, 0, 0, 12),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = "🛡️ ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,6,0) },
                    new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = "启用端到端加密", FontSize = 14, FontWeight = FontWeights.SemiBold },
                            new TextBlock { Text = "关闭时消息将以明文传输", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x6B,0x72,0x80)), Margin = new Thickness(0,2,0,0) },
                        },
                    },
                },
            },
        };
        col.Children.Add(_s3E2ee);

        var pwdBox = new StackPanel();
        pwdBox.SetBinding(VisibilityProperty, new System.Windows.Data.Binding
        {
            Source = _s3E2ee,
            Path = new PropertyPath(nameof(CheckBox.IsChecked)),
            Converter = new BoolToVisibilityConverter(),
        });
        pwdBox.Children.Add(Label("🔑 同步密码（所有设备需填一致才能互相解密）"));
        _s3SyncPwd = new PasswordBox
        {
            Height = 36, Password = _s.SyncPassword, FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 0, 12, 0),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1),
            PasswordChar = '•',
            Margin = new Thickness(0, 4, 0, 4),
        };
        pwdBox.Children.Add(_s3SyncPwd);
        pwdBox.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Text = "留空使用内置默认密码（各端通用，强度低于自设密码）",
        });
        _s3Status = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap };
        pwdBox.Children.Add(_s3Status);

        _s3E2ee.Checked += (_, _) => S3Refresh();
        _s3E2ee.Unchecked += (_, _) => S3Refresh();
        _s3SyncPwd.PasswordChanged += (_, _) => S3Refresh();
        col.Children.Add(pwdBox);
        S3Refresh();
        return col;
    }

    private void S3Refresh()
    {
        if (_s3Status is null) return;
        var on = _s3E2ee?.IsChecked == true;
        var pwd = _s3SyncPwd?.Password ?? "";
        if (!on)
        {
            _s3Status.Text = "加密已关闭：消息以明文传输";
            _s3Status.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            return;
        }
        var effective = pwd.Length == 0 ? E2EECrypto.BuiltinSyncPassword : pwd;
        var fp = PayloadCipher.Fingerprint(effective);
        if (pwd.Length == 0)
        {
            _s3Status.Text = $"⚠️ 未填同步密码，正在使用内置默认密码\n密钥指纹：{fp}";
            _s3Status.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
        }
        else
        {
            _s3Status.Text = $"✅ 自定义同步密码已启用\n密钥指纹：{fp}";
            _s3Status.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
        }
    }

    // -------- Step 4: 偏好设置 --------
    private Panel BuildStep4()
    {
        var col = new StackPanel();
        col.Children.Add(Heading("偏好设置", "这些选项以后在主界面可以随时修改"));
        _s4AutoStart = PrefCB("🚀", "开机自动启动", "登录 Windows 后 ClipSync 在后台自动运行", _s.AutoStart);
        _s4AutoClip = PrefCB("📋", "自动同步剪贴板", "电脑复制的内容自动推送到其他设备", _s.AutoSyncClipboard);
        _s4Show = PrefCB("🔔", "弹窗显示消息内容", "关闭时 Toast 只显示「有新消息」占位", _s.ShowContent);
        _s4Tray = PrefCB("📥", "关闭时收进托盘", "关闭窗口不退出，在系统托盘继续运行", _s.MinimizeToTrayOnClose);
        col.Children.Add(_s4AutoStart);
        col.Children.Add(Divider());
        col.Children.Add(_s4AutoClip);
        col.Children.Add(Divider());
        col.Children.Add(_s4Show);
        col.Children.Add(Divider());
        col.Children.Add(_s4Tray);
        return col;
    }

    // -------- Step 5: 完成 --------
    private Panel BuildStep5()
    {
        var col = new StackPanel();
        col.Children.Add(new Border
        {
            Width = 72, Height = 72, CornerRadius = new CornerRadius(36),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0x10, 0xB9, 0x81)),
            Margin = new Thickness(0, 4, 0, 20),
            Child = new TextBlock
            {
                Text = "🎉", FontSize = 30,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        });
        col.Children.Add(new TextBlock
        {
            Text = "配置完成！",
            FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
        });
        col.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 20),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            TextWrapping = TextWrapping.Wrap,
            Text = "点击「完成并连接」，ClipSync 将立即尝试连接服务器并开始同步。",
        });
        col.Children.Add(Summary("🌐 服务器", ServerAddress.Normalize(_s.ServerUrl)));
        col.Children.Add(Summary("👤 用户名", _s.Username));
        col.Children.Add(Summary("🛡️ 端到端加密",
            _s.E2eeEnabled
                ? (_s.UsingBuiltinSyncPassword ? "已启用（内置密码）" : "已启用（自定义密码）")
                : "已关闭"));
        col.Children.Add(Summary("🚀 开机自启", _s.AutoStart ? "已开启" : "未开启"));
        col.Children.Add(Summary("📋 自动同步剪贴板", _s.AutoSyncClipboard ? "已开启" : "未开启"));
        return col;
    }

    // ============================================================
    // UI 组件
    // ============================================================
    private static Panel Heading(string title, string desc)
    {
        var s = new StackPanel { Margin = new Thickness(0, 4, 0, 16) };
        s.Children.Add(new TextBlock
        {
            Text = title, FontSize = 18, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
        });
        s.Children.Add(new TextBlock
        {
            Text = desc, FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Margin = new Thickness(0, 3, 0, 0),
        });
        return s;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
        Margin = new Thickness(0, 0, 0, 6),
    };

    private static CheckBox PrefCB(string icon, string title, string desc, bool initial)
    {
        return new CheckBox
        {
            IsChecked = initial,
            Margin = new Thickness(0, 8, 0, 8),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = icon, Width = 20, Margin = new Thickness(0,0,8,0), VerticalAlignment = VerticalAlignment.Center },
                    new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.Medium },
                            new TextBlock { Text = desc, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x6B,0x72,0x80)), Margin = new Thickness(0,2,0,0), TextWrapping = TextWrapping.Wrap },
                        },
                    },
                },
            },
        };
    }

    private static Border Divider() => new()
    {
        Height = 1,
        Background = new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0x00, 0x00)),
    };

    private static Border Summary(string label, string value)
    {
        var b = new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xF9, 0xFA, 0xFB)),
        };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock
        {
            Text = label, FontSize = 12, Width = 120,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var v = new TextBlock
        {
            Text = string.IsNullOrEmpty(value) ? "（未设置）" : value,
            FontSize = 12, FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(l, 0);
        Grid.SetColumn(v, 1);
        g.Children.Add(l);
        g.Children.Add(v);
        b.Child = g;
        return b;
    }

    private static Button MakeBtn(string text, bool secondary, bool disabled = false)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(18, 8, 18, 8),
            FontSize = 13,
            FontWeight = secondary ? FontWeights.Regular : FontWeights.SemiBold,
            Background = secondary
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0xF3, 0xF4, 0xF6))
                : new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1)),
            Foreground = secondary ? Brushes.Black : Brushes.White,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(secondary ? 0 : 10, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            IsEnabled = !disabled,
            Template = BtnTpl(),
            Opacity = disabled ? 0.4 : 1.0,
        };
        return b;
    }

    private static ControlTemplate BtnTpl()
    {
        // 【最稳妥写法】Template Trigger 不用 TargetName，避免 FrameworkElementFactory 的 NameScope 坑：
        //  hover/press 时把 Button 自己的 Opacity 拉低，与"只改 Border 的 Opacity"视觉效果等价，
        //  因为整个按钮的视觉就是这个 bd + ContentPresenter（无其它装饰）。
        var f = new FrameworkElementFactory(typeof(Border));
        f.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        f.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        f.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        f.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        f.AppendChild(cp);
        var t = new ControlTemplate(typeof(Button)) { VisualTree = f };

        // 注意：以下 Setter **没有 TargetName**，直接改 TemplatedParent（即 Button）的 Opacity
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = 0.88 });
        t.Triggers.Add(hover);
        var press = new Trigger { Property = Button.IsPressedProperty, Value = true };
        press.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = 0.75 });
        t.Triggers.Add(press);

        return t;
    }
}
