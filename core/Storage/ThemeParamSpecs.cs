// ThemeParamSpecs.cs —— 主题的 8 个可调参数（原版 `theme.py::PARAM_SPECS` 的逐字移植）
//
// 每个参数 = key + 默认值 + 范围 + 步长 + 中英标签。原版的注释也一并搬过来了，
// 因为那几条"默认值为什么取这个数"的讲究是实测结论，不是随手写的：
//
//   ⚠️ window_opacity 默认值取值讲究（**实测定标，别随意调高**）：
//   它控制「玻璃色调对 DWM 模糊层」的遮盖程度：
//     1.00 -> 完全不透明，毛玻璃彻底看不见
//     0.82 -> 桌面只透出约 12%，暗色桌面下肉眼几乎察觉不到
//     0.34 -> 毛玻璃非常明显
//     >0.6 -> 开始明显盖住模糊，观感退回「实心板」
//
// ⚠️ 本文件只描述**参数本身**；"它在 WinUI 线怎么落地"由 `ThemeResolver` 与 UI 负责，
//    两者不一致的地方都在 `ThemeResolver` 里逐条写明（例如 `blur_radius` 在本项目里不可调）。

namespace ToolboxPanel.Core.Storage;

/// <summary>
/// 一个可调参数的元数据（照原版 <c>PARAM_SPECS</c> 的元组：默认 / 最小 / 最大 / 步长 / 中文 / 英文）。
/// </summary>
/// <param name="Key">线上的参数名（也是 `theme_overrides` 里 `param:&lt;key&gt;` 的那个 key）。</param>
/// <param name="Default">原版默认值（**不是**"界面初始值"：界面初始值来自当前预置）。</param>
/// <param name="Min">原版下界。</param>
/// <param name="Max">原版上界。</param>
/// <param name="Step">滑杆步长。</param>
/// <param name="Zh">中文标签（原版 <c>paramSpecs</c> 就是这样给界面用的）。</param>
/// <param name="En">英文标签。</param>
/// <param name="Adjustable">是否出现在设置面板的「外观微调」里（见各参数的说明）。</param>
/// <param name="UiMinOverride">界面上实际可拖的下界（null = 用 <see cref="Min"/>）。</param>
/// <param name="UiMaxOverride">界面上实际可拖的上界（null = 用 <see cref="Max"/>）。</param>
public sealed record ThemeParamSpec(
    string Key,
    double Default,
    double Min,
    double Max,
    double Step,
    string Zh,
    string En,
    bool Adjustable = true,
    double? UiMinOverride = null,
    double? UiMaxOverride = null)
{
    /// <summary>界面滑杆的下界。</summary>
    public double UiMin => UiMinOverride ?? Min;

    /// <summary>界面滑杆的上界。</summary>
    public double UiMax => UiMaxOverride ?? Max;

    /// <summary>按语言取标签（原版就是按 <c>language</c> 从 PARAM_SPECS 里挑一列）。</summary>
    public string Label(string? language)
        => string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? En : Zh;

    /// <summary>夹到合法区间（NaN / 无穷一律回落默认值 —— 配置文件可能是手改的）。</summary>
    public double Clamp(double value)
        => double.IsFinite(value) ? Math.Clamp(value, Min, Max) : Default;

    /// <summary>夹到"界面上可拖的区间"（比 <see cref="Clamp"/> 更窄，用于滑杆）。</summary>
    public double ClampUi(double value)
        => double.IsFinite(value) ? Math.Clamp(value, UiMin, UiMax) : Default;
}

/// <summary>8 个参数的规格表（顺序 = 原版 <c>PARAM_SPECS</c> 的顺序，界面也照这个顺序铺）。</summary>
public static class ThemeParamSpecs
{
    public const string WindowOpacityKey = "window_opacity";
    public const string CardOpacityKey = "card_opacity";
    public const string CardHoverOpacityKey = "card_hover_opacity";
    public const string BlurRadiusKey = "blur_radius";
    public const string RadiusKey = "radius";
    public const string AnimMsKey = "anim_ms";
    public const string HoverMsKey = "hover_ms";
    public const string SheetOpacityKey = "sheet_opacity";

