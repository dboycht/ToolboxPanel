// IAnimatedPage.cs —— 内容页的统一动效契约
//
// 主窗口只认这个接口：切换标签页时通知页面播放入场动效；设置变化时把新参数发下去。
// 具体怎么做动画由页面内部（EntranceAnimator）决定，主窗口不需要知道用的是网格还是列表。

using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Views;

public interface IAnimatedPage
{
    /// <summary>套用动效参数（时长 / 交错 / 曲线 / 总开关）。</summary>
    void ApplyAnimationSpec(AnimationSpec spec);

    /// <summary>
    /// 套用主题令牌。
    ///
    /// <para>页面里那些**引用本项目注入的固定资源键**（如 `{ThemeResource AccentBrushDark}`，
    /// 拖放落点指示线）不会随主题自动换色 —— 必须由代码直接赋值（memory/07 §4 的"表面按名字"）。
    /// 主题切换与页面创建时都会调用。</para>
    /// </summary>
    /// <param name="radiusScale">
    /// 主题里 `radius` 参数的**倍率**（默认 1.0 ⇒ 界面设计圆角不变）。
    /// 页面按自己的设计圆角（图块 6 / 列表行 4）乘这个倍率 —— 见 <see cref="ThemeScale"/>。
    /// </param>
    void ApplyTheme(ThemePalette palette, double radiusScale);

    /// <summary>
    /// 第一步：把页面置于"入场起始态"（**整页**不透明度 = 0，各容器也置起始态）。
    ///
    /// <para>必须在页面**成为可见内容之前**调用 —— 页面级不透明度是"总闸门"，
    /// 它保证从这一刻到动画真正开始之间渲染出的任何一帧都看不到内容。
    /// 只把每个 item 容器置 0 是不够的：容器可能还没实现（切页时会被回收），
    /// 而未实现的容器会在渲染之后才以最终态出现。</para>
    /// </summary>
    void PrepareEntrance();

    /// <summary>
    /// 第二步：**在页面可以安全放行时**开始入场（放行整页 + 让各容器从起始态动起来）。
    ///
    /// <para>⚠️ 不要简单地"延迟一帧"就调用它：延迟一帧只保证调度器转了一圈，
    /// **不保证布局已经跑过** —— 布局没跑就没有 item 容器，动画一个都建不出来，
    /// 而整页闸门却被放行 ⇒ 用户看到的是一块空白或直接亮起的最终态。
    /// 实现方应当在"等到容器"或"确认布局已跑过"之后再放行（本项目见
    /// <c>EntranceAnimator.RevealWhenReady</c> 的轮询实现）。</para>
    /// </summary>
    void RevealWhenReady();

    /// <summary>
    /// 套用当前语言的文案（v2.0.3 i18n）。
    ///
    /// <para>分工：XAML 里标了 <c>ui:Tr.Key</c> 的静态文字由 `Tr.RefreshAll()` 统一重刷，
    /// **不在这里重复处理**；这里只管"由代码设置的"那几处（空页提示、无匹配提示、计数文字）。</para>
    /// </summary>
    void ApplyLanguage();
}
