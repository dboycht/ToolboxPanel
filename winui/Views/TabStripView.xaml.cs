// TabStripView.xaml.cs —— 标签栏的交互与三种图标形态
//
// ⚠️ 这里修掉了一个真实 bug（用户实测）：
//   "把鼠标悬停在标签文字上并移动时，会反复拉伸缩小往复，但鼠标并没有离开这个标签"。
//
// 根因：图标槽变宽 → 标签内容整体平移 + 控件重排 → WinUI 在指针**没有真的离开**的情况下
//       抛出 PointerExited → 收起动画 → 指针又"回到"里面 → PointerEntered → 展开……
//       形成 展开/收起 的高频往复；旧实现每次 PointerEntered 都**重启动画**，让抖动更明显。
//
// 修法（四件一起做）：
//   1. **状态幂等**：用 `_expanded` 记录当前是否已展开，重复的"显示"请求直接返回，
//      **不重启动画**（重启动画本身就是"很怪"的来源之一）；
//   2. **范围复核**：PointerExited 时先看指针是否仍在本项范围内（带容差），在里面就忽略这次退出；
//   3. **去抖**：刚展开后极短时间内的"退出"忽略掉（防止动画期间的假退出）；
//   4. 图标槽 `IsHitTestVisible=False`：槽的增长不参与命中测试。

using System;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ToolboxPanel.Core.Storage;
using ToolboxPanel.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace ToolboxPanel.Views;

public sealed partial class TabStripView : UserControl
{
    /// <summary>图标槽展开后的宽度（DIP）。</summary>
    private const double GlyphSlotWidth = 16;

    /// <summary>
    /// 指针范围复核的容差（DIP）：只有指针"明显离开"才算离开。
    /// ⚠️ 不要用"时间去抖"来忽略退出 —— 那会把**真实退出**一起吞掉，
    /// 表现就是"鼠标走开了图标还挂着"（用户实测反馈）。退出与否只看坐标。
    /// </summary>
    private const double ExitTolerance = 2;

    /// <summary>
    /// 拖拽悬停在某个标签上多久才自动切到那一页（毫秒）。
    /// ⚠️ 不能太短：用户可能只是拖过标签栏、想放到**当前页**的某个位置。
    /// </summary>
    private static readonly TimeSpan DragAutoSwitchDelay = TimeSpan.FromMilliseconds(550);

    /// <summary>拖拽"离开标签栏"的去抖时长：DragLeave 在项之间移动时会误报，用这个兜底取消。</summary>
    private static readonly TimeSpan DragLeaveGrace = TimeSpan.FromMilliseconds(260);

    private readonly Dictionary<TabItemViewModel, Border> _glyphHosts = new();
    private readonly HashSet<TabItemViewModel> _expanded = new();
    private readonly Dictionary<TabItemViewModel, Storyboard> _animations = new();

    private TabIconMode _iconMode = TabIconMode.Hover;
    private AnimationSpec _spec = AnimationSpec.Disabled;

    private DispatcherQueueTimer? _dragAutoSwitchTimer;
    private DispatcherQueueTimer? _dragLeaveTimer;
    private TabItemViewModel? _dragOverTab;

    public TabStripView()
    {
        InitializeComponent();

        // 兜底：指针离开整条标签栏时，把所有展开项收起来
        // （万一某项没收到 PointerExited，也不会留下"图标一直挂着"的状态）
        Tabs.PointerExited += OnStripPointerExited;

        _dragAutoSwitchTimer = DispatcherQueue.CreateTimer();
        _dragAutoSwitchTimer.Interval = DragAutoSwitchDelay;
        _dragAutoSwitchTimer.IsRepeating = false;
        _dragAutoSwitchTimer.Tick += OnDragAutoSwitchTick;

        _dragLeaveTimer = DispatcherQueue.CreateTimer();
        _dragLeaveTimer.Interval = DragLeaveGrace;
        _dragLeaveTimer.IsRepeating = false;
        _dragLeaveTimer.Tick += OnDragLeaveGraceTick;
    }

