// TabStripView.xaml.cs —— 标签栏的交互与"隐藏项"（图标 + 数量文字）三态
//
// ────────────────────────── 悬停判定为什么改成"标签栏统一判定" ──────────────────────────
// 以前是**每个标签各自**判定（模板上挂 PointerEntered/Moved/Exited），用户实测有两个问题：
//   ① 移到别的标签时，**上一个标签收不回去**（各自的退出事件互相打架，谁也没真正收起谁）；
//   ② 图标槽变宽会重排内容，WinUI 会在指针**没离开**时抛出 PointerExited → 反复展开/收起。
//
// 现在改成：**标签栏自己**在 PointerMoved 时算一次"指针在哪个标签上"
// （用"平移后的实际文字范围"做命中，而不是拿整个容器矩形），然后
// **只展开那一个、其余一律收起** —— 于是"同时最多一个标签展开"是结构性保证，不靠事件顺序。
// 判定只在"悬停目标发生变化"时才动手，避免每移动 1px 就重启动画（那是卡顿的来源）。
//
// ────────────────────────── 隐藏项 = 图标 + 数量文字（用户要求） ──────────────────────────
// 两者在同一个 `TabHiddenHost` 里，一起淡出/淡入、一起横向滑出/滑回。
// ⚠️ 滑动用**负左边距**而不是改宽度：改宽度会触发布局重排（抖动 + 卡顿），
//    负边距只影响渲染位置，标签的布局尺寸恒定。
//
// ⚠️ 悬停动效本身只能由用户目检（代理不注入鼠标，见 ERROR.md E5）。

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
    /// <summary>隐藏项滑动到位后占用的额外宽度（DIP）：图标 16 + 与文字的间距 6。</summary>
    private const double HiddenSlotOffset = 22;

    /// <summary>
    /// 指针范围核对的容差（DIP）。留一点点，避免正好压在边界上时判定跳变。
    /// </summary>
    private const double HitTolerance = 1;

    /// <summary>
    /// 拖拽悬停在某个标签上多久才自动切到那一页（毫秒）。
    /// ⚠️ 不能太短：用户可能只是拖过标签栏、想放到**当前页**的某个位置。
    /// </summary>
    private static readonly TimeSpan DragAutoSwitchDelay = TimeSpan.FromMilliseconds(550);

    /// <summary>拖拽"离开标签栏"的去抖时长：DragLeave 在项之间移动时会误报，用这个兜底取消。</summary>
    private static readonly TimeSpan DragLeaveGrace = TimeSpan.FromMilliseconds(260);

    /// <summary>一个标签的隐藏项宿主（供动画与命中判定使用）。</summary>
    private sealed class HiddenHost
    {
        public required Grid Root { get; init; }

        public required CompositeTransform Transform { get; init; }
    }

    private readonly Dictionary<TabItemViewModel, HiddenHost> _hosts = new();

    /// <summary>当前展开（显示隐藏项）的那个标签 —— 结构性保证"最多只有一个"。</summary>
    private TabItemViewModel? _expandedTab;

    private readonly Dictionary<TabItemViewModel, Storyboard> _animations = new();

    private TabIconMode _iconMode = TabIconMode.Hover;
    private AnimationSpec _spec = AnimationSpec.Disabled;

    private DispatcherQueueTimer? _dragAutoSwitchTimer;
    private DispatcherQueueTimer? _dragLeaveTimer;
    private TabItemViewModel? _dragOverTab;

    public TabStripView()
    {
        InitializeComponent();

        // 悬停判定统一在"标签栏"这一层做：容器上收 PointerMoved / PointerExited，
        // 这样"移到别的标签"和"离开整条标签栏"都由同一条逻辑处理。
        Tabs.PointerMoved += OnStripPointerMoved;
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

    /// <summary>拖拽悬停到某个标签上（主窗口据此切页）。</summary>
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

    // ────────────────────────────── 隐藏项的登记与三态 ──────────────────────────────

    private void OnHiddenHostLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid host || host.DataContext is not TabItemViewModel tab)
        {
            return;
        }

        if (host.RenderTransform is not CompositeTransform transform)
        {
            return;
        }

        _hosts[tab] = new HiddenHost { Root = host, Transform = transform };

        // 初始态：按当前形态（text = 收起；always = 展开）就位，不做动画
        bool expanded = _iconMode == TabIconMode.Always;
        ApplyHiddenState(tab, expanded, animate: false);
    }

    private void ApplyIconModeToAll()
    {
        foreach (var tab in _hosts.Keys.ToList())
        {
            StopAnimation(tab);
            ApplyHiddenState(tab, _iconMode == TabIconMode.Always, animate: false);
        }

        _expandedTab = _iconMode == TabIconMode.Always
            ? Tabs.Items.OfType<TabItemViewModel>().FirstOrDefault()
            : null;
    }

    /// <summary>把某个标签的隐藏项设置为展开/收起（可选动画）。</summary>
    private void ApplyHiddenState(TabItemViewModel tab, bool expanded, bool animate)
    {
        if (!_hosts.TryGetValue(tab, out var host))
        {
            return;
        }

        if (!animate || !_spec.Enabled)
        {
            StopAnimation(tab);
            host.Root.Opacity = expanded ? 1 : 0;
            host.Transform.TranslateX = expanded ? 0 : -HiddenSlotOffset;
            return;
        }

        StopAnimation(tab);

        var duration = new Duration(TimeSpan.FromMilliseconds(_spec.DurationMs));
        var easing = EntranceAnimator.CreateEasing(_spec.Easing);

        var fade = new DoubleAnimation
        {
            From = expanded ? 0 : 1,       // 显式起始值，别依赖"当前值"
            To = expanded ? 1 : 0,
            Duration = duration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(fade, host.Root);
        Storyboard.SetTargetProperty(fade, "Opacity");

        // ⚠️ 只动 TranslateX（渲染层），**不动 Width**：改宽度会触发布局重排，
        //    既卡顿又会让 WinUI 抛假的指针退出事件（用户反馈的抖动就是这么来的）。
        var slide = new DoubleAnimation
        {
            From = expanded ? -HiddenSlotOffset : 0,
            To = expanded ? 0 : -HiddenSlotOffset,
            Duration = duration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(slide, host.Transform);
        Storyboard.SetTargetProperty(slide, "TranslateX");

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
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

    // ────────────────────────────── 悬停判定（标签栏统一） ──────────────────────────────

    private void OnStripPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_iconMode != TabIconMode.Hover)
        {
            return;
        }

        var position = e.GetCurrentPoint(Tabs).Position;
        var hit = FindTabAt(position);

        if (ReferenceEquals(hit, _expandedTab))
        {
            return;   // 悬停目标没变 —— 不动手，避免每移动一点就重启动画（卡顿来源）
        }

        SetExpandedTab(hit);
    }

    /// <summary>指针离开整条标签栏：把所有隐藏项收起。</summary>
    private void OnStripPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_iconMode != TabIconMode.Hover)
        {
            return;
        }

        SetExpandedTab(null);
    }

    /// <summary>
    /// 保证"同时最多只有一个标签展开"：只展开 <paramref name="tab"/>，其余一律收起。
    /// 这是结构性保证，不依赖任何 pointer 事件的到达顺序。
    /// </summary>
    private void SetExpandedTab(TabItemViewModel? tab)
    {
        _expandedTab = tab;

        foreach (var candidate in _hosts.Keys.ToList())
        {
            bool expanded = ReferenceEquals(candidate, tab);
            ApplyHiddenState(candidate, expanded, animate: true);
        }
    }

    /// <summary>
    /// 指针落在哪个标签上。
    ///
    /// <para>判据用**平移后的实际文字范围**（而不是整个容器矩形）：
    /// 隐藏项展开后是把文字往右推，此时"文字左侧那块新增区域"并没有真的悬停在标签文字上，
    /// 用容器矩形会让判定范围越滚越大。</para>
    /// </summary>
    private TabItemViewModel? FindTabAt(Windows.Foundation.Point positionInStrip)
    {
        TabItemViewModel? hit = null;
        double bestDistance = double.MaxValue;

        foreach (var tab in Tabs.Items.OfType<TabItemViewModel>())
        {
            if (!TryGetTabBounds(tab, out var bounds))
            {
                continue;
            }

            bool insideX = positionInStrip.X >= bounds.Left - HitTolerance
                           && positionInStrip.X <= bounds.Right + HitTolerance;
            bool insideY = positionInStrip.Y >= bounds.Top - HitTolerance
                           && positionInStrip.Y <= bounds.Bottom + HitTolerance;

            if (!insideX || !insideY)
            {
                continue;
            }

            // 命中多项时取离中心最近的（理论上不会重叠，防御性处理）
            double center = (bounds.Left + bounds.Right) / 2;
            double distance = Math.Abs(positionInStrip.X - center);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                hit = tab;
            }
        }

        return hit;
    }

    /// <summary>取某个标签项在标签栏坐标系里的实际范围（容器级，含内边距）。</summary>
    private bool TryGetTabBounds(TabItemViewModel tab, out Windows.Foundation.Rect bounds)
    {
        bounds = default;

        try
        {
            if (Tabs.ContainerFromItem(tab) is not ListViewItem container
                || container.ActualWidth <= 0)
            {
                return false;
            }

            var origin = container
                .TransformToVisual(Tabs)
                .TransformPoint(new Windows.Foundation.Point(0, 0));

            bounds = new Windows.Foundation.Rect(
                origin.X, origin.Y, container.ActualWidth, container.ActualHeight);
            return true;
        }
        catch (Exception)
        {
            // 元素还没进可视树时会抛异常：当作"没命中"即可，不影响其它标签
            return false;
        }
    }

    // ────────────────────────────── 拖拽：悬停切页 + 丢到标签上 ──────────────────────────────
    //
    // 跨页移动的两条路：
    //   ① 拖到某个标签上停住 → 自动切到那一页 → 用户继续在新页面里选位置放下（位置可精确控制）；
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

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncSelection();

        if (Tabs.SelectedItem is TabItemViewModel tab)
        {
            TabSelected?.Invoke(this, tab);
        }
    }

    // ────────────────────────────── 视觉树小工具 ──────────────────────────────

    private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.Name == name)
            {
                return match;
            }

            var found = FindByName<T>(child, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
