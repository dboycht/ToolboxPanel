// GridPage.xaml.cs —— 网格页
//
// 职责：铺出该标签页的图标集合、点击打开、入场动效、**拖拽排序（W3）**、
// **右键菜单入口（W5：空白处新建 / 图块编辑属性）**。
// 菜单里的其余项（打开 / 用其他应用打开 / 打开文件位置 / 重命名 / 删除）与批量管理属于后续。
//
// ────────────────────────── 为什么不用 GridView 内置的 CanReorderItems ──────────────────────────
// 内置排序只能覆盖"同页重排"，而且**它直接改 ItemsSource 的集合**，不会经过 Core，
// 结果就是"界面上顺序变了、tabs.json 没变"（重启后又跳回去）。
// 跨页移动更是完全不在它能力范围内。所以这里统一手写 DragDrop：
//   · 拖起：容器 CanDrag=True → 这里在 DragStarting 里塞一个 DragPayload（id + 类型 + 来源页）；
//   · 拖动中：DragOver 算出"会插到第几个"，在**相邻图块之间**画一根强调色插入竖条；
//   · 放下：Drop 把结果交给宿主窗口 → MainViewModel.ApplyDrop → DataStore 落库 → 界面跟着 Core 重排。
// 好处是"同页排序 / 跨页移动 / 拒绝异类拖放"三条路共用同一套代码和同一个落库入口。
//
// ⚠️ **拖动过程中不移动任何图块**（用户 2026-09-15 明确要求："只加竖条，不让位"）：
//    此前做过"实时让位"（把被拖项在集合里 Move，其它图块被推着让开），用户复测后**不要**这个效果。
//    现在只显示插入竖条，顺序**只在松手时**才真正改变 ——
//    好处是拖动期间"界面 = Core"，不需要那套"拖动取消要还原现场"的快照/回滚逻辑。
//
// ⚠️ 落点判定（DropIndexCalculator）在 Core 里，有单测；这里只负责把矩形喂给它。
// ⚠️ 拖拽交互本身**只能由用户手动验证**（代理不注入鼠标输入，见 ERROR.md E5）。

using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ToolboxPanel.Core;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;
using ToolboxPanel.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

namespace ToolboxPanel.Views;

public sealed partial class GridPage : UserControl, IAnimatedPage, IIconSizedPage, ISearchablePage
{
    private readonly TabItemViewModel _tab;
    private readonly EntranceAnimator _entrance;

    /// <summary>当前图标大小档（默认 medium；由主窗口在创建页面与设置变化时下发）。</summary>
    private IconSizeMetrics _iconSize = IconSizeMetrics.Medium;

    public GridPage(TabItemViewModel tab)
    {
        _tab = tab;

        InitializeComponent();

        // ⚠️ 绑的是**可见集合**（过滤后的子集，仍是 Core 顺序），不是 _tab.Icons ——
        //    理由见 TabItemViewModel 的"搜索过滤"一节：模板里隐藏会留空档，改完整集合会让顺序脱钩。
        TileGrid.ItemsSource = tab.VisibleIcons;
        NoMatchText.Text = SearchFilter.NoResultIconText;

        // 可见集合一变就要重算"空页 / 无匹配"两种提示（新建、删除、过滤都会走到这里）；
        // 顺带补一次容器尺寸与拖拽状态（过滤后容器是重建的）。
        tab.VisibleIcons.CollectionChanged += (_, _) =>
        {
            UpdateEmptyHints();
            RefreshRealizedContainers();
        };

        _entrance = new EntranceAnimator(TileGrid);

        // 图块容器尺寸由代码按档位设置（样式里那对 68×72 只是 medium 的默认值）：
        // 新实现/回收再利用的容器都要在这里补一次，否则换档后滚动出来的图块会是旧尺寸。
        TileGrid.ContainerContentChanging += (_, args) =>
        {
            if (!args.InRecycleQueue && args.ItemContainer is Control container)
            {
                ApplyContainerSize(container);
                ApplyContainerDrag(container);   // 批量模式下新实现的容器也不能拖
                container.CornerRadius = ThemeScale.Corners(DesignCornerRadius, _cornerScale);
            }
        };

        // ⚠️ 这里**不再挂 PointerPressed**：实测它也收不到（日志里"按下"一行都没有）。
        //    拖动链路只依赖两个实测可靠的事件：目标端 `DragOver` + 源端 `DragItemsCompleted`，
        //    落点由 DragSession 在拖动中登记。详见 ERROR.md E25。

        UpdateEmptyHints();
        UpdateBulkBar();   // 批量条的计数文字（"已选 N 个"）也走 Core 文案，构造时按当前语言摆好
    }