    /// <summary>选中项变化（主窗口据此切页）。</summary>
    public event EventHandler<TabItemViewModel>? TabSelected;

    /// <summary>拖拽悬停到某个标签上（主窗口据此切页；拖拽过程中不改变用户自己的点击选择语义）。</summary>
    public event EventHandler<TabItemViewModel>? TabDraggedOver;

    /// <summary>把图标/列表项直接丢在某个标签上 —— 主窗口把它追加到那一页末尾。</summary>
    public event EventHandler<(DragPayload Payload, TabItemViewModel Tab)>? ItemDroppedOnTab;

    public object? ItemsSource
    {
        get => Tabs.ItemsSource;
        set => Tabs.ItemsSource = value;
    }

    public IReadOnlyList<TabItemViewModel> Tabs_Items =>
        Tabs.Items.OfType<TabItemViewModel>().ToList();

    public int SelectedIndex
    {
        get => Tabs.SelectedIndex;
        set => Tabs.SelectedIndex = value;
    }

    public int ItemCount => Tabs.Items.Count;

    public TabItemViewModel? SelectedTab
    {
        get => Tabs.SelectedItem as TabItemViewModel;
        set => Tabs.SelectedItem = value;
    }

    /// <summary>套用设置：图标形态、是否显示数量、动效参数。</summary>
    public void ApplySettings(TabIconMode iconMode, bool showCounts, AnimationSpec spec)
    {
        _iconMode = iconMode;
        _spec = spec;

        foreach (var tab in Tabs.Items.OfType<TabItemViewModel>())
        {
            tab.ShowCount = showCounts;
        }

        ApplyIconModeToAll();
    }

    /// <summary>
    /// 套用主题令牌。
    ///
    /// <para>为什么标签栏要单独处理：WinUI 的**轻量样式覆盖**（同名资源键）**不会**跟着
    /// <c>RequestedTheme</c> 自动变色 —— 我们覆盖的那几个键是本控件的资源，
    /// 浅色主题下不换就会"选中标签白字白底"看不见。</para>
    /// </summary>
    public void ApplyTheme(ThemePalette palette)
    {
        void Set(string key, (byte A, byte R, byte G, byte B) value)
            => Resources[key] = new SolidColorBrush(
                Windows.UI.Color.FromArgb(value.A, value.R, value.G, value.B));

        Set("ListViewItemBackgroundSelected", palette.SelectedSurface);
        Set("ListViewItemBackgroundSelectedPointerOver", palette.SelectedSurface);
        Set("ListViewItemBackgroundSelectedPressed", palette.PressedSurface);
        Set("ListViewItemBackgroundPointerOver", palette.HoverSurface);
        Set("ListViewItemBackgroundPressed", palette.PressedSurface);

        // 选中标签的文字/图标前景：深色下是白、浅色下必须是黑，否则看不见
        Set("ListViewItemForegroundSelected", palette.TitleBarButtonForeground);

        // 标签栏外框的底与描边
        Set("StripSurfaceBrush", palette.TabStripSurface);
        Set("StripBorderBrush", palette.TabStripBorder);
        StripSurface.Background = Resources["StripSurfaceBrush"] as Brush;
        StripSurface.BorderBrush = Resources["StripBorderBrush"] as Brush;
    }

    /// <summary>主窗口在切换选中项后调用，用于同步强调条。</summary>
    public void SyncSelection()
    {
        foreach (var tab in Tabs.Items.OfType<TabItemViewModel>())
        {
            tab.IsSelected = ReferenceEquals(tab, Tabs.SelectedItem);
        }
    }

    // ────────────────────────────── 三种图标形态 ──────────────────────────────

