// ListViewPage.xaml.cs —— 列表页
//
// 职责：铺出该标签页的列表项、点击打开、入场动效、**拖拽排序（W3）**。
// 行内编辑、右键菜单属于 W5。
//
// 拖拽与网格页共用同一套设计（拖起塞载荷 → DragOver 算落点 → Drop 交给宿主落库），
// 差别只有两处：
//   ① 载荷类型是 ListItem（因此"列表项拖到网格页"会被目标页直接拒收）；
//   ② 插入位置是**行与行之间的横条**，不是竖条。
// 落点判定仍然用 Core 的 DropIndexCalculator（单列布局它能自动只按 Y 判）。
//
// ⚠️ 与网格页同步（2026-09-15）：**拖动过程中不移动任何行**，只显示插入横条，
//    松手才真正插入（用户明确要求"只加竖条，不让位"；那套实时让位/取消还原已整体删除）。

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

namespace ToolboxPanel.Views;

public sealed partial class ListViewPage : UserControl, IAnimatedPage
{
    private readonly TabItemViewModel _tab;
    private readonly EntranceAnimator _entrance;

    public ListViewPage(TabItemViewModel tab)
    {
        _tab = tab;

        InitializeComponent();

        Rows.ItemsSource = tab.ListItems;
        EmptyHint.Visibility = tab.ListItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _entrance = new EntranceAnimator(Rows);

        // ⚠️ 不再挂 PointerPressed / DragStarting / DragItemsStarting —— 实测**全都收不到**
        //    （ERROR.md E25）。拖动链路只靠：目标端 DragOver（登记落点）+ 源端 DragItemsCompleted
        //    （带着 args.Items 与 DropResult 来收口）。
    }

    /// <summary>点了一行 —— 把该行的路径交给宿主窗口打开。</summary>
    public event EventHandler<ListItemModel>? ItemActivated;

    /// <summary>拖放落下 —— 交给宿主窗口落库。</summary>
    public event EventHandler<DragDropRequest>? ItemDropped;

    /// <summary>行的右键菜单选了某一项（页面不碰数据、不开对话框，同网格页的分工）。</summary>
    public event EventHandler<ListItemMenuRequest>? ItemMenuActionRequested;

    /// <summary>在空白处右键 = 新建列表项（原版语义：**直接弹对话框**，不经过菜单）。</summary>
    public event EventHandler? NewListItemRequested;

    // ────────────────────────────── 行右键菜单（W5）──────────────────────────────
    //
    // 菜单规格（顺序 / 分隔线 / 文案）在 Core 的 `ListItemContextMenu`（有单测）；
    // 这里只负责按规格铺控件 + 把动作转成事件。
    // ⚠️ 原版行菜单**没有「打开」**（点行/双击就是打开），别"顺手"加。

    private void OnRowContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        var position = args.TryGetPosition(Rows, out var point) ? point : new Windows.Foundation.Point(0, 0);
        var row = FindRowFromSource(args.OriginalSource);

        if (row is null)
        {
            NewListItemRequested?.Invoke(this, EventArgs.Empty);   // 空白处 = 新建列表项
            args.Handled = true;
            return;
        }

        var menu = new MenuFlyout();

        foreach (var item in ListItemContextMenu.Build())
        {
            if (item.SeparatorBefore)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            var action = item.Action;
            var menuItem = new MenuFlyoutItem { Text = item.Label };
            menuItem.Click += (_, _) => ItemMenuActionRequested?.Invoke(this, new ListItemMenuRequest(row.Model, action));
            menu.Items.Add(menuItem);
        }

