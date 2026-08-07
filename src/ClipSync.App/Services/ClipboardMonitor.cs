using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Imaging = System.Drawing.Imaging;

namespace ClipSync.App.Services;

using ClipSync.Core.Diagnostics;
using ClipSync.Core.Net;
using ClipSync.Core.Protocol;
using ClipSync.Core.Storage;

// ============================================================
// ClipboardMonitor：轮询 Clipboard 变化，检测本机剪贴板变化
// - Windows 可以用 AddClipboardFormatListener 做真监听，但轮询 600ms
//   已经够实用，而且不依赖 P/Invoke，跨 Win10/11 版本最稳
// - 需要过滤"自己写入的内容"（收到远端消息后写回时不再上传）
// - 通过 suppressNext() + markSignature() 双重去重
// ============================================================

/// <summary>剪贴板图片压缩：长边缩到 1600 + JPEG(0.82)，避免截图把 WS 撑断。</summary>
public static class ClipboardImageCompressor
{
    public const int MaxEdge = 1600;

    /// <summary>返回 (base64, mime)，输入是 WPF 位图或原始字节。</summary>
    public static (string base64, string mime)? Compress(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var img = Drawing.Image.FromStream(ms);
            var longEdge = Math.Max(img.Width, img.Height);

            // 小图直接走 PNG（保持透明）
            if (longEdge <= MaxEdge)
            {
                using var outMs = new MemoryStream();
                img.Save(outMs, Imaging.ImageFormat.Png);
                return (Convert.ToBase64String(outMs.ToArray()), "image/png");
            }

            // 大图缩放后转 JPEG(0.82)
            var scale = (double)MaxEdge / longEdge;
            var w = Math.Max(1, (int)(img.Width * scale));
            var h = Math.Max(1, (int)(img.Height * scale));
            using var bmp = new Drawing.Bitmap(w, h);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, 0, 0, w, h);
            }
            using var jpegMs = new MemoryStream();
            var jpegEncoder = Imaging.ImageCodecInfo.GetImageEncoders()
                .First(e => e.FormatID == Imaging.ImageFormat.Jpeg.Guid);
            var @params = new Imaging.EncoderParameters(1);
            @params.Param[0] = new Imaging.EncoderParameter(Imaging.Encoder.Quality, 82L);
            bmp.Save(jpegMs, jpegEncoder, @params);
            return (Convert.ToBase64String(jpegMs.ToArray()), "image/jpeg");
        }
        catch (Exception ex)
        {
            Log.Warn($"[Clipboard] 图片压缩失败: {ex.Message}");
            return null;
        }
    }
}

public sealed class ClipboardMonitor
{
    public static ClipboardMonitor Shared { get; } = new();

    /// <summary>是否启用（由 SettingsStore.AutoSyncClipboard 控制）。</summary>
    public bool IsEnabled { get; set; }

    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private uint _lastChangeCount;
    private string _lastSignature = "";
    private int _suppressCount;

    private WSClient? _ws;

    private ClipboardMonitor()
    {
        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _timer.Tick += (_, _) => Tick();
    }

    public void Bind(WSClient ws) => _ws = ws;

    public void Start()
    {
        if (_timer.IsEnabled)
        {
            Log.Info("[Clipboard] 已在监听，跳过");
            return;
        }
        _lastChangeCount = GetChangeCount();
        _timer.Start();
        Log.Info("[Clipboard] 监听已启动");
    }

    public void Stop()
    {
        _timer.Stop();
        Log.Info("[Clipboard] 监听已停止");
    }

    /// <summary>写入剪贴板前调用，让下一次 tick 忽略这次变化。</summary>
    public void SuppressNext() => Interlocked.Increment(ref _suppressCount);

    /// <summary>记录签名，防止连续两次相同内容重复上传。</summary>
    public void MarkSignature(string sig) => _lastSignature = sig;

    // MARK: - 手动推送 API（UI 按钮用）

    /// <summary>读取当前剪贴板文本（不上传）。</summary>
    public static string? PeekText()
    {
        try
        {
            if (!Clipboard.ContainsText()) return null;
            var text = Clipboard.GetText();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch { return null; }
    }

    /// <summary>当前剪贴板是否有可推送的内容（文本或图片）。</summary>
    public static bool HasContent
    {
        get
        {
            try { return Clipboard.ContainsText() || Clipboard.ContainsImage(); }
            catch { return false; }
        }
    }

    /// <summary>
    /// 手动推送当前剪贴板内容。与自动 tick 不同，这里会强制放行。
    /// 返回推送类型（"text" / "image" / null），供 UI 给出反馈。
    /// </summary>
    public string? ManualPush()
    {
        _lastSignature = "";
        return TickCore(force: true);
    }

    // MARK: - 轮询核心

    private static uint GetChangeCount()
    {
        // WPF 的 Clipboard 不直接暴露 sequence number，
        // 这里用"内容 hash"做兜底比对：文本 hash，图片取尺寸+格式
        try
        {
            if (Clipboard.ContainsText())
                return (uint)Clipboard.GetText().GetHashCode(StringComparison.Ordinal);
            if (Clipboard.ContainsImage())
            {
                var bmp = Clipboard.GetImage();
                if (bmp is not null)
                    return (uint)(bmp.PixelWidth * 31 + bmp.PixelHeight);
            }
        }
        catch { /* ignore */ }
        return 0;
    }

    private void Tick()
    {
        if (!IsEnabled) return;
        var cc = GetChangeCount();
        if (cc == _lastChangeCount) return;
        _lastChangeCount = cc;

        if (_suppressCount > 0)
        {
            Interlocked.Decrement(ref _suppressCount);
            return;
        }

        TickCore(force: false);
    }

    private string? TickCore(bool force)
    {
        // 优先文本
        if (PeekText() is { } text)
        {
            var sig = $"text:{text.GetHashCode(StringComparison.Ordinal)}";
            if (!force && sig == _lastSignature) return null;
            _lastSignature = sig;
            _ws?.SendClipboardText(text);
            Log.Info($"[Clipboard] ↑ 上传文本 {text.Length} 字符");
            return "text";
        }

        // 尝试图片
        try
        {
            if (Clipboard.ContainsImage())
            {
                // 把 WPF BitmapSource 转成字节数组走压缩
                var bmp = Clipboard.GetImage();
                if (bmp is not null)
                {
                    byte[] bytes;
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    using var ms = new MemoryStream();
                    encoder.Save(ms);
                    bytes = ms.ToArray();

                    if (bytes.Length > 0)
                    {
                        var sig = $"img:{bytes.Length}";
                        if (!force && sig == _lastSignature) return null;
                        _lastSignature = sig;

                        var compressed = ClipboardImageCompressor.Compress(bytes);
                        if (compressed.HasValue)
                        {
                            _ws?.SendClipboardImage(compressed.Value.base64, compressed.Value.mime);
                            Log.Info($"[Clipboard] ↑ 上传图片 (base64 {compressed.Value.base64.Length} 字符)");
                            return "image";
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[Clipboard] 读取图片失败: {ex.Message}");
        }

        return null;
    }
}
