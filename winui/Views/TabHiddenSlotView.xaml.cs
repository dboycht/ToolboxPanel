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
using Microsoft.UI.Xaml.Input;
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

    private TextBlock? _nameText;
    private TextBox? _nameEditor;

    public TabHiddenSlotView()
    {
        DefaultStyleKey = typeof(TabHiddenSlotView);
    }

    // ────────────────────────────── 就地改名（双击标签文字，2026-10-02）──────────────────────────────
    //
    // 与网格页的图块**刻意同一套语义**（进编辑 / Enter 或点别处提交 / Esc 取消 / 空名回退），
    // 区别只有一个：标签是"单击即选中"，没有"点文字会误触发打开"那件事 ⇒ 不需要延后。
    //
    // 分工：本控件只管"元素与焦点"，**提交与否由 TabStripView 交给宿主窗口**（它才拿得到 DataStore）。

    /// <summary>双击了名字：请求进入就地改名（调用方把 ViewModel 的 IsEditing 置上，状态再回流到这里）。</summary>
    public event EventHandler? RenameRequested;

    /// <summary>用户按了 Enter 或点到了别处：交上输入框里的字（是否真的要改由 Core 判）。</summary>
    public event EventHandler<string>? RenameCommitted;

    /// <summary>用户按了 Esc：放弃这次改名。</summary>
    public event EventHandler? RenameCanceled;

    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing), typeof(bool), typeof(TabHiddenSlotView),
        new PropertyMetadata(false, (d, _) => ((TabHiddenSlotView)d).ApplyEditState()));

    public static readonly DependencyProperty EditTextProperty = DependencyProperty.Register(
        nameof(EditText), typeof(string), typeof(TabHiddenSlotView), new PropertyMetadata(string.Empty));

    /// <summary>true = 正在就地改名（名字换成输入框）。</summary>
    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    /// <summary>进编辑那一刻要填进输入框的字（**模型里的原始名字**，不是带兜底的显示名）。</summary>
    public string EditText
    {
        get => (string)GetValue(EditTextProperty);
        set => SetValue(EditTextProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // 模板可能被重新套用 ⇒ 先摘旧元素上的处理器（本项目踩过"处理器反复累加"）
        if (_nameText is not null)
        {
            _nameText.DoubleTapped -= OnNameDoubleTapped;
        }

        if (_nameEditor is not null)
        {
            _nameEditor.KeyDown -= OnEditorKeyDown;
            _nameEditor.LostFocus -= OnEditorLostFocus;
        }

        _slotHost = GetTemplateChild("SlotHost") as Grid;
        _nameText = GetTemplateChild("NameText") as TextBlock;
        _nameEditor = GetTemplateChild("NameEditor") as TextBox;

        if (_nameText is not null)
        {
            _nameText.DoubleTapped += OnNameDoubleTapped;
        }

        if (_nameEditor is not null)
        {
            _nameEditor.KeyDown += OnEditorKeyDown;
            _nameEditor.LostFocus += OnEditorLostFocus;
        }

        ApplyState(animate: false);
        ApplyEditState();
    }

    private void OnNameDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IsEditing)
        {
            return;
        }

        e.Handled = true;   // 别让这一下冒泡到标签项（避免被当成"又一次选中"之类的动作）
        RenameRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!IsEditing)
        {
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            RenameCommitted?.Invoke(this, _nameEditor?.Text ?? string.Empty);
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            RenameCanceled?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnEditorLostFocus(object sender, RoutedEventArgs e)
    {
        // 点别处 = 提交；⚠️ 提交/取消之后会把输入框收起，那次失焦必须被这个判断挡掉（否则重复提交）
        if (!IsEditing)
        {
            return;
        }

        RenameCommitted?.Invoke(this, _nameEditor?.Text ?? string.Empty);
    }

    /// <summary>在"名字 / 输入框"之间切换；进入编辑时聚焦并全选。</summary>
    private void ApplyEditState()    {
        if (_nameText is null || _nameEditor is null)
        {
            return;
        }

        var editing = IsEditing;
        _nameText.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        _nameEditor.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        if (!editing)
        {
            return;
        }

        // 没名字的标签：给一句提示（用当前显示名当占位，不新增文案 key）
        _nameEditor.PlaceholderText = TabName;

        // 输入框先对齐名字的宽度，避免"进编辑时整条标签跳一下"
        if (_nameText.ActualWidth > 0)
        {
            _nameEditor.MinWidth = Math.Max(72, _nameText.ActualWidth + 12);
        }

        _nameEditor.Focus(FocusState.Programmatic);
        _nameEditor.SelectAll();
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
