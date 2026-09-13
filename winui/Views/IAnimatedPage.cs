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
    /// 第一步：把页面置于"入场起始态"（**整页**不透明度 = 0，各容器也置起始态）。
    ///
    /// <para>必须在页面**成为可见内容之前**调用 —— 页面级不透明度是"总闸门"，
    /// 它保证从这一刻到动画真正开始之间渲染出的任何一帧都看不到内容。
    /// 只把每个 item 容器置 0 是不够的：容器可能还没实现（切页时会被回收），
    /// 而未实现的容器会在渲染之后才以最终态出现。</para>
    /// </summary>
    void PrepareEntrance();

    /// <summary>
    /// 第二步：开始入场（放行整页 + 让各容器从起始态动起来）。
    ///
    /// <para>⚠️ 它与 <see cref="PrepareEntrance"/> 之间**必须隔一次布局**（延迟一帧调用），
    /// 否则容器还没来得及被实现，动画就"没有对象可播"，于是整页直接亮起来。
    /// 推荐做法见 <c>MainWindow.ShowTab</c>：先 Prepare，再 <c>DispatcherQueue.TryEnqueue</c> 延迟一帧 Play。</para>
    /// </summary>
    void PlayEntrance();
}