    /// <summary>`theme_overrides` 里数字参数的键前缀（原版 <c>setParam</c> 写的格式）。</summary>
    public const string OverridePrefix = "param:";

    /// <summary>圆角参数的基准值（原版默认 16，对应本项目的"设计圆角"）。</summary>
    public const double RadiusDesignDefault = 16.0;

    /// <summary>悬停时长的基准值（原版默认 160ms，对应本项目的"设计悬停时长"）。</summary>
    public const double HoverDesignDefault = 160.0;

    public static readonly IReadOnlyList<ThemeParamSpec> All = new[]
    {
        new ThemeParamSpec(WindowOpacityKey, 0.42, 0.20, 1.00, 0.02, "窗口不透明度", "Window opacity"),
        new ThemeParamSpec(CardOpacityKey, 0.16, 0.00, 0.60, 0.02, "卡片不透明度", "Card opacity"),
        new ThemeParamSpec(CardHoverOpacityKey, 0.26, 0.00, 0.80, 0.02, "卡片悬停", "Card hover"),

        // ⚠️ blur_radius 在 WinUI 线**不进界面**：Mica / Acrylic 的模糊半径由系统材质决定，
        //    我们不掌握那个模糊层，也就没有可调的地方（硬要"假装能调"只会骗用户）。
        //    它的数值仍然照原版保留在表里（保真测试盯着），界面上只在提示里说明一句。
        new ThemeParamSpec(BlurRadiusKey, 26.0, 0.0, 64.0, 1.0, "模糊强度", "Blur radius", Adjustable: false),

        new ThemeParamSpec(RadiusKey, 16.0, 0.0, 32.0, 1.0, "圆角", "Corner radius"),

        // ⚠️ anim_ms 也**不进「外观微调」**：本项目「动效」一节已经有一个"动效时长"滑杆，
        //    两者是同一个值（2026-09-20 与用户确认：合并成一个，不搞两个地方调同一个东西）。
        //    它的解析优先级见 `ThemeResolver`：动效设置 > theme_overrides 里旧版写过的值 > 预置默认。
        new ThemeParamSpec(AnimMsKey, 260.0, 0.0, 800.0, 10.0, "动效时长(ms)", "Animation (ms)", Adjustable: false),

        new ThemeParamSpec(HoverMsKey, 160.0, 0.0, 600.0, 10.0, "悬停时长(ms)", "Hover (ms)"),

        // 抽屉（设置面板）玻璃色调：旧版写死 0.97 近实心，是"整体看着不透明"的主因之一
        // ⚠️ 界面区间只到**预置默认值 0.62**（= 不透明面板）：0.62 以上没有意义了（面板本来就实心），
        //    这么定让滑杆的整条行程都在"从实心到更透"的有效范围内。
        new ThemeParamSpec(SheetOpacityKey, 0.62, 0.20, 1.00, 0.02, "抽屉不透明度", "Panel opacity",
            UiMaxOverride: 0.62),
    };

    /// <summary>参数名列表（顺序同上）。</summary>
    public static readonly IReadOnlyList<string> Keys = All.Select(s => s.Key).ToArray();

    /// <summary>进「外观微调」界面的那些参数（顺序同上）。</summary>
    public static readonly IReadOnlyList<ThemeParamSpec> Adjustable =
        All.Where(s => s.Adjustable).ToArray();

    /// <summary>按 key 找规格（不区分大小写；找不到返回 null）。</summary>
    public static ThemeParamSpec? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var trimmed = key.Trim();
        foreach (var spec in All)
        {
            if (string.Equals(spec.Key, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return spec;
            }
        }

        return null;
    }

    /// <summary>夹取某个参数（未知 key 原样返回）。</summary>
    public static double Clamp(string key, double value)
        => Find(key)?.Clamp(value) ?? value;

    /// <summary>全部参数的默认值（= 原版 <c>PARAM_SPECS</c> 的默认列）。</summary>
    public static ThemeParams Defaults() => new(
        WindowOpacity: 0.42,
        CardOpacity: 0.16,
        CardHoverOpacity: 0.26,
        BlurRadius: 26.0,
        Radius: 16.0,
        AnimMs: 260.0,
        HoverMs: 160.0,
        SheetOpacity: 0.62);
}

