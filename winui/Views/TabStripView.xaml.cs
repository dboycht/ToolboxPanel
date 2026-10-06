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

    /// <summary>已经挂过 <c>Unloaded</c> 的插槽控件（避免同一个控件被复用/重复 Loaded 时累加处理器）。</summary>
    private readonly HashSet<TabHiddenSlotView> _unloadHooked = new();

    /// <summary>
    /// 已经挂过"就地改名"三个事件的插槽控件。
    /// ⚠️ 与 <see cref="_unloadHooked"/> 不同：**这个表只增不减** ——
    /// 插槽控件是"复用"而不是"销毁"，取消订阅会让复用到别处的控件失去响应；
    /// 处理的是具名方法（不捕获循环变量），事件里一律从 `DataContext` 现取当前标签。
    /// </summary>
    private readonly HashSet<TabHiddenSlotView> _renameHooked = new();

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

    /// <summary>
    /// 本次拖动是否**真的落在标签栏上**（`Drop` 事件到场）。
    /// 用途与页面的 `_dropSeen` 完全一致：载荷为空时 OS 可能把 `DropResult` 报成 `None`，
    /// 只看它会把"真落下"当成"取消"。见 ERROR.md E25。
    /// </summary>
    private bool _tabDropSeen;

    /// <summary>演示模式下关掉"拖起标签"（假数据不会落库，别让用户看着能拖、其实存不下来）。</summary>
    private bool _dragDropEnabled = true;

    /// <summary>本次拖动是否已经打过"标签栏收到 DragOver（首次）"那条诊断（每次拖动复位一次）。</summary>
    private bool _tracedStripDragEntry;

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

        // ⚠️ 2026-10-06（ERROR.md E58）：拖放的两个事件在**外层容器**上再挂一遍，且 `handledEventsToo: true` ——
        //    万一某个子元素（ListViewItem 的拖放视觉逻辑之类）把事件标记成 handled，
        //    XAML 上挂的那个处理器就**再也收不到**了（"整条拖放链路静默不通"）。
        //    这条链路的命脉，不能取决于"谁先处理了它"。
        StripHost.AddHandler(UIElement.DragEnterEvent, new DragEventHandler(OnTabsDragOver), handledEventsToo: true);
        StripHost.AddHandler(UIElement.DragOverEvent, new DragEventHandler(OnTabsDragOver), handledEventsToo: true);
        StripHost.AddHandler(UIElement.DropEvent, new DragEventHandler(OnTabsDrop), handledEventsToo: true);
        StripHost.AddHandler(UIElement.DragLeaveEvent, new DragEventHandler(OnTabsDragLeave), handledEventsToo: true);
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

    /// <summary>
    /// 就地改名（双击标签文字）提交 —— 交给宿主窗口落库（页面不碰 DataStore）。
    /// <para><see cref="TabRenameRequest.NewName"/> 已经过 Core 的 <c>InlineRename.Decide</c>：
    /// 空输入与"没改"在标签栏这一层就被拦掉了，不会发出来。</para>
    /// </summary>
    public event EventHandler<TabRenameRequest>? TabRenameCommitted;

    /// <summary>
    /// 标签拖动落到了标签栏上（2026-10-06）—— 交给宿主窗口落库（页面不碰 DataStore）。
    /// <para><see cref="TabReorderRequest.InsertIndex"/> 是"插到第几个标签之前"（0..标签数），
    /// 由 <see cref="ToolboxPanel.Core.Services.TabReorder.Resolve"/> 换算成 Core 的下标。</para>
    /// </summary>
    public event EventHandler<TabReorderRequest>? TabReorderRequested;

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

    public int SelectedIndex
    {
        get => Tabs.SelectedIndex;
        set => Tabs.SelectedIndex = value;
    }

    public int ItemCount => Tabs.Items.Count;

    /// <summary>
    /// 演示模式下关掉"从标签栏拖起一个标签"（与三个页面的同名属性同一用意）。
    /// ⚠️ 只关"起拖"（`CanDragItems`）：落点那一侧本来就不会落库（`MainViewModel` 的 `_store` 为空），
    /// 关掉起拖是为了不让用户看着能拖、其实存不下来。
    /// </summary>
    public bool DragDropEnabled
    {
        set
        {
            _dragDropEnabled = value;
            Tabs.CanDrag = value;
            Tabs.CanDragItems = value;
        }
    }

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

        // 🆕 标签重排的插入竖条同一个键，同样**直接改已实现出来的那个元素**
        //    （`{ThemeResource}` 不会因为资源键变了就重新解析，见上面那段注释）
        TabDropIndicator.Background = accent;

        // 🆕 「图标拖到标签上」的目标描边也是同一个键（同一个理由）
        TabDropTarget.BorderBrush = accent;

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

        // 成对登记：容器被回收/换数据源时要把它从表里摘掉，否则这张强引用表只增不减。
        //
        // ⚠️ 2026-09-21 修：同一个插槽控件会被**复用给另一个标签**（于是 `Loaded` 会再触发一次），
        //    原来每次都 `slot.Unloaded += lambda` 且从不摘除 ⇒ 处理器逐个累积：一次 Unloaded 跑 N 遍，
        //    旧 lambda 还把旧的 tab 键钉住不放。现在**每个插槽只挂一次具名处理器**。
        if (_unloadHooked.Add(slot))
        {
            slot.Unloaded += OnHiddenSlotViewUnloaded;
        }

        // 就地改名（双击标签文字）：**每个插槽只挂一次**，事件里按 DataContext 现取标签
        if (_renameHooked.Add(slot))
        {
            slot.RenameRequested += OnSlotRenameRequested;
            slot.RenameCommitted += OnSlotRenameCommitted;
            slot.RenameCanceled += OnSlotRenameCanceled;
        }

        // 初始态：按当前形态（text = 收起；always = 展开）就位，不做动画。
        // ⚠️ 用 CurrentIconMode（静态）而不是 _iconMode（实例）：模板加载可能早于/晚于设置下发。
        // ⚠️ 2026-09-21 修：还要认"**这一项正是当前悬停项**" —— 否则悬停期间容器被回收重建时，
        //    那个标签会突然收起，要等指针再动一下才展开。
        slot.IsExpanded = CurrentIconMode == TabIconMode.Always || ReferenceEquals(tab, _expandedTab);

        ApplyPendingHoverTab();
    }

    /// <summary>插槽控件卸载：把它从 <see cref="_slots"/> / <see cref="_unloadHooked"/> 里摘掉。</summary>
    private void OnHiddenSlotViewUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TabHiddenSlotView slot)
        {
            return;
        }

        _unloadHooked.Remove(slot);

        // 按"控件自己"摘：谁登记的是它，就摘谁（不看闭包里的 tab —— 那个控件可能已经换了标签）
        foreach (var key in _slots.Where(pair => ReferenceEquals(pair.Value, slot)).Select(pair => pair.Key).ToList())
        {
            _slots.Remove(key);
        }
    }

    // ────────────────────────────── 就地改名（双击标签文字，2026-10-02）──────────────────────────────

    private void OnSlotRenameRequested(object? sender, EventArgs e)
    {
        if (sender is not TabHiddenSlotView slot || slot.DataContext is not TabItemViewModel tab)
        {
            return;
        }

        BeginInlineRename(tab);
    }

    /// <summary>进编辑：**同时只允许一个标签在编辑**（另一个正在编辑就先收掉，它的失焦会先提交）。</summary>
    private void BeginInlineRename(TabItemViewModel tab)
    {
        foreach (var other in Tabs.Items.OfType<TabItemViewModel>())
        {
            if (!ReferenceEquals(other, tab) && other.IsEditing)
            {
                other.EndEdit();
            }
        }

        tab.BeginEdit();
    }

    private void OnSlotRenameCommitted(object? sender, string text)
    {
        if (sender is not TabHiddenSlotView slot || slot.DataContext is not TabItemViewModel tab || !tab.IsEditing)
        {
            return;
        }

        // 先收编辑态（名字回显由模板的 OneWay 绑定负责），再按 Core 的决定要不要落库
        tab.EndEdit();

        var decision = InlineRename.Decide(tab.Model.Name, text);
        if (decision.Outcome != InlineRenameOutcome.Commit)
        {
            return;   // 空名字 ⇒ 回退原值；没改 ⇒ 不落库、不提示
        }

        TabRenameCommitted?.Invoke(this, new TabRenameRequest(tab, decision.Name));
    }

    private void OnSlotRenameCanceled(object? sender, EventArgs e)
    {
        if (sender is not TabHiddenSlotView slot || slot.DataContext is not TabItemViewModel tab)
        {
            return;
        }

        tab.EndEdit();
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

    // ────────────────────────────── 拖拽：悬停切页 + 丢到标签上 + 标签重排 ──────────────────────────────
    //
    // 跨页移动的两条路：
    //   ① 拖到某个标签上停住 → 自动切到那一页 → 用户继续在新页面里选位置放下（位置可精确控制）；
    //   ② 直接松手在标签上 → 追加到那一页末尾（快手操作）。
    // ⚠️ 悬停判定用**定时器**而不是 PointerEntered：拖拽期间指针事件与普通指针事件是两套，
    //    这里以 DragOver 为准（每次移动都会重置定时器，停住才开始计时）。
    //
    // 🆕 2026-10-06：标签栏**自己也能被拖动**（拖起一个标签做重排）。于是同一次 DragOver 要写两份落点：
    //   · 「落在哪个标签上 + 追加到第几位」→ 条目拖动用（页面那条 DragItemsCompleted 收口）；
    //   · 「插到第几个标签之前」        → 标签重排用（标签栏自己的 DragItemsCompleted 收口）。
    //   谁用哪一份由**源端**决定（谁起的拖，谁的 DragItemsCompleted 才会到场），这里一次写齐、互不干扰。

    private void OnTabsDragOver(object sender, DragEventArgs e)
    {
        // ⚠️ 2026-09-16：内部拖动时 DataPackage 永远是空的（起拖事件收不到 ⇒ 写不进载荷，见 ERROR.md E25），
        //    所以"是不是本应用在拖"改判"载荷为空"；落点登记给源端，由源端的 DragItemsCompleted 收口。
        var (hasText, hasStorage) = DescribeDrag(e);

        // ⚠️ 2026-09-21 收紧：只有"看起来是本应用在拖"、或者"**不是文件拖入**且已经登记过落点"才接受。
        //    原来写成 `!internal && Target is null` ⇒ 只要 `DragSession.Target` 有残留
        //    （源端 `DragItemsCompleted` 没送达，例如拖动中源页被销毁），
        //    从资源管理器拖文件进来也会被当成内部拖动接受成 `Move` 并登记假落点。
        if (!DragSession.LooksLikeInternalDrag(hasText, hasStorage)
            && (hasStorage || DragSession.Target is null))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            StopDragTimers();
            ClearDragFeedback();   // 从资源管理器拖进来一条也得把上一次的内部拖动提示收干净
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;

        // 指针还在标签栏区域内 → 取消"离开"的兜底计时
        _dragLeaveTimer?.Stop();
        _dragLeaveTimer?.Start();

        var tab = FindTabAtPointer(e);

        // 标签重排的落点（0..标签数）：指针越过第 i 个标签的中心 ⇒ 插到它后面；
        // 在标签栏空白处 ⇒ 标签数（放到最后）。算法在 Core（`TabReorder.ComputeInsertIndex`，有单测）。
        var tabInsertIndex = ComputeTabInsertIndex(e);
        var tabDrag = DragSession.LooksLikeTabDrag;
        int appendIndex = 0;

        // ⚠️ 诊断（E25 的教训：先证明"事件到底有没有进来"）：**每次拖动的第一拍**无条件留一条，
        //    含指针坐标与命中的标签。用户报"跨页拖动没反应"时，看这一行有没有出现就能立刻分清
        //    "标签栏根本没收到拖放事件"（E58 那种）还是"收到了但判据不认"。
        if (!_tracedStripDragEntry)
        {
            _tracedStripDragEntry = true;
            var point = e.GetPosition(TabDropLayer);
            DragTrace($"标签栏收到 DragOver（首次）：指针=({point.X:0.#},{point.Y:0.#}) "
                      + $"命中标签={(tab is null ? "无" : "「" + tab.Name + "」")} "
                      + $"按标签拖动={tabDrag} 事件源={e.OriginalSource?.GetType().Name ?? "null"}");
        }

        if (tab is null)
        {
            // 标签栏空白处：条目那半**原样保留**改动前的行为（此前这里直接 return，落点登记不动），
            // 只补上"标签重排落在最后一位"这条 —— 拖标签时把指针放到最右边松手就走这一支。
            DragSession.ReportTabStripHover(tabId: null, DragItemKind.Icon, appendIndex: 0, tabInsertIndex);
        }
        else
        {
            // 落在这条标签上 ⇒ 登记"松手就追加到这一页末尾"（松手在标签上 = 快手跨页移动）
            // ⚠️ 用**可见数量**（`VisibleIcons / VisibleListItems / VisibleTextItems`）而不是完整集合：
            //    落点的口径统一是"过滤视图里的第几位"，目标页若正在搜索，追加的含义就是
            //    "放到最后一个**可见项**后面"（MainViewModel 会再换算成 Core 下标）。
            // ⚠️ 三类页各有自己的可见集合（2026-10-03 加上文本页）：漏掉文本页那一支，
            //    "把文本项丢到文本标签上"会拿 `VisibleIcons.Count`（文本页上恒为 0）当落点 ⇒ 永远插到最前。
            var kind = tab.DraggableKind;
            appendIndex = kind switch
            {
                DragItemKind.TextItem => tab.VisibleTextItems.Count,
                DragItemKind.ListItem => tab.VisibleListItems.Count,
                _ => tab.VisibleIcons.Count,
            };

            DragSession.ReportTabStripHover(tab.Id, kind, appendIndex, tabInsertIndex);
        }

        // 反馈两类拖动各一套（一次拖动只会是其中一种，所以两个提示互斥）：
        //   · 拖**标签** ⇒ 插入竖条（"插到第几个标签之前"）；
        //   · 拖**页面里的条目** ⇒ 给目标标签套一圈描边（"松手就落进这一页"）。
        // ⚠️ 别在条目拖动时画插入竖条：那条子表达的是"标签会插到这里"，会误导用户。
        if (tabDrag)
        {
            HideTabDropTarget();
            ShowTabDropIndicator(tabInsertIndex);
        }
        else
        {
            HideTabDropIndicator();

            if (tab is null)
            {
                HideTabDropTarget();
            }
            else
            {
                ShowTabDropTarget(tab);
            }
        }

        // 悬停切页只对**条目拖动**有意义；拖的是标签时绝不能切页（用户正在重排标签栏，界面却跟着翻页）。
        if (tab is null || tabDrag)
        {
            return;
        }

        if (ReferenceEquals(tab, _dragOverTab))
        {
            return;
        }

        // 换了一个标签 → 重新开始计时（拖过标签栏时不会乱切页）。
        // 顺带留一条可诊断的链路日志（只在这一刻打，不会每移动 1px 刷屏）：
        // 用户报"跨页拖动没反应"时，先看它有没有出现 ⇒ 一眼分清"没命中标签"还是"落库被否"。
        _dragOverTab = tab;
        DragTrace($"条目拖动经过标签「{tab.Name}」⇒ 登记：松手追加到该页第 {appendIndex} 位"
                  + $"（停 {DragAutoSwitchDelay.TotalMilliseconds:0}ms 则自动切过去）");
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

        // 指针已经离开标签栏（去抖确认）⇒ 两个拖动提示都收干净
        ClearDragFeedback();
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
    /// 松手在标签栏上：**不在这里落库**（Drop 不带"拖的是谁"）—— 落点已在
    /// <see cref="OnTabsDragOver"/> / 这里登记给 <see cref="DragSession"/>，
    /// 由**源端**的 `DragItemsCompleted`（页面或标签栏自己的那一个）统一收口。
    /// </summary>
    private void OnTabsDrop(object sender, DragEventArgs e)
    {
        var (hasText, hasStorage) = DescribeDrag(e);
        if (DragSession.LooksLikeInternalDrag(hasText, hasStorage))
        {
            _tabDropSeen = true;

            // 🆕 会话级"真的落下过"：跨区域的拖动，`Drop` 落在**目标**侧（这里），
            //    而收口在**源**侧（页面）—— 源侧自己的 `_dropSeen` 永远是假，
            //    只靠 `DropResult == Move` 一根独苗（E25 记录过它可能报 None）⇒ 这里补一条证据。
            DragSession.ReportDropSeen();

            // 松手那一刻的位置最准（DragOver 与 Drop 之间指针可能还会动一点）
            var insertIndex = ComputeTabInsertIndex(e);
            DragSession.ReportTabStripHover(tabId: null, DragItemKind.Icon, appendIndex: 0, insertIndex);
        }

        StopDragTimers();
        ClearDragFeedback();
        _tracedStripDragEntry = false;
    }

    private void StopDragTimers()
    {
        _dragAutoSwitchTimer?.Stop();
        _dragLeaveTimer?.Stop();
        _dragOverTab = null;
    }

    // ────────────────────────────── 标签拖动重排（2026-10-06）──────────────────────────────
    //
    // 与页面里的条目拖动**同一套链路**（ERROR.md E25）：
    //   · 起拖交给系统的原生拖拽（容器 `CanDrag` + `CanDragItems`，见 TabStripView.xaml）；
    //   · 落点在 DragOver 里登记（上面那些 `DragSession.ReportTabStripHover`）；
    //   · "拖的是谁"只有**源端**的 `DragItemsCompleted` 知道（`args.Items`）—— 这里就是标签栏自己。
    //
    // ⚠️ 本方法**只在"拖动是从标签栏起手"时才会被调用**（页面起手的拖动走页面自己的
    //    `DragItemsCompleted`），所以这里不需要再判断"拖的是标签还是条目"。
    //    反过来，标签拖到页面上松手时，页面那条 `DragItemsCompleted` 也不会到场 ⇒ 不会误当成条目移动。
    //
    // ⚠️⚠️ 判据里最要紧的一条：**最后登记的落点必须在标签栏上**（`DragSession.LastRegion`）。
    //    标签被拖出标签栏、松手在页面内容区里时，页面会把区域登记成 `Page`（页面还会把这次拖动
    //    accept 成 Move）⇒ 只看 `DropResult` 会把这种"拖到页面上"误当成在标签栏上松手。

    /// <summary>
    /// 标签的 <c>DragItemsStarting</c>：把"本次拖的是哪个标签"记进会话。
    ///
    /// <para>⚠️ 这里**绝不能**往 <c>args.Data</c> 里写载荷：全项目"内部拖动"的判据就是
    /// "DataPackage 是空的"（<see cref="DragSession.LooksLikeInternalDrag"/>）——
    /// 写进去会让所有页面与标签栏一起拒收这次拖动。
    /// 这条事件在本项目历史上实测**收不到**（E24/E25），所以它只是首选判据之一：
    /// 拿不到时由 <see cref="DragSession.LooksLikeTabDrag"/> 的第二条兜底，
    /// 这里也无条件写一行探针日志（下一次手验就能看出它现在到底通不通）。</para>
    /// </summary>
    private void OnTabsDragItemsStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (args.Items.Count > 0 && args.Items[0] is TabItemViewModel tab)
        {
            DragSession.BeginTabDrag(tab.Id);
            DragTrace($"DragItemsStarting：被拖标签=「{tab.Name}」⇒ 本次按标签拖动");
            return;
        }

        DragTrace($"DragItemsStarting：没拿到标签项（items={args.Items.Count}）");
    }

    /// <summary>★ 一次标签拖动的收口点（源端事件，实测可靠）。</summary>
    private void OnTabsDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var tab = args.Items.Count > 0 ? args.Items[0] as TabItemViewModel : null;
        var dropResult = args.DropResult;
        var dropSeen = _tabDropSeen;

        // ⚠️ 先复位再判定：这一拍之后就属于"下一次拖动"了（同 ERROR.md E32 的教训 —— 别在延后回调里
        //    二次读字段）。落点与区域在 EndSession 之前已经进了 FinishTabDrag 的局部量。
        _tabDropSeen = false;
        ClearDragFeedback();
        StopDragTimers();

        FinishTabDrag(tab, dropResult, dropSeen);
        DragSession.EndSession();
    }

    /// <summary>
    /// 判据齐了就把"重排请求"发给宿主窗口（落库在那一层，页面不碰 DataStore）。
    /// <para>刻意拆成独立方法：2026-10-06 那一轮用**一次性探针直接调它**，把"事件投递"与
    /// "业务链"分开证伪（E25 的方法论：单测测不到"没人调用它"）。探针用完已删。</para>
    /// </summary>
    private void FinishTabDrag(TabItemViewModel? tab, DataPackageOperation dropResult, bool dropSeen)
    {
        var insertIndex = DragSession.TabInsertIndex;
        var region = DragSession.LastRegion;

        DragTrace($"标签 DragItemsCompleted：DropResult={dropResult} 被拖=「{tab?.Name ?? "null"}」 "
                  + $"落点区域={region} 插入位={(insertIndex?.ToString() ?? "无")} Drop到过标签栏={dropSeen}");

        if (tab is null || insertIndex is not { } insert)
        {
            DragTrace("→ 没拿到被拖的标签或没有落点 ⇒ 不重排");
            return;
        }

        if (region != DragRegion.TabStrip)
        {
            DragTrace($"→ 最后登记的落点在「{region}」而不是标签栏（多半是拖出标签栏后松手在页面上）⇒ 不重排");
            return;
        }

        if (dropResult != DataPackageOperation.Move && !dropSeen)
        {
            DragTrace("→ 判定：拖动被取消（DropResult 不是 Move，且 Drop 没到过标签栏）⇒ 不重排");
            return;
        }

        DragTrace($"→ 请求重排：插入位={insert}（被拖标签的当前下标由宿主窗口按 id 现取）");

        // 与页面同款：结算延后一拍（Drop 与 DragItemsCompleted 的先后顺序不保证）
        DispatcherQueue.TryEnqueue(
            () => TabReorderRequested?.Invoke(this, new TabReorderRequest(tab, insert)));
    }

    // ────────────────────────────── 标签重排的落点与插入竖条 ──────────────────────────────

    /// <summary>
    /// 已实现标签的矩形（相对插入条所在的画布）**与它对应的标签** —— 一起返回，避免"矩形下标"与
    /// "标签下标"在虚拟化（有标签没被实现）时对不上。
    /// </summary>
    private List<(TabItemViewModel Tab, ItemBounds Bounds)> CollectTabRects()
    {
        var result = new List<(TabItemViewModel, ItemBounds)>();

        foreach (var tab in Tabs.Items.OfType<TabItemViewModel>())
        {
            if (Tabs.ContainerFromItem(tab) is not FrameworkElement container || container.ActualWidth <= 0)
            {
                continue;   // 虚拟化后屏幕外的标签没有容器：跳过（别塞 0 尺寸假矩形）
            }

            try
            {
                var origin = container.TransformToVisual(TabDropLayer)
                    .TransformPoint(new Windows.Foundation.Point(0, 0));
                result.Add((tab, new ItemBounds(origin.X, origin.Y, container.ActualWidth, container.ActualHeight)));
            }
            catch (Exception ex)
            {
                App.WriteCrash("TabStripView.CollectTabRects", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// 这次 DragOver 的指针**压在哪一个标签上**（标签之间 / 标签栏空白处 ⇒ null）。
    ///
    /// <para>⚠️ 2026-10-06（`ERROR.md` **E58**）：判据**按指针坐标**来，**不看 `e.OriginalSource`**。
    /// 实测：从页面里拖图标经过标签栏时标签栏一个拖放事件都收不到、而拖标签时却正常 ⇒
    /// "拖放命中会把事件交给谁"是框架内部的事，不能拿它当业务判据。
    /// （兜底：坐标判不出来时再走一次"往上找 `DataContext`"的老路。）</para>
    /// </summary>
    private TabItemViewModel? FindTabAtPointer(DragEventArgs e)
    {
        var rects = CollectTabRects();
        var position = e.GetPosition(TabDropLayer);
        int index = TabReorder.HitTestIndex(
            rects.Select(pair => pair.Bounds).ToList(), position.X, position.Y);

        if (index >= 0 && index < rects.Count)
        {
            return rects[index].Tab;
        }

        return FindTabFromElement(e.OriginalSource);
    }

    /// <summary>老判据（往上找带 `DataContext` 的元素）—— 只当兜底，不再当主判据。</summary>
    private TabItemViewModel? FindTabFromElement(object? source)
    {
        var element = source as FrameworkElement;
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

    /// <summary>标签重排的落点（0..标签数）：指针越过那个位置之前；空白处 ⇒ 放到最后。</summary>
    private int ComputeTabInsertIndex(DragEventArgs e)
    {
        var rects = CollectTabRects();
        if (rects.Count == 0)
        {
            return 0;
        }

        var position = e.GetPosition(TabDropLayer);
        int insert = TabReorder.ComputeInsertIndex(
            rects.Select(pair => pair.Bounds).ToList(), position.X);

        // ⚠️ 把"第几个**已实现**标签之前"换算成"第几个**标签**之前"：
        //    标签栏横向滚动时，屏幕外的标签没有容器，两个下标不是一回事。
        if (insert < rects.Count)
        {
            int real = Tabs.Items.IndexOf(rects[insert].Tab);
            return real >= 0 ? real : insert;
        }

        int lastReal = Tabs.Items.IndexOf(rects[^1].Tab);
        return lastReal >= 0 ? lastReal + 1 : rects.Count;
    }

    private void ShowTabDropIndicator(int insertIndex)
        => DragDropShared.ShowVerticalIndicator(
            TabDropIndicator,
            CollectTabRects().Select(pair => pair.Bounds).ToList(),
            insertIndex);

    /// <summary>
    /// 把"松手就落进这一页"的目标描边套在 <paramref name="tab"/> 上（拖**页面里的条目**经过标签时）。
    /// 几何与插入竖条共用同一套"容器矩形 → 画布坐标"的换算（`CollectTabBounds`，本轮实测过）。
    /// </summary>
    private void ShowTabDropTarget(TabItemViewModel tab)
    {
        var tabs = Tabs.Items.OfType<TabItemViewModel>().ToList();
        int index = tabs.IndexOf(tab);

        if (index < 0 || Tabs.ContainerFromIndex(index) is not FrameworkElement container)
        {
            HideTabDropTarget();
            return;
        }

        try
        {
            var origin = container.TransformToVisual(TabDropLayer)
                .TransformPoint(new Windows.Foundation.Point(0, 0));

            Canvas.SetLeft(TabDropTarget, origin.X);
            Canvas.SetTop(TabDropTarget, origin.Y);
            TabDropTarget.Width = Math.Max(8, container.ActualWidth);
            TabDropTarget.Height = Math.Max(8, container.ActualHeight);
            TabDropTarget.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            // 元素还没进可视树时会抛异常：当作"画不出来"，不影响拖动本身
            App.WriteCrash("TabStripView.ShowTabDropTarget", ex);
            HideTabDropTarget();
        }
    }

    private void HideTabDropIndicator() => DragDropShared.HideIndicator(TabDropIndicator);

    private void HideTabDropTarget() => TabDropTarget.Visibility = Visibility.Collapsed;

    /// <summary>把两类拖动提示（标签插入竖条 / 目标标签描边）一起收干净。</summary>
    private void ClearDragFeedback()
    {
        HideTabDropIndicator();
        HideTabDropTarget();
    }

    /// <summary>拖动链路诊断（写 %TEMP%\toolboxpanel-probe.log）—— 与页面同一套。</summary>
    private static void DragTrace(string message) => DragDropShared.Trace(message);

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

    /// <summary>
    /// 右键点在哪 —— 往上找到承载标签的那个元素；点在空白处返回 null。
    ///
    /// <para>⚠️ 2026-10-03：判据收进 <see cref="ContextHitTest"/>（原来那版写成
    /// <c>ListViewItem { DataContext: TabItemViewModel }</c>，**永远不成立** —— 容器 DataContext 是 null、
    /// 数据项在模板元素上，见 `ERROR.md` E47 与 E52。这个 bug 的后果是：**在标签上右键也只会出
    /// "空白处菜单"（两个新建），「重命名 / 删除」永远看不到**）。</para>
    ///
    /// <para>⚠️ 传的是**界面上正在用的那一份集合**（<c>ItemsSource</c>）：
    /// <c>IndexFromContainer</c> 给的是视图下标，与 Core 的列表下标不是一回事。</para>
    /// </summary>
    private TabItemViewModel? FindTabFromSource(object? source)
        => ContextHitTest.Resolve(
            source,
            Tabs,
            ItemsSource as IReadOnlyList<TabItemViewModel> ?? Array.Empty<TabItemViewModel>());
}

/// <summary>
/// 一次标签页菜单动作请求（页面 → 宿主窗口）。
/// <see cref="Tab"/> 为 null = 点在标签栏空白处（此时只可能是两个「新建」动作）。
/// </summary>
public sealed record TabMenuRequest(TabItemViewModel? Tab, TabMenuAction Action);

/// <summary>
/// 一次"标签名就地改名"提交（标签栏 → 宿主窗口）：双击标签文字 → 输入框 → Enter / 点别处。
/// <para><see cref="NewName"/> 已 Trim、也**一定与原值不同**（空输入与没改在标签栏那一层就被 Core 的决定拦掉了）。</para>
/// </summary>
public sealed record TabRenameRequest(TabItemViewModel Tab, string NewName);

/// <summary>
/// 一次"标签拖动重排"请求（标签栏 → 宿主窗口）。
/// <para><see cref="InsertIndex"/> 是"插到第几个标签之前"（0..标签数，界面的坐标系），
/// 由宿主窗口交给 Core 的 <see cref="ToolboxPanel.Core.Services.TabReorder.Resolve"/>
/// 换算成 <c>DataStore.ReorderTabs</c> 的下标（并挡掉"拖回原位"）。</para>
/// </summary>
public sealed record TabReorderRequest(TabItemViewModel Tab, int InsertIndex);
