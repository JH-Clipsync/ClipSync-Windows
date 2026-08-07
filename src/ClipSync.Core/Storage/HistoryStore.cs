using System.Collections.ObjectModel;
using System.Text;

namespace ClipSync.Core.Storage;

using ClipSync.Core.Diagnostics;
using ClipSync.Core.Protocol;

// ============================================================
// HistoryStore：本地消息历史持久化
//
// 存储位置：%APPDATA%\ClipSync\history.json
//
// 特性（与 Mac 端 HistoryStore.swift 对齐）：
//   - Messages 是 ObservableCollection，WPF 列表可直接绑定
//   - Append 自动按 id 去重 + 最新在前 + 上限裁剪
//   - 变更后 200ms 防抖写盘
//   - 提供 sms / clipboard 过滤视图
//
// 线程约定：所有集合改动都通过 _dispatch 投到 UI 线程执行，
// 这样 WebSocket 后台线程收到消息也能安全更新绑定集合。
// ============================================================
public sealed class HistoryStore
{
    public static HistoryStore Shared { get; } = new();

    public enum Filter { Sms, Clipboard }

    /// <summary>全部历史消息（时间倒序，最新在前）。</summary>
    public ObservableCollection<SyncMessage> Messages { get; } = new();

    /// <summary>历史发生任何变化后触发，UI 用它刷新计数/筛选视图。</summary>
    public event Action? Changed;

    /// <summary>最多保留多少条（避免文件无限增长）。</summary>
    private const int MaxCount = 500;

    private readonly object _saveGate = new();
    private Timer? _saveTimer;
    private Action<Action> _dispatch = action => action();

    private HistoryStore() => Load();

    /// <summary>把集合改动交给 UI 线程执行。App 启动时注入 Dispatcher.Invoke。</summary>
    public void UseDispatcher(Action<Action> dispatch) => _dispatch = dispatch;

    // MARK: - 增

    /// <summary>追加一条消息；已存在（同 id）则替换到最前。</summary>
    public void Append(SyncMessage message)
    {
        _dispatch(() =>
        {
            for (var i = Messages.Count - 1; i >= 0; i--)
            {
                if (Messages[i].Id == message.Id) Messages.RemoveAt(i);
            }
            Messages.Insert(0, message);
            while (Messages.Count > MaxCount) Messages.RemoveAt(Messages.Count - 1);
            ScheduleSave();
            Changed?.Invoke();
        });
    }

    // MARK: - 删

    public void Clear()
    {
        _dispatch(() =>
        {
            Messages.Clear();
            ScheduleSave();
            Changed?.Invoke();
        });
    }

    public void Clear(Filter filter)
    {
        _dispatch(() =>
        {
            for (var i = Messages.Count - 1; i >= 0; i--)
            {
                var isMatch = filter == Filter.Sms ? Messages[i].IsSms : Messages[i].IsClipboard;
                if (isMatch) Messages.RemoveAt(i);
            }
            ScheduleSave();
            Changed?.Invoke();
        });
    }

    public void Remove(string id)
    {
        _dispatch(() =>
        {
            for (var i = Messages.Count - 1; i >= 0; i--)
            {
                if (Messages[i].Id == id) Messages.RemoveAt(i);
            }
            ScheduleSave();
            Changed?.Invoke();
        });
    }

    // MARK: - 查

    public IReadOnlyList<SyncMessage> Filtered(Filter filter) =>
        Messages.Where(m => filter == Filter.Sms ? m.IsSms : m.IsClipboard).ToList();

    public int SmsCount => Messages.Count(m => m.IsSms);
    public int ClipboardCount => Messages.Count(m => m.IsClipboard);

    // MARK: - 持久化

    private void Load()
    {
        try
        {
            var path = AppPaths.HistoryFile;
            if (!File.Exists(path))
            {
                Log.Info("[History] 无历史文件，从空开始");
                return;
            }
            var list = ProtocolJson.Deserialize<List<SyncMessage>>(
                File.ReadAllText(path, Encoding.UTF8));
            if (list is null) return;
            foreach (var m in list) Messages.Add(m);
            Log.Info($"[History] 已加载 {list.Count} 条历史");
        }
        catch (Exception ex)
        {
            Log.Warn($"[History] 加载失败: {ex.Message}");
        }
    }

    /// <summary>200ms 防抖后写盘。</summary>
    private void ScheduleSave()
    {
        lock (_saveGate)
        {
            _saveTimer?.Dispose();
            _saveTimer = new Timer(_ => SaveNow(), null, 200, Timeout.Infinite);
        }
    }

    public void SaveNow()
    {
        // 快照要在 UI 线程取：后台线程直接遍历 ObservableCollection 会和
        // Append/Remove 撞上，抛 InvalidOperationException。
        List<SyncMessage>? snapshot = null;
        _dispatch(() => snapshot = Messages.ToList());
        if (snapshot is null) return;
        try
        {
            AppPaths.EnsureRoot();
            File.WriteAllText(
                AppPaths.HistoryFile, ProtocolJson.Serialize(snapshot), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Warn($"[History] 保存失败: {ex.Message}");
        }
    }
}