    // ────────────────────────────── 搜索过滤（W5）──────────────────────────────
    //
    // 判定在 Core 的 `SearchFilter`（有单测）；页面只做三件事：
    //   ① 把查询交给标签页（它重算可见集合，界面绑的就是那个集合）；
    //   ② 摆对"空页 / 无匹配"两种提示；
    //   ③ 过滤会重建容器 ⇒ 补一次尺寸与拖拽状态、收起落点指示条。
    // ⚠️ 拖动排序在过滤态下**照样能用**：落点是"可见位"，由 MainViewModel 用 Core 的
    //    `SearchFilter.MapViewIndexToModelIndex` 换回 Core 下标再落库（无过滤时是恒等映射）。

    public void ApplySearch(string? query)
    {
        _tab.SetFilter(query);
        RefreshRealizedContainers();
        HideDropIndicator();
        UpdateEmptyHints();
        UpdateBulkBar();   // 过滤会清掉勾选，批量条上的计数要跟着走
    }

    /// <summary>
    /// 套用当前语言（v2.0.3 i18n）：XAML 上标了 `ui:Tr.Key` 的静态文字由 `Tr.RefreshAll()` 负责，
    /// 这里只重写"由代码设置的"两处 —— 无匹配提示（文案来自 Core）与批量条的计数。
    /// </summary>
    public void ApplyLanguage()
    {
        NoMatchText.Text = SearchFilter.NoResultIconText;
        UpdateBulkBar();
    }

    /// <summary>把"已实现"的容器补成当前档位尺寸 + 当前拖拽开关（过滤/换档后调）。</summary>
    private void RefreshRealizedContainers()
    {
        for (int i = 0; i < _tab.VisibleIcons.Count; i++)
        {
            if (TileGrid.ContainerFromIndex(i) is FrameworkElement container)
            {
                ApplyContainerSize(container);
                ApplyContainerDrag(container);
            }
        }
    }

