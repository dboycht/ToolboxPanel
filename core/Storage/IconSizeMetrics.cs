// IconSizeMetrics.cs —— 图标大小三档的尺寸表（纯数据，可单测）
//
// 对应原版 v1.11.6 的 `icon_widget.SIZE_PRESETS`（small/medium/large）。
//
// ⚠️⚠️ **本表不是照抄原版数值**，原因有两条（都是刻意的，别"顺手改回去"）：
//   ① 项目里那三个数字**已经定版**：用户 2026-09-14 确认"UI 已经完美了"，
//      当时的密度是 **图块 68×72 / 图标 30 / 名称 10.5 号(行高 12.5)** —— 所以 **medium 必须逐值等于它**，
//      本表的 medium 一旦被改动，用户界面就会"莫名变大变小"（有单测钉着）；
//   ② 原版那套高度（82/104/126）是按**三行标签**留的（它用 `icon_label` 三行省略），
//      我们的图块是**两行**，所以只按同一比例缩放宽度与图标，高度按两行标签重算。
//
// 原版参考值（保真记录）：small 32/52×82 · medium 48/68×104 · large 64/84×126。

namespace ToolboxPanel.Core.Storage;

/// <summary>一档图标尺寸的全部展示尺寸（单位 DIP；与 XAML 里的度量同一坐标系）。</summary>
public sealed record IconSizeMetrics(
    string Name,
    double TileWidth,
    double TileHeight,
    double IconPixels,
    double GlyphPixels,
    double FontSize,
    double LineHeight)
{
    /// <summary>小：图块 56×60，图标 22。</summary>
    public static readonly IconSizeMetrics Small = new("small", 56, 60, 22, 18, 9.5, 11.5);

    /// <summary>
    /// 中（默认）：**与已定版密度逐值一致** —— 图块 68×72 / 图标 30 / 字形 24 / 名称 10.5 号(行高 12.5)。
    /// ⚠️ 改这些数字就等于改用户已确认的界面观感（`tests/IconSizeMetricsTests.cs` 会拦下来）。
    /// </summary>
    public static readonly IconSizeMetrics Medium = new("medium", 68, 72, 30, 24, 10.5, 12.5);

    /// <summary>大：图块 84×88，图标 40。</summary>
    public static readonly IconSizeMetrics Large = new("large", 84, 88, 40, 32, 12.0, 14.0);

    /// <summary>三档（界面按这个顺序列出来）。</summary>
    public static readonly IReadOnlyList<IconSizeMetrics> All = new[] { Small, Medium, Large };

    /// <summary>
    /// 按设置里的名字取尺寸；**未知/空/null 一律回落 medium**
    /// （与 <see cref="AppSettings.IconSizes"/> 的归一化语义一致，大小写不敏感）。
    /// </summary>
    public static IconSizeMetrics For(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var metrics in All)
            {
                if (string.Equals(metrics.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return metrics;
                }
            }
        }

        return Medium;
    }

    /// <summary>设置里存的线名（与 <see cref="For"/> 互逆；用于写回 config.json）。</summary>
    public string WireName => Name;
}
