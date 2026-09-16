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

        // ⚠️⚠️ 同网格页：起拖事件收不到（`ListViewBase` 内部处理并标记 handled，
        //   WinUI 3 又不暴露 RoutedEvent ⇒ AddHandler 那条路编译不过）。
        //   载荷改走 DragSession，在**按下**时记下"拖谁" —— 指针事件能收到 handledEventsToo。
        Rows.AddHandler(
            PointerPressedEvent,
            new PointerEventHandler(OnRowPointerPressed),
            handledEventsToo: true);

        Rows.AddHandler(
            PointerReleasedEvent,
            new PointerEventHandler(OnRowPointerReleased),
            handledEventsToo: true);
    }

    /// <summary>点了一行 —— 把该行的路径交给宿主窗口打开。</summary>
    public event EventHandler<ListItemModel>? ItemActivated;

    /// <summary>拖放落下 —— 交给宿主窗口落库。</summary>
    public event EventHandler<DragDropRequest>? ItemDropped;

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

        // 单纯点击 = 没拖动 ⇒ 顺手收掉拖动会话
        DragSession.End();

        if (e.ClickedItem is ListRowViewModel row)
        {
            ItemActivated?.Invoke(this, row.Model);
        }
    }

    // ────────────────────────────── 拖动反馈（"浮起"占位）──────────────────────────────
    //
    // 起拖交给**系统的原生拖拽**：按下拖动即起拖（同网格页）—— 用户明确要求取消长按门控。
    // 这里只负责：拖动开始时把行"浮起"（轻微放大 + 半透明占位），以及抑制起拖后系统补的那次 ItemClick。
    // ⚠️ 压暗要在拖拽视觉**抓取之后**（TryEnqueue 排一下）。

    private bool _suppressNextClick;
    private FrameworkElement? _liftedContainer;
    private ListRowViewModel? _pressedRow;
    private bool _dragStarted;
    private int _lastTracedIndex = -1;
    private int _lastDropIndex = -1;
    private bool _tracedDragOverEntry;

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

    private void ApplyLift(FrameworkElement container, bool lifted)
    {
        try
        {
            container.CenterPoint = new Vector3(
                (float)(container.ActualWidth / 2), (float)(container.ActualHeight / 2), 0f);
            container.Scale = lifted ? new Vector3(1.02f, 1.06f, 1f) : new Vector3(1f, 1f, 1f);
            container.Opacity = lifted ? 0.35 : 1.0;
        }
        catch (Exception ex)
        {
            App.WriteCrash("ListViewPage.ApplyLift", ex);
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

    // ────────────────────────────── 拖拽排序 ──────────────────────────────
    //
    // 载荷不走 DataPackage，走 DragSession（同网格页，见 DragSession.cs 与 ERROR.md E25）：
    //   · 按下 → 记下"拖的是哪一行"并 Begin 会话；
    //   · DragOver/Drop → 读会话判断，再算落点、画插入横条；
    //   · DragItemsCompleted / Drop / 单纯点击松手 → End 会话。

    private void OnRowPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pressedRow = (e.OriginalSource as FrameworkElement)?.DataContext as ListRowViewModel;

        if (_pressedRow is null)
        {
            DragSession.End();
            return;
        }

        DragSession.Begin(new DragPayload(DragItemKind.ListItem, _tab.Id, _pressedRow.Model.Id));
        DragTrace($"按下：{_pressedRow.Description}（会话已开）");
    }

    private void OnRowPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragStarted)
        {
            DragSession.End();
        }
    }

    /// <summary>拖动真的开始了（第一次 DragOver 才发现 —— 起拖事件收不到）。</summary>
    private void EnsureLiftStarted()
    {
        if (_dragStarted)
        {
            return;
        }

        _dragStarted = true;
        _suppressNextClick = true;   // 起拖之后系统补的那次 ItemClick 不能当"打开"

        if (_pressedRow is not null && Rows.ContainerFromItem(_pressedRow) is FrameworkElement container)
        {
            _liftedContainer = container;
            DispatcherQueue.TryEnqueue(() => ApplyLift(container, lifted: true));
        }
    }

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        var (hasText, text, hasStorage) = DescribeData(e);

        if (!_tracedDragOverEntry)
        {
            _tracedDragOverEntry = true;
            DragTrace($"DragOver 首次：含文本={hasText} 文本=\"{text}\" 含StorageItems={hasStorage} "
                      + $"会话={(DragSession.Current is null ? "无" : DragSession.Current.ItemId)}");
        }

        var payload = DragSession.Take(_tab.DraggableKind);
        if (payload is null || hasStorage)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            HideDropIndicator();
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;

        EnsureLiftStarted();

        var insertIndex = ComputeInsertIndex(e);
        _lastDropIndex = insertIndex;

        if (insertIndex != _lastTracedIndex)
        {
            _lastTracedIndex = insertIndex;
            DragTrace($"DragOver：落点索引={insertIndex}");
        }

        // 拖动中**不移动任何行**（用户 2026-09-15："只加竖条，不让位"），
        // 只在行与行之间画一根插入横条表示"松手会插到这里"。
        ShowDropIndicator(insertIndex);
    }

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        var (hasText, text, hasStorage) = DescribeData(e);
        DragTrace($"Drop：含文本={hasText} 文本=\"{text}\" 含StorageItems={hasStorage} "
                  + $"会话={(DragSession.Current is null ? "无" : DragSession.Current.ItemId)}");

        var payload = DragSession.Take(_tab.DraggableKind);
        if (payload is null || hasStorage)
        {
            HideDropIndicator();
            return;
        }

        DragSession.End();

        var insertIndex = ComputeInsertIndex(e);
        HideDropIndicator();
        DragTrace($"Drop（内部重排）：落点={insertIndex}");

        ItemDropped?.Invoke(this, new DragDropRequest(payload, _tab.Id, insertIndex));
    }

    /// <summary>
    /// 拖动结束（含取消）：收掉插入横条、复位"浮起"。
    /// 拖动期间没有移动任何行，所以**不需要**做任何"还原"或"补落库"。
    /// </summary>
    private void OnRowDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        HideDropIndicator();
        EndLiftForDrag();
        _lastTracedIndex = -1;
        _dragStarted = false;
        _pressedRow = null;

        // ⚠️ 兜底：Drop 事件不一定来；会话还在 + DropResult=Move ⇒ 按最后落点补落库（同网格页）。
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