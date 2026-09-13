// EntranceAnimator.cs —— 内容页的入场动效（**整片一起从无到有**，可配置时长/曲线，可重复播放）
//
// 用户明确要求（2026-09-13）：**不要逐个浮现的交错动画**，要"所有图标/列表项一起从无到有地出现"。
// 所以现在只做**一次整片淡入**：把列表整体从 0 淡到 1，里面的内容同时出现。
//
// 为什么不用内置的 `EntranceThemeTransition`：
//   1. 它**没有时长/曲线参数**，做不到"设置里调动画速度"；
//   2. 它**只在容器第一次实现时播放**，切回已缓存的标签页不会再播。
//
// ⚠️ 安全底线（踩过一次"整页空白"，见 ERROR.md E15）：
//   凡是"把东西置成不可见、再等动画放行"的写法，都可能因为放行没跑到而永久不可见。
//   所以这里除了 try/catch，还挂了一道**超时兜底**（见 StartSafetyWatchdog）。

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Views;

/// <summary>
/// 负责一个列表/网格页面的入场动效：**整片一次淡入**。
///
/// <para>时序：</para>
/// <list type="number">
/// <item><b>关闸门</b>：把列表整体不透明度置 0（内容此刻不可见）；</item>
/// <item><b>等布局</b>：页面要可见才会布局（折叠状态下 GridView 不实现容器）；</item>
/// <item><b>放行</b>：起一条"整片 0 → 1"的动画。因为容器此刻都被置成了 0，
/// 即便动画因为某种原因没跑，末尾的兜底也会把它们恢复可见。</item>
/// </list>
/// </summary>
internal sealed class EntranceAnimator
{
    /// <summary>
    /// 诊断开关（`--diag` / `--probe-switch`）：把入场时序写进 `%TEMP%\toolboxpanel-probe.log`。
    /// 默认关闭、零开销 —— "闪一帧 / 空白"这类看不见的问题只能靠它定位（ERROR.md E15）。
    /// </summary>
    internal static bool DiagnosticsEnabled { get; set; }

    private readonly ListViewBase _list;

    /// <summary>本轮起过的 Storyboard —— 下次播放前必须 Stop，否则它 HoldEnd 的值会压住我们设的起始态。</summary>
    private readonly List<Storyboard> _running = new();

    private AnimationSpec _spec = AnimationSpec.Disabled;

    /// <summary>诊断：用于给日志加上"距本次切页多少毫秒"。</summary>
    private static System.Diagnostics.Stopwatch? _diagnosticClock;

    public EntranceAnimator(ListViewBase list)
    {
        _list = list;
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

        App.ProbeLog($"[{(_diagnosticClock?.Elapsed.TotalMilliseconds ?? -1),7:F1}ms] {message}");
    }

    /// <summary>诊断：把"列表整体不透明度 + 容器可见情况"打成一行。</summary>
    internal void DiagSnapshot(string stage)
    {
        if (!DiagnosticsEnabled)
        {
            return;
        }

        int realized = 0;
        int hidden = 0;
        for (int index = 0; index < _list.Items.Count; index++)
        {
            if (_list.ContainerFromIndex(index) is UIElement container)
            {
                realized++;
                if (container.Opacity < 0.999)
                {
                    hidden++;
                }
            }
        }

        Diag($"{stage}：items={_list.Items.Count} 已实现={realized} 仍不可见={hidden} "
             + $"列表Opacity={_list.Opacity:0.00}");
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
    /// 准备入场：把**列表整体**置为不可见（整片一起的起始态）。
    /// 主窗口会在页面可见之后、起动画之前调用它。
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
        HideRealized();          // 整片置 0（含已实现的容器）
        DiagSnapshot("Prepare 结束");
    }

    /// <summary>
    /// 开始入场。因为动画只有"整片一次淡入"、不依赖容器是否已实现，
    /// 所以这里**不再需要轮询等容器**：把页面显示出来、下一帧起动画即可
    /// （名字保留，是为了让主窗口那边的调用语义保持"准备好就放行"）。
    /// </summary>
    public void RevealWhenReady()
    {
        if (!_spec.Enabled)
        {
            ResetAll();
            return;
        }

        Play();
    }

