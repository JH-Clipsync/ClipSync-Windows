namespace ClipSync.App.UI;

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using ClipSync.Core.Crypto;
using ClipSync.Core.Protocol;

// ============================================================
// ToastManager：管理多个 Toast 窗口的堆叠与生命期
// - 同一时刻最多 3 个 Toast，垂直堆叠在屏幕右上角
// - 每个 Toast 5 秒后自动关闭，关闭后下面的往上补
// - 去重双轨（与 HistoryStore 的 id 去重对齐，Mac 端同构修改）：
//   1) 有稳定 id 的消息（手机短信等）：按 id 走 LRU 集合——
//      容量 200、时间窗口 10 分钟（覆盖 Android 75s 送达确认窗并留余量）。
//      重连补发的同 id 帧命中后静默不弹；仅当后到帧正文更完整
//      （更长 / 从打码占位变明文）才原地更新已弹 Toast。
//   2) 无 id 的消息（剪贴板等无稳定 id 类型）：沿用 3 秒内容指纹
//      （text+kind+sender）兜底，行为与旧版完全一致。
// - 线程约定：所有集合访问都在 Dispatcher 的 UI 线程上执行；
//   Show/ShowInfo 仅在调用线程做无状态计算后 BeginInvoke 投到 UI 线程。
// - 提供 Show(message) 公共入口（App.OnMessageReceived 里调用）
// ============================================================
public sealed class ToastManager
{
    public static ToastManager Shared { get; } = new();

    private readonly List<Window> _active = new();
    private const int MaxActive = 3;

    /// <summary>无 id 消息的内容指纹去重窗口（对齐 Mac 端 dedupWindow）。</summary>
    private static readonly TimeSpan FingerprintWindow = TimeSpan.FromSeconds(3);

    /// <summary>最近弹过的无 id 消息：(指纹, 时间戳)。按时间顺序，清理过期项。</summary>
    private readonly List<(string fingerprint, DateTime at)> _recentShown = new();

    /// <summary>有 id 消息的 LRU 去重容量。</summary>
    private const int IdLruCapacity = 200;

    /// <summary>有 id 消息的去重时间窗口：10 分钟（Android 75s 送达确认窗 + 余量）。</summary>
    private static readonly TimeSpan IdDedupWindow = TimeSpan.FromMinutes(10);

    // 链表头=最近见过，尾=最久未碰；_idIndex 提供按 id 的 O(1) 查找。
    private readonly LinkedList<IdDedupEntry> _idLru = new();
    private readonly Dictionary<string, LinkedListNode<IdDedupEntry>> _idIndex = new();

    /// <summary>打码识别与 Android NotificationSmsListener 对齐：连续 5 个及以上 *。</summary>
    private static readonly Regex MaskedRegex = new(@"\*{5,}", RegexOptions.Compiled);

    /// <summary>全局键盘监听：Toast 因为 ShowActivated=false 不拿焦点，
    /// 用 EventManager.RegisterClassHandler 在 Window 类上挂一个 PreviewKeyDown，
    /// ESC 时关掉最顶部的 Toast，其他键原样放行。</summary>
    private bool _escapeHooked;

    private ToastManager() { }

    /// <summary>一条有 id 消息的去重记录。</summary>
    private sealed class IdDedupEntry
    {
        public required string Id { get; init; }
        public DateTime At { get; set; }
        /// <summary>已展示正文的完整度快照，用于判断后到帧是否「更完整」。</summary>
        public BodySnapshot Body { get; set; }
        /// <summary>仍在屏幕上的 Toast；关闭后置 null（记录保留到过期/淘汰）。</summary>
        public ToastWindow? Toast { get; set; }
    }

    /// <summary>正文完整度快照：文本看有效长度与打码标记，图片看 base64 数据长度。</summary>
    private readonly record struct BodySnapshot(int Length, bool Masked, bool IsImage, int DataLength)
    {
        public static BodySnapshot Of(SyncMessage msg, Regex maskedRegex)
        {
            var p = msg.Payload;
            if (!string.IsNullOrEmpty(p.Data))
            {
                return new BodySnapshot(0, false, true, p.Data!.Length);
            }
            // 与 ToastWindow.BodyText 的取文口径一致：Text 缺失时退回 Preview
            var text = string.IsNullOrEmpty(p.Text) ? (p.Preview ?? "") : p.Text!;
            var masked = maskedRegex.IsMatch(text) || text.Contains(PayloadCipher.Placeholder);
            return new BodySnapshot(text.Length, masked, false, 0);
        }
    }

    /// <summary>后到帧是否比已展示版本更完整：
    /// 图片看数据长度；文本先看「打码→明文」，再看有效正文是否更长。
    /// 等长或更短（含完全相同）都不算更完整 → 调用方静默处理。</summary>
    private static bool IsMoreComplete(BodySnapshot incoming, BodySnapshot existing)
    {
        if (incoming.IsImage || existing.IsImage)
        {
            return incoming.DataLength > existing.DataLength;
        }
        if (existing.Masked && !incoming.Masked) return true;
        if (incoming.Masked) return false;
        return incoming.Length > existing.Length;
    }

