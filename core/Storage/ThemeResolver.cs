// ThemeResolver.cs —— 主题解析（v2.0.6「主题引擎做全」的核心）
//
// 三层叠加，后层覆盖前层（与原版 `theme.py::_resolve` 完全同序）：
//
//     预置颜色/参数  →  参数覆盖（theme_overrides 的 `param:<key>`）  →  逐令牌颜色覆盖
//
// 外加本项目自己的两条（原版没有的）规则，都在这里写清楚：
//
//   ① **明暗由预置决定**：`ui_theme` 里的 `light`/`dark` 只是"锁定明暗"，真正决定控件主题与
//      调色板的是**生效预置**（`midnight`/`grape`/`matcha` 都是深色系 —— 与原版一致）。
//      这样就不会出现"浅色底 + 深色控件"这种自相矛盾的组合。
//   ② **"跟随系统"是一种选择项**（`ui_theme=system`）：它不改变用户选的预置，
//      只是本次运行时按系统明暗挑 `dark` / `light` 预置。
//
// ⚠️ 保真纪律：本文件**只做投影与调制**，不改预置表里任何一个颜色字面量；
//    `tests/ThemeFidelityTests.cs` 会拿**真实 Python** 跑原版 `theme.py` 逐字段核对。

namespace ToolboxPanel.Core.Storage;

/// <summary>一次主题解析的输入（由 <see cref="AppSettings"/> 或临时开关构造）。</summary>
/// <param name="Mode">`ui_theme`：跟随系统 / 锁定浅色 / 锁定深色。</param>
/// <param name="PresetId">`theme`：预置 id（dark|light|midnight|grape|matcha）。</param>
/// <param name="ColorOverrides">逐令牌颜色覆盖（21 个令牌之一 → `#RRGGBB`）。</param>
/// <param name="ParamOverrides">参数覆盖（8 个参数之一 → 数值）。</param>
/// <param name="AnimationDurationMs">本项目「动效」节里的时长；给了就以它为准（见 anim_ms 的说明）。</param>
public sealed record ThemeRequest(
    ThemeMode Mode,
    string? PresetId,
    IReadOnlyDictionary<string, string>? ColorOverrides = null,
    IReadOnlyDictionary<string, double>? ParamOverrides = null,
    int? AnimationDurationMs = null);

/// <summary>解析结果：生效预置 + 最终令牌 + 最终参数。</summary>
public sealed record ThemeResolution(
    string PresetId,
    ThemePreset Preset,
    bool IsDark,
    ThemePalette Palette,
    ThemeParams Params,
    IReadOnlyDictionary<string, string> Colors)
{
    /// <summary>按语言取预置标签（界面上的"预置主题"一节用它）。</summary>
    public string Label(string? language) => Preset.Label(language);

    /// <summary>把一个设计圆角按当前 `radius` 参数缩放（默认参数下等于不变）。</summary>
    public double ScaleCornerRadius(double designCornerRadius) => Params.ScaleCornerRadius(designCornerRadius);

    /// <summary>悬停时长倍率（默认参数下 = 1.0）。</summary>
    public double HoverScale => Params.HoverScale;
}

/// <summary>主题解析器（纯函数，可单测）。</summary>
public static class ThemeResolver
{
    /// <summary>
    /// 材质回退（兜底纯色）时的**可读下限**：兜底底色再透也不低于这个不透明度。
    ///
    /// <para>为什么需要它：`window_opacity` 在原版里控制"玻璃色调对模糊层的遮盖程度"，
    /// 而 WinUI 的玻璃浓度由系统材质决定、我们不掌握那个模糊层（2026-09-20 与用户确认）——
    /// 所以本项目的落点是**材质不可用时的窗口兜底底色**（材质生效时该值不参与显示，
    /// 现有玻璃观感一丝不变）。既然那是"没有模糊可依"的场景，底色就必须够实心，
    /// 否则字会糊在桌面背景上。</para>
    /// </summary>
    public const double WindowFallbackMinAlpha = 0.80;

    /// <summary>解析主题；<paramref name="systemIsDark"/> = 当前系统是不是深色（仅在"跟随系统"时用到）。</summary>
    public static ThemeResolution Resolve(ThemeRequest request, bool systemIsDark)
    {
        ArgumentNullException.ThrowIfNull(request);

        var preset = ResolvePreset(request, systemIsDark);
        var colors = ResolveColors(preset, request.ColorOverrides);
        var parameters = ResolveParams(preset, request.ParamOverrides, request.AnimationDurationMs);
        var palette = Project(preset, colors, parameters);

        return new ThemeResolution(preset.Id, preset, preset.IsDark, palette, parameters, colors);
    }

    /// <summary>便捷入口：直接按设置解析。</summary>
    public static ThemeResolution Resolve(AppSettings settings, bool systemIsDark)
        => Resolve(FromSettings(settings), systemIsDark);

