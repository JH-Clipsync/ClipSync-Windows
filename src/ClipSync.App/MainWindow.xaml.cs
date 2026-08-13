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
    private enum NavItem { Home, Sms, Clipboard, Settings }

    private readonly ListBox _navList;
    private readonly Grid _root;
    private readonly Frame _contentFrame;

    private readonly HomeView _homeView;
    private readonly HistoryView _smsView;
    private readonly HistoryView _clipboardView;
    private readonly SettingsView _settingsView;

    public MainWindow()
    {
        InitializeComponent();

        Title = "ClipSync";
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF8));
        Icon = App.GetWindowIcon();
        ShowInTaskbar = true;

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
        sidePadding.RowDefinitions.Add(new RowDefinition
        { Height = GridLength.Auto });

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

        // 底部设置按钮（点击后在内嵌区域打开设置页，不再弹窗）
        var settingsBtn = new Button
        {
            Content = "⚙  设置",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        settingsBtn.Click += (_, _) =>
        {
            // 选中侧边栏的"设置"项，Navigate 会切到 SettingsView
            for (var i = 0; i < _navList.Items.Count; i++)
            {
                if (_navList.Items[i] is ListBoxItem li && li.Tag is NavItem n && n == NavItem.Settings)
                {
                    _navList.SelectedIndex = i;
                    break;
                }
            }
        };
        Grid.SetRow(settingsBtn, 2);
        sidePadding.Children.Add(settingsBtn);

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

        // 预创建视图
        _homeView = new HomeView(this);
        _smsView = new HistoryView(HistoryStore.Filter.Sms);
        _clipboardView = new HistoryView(HistoryStore.Filter.Clipboard);
        _settingsView = new SettingsView();

        PopulateNav();

        // 监听设置变更（设置页保存后，主页状态卡需要同步）
        SettingsStore.Shared.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsStore.AutoSyncClipboard)
                or nameof(SettingsStore.ServerUrl)
                or nameof(SettingsStore.Username)
                or nameof(SettingsStore.Password)
                or nameof(SettingsStore.Token))
            {
                _homeView.Refresh();
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
        // 设置入口放在侧边栏底部按钮，不占导航列表
        AddNavItem("⚙  设置", NavItem.Settings, hidden: true);
    }

    private void AddNavItem(string label, NavItem tag, bool isSelected = false, bool hidden = false)
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
            Visibility = hidden ? Visibility.Collapsed : Visibility.Visible,
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
            case NavItem.Settings:
                _contentFrame.Content = _settingsView.Root;
                _settingsView.Refresh();
                break;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (SettingsStore.Shared.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            // 如果当前处于最小化状态，先恢复正常再 Hide，否则某些 Windows 版本下
            // 隐藏最小化窗口会导致任务栏按钮消失而托盘图标也一并失效。
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
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

    /// <summary>跳转到设置页（选中隐藏的设置导航项）。</summary>
    public void NavigateToSettings()
    {
        for (var i = 0; i < _navList.Items.Count; i++)
        {
            if (_navList.Items[i] is ListBoxItem li && li.Tag is NavItem n && n == NavItem.Settings)
            {
                _navList.SelectedIndex = i;
                break;
            }
        }
    }

    /// <summary>回到主页（选中第一个导航项）。</summary>
    public void NavigateHome() => _navList.SelectedIndex = 0;
}
