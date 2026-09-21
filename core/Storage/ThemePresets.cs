// ThemePresets.cs —— 主题预置表（v2.0.6「主题引擎做全」的第一步）
//
// 本文件是原 QML 线 `src/toolbox/ui/theme.py` 里 `DARK` / `LIGHT` / `PRESETS` 的**逐字移植**：
//   · 颜色表 21 个令牌、预置的参数默认值，全部**照抄**，一个字符都不改
//     （`tests/ThemeFidelityTests.cs` 用**真实 Python** 读原文件逐字段比对，改错必红）；
//   · 5 个预置 = 深色 / 浅色 / 午夜蓝 / 葡萄紫 / 抹茶绿，后三个是"深色基线 + 少量覆盖"
//     （照 Python 的 `{**DARK, ...}` 写法）。
//
// ⚠️ 为什么整个搬到 core：它是**纯数据**（字符串 + 数字），不依赖任何 UI 框架，
//    放在 core 才能脱离界面做"令牌表同构 / 与原版逐字一致 / 参数夹取"这些单测。
//
// ⚠️ 与 `ThemeTokens` 的分工（别混）：
//   · 本文件 = **原版语义的调色板**（窗口 / 表面 / 文字 / 强调色 … 21 个令牌，颜色是 `#RRGGBB(AA)` 字符串）；
//   · `ThemeTokens.ThemePalette` = **WinUI 界面真正吃掉的那组令牌**（面板底 / 遮罩 / 圆角相关底色 …）。
//   两者靠 `ThemeResolver` 投影：预置决定色相，参数（不透明度 / 圆角）做调制。

namespace ToolboxPanel.Core.Storage;

/// <summary>
/// 一个主题预置：id + 中英标签 + 明暗归属 + 21 个颜色令牌 + 8 个参数默认值。
/// </summary>
public sealed record ThemePreset(
    string Id,
    string Zh,
    string En,
    bool IsDark,
    IReadOnlyDictionary<string, string> Colors,
    IReadOnlyDictionary<string, double> Params)
{
    /// <summary>该预置里某个参数的默认值（表中没写就回落 <see cref="ThemeParamSpecs"/> 的默认值）。</summary>
    public double ParamOrDefault(string key)
        => Params.TryGetValue(key, out var value) ? value : ThemeParamSpecs.Find(key)?.Default ?? 0;
}

/// <summary>主题预置表（原版 <c>theme.py</c> 的 <c>PRESETS</c>）。</summary>
public static class ThemePresets
{
    /// <summary>原版 <c>DEFAULT_PRESET</c>。</summary>
    public const string DefaultId = "dark";

    /// <summary>深色预置的 id（投影时的"定标基准"之一）。</summary>
    public const string DarkId = "dark";

    /// <summary>浅色预置的 id（投影时的"定标基准"之一）。</summary>
    public const string LightId = "light";

    /// <summary>"跟随系统"的线上值（`ui_theme`）—— 它不是预置，而是"由系统明暗挑深色/浅色预置"。</summary>
    public const string SystemId = "system";

    // ────────────────────────────── 颜色表（照抄 theme.py）──────────────────────────────

