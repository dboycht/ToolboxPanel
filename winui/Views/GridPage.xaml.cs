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
//   · 拖动中：DragOver 算出"会插到第几个"，并用一条 2px 指示线画出来；
//   · 放下：Drop 把结果交给宿主窗口 → MainViewModel.ApplyDrop → DataStore 落库 → 界面跟着 Core 重排。
// 好处是"同页排序 / 跨页移动 / 拒绝异类拖放"三条路共用同一套代码和同一个落库入口。
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

        // 长按起拖要用**指针事件**；而 ListViewBase / GridViewItem 自己也会处理这些事件，
        // 所以必须 handledEventsToo: true —— 否则我们根本收不到（GridView 内部已经把按下吃掉了）。
        TileGrid.AddHandler(PointerPressedEvent, new PointerEventHandler(OnTilePointerPressed), handledEventsToo: true);
        TileGrid.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnTilePointerReleased), handledEventsToo: true);
        TileGrid.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnTilePointerAborted), handledEventsToo: true);
        TileGrid.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnTilePointerAborted), handledEventsToo: true);
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
    /// 做过"实时让位"的拖动落下时：把界面上的最终顺序交给宿主窗口落库。
    /// （分工：**本页内**拖动走这个 —— 界面已经让好位了，只需把顺序写下去；
    ///   **跨页**移动仍走 <see cref="ItemDropped"/>，因为被拖项不在本页集合里、让不了位。）
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? OrderCommitted;

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

        if (e.ClickedItem is IconTileViewModel tile)
        {
            IconActivated?.Invoke(this, tile.Model);
        }
    }

    // ────────────────────────────── 长按起拖（手机桌面式，2026-09-14 第二版）──────────────────────────────
    //
    // 手感（用户明确要求，别再自作聪明）：
    //   **按住 350ms ⇒ 图块立刻"浮起"跟手拖，像磁贴一样；松手落在哪就排到哪。**
    //   · **没有**"长按原地松手弹菜单"这套判定（菜单只管鼠标右键 / 键盘菜单键）；
    //   · **不因**"按住期间移动过"而取消 —— 只要按够久就起拖（上一版这里会静默作废，像"没反应"）；
    //   · 阈值之前松手 = 普通点击（打开图标），由 ItemClick 负责。
    //
    // ⚠️⚠️ **Windows 定时器会早到**：早到时 `Tick()` 返回 false，必须按 `RemainingMs` **重排再试**，
    //      否则那一次长按会被静默丢掉 —— 上一版"长按偶发完全没反应"就是这个 bug。

    private readonly LongPressGesture _longPress = new();

    private DispatcherQueueTimer? _longPressTimer;
    private GridViewItem? _pressedContainer;
    private Microsoft.UI.Input.PointerPoint? _pressedPoint;   // WinUI 3 的 PointerPoint 在 Microsoft.UI.Input
    private bool _suppressNextClick;

    private void OnTilePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _suppressNextClick = false;

        if (!DragDropEnabled)
        {
            return;
        }

        var point = e.GetCurrentPoint(TileGrid);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;                     // 右键另有菜单
        }

        var container = FindContainerFromSource(e.OriginalSource);
        if (container is null)
        {
            return;                     // 空白处按下：不参与长按
        }

        _pressedContainer = container;
        _pressedPoint = point;
        _longPress.Press(Environment.TickCount64);
        RestartLongPressTimer(_longPress.ThresholdMs);
    }

    private void OnTilePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _longPressTimer?.Stop();
        _longPress.Release();
        ResetPressedTile();             // 拖动（若已起拖）由 StartLongPressDragAsync 收尾
    }

    /// <summary>指针被取消 / 捕获丢失（切页等）：清干净，别留下"按着"的假状态。</summary>
    private void OnTilePointerAborted(object sender, PointerRoutedEventArgs e)
    {
        _longPressTimer?.Stop();
        _longPress.Reset();
        ResetPressedTile();
    }

    private DispatcherQueueTimer EnsureLongPressTimer()
    {
        if (_longPressTimer is null)
        {
            _longPressTimer = DispatcherQueue.CreateTimer();
            _longPressTimer.IsRepeating = false;
            _longPressTimer.Tick += async (_, _) => await StartLongPressDragAsync();
        }

        return _longPressTimer;
    }

    /// <summary>（重新）排一次定时器 —— 早到时按剩余毫秒再来一次。</summary>
    private void RestartLongPressTimer(int delayMs)
    {
        var timer = EnsureLongPressTimer();
        timer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs));
        timer.Start();
    }

    private async Task StartLongPressDragAsync()
    {
        var now = Environment.TickCount64;

        if (!_longPress.Tick(now))
        {
            if (_longPress.IsPressed)
            {
                RestartLongPressTimer(_longPress.RemainingMs(now));   // 早到 ⇒ 重排再试
            }

            return;
        }

        var container = _pressedContainer;
        var point = _pressedPoint;
        if (container is null || point is null)
        {
            return;
        }

        try
        {
            // StartDragAsync 要求 CanDrag=true：起拖前临时打开，起拖结束再关回去
            // （这样"按下就移动"永远起不了拖 —— 只有长按才算数）
            container.CanDrag = true;
            _suppressNextClick = true;      // 长按后系统补的那次 ItemClick 不能当"打开"
            // ⚠️ 先让 StartDragAsync 抓取"跟着鼠标的那份视觉"，**再**压暗原件：
            //    反过来的话，被抓走的图标会带着 0.35 的不透明度，看着像根本没浮起。
            DispatcherQueue.TryEnqueue(() => ApplyLift(container, lifted: true));

            await container.StartDragAsync(point);
            ApplyLift(container, lifted: true);   // 系统拖拽视觉 = 图块快照，跟着鼠标走
        }
        catch (Exception ex)
        {
            App.WriteCrash("GridPage.StartLongPressDragAsync", ex);
        }
        finally
        {
            container.CanDrag = false;
            ApplyLift(container, lifted: false);
            HideDropIndicator();
            _longPress.Release();
            ResetPressedTile();
        }
    }

    /// <summary>
    /// "浮起"反馈（手机桌面那种"拎起来"）：原件轻微放大 + 半透明占位，跟着鼠标走的是拖拽视觉。
    /// ⚠️ 只用**合成变换**（Scale / Opacity），不碰布局 —— 不会把相邻图块挤走。
    /// </summary>
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

    private void ResetPressedTile()
    {
        _pressedContainer = null;
        _pressedPoint = null;
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

    /// <summary>把"拖的是谁、从哪一页拖的"写进 DataPackage。</summary>
    private void OnTileDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var tile = FindTileFromArgs(args);
        if (tile is null)
        {
            args.Cancel = true;
            return;
        }

        var payload = new DragPayload(DragItemKind.Icon, _tab.Id, tile.Model.Id);
        args.Data.SetText(payload.ToString());
        args.Data.RequestedOperation = DataPackageOperation.Move;

        // 实时让位的现场记录：这次拖动开始时界面的顺序 + 被拖的是谁（拖动取消时要还原）
        _dragOrderSnapshot = _tab.Icons.Select(t => t.Model.Id).ToList();
        _draggingId = tile.Model.Id;
        _livePreviewed = false;
        _dropHandled = false;
    }

    /// <summary>
    /// 只接受两类拖放：
    ///   ① **本应用内部的图标重排**（文本载荷，见 <see cref="DragPayload"/>）；
    ///   ② **从资源管理器拖进来的文件/文件夹/快捷方式**（StorageItems）—— 建新图标（原版语义）。
    /// 其余一律拒绝（例如"图标拖到列表页"）。拖动中顺便算出落点并画指示线（只对内部重排有意义）。
    /// </summary>
    private void OnTileDragOver(object sender, DragEventArgs e)
    {
        if (TryReadPayload(e, out _))
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.IsCaptionVisible = false;

            var insertIndex = ComputeInsertIndex(e);

            // 优先"实时让位"（手机那种插入效果）；让不了位（跨页拖过来）才退回画一条指示线
            if (!TryLivePreview(insertIndex))
            {
                ShowDropIndicator(insertIndex);
            }

            return;
        }

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
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
        // ① 本应用内部的重排/跨页移动（有自己的文本载荷）
        if (TryReadPayload(e, out var payload))
        {
            var insertIndex = ComputeInsertIndex(e);
            HideDropIndicator();
            _dropHandled = true;

            if (TryLivePreview(insertIndex))
            {
                // 已经实时让好位了 ⇒ 界面顺序就是最终顺序，直接写下去
                // （不能再按落点索引算一次：界面顺序已经变了，索引会对不上）
                OrderCommitted?.Invoke(this, _tab.Icons.Select(t => t.Model.Id).ToList());
                return;
            }

            ItemDropped?.Invoke(this, new DragDropRequest(payload, _tab.Id, insertIndex));
            return;
        }

        // ② 从资源管理器拖入的文件/文件夹/快捷方式
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
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
    /// 拖动结束（含取消）：收掉指示线；**若这次拖动做了实时让位却没落下（Esc / 丢到窗口外），
    /// 把界面顺序还原**（否则界面与 Core 就不一致了 —— Core 才是唯一事实源）。
    /// </summary>
    private void OnTileDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        HideDropIndicator();

        if (!_dropHandled && _livePreviewed)
        {
            RevertLivePreview();
        }

        _dragOrderSnapshot = null;
        _draggingId = null;
        _livePreviewed = false;
        _dropHandled = false;
    }

    // ────────────────────────────── 实时让位（"插入效果"，2026-09-14）──────────────────────────────
    //
    // 手机桌面的手感：拖动时**其它图块让开、露出插入空位**。
    // 做法：把被拖的图块在界面集合里实时 `Move` 到落点 —— GridView 会用内置的重排动画把其它图块推过去。
    // ⚠️ 索引口径与 Core 完全一致：落点索引是"插到**当前**第 N 项之前"，往后移时要减去自己那一格
    //    （Core 的 ApplySameTabDrop 也在做同一件事，两边必须同口径）。
    // ⚠️ 界面顺序一旦实时变了，落库就不能再按"落点索引"算 —— 直接把这个顺序交给
    //    `DataStore.ApplyIconOrder` 写下去（否则会闪一下又弹回去）。

    private List<string>? _dragOrderSnapshot;   // 拖动开始时的界面顺序（取消时还原）
    private string? _draggingId;                // 正在拖的图块 id（本页集合里找得到才做让位）
    private bool _livePreviewed;                // 本次拖动做过让位
    private bool _dropHandled;                  // 落下是否已处理（决定要不要还原）

    /// <summary>
    /// 把被拖的图块实时挪到落点，让其它图块让开。
    /// </summary>
    /// <returns>
    /// true = 本次能做实时让位（调用方不要画指示线）；false = 做不了（例如跨页拖过来，
    /// 被拖项不在本页集合里）⇒ 调用方退回"画一条落点指示线"。
    /// </returns>
    private bool TryLivePreview(int insertIndex)
    {
        if (_draggingId is null)
        {
            return false;
        }

        int from = -1;
        for (int i = 0; i < _tab.Icons.Count; i++)
        {
            if (_tab.Icons[i].Model.Id == _draggingId)
            {
                from = i;
                break;
            }
        }

        if (from < 0)
        {
            return false;   // 不是本页的图块（跨页拖过来）：让不了位
        }

        var to = insertIndex > from ? insertIndex - 1 : insertIndex;
        to = Math.Clamp(to, 0, Math.Max(0, _tab.Icons.Count - 1));

        if (to != from)
        {
            _tab.Icons.Move(from, to);
            _livePreviewed = true;
        }

        HideDropIndicator();   // 有让位就不需要那条线了
        return true;
    }

    /// <summary>拖动被取消：按拖动开始时的快照把界面顺序挪回去。</summary>
    private void RevertLivePreview()    {
        if (_dragOrderSnapshot is null)
        {
            return;
        }

        for (int target = 0; target < _dragOrderSnapshot.Count; target++)
        {
            var id = _dragOrderSnapshot[target];

            int current = -1;
            for (int i = 0; i < _tab.Icons.Count; i++)
            {
                if (_tab.Icons[i].Model.Id == id)
                {
                    current = i;
                    break;
                }
            }

            if (current >= 0 && current != target)
            {
                _tab.Icons.Move(current, target);
            }
        }
    }

    /// <summary>读取拖放载荷；不是本页该收的类型就返回 false（例如"图标拖到列表页"）。</summary>
    private bool TryReadPayload(DragEventArgs e, out DragPayload payload)
    {
        payload = null!;

        try
        {
            if (!e.DataView.Contains(StandardDataFormats.Text))
            {
                return false;
            }

            var text = e.DataView.GetTextAsync().AsTask().GetAwaiter().GetResult();
            var parsed = DragPayload.TryParse(text);
            if (parsed is null || parsed.Kind != _tab.DraggableKind)
            {
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (Exception ex)
        {
            App.WriteCrash("GridPage.TryReadPayload", ex);
            return false;
        }
    }

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

        Canvas.SetLeft(DropIndicator, x);
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