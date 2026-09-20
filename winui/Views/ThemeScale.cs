// ThemeScale.cs —— 主题参数 → 界面度量 的换算（一处收口）
//
// 为什么要有这个文件：`radius` / `hover_ms` 这两个参数在原版里是**绝对值**
// （圆角 16、悬停 160ms），而本项目界面里的圆角是**量出来的设计值**
// （图块/标签/面板 6、状态栏 5、列表行 4 …），悬停动画时长来自「动效」节的设置。
//
// 所以两者之间用**倍率**挂钩（默认参数 ⇒ ×1.0 ⇒ 界面与现在一模一样），
// 换算收在这里一处，避免每个控件各写一遍四舍五入。

using Microsoft.UI.Xaml;

namespace ToolboxPanel.Views;

internal static class ThemeScale
{
    /// <summary>圆角上限（DIP）—— 再大就不像"卡片"了。</summary>
    private const double MaxCornerRadius = 24;

    /// <summary>把一个设计圆角按主题倍率缩放（默认参数下原样返回）。</summary>
    public static double Corner(double designCornerRadius, double scale)
    {
        if (designCornerRadius <= 0)
        {
            return 0;
        }

        return Math.Clamp(
            Math.Round(designCornerRadius * scale, MidpointRounding.AwayFromZero),
            0,
            MaxCornerRadius);
    }

    /// <summary>同上，直接给 <see cref="CornerRadius"/>。</summary>
    public static CornerRadius Corners(double designCornerRadius, double scale)
        => new(Corner(designCornerRadius, scale));

    /// <summary>悬停动画时长（毫秒）：在「动效」节设定的时长上按主题倍率缩放。</summary>
    public static int HoverDuration(int animationDurationMs, double scale)
        => (int)Math.Clamp(
            Math.Round(animationDurationMs * scale, MidpointRounding.AwayFromZero),
            0,
            2000);
}