    private void EnsureEscapeHook()
    {
        if (_escapeHooked) return;
        _escapeHooked = true;
        EventManager.RegisterClassHandler(
            typeof(Window),
            UIElement.PreviewKeyDownEvent,
            new KeyEventHandler((_, e) =>
            {
                if (e.Key != Key.Escape) return;
                // 预览窗口自己的 ESC 优先（它是激活窗口），不要把它和 Toast 一起关掉。
                if (Application.Current?.Windows.OfType<ImagePreviewWindow>()
                    .Any(w => w.IsActive) == true) return;
                if (_active.Count > 0)
                {
                    var top = _active[^1];
                    FadeOut(top);
                    e.Handled = true;
                }
            }));
    }

    /// <summary>简洁状态通知（设备上下线）：标题 + 正文。</summary>
    public void ShowInfo(string title, string body, bool online)
    {
        EnsureEscapeHook();
        if (System.Windows.Application.Current?.Dispatcher is not { } d) return;
        d.BeginInvoke(() =>
        {
            var fp = $"INFO|{(online ? "on" : "off")}|{title}|{body}";
            var now = DateTime.Now;
            _recentShown.RemoveAll(x => (now - x.at) > FingerprintWindow);
            if (_recentShown.Any(x => x.fingerprint == fp)) return;
            _recentShown.Add((fp, now));

            var toast = PresentNewInfoToast(title, body, online);
            toast.Show();
        });
    }

    public void Show(SyncMessage msg)
    {
        EnsureEscapeHook();
        if (System.Windows.Application.Current?.Dispatcher is not { } d) return;

        // 调用线程上只做无状态计算，集合读写全部投到 UI 线程
        var hasId = !string.IsNullOrEmpty(msg.Id);
        var fingerprint = FingerprintOf(msg);
        var snapshot = BodySnapshot.Of(msg, MaskedRegex);
        var now = DateTime.Now;

        d.BeginInvoke(() =>
        {
            if (hasId)
            {
                ShowById(msg, snapshot, now);
            }
            else
            {
                ShowByFingerprint(msg, fingerprint, now);
            }
        });
    }

    /// <summary>有稳定 id：LRU 去重 + 同 id 补明文更新。</summary>
    private void ShowById(SyncMessage msg, BodySnapshot snapshot, DateTime now)
    {
        PruneExpiredIdEntries(now);

        if (_idIndex.TryGetValue(msg.Id, out var node))
        {
            var entry = node.Value;
            if (!IsMoreComplete(snapshot, entry.Body))
            {
                // 同 id 复帧且没有更完整（重连补发的典型场景）：静默不弹
                TouchIdEntry(node, now);
                return;
            }

            // 同 id 的更完整帧（如先收到打码占位、后补到明文）：
            // 历史此时已由 HistoryStore.Append 按 id 替换为新版本；
            // Toast 还在屏幕上就原地替换内容，已关闭则只更新记录、不重新弹窗。
            entry.Body = snapshot;
            TouchIdEntry(node, now);
            if (entry.Toast is { } alive)
            {
                alive.UpdateMessage(msg);
                Relayout();
            }
            return;
        }

        // 新 id：先进 LRU（超容量淘汰最久未碰的），再弹窗
        var newEntry = new IdDedupEntry { Id = msg.Id, At = now, Body = snapshot };
        _idIndex[msg.Id] = _idLru.AddFirst(newEntry);
        EvictOverflowIdEntries();

        var toast = PresentNewToast(msg);
        newEntry.Toast = toast;
        toast.Closed += (_, _) => newEntry.Toast = null;
    }

    /// <summary>无稳定 id：沿用 3 秒内容指纹兜底，旧行为不变。</summary>
    private void ShowByFingerprint(SyncMessage msg, string fingerprint, DateTime now)
    {
        _recentShown.RemoveAll(x => (now - x.at) > FingerprintWindow);
        if (_recentShown.Any(x => x.fingerprint == fingerprint)) return;
        _recentShown.Add((fingerprint, now));
        PresentNewToast(msg);
    }

    /// <summary>创建并展示一个新业务 Toast，接好生命期回调后返回实例。</summary>
    private ToastWindow PresentNewToast(SyncMessage msg)
    {
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
        return toast;
    }

    private InfoToastWindow PresentNewInfoToast(string title, string body, bool online)
    {
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
        Relayout();
        return toast;
    }

    // === id LRU 维护（仅 UI 线程调用）===

    /// <summary>清掉超过 10 分钟窗口的记录。链表按 At 从新到旧排列，过期项必在尾部。</summary>
    private void PruneExpiredIdEntries(DateTime now)
    {
        while (_idLru.Count > 0)
        {
            var last = _idLru.Last!;
            if (now - last.Value.At <= IdDedupWindow) break;
            _idIndex.Remove(last.Value.Id);
            _idLru.RemoveLast();
        }
    }

    /// <summary>容量上限 200：超出时从尾部淘汰最久未碰的记录。</summary>
    private void EvictOverflowIdEntries()
    {
        while (_idLru.Count > IdLruCapacity)
        {
            var last = _idLru.Last!;
            _idIndex.Remove(last.Value.Id);
            _idLru.RemoveLast();
        }
    }

    /// <summary>命中/更新后把节点挪到链头并刷新时间，维持 LRU 顺序。</summary>
    private void TouchIdEntry(LinkedListNode<IdDedupEntry> node, DateTime now)
    {
        node.Value.At = now;
        _idLru.Remove(node);
        _idLru.AddFirst(node);
    }

    /// <summary>3 秒内去重用的指纹：kind + sender + text/data 预览拼接。
    /// 只看内容是否"看起来同一条"，不在乎 id/ts 这些每发必变的字段。
    /// 仅用于无稳定 id 的消息。</summary>
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
