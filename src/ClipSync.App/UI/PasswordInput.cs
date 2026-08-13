using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClipSync.App.UI;

/// <summary>
/// 带「小眼睛」和「复制」按钮的密码输入框。
/// - 眼睛按钮：切换密码明文/密文显示
/// - 复制按钮：把当前内容写入剪贴板
/// 输入框自身绘制白底+灰边框（与服务器地址/用户名输入框样式一致，
/// 也与 OnboardingWizard 中已验证可正常显示圆点的 PasswordBox 写法完全相同）。
/// </summary>
public sealed class PasswordInput : Grid
{
    private readonly PasswordBox _passwordBox;
    private readonly TextBox _plainBox;
    private readonly Button _eyeBtn;
    private readonly Button _copyBtn;

    private bool _isRevealed;
    private bool _internalChange;

    public PasswordInput()
    {
        Height = 36;
        Background = Brushes.Transparent;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var borderBrush = AppColors.InputBorderBrush;

        // PasswordBox（密文）—— 自身带白底灰边框，与普通 TextBox 外观一致
        _passwordBox = new PasswordBox
        {
            Height = 36,
            FontSize = 13,
            PasswordChar = '•',
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 0, 8, 0),
            Background = Brushes.White,
            Foreground = AppColors.Gray900Brush,
            CaretBrush = AppColors.Gray900Brush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        SetColumn(_passwordBox, 0);
        Children.Add(_passwordBox);
        _passwordBox.PasswordChanged += OnPasswordChanged;

        // TextBox（明文），默认隐藏 —— 同样自身带边框
        _plainBox = new TextBox
        {
            Height = 36,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 0, 8, 0),
            Background = Brushes.White,
            Foreground = AppColors.Gray900Brush,
            CaretBrush = AppColors.Gray900Brush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        SetColumn(_plainBox, 0);
        Children.Add(_plainBox);
        _plainBox.TextChanged += OnPlainTextChanged;

        // 眼睛按钮
        _eyeBtn = CreateIconButton("👁", "显示密码");
        SetColumn(_eyeBtn, 1);
        Children.Add(_eyeBtn);
        _eyeBtn.Click += (_, _) => ToggleReveal();

        // 复制按钮
        _copyBtn = CreateIconButton("📋", "复制");
        SetColumn(_copyBtn, 2);
        Children.Add(_copyBtn);
        _copyBtn.Click += (_, _) => CopyToClipboard();
    }

    public string Password
    {
        get => _isRevealed ? _plainBox.Text : _passwordBox.Password;
        set
        {
            var v = value ?? "";
            if (Password == v) return;
            _internalChange = true;
            _passwordBox.Password = v;
            _plainBox.Text = v;
            _internalChange = false;
        }
    }

    public event EventHandler? PasswordChanged;

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_internalChange) return;
        _internalChange = true;
        _plainBox.Text = _passwordBox.Password;
        _internalChange = false;
        PasswordChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlainTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_internalChange) return;
        _internalChange = true;
        _passwordBox.Password = _plainBox.Text;
        _internalChange = false;
        PasswordChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleReveal()
    {
        _isRevealed = !_isRevealed;
        var text = _passwordBox.Password;
        if (_isRevealed)
        {
            _passwordBox.Visibility = Visibility.Collapsed;
            _plainBox.Visibility = Visibility.Visible;
            _plainBox.Text = text;
            _plainBox.Focus();
            _plainBox.CaretIndex = text.Length;
            _eyeBtn.Content = "🙈";
            ToolTipService.SetToolTip(_eyeBtn, "隐藏密码");
        }
        else
        {
            _plainBox.Visibility = Visibility.Collapsed;
            _passwordBox.Visibility = Visibility.Visible;
            _passwordBox.Password = text;
            _passwordBox.Focus();
            _eyeBtn.Content = "👁";
            ToolTipService.SetToolTip(_eyeBtn, "显示密码");
        }
    }

    private void CopyToClipboard()
    {
        var text = Password;
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            _copyBtn.Content = "✓";
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1200),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _copyBtn.Content = "📋";
            };
            timer.Start();
        }
        catch { }
    }

    private static Button CreateIconButton(string content, string tooltip)
    {
        var btn = new Button
        {
            Content = content,
            FontSize = 13,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 0, 4, 0),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = AppColors.Gray500Brush,
            MinWidth = 30,
            Focusable = false,
        };
        ToolTipService.SetToolTip(btn, tooltip);
        btn.MouseEnter += (_, _) => btn.Foreground = AppColors.Gray700Brush;
        btn.MouseLeave += (_, _) => btn.Foreground = AppColors.Gray500Brush;
        return btn;
    }
}
