// EntranceAnimator.cs —— 页面入场动效（可配置时长/交错/曲线，可重复播放）
//
// 为什么不用内置的 `EntranceThemeTransition`：
//   1. 它**没有时长/曲线参数**，做不到"设置里调动画速度"；
//   2. 它**只在容器第一次实现时播放**，切回已缓存的标签页不会再播（用户实测反馈的第 4 条）。
//
// 这里自己管：容器实现时（ContainerContentChanging）与"每次切页"（Play）两个时机都套用同一套参数，
// 并用 `_played` 去重，避免同一次入场被播两遍。

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Views;

/// <summary>
/// 负责一个列表/网格页面的入场动效。
///
/// <para><b>核心机制（这三条一起才成立，别拆）</b>：</para>
/// <list type="number">
/// <item><b>延迟一帧</b>：<see cref="Prepare"/> 与 <see cref="Play"/> 之间必须隔一次布局。
/// 因为切页时 item 容器会被回收，未实现的容器"渲染之后才出现" ——
/// 不等这一帧，动画就没有对象可播，整页会直接亮起来。</item>
/// <item><b>页面级不透明度当闸门</b>：Prepare 把整页置 0、Play 在**起完动画之后**才放行；
/// 于是放行那一帧页面上"什么都没有"，随后才逐格浮现。</item>
/// <item><b>容器级起始态 + 交错</b>：每个容器在起动画前同步置 0（同帧完成，不会渲染出 1）。</item>
/// </list>
///
/// <para>⚠️ 页面级闸门只能在页面**还没显示**（折叠）或**已经置 0** 时关闭 ——
/// 反过来说：如果先让页面以最终态可见、再置 0，中间那一帧就会被渲染出来（用户看到的"先亮一下"）。
/// 本项目由 <c>MainWindow.ShowTab</c> 保证顺序：折叠切换 → Prepare → 延迟一帧 → Play。</para>
/// </summary>
internal sealed class EntranceAnimator
{
    /// <summary>
    /// 诊断开关（`--diag` / `--probe-switch`）：把入场动效的时序细节写进
    /// <c>%TEMP%\toolboxpanel-probe.log</c>。
    ///
    /// <para>默认关闭、零开销 —— 这类"看不见的观感问题"（闪一帧、顺序错位）
    /// 只能靠这种逐毫秒证据定位，所以留着这个入口，别删
    /// （排查详录见 ERROR.md E15）。</para>
    /// </summary>
    internal static bool DiagnosticsEnabled { get; set; }

    private readonly ListViewBase _list;
    private readonly HashSet<object> _played = new();

    /// <summary>本轮起过的 Storyboard —— 下次播放前必须 Stop，否则它 HoldEnd 的值会压住我们设的起始态。</summary>
    private readonly List<Storyboard> _running = new();

    private AnimationSpec _spec = AnimationSpec.Disabled;
    private bool _pending;

    /// <summary>本次入场是否被请求过（<see cref="Prepare"/> 置位，下次 Prepare 前一直保持）。</summary>
    private bool _playRequested;

    /// <summary>诊断：用于给日志加上"距本次切页多少毫秒"。</summary>
    private static System.Diagnostics.Stopwatch? _diagnosticClock;

    public EntranceAnimator(ListViewBase list)
    {
        _list = list;
        _list.ContainerContentChanging += OnContainerContentChanging;
    }

    /// <summary>诊断：开始一次采样窗口（切页那一刻调用）。</summary>
    internal static void BeginDiagnostics()
    {
        if (!DiagnosticsEnabled)
        {
            return;
        }

        _diagnosticClock = System.Diagnostics.Stopwatch.StartNew();
    }

    private static void Diag(string message)
    {
        if (!DiagnosticsEnabled)
        {
            return;
        }

        var ms = _diagnosticClock?.Elapsed.TotalMilliseconds ?? -1;
        App.ProbeLog($"[{ms,7:F1}ms] {message}");
    }

    /// <summary>
    /// ⚠️ 临时诊断：把"页面根元素的不透明度 + 已实现容器的最值"打成一行。
    /// 定位"切页先亮一下"用：页面级不透明度必须在挂载前就是 0，且直到动画结束才回 1。
    /// </summary>
    internal void DiagFrame(string stage)
    {
        if (!DiagnosticsEnabled)
        {
            return;
        }

        double pageOpacity = GetPageOpacity();
        int realized = 0;
        int atOne = 0;
        for (int index = 0; index < _list.Items.Count; index++)
        {
            if (_list.ContainerFromIndex(index) is UIElement container)
            {
                realized++;
                if (container.Opacity >= 0.999) atOne++;
            }
        }

        Diag($"{stage}：页面Opacity={pageOpacity:0.00} 已实现={realized} 其中全1的={atOne}");
    }

