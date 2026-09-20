// TabStripView.xaml.cs —— 标签栏的交互与"隐藏项"（图标 + 数量文字）三态
//
// ────────────────────────── 悬停判定：由标签栏**统一**判定 ──────────────────────────
// 用户实测的两个问题：
//   ① 移到别的标签时**上一个收不回去**（每个标签各自 PointerEntered/Exited 互相打架）；
//   ② 图标槽变宽会重排内容，WinUI 在指针没离开时抛假退出 → 反复展开/收起。
// 现在：标签栏自己在 PointerMoved 里算一次"指针在哪个标签上"（用**整个标签项**的范围命中，
// 用户要的是"鼠标在标签上就展开"），然后**只展开那一个、其余一律收起** ——
// "同时最多一个展开"是结构性保证，不靠事件顺序。
// 判定只在"悬停目标变化"时才动手，避免每移动 1px 就重启动画。
//
// ────────────────────────── 隐藏项 = 图标 + 数量文字 ──────────────────────────
// 由每个标签里的 `TabHiddenSlotView` 自己管（见该文件的说明）。
// 它的轨道宽度恒定、只动渲染位移，所以**标签项的布局尺寸永远不变**：
// 不卡、不抖、也不会把相邻标签推走。
//
// ⚠️ 悬停动效本身只能由用户目检（代理不注入鼠标，见 ERROR.md E5）；
//    排查这类"看不见的布局问题"时用 `--hover-tab=N` 强制展开某个标签来截图核对。

using System;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;
using ToolboxPanel.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace ToolboxPanel.Views;

public sealed partial class TabStripView : UserControl
{
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

    /// <summary>每个标签的隐藏项控件（自己管"图标 + 数量文字"的出现与名字让位）。</summary>
    private readonly Dictionary<TabItemViewModel, TabHiddenSlotView> _slots = new();

    /// <summary>
    /// 当前的图标形态（`static`）。
    ///
    /// <para>⚠️ 为什么是 static：`ApplySettings` 往往**早于**标签项的模板被实现
    /// （设置是装配阶段下发的，标签项的 Loaded 在布局之后），那时 `_slots` 还是空的；
    /// 而 `OnHiddenSlotViewLoaded` 又需要知道"我现在应该收起还是展开"。
    /// 用一个模块级状态让**后加载的控件也能取到当前形态**，比"先加载的记住"更不容易错位。
    /// （这个 bug 真出现过：`--tab-icons=always` 下名字不让位，图标与名字叠在一起。）</para>
    /// </summary>
    private static TabIconMode CurrentIconMode = TabIconMode.Hover;

    /// <summary>当前展开（显示隐藏项）的那个标签 —— 结构性保证"最多只有一个"。</summary>
    private TabItemViewModel? _expandedTab;

    /// <summary>`--hover-tab=N` 请求的展开目标（等模板登记完成后再应用）。</summary>
    private TabItemViewModel? _pendingHoverTab;

    private TabIconMode _iconMode = TabIconMode.Hover;
    private AnimationSpec _spec = AnimationSpec.Disabled;

    private DispatcherQueueTimer? _dragAutoSwitchTimer;
    private DispatcherQueueTimer? _dragLeaveTimer;
    private TabItemViewModel? _dragOverTab;

