namespace ClipSync.App.UI;

using ClipSync.Core.Protocol;

// ============================================================
// ToastManager：管理多个 Toast 窗口的堆叠与生命期
// - 同一时刻最多 3 个 Toast，垂直堆叠在屏幕右上角
// - 每个 Toast 5 秒后自动关闭，关闭后下面的往上补
// - 提供 Show(message) 公共入口（App.OnMessageReceived 里调用）
// ============================================================
public sealed class ToastManager
{
    public static ToastManager Shared { get; } = new();

    private readonly List<ToastWindow> _active = new();
    private const int MaxActive = 3;

    private ToastManager() { }

    public void Show(SyncMessage msg)
    {
        if (System.Windows.Application.Current?.Dispatcher is not { } d) return;

        d.BeginInvoke(() =>
        {
            // 超过上限：关掉最旧的那个（队头）
            while (_active.Count >= MaxActive)
            {
                var oldest = _active[0];
                oldest.FadeOutAndClose();
                // 事件会把它从列表移除
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