    /// <summary>深色基线（原版 <c>theme.py::DARK</c>）。</summary>
    public static readonly IReadOnlyDictionary<string, string> DarkColors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["window"] = "#14161f",
        ["base"] = "#1b1e29",
        ["alt_base"] = "#222634",
        ["text"] = "#f2f4f8",
        ["text_dim"] = "#9aa3b2",
        ["text_hint"] = "#6b7385",
        ["border"] = "#33ffffff",
        ["border_soft"] = "#1fffffff",
        ["accent"] = "#4c8bf5",
        ["accent_hover"] = "#6ba1ff",
        ["accent_pressed"] = "#3a72d4",
        ["on_accent"] = "#0d0f16",
        ["highlight"] = "#4c8bf5",
        ["highlighted_text"] = "#0d0f16",
        ["card"] = "#ffffff",
        ["card_hover"] = "#ffffff",
        ["glow_1"] = "#3a4c8bf5",
        ["glow_2"] = "#3a7c6bf0",
        ["danger"] = "#e04b4b",
        ["success"] = "#57c08a",
        ["warning"] = "#f2b24c",
    };

    /// <summary>浅色基线（原版 <c>theme.py::LIGHT</c>）。</summary>
    public static readonly IReadOnlyDictionary<string, string> LightColors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["window"] = "#eef0f5",
        ["base"] = "#ffffff",
        ["alt_base"] = "#f5f7fa",
        ["text"] = "#1b1e29",
        ["text_dim"] = "#5a6274",
        ["text_hint"] = "#8b93a5",
        ["border"] = "#33000000",
        ["border_soft"] = "#1a000000",
        ["accent"] = "#2f6fe4",
        ["accent_hover"] = "#4a86f0",
        ["accent_pressed"] = "#1f56bd",
        ["on_accent"] = "#ffffff",
        ["highlight"] = "#2f6fe4",
        ["highlighted_text"] = "#ffffff",
        ["card"] = "#000000",
        ["card_hover"] = "#000000",
        ["glow_1"] = "#334c8bf5",
        ["glow_2"] = "#337c6bf0",
        ["danger"] = "#d63c3c",
        ["success"] = "#3a9e6c",
        ["warning"] = "#cf8f2a",
    };

    /// <summary>颜色令牌名（由深/浅两张基线推导，保证完备；与原版 <c>_COLOR_KEYS</c> 同一套）。</summary>
    public static readonly IReadOnlyList<string> ColorKeys =
        DarkColors.Keys.Union(LightColors.Keys, StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

    /// <summary>这个键是不是合法的颜色令牌（逐令牌覆盖只认这些）。</summary>
    public static bool IsColorKey(string? key)
        => !string.IsNullOrEmpty(key) && DarkColors.ContainsKey(key);

    // ────────────────────────────── 5 个预置 ──────────────────────────────

    public static readonly ThemePreset Dark = new(
        "dark", "深色", "Dark", IsDark: true,
        DarkColors,
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["sheet_opacity"] = 0.62,
            ["window_opacity"] = 0.42,
            ["card_opacity"] = 0.16,
            ["card_hover_opacity"] = 0.26,
            ["blur_radius"] = 26.0,
            ["radius"] = 16.0,
            ["anim_ms"] = 260.0,
            ["hover_ms"] = 160.0,
        });

    public static readonly ThemePreset Light = new(
        "light", "浅色", "Light", IsDark: false,
        LightColors,
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["sheet_opacity"] = 0.62,
            ["window_opacity"] = 0.58,
            ["card_opacity"] = 0.10,
            ["card_hover_opacity"] = 0.18,
            ["blur_radius"] = 26.0,
            ["radius"] = 16.0,
            ["anim_ms"] = 260.0,
            ["hover_ms"] = 160.0,
        });

    /// <summary>午夜蓝 = 深色基线 + 窗口/表面/强调色/光斑覆盖（照抄原版）。</summary>
    public static readonly ThemePreset Midnight = new(
        "midnight", "午夜蓝", "Midnight", IsDark: true,
        Overlay(DarkColors,
            ("window", "#0b1020"), ("base", "#111830"), ("alt_base", "#16203c"),
            ("accent", "#5b8def"), ("accent_hover", "#7aa5f5"),
            ("glow_1", "#3a5b8def"), ("glow_2", "#3a3f6fd8")),
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["sheet_opacity"] = 0.62,
            ["window_opacity"] = 0.44,
            ["card_opacity"] = 0.18,
            ["card_hover_opacity"] = 0.28,
            ["blur_radius"] = 32.0,
            ["radius"] = 18.0,
            ["anim_ms"] = 300.0,
            ["hover_ms"] = 180.0,
        });

    /// <summary>葡萄紫 = 深色基线 + 覆盖（照抄原版）。</summary>
    public static readonly ThemePreset Grape = new(
        "grape", "葡萄紫", "Grape", IsDark: true,
        Overlay(DarkColors,
            ("window", "#150f22"), ("base", "#1d142e"), ("alt_base", "#251a3a"),
            ("accent", "#a06bf0"), ("accent_hover", "#b78bf7"), ("accent_pressed", "#8450d6"),
            ("glow_1", "#3aa06bf0"), ("glow_2", "#3ae45c8a")),
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["sheet_opacity"] = 0.62,
            ["window_opacity"] = 0.42,
            ["card_opacity"] = 0.18,
            ["card_hover_opacity"] = 0.28,
            ["blur_radius"] = 30.0,
            ["radius"] = 20.0,
            ["anim_ms"] = 280.0,
            ["hover_ms"] = 170.0,
        });

    /// <summary>抹茶绿 = 深色基线 + 覆盖（照抄原版）。</summary>
    public static readonly ThemePreset Matcha = new(
        "matcha", "抹茶绿", "Matcha", IsDark: true,
        Overlay(DarkColors,
            ("window", "#0f1a14"), ("base", "#142219"), ("alt_base", "#1b2c21"),
            ("accent", "#4fb07a"), ("accent_hover", "#6bc794"), ("accent_pressed", "#3d8f61"),
            ("glow_1", "#3a4fb07a"), ("glow_2", "#3ac9a24b")),
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["sheet_opacity"] = 0.62,
            ["window_opacity"] = 0.46,
            ["card_opacity"] = 0.16,
            ["card_hover_opacity"] = 0.26,
            ["blur_radius"] = 26.0,
            ["radius"] = 16.0,
            ["anim_ms"] = 260.0,
            ["hover_ms"] = 160.0,
        });

    /// <summary>全部预置（**顺序即界面上的顺序**，与原版 `preset_names()` 一致）。</summary>
    public static readonly IReadOnlyList<ThemePreset> All = new[] { Dark, Light, Midnight, Grape, Matcha };

    /// <summary>预置 id 列表（不含 "system"）。</summary>
    public static readonly IReadOnlyList<string> Ids = All.Select(p => p.Id).ToArray();

    /// <summary>按 id 找预置（**不区分大小写**，与项目其它"线名解析"一致）；找不到返回 null。</summary>
    public static ThemePreset? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var trimmed = id.Trim();
        foreach (var preset in All)
        {
            if (string.Equals(preset.Id, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return preset;
            }
        }

        return null;
    }

    /// <summary>归一化：认不出的 id 一律回落 <see cref="DefaultId"/>（与原版"非法值归一化"一致）。</summary>
    public static string Normalize(string? id) => Find(id)?.Id ?? DefaultId;

    /// <summary>复制一份基线并覆盖若干令牌（对应 Python 的 <c>{**DARK, ...}</c>）。</summary>
    private static Dictionary<string, string> Overlay(
        IReadOnlyDictionary<string, string> baseline,
        params (string Key, string Value)[] overrides)
    {
        var copy = new Dictionary<string, string>(baseline, StringComparer.Ordinal);
        foreach (var (key, value) in overrides)
        {
            copy[key] = value;
        }

        return copy;
    }
}