    /// <summary>⚠️ 临时诊断：页面根元素（网格页/列表页 UserControl）当前的不透明度。</summary>
    internal double GetPageOpacity() => PageElement?.Opacity ?? -1;

    /// <summary>
    /// 页面根元素（GridPage / ListViewPage 这个 **UserControl**）——**整页一次性隐藏的开关**。
    ///
    /// <para>⚠️ 不能停在"页面内部的 Grid"上（第一版就错在这儿，日志里表现为
    /// `Prepare 后：页面Opacity=1.00`）：视觉树是
    /// `ContentControl → ContentPresenter → UserControl(页面) → Grid(页面内根) → ListView`，
    /// 所以要一直往上找到 UserControl 为止。</para>
    /// </summary>
    internal FrameworkElement? PageElement
    {
        get
        {
            DependencyObject? current = _list;

            while (current is not null)
            {
                if (current is UserControl page)
                {
                    return page;
                }

                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }

    /// <summary>诊断：把"当前已实现容器的数量 + 不透明度分布"打成一行。</summary>
    internal void DiagSnapshot(string stage)
    {
        if (!DiagnosticsEnabled)
        {
            return;
        }

        int realized = 0;
        int atZero = 0;
        int atOne = 0;
        double min = double.MaxValue;
        double max = double.MinValue;

        for (int index = 0; index < _list.Items.Count; index++)
        {
            if (_list.ContainerFromIndex(index) is UIElement container)
            {
                realized++;
                double o = container.Opacity;
                if (o <= 0.001) atZero++;
                if (o >= 0.999) atOne++;
                min = Math.Min(min, o);
                max = Math.Max(max, o);
            }
        }

        double shown = realized == 0 ? -1 : min;
        Diag($"{stage}：items={_list.Items.Count} 已实现={realized} 全0={atZero} 全1={atOne} "
             + $"范围=[{(realized == 0 ? "n/a" : min.ToString("0.00"))}..{(realized == 0 ? "n/a" : max.ToString("0.00"))}] "
             + $"pending={_pending} played={_played.Count}");
    }

    /// <summary>套用新的动效参数（关掉动效时把已动画过的项恢复成常态）。</summary>
    public void ApplySpec(AnimationSpec spec)
    {
        _spec = spec;
        if (!spec.Enabled)
        {
            ResetAll();
        }
    }

    /// <summary>
    /// 准备入场起始态：把已实现的容器**同步置为不可见 + 起始位移**，但**不起动画**。
    /// 主窗口在把页面挂进可视树**之前**调用它 —— 这样"最终态那一帧"根本没机会被渲染出来。
    /// </summary>
    /// <summary>
    /// 第一步：把页面置于**入场起始态**（整页不透明度 = 0），并标记"下一次 <see cref="Play"/> 要从头播"。
    ///
    /// <para>⚠️ 页面级不透明度是"总闸门"，不是可选优化：容器在切页时会被回收，
    /// 未实现的容器是**渲染之后才出现**的；只把已实现的容器置 0 挡不住它们。
    /// 闸门在这里关掉之后，从这一刻到 <see cref="Play"/> 之间渲染出的任何一帧都看不到内容。</para>
    /// </summary>
    public void Prepare()
    {
        DiagSnapshot("Prepare 进入");
        StopRunning();

        if (!_spec.Enabled)
        {
            ResetAll();
            return;
        }

        _playRequested = true;
        _pending = true;
        SetPageOpacity(0);          // 总闸门：整页不可见
        _played.Clear();
        HideRealized();             // 已实现的容器也给上起始态（供交错动画）
        DiagSnapshot("Prepare 结束");
    }

    /// <summary>
    /// 第二步：开始入场。**必须在 <see cref="Prepare"/> 之后隔一次布局（延迟一帧）调用**。
    ///
    /// <para>延迟这一帧的作用：让容器先被实现出来。到这里时容器已是最终态，
    /// 我们临起动画前再置 0（同一帧内完成，不会渲染出中间态），
    /// 最后才放开整页闸门 —— 放行那一帧页面上"什么都没有"，随后逐格浮现。</para>
    /// </summary>
    public void Play()
    {
        DiagSnapshot("Play 进入");

        if (!_spec.Enabled)
        {
            ResetAll();
            return;
        }

        // 本次入场没被要求（例如只是设置变化后的重套），不播
        if (!_playRequested)
        {
            SetPageOpacity(1);
            return;
        }

        // 停止上一轮：HoldEnd 的动画值优先级高于本地赋值，不停掉就会"先闪最终态再重播"
        StopRunning();

        // 容器此刻是最终态 → 同步置 0（同帧完成，不会渲染出 1）
        HideRealized();

        // 起动画（已实现的容器）
        PlayRealized();

        // 放行整页：此刻各容器都在起始态，所以这一帧看到的是"什么都没有"
        SetPageOpacity(1);

        // ⚠️ 这里**不**清 _playRequested / _pending：
        //    本帧之后才被实现的容器（滚动进视野、虚拟化补实现）也要按入场态出现，
        //    由 ContainerContentChanging 兜住；下次切页时 Prepare() 才重置。
        DiagSnapshot("Play 结束");
    }

    /// <summary>设置"整页"的不透明度（找不到页面元素时静默跳过）。</summary>
    private void SetPageOpacity(double value)
    {
        if (PageElement is { } page)
        {
            page.Opacity = value;
        }
    }

    private void StopRunning()
    {
        foreach (var storyboard in _running)
        {
            storyboard.Stop();
        }

        _running.Clear();
    }

    /// <summary>把已实现的容器同步置为起始态（不可见 + 位移）。</summary>
    private void HideRealized()
    {
        for (int index = 0; index < _list.Items.Count; index++)
        {
            if (_list.ContainerFromIndex(index) is not UIElement container)
            {
                continue;
            }

            container.Opacity = 0;
            EnsureTransform(container).TranslateY = _spec.FromOffset;
        }
    }

    private void PlayRealized()
    {
        bool playedAny = false;

        for (int index = 0; index < _list.Items.Count; index++)
        {
            if (_list.ContainerFromIndex(index) is UIElement container)
            {
                playedAny |= AnimateContainer(container, index);
            }
        }

        // 一个容器都还没实现（首次显示）：保持 pending，由 ContainerContentChanging 兜住
        if (playedAny)
        {
            _pending = false;
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            _played.Remove(args.Item);
            return;
        }

        if (_pending && args.ItemContainer is UIElement container)
        {
            AnimateContainer(container, args.ItemIndex);
        }
    }

    /// <summary>给一个容器起动画；返回是否真的播了（已播过的返回 false）。</summary>
    private bool AnimateContainer(UIElement container, int index)
    {
        if (!_played.Add(container))
        {
            return false;   // 这一轮已经播过，别重播（重播会看起来"闪一下"）
        }

        var transform = EnsureTransform(container);

        container.Opacity = 0;
        transform.TranslateY = _spec.FromOffset;

        var beginTime = TimeSpan.FromMilliseconds(index * _spec.StaggerMs);
        var duration = new Duration(TimeSpan.FromMilliseconds(_spec.DurationMs));
        var easing = CreateEasing(_spec.Easing);

        var storyboard = new Storyboard();
        storyboard.Children.Add(CreateAnimation(container, "Opacity", from: 0, to: 1, duration, beginTime, easing));

        // ⚠️ 显式给 From：动画起始值不再依赖"当前值"，切页重播时不会先停一拍最终态
        storyboard.Children.Add(CreateAnimation(transform, "TranslateY", from: _spec.FromOffset, to: 0, duration, beginTime, easing));
        storyboard.Begin();
        _running.Add(storyboard);
        return true;
    }

    private static CompositeTransform EnsureTransform(UIElement container)
    {
        if (container.RenderTransform is CompositeTransform existing)
        {
            return existing;
        }

        var transform = new CompositeTransform();
        container.RenderTransform = transform;
        return transform;
    }

    private void ResetAll()
    {
        StopRunning();
        _pending = false;
        _playRequested = false;
        _played.Clear();

        // 整页放行：动效关掉时页面必须立刻可见（否则会留下一块整页透明）
        SetPageOpacity(1);

        for (int index = 0; index < _list.Items.Count; index++)
        {
            if (_list.ContainerFromIndex(index) is not UIElement container)
            {
                continue;
            }

            container.Opacity = 1;
            if (container.RenderTransform is CompositeTransform transform)
            {
                transform.TranslateY = 0;
            }
        }
    }

    private static DoubleAnimation CreateAnimation(
        DependencyObject target, string property, double from, double to,
        Duration duration, TimeSpan beginTime, EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            From = from,           // 显式起始值：不依赖"当前值/上一轮残留值"
            To = to,
            Duration = duration,
            BeginTime = beginTime,
            EasingFunction = easing,
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    /// <summary>三种观感的曲线（设置里只暴露这三种，不把缓动函数名暴露给用户）。</summary>
    internal static EasingFunctionBase CreateEasing(AnimationEasing easing) => easing switch
    {
        AnimationEasing.Soft => new SineEase { EasingMode = EasingMode.EaseOut },
        AnimationEasing.Snappy => new QuadraticEase { EasingMode = EasingMode.EaseOut },
        _ => new CubicEase { EasingMode = EasingMode.EaseOut },
    };
}
