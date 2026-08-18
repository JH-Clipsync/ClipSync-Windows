namespace ClipSync.App.UI;

using System.Windows;
using ClipSync.Core.Protocol;

// ============================================================
// ToastManager：管理多个 Toast 窗口的堆叠与生命期
// - 同一时刻最多 3 个 Toast，垂直堆叠在屏幕右上角
// - 每个 Toast 5 秒后自动关闭，关闭后下面的往上补
// - 3 秒内相同内容（text+kind+sender 相同）的消息去重，避免重复弹窗
//   （对齐 Mac 端：短时间内重复推送同一条短信验证码会被合并）
// - 提供 Show(message) 公共入口（App.OnMessageReceived 里调用）
// ============================================================
public sealed class ToastManager
{
    public static ToastManager Shared { get; } = new();

    private readonly List<Window> _active = new();
    private const int MaxActive = 3;

    /// <summary>3 秒内相同内容去重窗口（对齐 Mac 端 dedupWindow）</summary>
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(3);

    /// <summary>最近弹过的消息：(指纹, 时间戳)。按时间顺序，清理过期项。</summary>
    private readonly List<(string fingerprint, DateTime at)> _recentShown = new();

    private ToastManager() { }

    /// <summary>简洁状态通知（设备上下线）：标题 + 正文。</summary>
    public void ShowInfo(string title, string body, bool online)
    {
        if (System.Windows.Application.Current?.Dispatcher is not { } d) return;
        d.BeginInvoke(() =>
        {
            var fp = $"INFO|{(online ? "on" : "off")}|{title}|{body}";
            var now = DateTime.Now;
            _recentShown.RemoveAll(x => (now - x.at) > DedupWindow);
            if (_recentShown.Any(x => x.fingerprint == fp)) return;
            _recentShown.Add((fp, now));

            if (_active.Count >= MaxActive)
            {
                FadeOut(_active[0]);
            }

            var toast = new InfoToastWindow(title, body, online);
            toast.Closed += (_, _) =>
            {
                _active.Remove(toast);
                Relayout();
            };
            toast.ClosedByUser += () => _active.Remove(toast);
            _active.Add(toast);
            toast.Show();
            Relayout();
        });
    }

    public void Show(SyncMessage msg)
    {
        if (System.Windows.Application.Current?.Dispatcher is not { } d) return;

        // 先在调用线程算指纹（不依赖 UI 线程），避免把判重逻辑放进 BeginInvoke
        var fp = FingerprintOf(msg);
        var now = DateTime.Now;

        d.BeginInvoke(() =>
        {
            // 1) 先清理掉 3 秒窗口外的记录
            _recentShown.RemoveAll(x => (now - x.at) > DedupWindow);

            // 2) 判重：3 秒内指纹相同 → 跳过（同一短信被重复推送）
            if (_recentShown.Any(x => x.fingerprint == fp))
            {
                return;
            }
            _recentShown.Add((fp, now));

            // 超过上限：关掉最旧的那个（队头）
            if (_active.Count >= MaxActive)
            {
                FadeOut(_active[0]);
            }

            var toast = new ToastWindow(msg);
            toast.Closed += (_, _) =>
            {
                _active.Remove(toast);
                Relayout();
            };
            toast.ClosedByUser += () => _active.Remove(toast);

            _active.Add(toast);
            toast.Show();
            Relayout();
        });
    }

    /// <summary>3 秒内去重用的指纹：kind + sender + text/data 预览拼接。
    /// 只看内容是否"看起来同一条"，不在乎 id/ts 这些每发必变的字段。</summary>
    private static string FingerprintOf(SyncMessage msg)
    {
        var p = msg.Payload;
        // text 类：kind + sender + text(前100字)
        if (!string.IsNullOrEmpty(p.Text))
        {
            var t = p.Text.Length > 100 ? p.Text[..100] : p.Text;
            return $"T|{p.Kind ?? ""}|{p.Sender ?? ""}|{t}";
        }
        // 图片类：kind + mime + data 前 40 字符（base64 开头一致通常是同一张）
        if (!string.IsNullOrEmpty(p.Data))
        {
            var d = p.Data.Length > 40 ? p.Data[..40] : p.Data;
            return $"I|{p.Kind ?? ""}|{p.Mime ?? ""}|{d}";
        }
        return $"O|{msg.Type}|{p.Kind ?? ""}|{p.Preview ?? ""}";
    }

    private static void FadeOut(Window w)
    {
        switch (w)
        {
            case ToastWindow tw: tw.FadeOutAndClose(); break;
            case InfoToastWindow iw: iw.FadeOutAndClose(); break;
            default: w.Close(); break;
        }
    }

    private void Relayout()
    {
        if (_active.Count == 0) return;
        var screenRight = System.Windows.SystemParameters.WorkArea.Right;
        var top = 24.0;
        foreach (var t in _active)
        {
            // 防止窗口在加载完毕前拿不到高度：用 Top 偏移叠加
            t.Left = screenRight - t.Width - 24;
            t.Top = top;
            top += t.ActualHeight > 0 ? t.ActualHeight + 10 : 140;
        }
    }
}
