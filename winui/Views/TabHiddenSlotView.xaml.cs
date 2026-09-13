// TabHiddenSlotView.cs —— 标签里的"隐藏项"（图标 + 数量文字）控件
//
// 用户要求：**图标与数量文字视为同一个隐藏项** —— 鼠标移到标签上时一起出现、移开时一起收起。
//
// ────────────────────────── 走过的弯路（别再重复） ──────────────────────────
//   ① 在 `TabStripView` 里按名字从模板根找元素 → **取到复用模板里的错误实例**（不同标签互相串）。
//   ② 做成 `UserControl` → 尺寸由 UserControl 自己算，宽度被固定、名字被挤进同一块里。
//   ③ 用 Grid 当模板根（名字与轨道同格）→ Grid 宽度取较大者，**名字宽度根本没算进去**。
//   ④ 用 StackPanel 顺序排但**轨道宽度恒定 66** → 收起时留出 66 死空隙，整条标签栏被撑开。
//   ⑤ 用渲染位移（TranslateX）把内容"移出裁剪区" → 轨道是 StackPanel 的一个子元素，
//      位移只改变绘制位置、不改变占位 ⇒ 视觉错位。
// 最终采用最朴素也最可靠的一条：**槽的宽度就是它的占位**，
// 收起时宽 0（图标/数量文字在宽 0 的裁剪区里，完全看不见、不占位），
// 展开时动画到 66 —— 名字作为它的兄弟自然被推到右边。**只有槽宽参与布局**。
//
// ────────────────────────── 三条交互要求怎么满足 ──────────────────────────
//   1. 图标与数量文字在**同一个槽**里 ⇒ 一定同进同出（用户要求）；
//   2. 悬停判定由**标签栏统一做**（TabStripView），只展开一个、其余收起
//      ⇒ "移到别的标签时上一个立刻收起"是结构性保证；
//   3. 判定只在"悬停目标变化"时动手 ⇒ 不会每移动 1px 就重启动画（卡顿来源）。
//
// ⚠️ 槽宽动画会触发布局（`EnableDependentAnimation` 必须显式打开），
//    但槽只有 66 DIP、且**槽内容 `IsHitTestVisible=False`**，
//    所以既不会引发假的指针退出事件，也不会把相邻标签推得很远。

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace ToolboxPanel.Views;

/// <summary>标签里的隐藏项：图标 + 数量文字（一起出现 / 一起收起）。</summary>
public sealed partial class TabHiddenSlotView : Control
{
    /// <summary>隐藏项展开后的宽度（DIP）：图标 16 + 间距 6 + 数量文字约 44。</summary>
    private const double SlotWidth = 66;

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(TabHiddenSlotView), new PropertyMetadata("\uE80A"));

    public static readonly DependencyProperty CountLabelProperty = DependencyProperty.Register(
        nameof(CountLabel), typeof(string), typeof(TabHiddenSlotView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowCountProperty = DependencyProperty.Register(
        nameof(ShowCount), typeof(bool), typeof(TabHiddenSlotView), new PropertyMetadata(true));

    public static readonly DependencyProperty TabNameProperty = DependencyProperty.Register(
        nameof(TabName), typeof(string), typeof(TabHiddenSlotView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(TabHiddenSlotView),
        new PropertyMetadata(false, (d, _) => ((TabHiddenSlotView)d).ApplyState(animate: true)));

    public static readonly DependencyProperty DurationMsProperty = DependencyProperty.Register(
        nameof(DurationMs), typeof(int), typeof(TabHiddenSlotView), new PropertyMetadata(220));

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string CountLabel
    {
        get => (string)GetValue(CountLabelProperty);
        set => SetValue(CountLabelProperty, value);
    }

    public bool ShowCount
    {
        get => (bool)GetValue(ShowCountProperty);
        set => SetValue(ShowCountProperty, value);
    }

    /// <summary>标签名（与隐藏项在同一个控件里，名字跟着槽宽自然让位）。</summary>
    public string TabName
    {
        get => (string)GetValue(TabNameProperty);
        set => SetValue(TabNameProperty, value);
    }

    /// <summary>true = 展开（图标 + 数量文字可见）。</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>动画时长（毫秒），由标签栏按设置统一下发。</summary>
    public int DurationMs
    {
        get => (int)GetValue(DurationMsProperty);
        set => SetValue(DurationMsProperty, value);
    }

    private Grid? _slotHost;
    private Storyboard? _storyboard;

    public TabHiddenSlotView()
    {
        DefaultStyleKey = typeof(TabHiddenSlotView);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _slotHost = GetTemplateChild("SlotHost") as Grid;
        ApplyState(animate: false);
    }

    private void ApplyState(bool animate)
    {
        if (_slotHost is null)
        {
            return;
        }

        double target = IsExpanded ? SlotWidth : 0;

        _storyboard?.Stop();
        _storyboard = null;

        // 先无条件写目标值：动画只是让它更顺，不该决定"对不对"
        _slotHost.Width = target;
        _slotHost.Opacity = IsExpanded ? 1 : 0;

        if (!animate || DurationMs <= 0)
        {
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(DurationMs));

        var width = new DoubleAnimation
        {
            From = IsExpanded ? 0 : SlotWidth,   // 显式起始值，不依赖"当前值/上一轮残留值"
            To = target,
            Duration = duration,

            // 宽度动画会触发布局（"槽撑开、名字让位"就是要它发生），必须显式允许
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(width, _slotHost);
        Storyboard.SetTargetProperty(width, "Width");

        var fade = new DoubleAnimation
        {
            From = IsExpanded ? 0 : 1,
            To = IsExpanded ? 1 : 0,
            Duration = duration,
        };
        Storyboard.SetTarget(fade, _slotHost);
        Storyboard.SetTargetProperty(fade, "Opacity");

        _storyboard = new Storyboard();
        _storyboard.Children.Add(width);
        _storyboard.Children.Add(fade);
        _storyboard.Begin();
    }
}
