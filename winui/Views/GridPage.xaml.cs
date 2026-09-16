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

public sealed partial class GridPage : UserControl, IAnimatedPage
{
    private readonly TabItemViewModel _tab;
    private readonly EntranceAnimator _entrance;

    public GridPage(TabItemViewModel tab)
    {
        _tab = tab;

        InitializeComponent();

        TileGrid.ItemsSource = tab.Icons;
        EmptyHint.Visibility = tab.Icons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _entrance = new EntranceAnimator(TileGrid);

        // ⚠️⚠️ 起拖事件（`DragStarting` / `DragItemsStarting`）**收不到** ——
        //   `ListViewBase` 内部处理并标记 handled，XAML 处理器不触发；WinUI 3 又不暴露对应
        //   RoutedEvent，`AddHandler(handledEventsToo:true)` 也做不了（2026-09-16 实测，见 ERROR.md E25）。
        //   所以载荷改走进程内会话（DragSession），**在按下时记下"拖谁"** ——
        //   指针事件是能收到 handledEventsToo 的（实测有效）。
        TileGrid.AddHandler(
            PointerPressedEvent,
            new PointerEventHandler(OnTilePointerPressed),
            handledEventsToo: true);

        TileGrid.AddHandler(
            PointerReleasedEvent,
            new PointerEventHandler(OnTilePointerReleased),
            handledEventsToo: true);
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

        // 单纯点击 = 没拖动 ⇒ 顺手收掉拖动会话（防"会话残留"影响后续拖放）
        DragSession.End();

        if (e.ClickedItem is IconTileViewModel tile)
        {
            IconActivated?.Invoke(this, tile.Model);
        }
    }

    // ────────────────────────────── 拖动反馈（"浮起"占位，2026-09-14 第三版）──────────────────────────────
    //
    // 起拖**交给系统的原生拖拽**：**按下拖动即起拖（像 Windows 桌面），不再有长按门控**
    // —— 用户明确要求取消长按那套判定（"只要有拖动的话都算"）。
    // 这里只负责两件事：
    //   ① 拖动开始时把原件"浮起"（轻微放大 + 半透明当占位）—— 跟着鼠标的是系统抓走的图块快照；
    //   ② 起拖之后系统偶尔补的那次 ItemClick 不能当成"打开"。
    // ⚠️ 压暗必须等拖拽视觉**抓取之后**（先 TryEnqueue 排一下），否则被抓走的那份也带 35% 透明度。

    private bool _suppressNextClick;
    private FrameworkElement? _liftedContainer;
    private IconTileViewModel? _pressedTile;   // 按下落在哪个图块上（= 本次拖动的对象）
    private bool _dragStarted;                 // 本次拖动是否已经真的开始（由 DragOver 判定）
    private int _lastTracedIndex = -1;         // 拖动中只在落点变化时打一条日志，别刷屏
    private int _lastDropIndex = -1;           // 本次拖动最后算出的落点（Drop 不来时兜底落库用）
    private bool _tracedDragOverEntry;         // 本次拖动是否已打过"首次进入 DragOver"的详情

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

    /// <summary>"浮起"反馈：只用合成变换（Scale / Opacity），不碰布局 —— 不会把相邻图块挤走。</summary>
    private void ApplyLift(FrameworkElement container, bool lifted)
    {
        try
        {
            container.CenterPoint = new Vector3(
                (float)(container.ActualWidth / 2), (float)(container.ActualHeight / 2), 0f);
            container.Scale = lifted ? new Vector3(1.08f, 1.08f, 1f) : new Vector3(1f, 1f, 1f);
            container.Opacity = lifted ? 0.35 : 1.0;
        }
        catch (Exception ex)
        {
            App.WriteCrash("GridPage.ApplyLift", ex);
        }
    }

    private void EndLiftForDrag()
    {
        if (_liftedContainer is { } container)
        {
            ApplyLift(container, lifted: false);
            _liftedContainer = null;
        }
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

    // ────────────────────────────── 拖拽排序 ──────────────────────────────
    //
    // 载荷不走 DataPackage，走 DragSession（理由见文件头与本类构造函数注释 / ERROR.md E25）：
    //   · 按下 → 记下"拖的是谁"并 Begin 会话（这是唯一能可靠收到的起点事件）；
    //   · DragOver/Drop → 读会话判断"是不是本应用在拖这类东西"，再算落点、画插入竖条；
    //   · 拖动开始/结束由 DragItemsCompleted 与 Drop 收口。

    /// <summary>按下：记下被拖的图块，并开启拖动会话（跨页拖动也认得出）。</summary>
    private void OnTilePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pressedTile = FindTileFromSource(e.OriginalSource);

        if (_pressedTile is null)
        {
            DragSession.End();
            return;
        }

        DragSession.Begin(new DragPayload(DragItemKind.Icon, _tab.Id, _pressedTile.Model.Id));
        DragTrace($"按下：{_pressedTile.DisplayName}（会话已开）");
    }