    /// <summary>把设置翻译成解析请求（**唯一**一处从设置读主题相关字段的地方）。</summary>
    public static ThemeRequest FromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ThemeRequest(
            settings.UiTheme,
            settings.Theme,
            settings.ThemeColorOverrides(),
            settings.ThemeParamOverrides(),
            settings.AnimationDurationMs);
    }

    // ────────────────────────────── 生效预置 ──────────────────────────────

    /// <summary>
    /// 挑出真正生效的预置：
    /// <list type="bullet">
    ///   <item>跟随系统 ⇒ 系统深色用 <c>dark</c>、系统浅色用 <c>light</c>（**不**用用户选的彩色预置）；</item>
    ///   <item>锁定浅/深 ⇒ 用用户选的预置；预置名认不出时回落该明暗的中性预置。</item>
    /// </list>
    /// </summary>
    public static ThemePreset ResolvePreset(ThemeRequest request, bool systemIsDark)
    {
        if (request.Mode == ThemeMode.System)
        {
            return systemIsDark ? ThemePresets.Dark : ThemePresets.Light;
        }

        return ThemePresets.Find(request.PresetId)
            ?? (request.Mode == ThemeMode.Light ? ThemePresets.Light : ThemePresets.Dark);
    }

    // ────────────────────────────── 颜色：预置 + 逐令牌覆盖 ──────────────────────────────

    /// <summary>预置颜色 + 逐令牌覆盖（只接受已知令牌 + 非空颜色串，未知键忽略 —— 照原版）。</summary>
    public static IReadOnlyDictionary<string, string> ResolveColors(
        ThemePreset preset, IReadOnlyDictionary<string, string>? overrides)
    {
        var colors = new Dictionary<string, string>(preset.Colors, StringComparer.Ordinal);

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                if (ThemePresets.IsColorKey(key) && !string.IsNullOrWhiteSpace(value))
                {
                    colors[key] = value.Trim();
                }
            }
        }

        return colors;
    }

    // ────────────────────────────── 参数：默认 → 预置 → 覆盖 ──────────────────────────────

    /// <summary>
    /// 解析 8 个参数：规格默认 → 预置默认 → 用户覆盖 → 「动效」节的时长（anim_ms）。
    ///
    /// <para>⚠️ anim_ms 的优先级最高那一档是**本项目动效设置**：2026-09-20 与用户确认
    /// "动效时长只留一个值、不做两个地方调同一个东西" ⇒ 界面上的时长滑杆写的就是它。
    /// 原版写在 `theme_overrides` 里的 `param:anim_ms` 仍然认（旧配置不丢），只是会被设置值压过。</para>
    /// </summary>
    public static ThemeParams ResolveParams(
        ThemePreset preset,
        IReadOnlyDictionary<string, double>? overrides,
        int? animationDurationMs = null)
    {
        var parameters = ThemeParamSpecs.Defaults().With(preset.Params).With(overrides);

        if (animationDurationMs is { } duration)
        {
            parameters = parameters.With(ThemeParamSpecs.AnimMsKey, duration);
        }

        return parameters;
    }

    // ────────────────────────────── 投影：原版令牌 → WinUI 调色板 ──────────────────────────────

    /// <summary>
    /// 把"原版语义的 21 个令牌 + 8 个参数"投影成 WinUI 界面真正吃掉的那组令牌。
    ///
    /// <para>三条规则（都有单测钉住）：</para>
    /// <list type="number">
    ///   <item><b>两个中性预置（深色/浅色）原样使用既有调色板</b> ——
    ///     "换了主题引擎不该顺手改外观"（`ThemeTokens.Dark/Light` 是 W4 定标过的值）；</item>
    ///   <item><b>彩色预置</b>在深色调色板的基础上只换四处**色相**：
    ///     窗口兜底色 ← `window`、面板表面 ← `base`、强调色 ← `accent`、系统窗按钮前景 ← `text`/`text_dim`
    ///     （其余淡层保持中性，避免"彩色预置在浅色系下花掉"）；</item>
    ///   <item><b>参数按"倍率"调制**alpha**</b>（相对预置自身的默认值）——
    ///     这样默认参数下界面与现在逐像素一致，拖动滑杆才是"在这个预置的基础上增减"。</item>
    /// </list>
    /// </summary>
    public static ThemePalette Project(
        ThemePreset preset, IReadOnlyDictionary<string, string> colors, ThemeParams parameters)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(colors);

        var baseline = preset.IsDark ? ThemeTokens.Dark : ThemeTokens.Light;

        // 两个中性预置是"定标基准"（他们的调色板就是界面上现在的样子）；其余预置才需要换色相。
        bool tinted = preset.Id is not (ThemePresets.DarkId or ThemePresets.LightId);

        var palette = baseline;

        if (tinted)
        {
            palette = palette with
            {
                // 色相取自预置，alpha 保持既有设计值（透明度由参数那一层单独管）
                WindowFallback = Recolor(baseline.WindowFallback, colors.GetValueOrDefault("window")),
                PanelSurface = Recolor(baseline.PanelSurface, colors.GetValueOrDefault("base")),
                Accent = Recolor(baseline.Accent, colors.GetValueOrDefault("accent"), forceOpaque: true),
                TitleBarButtonForeground = Recolor(
                    baseline.TitleBarButtonForeground, colors.GetValueOrDefault("text"), forceOpaque: true),
                TitleBarButtonInactiveForeground = Recolor(
                    baseline.TitleBarButtonInactiveForeground, colors.GetValueOrDefault("text_dim"), forceOpaque: true),
            };
        }

        // ── 参数调制 ──
        double sheetRatio = Ratio(parameters.SheetOpacity, ThemeParamSpecs.SheetOpacityKey, preset);
        double cardRatio = Ratio(parameters.CardOpacity, ThemeParamSpecs.CardOpacityKey, preset);
        double hoverRatio = Ratio(parameters.CardHoverOpacity, ThemeParamSpecs.CardHoverOpacityKey, preset);
        double windowRatio = Ratio(parameters.WindowOpacity, ThemeParamSpecs.WindowOpacityKey, preset);

        var fallbackAlpha = ScaleAlphaAbsolute(
            baseline.WindowFallback.A, WindowFallbackMinAlpha + (1 - WindowFallbackMinAlpha) * windowRatio);

        return palette with
        {
            WindowFallback = WithAlpha(palette.WindowFallback, fallbackAlpha),

            // 抽屉（设置面板玻璃）—— 那一层 AcrylicBrush 的 tint alpha 就是它
            PanelSurface = WithAlpha(palette.PanelSurface, ScaleAlpha(palette.PanelSurface.A, sheetRatio)),

            // 卡片类表面：面板分组块 + 标签栏外框底
            CardSurface = WithAlpha(palette.CardSurface, ScaleAlpha(palette.CardSurface.A, cardRatio)),
            TabStripSurface = WithAlpha(palette.TabStripSurface, ScaleAlpha(palette.TabStripSurface.A, cardRatio)),

            // 悬停/按下
            HoverSurface = WithAlpha(palette.HoverSurface, ScaleAlpha(palette.HoverSurface.A, hoverRatio)),
            PressedSurface = WithAlpha(palette.PressedSurface, ScaleAlpha(palette.PressedSurface.A, hoverRatio)),
        };
    }

    // ────────────────────────────── 小工具 ──────────────────────────────

    /// <summary>参数相对**该预置自身默认值**的倍率（默认参数 ⇒ 1.0 ⇒ 调制是恒等变换）。</summary>
    private static double Ratio(double value, string key, ThemePreset preset)
    {
        var reference = preset.ParamOrDefault(key);
        if (!double.IsFinite(reference) || reference <= 0)
        {
            return 1;
        }

        var ratio = value / reference;
        return double.IsFinite(ratio) && ratio >= 0 ? ratio : 1;
    }

    /// <summary>把 alpha 乘一个倍率（0 保持 0；结果夹到 0~255）。</summary>
    private static byte ScaleAlpha(byte alpha, double ratio)
    {
        if (alpha == 0)
        {
            return 0;   // "完全透明"是一个有意义的取值，不该被倍率放大
        }

        return ClampAlpha(alpha * ratio);
    }

    /// <summary>按"0~1 的比例"直接定 alpha（用于窗口兜底：先算出比例，再夹到可读下限）。</summary>
    private static byte ScaleAlphaAbsolute(byte designAlpha, double fraction)
    {
        var alpha = designAlpha * Math.Clamp(fraction, 0, 1);
        var floor = 255 * WindowFallbackMinAlpha;
        return ClampAlpha(Math.Max(alpha, Math.Min(floor, designAlpha)));
    }

    private static byte ClampAlpha(double alpha)
        => (byte)Math.Clamp(Math.Round(alpha, MidpointRounding.AwayFromZero), 0, 255);

    private static (byte A, byte R, byte G, byte B) WithAlpha((byte A, byte R, byte G, byte B) color, byte alpha)
        => (alpha, color.R, color.G, color.B);

    /// <summary>换掉色相、保留原来的 alpha（<paramref name="forceOpaque"/> 时不透明）。颜色串解析不了就原样返回。</summary>
    private static (byte A, byte R, byte G, byte B) Recolor(
        (byte A, byte R, byte G, byte B) baseline, string? color, bool forceOpaque = false)
    {
        if (!TryParseColor(color, out var parsed))
        {
            return baseline;
        }

        return forceOpaque
            ? ((byte)255, parsed.R, parsed.G, parsed.B)
            : (baseline.A, parsed.R, parsed.G, parsed.B);
    }

    /// <summary>
    /// 解析原版颜色串：`#RRGGBB` 或 `#AARRGGBB`（原版两种都写；不认的返回 false）。
    /// </summary>
    public static bool TryParseColor(string? color, out (byte A, byte R, byte G, byte B) value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(color))
        {
            return false;
        }

        var text = color.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length == 6)
        {
            text = "ff" + text;   // 六位 = 不透明
        }

        if (text.Length != 8 || !uint.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var packed))
        {
            return false;
        }

        value = (
            (byte)((packed >> 24) & 0xFF),
            (byte)((packed >> 16) & 0xFF),
            (byte)((packed >> 8) & 0xFF),
            (byte)(packed & 0xFF));
        return true;
    }
}
