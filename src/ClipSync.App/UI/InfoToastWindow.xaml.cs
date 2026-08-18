using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace ClipSync.App.UI;

// ============================================================
// InfoToastWindow：设备上下线等状态提示的轻量横幅
// - 只有图标 + 标题 + 正文 + 关闭，不绑业务消息
// - 不抢焦点、自动 5 秒淡出，定位与 ToastWindow 一致（右上角）
// ============================================================
public sealed class InfoToastWindow : Window
{
    public event Action? ClosedByUser;

    private readonly string _title;
    private readonly string _body;
    private readonly bool _online;

    public InfoToastWindow(string title, string body, bool online)
    {
        _title = title;
        _body = body;
        _online = online;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        Opacity = 0;

        Content = BuildContent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.WorkArea.Width - Width - 24;
        Top = 24;

        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { timer.Stop(); FadeOutAndClose(); };
        timer.Start();
    }

    public void FadeOutAndClose()
    {
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    private FrameworkElement BuildContent()
    {
        var accent = _online
            ? Color.FromRgb(0x10, 0xB9, 0x81)
            : Color.FromRgb(0x6B, 0x72, 0x80);
        var glyph = _online ? "M5 12l5 5L20 7" : "M6 6l12 12M18 6L6 18"; // 对勾 / 叉

        var root = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0, 0, 0)),
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

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B)),
            Child = new Viewbox
            {
                Width = 18,
                Height = 18,
                Child = new Path
                {
                    Data = Geometry.Parse(glyph),
                    Stroke = new SolidColorBrush(accent),
                    StrokeThickness = 2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Fill = Brushes.Transparent,
                },
            },
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var textCol = new StackPanel();
        textCol.Children.Add(new TextBlock
        {
            Text = _title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
            Margin = new Thickness(0, 0, 0, 3),
        });
        textCol.Children.Add(new TextBlock
        {
            Text = _body,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 0x11, 0x18, 0x27)),
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(textCol, 1);
        row.Children.Add(textCol);

        root.Child = row;
        return root;
    }
}
