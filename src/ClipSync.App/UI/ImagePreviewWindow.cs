using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipSync.Core.Storage;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfCursors = System.Windows.Input.Cursors;

namespace ClipSync.App.UI;

// ============================================================
// ImageSaver：图片另存为（弹窗 / 最近 / 历史 共用）
// - WPF 自带 SaveFileDialog，默认 PNG，可选 JPEG
// - 默认目录：上次保存位置 → 下载目录；默认文件名 ClipSync_时间戳.png
// - 保存成功后记住目录，失败弹 AppDialog
// ============================================================
public static class ImageSaver
{
    /// <summary>弹出另存为对话框保存图片。返回 true=已保存，false=取消或失败。</summary>
    public static bool SaveWithDialog(BitmapSource bmp, Window? owner = null)
    {
        var settings = SettingsStore.Shared;

        string? initialDir = settings.LastImageSaveDir;
        if (string.IsNullOrEmpty(initialDir) || !Directory.Exists(initialDir))
        {
            initialDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        var dlg = new SaveFileDialog
        {
            Title = "保存图片",
            Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg;*.jpeg)|*.jpg;*.jpeg",
            FilterIndex = 1,
            FileName = $"ClipSync_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            InitialDirectory = Directory.Exists(initialDir) ? initialDir : "",
            AddExtension = true,
            RestoreDirectory = false,
        };

        bool? ok;
        var effectiveOwner = owner;
        if (effectiveOwner is not { IsLoaded: true, IsVisible: true })
        {
            effectiveOwner = ResolveVisibleOwner();
        }
        if (effectiveOwner is not null)
        {
            ok = dlg.ShowDialog(effectiveOwner);
        }
        else
        {
            // 没有当前可见的稳定宿主时用无 owner 对话框，由系统独立管理。
            // 绝不为了挂对话框去激活/前置一个本应隐藏的主窗口，也不要让 WPF
            // 自动拿当前激活的 Toast 当 owner——Toast 一关闭对话框会被连带销毁。
            ok = dlg.ShowDialog();
        }
        if (ok != true || string.IsNullOrEmpty(dlg.FileName)) return false;
        try
        {
            EncodeToFile(bmp, dlg.FileName);
            settings.LastImageSaveDir = Path.GetDirectoryName(dlg.FileName);
            return true;
        }
        catch (Exception ex)
        {
            AppDialog.Alert($"图片保存失败：{ex.Message}", "保存失败", DialogIcon.Error);
            return false;
        }
    }

    /// <summary>
    /// 在“当前已可见”的窗口里找一个不会瞬时消失的对话框宿主：
    /// 优先 Application.MainWindow，其次任意可见的普通窗口；
    /// 显式排除 Toast/InfoToast/预览窗这些短生命周期窗口。都没有就返回 null。
    /// 注意：只选已经可见的窗口，绝不激活一个本应隐藏的主窗口，否则点保存会
    /// 把后台主窗口拽到最前。
    /// </summary>
    private static Window? ResolveVisibleOwner()
    {
        var app = Application.Current;
        if (app is null) return null;

        if (app.MainWindow is { IsLoaded: true, IsVisible: true } main && !IsTransient(main)) return main;

        return app.Windows
            .Cast<Window>()
            .FirstOrDefault(w => w.IsLoaded && w.IsVisible && !IsTransient(w));
    }

    private static bool IsTransient(Window w) =>
        w is ToastWindow or InfoToastWindow or ImagePreviewWindow;

    private static void EncodeToFile(BitmapSource src, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        bool asJpeg = ext is ".jpg" or ".jpeg";

        BitmapEncoder encoder;
        if (asJpeg)
        {
            // JPEG 不支持透明通道：统一转 Bgr24，否则带 alpha 的 PNG 帧编码会抛异常
            var converted = src.Format == PixelFormats.Bgr24
                ? src
                : new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0);
            encoder = new JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(BitmapFrame.Create(converted));
        }
        else
        {
            encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(src));
        }

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
    }
}

// ============================================================
// ImagePreviewWindow：大图片预览窗口
// - 居中显示，最大占屏幕 80%，图片等比缩放
// - ESC 关闭、点击空白处关闭
// - 复制按钮：把原始图片写入剪贴板
// - 保存按钮：另存为 PNG/JPEG（Ctrl+S）
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
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

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
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ImageSaver.SaveWithDialog(_bmp, this);
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

        // 右侧按钮组：保存 + 复制
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var saveBtn = new Button
        {
            Content = "保存",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Padding = new Thickness(18, 6, 18, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
            BorderThickness = new Thickness(0),
            Cursor = WpfCursors.Hand,
            ToolTip = "另存为图片（Ctrl+S）",
        };
        saveBtn.Click += (_, _) => ImageSaver.SaveWithDialog(_bmp, this);
        actions.Children.Add(saveBtn);

        var copyBtn = new Button
        {
            Content = "复制",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(18, 6, 18, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x4F, 0x46, 0xE5)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = WpfCursors.Hand,
            ToolTip = "复制到剪贴板（Ctrl+C）",
        };
        copyBtn.Click += (_, _) => CopyToClipboard();
        actions.Children.Add(copyBtn);

        barRow.Children.Add(actions);

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
            win.Show();
        });
    }
}
