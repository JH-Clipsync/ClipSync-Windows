using System.Windows.Media;

namespace ClipSync.App.UI;

/// <summary>
/// 全局配色方案：现代简洁的 Indigo + Emerald 主题。
/// 所有 UI 控件统一从这里取色，避免每个文件硬编码颜色。
/// </summary>
internal static class AppColors
{
    // 主色
    public static readonly Color Primary = Color.FromRgb(0x4F, 0x46, 0xE5);      // Indigo-600
    public static readonly Color PrimaryDark = Color.FromRgb(0x43, 0x3A, 0xCA);  // Indigo-700
    public static readonly Color PrimaryLight = Color.FromRgb(0x81, 0x7C, 0xF4); // Indigo-400
    public static readonly Color PrimarySubtle = Color.FromRgb(0xE0, 0xE7, 0xFF); // Indigo-100

    // 成功 / 在线
    public static readonly Color Success = Color.FromRgb(0x10, 0xB9, 0x81);      // Emerald-500
    public static readonly Color SuccessDark = Color.FromRgb(0x05, 0x96, 0x69);  // Emerald-600
    public static readonly Color SuccessSubtle = Color.FromRgb(0xD1, 0xFA, 0xE5); // Emerald-100

    // 警告
    public static readonly Color Warning = Color.FromRgb(0xF5, 0x9E, 0x0B);      // Amber-500
    public static readonly Color WarningSubtle = Color.FromRgb(0xFE, 0xF3, 0xC7); // Amber-100

    // 错误
    public static readonly Color Danger = Color.FromRgb(0xEF, 0x44, 0x44);       // Rose-500
    public static readonly Color DangerSubtle = Color.FromRgb(0xFE, 0xE2, 0xE2); // Rose-100

    // 灰度
    public static readonly Color Gray50 = Color.FromRgb(0xF9, 0xFA, 0xFB);       // 背景
    public static readonly Color Gray100 = Color.FromRgb(0xF3, 0xF4, 0xF6);      // 悬停背景
    public static readonly Color Gray200 = Color.FromRgb(0xE5, 0xE7, 0xEB);      // 边框
    public static readonly Color Gray300 = Color.FromRgb(0xD1, 0xD5, 0xDB);      // 禁用边框
    public static readonly Color Gray400 = Color.FromRgb(0x9C, 0xA3, 0xAF);      // 占位文字
    public static readonly Color Gray500 = Color.FromRgb(0x6B, 0x72, 0x80);      // 次要文字
    public static readonly Color Gray600 = Color.FromRgb(0x4B, 0x55, 0x63);      // 正文
    public static readonly Color Gray700 = Color.FromRgb(0x37, 0x41, 0x51);      // 强正文
    public static readonly Color Gray900 = Color.FromRgb(0x11, 0x18, 0x27);      // 标题

    // 纯白 / 纯黑
    public static readonly Color White = Colors.White;
    public static readonly Color Black = Color.FromRgb(0x11, 0x18, 0x27);

    // 阴影
    public static readonly Color ShadowColor = Color.FromArgb(0x1A, 0x00, 0x00, 0x00);

    // 常用 Brush（按需创建，避免重复 new）
    public static readonly SolidColorBrush PrimaryBrush = new(Primary);
    public static readonly SolidColorBrush PrimaryDarkBrush = new(PrimaryDark);
    public static readonly SolidColorBrush SuccessBrush = new(Success);
    public static readonly SolidColorBrush SuccessDarkBrush = new(SuccessDark);
    public static readonly SolidColorBrush WarningBrush = new(Warning);
    public static readonly SolidColorBrush DangerBrush = new(Danger);
    public static readonly SolidColorBrush Gray50Brush = new(Gray50);
    public static readonly SolidColorBrush Gray100Brush = new(Gray100);
    public static readonly SolidColorBrush Gray200Brush = new(Gray200);
    public static readonly SolidColorBrush Gray400Brush = new(Gray400);
    public static readonly SolidColorBrush Gray500Brush = new(Gray500);
    public static readonly SolidColorBrush Gray600Brush = new(Gray600);
    public static readonly SolidColorBrush Gray700Brush = new(Gray700);
    public static readonly SolidColorBrush Gray900Brush = new(Gray900);
    public static readonly SolidColorBrush WhiteBrush = new(White);
    public static readonly SolidColorBrush TransparentBrush = Brushes.Transparent;

    // 背景/卡片/边框常用组合
    public static readonly SolidColorBrush CardBackgroundBrush = WhiteBrush;
    public static readonly SolidColorBrush PageBackgroundBrush = Gray50Brush;
    public static readonly SolidColorBrush CardBorderBrush = new(Color.FromArgb(0x40, 0xE5, 0xE7, 0xEB));
    public static readonly SolidColorBrush SubtleBorderBrush = new(Gray200);
    public static readonly SolidColorBrush InputBorderBrush = new(Gray200);
    public static readonly SolidColorBrush InputHoverBorderBrush = new(Gray400);

    static AppColors()
    {
        // 冻结常用 Brush，提升性能
        if (PrimaryBrush.CanFreeze) PrimaryBrush.Freeze();
        if (PrimaryDarkBrush.CanFreeze) PrimaryDarkBrush.Freeze();
        if (SuccessBrush.CanFreeze) SuccessBrush.Freeze();
        if (SuccessDarkBrush.CanFreeze) SuccessDarkBrush.Freeze();
        if (WarningBrush.CanFreeze) WarningBrush.Freeze();
        if (DangerBrush.CanFreeze) DangerBrush.Freeze();
        if (Gray50Brush.CanFreeze) Gray50Brush.Freeze();
        if (Gray100Brush.CanFreeze) Gray100Brush.Freeze();
        if (Gray200Brush.CanFreeze) Gray200Brush.Freeze();
        if (Gray400Brush.CanFreeze) Gray400Brush.Freeze();
        if (Gray500Brush.CanFreeze) Gray500Brush.Freeze();
        if (Gray600Brush.CanFreeze) Gray600Brush.Freeze();
        if (Gray700Brush.CanFreeze) Gray700Brush.Freeze();
        if (Gray900Brush.CanFreeze) Gray900Brush.Freeze();
        if (WhiteBrush.CanFreeze) WhiteBrush.Freeze();
        if (CardBorderBrush.CanFreeze) CardBorderBrush.Freeze();
        if (SubtleBorderBrush.CanFreeze) SubtleBorderBrush.Freeze();
        if (InputBorderBrush.CanFreeze) InputBorderBrush.Freeze();
        if (InputHoverBorderBrush.CanFreeze) InputHoverBorderBrush.Freeze();
    }
}
