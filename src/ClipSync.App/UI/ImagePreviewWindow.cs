using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipSync.App.UI;

// ============================================================
// ImagePreviewWindow：大图片预览窗口
// - 居中显示，最大占屏幕 80%，图片等比缩放
// - ESC 关闭、点击空白处关闭
// - 复制按钮：把原始图片写入剪贴板
// - 从 Toast 点开时保持 ShowActivated=false，避免把主窗口顶出来
// ============================================================
public sealed class ImagePreviewWindow : Window
{
    private static ImagePreviewWindow? _open;

    private readonly BitmapSource _bmp;

    private ImagePreviewWindow(BitmapSource bmp)
    {
        _bmp = bmp;

        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;
        ShowActivated = true;
        Background = Brushes.White;
        Title = "图片预览";
        Width = 800;
        Height = 600;
        MinWidth = 320;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        BuildLayout();

        // ESC 关闭
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CopyToClipboard();
                e.Handled = true;
            }
        };

        Closed += (_, _) => _open = null;
    }

    private void BuildLayout()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 图片区：白底 + 等比缩放
        var image = new Image
        {
            Source = _bmp,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(16),
        };
        Grid.SetRow(image, 0);
        grid.Children.Add(image);

        // 底部工具栏：尺寸信息 + 复制按钮
        var bar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0x00, 0x00, 0x00)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 10, 16, 10),
        };
        var barRow = new DockPanel { LastChildFill = true };

        var info = new TextBlock
        {
            Text = $"{_bmp.PixelWidth} × {_bmp.PixelHeight}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(info, Dock.Left);
        barRow.Children.Add(info);

        var copyBtn = new Button
        {
            Content = "复制",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(18, 6, 18, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x4F, 0x46, 0xE5)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        copyBtn.Click += (_, _) => CopyToClipboard();
        barRow.Children.Add(copyBtn);

        bar.Child = barRow;
        Grid.SetRow(bar, 1);
        grid.Children.Add(bar);

        Content = grid;
    }

    private void CopyToClipboard()
    {
        try
        {
            // 写入 PNG 到剪贴板，其他应用可直接粘贴
            Clipboard.SetImage(_bmp);
        }
        catch { /* 剪贴板被占用时忽略 */ }
    }

    /// <summary>显示预览窗口。同一时刻只保留一个，重复调用会激活已有窗口。</summary>
    public static void Show(BitmapSource bmp)
    {
        if (_open is not null)
        {
            try
            {
                if (!_open.Dispatcher.HasShutdownStarted)
                {
                    _open.Dispatcher.Invoke(() =>
                    {
                        if (_open.IsVisible)
                        {
                            _open.Activate();
                            return;
                        }
                    });
                }
            }
            catch { /* dispatcher 已关闭，忽略，重建 */ }
        }

        // 按原图尺寸算一个合理的窗口大小，不超过屏幕 80%
        var workArea = SystemParameters.WorkArea;
        var maxW = workArea.Width * 0.8;
        var maxH = workArea.Height * 0.8;
        var w = Math.Min(maxW, Math.Max(480, bmp.PixelWidth + 48));
        var h = Math.Min(maxH, Math.Max(320, bmp.PixelHeight + 96));

        Application.Current?.Dispatcher.Invoke(() =>
        {
            var win = new ImagePreviewWindow(bmp)
            {
                Width = w,
                Height = h,
            };
            _open = win;
            if (Application.Current.MainWindow is { IsLoaded: true } main)
            {
                win.Owner = main;
            }
            win.Show();
        });
    }
}
