using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ClipSync.App.UI;

// ============================================================
// AppDialog —— 与应用整体风格一致的自定义对话框
//
// 取代 System.Windows.MessageBox，统一圆角/阴影/主色按钮风格。
// 支持：
//   1) Alert   ：单按钮提示（信息/成功/警告/错误）
//   2) Confirm ：双按钮确认（取消 / 主操作）
//   3) Input   ：带文本框的输入弹窗（重命名等）
//
// 所有方法都是模态（ShowDialog），返回 bool 表示是否确认；
// Input 弹窗还会把用户输入通过 out 参数带回。
// ============================================================
public enum DialogIcon
{
    Info,
    Success,
    Warning,
    Error,
    Question,
}

public sealed class AppDialog : Window
{
    private readonly TextBlock _messageText;
    private readonly TextBox? _inputBox;
    private readonly TextBlock _errorText;
    private readonly Button _primaryButton;
    private readonly Button _cancelButton;
    private readonly Func<string?, string?>? _validator;

    private AppDialog(
        string title,
        string message,
        string primaryText,
        string? cancelText,
        DialogIcon icon,
        bool showInput,
        string? initialValue,
        int maxLength,
        bool selectAll,
        Func<string?, string?>? validator)
    {
        _validator = validator;

        // ---- 窗口自身 ----
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Title = title;
        Owner = System.Windows.Application.Current?.MainWindow;

        // 按 Esc 关闭
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => Cancel()),
            new KeyGesture(Key.Escape)));

        // ---- 外层卡片（圆角 + 阴影）----
        var card = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0x00, 0x00)),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.18,
                BlurRadius = 24,
                ShadowDepth = 0,
            },
            Padding = new Thickness(22, 20, 22, 18),
        };

        var root = new StackPanel();

        // ---- 标题行（图标 + 标题）----
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconText = new TextBlock
        {
            Text = IconGlyph(icon),
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(iconText, 0);
        header.Children.Add(iconText);

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = AppColors.Gray900Brush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(titleText, 1);
        header.Children.Add(titleText);
        root.Children.Add(header);

        // ---- 消息正文 ----
        _messageText = new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = AppColors.Gray600Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 14),
            LineHeight = 20,
        };
        root.Children.Add(_messageText);

        // ---- 输入框（可选）----
        if (showInput)
        {
            _inputBox = new TextBox
            {
                Height = 36,
                FontSize = 13,
                Text = initialValue ?? "",
                MaxLength = maxLength,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 0, 10, 0),
                Background = Brushes.White,
                Foreground = AppColors.Gray900Brush,
                CaretBrush = AppColors.Gray900Brush,
                BorderBrush = AppColors.InputBorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8),
            };
            if (selectAll)
            {
                FocusBehavior.SetSelectAllOnFocus(_inputBox, true);
            }
            else
            {
                FocusBehavior.SetCaretAtEndOnFocus(_inputBox, true);
            }
            root.Children.Add(_inputBox);

            // Enter 直接确认
            _inputBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    TryConfirm();
                }
            };
        }

        // ---- 错误提示 ----
        _errorText = new TextBlock
        {
            FontSize = 12,
            Foreground = AppColors.DangerBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
            Visibility = Visibility.Collapsed,
        };
        root.Children.Add(_errorText);

        // ---- 按钮区 ----
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var primaryColor = icon switch
        {
            DialogIcon.Error => AppColors.DangerBrush,
            DialogIcon.Success => AppColors.SuccessDarkBrush,
            DialogIcon.Warning => AppColors.WarningBrush,
            _ => AppColors.PrimaryBrush,
        };

        _primaryButton = MakeButton(primaryText, primaryColor, isPrimary: true);
        _primaryButton.Click += (_, _) => TryConfirm();

        if (cancelText is not null)
        {
            _cancelButton = MakeButton(cancelText, AppColors.Gray100Brush, isPrimary: false);
            _cancelButton.Foreground = AppColors.Gray700Brush;
            _cancelButton.Click += (_, _) => Cancel();
            buttonRow.Children.Add(_cancelButton);
        }
        else
        {
            _cancelButton = null!;
        }
        buttonRow.Children.Add(_primaryButton);

        root.Children.Add(buttonRow);
        card.Child = root;

        // 让点击卡片以外区域不响应（卡片即为窗口内容）
        Content = card;

        // 回车触发主按钮
        _primaryButton.IsDefault = true;
        if (cancelText is not null) _cancelButton.IsCancel = true;

        Loaded += (_, _) =>
        {
            if (_inputBox is not null)
            {
                _inputBox.Focus();
                Keyboard.Focus(_inputBox);
            }
            else
            {
                _primaryButton.Focus();
            }
        };
    }

    // ---------------- 公共静态 API ----------------

    public static void Alert(
        string message,
        string title = "提示",
        DialogIcon icon = DialogIcon.Info,
        string okText = "我知道了")
    {
        var dlg = new AppDialog(
            title, message, okText, null, icon,
            showInput: false, null, 0, false, null);
        dlg.ShowDialog();
    }

    public static bool Confirm(
        string message,
        string title = "确认操作",
        string okText = "确定",
        string cancelText = "取消",
        DialogIcon icon = DialogIcon.Question)
    {
        var dlg = new AppDialog(
            title, message, okText, cancelText, icon,
            showInput: false, null, 0, false, null);
        return dlg.ShowDialog() == true;
    }

    public static bool Input(
        string message,
        out string value,
        string title = "请输入",
        string initialValue = "",
        string okText = "保存",
        string cancelText = "取消",
        int maxLength = 64,
        bool selectAllOnFocus = true,
        Func<string?, string?>? validator = null)
    {
        var dlg = new AppDialog(
            title, message, okText, cancelText, DialogIcon.Question,
            showInput: true, initialValue, maxLength, selectAllOnFocus, validator);
        var ok = dlg.ShowDialog() == true;
        value = dlg._inputBox?.Text ?? "";
        return ok;
    }

    // ---------------- 内部逻辑 ----------------

    private void TryConfirm()
    {
        if (_inputBox is not null && _validator is not null)
        {
            var err = _validator(_inputBox.Text);
            if (!string.IsNullOrEmpty(err))
            {
                _errorText.Text = err;
                _errorText.Visibility = Visibility.Visible;
                _inputBox.Focus();
                return;
            }
        }
        DialogResult = true;
    }

    private void Cancel() => DialogResult = false;

    private static string IconGlyph(DialogIcon icon) => icon switch
    {
        DialogIcon.Success => "✅",
        DialogIcon.Warning => "⚠️",
        DialogIcon.Error => "⛔",
        DialogIcon.Question => "❓",
        _ => "ℹ️",
    };

    private static Button MakeButton(string text, System.Windows.Media.Brush bg, bool isPrimary)
    {
        var hoverColor = isPrimary
            ? ModifyColor(((SolidColorBrush)bg).Color, 0.92)
            : new SolidColorBrush(AppColors.Gray200);

        var pressedColor = isPrimary
            ? ModifyColor(((SolidColorBrush)bg).Color, 0.82)
            : new SolidColorBrush(AppColors.Gray300);

        // 用 Style 设置默认背景；不要在 Button 上直接设 Background 本地值，
        // 否则 Style Trigger 的 hover/pressed 变色会被本地值压制（WPF 依赖属性优先级）。
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(BackgroundProperty, bg));
        style.Setters.Add(new Setter(ForegroundProperty,
            isPrimary ? Brushes.White : AppColors.Gray700Brush));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(CursorProperty, System.Windows.Input.Cursors.Hand));

        var isMouseOverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
        isMouseOverTrigger.Setters.Add(new Setter(BackgroundProperty, hoverColor));
        style.Triggers.Add(isMouseOverTrigger);
        var isPressedTrigger = new Trigger { Property = IsPressedProperty, Value = true };
        isPressedTrigger.Setters.Add(new Setter(BackgroundProperty, pressedColor));
        style.Triggers.Add(isPressedTrigger);
        var disabledTrigger = new Trigger { Property = IsEnabledProperty, Value = false };
        disabledTrigger.Setters.Add(new Setter(OpacityProperty, 0.5));
        style.Triggers.Add(disabledTrigger);

        var btn = new Button
        {
            Content = text,
            MinWidth = 84,
            Height = 34,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(isPrimary ? 8 : 0, 0, 0, 0),
            FontSize = 13,
            FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Medium,
            IsTabStop = true,
            Style = style,
            Template = new ControlTemplate(typeof(Button))
            {
                VisualTree = BuildButtonVisualTree(),
            },
        };

        return btn;
    }

    private static FrameworkElementFactory BuildButtonVisualTree()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(ForegroundProperty));
        presenter.SetValue(TextElement.FontSizeProperty, new TemplateBindingExtension(Control.FontSizeProperty));
        presenter.SetValue(TextElement.FontWeightProperty, new TemplateBindingExtension(Control.FontWeightProperty));
        border.AppendChild(presenter);
        return border;
    }

    private static SolidColorBrush ModifyColor(Color c, double factor)
    {
        byte Mix(byte channel) => (byte)Math.Clamp((int)(channel * factor), 0, 255);
        return new SolidColorBrush(Color.FromArgb(c.A, Mix(c.R), Mix(c.G), Mix(c.B)));
    }
}