    /// <summary>
    /// 开始入场：**整片一次淡入**（0 → 1）。
    ///
    /// <para>结构上刻意做得极简（用户明确要求"所有图标/列表项一起从无到有"）：
    /// 只有**一条**动画，作用在列表整体上，**不再逐个容器做动画**（也就没有"交错"）。
    /// 容器自己保持常态不透明度，被列表整体带着一起淡入。</para>
    ///
    /// <para>⚠️ 真正保证"绝不空白"的是动画自身的 <c>FillBehavior=HoldEnd</c>：
    /// 动画结束（或被中断）时保持的是**最终值 1**，而不是回落到本地 0（那正是上一版变空白的机制）。
    /// 末尾那条超时兜底只是最后一道保险。</para>
    /// </summary>
    private void Play()
    {
        DiagSnapshot("Play 进入");

        try
        {
            StopRunning();

            var storyboard = new Storyboard();
            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(_spec.DurationMs)),
                EasingFunction = CreateEasing(_spec.Easing),

                // ⚠️ 关键：动画结束后**保持最终值 1**。
                //    Storyboard 默认就是 HoldEnd，但这里显式写出来 —— 因为一旦有人把它改成 Stop，
                //    不透明度就会回落到本地值 0，页面立刻变不可见（这正是"整片空白"的成因）。
                FillBehavior = FillBehavior.HoldEnd,
            };
            Storyboard.SetTarget(fade, _list);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);

            storyboard.Begin();
            _running.Add(storyboard);

            DiagSnapshot("Play 结束（整片淡入已启动）");
        }
        catch (Exception ex)
        {
            // 外观类失败必须是"软"的：出错也绝不能把界面留在不可见状态
            App.WriteCrash("EntranceAnimator.Play", ex);
            ResetAll();
            return;
        }

        StartSafetyWatchdog();
    }

    /// <summary>
    /// 兜底保险：动画应当在这之前结束并把不透明度保持在 1。
    /// 如果到点还不可见，就停掉动画、把不透明度**永久写成 1**。
    ///
    /// <para>⚠️ 与上一版的区别（上一版就是在这里翻车的）：兜底**不再在动画进行中触发**，
    /// 而是等动画时长过去之后才检查 —— 否则它会在动画播到一半时打断它，
    /// 视觉上就是"淡到一半突然消失"（实测日志：`兜底触发：列表Opacity=0.43`）。</para>
    /// </summary>
    private void StartSafetyWatchdog()
    {
        var timer = _list.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(_spec.DurationMs + 700);
        timer.IsRepeating = false;

        timer.Tick += (_, _) =>
        {
            if (_list.Opacity >= 0.999)
            {
                return;   // 正常结束，什么都不用做
            }

            Diag($"兜底触发：动画时间已过但列表仍不可见（Opacity={_list.Opacity:0.00}）→ 强制显示");
            ResetAll();
        };

        timer.Start();
    }

    private void StopRunning()
    {
        foreach (var storyboard in _running)
        {
            storyboard.Stop();
        }

        _running.Clear();
    }

    /// <summary>入场起始态：把**整片**置为不可见（列表整体不透明度 = 0）。</summary>
    private void HideRealized() => _list.Opacity = 0;

    private void ResetAll()
    {
        StopRunning();

        // 恢复常态：整片可见。这是唯一"放行"的地方 —— 只要它跑到，界面就一定是可见的。
        _list.Opacity = 1;

        for (int index = 0; index < _list.Items.Count; index++)
        {
            if (_list.ContainerFromIndex(index) is UIElement container)
            {
                // 容器本身不再参与动画，恢复常态以防上一版残留（升级运行的极端情况）
                container.Opacity = 1;
            }
        }
    }

    /// <summary>三种观感的曲线（设置里只暴露这三种，不把缓动函数名暴露给用户）。</summary>
    internal static EasingFunctionBase CreateEasing(AnimationEasing easing) => easing switch
    {
        AnimationEasing.Soft => new SineEase { EasingMode = EasingMode.EaseOut },
        AnimationEasing.Snappy => new QuadraticEase { EasingMode = EasingMode.EaseOut },
        _ => new CubicEase { EasingMode = EasingMode.EaseOut },
    };
}