    private void OnGlyphHostLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border host && host.DataContext is TabItemViewModel tab)
        {
            _glyphHosts[tab] = host;
            SetExpanded(tab, expand: _iconMode == TabIconMode.Always, animate: false);
        }
    }

    private void ApplyIconModeToAll()
    {
        foreach (var (tab, host) in _glyphHosts)
        {
            StopAnimation(tab);
            bool expand = _iconMode == TabIconMode.Always;
            host.Width = expand ? GlyphSlotWidth : 0;
            host.Opacity = expand ? 1 : 0;
            if (expand)
            {
                _expanded.Add(tab);
            }
            else
            {
                _expanded.Remove(tab);
            }
        }
    }

    private void OnItemPointerEntered(object sender, PointerRoutedEventArgs e) => RequestExpand(sender, expand: true);

    /// <summary>移动时也请求展开：幂等，用来补偿动画期间的假退出（自我修复）。</summary>
    private void OnItemPointerMoved(object sender, PointerRoutedEventArgs e) => RequestExpand(sender, expand: true);

    private void OnItemPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement root || root.DataContext is not TabItemViewModel tab)
        {
            return;
        }

        if (_iconMode != TabIconMode.Hover || !_expanded.Contains(tab))
        {
            return;
        }

        var position = e.GetCurrentPoint(root).Position;
        bool stillInside =
            position.X >= -ExitTolerance && position.X <= root.ActualWidth + ExitTolerance &&
            position.Y >= -ExitTolerance && position.Y <= root.ActualHeight + ExitTolerance;

        // 指针确实还在项内（多半是展开动画改动布局导致的"假退出"）→ 忽略；
        // 真的离开了 → 立刻收起（不做时间去抖，否则会把真实退出吞掉）
        if (stillInside)
        {
            return;
        }

        SetExpanded(tab, expand: false, animate: true);
    }

    /// <summary>指针离开整条标签栏：把所有展开项收起来（兜底，防止残留展开态）。</summary>
    private void OnStripPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_iconMode != TabIconMode.Hover)
        {
            return;
        }

        foreach (var tab in _expanded.ToList())
        {
            SetExpanded(tab, expand: false, animate: true);
        }
    }

    private void RequestExpand(object sender, bool expand)
    {
        if (_iconMode != TabIconMode.Hover)
        {
            return;
        }

        if (sender is FrameworkElement root && root.DataContext is TabItemViewModel tab)
        {
            SetExpanded(tab, expand, animate: true);
        }
    }

    private void SetExpanded(TabItemViewModel tab, bool expand, bool animate)
    {
        if (!_glyphHosts.TryGetValue(tab, out var host))
        {
            return;
        }

        if (expand == _expanded.Contains(tab))
        {
            return;   // 状态没变：不重启动画（重启就是"抖动/很怪"的主因）
        }

        if (expand)
        {
            _expanded.Add(tab);
        }
        else
        {
            _expanded.Remove(tab);
        }

        if (!animate || !_spec.Enabled)
        {
            StopAnimation(tab);
            host.Width = expand ? GlyphSlotWidth : 0;
            host.Opacity = expand ? 1 : 0;
            return;
        }

        StopAnimation(tab);

        var duration = new Duration(TimeSpan.FromMilliseconds(_spec.DurationMs));
        var easing = EntranceAnimator.CreateEasing(_spec.Easing);

        var widthAnimation = new DoubleAnimation
        {
            From = expand ? 0 : GlyphSlotWidth,       // 显式起始值，别依赖"当前值"
            To = expand ? GlyphSlotWidth : 0,
            Duration = duration,
            EasingFunction = easing,

            // 宽度动画会触发布局（"拉伸"就是要它发生），必须显式允许
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(widthAnimation, host);
        Storyboard.SetTargetProperty(widthAnimation, "Width");

        var opacityAnimation = new DoubleAnimation
        {
            From = expand ? 0 : 1,
            To = expand ? 1 : 0,
            Duration = duration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(opacityAnimation, host);
        Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(widthAnimation);
        storyboard.Children.Add(opacityAnimation);
        _animations[tab] = storyboard;
        storyboard.Begin();
    }

    private void StopAnimation(TabItemViewModel tab)
    {
        if (_animations.Remove(tab, out var running))
        {
            running.Stop();
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncSelection();

        if (Tabs.SelectedItem is TabItemViewModel tab)
        {
            TabSelected?.Invoke(this, tab);
        }
    }

    // ────────────────────────────── 拖拽：悬停切页 + 丢到标签上 ──────────────────────────────
    //
    // 跨页移动的两条路：
    //   ① 拖到某个标签上停住 → 自动切到那一页 → 用户继续在新页面里选位置放下（推荐，位置可精确控制）；
    //   ② 直接松手在标签上 → 追加到那一页末尾（快手操作）。
    // ⚠️ 悬停判定用**定时器**而不是 PointerEntered：拖拽期间指针事件与普通指针事件是两套，
    //    这里以 DragOver 为准（每次移动都会重置定时器，停住才开始计时）。

    private void OnTabsDragOver(object sender, DragEventArgs e)
    {
        if (!TryReadPayload(e, out var payload))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            StopDragTimers();
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;

        // 指针还在标签栏区域内 → 取消"离开"的兜底计时
        _dragLeaveTimer?.Stop();
        _dragLeaveTimer?.Start();

        var tab = FindTabFromArgs(e);
        if (tab is null || ReferenceEquals(tab, _dragOverTab))
        {
            return;
        }

        // 换了一个标签 → 重新开始计时（拖过标签栏时不会乱切页）
        _dragOverTab = tab;
        _dragAutoSwitchTimer?.Stop();
        _dragAutoSwitchTimer?.Start();
    }

    /// <summary>项之间的 DragLeave 会误报，所以离开与否交给去抖定时器判定。</summary>
    private void OnTabsDragLeave(object sender, DragEventArgs e)
    {
        // 什么都不做：真正"离开整条标签栏"由 OnDragLeaveGraceTick 处理
    }

    private void OnDragLeaveGraceTick(DispatcherQueueTimer sender, object args)
    {
        _dragLeaveTimer?.Stop();
        _dragOverTab = null;
        _dragAutoSwitchTimer?.Stop();
    }

    private void OnDragAutoSwitchTick(DispatcherQueueTimer sender, object args)
    {
        _dragAutoSwitchTimer?.Stop();

        if (_dragOverTab is { } tab)
        {
            TabDraggedOver?.Invoke(this, tab);
        }
    }

    private void OnTabsDrop(object sender, DragEventArgs e)
    {
        StopDragTimers();

        if (!TryReadPayload(e, out var payload))
        {
            return;
        }

        if (FindTabFromArgs(e) is { } tab)
        {
            ItemDroppedOnTab?.Invoke(this, (payload, tab));
        }
    }

    private void StopDragTimers()
    {
        _dragAutoSwitchTimer?.Stop();
        _dragLeaveTimer?.Stop();
        _dragOverTab = null;
    }

    /// <summary>载荷来自本应用、且格式合法才接受（具体"哪一页收哪一类"由主窗口判）。</summary>
    private static bool TryReadPayload(DragEventArgs e, out DragPayload payload)
    {
        payload = null!;

        try
        {
            if (!e.DataView.Contains(StandardDataFormats.Text))
            {
                return false;
            }

            var parsed = DragPayload.TryParse(e.DataView.GetTextAsync().AsTask().GetAwaiter().GetResult());
            if (parsed is null)
            {
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (Exception ex)
        {
            App.WriteCrash("TabStripView.TryReadPayload", ex);
            return false;
        }
    }

    /// <summary>这次 DragOver 落在哪个标签项上（落在标签栏空白处 → null）。</summary>
    private static TabItemViewModel? FindTabFromArgs(DragEventArgs e)
    {
        var element = e.OriginalSource as FrameworkElement;
        while (element is not null)
        {
            if (element.DataContext is TabItemViewModel tab)
            {
                return tab;
            }

            element = element.Parent as FrameworkElement;
        }

        return null;
    }
}
