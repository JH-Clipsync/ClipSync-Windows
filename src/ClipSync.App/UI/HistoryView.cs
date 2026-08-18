using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace ClipSync.App.UI;

using ClipSync.App.Services;
using ClipSync.Core.Protocol;
using ClipSync.Core.Storage;

// ============================================================
// HistoryView：短信 / 剪贴板 历史记录列表
// 与 Mac 端 HistoryView.swift 对齐：
// - 按时间倒序显示（最新在上）
// - 每一行：图标 + 类型标签 + 手机号pill(短信) + 时间 + 内容预览
// - 底部：清空按钮 + 总数显示
// ============================================================
public class HistoryView
{
    public FrameworkElement Root { get; }

    private readonly HistoryStore.Filter _filter;
    private readonly StackPanel _list;
    private readonly TextBlock _countLabel;

    public HistoryView(HistoryStore.Filter filter)
    {
        _filter = filter;

        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 标题栏
        var header = new Border
        {
            Padding = new Thickness(20, 16, 20, 8),
            Child = new Grid
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = filter == HistoryStore.Filter.Sms ? "短信" : "剪贴板",
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.Black,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    (_countLabel = new TextBlock
                    {
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                    }),
                },
            },
        };
        Grid.SetRow(header, 0);
        outer.Children.Add(header);

        // 中间：滚动列表
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(20, 0, 20, 0),
        };
        _list = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        scroll.Content = _list;
        Grid.SetRow(scroll, 1);
        outer.Children.Add(scroll);

        // 底部：清空按钮
        var footer = new Border
        {
            Padding = new Thickness(20, 12, 20, 20),
            Child = new Button
            {
                Content = $"清空{(_filter == HistoryStore.Filter.Sms ? "短信" : "剪贴板")}历史",
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(14, 6, 14, 6),
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xF3, 0xF4, 0xF6)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Medium,
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                Template = RoundCornerBtnTemplate(),
                Command = new RelayCommand(_ =>
                {
                    if (AppDialog.Confirm(
                        $"确定要清空全部{(_filter == HistoryStore.Filter.Sms ? "短信" : "剪贴板")}历史吗？此操作不可撤销。",
                        "确认清空",
                        okText: "清空",
                        cancelText: "再想想",
                        icon: DialogIcon.Warning))
                    {
                        HistoryStore.Shared.Clear(_filter);
                    }
                }),
            },
        };
        Grid.SetRow(footer, 2);
        outer.Children.Add(footer);

        Root = outer;

        // 历史变化时自动刷新
        HistoryStore.Shared.Changed += Refresh;
    }

    public void Refresh()
    {
        _list.Children.Clear();
        var list = HistoryStore.Shared.Filtered(_filter);
        if (list.Count == 0)
        {
            _countLabel.Text = "共 0 条";
            _list.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(0x0A, 0x00, 0x00, 0x00)),
                Padding = new Thickness(16),
                Child = new TextBlock
                {
                    Text = _filter == HistoryStore.Filter.Sms
                        ? "暂无短信记录\n收到验证码或短信后会显示在这里"
                        : "暂无剪贴板记录\n跨设备复制的内容会显示在这里",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                Margin = new Thickness(0, 20, 0, 0),
            });
            return;
        }

        _countLabel.Text = $"共 {list.Count} 条";
        foreach (var msg in list)
        {
            _list.Children.Add(BuildRow(msg));
            _list.Children.Add(new Border { Height = 8 });
        }
    }

    private Border BuildRow(SyncMessage msg)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.04,
                BlurRadius = 8,
                ShadowDepth = 0,
            },
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左侧图标
        bool isSms = _filter == HistoryStore.Filter.Sms || LooksLikeSms(msg);
        var iconBg = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(isSms
                ? Color.FromArgb(0x1F, 0x3B, 0x82, 0xF6)
                : Color.FromArgb(0x1F, 0x63, 0x66, 0xF1)),
            Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock
            {
                Text = isSms ? "💬" : "📋",
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(iconBg, 0);
        grid.Children.Add(iconBg);

        // 右侧内容
        var content = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = isSms ? "短信" : "剪贴板",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
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
        // 时间靠右
        var time = new TextBlock
        {
            Text = msg.Date.ToString("MM-dd HH:mm"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            Margin = new Thickness(10, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        titleRow.Children.Add(time);
        content.Children.Add(titleRow);

        // 内容预览
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
                    MaxHeight = 160,
                    CornerRadius = new CornerRadius(8),
                    ClipToBounds = true,
                    Background = new SolidColorBrush(Color.FromArgb(0x0F, 0x00, 0x00, 0x00)),
                    Padding = new Thickness(8),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Stretch = Stretch.Uniform,
                        MaxHeight = 160,
                    },
                });
            }
            catch { /* ignore */ }
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = PreviewText(msg),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0x11, 0x18, 0x27)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 72,
            });
        }

        // 操作行
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var code = ExtractedCode(msg);
        if (code is not null)
        {
            actions.Children.Add(MakePill($"复制 {code}", primary: true, _ => ClipboardWriter.CopyText(code)));
        }
        actions.Children.Add(MakePill("复制", primary: code is null, _ => ClipboardWriter.Apply(msg.Payload)));
        actions.Children.Add(MakePill("删除", primary: false, _ =>
        {
            if (AppDialog.Confirm(
                "确定删除这条记录？",
                "确认删除",
                okText: "删除",
                cancelText: "取消",
                icon: DialogIcon.Warning))
                HistoryStore.Shared.Remove(msg.Id);
        }, danger: true));
        content.Children.Add(actions);

        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        card.Child = grid;
        return card;
    }

    private static Button MakePill(string text, bool primary, Action<object?> onClick, bool danger = false)
    {
        var b = new Button
        {
            Content = text,
            FontSize = 11,
            FontWeight = primary ? FontWeights.Medium : FontWeights.Regular,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Background = primary
                ? new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1))
                : danger
                    ? new SolidColorBrush(Color.FromArgb(0xFF, 0xFE, 0x2E, 0x2E))
                    : new SolidColorBrush(Color.FromArgb(0xFF, 0xF3, 0xF4, 0xF6)),
            Foreground = primary
                ? Brushes.White
                : danger
                    ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
                    : Brushes.Black,
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
        var f = new FrameworkElementFactory(typeof(Border));
        f.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        f.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        f.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        f.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        var p = new FrameworkElementFactory(typeof(ContentPresenter));
        p.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        p.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        f.AppendChild(p);
        var t = new ControlTemplate(typeof(Button)) { VisualTree = f };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = 0.9 });
        t.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = 0.75 });
        t.Triggers.Add(pressed);
        return t;
    }

    // ============================================================
    // 派生属性（与 Mac 端一致）
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
        // 末尾手动截断（与 Mac 端一致）
        return cleaned.Length <= 300 ? cleaned : cleaned[..300] + "…";
    }
}

public class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;
    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    public event EventHandler? CanExecuteChanged
    {
        add => System.Windows.Input.CommandManager.RequerySuggested += value;
        remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
    }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
}
