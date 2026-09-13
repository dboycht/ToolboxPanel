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
    /// 诊断：把"已实现容器的不透明度分布"打成一行。
    /// 判据：动画期间**不应该**有容器已经停在最终态（那说明它没走入场、会直接亮着出现）。
    /// </summary>
    internal void DiagFrame(string stage)
    {
        if (!DiagnosticsEnabled)
        {
            return;
        }

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

        Diag($"{stage}：已实现={realized} 其中已到最终态的={atOne}");
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
    /// 准备入场：登记"这一轮要播"，并把**已实现**的容器同步置为起始态。
    ///
    /// <para>⚠️ 这里**刻意不碰页面级不透明度**（上一版用它当"总闸门"，结果一旦放行没跑到，
    /// 整页就永久不可见 —— 用户看到空白）。现在只做容器级起始态：
    /// 没有容器时什么都不用做，页面本来就是可见的，**不可能出现整页空白**。</para>
    /// </summary>
    public void Prepare()
    {
        DiagSnapshot("Prepare 进入");

        if (!_spec.Enabled)
        {
            ResetAll();
            return;
        }

        StopRunning();
        _pending = true;
        _played.Clear();
        HideRealized();         // 已实现的容器给上起始态（未实现的交给 ContainerContentChanging）
        DiagSnapshot("Prepare 结束");
    }

    /// <summary>
    /// 在"可以开始动画"时放行：等到容器就位（或确认这一页没有内容）之后再起交错动画。
    ///
    /// <para>轮询而不是"延迟一帧"：延迟一帧只保证调度器转了一圈，**不保证布局跑过**；
    /// 布局没跑就没有容器，动画建不出来。轮询能让这两种情况都收敛，
    /// 并且**最多等 <paramref name="maxAttempts"/> 个周期**，绝不会把界面卡住。</para>
    /// </summary>
    /// <param name="maxAttempts">最多等多少个调度器周期（每个周期 ≈ 一帧）。</param>
    public void RevealWhenReady(int maxAttempts = 4)
    {
        if (!_spec.Enabled)
        {
            ResetAll();
            return;
        }

        AttemptReveal(attempt: 0, maxAttempts);
    }

    private void AttemptReveal(int attempt, int maxAttempts)
    {
        if (!_spec.Enabled)
        {
            ResetAll();
            return;
        }

        int realized = RealizedCount();
        bool layoutHasRun = _list.ActualWidth > 0 && _list.ActualHeight > 0;

        Diag($"RevealWhenReady 尝试#{attempt}：已实现容器={realized} 列表尺寸={_list.ActualWidth:0}x{_list.ActualHeight:0}");

        // 有容器可播，或布局已经跑过（说明这一页确实没有可播的东西），就开始
        if (realized > 0 || layoutHasRun || attempt >= maxAttempts)
        {
            Play();
            return;
        }

        _list.DispatcherQueue.TryEnqueue(() => AttemptReveal(attempt + 1, maxAttempts));
    }

    /// <summary>开始入场：把已实现的容器置起始态并起交错动画。</summary>
    private void Play()
    {
        DiagSnapshot("Play 进入");

        try
        {
            // 停止上一轮：HoldEnd 的动画值优先级高于本地赋值，不停掉就会"先闪最终态再重播"
            StopRunning();

            // 容器此刻可能已是最终态 → 同步置 0（同帧完成，不会渲染出 1）
            HideRealized();

            // 起交错动画（已实现的容器）。未实现的容器由 ContainerContentChanging 兜住。
            PlayRealized();

            DiagFrame("Play 结束（判据：不应有容器停在最终态）");
            DiagSnapshot("Play 结束");
        }
        catch (Exception ex)
        {
            // 外观类失败必须是"软"的：出错也绝不能把界面留在不可见状态
            App.WriteCrash("EntranceAnimator.Play", ex);
            ResetAll();
        }
    }

    private int RealizedCount()
    {
        int count = 0;
        for (int index = 0; index < _list.Items.Count; index++)
        {
            if (_list.ContainerFromIndex(index) is not null)
            {
                count++;
            }
        }

        return count;
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
        _played.Clear();

        // 恢复所有已实现容器的常态（不透明度 1、位移 0）。
        // ⚠️ 这里**只碰容器**，不碰页面级不透明度 —— 现在的设计里页面从不被隐藏，
        //    所以"界面空白"在设计上就不可能发生（这也是上一版整页闸门被撤掉的原因）。
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
