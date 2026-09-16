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
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;
using ToolboxPanel.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

namespace ToolboxPanel.Views;

public sealed partial class GridPage : UserControl, IAnimatedPage, IIconSizedPage
{
    private readonly TabItemViewModel _tab;
    private readonly EntranceAnimator _entrance;

    /// <summary>当前图标大小档（默认 medium；由主窗口在创建页面与设置变化时下发）。</summary>
    private IconSizeMetrics _iconSize = IconSizeMetrics.Medium;

    public GridPage(TabItemViewModel tab)
    {
        _tab = tab;

        InitializeComponent();

        TileGrid.ItemsSource = tab.Icons;
        EmptyHint.Visibility = tab.Icons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _entrance = new EntranceAnimator(TileGrid);

        // 图块容器尺寸由代码按档位设置（样式里那对 68×72 只是 medium 的默认值）：
        // 新实现/回收再利用的容器都要在这里补一次，否则换档后滚动出来的图块会是旧尺寸。
        TileGrid.ContainerContentChanging += (_, args) =>
        {
            if (!args.InRecycleQueue && args.ItemContainer is FrameworkElement container)
            {
                ApplyContainerSize(container);
            }
        };

        // ⚠️ 这里**不再挂 PointerPressed**：实测它也收不到（日志里"按下"一行都没有）。
        //    拖动链路只依赖两个实测可靠的事件：目标端 `DragOver` + 源端 `DragItemsCompleted`，
        //    落点由 DragSession 在拖动中登记。详见 ERROR.md E25。
    }

    /// <summary>
    /// 套用"图标大小"档（设置：小 / 中 / 大）：
    /// ① 图块视图模型换档（模板 x:Bind OneWay ⇒ 图标、字形、名称字号就地刷新）；
    /// ② **已实现**容器的尺寸立刻改；未实现的会在 <c>ContainerContentChanging</c> 里补。
    /// ⚠️ 幂等：同一档重复下发不产生任何变化（设置一变就整份重套，见 MainWindow.ApplyAllSettings）。
    /// ⚠️ 图标位图**不重新提取**：缓存仍是同一份（默认 64px），这里只改显示尺寸。
    /// </summary>
    public void ApplyIconSize(IconSizeMetrics metrics)
    {
        _iconSize = metrics;

        foreach (var tile in _tab.Icons)
        {
            tile.ApplySize(metrics);
        }

        for (int i = 0; i < _tab.Icons.Count; i++)
        {
            if (TileGrid.ContainerFromIndex(i) is FrameworkElement container)
            {
                ApplyContainerSize(container);
            }
        }
    }

    private void ApplyContainerSize(FrameworkElement container)
    {
        container.Width = _iconSize.TileWidth;
        container.Height = _iconSize.TileHeight;
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
        get => _dragDropEnabled;
        set
        {
            _dragDropEnabled = value;

            // ⚠️ 刻意**不设** TileGrid.CanDrag：ListViewBase 的 CanDrag=true 会让系统
            // "按下 + 移动"就自己起拖，绕过我们的长按门控（两条路打架）。
            // 起拖只由长按驱动：长按到点才把**容器**的 CanDrag 临时打开并调 StartDragAsync。
            TileGrid.AllowDrop = value;
        }
    }

    private bool _dragDropEnabled;

    /// <summary>
    /// 演示模式下同样关掉「新建 / 编辑属性」菜单：假数据不会落库，菜单点了只会误导用户。
    /// </summary>
    public bool IconEditingEnabled { get; set; } = true;

    public void ApplyAnimationSpec(AnimationSpec spec) => _entrance.ApplySpec(spec);

    /// <summary>
    /// 拖放落点指示线用的是本项目注入的固定键（`AccentBrushDark`）——
    /// **它不随主题变**，所以这里按当前令牌直接赋值（切主题与页面创建时都会调用）。
    /// </summary>
    public void ApplyTheme(ThemePalette palette)
    {
        var (a, r, g, b) = palette.Accent;
        DropIndicator.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
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
    private int _lastDropIndex = -1;           // 本次拖动最后登记的落点（日志/自检用）
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

    /// <summary>空白处菜单：新建五类（顺序与分隔线照原版）。</summary>
    private MenuFlyout BuildNewIconMenu()
    {
        var menu = new MenuFlyout();

        AddNew("新建文件图标…", IconType.File);
        AddNew("新建文件夹图标…", IconType.Folder);
        AddNew("新建快捷方式图标…", IconType.Shortcut);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddNew("新建网址图标…", IconType.Url);
        AddNew("新建命令图标…", IconType.Command);

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
    private static GridViewItem? FindContainerFromSource(object? source)
    {
        var current = source as DependencyObject;

        while (current is not null)
        {
            if (current is GridViewItem item)
            {
                return item;
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

    /// <summary>右键点在哪 —— 往上找到承载图块的 GridViewItem；点在空白处返回 null。</summary>
    private static IconTileViewModel? FindTileFromSource(object? source)    {
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
            _lastDropIndex = insertIndex;
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
            _lastDropIndex = ComputeInsertIndex(e);
            DragSession.ReportTarget(_tab.Id, DragItemKind.Icon, _lastDropIndex);
            HideDropIndicator();
            DragTrace($"Drop（内部拖动）：落点={_lastDropIndex} ⇒ 留给 DragItemsCompleted 收口");
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

        var target = DragSession.TakeTarget();
        var tile = args.Items.Count > 0 ? args.Items[0] as IconTileViewModel : null;
        var dropResult = args.DropResult;
        var dropSeen = _dropSeen;
        _dropSeen = false;
        _lastDropIndex = -1;

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
        DispatcherQueue.TryEnqueue(() =>
        {
            var landed = dropResult == DataPackageOperation.Move || dropSeen || _dropSeen;
            _dropSeen = false;

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
    /// </summary>
    private List<ItemBounds> CollectItemBounds()
    {
        var result = new List<ItemBounds>(_tab.Icons.Count);

        for (int i = 0; i < _tab.Icons.Count; i++)
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

    /// <summary>从 DragStarting 的事件源拿到承载图块的展示模型（容器的 DataContext）。</summary>
    private static IconTileViewModel? FindTileFromArgs(DragStartingEventArgs args)
        => (args.OriginalSource as FrameworkElement)?.DataContext as IconTileViewModel;
}

/// <summary>
/// 一次右键菜单动作请求（页面 → 宿主窗口）。
/// 用"一个事件 + 动作枚举"而不是给每个动作开一个事件：菜单项以后再加也不会到处改签名。
/// </summary>
public sealed record IconMenuRequest(IconModel Icon, IconMenuAction Action);