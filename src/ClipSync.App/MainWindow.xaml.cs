using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClipSync.App;

using ClipSync.App.Services;
using ClipSync.App.UI;
using ClipSync.Core.Net;
using ClipSync.Core.Storage;

// ============================================================
// MainWindow：左侧导航栏 + 右侧内容区（NavigationSplitView 风格）
// - 左侧三个项：主页 / 短信 / 剪贴板
// - 右侧对应显示 HomeView / HistoryView(sms) / HistoryView(clipboard)
// - 关闭按钮：按设置 MinimizeToTrayOnClose 决定是隐藏到托盘还是退出
// ============================================================
public partial class MainWindow : Window
{
    private enum NavItem { Home, Sms, Clipboard }

    private readonly ListBox _navList;
    private readonly Grid _root;
    private readonly Frame _contentFrame;

    private readonly HomeView _homeView;
    private readonly HistoryView _smsView;
    private readonly HistoryView _clipboardView;

    public MainWindow()
    {
        InitializeComponent();

        Title = "ClipSync";
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF8));

        // 根：左列 200 导航 + 右列 内容
        _root = new Grid();
        _root.ColumnDefinitions.Add(new ColumnDefinition
        { Width = new GridLength(200) });
        _root.ColumnDefinitions.Add(new ColumnDefinition
        { Width = new GridLength(1, GridUnitType.Star) });

        // 左侧：导航栏（背景比主窗口略深一丢丢，形成层次）
        var sideBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF3)),
            SnapsToDevicePixels = true,
        };
        var sidePadding = new Grid
        {
            Margin = new Thickness(12, 16, 12, 16),
        };
        sidePadding.RowDefinitions.Add(new RowDefinition
        { Height = GridLength.Auto });
        sidePadding.RowDefinitions.Add(new RowDefinition
        { Height = new GridLength(1, GridUnitType.Star) });

        // 左侧标题
        var appTitle = new TextBlock
        {
            Text = "ClipSync",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
            Margin = new Thickness(4, 0, 0, 16),
        };
        Grid.SetRow(appTitle, 0);
        sidePadding.Children.Add(appTitle);

        // 导航列表
        _navList = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
        };
        _navList.SelectionChanged += (_, _) => Navigate();
        Grid.SetRow(_navList, 1);
        sidePadding.Children.Add(_navList);

        sideBar.Child = sidePadding;
        Grid.SetColumn(sideBar, 0);
        _root.Children.Add(sideBar);

        // 右侧：内容
        var contentHost = new Border
        {
            Background = Background,
            Padding = new Thickness(0),
        };
        _contentFrame = new Frame
        {
            Background = Brushes.Transparent,
            NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden,
            Margin = new Thickness(0),
        };
        contentHost.Child = _contentFrame;
        Grid.SetColumn(contentHost, 1);
        _root.Children.Add(contentHost);

        Content = _root;

        // 预创建三个视图，各持同一个数据源的引用
        _homeView = new HomeView(this);
        _smsView = new HistoryView(HistoryStore.Filter.Sms);
        _clipboardView = new HistoryView(HistoryStore.Filter.Clipboard);

        PopulateNav();

        // 监听设置：剪贴板自动同步变更时 Home 里的开关同步
        SettingsStore.Shared.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsStore.AutoSyncClipboard))
            {
                _homeView.RefreshClipboardToggle();
            }
        };

        // 默认选中主页
        _navList.SelectedIndex = 0;

        // 关闭事件：隐藏到托盘还是真正关闭
        Closing += OnClosing;
    }

    private void PopulateNav()
    {
        _navList.Items.Clear();
        AddNavItem("🏠  主页", NavItem.Home, isSelected: true);
        AddNavItem("💬  短信", NavItem.Sms);
        AddNavItem("📋  剪贴板", NavItem.Clipboard);
    }

    private void AddNavItem(string label, NavItem tag, bool isSelected = false)
    {
        var item = new ListBoxItem
        {
            Content = label,
            Tag = tag,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 2, 0, 2),
            Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsSelected = isSelected,
        };
        // 自定义选中样式
        item.SetResourceReference(
            System.Windows.Controls.Control.BackgroundProperty,
            SystemColors.WindowBrushKey);
        _navList.Items.Add(item);
    }

    private void Navigate()
    {
        if (_navList.SelectedItem is not ListBoxItem item) return;
        var tag = (NavItem)item.Tag;
        switch (tag)
        {
            case NavItem.Home:
                _contentFrame.Content = _homeView.Root;
                _homeView.Refresh();
                break;
            case NavItem.Sms:
                _contentFrame.Content = _smsView.Root;
                _smsView.Refresh();
                break;
            case NavItem.Clipboard:
                _contentFrame.Content = _clipboardView.Root;
                _clipboardView.Refresh();
                break;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (SettingsStore.Shared.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            try { HistoryStore.Shared.SaveNow(); } catch { }
            try { SettingsStore.Shared.SaveNow(); } catch { }
        }
    }

    // === HomeView 与 HistoryView 需要的导航方法 ===
    public void NavigateToSms() => _navList.SelectedIndex = 1;
    public void NavigateToClipboard() => _navList.SelectedIndex = 2;
}
