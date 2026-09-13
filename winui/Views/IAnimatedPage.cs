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
    /// 就地准备入场起始态（把页面同步置为不可见），**不**起动画。
    ///
    /// <para>⚠️ 这个方法是为修"重播看起来像强调动画"而加的：
    /// 切回**已打开过**的标签页时，页面里的容器早已实现、且停在最终态；
    /// 若先把页面挂进可视树、再置起始态，中间那一帧会以最终态被渲染出来（用户看到的正是这个）。
    /// 正确顺序：**准备起始态（页面尚未显示）→ 挂进可视树 → 再起动画**。</para>
    /// </summary>
    void PrepareEntrance();

    /// <summary>播放一次入场动效（每次切到该页都会调用，且必须每次都从起始态开始）。</summary>
    void PlayEntrance();
}
