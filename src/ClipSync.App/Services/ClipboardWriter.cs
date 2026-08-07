using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ClipSync.App.Services;

using ClipSync.Core.Diagnostics;
using ClipSync.Core.Protocol;

// ============================================================
// ClipboardWriter：把远端同步过来的 payload 写到本机剪贴板
// - 写之前调用 ClipboardMonitor.Shared.SuppressNext()，避免自己监听到后
//   再次回传服务器（回环问题）
// - 文本：Clipboard.SetText
// - 图片：base64 → BitmapSource → Clipboard.SetImage
// ============================================================
public static class ClipboardWriter
{
    public static void Apply(MessagePayload payload)
    {
        try
        {
            ClipboardMonitor.Shared.SuppressNext();

            // 优先图片
            if (!string.IsNullOrEmpty(payload.Data)
                && (payload.Mime?.StartsWith("image/", StringComparison.Ordinal) == true
                    || payload.Kind == MessageKind.Image))
            {
                var bytes = Convert.FromBase64String(payload.Data);
                using var ms = new MemoryStream(bytes);
                var bmp = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                bmp.Freeze();
                Clipboard.SetImage(bmp);

                // 签名：图片按字节数记录，避免 tick 立刻回传
                ClipboardMonitor.Shared.MarkSignature($"img:{bytes.Length}");
                Log.Info("[Clipboard] ↓ 已写入图片到剪贴板");
                return;
            }

            // 文本
            if (!string.IsNullOrEmpty(payload.Text))
            {
                Clipboard.SetText(payload.Text);
                ClipboardMonitor.Shared.MarkSignature($"text:{payload.Text.GetHashCode(StringComparison.Ordinal)}");
                Log.Info($"[Clipboard] ↓ 已写入文本 {payload.Text.Length} 字符");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[Clipboard] 写入失败: {ex.Message}");
        }
    }

    /// <summary>只复制一段纯文本（Toast 上的「复制验证码」按钮用）。</summary>
    public static void CopyText(string text)
    {
        try
        {
            ClipboardMonitor.Shared.SuppressNext();
            Clipboard.SetText(text);
            ClipboardMonitor.Shared.MarkSignature($"text:{text.GetHashCode(StringComparison.Ordinal)}");
        }
        catch (Exception ex)
        {
            Log.Warn($"[Clipboard] 复制文本失败: {ex.Message}");
        }
    }
}
