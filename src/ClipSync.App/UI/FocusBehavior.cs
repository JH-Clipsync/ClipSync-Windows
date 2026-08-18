using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClipSync.App.UI;

// ============================================================
// FocusBehavior：统一处理输入框获焦时的光标位置
//
// 背景：
//   WPF 的 PasswordBox / TextBox 在程序化 Focus() 或用户点击时，
//   光标默认停在开头；对于"已填充内容"的输入框（如设置页预填的密码），
//   用户希望光标落在末尾以便直接追加/修改。
//
// 用法：
//   <PasswordBox ui:FocusBehavior.CaretAtEndOnFocus="True" />
//   FocusBehavior.SetCaretAtEndOnFocus(box, true);
//
// 额外提供：
//   - SelectAllOnFocus：获焦时全选（重命名对话框等场景）
// ============================================================
public static class FocusBehavior
{
    // ------------------------------------------------------------
    // CaretAtEndOnFocus：获焦时把光标移到内容末尾
    // ------------------------------------------------------------
    public static readonly DependencyProperty CaretAtEndOnFocusProperty =
        DependencyProperty.RegisterAttached(
            "CaretAtEndOnFocus",
            typeof(bool),
            typeof(FocusBehavior),
            new PropertyMetadata(false, OnCaretAtEndOnFocusChanged));

    public static bool GetCaretAtEndOnFocus(DependencyObject obj)
        => (bool)obj.GetValue(CaretAtEndOnFocusProperty);

    public static void SetCaretAtEndOnFocus(DependencyObject obj, bool value)
        => obj.SetValue(CaretAtEndOnFocusProperty, value);

    private static void OnCaretAtEndOnFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox tb)
        {
            if ((bool)e.NewValue)
            {
                tb.GotKeyboardFocus += OnTextBoxGotFocus;
                tb.PreviewMouseLeftButtonDown += OnTextBoxPreviewMouseDown;
            }
            else
            {
                tb.GotKeyboardFocus -= OnTextBoxGotFocus;
                tb.PreviewMouseLeftButtonDown -= OnTextBoxPreviewMouseDown;
            }
        }
        else if (d is PasswordBox pb)
        {
            if ((bool)e.NewValue)
            {
                pb.GotKeyboardFocus += OnPasswordGotFocus;
                pb.PreviewMouseLeftButtonDown += OnPasswordPreviewMouseDown;
            }
            else
            {
                pb.GotKeyboardFocus -= OnPasswordGotFocus;
                pb.PreviewMouseLeftButtonDown -= OnPasswordPreviewMouseDown;
            }
        }
    }

    private static void OnTextBoxGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        tb.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (tb.IsLoaded && tb.IsVisible)
            {
                tb.CaretIndex = tb.Text?.Length ?? 0;
                tb.ScrollToEnd();
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private static void OnTextBoxPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.IsKeyboardFocusWithin) return;
        tb.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (tb.IsLoaded && tb.IsVisible)
            {
                tb.CaretIndex = tb.Text?.Length ?? 0;
                tb.ScrollToEnd();
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private static void OnPasswordGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not PasswordBox pb) return;
        pb.Dispatcher.BeginInvoke(new Action(() =>
        {
            SetPasswordCaretToEnd(pb);
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private static void OnPasswordPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not PasswordBox pb) return;
        if (pb.IsKeyboardFocusWithin) return;
        pb.Dispatcher.BeginInvoke(new Action(() =>
        {
            SetPasswordCaretToEnd(pb);
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    // PasswordBox 没有公开的 CaretIndex API，通过反射拿到内部的
    // TextEditor 再调用 Select(len, 0) 来把插入点定位到末尾。
    private static void SetPasswordCaretToEnd(PasswordBox pb)
    {
        try
        {
            if (!pb.IsLoaded || !pb.IsVisible) return;
            var len = pb.SecurePassword?.Length ?? pb.Password.Length;
            if (len == 0) return;

            var textEditorProp = typeof(PasswordBox).GetProperty(
                "TextEditor",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            var textEditor = textEditorProp?.GetValue(pb);
            if (textEditor is null) return;

            var selectMethod = textEditor.GetType().GetMethod(
                "Select",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            selectMethod?.Invoke(textEditor, new object[] { len, 0 });
        }
        catch
        {
            // 反射拿内部 TextEditor 失败时静默兜底（极少数 .NET 版本内部结构变化）
        }
    }

    // ------------------------------------------------------------
    // SelectAllOnFocus：获焦时全选文本（重命名、批量输入等场景）
    // ------------------------------------------------------------
    public static readonly DependencyProperty SelectAllOnFocusProperty =
        DependencyProperty.RegisterAttached(
            "SelectAllOnFocus",
            typeof(bool),
            typeof(FocusBehavior),
            new PropertyMetadata(false, OnSelectAllOnFocusChanged));

    public static bool GetSelectAllOnFocus(DependencyObject obj)
        => (bool)obj.GetValue(SelectAllOnFocusProperty);

    public static void SetSelectAllOnFocus(DependencyObject obj, bool value)
        => obj.SetValue(SelectAllOnFocusProperty, value);

    private static void OnSelectAllOnFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox tb)
        {
            if ((bool)e.NewValue) tb.GotKeyboardFocus += OnTextBoxSelectAll;
            else tb.GotKeyboardFocus -= OnTextBoxSelectAll;
        }
        else if (d is PasswordBox pb)
        {
            if ((bool)e.NewValue) pb.GotKeyboardFocus += OnPasswordSelectAll;
            else pb.GotKeyboardFocus -= OnPasswordSelectAll;
        }
    }

    private static void OnTextBoxSelectAll(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (tb.IsLoaded && tb.IsVisible && tb.Text?.Length > 0) tb.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private static void OnPasswordSelectAll(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is PasswordBox pb)
        {
            pb.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var len = pb.SecurePassword?.Length ?? pb.Password.Length;
                    if (len == 0) return;
                    var textEditorProp = typeof(PasswordBox).GetProperty(
                        "TextEditor",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);
                    var textEditor = textEditorProp?.GetValue(pb);
                    var selectMethod = textEditor?.GetType().GetMethod(
                        "Select",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                    selectMethod?.Invoke(textEditor, new object[] { 0, len });
                }
                catch { /* 忽略 */ }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}
