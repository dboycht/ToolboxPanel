// ThemeTokens.cs —— 主题令牌（W4 的第一步：**最小全局主题**）
//
// 为什么放在 core 而不是 winui：
//   · 令牌就是一组语义色值，**不依赖任何 UI 框架**，放在这里可以单测（浅/深两张表必须同构、
//     取值必须在合法范围、未知模式必须回落）；
//   · winui 侧只负责"把令牌搬到界面资源上 + 跟随系统主题"。
//
// ⚠️ 与老线（`src/toolbox/themes.py`，QML 版 5 预置 / 8 参数）的关系：
//   这里先做**最小可用的一套**（浅色 / 深色 + 跟随系统），令牌**语义命名与老线对齐**，
//   后续 W4 扩展成多预置 / 参数细调 / 逐令牌覆盖时，从这里往上长即可。
//
// ⚠️ 命名约定（别随手加）：
//   · `*Surface*`   —— 表面底色（窗口 / 卡片 / 浮层）
//   · `*Text*`      —— 文字与图标前景
//   · `*Divider`    —— 分隔线 / 描边
//   · `*Accent`     —— 强调色（选中条、拖放落点指示线）

namespace ToolboxPanel.Core.Storage;

/// <summary>界面主题模式（`config.json` 的 <c>ui_theme</c> 字段）。</summary>
public enum ThemeMode
{
    /// <summary>跟随系统（默认）。</summary>
    System,

    /// <summary>强制浅色。</summary>
    Light,

    /// <summary>强制深色。</summary>
    Dark,
}

/// <summary>
/// 一套主题的颜色令牌。
///
/// <para>用 <c>byte</c> 四元组表示 ARGB，**刻意不引用 <c>Windows.UI.Color</c>**：
/// core 是纯数据层，引了 UI 类型就没法脱离界面单测了。</para>
/// </summary>
public sealed record ThemePalette
{
    // ── 窗口与表面 ──

    /// <summary>窗口兜底底色（**只在材质不生效时才用**；材质生效时窗口必须完全透明，否则会盖住玻璃）。</summary>
    public required (byte A, byte R, byte G, byte B) WindowFallback { get; init; }

    /// <summary>飘在玻璃上的表面（设置面板）。带 alpha ⇒ 让背后的材质透出来。</summary>
    public required (byte A, byte R, byte G, byte B) PanelSurface { get; init; }

    /// <summary>面板的左描边。</summary>
    public required (byte A, byte R, byte G, byte B) PanelBorder { get; init; }

    /// <summary>模态遮罩（设置面板打开时压暗主界面）。</summary>
    public required (byte A, byte R, byte G, byte B) Overlay { get; init; }

    /// <summary>状态栏底（很淡的一层）。</summary>
    public required (byte A, byte R, byte G, byte B) StatusSurface { get; init; }

    /// <summary>标签栏外框底。</summary>
    public required (byte A, byte R, byte G, byte B) TabStripSurface { get; init; }

    /// <summary>标签栏外框描边。</summary>
    public required (byte A, byte R, byte G, byte B) TabStripBorder { get; init; }

    /// <summary>面板里分组块的底（比面板底再淡一点）。</summary>
    public required (byte A, byte R, byte G, byte B) CardSurface { get; init; }

    // ── 交互态（标签项 / 按钮的悬停与按下）──

    public required (byte A, byte R, byte G, byte B) HoverSurface { get; init; }

    public required (byte A, byte R, byte G, byte B) PressedSurface { get; init; }

    public required (byte A, byte R, byte G, byte B) SelectedSurface { get; init; }

    // ── 强调与线条 ──

    public required (byte A, byte R, byte G, byte B) Accent { get; init; }

    public required (byte A, byte R, byte G, byte B) Divider { get; init; }

    // ── 标题栏按钮（系统绘制的那三个按钮）──

    public required (byte A, byte R, byte G, byte B) TitleBarButtonForeground { get; init; }

    public required (byte A, byte R, byte G, byte B) TitleBarButtonInactiveForeground { get; init; }

    public required (byte A, byte R, byte G, byte B) TitleBarButtonHoverBackground { get; init; }

    public required (byte A, byte R, byte G, byte B) TitleBarButtonPressedBackground { get; init; }

    /// <summary>是否深色（决定系统控件的 <c>RequestedTheme</c>）。</summary>
    public required bool IsDark { get; init; }
}

