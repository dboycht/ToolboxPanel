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

    /// <summary>播放一次入场动效（每次切到该页都会调用）。</summary>
    void PlayEntrance();
}