    /// <summary>松手：单纯点击（没起拖）就把会话收掉，避免影响后续别的拖放。</summary>
    private void OnTilePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragStarted)
        {
            DragSession.End();
        }
    }

    /// <summary>
    /// 拖动真的开始了（第一次 DragOver 才发现 —— 起拖事件收不到，这里是最早的可靠信号）：
    /// 把原件"浮起"当占位。⚠️ 压暗延后一拍，免得被抓走的拖拽视觉也带 35% 透明度。
    /// </summary>
    private void EnsureLiftStarted()
    {
        if (_dragStarted)
        {
            return;
        }

        _dragStarted = true;
        _suppressNextClick = true;   // 起拖之后系统补的那次 ItemClick 不能当"打开"

        if (_pressedTile is not null && TileGrid.ContainerFromItem(_pressedTile) is FrameworkElement container)
        {
            _liftedContainer = container;
            DispatcherQueue.TryEnqueue(() => ApplyLift(container, lifted: true));
        }
    }

    /// <summary>
    /// 只接受两类拖放：
    ///   ① **本应用内部的图标重排**（文本载荷，见 <see cref="DragPayload"/>）；
    ///   ② **从资源管理器拖进来的文件/文件夹/快捷方式**（StorageItems）—— 建新图标（原版语义）。
    /// 其余一律拒绝（例如"图标拖到列表页"）。拖动中顺便算出落点并画指示线（只对内部重排有意义）。
    /// </summary>
    private void OnTileDragOver(object sender, DragEventArgs e)
    {
        var (hasText, text, hasStorage) = DescribeData(e);

        // ⚠️ 无条件打一次"进入 DragOver"的详情：载荷有没有、是什么，一目了然。
        //    （本应用内部拖动时 DataView 本来就是空的 —— 载荷走 DragSession。）
        if (!_tracedDragOverEntry)
        {
            _tracedDragOverEntry = true;
            DragTrace($"DragOver 首次：含文本={hasText} 文本=\"{text}\" 含StorageItems={hasStorage} "
                      + $"会话={(DragSession.Current is null ? "无" : DragSession.Current.ItemId)}");
        }

        var payload = DragSession.Take(_tab.DraggableKind);
        if (payload is not null && !hasStorage)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.IsCaptionVisible = false;

            EnsureLiftStarted();   // 到这一步才算"拖动真的开始了"

            var insertIndex = ComputeInsertIndex(e);
            _lastDropIndex = insertIndex;

            if (insertIndex != _lastTracedIndex)
            {
                _lastTracedIndex = insertIndex;
                DragTrace($"DragOver：落点索引={insertIndex}");
            }

            // 拖动中**不移动任何图块**（用户 2026-09-15："只加竖条，不让位"），
            // 只在相邻图块之间画一根插入竖条表示"松手会插到这里"。
            ShowDropIndicator(insertIndex);

            return;
        }

        if (hasStorage)
        {
            // 外部拖入（含"会话残留"时的兜底）：接受"复制"语义，把路径交给宿主窗口建图标
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
        DragTrace($"Drop：含文本={hasText} 文本=\"{text}\" 含StorageItems={hasStorage} "
                  + $"会话={(DragSession.Current is null ? "无" : DragSession.Current.ItemId)}");

        // ① 本应用内部的重排/跨页移动（会话携带"拖的是谁"）
        var payload = DragSession.Take(_tab.DraggableKind);
        if (payload is not null && !hasStorage)
        {
            DragSession.End();

            var insertIndex = ComputeInsertIndex(e);
            HideDropIndicator();
            DragTrace($"Drop（内部重排）：落点={insertIndex}");

            ItemDropped?.Invoke(this, new DragDropRequest(payload, _tab.Id, insertIndex));
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
    /// 拖动结束（含取消）：收掉插入竖条、复位"浮起"。
    /// 拖动期间没有移动任何图块，所以这里**不需要**做任何"还原"或"补落库"——
    /// 真正的顺序变化只发生在 Drop 落库那一步。
    /// </summary>
    private void OnTileDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        HideDropIndicator();
        EndLiftForDrag();
        _lastTracedIndex = -1;
        _dragStarted = false;
        _pressedTile = null;

        // ⚠️ 兜底：`Drop` 事件不一定来（载荷为空时 OS 可能不投递）。
        //    只要"会话还在"（= Drop 没处理过，处理时就会 End）且这次确实是落在本页（DropResult=Move），
        //    就按**最后一次算出的落点**落库 —— 别让用户白拖一次。落点由 DragOver 记录，正是用户看到竖条的位置。
        var session = DragSession.Current;
        var dropIndex = _lastDropIndex;
        _lastDropIndex = -1;
        DragSession.End();

        if (session is not null && args.DropResult == DataPackageOperation.Move && dropIndex >= 0)
        {
            DragTrace($"DragItemsCompleted：Drop 没来但 DropResult=Move ⇒ 按落点 {dropIndex} 补落库");
            ItemDropped?.Invoke(this, new DragDropRequest(session, _tab.Id, dropIndex));
            return;
        }

        DragTrace($"DragItemsCompleted：DropResult={args.DropResult}");
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