/// <summary>主题令牌表：深色 / 浅色两张，**必须同构**（有单测守着）。</summary>
public static class ThemeTokens
{
    public const string SystemWire = "system";
    public const string LightWire = "light";
    public const string DarkWire = "dark";

    public static readonly string[] AllModes = { SystemWire, LightWire, DarkWire };

    /// <summary>
    /// 深色（默认）。观感与原 W3 的硬编码值保持一致 —— 换主题引擎不该顺手改外观。
    /// </summary>
    public static readonly ThemePalette Dark = new()
    {
        IsDark = true,
        WindowFallback = (255, 32, 32, 37),          // 与原来 ApplyBackdrop 的兜底色一致
        // ⚠️ 2026-09-21 起**不透明**（用户 2026-09-21：「这个默认主题你可以不用半透明的」）：
        //    设置面板是一块实心表面 ⇒ 不再依赖"窗口背后是什么"，也就不会再出现"面板整块发黑"
        //    （ERROR.md E43：元素级 Acrylic 的合成层在用户机器上会画成黑的）。
        //    想要玻璃观的话，「外观微调 → 抽屉不透明度」可以把它调透（0.20~0.62 映射 82~255）。
        PanelSurface = (255, 27, 27, 31),
        PanelBorder = (36, 255, 255, 255),           // 原来是 #1FFFFFFF
        Overlay = (89, 0, 0, 0),                     // 原来是 #59000000
        StatusSurface = (38, 0, 0, 0),               // 原来是 #26000000
        TabStripSurface = (20, 255, 255, 255),       // 原来是 #14FFFFFF
        TabStripBorder = (26, 255, 255, 255),        // 原来是 #1AFFFFFF
        CardSurface = (14, 255, 255, 255),
        HoverSurface = (20, 255, 255, 255),
        PressedSurface = (34, 255, 255, 255),
        SelectedSurface = (41, 255, 255, 255),
        Accent = (255, 108, 198, 255),               // 原来是 #FF6CC6FF
        Divider = (26, 255, 255, 255),
        TitleBarButtonForeground = (255, 255, 255, 255),
        TitleBarButtonInactiveForeground = (255, 160, 160, 160),
        TitleBarButtonHoverBackground = (40, 255, 255, 255),
        TitleBarButtonPressedBackground = (60, 255, 255, 255),
    };

    /// <summary>浅色（对着 Mica 的浅色底，用"淡黑"表面而不是"淡白"）。</summary>
    public static readonly ThemePalette Light = new()
    {
        IsDark = false,
        WindowFallback = (255, 243, 243, 243),
        PanelSurface = (255, 249, 249, 249),         // 同深色：不透明的实心面板（见上一条说明）
        PanelBorder = (36, 0, 0, 0),
        Overlay = (68, 0, 0, 0),
        StatusSurface = (30, 0, 0, 0),
        TabStripSurface = (14, 0, 0, 0),
        TabStripBorder = (22, 0, 0, 0),
        CardSurface = (12, 0, 0, 0),
        HoverSurface = (18, 0, 0, 0),
        PressedSurface = (30, 0, 0, 0),
        SelectedSurface = (36, 0, 0, 0),
        Accent = (255, 0, 95, 184),                  // 同一族蓝，浅色底上压深一点才看得清
        Divider = (24, 0, 0, 0),
        TitleBarButtonForeground = (255, 0, 0, 0),
        TitleBarButtonInactiveForeground = (255, 128, 128, 128),
        TitleBarButtonHoverBackground = (30, 0, 0, 0),
        TitleBarButtonPressedBackground = (48, 0, 0, 0),
    };

    /// <summary>按主题模式取令牌；<see cref="ThemeMode.System"/> 用调用方给的"系统当前是否深色"决定。</summary>
    public static ThemePalette Resolve(ThemeMode mode, bool systemIsDark) => mode switch
    {
        ThemeMode.Light => Light,
        ThemeMode.Dark => Dark,
        _ => systemIsDark ? Dark : Light,
    };

    /// <summary>解析 `config.json` 里的字符串（未知值回落 System，与原版"非法值归一化"一致）。</summary>
    public static ThemeMode ParseMode(string? wire)
    {
        var value = wire?.Trim().ToLowerInvariant();
        return value switch
        {
            LightWire or "lightmode" => ThemeMode.Light,
            DarkWire or "darkmode" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    public static string ToWire(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => LightWire,
        ThemeMode.Dark => DarkWire,
        _ => SystemWire,
    };
}