    /// <summary>
    /// 空页提示 vs 搜索无匹配提示：前者=这一页真的没有图标，后者=有图标但当前查询一个都没命中。
    /// ⚠️ 必须**每次集合变化都重算**：此前只在构造时算一次，于是"给空页新建一个图标后提示还在"、
    ///    "删光图标后提示不出现"（本轮顺手修掉的真 bug）。
    /// </summary>
    private void UpdateEmptyHints()
    {
        var empty = _tab.Icons.Count == 0;
        var noMatch = !empty && _tab.VisibleIcons.Count == 0;

        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        NoMatchHint.Visibility = noMatch ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 套用"图标大小"档（设置：小 / 中 / 大）：
    /// ① 图块视图模型换档（模板 x:Bind OneWay ⇒ 图标、字形、名称字号就地刷新）；
    /// ② **已实现**容器的尺寸立刻改；未实现的会在 <c>ContainerContentChanging</c> 里补。
    /// ⚠️ 幂等：同一档重复下发不产生任何变化（设置一变就整份重套，见 MainWindow.ApplyAllSettings）。
    /// ⚠️ 图标位图**不重新提取**：缓存仍是同一份（默认 64px），这里只改显示尺寸。
    /// ⚠️ 尺寸要下发给**所有**图块（含被过滤隐藏的）—— 它们重新可见时不该是旧尺寸。
    /// </summary>
    public void ApplyIconSize(IconSizeMetrics metrics)
    {
        _iconSize = metrics;

        foreach (var tile in _tab.Icons)
        {
            tile.ApplySize(metrics);
        }

        RefreshRealizedContainers();
    }

    private void ApplyContainerSize(FrameworkElement container)
    {
        container.Width = _iconSize.TileWidth;
        container.Height = _iconSize.TileHeight;
    }

    // ────────────────────────────── 批量管理（勾选多项删除，W5）──────────────────────────────
    //
    // 照原版语义：进入批量管理模式 → 图块显示勾选框 → 点图块即勾选/取消 → 「批量删除勾选图标」
    // → 二次确认 → 删除；退出模式时清空勾选。
    // 形态上适配我们没有菜单栏的窗口：入口放**空白处右键菜单**，操作放页面顶部的**批量管理条**。
    // ⚠️ 逻辑（文案 / 勾选收敛 / 一次落盘的批量删除）都在 Core（`BulkDelete` + `DataStore.RemoveIcons`，有单测）。

    /// <summary>点「批量删除勾选图标」—— 把勾选的 id 交给宿主窗口（关确认框、落库都在那边）。</summary>
    public event EventHandler<IReadOnlyList<string>>? BulkDeleteRequested;

    /// <summary>批量模式的开关变了（宿主窗口据此更新状态栏提示）。</summary>
    public event EventHandler<bool>? BulkModeChanged;

    /// <summary>当前是否处于批量管理模式。</summary>
    public bool IsBulkMode { get; private set; }

    /// <summary>进入/退出批量管理模式（幂等）。</summary>
    public void SetBulkMode(bool on)
    {
        if (IsBulkMode == on)
        {
            return;
        }

        IsBulkMode = on;

        foreach (var tile in _tab.Icons)
        {
            tile.SetBulkMode(on);
        }

        BulkBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        // 批量模式下**禁用拖拽**：点图块的含义已经变成"勾选"，再让它可拖会互相打架
        RefreshRealizedContainers();

        UpdateBulkBar();
        BulkModeChanged?.Invoke(this, on);
    }

    /// <summary>容器是否可拖：批量模式下关掉（样式里默认是 True）。</summary>
    private void ApplyContainerDrag(FrameworkElement container)
    {
        if (container is GridViewItem item)
        {
            item.CanDrag = !IsBulkMode;
        }
    }

    /// <summary>
    /// 勾选清单（按页面顺序；界面是唯一来源，Core 会再收敛一次）。
    /// ⚠️ 只收**可见**的图块：过滤态下"看不见"的项不该被算进批量删除；
    ///    过滤一变 SetFilter 就会清掉全部勾选，所以不会留下"看不见却被勾着"的项。
    /// </summary>
    private IReadOnlyList<string> SelectedIconIds()
        => _tab.VisibleIcons.Where(tile => tile.IsChecked).Select(tile => tile.Model.Id).ToList();

    private void UpdateBulkBar() => BulkCount.Text = BulkDelete.SelectedText(SelectedIconIds().Count);

    private void OnBulkSelectAllClick(object sender, RoutedEventArgs e)
    {
        // 已经全勾了就变成"全不选"（一个按钮两用，省地方）；范围 = 当前可见的图块
        var all = _tab.VisibleIcons.ToList();
        var selectAll = all.Any(tile => !tile.IsChecked);

        foreach (var tile in all)
        {
            tile.IsChecked = selectAll;
        }

        UpdateBulkBar();
    }

    private void OnBulkDeleteClick(object sender, RoutedEventArgs e)
        => BulkDeleteRequested?.Invoke(this, SelectedIconIds());

    /// <summary>
    /// **宿主窗口用**（快捷键 `Shift+Delete`）：按当前勾选发起批量删除。
    ///
    /// <para>为什么不让宿主自己去数勾选：勾选状态（`IconTileViewModel.IsChecked`）与"只算可见项"
    /// 这条口径都是**页面自己的事**，宿主看不到（`VisibleIcons` 也是页面的视图模型）。
    /// 走这个方法 = 与点「批量删除」按钮**完全同一条路**。</para>
    ///
    /// <para>不在批量模式时什么都不做：没有批量模式就没有"勾选清单"可言
    /// （列表页也没有批量模式 —— 它的行只有"打开"）。</para>
    /// </summary>
    public void RequestBulkDelete()
    {
        if (!IsBulkMode)
        {
            return;
        }

        BulkDeleteRequested?.Invoke(this, SelectedIconIds());
    }

    private void OnBulkExitClick(object sender, RoutedEventArgs e) => SetBulkMode(false);

    /// <summary>批量删除完成后由宿主窗口调用：退出模式并收起勾选。</summary>
    public void LeaveBulkModeAfterDelete()
    {
        SetBulkMode(false);
        UpdateBulkBar();
    }

    /// <summary>点了某个图标 —— 交给宿主窗口去执行并反馈结果。</summary>
    public event EventHandler<IconModel>? IconActivated;

    /// <summary>在空白处选了「新建 XX 图标…」—— 交给宿主窗口（选文件 → 弹对话框 → 落库）。</summary>
    public event EventHandler<IconType>? NewIconRequested;

    /// <summary>图块右键菜单选了某一项 —— 交给宿主窗口执行（**页面不碰数据、不开对话框**）。</summary>
    public event EventHandler<IconMenuRequest>? IconMenuActionRequested;

    /// <summary>
    /// 从资源管理器拖入的文件/文件夹/快捷方式 —— 交给宿主窗口建图标
    /// （对应原版 <c>tab_widget._add_dropped_paths</c>）。
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? FilesDropped;

    /// <summary>拖放落下 —— 交给宿主窗口落库（页面自己不改数据）。</summary>
    public event EventHandler<DragDropRequest>? ItemDropped;

    /// <summary>
    /// 演示模式下不写盘：由宿主窗口置为 false 关掉拖拽（避免"看着能拖、其实存不下来"）。
    /// </summary>
    public bool DragDropEnabled
    {
        set
        {
            // ⚠️ 刻意**不设** TileGrid.CanDrag：ListViewBase 的 CanDrag=true 会让系统
            // "按下 + 移动"就自己起拖，绕过我们的长按门控（两条路打架）。
            // 起拖只由长按驱动：长按到点才把**容器**的 CanDrag 临时打开并调 StartDragAsync。
            TileGrid.AllowDrop = value;
        }
    }

    /// <summary>
    /// 演示模式下同样关掉「新建 / 编辑属性」菜单：假数据不会落库，菜单点了只会误导用户。
    /// </summary>
    public bool IconEditingEnabled { get; set; } = true;

    public void ApplyAnimationSpec(AnimationSpec spec) => _entrance.ApplySpec(spec);

    /// <summary>
    /// 拖放落点指示线用的是本项目注入的固定键（`AccentBrushDark`）——
    /// **它不随主题变**，所以这里按当前令牌直接赋值（切主题与页面创建时都会调用）。
    ///
    /// <para>顺带把主题的圆角倍率落到图块容器与批量条上（`radius` 参数，默认倍率 1.0 ⇒ 不变）。</para>
    /// </summary>
    public void ApplyTheme(ThemePalette palette, double radiusScale)
    {
        var (a, r, g, b) = palette.Accent;
        DropIndicator.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));

