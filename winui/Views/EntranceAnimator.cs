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

/// <summary>负责一个列表/网格页面的入场动效。</summary>
internal sealed class EntranceAnimator
{
    private readonly ListViewBase _list;
    private readonly HashSet<object> _played = new();

    private AnimationSpec _spec = AnimationSpec.Disabled;
    private bool _pending;

    public EntranceAnimator(ListViewBase list)
    {
        _list = list;
        _list.ContainerContentChanging += OnContainerContentChanging;
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

    /// <summary>播放一次入场（每次切到该页都会调用）。</summary>
    public void Play()
    {
        _played.Clear();

        if (!_spec.Enabled)
        {
            ResetAll();
            return;
        }

        _pending = true;

        // ⚠️ 关键（用户实测反馈的不协调）：**同步**把已实现的容器置为起始态，再起动画。
        //    否则页面会先以"最终态"渲染一帧，随后动画又把它拉回 0 重新播一遍 ——
        //    看起来就是"先显示出来、然后才播动画"。设 Opacity / Transform 不需要布局，同一帧即可完成。
        HideRealized();
        PlayRealized();
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
        storyboard.Children.Add(CreateAnimation(container, "Opacity", 1, duration, beginTime, easing));
        storyboard.Children.Add(CreateAnimation(transform, "TranslateY", 0, duration, beginTime, easing));
        storyboard.Begin();
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
        _pending = false;
        _played.Clear();

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
        DependencyObject target, string property, double to, Duration duration, TimeSpan beginTime, EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
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
