using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace ClipSync.App.UI;

using ClipSync.App.Services;
using ClipSync.Core.Protocol;
using ClipSync.Core.Storage;

// ============================================================
// ToastWindow：屏幕右上角的通知横幅
// - 短信类 + 提取到验证码 → 显示「复制 xxx」和「全文」两个按钮
// - 剪贴板 / 其他 → 显示单个「复制」按钮
// - 淡入淡出动画（对齐 Mac 端 ToastView）
// - 点击按钮时不激活主窗口（避免用户正在输验证码时被打断）
// - ShowActivated = false + Topmost = true，保证只是飘在屏幕上不抢焦点
// ============================================================
public partial class ToastWindow : Window
{
    private readonly SyncMessage _msg;
    private readonly bool _showContent;
    private readonly string? _extractedCode;

    public event Action? ClosedByUser;

    public ToastWindow(SyncMessage msg)
    {
        InitializeComponent();
        _msg = msg;
        _showContent = SettingsStore.Shared.ShowContent;
        _extractedCode = ExtractCode(msg);

        // WPF 无边框 + 不抢焦点
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        Opacity = 0;
        // ESC 关闭：弹窗不抢焦点，但挂一个 PreviewKeyDown 在 Window 上，
        // 配合 ToastManager 装的全局 Keyboard.PreviewKeyDown 也能关掉最顶层 Toast。
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                ClosedByUser?.Invoke();
                FadeOutAndClose();
                e.Handled = true;
            }
        };

        Content = BuildContent();
        Loaded += OnLoaded;
    }

    // === 加载后：摆位置 + 淡入 + 5 秒自动关闭 ===
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var screenWidth = SystemParameters.WorkArea.Width;
        var screenHeight = SystemParameters.WorkArea.Height;
        // 屏幕右上角，距边各 24 像素（Mac 端同样定位）
        Left = screenWidth - Width - 24;
        Top = 24;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(OpacityProperty, fadeIn);

        // 5 秒后自动淡出关闭
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            FadeOutAndClose();
        };
        timer.Start();
    }

    public void FadeOutAndClose()
    {
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    // === 构建内容（代码而非 XAML，方便精确对齐 Mac 端 ToastView） ===
    private FrameworkElement BuildContent()
    {
        // 根容器：圆角卡片 + 阴影
        var root = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0x00, 0x00, 0x00)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.2,
                BlurRadius = 24,
                ShadowDepth = 8,
            },
            Padding = new Thickness(16, 14, 16, 14),
        };

        var body = new System.Windows.Controls.StackPanel();

        // 顶部：左侧图标 + 标题区 + 关闭按钮
        var topRow = new System.Windows.Controls.Grid
        {
            Margin = new Thickness(0, 0, 0, 4),
        };
        topRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
        { Width = new GridLength(40) });
        topRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
        { Width = new GridLength(1, GridUnitType.Star) });

        var icon = BuildIcon();
        System.Windows.Controls.Grid.SetColumn(icon, 0);
        topRow.Children.Add(icon);

        var titleBlock = BuildTitleBlock();
        System.Windows.Controls.Grid.SetColumn(titleBlock, 1);
        topRow.Children.Add(titleBlock);

        body.Children.Add(topRow);

        // 图片消息单独一行
        if (IsImageMsg)
        {
            var img = BuildImage();
            if (img is not null)
            {
                img.Margin = new Thickness(0, 10, 0, 0);
                body.Children.Add(img);
            }
        }

        // 正文
        if (!IsImageMsg)
        {
            var text = new System.Windows.Controls.TextBlock
            {
                Text = BodyText,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 0x11, 0x18, 0x27)),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(40, 0, 0, 6),
            };
            body.Children.Add(text);
        }

        // 按钮行
        var actions = BuildActionRow();
        actions.Margin = new Thickness(40, 2, 0, 0);
        body.Children.Add(actions);

        root.Child = body;
        return root;
    }

    private FrameworkElement BuildIcon()
    {
        // 主图标：紫色渐变底 + 类型角标（跟 Mac 一致）
        var outer = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromRgb(0x81, 0x7C, 0xF4), 0),
                    new(Color.FromRgb(0x4F, 0x46, 0xE5), 1),
                },
            },
            Child = new System.Windows.Controls.Viewbox
            {
                Stretch = Stretch.Uniform,
                Width = 18,
                Height = 18,
                Child = CreateGlyphIcon(),
            },
        };
        return outer;
    }

    /// <summary>用 Path 画一个简单的"聊天气泡"字形图标。</summary>
    private static FrameworkElement CreateGlyphIcon()
    {
        var path = new System.Windows.Shapes.Path
        {
            Fill = Brushes.White,
            Data = Geometry.Parse("M2 2h10a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H7l-3 3V4a2 2 0 0 1 2-2z"),
        };
        return path;
    }

    private FrameworkElement BuildTitleBlock()
    {
        var stack = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var title = new System.Windows.Controls.TextBlock
        {
            Text = TitleText,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(title);

        if (ExtractedPhone is { } phone)
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(999),
                Background = new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0x00, 0x00)),
                Padding = new Thickness(6, 1, 6, 2),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = phone,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                },
            };
            stack.Children.Add(pill);
        }

        stack.Children.Add(new System.Windows.Controls.Border
        {
            Width = 1,
            Background = Brushes.Transparent,
        });

        // 时间 + 关闭按钮靠右
        var right = new System.Windows.Controls.Grid
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 0, 0),
        };

        var row = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var time = new System.Windows.Controls.TextBlock
        {
            Text = TimeStr,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        row.Children.Add(time);

        var close = new System.Windows.Controls.Button
        {
            Content = "✕",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Width = 16,
            Height = 16,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0x00, 0x00)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Template = MakeCloseButtonTemplate(),
        };
        close.Click += (_, _) =>
        {
            ClosedByUser?.Invoke();
            FadeOutAndClose();
        };
        row.Children.Add(close);

        // 让时间+关闭按钮自动占右侧空间
        var filler = new System.Windows.Controls.Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        filler.Children.Add(row);

        var grid = new System.Windows.Controls.Grid();
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(stack);
        grid.Children.Add(filler);
        return grid;
    }

    private static System.Windows.Controls.ControlTemplate MakeCloseButtonTemplate()
    {
        // 修复：去掉 TargetName="border"，避免 FrameworkElementFactory NameScope 在 Seal 时崩溃。
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(999));
        factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
        var presenter = new FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
        presenter.SetValue(
            System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty,
            HorizontalAlignment.Center);
        presenter.SetValue(
            System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty,
            VerticalAlignment.Center);
        factory.AppendChild(presenter);

        var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button))
        {
            VisualTree = factory,
        };
        var hoverTrigger = new Trigger
        {
            Property = System.Windows.UIElement.IsMouseOverProperty,
            Value = true,
        };
        // 直接改 Button（TemplatedParent）的 Background；Border 用 TemplateBinding 会自动跟随。
        hoverTrigger.Setters.Add(new Setter
        {
            Property = System.Windows.Controls.Control.BackgroundProperty,
            Value = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x00, 0x00)),
        });
        template.Triggers.Add(hoverTrigger);
        return template;
    }

    private const double ThumbWidth = 280;
    private const double ThumbHeight = 158;

    /// <summary>解码 base64 图片为 BitmapSource，失败返回 null。</summary>
    private System.Windows.Media.Imaging.BitmapSource? DecodeImage()
    {
        if (!IsImageMsg || string.IsNullOrEmpty(_msg.Payload.Data)) return null;
        try
        {
            var bytes = Convert.FromBase64String(_msg.Payload.Data);
            using var ms = new System.IO.MemoryStream(bytes);
            var bmp = System.Windows.Media.Imaging.BitmapFrame.Create(
                ms,
                System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private FrameworkElement? BuildImage()
    {
        var bmp = DecodeImage();
        if (bmp is null) return null;

        // 固定 280×158 卡片，图片等比缩放 letterbox 居中。
        // 卡片尺寸稳定，Toast 宽度也就稳定了。
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(10),
            Width = ThumbWidth,
            Height = ThumbHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new System.Windows.Controls.Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(10),
                Cursor = System.Windows.Input.Cursors.Hand,
            },
        };
        // 点击卡片 → 预览大图（先关 Toast 再开预览）
        card.MouseLeftButtonUp += (_, _) =>
        {
            ClosedByUser?.Invoke();
            FadeOutAndClose();
            ImagePreviewWindow.Show(bmp);
        };
        card.ToolTip = "点击预览大图（Esc 关闭）";
        return card;
    }

    private FrameworkElement BuildActionRow()
    {
        var row = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
        };

        // 主按钮样式（圆角矩形 8px）
        static System.Windows.Controls.Button MakePill(string text, bool primary, Action<object, RoutedEventArgs> onClick)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = text,
                FontSize = 12,
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Medium,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = primary
                    ? new SolidColorBrush(Color.FromRgb(0x4F, 0x46, 0xE5))
                    : new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
                Foreground = primary
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
                BorderThickness = new Thickness(0),
                MinHeight = 30,
                // 按内容自适应宽度，避免长文案（如"复制 888101"）被截断
                HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                UseLayoutRounding = true,
            };
            btn.Click += new RoutedEventHandler(onClick);
            btn.Template = RoundedButtonTemplate();
            return btn;
        }

        if (_extractedCode is not null)
        {
            var copyCode = MakePill($"复制 {_extractedCode}", true, (_, _) =>
            {
                ClipboardWriter.CopyText(_extractedCode);
                FadeOutAndClose();
            });
            var copyAll = MakePill("全文", false, (_, _) =>
            {
                ClipboardWriter.Apply(_msg.Payload);
                FadeOutAndClose();
            });
            row.Children.Add(copyCode);
            row.Children.Add(copyAll);
        }
        else if (IsImageMsg)
        {
            // 图片消息：复制 + 预览；复制后不自动关，让用户可以接着点预览
            var copy = MakePill("复制", true, (_, _) =>
            {
                ClipboardWriter.Apply(_msg.Payload);
            });
            var preview = MakePill("预览", false, (_, _) =>
            {
                if (DecodeImage() is { } bmp)
                {
                    ClosedByUser?.Invoke();
                    FadeOutAndClose();
                    ImagePreviewWindow.Show(bmp);
                }
            });
            row.Children.Add(copy);
            row.Children.Add(preview);
        }
        else
        {
            var copyAll = MakePill("复制", true, (_, _) =>
            {
                ClipboardWriter.Apply(_msg.Payload);
                FadeOutAndClose();
            });
            row.Children.Add(copyAll);
        }

        return row;
    }

    private static System.Windows.Controls.ControlTemplate RoundedButtonTemplate()
    {
        // 标准圆角按钮模板：
        // - 根 Border 自动 Stretch 填满 Button 可视区域
        // - 绑定 Background/Padding 到 Button 的同名属性
        // - ContentPresenter 绑定 HorizontalContentAlignment/VerticalContentAlignment
        //   让 Button 上设置的内容对齐生效
        // 不要在根 Border 上绑定 HorizontalAlignment/VerticalAlignment，
        // 那会导致模板根元素不填充、按钮测量异常。
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        factory.SetValue(Border.BackgroundProperty,
            new TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
        factory.SetValue(Border.PaddingProperty,
            new TemplateBindingExtension(System.Windows.Controls.Control.PaddingProperty));
        factory.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);

        var presenter = new FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
        presenter.SetValue(
            System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty,
            new TemplateBindingExtension(System.Windows.Controls.Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(
            System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty,
            new TemplateBindingExtension(System.Windows.Controls.Control.VerticalContentAlignmentProperty));
        presenter.SetValue(
            System.Windows.Controls.ContentPresenter.RecognizesAccessKeyProperty, true);
        presenter.SetValue(
            System.Windows.Controls.ContentPresenter.MarginProperty,
            new System.Windows.Thickness(0));
        factory.AppendChild(presenter);

        var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button))
        {
            VisualTree = factory,
        };
        var hover = new Trigger
        {
            Property = System.Windows.UIElement.IsMouseOverProperty, Value = true,
        };
        hover.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = 0.88 });
        template.Triggers.Add(hover);
        var pressed = new Trigger
        {
            Property = System.Windows.Controls.Button.IsPressedProperty, Value = true,
        };
        pressed.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = 0.75 });
        template.Triggers.Add(pressed);
        return template;
    }

    private static System.Windows.Controls.ControlTemplate PillButtonTemplate()
    {
        // 修复：去掉 TargetName="bd"，避免 FrameworkElementFactory NameScope 在 Seal 时崩溃。
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(999));
        factory.SetValue(Border.BackgroundProperty,
            new TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
        factory.SetValue(Border.PaddingProperty,
            new TemplateBindingExtension(System.Windows.Controls.Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
        presenter.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty,
            HorizontalAlignment.Center);
        presenter.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty,
            VerticalAlignment.Center);
        factory.AppendChild(presenter);

        var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button))
        {
            VisualTree = factory,
        };
        var hover = new Trigger
        {
            Property = System.Windows.UIElement.IsMouseOverProperty, Value = true,
        };
        hover.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = 0.9 });
        template.Triggers.Add(hover);
        var pressed = new Trigger
        {
            Property = System.Windows.Controls.Button.IsPressedProperty, Value = true,
        };
        pressed.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = 0.75 });
        template.Triggers.Add(pressed);
        return template;
    }

    // === 派生属性 ===
    private bool IsImageMsg =>
        _msg.Content == MessageContent.Image && !string.IsNullOrEmpty(_msg.Payload.Data);

    private string TitleText
    {
        get
        {
            if (LooksLikeSms) return "短信验证码";
            return _msg.Kind switch
            {
                MessageKind.Image => "剪贴板图片",
                MessageKind.Share => "分享",
                _ => _msg.Type == MessageType.Clipboard ? "剪贴板" : "通知",
            };
        }
    }

    private bool LooksLikeSms
    {
        get
        {
            if (_msg.IsSms) return true;
            var raw = _msg.Payload.Text ?? _msg.Payload.Preview ?? "";
            return SmsPayloadSanitizer.HasSmsMarkers(raw);
        }
    }

    private string? ExtractedPhone
    {
        get
        {
            if (!LooksLikeSms) return null;
            if (!string.IsNullOrEmpty(_msg.Payload.Sender))
                return SmsPayloadSanitizer.Sanitize("", _msg.Payload.Sender).Sender;
            var raw = _msg.Payload.Text ?? _msg.Payload.Preview ?? "";
            return SmsPayloadSanitizer.Sanitize(raw, null).Sender;
        }
    }

    private string BodyText
    {
        get
        {
            if (!_showContent) return "收到一条新消息";
            var raw = (string.IsNullOrEmpty(_msg.Payload.Text) ? _msg.Payload.Preview : _msg.Payload.Text) ?? "";
            if (_msg.Payload.Mime?.StartsWith("image/") == true) return "[图片]";
            var cleaned = LooksLikeSms
                ? SmsPayloadSanitizer.Sanitize(raw, _msg.Payload.Sender).Text
                : raw;
            return TruncateTail(cleaned, 80);
        }
    }

    private static string? ExtractCode(SyncMessage msg)
    {
        if (msg.Category != MessageCategory.Sms
            && !SmsPayloadSanitizer.HasSmsMarkers(msg.Payload.Text ?? msg.Payload.Preview ?? ""))
            return null;
        var text = msg.Payload.Text ?? msg.Payload.Preview;
        if (string.IsNullOrEmpty(text)) return null;
        return SmsCodeExtractor.Extract(text);
    }

    private string TimeStr => _msg.Date.ToString("HH:mm");

    private static string TruncateTail(string s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= maxChars ? s : s[..maxChars] + "…";
    }
}