        _cornerScale = radiusScale;
        BulkBar.CornerRadius = ThemeScale.Corners(DesignCornerRadius, radiusScale);
        ApplyCornerRadiusToRealizedContainers();
    }

    /// <summary>图块/批量条的设计圆角（DIP）—— `radius` 参数默认值时就是它本身。</summary>
    private const double DesignCornerRadius = 6;

    /// <summary>当前主题的圆角倍率（默认 1.0）。</summary>
    private double _cornerScale = 1;

    /// <summary>把圆角写给"已经实现出来"的图块容器（新实现/回收的走 ContainerContentChanging）。</summary>
    private void ApplyCornerRadiusToRealizedContainers()
    {
        for (int i = 0; i < _tab.VisibleIcons.Count; i++)
        {
            if (TileGrid.ContainerFromIndex(i) is Control container)
            {
                container.CornerRadius = ThemeScale.Corners(DesignCornerRadius, _cornerScale);
            }
        }
    }

    /// <summary>置于入场起始态（整页不透明度 = 0）。必须在页面可见之前调用。</summary>
    public void PrepareEntrance() => _entrance.Prepare();

    /// <summary>等到"可以安全放行"（容器已就位或布局已跑过）再开始入场并放行整页。</summary>
    public void RevealWhenReady() => _entrance.RevealWhenReady();

    public TabItemViewModel Tab => _tab;

    private void OnTileClick(object sender, ItemClickEventArgs e)
    {
        // 长按（起拖/要菜单）之后系统偶尔还会补一次 ItemClick —— 那次不能当成"打开"
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            return;
        }

        // 单纯点击 = 没拖动 ⇒ 顺手清掉落点登记（防"上次拖动的落点"影响后续判断）
        DragSession.ClearTarget();

        if (e.ClickedItem is IconTileViewModel tile)
        {
            // 批量管理模式：点图块 = 勾选/取消勾选（**不打开**）—— 原版 batch 模式同义
            if (IsBulkMode)
            {
                tile.IsChecked = !tile.IsChecked;
                UpdateBulkBar();
                return;
            }

            IconActivated?.Invoke(this, tile.Model);
        }
    }

    // ────────────────────────────── 拖动链路（2026-09-16 第四次返工后的最终形态）──────────────────────────────
    //
    // 起拖交给系统的原生拖拽（按下拖动即起拖，用户明确要求取消长按门控）。
    //
    // ⚠️ 实测（ERROR.md E25）：本页**收不到** `DragStarting` / `DragItemsStarting` /
    //    `PointerPressed(handledEventsToo)`，WinUI 3 也不暴露对应 RoutedEvent，
    //    所以"拖的是谁"和"松手在哪"都不能靠那些事件。**实测可靠的只有两个**：
    //      ① 目标端 `DragOver`；
    //      ② 源端 `DragItemsCompleted`（`args.Items` 直接给出被拖的项，`args.DropResult` 给出落没落下）。
    //    于是：DragOver 里把落点登记进 `DragSession`，等 `DragItemsCompleted` 到达时两边一拼即完成落库。
    //    DataPackage 里永远是空的 ⇒ "载荷为空"正是"本应用内部拖动"的判据。

    private bool _suppressNextClick;
    private int _lastTracedIndex = -1;         // 拖动中只在落点变化时打一条日志，别刷屏
    private bool _tracedDragOverEntry;         // 本次拖动是否已打过"首次进入 DragOver"的详情
    private bool _dropSeen;                    // 本次拖动是否真的落在本页（Drop 事件到场）

    /// <summary>拖动链路诊断 —— 写 `%TEMP%\toolboxpanel-probe.log`（手感类问题只能用户手试，
    /// 有了这条链路日志，用户试一次就能定位"哪一步断了"）。</summary>
    private static void DragTrace(string message)
        => App.ProbeLog($"[拖动 {DateTime.Now:HH:mm:ss.fff}] {message}");

    /// <summary>读一下这次拖放到底带了什么（诊断用；读不到就当作没有）。</summary>
    private static (bool HasText, string? Text, bool HasStorageItems) DescribeData(DragEventArgs e)
    {
        bool hasText = false;
        string? text = null;
        bool hasStorage = false;

        try
        {
            hasText = e.DataView.Contains(StandardDataFormats.Text);
            if (hasText)
            {
                text = e.DataView.GetTextAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            App.WriteCrash("GridPage.DescribeData/text", ex);
        }

        try
        {
            hasStorage = e.DataView.Contains(StandardDataFormats.StorageItems);
        }
        catch (Exception ex)
        {
            App.WriteCrash("GridPage.DescribeData/storage", ex);
        }

        return (hasText, text, hasStorage);
    }
    // ────────────────────────────── 右键菜单（W5：新建 / 编辑属性）──────────────────────────────
    //
    // 交互照原版 v1.11.6：
    //   · 空白处右键 → 新建文件 / 文件夹 / 快捷方式 / （分隔）网址 / 命令图标…
    //   · 图块上右键 → 编辑属性…
    //
    // ⚠️ 页面**只发事件**：开文件选择框、弹对话框、落库都在宿主窗口（MainWindow）里做 ——
    //    页面拿不到 DataStore，这样也就不可能"界面改了、数据没改"。

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (!IconEditingEnabled)
        {
            return;
        }

        // 键盘菜单键（Shift+F10）时拿不到坐标，退回左上角；右键时就是鼠标位置
        var position = args.TryGetPosition(TileGrid, out var point) ? point : new Windows.Foundation.Point(0, 0);

        var tile = FindTileFromSource(args.OriginalSource);
        if (tile is null)
        {
            BuildNewIconMenu().ShowAt(TileGrid, position);
        }
        else
        {
            ShowTileMenu(tile, position);
        }

        args.Handled = true;
    }

    /// <summary>弹某个图块的菜单（右键与"长按原地松手"共用同一个入口）。</summary>
    private void ShowTileMenu(IconTileViewModel tile, Windows.Foundation.Point position)
        => BuildTileMenu(tile).ShowAt(TileGrid, position);

    /// <summary>空白处菜单：新建五类（顺序与分隔线照原版）+ 批量管理。</summary>
    private MenuFlyout BuildNewIconMenu()
    {
        var menu = new MenuFlyout();

        AddNew(I18n.T("grid.menu.file"), IconType.File);
        AddNew(I18n.T("grid.menu.folder"), IconType.Folder);
        AddNew(I18n.T("grid.menu.shortcut"), IconType.Shortcut);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddNew(I18n.T("grid.menu.url"), IconType.Url);
        AddNew(I18n.T("grid.menu.command"), IconType.Command);

        // 批量管理（原版在菜单栏里；我们没有菜单栏，放在页面级菜单的末尾）
        menu.Items.Add(new MenuFlyoutSeparator());
        var bulkItem = new MenuFlyoutItem { Text = BulkDelete.MenuLabel };
        bulkItem.Click += (_, _) => SetBulkMode(true);
        menu.Items.Add(bulkItem);

        return menu;

        void AddNew(string text, IconType type)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => NewIconRequested?.Invoke(this, type);
            menu.Items.Add(item);
        }
    }

    /// <summary>
    /// 图块菜单：**按 Core 的 <see cref="IconContextMenu.Build"/> 规格铺**。
    ///
    /// <para>顺序、按类型的门控（"用其他应用打开…"只对文件/文件夹/快捷方式出现）、
    /// 分隔线位置与文案全部来自 Core（有单测钉住），这里只负责把它们变成 <see cref="MenuFlyoutItem"/>。</para>
    /// </summary>
    private MenuFlyout BuildTileMenu(IconTileViewModel tile)
    {
        var menu = new MenuFlyout();

        foreach (var item in IconContextMenu.Build(tile.Model.Type))
        {
            if (item.SeparatorBefore)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            var action = item.Action;
            var menuItem = new MenuFlyoutItem { Text = item.Label };
            menuItem.Click += (_, _) => IconMenuActionRequested?.Invoke(this, new IconMenuRequest(tile.Model, action));
            menu.Items.Add(menuItem);
        }

        return menu;
    }

    /// <summary>右键点在哪 —— 往上找到承载图块的 GridViewItem；点在空白处返回 null。</summary>
    private static IconTileViewModel? FindTileFromSource(object? source)
    {
        var current = source as DependencyObject;

        while (current is not null)
        {
            if (current is GridViewItem { DataContext: IconTileViewModel tile })
            {
                return tile;
            }

            try
            {
                current = VisualTreeHelper.GetParent(current);
            }
            catch (Exception)
            {
                // 命中的不是可视元素（例如 Run/TextElement）：当作"点在空白处"
                return null;
            }
        }

        return null;
    }

    // ────────────────────────────── 拖拽排序（2026-09-16 最终形态）──────────────────────────────
    //
    // 收不到任何起拖/指针事件（ERROR.md E25），所以只靠两个实测可靠的事件：
    //   · 这里（目标端）在 `DragOver` 里**登记落点** → `DragSession.ReportTarget(...)`；
    //   · 源端在 `DragItemsCompleted` 里带着"被拖项 + DropResult"来取走落点并落库。
    // DataPackage 里永远是空的 ⇒ **"载荷为空 + 没有 StorageItems"就是"本应用内部拖动"的判据**。

    /// <summary>
    /// 只接受两类拖放：
    ///   ① **本应用内部的图标重排**（DataPackage 为空 —— 框架不给我们写载荷的机会，见 E25）；
    ///   ② **从资源管理器拖进来的文件/文件夹/快捷方式**（StorageItems）—— 建新图标（原版语义）。
    /// 其余一律拒绝（例如"别处拖来的文本"）。拖动中顺便算出落点、登记给源端，并画插入竖条。
    /// </summary>
    private void OnTileDragOver(object sender, DragEventArgs e)
    {
        var (hasText, text, hasStorage) = DescribeData(e);

        // ⚠️ 无条件打一次"进入 DragOver"的详情：载荷有没有、是什么，一目了然。
        if (!_tracedDragOverEntry)
        {
            _tracedDragOverEntry = true;
            DragTrace($"DragOver 首次：含文本={hasText} 文本=\"{text}\" 含StorageItems={hasStorage} "
                      + $"判定={(DragSession.LooksLikeInternalDrag(hasText, hasStorage) ? "内部拖动" : "非内部")}");
        }

        if (DragSession.LooksLikeInternalDrag(hasText, hasStorage))
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.IsCaptionVisible = false;
            _suppressNextClick = true;   // 起拖之后系统补的那次 ItemClick 不能当"打开"

            var insertIndex = ComputeInsertIndex(e);
            DragSession.ReportTarget(_tab.Id, DragItemKind.Icon, insertIndex);

            if (insertIndex != _lastTracedIndex)
            {
                _lastTracedIndex = insertIndex;
                DragTrace($"DragOver：落点索引={insertIndex}（已登记）");
            }

            // 拖动中**不移动任何图块**（用户 2026-09-15："只加竖条，不让位"），
            // 只在相邻图块之间画一根插入竖条表示"松手会插到这里"。
            ShowDropIndicator(insertIndex);

            return;
        }

        if (hasStorage)
        {
            // 外部拖入：接受"复制"语义（原版也是把拖入当成"新建图标"，不移动原文件）
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.IsCaptionVisible = false;
            HideDropIndicator();
            return;
        }

        e.AcceptedOperation = DataPackageOperation.None;
        HideDropIndicator();
    }

    /// <summary>
    /// 拖放落下：内部重排 → 交给宿主窗口落库；外部拖入 → 把路径交给宿主窗口建图标。
    ///
    /// <para>⚠️ 外部拖入必须用 <c>GetDeferral()</c> 包住 `await`：Drop 事件返回后 DataView 就可能失效，
    /// 没有 deferral 的话取 StorageItems 会随机拿不到东西（WinUI/OS 的硬要求）。</para>
    /// </summary>
    private async void OnTileDrop(object sender, DragEventArgs e)
    {
        var (hasText, text, hasStorage) = DescribeData(e);
        DragTrace($"Drop：含文本={hasText} 文本=\"{text}\" 含StorageItems={hasStorage}");

        // ① 本应用内部的重排/跨页移动：**这里不做动作**（Drop 不一定来，而且它不带"拖的是谁"）。
        //    但它是"真的落在本页"的**强信号** —— 记下来，给 DragItemsCompleted 用（载荷为空时
        //    OS 可能把 DropResult 报成 None，光看 DropResult 会漏掉）。
        if (DragSession.LooksLikeInternalDrag(hasText, hasStorage))
        {
            _dropSeen = true;
            var dropIndex = ComputeInsertIndex(e);
            DragSession.ReportTarget(_tab.Id, DragItemKind.Icon, dropIndex);
            HideDropIndicator();
            DragTrace($"Drop（内部拖动）：落点={dropIndex} ⇒ 留给 DragItemsCompleted 收口");
            return;
        }

        // ② 从资源管理器拖入的文件/文件夹/快捷方式
        if (!hasStorage)
        {
            HideDropIndicator();
            return;
        }

        HideDropIndicator();
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = new List<string>(items.Count);

            foreach (var item in items)
            {
                switch (item)
                {
                    case StorageFile file when !string.IsNullOrWhiteSpace(file.Path):
                        paths.Add(file.Path);
                        break;
                    case StorageFolder folder when !string.IsNullOrWhiteSpace(folder.Path):
                        paths.Add(folder.Path);
                        break;
                }
            }

            if (paths.Count > 0)
            {
                FilesDropped?.Invoke(this, paths);
            }
        }
        catch (Exception ex)
        {
            App.WriteCrash("GridPage.OnTileDrop/StorageItems", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// ★ **一次内部拖动的收口点**（源端事件，实测可靠）：
    /// `args.Items` 给出**被拖的图块本身**，`args.DropResult` 给出"到底落下了没有"，
    /// 再加上目标端在 `DragOver` 里登记的落点，三者一拼就是完整的一次重排/跨页移动。
    ///
    /// <para>⚠️ 这正是绕开"起拖事件全收不到"的关键：**拖动过程中我们不知道拖的是谁，
    /// 但拖动结束这一刻框架会告诉我们。**</para>
    /// </summary>
    private void OnTileDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        HideDropIndicator();
        _lastTracedIndex = -1;
        _suppressNextClick = false;

        // ⚠️ 复位"本次拖动是否已打过 DragOver 详情"。**漏了这一行，诊断日志从第二次拖动起就废了**：
        //    `_tracedDragOverEntry` 只写不复位 ⇒ `DragOver 首次：…` 那条从此再也不出现，
        //    而 HANDOVER 把 %TEMP%\toolboxpanel-probe.log 当常驻诊断资产（"拖放手感先看它"）。
        _tracedDragOverEntry = false;

        var target = DragSession.TakeTarget();
        var tile = args.Items.Count > 0 ? args.Items[0] as IconTileViewModel : null;
        var dropResult = args.DropResult;
        var dropSeen = _dropSeen;
        _dropSeen = false;

        DragTrace($"DragItemsCompleted：DropResult={dropResult} items={args.Items.Count} "
                  + $"被拖={tile?.DisplayName ?? "null"} 登记落点={(target is null ? "无" : $"{target.Value.TabId}#{target.Value.InsertIndex}")} "
                  + $"Drop到过本页={dropSeen}");

        if (tile is null || target is null)
        {
            return;   // 取消（Esc / 丢到窗外）或没进过本页 ⇒ 什么都不做
        }

        if (target.Value.Kind != DragItemKind.Icon)
        {
            DragTrace("→ 落点是列表页，图标不进列表页 ⇒ 忽略");
            return;
        }

        // ⚠️ 结算**延后一拍**：`Drop` 与 `DragItemsCompleted` 的先后顺序在本项目实测不保证，
        //    先让可能到来的 Drop 把 `_dropSeen` 置上，再决定"到底算不算落下"。
        //
        // ⚠️ 这里**只认上面那个 `dropSeen` 快照**，绝不能再读一次字段 `_dropSeen`：
        //    那一次读发生在"下一拍"，此时如果用户已经开始**第二次拖动**，
        //    新拖动刚置上的 `_dropSeen=true` 会被当成本轮的"落下"证据 ⇒
        //    本该丢弃的取消拖动被落库（顺序被改）。同理也不再无条件把字段清零。
        DispatcherQueue.TryEnqueue(() =>
        {
            var landed = dropResult == DataPackageOperation.Move || dropSeen;

            if (!landed)
            {
                DragTrace("→ 判定：拖动被取消（DropResult 非 Move 且没收到 Drop）⇒ 不落库");
                return;
            }

            var payload = new DragPayload(DragItemKind.Icon, _tab.Id, tile.Model.Id);
            DragTrace($"→ 落库：{tile.DisplayName} → 页 {target.Value.TabId} 第 {target.Value.InsertIndex} 位");
            ItemDropped?.Invoke(this, new DragDropRequest(payload, target.Value.TabId, target.Value.InsertIndex));
        });
    }

    // ────────────────────────────── 插入竖条（2026-09-15 起，唯一一种拖拽反馈）──────────────────────────────
    //
    // 用户 2026-09-15 明确要求："拖动到图标附近时，在相邻图标处显示插入的符号，并且插入" ——
    // **只加竖条，不让位**。所以这里不再有"实时让位/取消还原"那套（已整体删除）：
    //   · 拖动期间：集合一动不动，只在落点处画一根强调色竖条；
    //   · 松手：走 ItemDropped → MainViewModel.ApplyDrop → DataStore.ApplyDragDrop 落库（含跨页）。
    //   · 载荷解析统一用 DescribeData（顺带无条件写日志），不再单独 TryReadPayload。

    /// <summary>把落点（相对本页的坐标）交给 Core 的几何计算，得到"插到第几个"。</summary>
    private int ComputeInsertIndex(DragEventArgs e)
    {
        var bounds = CollectItemBounds();
        if (bounds.Count == 0)
        {
            return 0;
        }

        var position = e.GetPosition(this);
        return DropIndexCalculator.Compute(bounds, position.X, position.Y);
    }

    /// <summary>
    /// 收集每个已实现图块的矩形（**相对本页**，DIP），顺序 = 界面上看到的顺序。
    /// ⚠️ 只用"已实现"的容器：GridView 会虚拟化，屏幕外的项没有容器，
    ///    查不到就跳过（而不是塞一个 0 尺寸的假矩形，那会把落点算歪）。
    /// ⚠️ 遍历的是**可见集合**（= ItemsSource）：算出来的落点是"可见位"，
    ///    过滤态下由 MainViewModel 用 Core 的换算函数换回 Core 下标。
    /// </summary>
    private List<ItemBounds> CollectItemBounds()
    {
        var result = new List<ItemBounds>(_tab.VisibleIcons.Count);

        for (int i = 0; i < _tab.VisibleIcons.Count; i++)
        {
            if (TileGrid.ContainerFromIndex(i) is not FrameworkElement container)
            {
                continue;
            }

            try
            {
                var origin = container.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, 0));
                result.Add(new ItemBounds(origin.X, origin.Y, container.ActualWidth, container.ActualHeight));
            }
            catch (Exception ex)
            {
                App.WriteCrash("GridPage.CollectItemBounds", ex);
            }
        }

        return result;
    }

    private void ShowDropIndicator(int insertIndex)
    {
        var bounds = CollectItemBounds();
        if (bounds.Count == 0)
        {
            HideDropIndicator();
            return;
        }

        var (x, y, height) = DropIndexCalculator.IndicatorAt(bounds, insertIndex);

        // ⚠️ 竖条要**骑在边界线上**（左移半个条宽），看起来才是"插在两块之间"，
        //    而不是"盖在右边那一块上"。
        Canvas.SetLeft(DropIndicator, x - DropIndicator.Width / 2);
        Canvas.SetTop(DropIndicator, y);
        DropIndicator.Height = Math.Max(8, height);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDropIndicator() => DropIndicator.Visibility = Visibility.Collapsed;
}

/// <summary>
/// 一次右键菜单动作请求（页面 → 宿主窗口）。
/// 用"一个事件 + 动作枚举"而不是给每个动作开一个事件：菜单项以后再加也不会到处改签名。
/// </summary>
public sealed record IconMenuRequest(IconModel Icon, IconMenuAction Action);