    public TabStripView()
    {
        InitializeComponent();

        // 悬停判定统一在"标签栏"这一层做：这样"移到别的标签"和"离开整条标签栏"
        // 由同一条逻辑处理，不会出现"上一个收不回去"。
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

    /// <summary>
    /// 标签页右键菜单选了某一项（批次 1）。
    /// <para>页面**只发事件**：弹对话框、落库、刷新页面都在宿主窗口（MainWindow）里做 ——
    /// 与图块菜单 / 列表行菜单同一分工（页面拿不到 DataStore）。</para>
    /// </summary>
    public event EventHandler<TabMenuRequest>? TabMenuActionRequested;

    // ⚠️ 2026-09-16：原来还有一个 `ItemDroppedOnTab`（松手在标签上直接落库）。
    //    现在"松手在标签上"由 `DragSession.ReportTarget` 登记落点、源端 `DragItemsCompleted` 统一收口，
    //    这条独立路径已删除（避免两套机制并存；落库入口仍只有 MainWindow.OnItemDropped 一个）。

    public object? ItemsSource
    {
        get => Tabs.ItemsSource;
        set
        {
            // ⚠️ 换数据源 = 上一代标签视图模型全部作废（导入备份后 `MainViewModel.Load()`
            //    会 `Tabs.Clear()` 并重建每一个 `TabItemViewModel`）。
            //    `_slots` 是**强引用**字典，而它的写入点是模板的 `Loaded` —— 旧一代的
            //    TabItemViewModel（连同它持有的 Icons 集合与图标位图引用）只能被这张表钉住。
            //    不清就是"每导入一次备份泄漏一整代"，而且 `_expandedTab` / `_pendingHoverTab`
            //    还会指着已经不在列表里的旧对象。
            _slots.Clear();
            _expandedTab = null;
            _pendingHoverTab = null;

            Tabs.ItemsSource = value;
        }
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
        CurrentIconMode = iconMode;

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
    ///
    /// <para>参数里还带两个主题度量：<paramref name="radiusScale"/>（`radius` 参数倍率，
    /// 落到标签栏外框与标签项的圆角）与 <paramref name="hoverScale"/>（`hover_ms` 参数倍率，
    /// 只用在"图标槽展开/收起"这个悬停动画上；默认都是 1.0 ⇒ 与现在一模一样）。</para>
    /// </summary>
    public void ApplyTheme(ThemePalette palette, double radiusScale = 1, double hoverScale = 1)
    {
        void Set(string key, (byte A, byte R, byte G, byte B) value)
            => Resources[key] = new SolidColorBrush(
                Windows.UI.Color.FromArgb(value.A, value.R, value.G, value.B));

        Set("ListViewItemBackgroundSelected", palette.SelectedSurface);
        Set("ListViewItemBackgroundSelectedPointerOver", palette.SelectedSurface);
        Set("ListViewItemBackgroundSelectedPressed", palette.PressedSurface);
        Set("ListViewItemBackgroundPointerOver", palette.HoverSurface);
        Set("ListViewItemBackgroundPressed", palette.PressedSurface);

        // ⚠️ 选中标签的**文字/图标**前景不在这里管：
        //    过去是覆盖轻量样式键 `ListViewItemForegroundSelected`，但它切主题时不会重新解析
        //    （实测：浅色下选中标签仍是白字 rgb(152,152,152)，未选中已是黑字 rgb(16,16,16)）。
        //    现在由 TabHiddenSlotView 的模板显式声明框架主题资源 ⇒ 跟随元素主题，必然正确。
        //    详见 ERROR.md E19。

        // 标签栏外框的底与描边
        Set("StripSurfaceBrush", palette.TabStripSurface);
        Set("StripBorderBrush", palette.TabStripBorder);
        StripSurface.Background = Resources["StripSurfaceBrush"] as Brush;
        StripSurface.BorderBrush = Resources["StripBorderBrush"] as Brush;

        // 选中强调条：模板里是 `{ThemeResource AccentBrushDark}`（本项目注入的固定值键，不会自己变）
        // ⇒ 既更新本控件资源里的同名键（之后新实现/回收出来的容器用得上），
        //    也**直接改已经实现出来的那些条**（存量元素不会因资源变化重新解析，实测过）。
        var accent = new SolidColorBrush(
            Windows.UI.Color.FromArgb(palette.Accent.A, palette.Accent.R, palette.Accent.G, palette.Accent.B));
        Resources["AccentBrushDark"] = accent;
        ApplyAccentToRealizedSelectionBars(accent);

        // 主题圆角 + 悬停时长（默认参数下这两步都是恒等变换）
        _cornerScale = radiusScale;
        _hoverScale = hoverScale;
        StripSurface.CornerRadius = ThemeScale.Corners(DesignStripCornerRadius, _cornerScale);
        ApplyCornerRadiusToRealizedTabs();
        ApplyHoverDurationToSlots();   // 只改时长，不动展开状态（别把正在悬停的那一项收起来）
    }

    /// <summary>把新的悬停时长下发给已登记的图标槽（`hover_ms` 参数）。</summary>
    private void ApplyHoverDurationToSlots()
    {
        foreach (var (_, slot) in _slots)
        {
            slot.DurationMs = HoverDurationMs;
        }
    }

    /// <summary>标签栏外框 / 标签项的设计圆角（DIP）。</summary>
    private const double DesignStripCornerRadius = 6;

    private const double DesignTabCornerRadius = 5;

    /// <summary>主题圆角倍率（`radius` 参数；默认 1.0）。</summary>
    private double _cornerScale = 1;

    /// <summary>主题悬停时长倍率（`hover_ms` 参数；默认 1.0）。</summary>
    private double _hoverScale = 1;

    /// <summary>图标槽的悬停动画时长 =「动效」节的时长 × 主题倍率。</summary>
    private int HoverDurationMs => ThemeScale.HoverDuration(_spec.DurationMs, _hoverScale);

    /// <summary>把圆角写给"已经实现出来"的标签项容器（新实现/回收的走 ContainerContentChanging）。</summary>
    private void ApplyCornerRadiusToRealizedTabs()
    {
        for (int i = 0; i < Tabs.Items.Count; i++)
        {
            if (Tabs.ContainerFromIndex(i) is Control container)
            {
                container.CornerRadius = ThemeScale.Corners(DesignTabCornerRadius, _cornerScale);
            }
        }
    }

    /// <summary>把强调条颜色直接写到"已经实现出来"的标签项上（含回收复用的容器）。</summary>
    private void ApplyAccentToRealizedSelectionBars(Brush accent)
    {
        for (int i = 0; i < Tabs.Items.Count; i++)
        {
            if (Tabs.ContainerFromIndex(i) is not DependencyObject container)
            {
                continue;
            }

            if (FindByName(container, "SelectionBar") is Border bar)
            {
                bar.Background = accent;
            }
        }
    }

    /// <summary>在**某个容器自己的子树里**找名字（不是跨容器找，见 TabHiddenSlotView 的注释）。</summary>
    private static DependencyObject? FindByName(DependencyObject root, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && element.Name == name)
            {
                return child;
            }

            if (FindByName(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>主窗口在切换选中项后调用，用于同步强调条。</summary>
    public void SyncSelection()
    {
        foreach (var tab in Tabs.Items.OfType<TabItemViewModel>())
        {
            tab.IsSelected = ReferenceEquals(tab, Tabs.SelectedItem);
        }
    }

    /// <summary>
    /// 开发/验证用：强制把第 <paramref name="index"/> 个标签置为"展开"（`--hover-tab=N`）。
    /// 用途：悬停态**没法用工具稳定复现**（合成鼠标注入对 WinUI 时灵时不灵，
    /// 而真实悬停又必须由用户手点），留一个开关让布局本身可被截图核对
    /// （本轮"名字与图标重叠"就是靠它抓出来的）。
    /// </summary>
    public void ForceHoverTab(int index)
    {
        var tabs = Tabs.Items.OfType<TabItemViewModel>().ToList();
        _pendingHoverTab = index >= 0 && index < tabs.Count ? tabs[index] : null;

        // 模板可能还没实现出来 —— 那就等登记完成后统一应用
        if (_pendingHoverTab is null || _slots.ContainsKey(_pendingHoverTab))
        {
            ApplyPendingHoverTab();
        }
    }

    private void ApplyPendingHoverTab()
    {
        if (_pendingHoverTab is not null && _slots.ContainsKey(_pendingHoverTab))
        {
            SetExpandedTab(_pendingHoverTab);
            _pendingHoverTab = null;
        }
    }

    // ────────────────────────────── 隐藏项的三态 ──────────────────────────────

    private void OnHiddenSlotViewLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TabHiddenSlotView slot || slot.DataContext is not TabItemViewModel tab)
        {
            return;
        }

        slot.DurationMs = HoverDurationMs;
        _slots[tab] = slot;

        // 主题圆角：标签项容器（ListViewItem）刚实现出来时也补一次
        // ⚠️ 这里能拿到容器：模板 Loaded 时容器已经建好（TabHiddenSlotView 就在容器子树里）。
        if (Tabs.ContainerFromItem(tab) is Control container)
        {
            container.CornerRadius = ThemeScale.Corners(DesignTabCornerRadius, _cornerScale);
        }

        // 成对登记：容器被回收/换数据源时要把它从表里摘掉，否则这张强引用表只增不减
        // （条目本身不会重复 Loaded，但**同一个插槽控件**可能被复用给另一个标签，
        //   所以这里按"控件自己卸载"来摘，而不是按索引猜）。
        slot.Unloaded += (s, _) =>
        {
            if (s is TabHiddenSlotView self
                && _slots.TryGetValue(tab, out var registered)
                && ReferenceEquals(registered, self))
            {
                _slots.Remove(tab);
            }
        };

        // 初始态：按当前形态（text = 收起；always = 展开）就位，不做动画。
        // ⚠️ 用 CurrentIconMode（静态）而不是 _iconMode（实例）：模板加载可能早于/晚于设置下发。
        slot.IsExpanded = CurrentIconMode == TabIconMode.Always;

        ApplyPendingHoverTab();
    }

    private void ApplyIconModeToAll()
    {
        CurrentIconMode = _iconMode;

        foreach (var (tab, slot) in _slots)
        {
            slot.DurationMs = HoverDurationMs;
            slot.IsExpanded = CurrentIconMode == TabIconMode.Always;
        }

        _expandedTab = CurrentIconMode == TabIconMode.Always
            ? Tabs.Items.OfType<TabItemViewModel>().FirstOrDefault()
            : null;
    }

    /// <summary>
    /// 保证"同时最多只有一个标签展开"：只展开 <paramref name="tab"/>，其余一律收起。
    /// 这是结构性保证，不依赖任何 pointer 事件的到达顺序。
    /// </summary>
    private void SetExpandedTab(TabItemViewModel? tab)
    {
        _expandedTab = tab;

        foreach (var (candidate, slot) in _slots)
        {
            slot.DurationMs = HoverDurationMs;
            slot.IsExpanded = ReferenceEquals(candidate, tab);
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
    /// 指针落在哪个标签上。
    ///
    /// <para>判据用**整个标签项的范围**（含内边距）—— 用户要求"鼠标在标签上就展开"，
    /// 而不是只有精确落在文字笔画上才展开。轨道宽度恒定，容器范围不会随展开变化，
    /// 所以不存在"判定范围越滚越大"的问题。</para>
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
        // ⚠️ 2026-09-16：内部拖动时 DataPackage 永远是空的（起拖事件收不到 ⇒ 写不进载荷，见 ERROR.md E25），
        //    所以"是不是本应用在拖"改判"载荷为空"；落点登记给源端，由源端的 DragItemsCompleted 收口。
        var (hasText, hasStorage) = DescribeDrag(e);

        if (!DragSession.LooksLikeInternalDrag(hasText, hasStorage) && DragSession.Target is null)
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
        if (tab is null)
        {
            return;
        }

        // 落在这条标签上 ⇒ 登记"松手就追加到这一页末尾"（松手在标签上 = 快手跨页移动）
        // ⚠️ 用**可见数量**（`VisibleIcons/VisibleListItems`）而不是 `Icons/ListItems`：
        //    落点的口径统一是"过滤视图里的第几位"，目标页若正在搜索，追加的含义就是
        //    "放到最后一个**可见项**后面"（MainViewModel 会再换算成 Core 下标）。
        var appendIndex = tab.IsList ? tab.VisibleListItems.Count : tab.VisibleIcons.Count;
        DragSession.ReportTarget(tab.Id, tab.IsList ? DragItemKind.ListItem : DragItemKind.Icon, appendIndex);

        if (ReferenceEquals(tab, _dragOverTab))
        {
            return;
        }

        // 换了一个标签 → 重新开始计时（拖过标签栏时不会乱切页）
        _dragOverTab = tab;
        _dragAutoSwitchTimer?.Stop();
        _dragAutoSwitchTimer?.Start();
    }

    /// <summary>这次拖放带了什么格式（内部拖动 = 什么都没有）。</summary>
    private static (bool HasText, bool HasStorageItems) DescribeDrag(DragEventArgs e)
    {
        try
        {
            return (e.DataView.Contains(StandardDataFormats.Text),
                    e.DataView.Contains(StandardDataFormats.StorageItems));
        }
        catch (Exception ex)
        {
            App.WriteCrash("TabStripView.DescribeDrag", ex);
            return (false, false);
        }
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

    /// <summary>
    /// 松手在标签上：**不在这里落库** —— 落点已在 <see cref="OnTabsDragOver"/> 里登记给
    /// <see cref="DragSession"/>，由源端的 `DragItemsCompleted`（那里才有"拖的是谁"）统一收口。
    /// </summary>
    private void OnTabsDrop(object sender, DragEventArgs e)
    {
        StopDragTimers();
    }

    private void StopDragTimers()
    {
        _dragAutoSwitchTimer?.Stop();
        _dragLeaveTimer?.Stop();
        _dragOverTab = null;
    }

    /// <summary>
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

    // ────────────────────────────── 标签页右键菜单（批次 1）──────────────────────────────
    //
    // 菜单规格（顺序 / 分隔线 / 文案 / 按位置的取舍）全在 Core 的 `TabContextMenu`（有单测），
    // 这里只负责按规格铺控件 + 把动作转成事件。
    //
    // 两种位置两种菜单（与规格一一对应）：
    //   · 标签上右键   → 五项：新建 / 新建列表页 / 重命名 / ─── / 删除
    //   · 标签栏空白处 → 只出两个「新建」（"管理"类动作没有作用对象）
    //
    // ⚠️ 用 `ContextRequested` 而不是 `RightTapped`：右键与键盘「菜单键 / Shift+F10」都能触发
    //    （与 GridPage / ListViewPage 同一做法，见 memory/03 §25）。

    private void OnTabsContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        // 键盘菜单键时拿不到坐标，退回左上角；右键时就是鼠标位置
        var position = args.TryGetPosition(Tabs, out var point)
            ? point
            : new Windows.Foundation.Point(0, 0);

        var tab = FindTabFromSource(args.OriginalSource);
        var spec = tab is null ? TabContextMenu.BuildForEmptyArea() : TabContextMenu.BuildForTab();

        var menu = new MenuFlyout();

        foreach (var item in spec)
        {
            if (item.SeparatorBefore)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            var action = item.Action;
            var menuItem = new MenuFlyoutItem { Text = item.Label };
            menuItem.Click += (_, _) => TabMenuActionRequested?.Invoke(this, new TabMenuRequest(tab, action));
            menu.Items.Add(menuItem);
        }

        menu.ShowAt(Tabs, position);
        args.Handled = true;
    }

    /// <summary>右键点在哪 —— 往上找到承载标签的 `ListViewItem`；点在空白处返回 null。</summary>
    private static TabItemViewModel? FindTabFromSource(object? source)
    {
        var current = source as DependencyObject;

        while (current is not null)
        {
            if (current is ListViewItem { DataContext: TabItemViewModel tab })
            {
                return tab;
            }

            try
            {
                current = VisualTreeHelper.GetParent(current);
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }
}

/// <summary>
/// 一次标签页菜单动作请求（页面 → 宿主窗口）。
/// <see cref="Tab"/> 为 null = 点在标签栏空白处（此时只可能是两个「新建」动作）。
/// </summary>
public sealed record TabMenuRequest(TabItemViewModel? Tab, TabMenuAction Action);