        menu.ShowAt(Rows, position);
        args.Handled = true;
    }

    /// <summary>右键点在哪 —— 往上找到承载这一行的 ListViewItem（空白处返回 null）。</summary>
    private static ListRowViewModel? FindRowFromSource(object? source)
    {
        var current = source as DependencyObject;

        while (current is not null)
        {
            if (current is ListViewItem { DataContext: ListRowViewModel row })
            {
                return row;
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

    /// <summary>演示模式下不写盘：由宿主窗口置为 false 关掉拖拽。</summary>
    public bool DragDropEnabled
    {
        get => _dragDropEnabled;
        set
        {
            _dragDropEnabled = value;

            // ⚠️ 同网格页：不设 Rows.CanDrag（那会让系统自己起拖，绕过长按门控）
            Rows.AllowDrop = value;
        }
    }

    private bool _dragDropEnabled;

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

    private void OnRowClick(object sender, ItemClickEventArgs e)
    {
        // 长按（起拖）之后系统偶尔还会补一次 ItemClick —— 那次不能当成"打开"
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            return;
        }

        // 单纯点击 = 没拖动 ⇒ 顺手清掉落点登记
        DragSession.ClearTarget();

        if (e.ClickedItem is ListRowViewModel row)
        {
            ItemActivated?.Invoke(this, row.Model);
        }
    }

    // ────────────────────────────── 拖动链路（2026-09-16 最终形态，与网格页同一套）──────────────────────────────
    //
    // 收不到任何起拖/指针事件（ERROR.md E25）⇒ 只靠两个实测可靠的事件：
    //   目标端 `DragOver` 登记落点 + 源端 `DragItemsCompleted`（带 args.Items / DropResult）收口。

    private bool _suppressNextClick;
    private int _lastTracedIndex = -1;
    private int _lastDropIndex = -1;
    private bool _tracedDragOverEntry;
    private bool _dropSeen;                    // 本次拖动是否真的落在本页（Drop 事件到场）

    /// <summary>拖动链路诊断（写 %TEMP%\toolboxpanel-probe.log）。</summary>
    private static void DragTrace(string message)
        => App.ProbeLog($"[拖动 {DateTime.Now:HH:mm:ss.fff}] {message}");

    /// <summary>这次拖放带了什么（诊断用；读不到就当作没有）—— 与网格页同款。</summary>
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
            App.WriteCrash("ListViewPage.DescribeData/text", ex);
        }

        try
        {
            hasStorage = e.DataView.Contains(StandardDataFormats.StorageItems);
        }
        catch (Exception ex)
        {
            App.WriteCrash("ListViewPage.DescribeData/storage", ex);
        }

        return (hasText, text, hasStorage);
    }

    // ────────────────────────────── 拖拽排序（2026-09-16 最终形态）──────────────────────────────
    //
    // 与网格页同一套（ERROR.md E25）：DragOver 登记落点 → DragItemsCompleted 收口落库。
    // DataPackage 永远是空的 ⇒ 这就是"本应用内部拖动"的判据。

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        var (hasText, text, hasStorage) = DescribeData(e);

        if (!_tracedDragOverEntry)
        {
            _tracedDragOverEntry = true;
            DragTrace($"DragOver 首次：含文本={hasText} 文本=\"{text}\" 含StorageItems={hasStorage} "
                      + $"判定={(DragSession.LooksLikeInternalDrag(hasText, hasStorage) ? "内部拖动" : "非内部")}");
        }

        if (!DragSession.LooksLikeInternalDrag(hasText, hasStorage))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            HideDropIndicator();
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;
        _suppressNextClick = true;

        var insertIndex = ComputeInsertIndex(e);
        _lastDropIndex = insertIndex;
        DragSession.ReportTarget(_tab.Id, DragItemKind.ListItem, insertIndex);

        if (insertIndex != _lastTracedIndex)
        {
            _lastTracedIndex = insertIndex;
            DragTrace($"DragOver：落点索引={insertIndex}（已登记）");
        }

        // 拖动中**不移动任何行**（用户 2026-09-15："只加竖条，不让位"），
        // 只在行与行之间画一根插入横条表示"松手会插到这里"。
        ShowDropIndicator(insertIndex);
    }

    /// <summary>内部拖动不在 Drop 里做动作（Drop 不一定来、且不带"拖的是谁"）—— 统一留给 DragItemsCompleted。</summary>
    private void OnRowDrop(object sender, DragEventArgs e)
    {
        var (hasText, text, hasStorage) = DescribeData(e);
        DragTrace($"Drop：含文本={hasText} 文本=\"{text}\" 含StorageItems={hasStorage}");

        if (DragSession.LooksLikeInternalDrag(hasText, hasStorage))
        {
            _dropSeen = true;
            _lastDropIndex = ComputeInsertIndex(e);
            DragSession.ReportTarget(_tab.Id, DragItemKind.ListItem, _lastDropIndex);
            HideDropIndicator();
            DragTrace($"Drop（内部拖动）：落点={_lastDropIndex} ⇒ 留给 DragItemsCompleted 收口");
            return;
        }

        HideDropIndicator();
    }

    /// <summary>
    /// ★ **一次内部拖动的收口点**（源端事件，实测可靠）：`args.Items` 给出被拖的行本身，
    /// `args.DropResult` 给出落没落下，再加上目标端登记的落点 ⇒ 完整的一次重排/跨页移动。
    /// </summary>
    private void OnRowDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        HideDropIndicator();
        _lastTracedIndex = -1;
        _suppressNextClick = false;

        var target = DragSession.TakeTarget();
        var row = args.Items.Count > 0 ? args.Items[0] as ListRowViewModel : null;
        var dropResult = args.DropResult;
        var dropSeen = _dropSeen;
        _dropSeen = false;
        _lastDropIndex = -1;

        DragTrace($"DragItemsCompleted：DropResult={dropResult} items={args.Items.Count} "
                  + $"被拖={row?.Description ?? "null"} 登记落点={(target is null ? "无" : $"{target.Value.TabId}#{target.Value.InsertIndex}")} "
                  + $"Drop到过本页={dropSeen}");

        if (row is null || target is null)
        {
            return;
        }

        if (target.Value.Kind != DragItemKind.ListItem)
        {
            DragTrace("→ 落点是网格页，列表项不进网格页 ⇒ 忽略");
            return;
        }

        // ⚠️ 结算延后一拍（Drop 与 DragItemsCompleted 先后顺序不保证，同网格页）
        DispatcherQueue.TryEnqueue(() =>
        {
            var landed = dropResult == DataPackageOperation.Move || dropSeen || _dropSeen;
            _dropSeen = false;

            if (!landed)
            {
                DragTrace("→ 判定：拖动被取消 ⇒ 不落库");
                return;
            }

            var payload = new DragPayload(DragItemKind.ListItem, _tab.Id, row.Model.Id);
            DragTrace($"→ 落库：{row.Description} → 页 {target.Value.TabId} 第 {target.Value.InsertIndex} 位");
            ItemDropped?.Invoke(this, new DragDropRequest(payload, target.Value.TabId, target.Value.InsertIndex));
        });
    }

    // ────────────────────────────── 插入横条（2026-09-15 起，唯一一种拖拽反馈）──────────────────────────────
    //
    // 用户 2026-09-15 明确要求："只加竖条，不让位"（网格页是竖条，列表页对应为行间横条）——
    // 曾经的"实时让位 + 取消还原"已整体删除：
    //   · 拖动期间：集合一动不动，只在落点处画一根强调色横条；
    //   · 松手：走 ItemDropped → MainViewModel.ApplyDrop → DataStore.ApplyDragDrop 落库（含跨页）。

    /// <summary>把落点（相对本页的坐标）交给 Core 的几何计算，得到"插到第几个"。</summary>
    private int ComputeInsertIndex(DragEventArgs e)
    {
        var bounds = CollectRowBounds();
        if (bounds.Count == 0)
        {
            return 0;
        }

        var position = e.GetPosition(this);
        return DropIndexCalculator.Compute(bounds, position.X, position.Y);
    }

    /// <summary>已实现行的矩形（相对本页，DIP），顺序 = 从上到下。</summary>
    private List<ItemBounds> CollectRowBounds()
    {
        var result = new List<ItemBounds>(_tab.ListItems.Count);

        for (int i = 0; i < _tab.ListItems.Count; i++)
        {
            if (Rows.ContainerFromIndex(i) is not FrameworkElement container)
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
                App.WriteCrash("ListViewPage.CollectRowBounds", ex);
            }
        }

        return result;
    }

    private void ShowDropIndicator(int insertIndex)
    {
        var bounds = CollectRowBounds();
        if (bounds.Count == 0)
        {
            HideDropIndicator();
            return;
        }

        var (x, y, _) = DropIndexCalculator.IndicatorAt(bounds, insertIndex);

        // 横条要横跨整行宽度（不能只画在某一行的左边界那么宽）
        double width = 0;
        foreach (var bound in bounds)
        {
            width = Math.Max(width, bound.X + bound.Width);
        }

        Canvas.SetLeft(DropIndicator, x);
        // ⚠️ 横条要**骑在行边界上**（上移半个条高），看起来才是"插在两行之间"。
        Canvas.SetTop(DropIndicator, y - DropIndicator.Height / 2);
        DropIndicator.Width = Math.Max(24, width - x);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDropIndicator() => DropIndicator.Visibility = Visibility.Collapsed;
}

/// <summary>一次列表行菜单动作请求（页面 → 宿主窗口）。与图块的 <c>IconMenuRequest</c> 同一套写法。</summary>
public sealed record ListItemMenuRequest(ListItemModel Item, ListItemMenuAction Action);