/// <summary>
/// 一组**已解析**的参数值（8 个，顺序与原版一致）。用 record struct 是为了让单测能直接 `==` 比较。
/// </summary>
public readonly record struct ThemeParams(
    double WindowOpacity,
    double CardOpacity,
    double CardHoverOpacity,
    double BlurRadius,
    double Radius,
    double AnimMs,
    double HoverMs,
    double SheetOpacity)
{
    /// <summary>按 key 取值（未知 key 返回 0 —— 调用方应先 <see cref="ThemeParamSpecs.Find"/>）。</summary>
    public double Get(string key) => key switch
    {
        ThemeParamSpecs.WindowOpacityKey => WindowOpacity,
        ThemeParamSpecs.CardOpacityKey => CardOpacity,
        ThemeParamSpecs.CardHoverOpacityKey => CardHoverOpacity,
        ThemeParamSpecs.BlurRadiusKey => BlurRadius,
        ThemeParamSpecs.RadiusKey => Radius,
        ThemeParamSpecs.AnimMsKey => AnimMs,
        ThemeParamSpecs.HoverMsKey => HoverMs,
        ThemeParamSpecs.SheetOpacityKey => SheetOpacity,
        _ => 0,
    };

    /// <summary>圆角相对基准的倍率（默认 16 ⇒ 1.0）。界面用它把"设计圆角"按比例缩放。</summary>
    public double RadiusScale => ThemeParamSpecs.RadiusDesignDefault <= 0
        ? 1
        : Radius / ThemeParamSpecs.RadiusDesignDefault;

    /// <summary>悬停时长相对基准的倍率（默认 160 ⇒ 1.0）。</summary>
    public double HoverScale => ThemeParamSpecs.HoverDesignDefault <= 0
        ? 1
        : HoverMs / ThemeParamSpecs.HoverDesignDefault;

    /// <summary>
    /// 把一个"设计圆角"按当前圆角参数缩放。
    ///
    /// <para>为什么是"按倍率缩放"而不是"直接等于 radius"：界面里的圆角是**量出来的设计值**
    /// （图块/标签 6、状态栏 5 …），把 16 直接当圆角用会一次性改掉现有观感。
    /// 倍率映射保证**默认参数下界面与现在一模一样**（16 ⇒ ×1.0 ⇒ 还是 6）。</para>
    /// </summary>
    public double ScaleCornerRadius(double designCornerRadius, double max = 24.0)
    {
        if (designCornerRadius <= 0)
        {
            return 0;
        }

        return Math.Clamp(Math.Round(designCornerRadius * RadiusScale, MidpointRounding.AwayFromZero), 0, max);
    }

    /// <summary>构造时把 8 个已知 key 的覆盖值叠上去（未知 key 忽略）。</summary>
    public ThemeParams With(IReadOnlyDictionary<string, double>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return this;
        }

        var result = this;
        foreach (var (key, raw) in overrides)
        {
            var spec = ThemeParamSpecs.Find(key);
            if (spec is null)
            {
                continue;
            }

            var value = spec.Clamp(raw);
            result = key switch
            {
                ThemeParamSpecs.WindowOpacityKey => result with { WindowOpacity = value },
                ThemeParamSpecs.CardOpacityKey => result with { CardOpacity = value },
                ThemeParamSpecs.CardHoverOpacityKey => result with { CardHoverOpacity = value },
                ThemeParamSpecs.BlurRadiusKey => result with { BlurRadius = value },
                ThemeParamSpecs.RadiusKey => result with { Radius = value },
                ThemeParamSpecs.AnimMsKey => result with { AnimMs = value },
                ThemeParamSpecs.HoverMsKey => result with { HoverMs = value },
                ThemeParamSpecs.SheetOpacityKey => result with { SheetOpacity = value },
                _ => result,
            };
        }

        return result;
    }

    /// <summary>额外把其中一个 key 覆盖掉（界面改滑杆时的便捷写法）。</summary>
    public ThemeParams With(string key, double value)
        => With(new Dictionary<string, double>(StringComparer.Ordinal) { [key] = value });
}
