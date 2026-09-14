// ListViewPage.xaml.cs —— 列表页
//
// 职责：铺出该标签页的列表项、点击打开、入场动效、**拖拽排序（W3）**。
// 行内编辑、右键菜单属于 W5。
//
// 拖拽与网格页共用同一套设计（拖起塞载荷 → DragOver 算落点 → Drop 交给宿主落库），
// 差别只有两处：
//   ① 载荷类型是 ListItem（因此"列表项拖到网格页"会被目标页直接拒收）；
//   ② 插入位置是**行与行之间的横线**，不是竖线。
// 落点判定仍然用 Core 的 DropIndexCalculator（单列布局它能自动只按 Y 判）。

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

        // 只为"记下用户按在哪一行上"（无定时器、无门控）—— 见 GridPage 同名注释。
        Rows.AddHandler(PointerPressedEvent, new PointerEventHandler(OnRowPressedForDrag), handledEventsToo: true);
    }

    /// <summary>点了一行 —— 把该行的路径交给宿主窗口打开。</summary>
    public event EventHandler<ListItemModel>? ItemActivated;

    /// <summary>拖放落下 —— 交给宿主窗口落库。</summary>
    public event EventHandler<DragDropRequest>? ItemDropped;

    /// <summary>做过"实时让位"的拖动落下时：界面上的最终顺序交给宿主窗口落库（与网格页同款分工）。</summary>
    public event EventHandler<IReadOnlyList<string>>? OrderCommitted;

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
    private string? _pressedItemId;
    private int _lastTracedIndex = -1;

    /// <summary>拖动链路诊断（写 %TEMP%\toolboxpanel-probe.log）。</summary>
    private static void DragTrace(string message)
        => App.ProbeLog($"[拖动 {DateTime.Now:HH:mm:ss.fff}] {message}");

    private void OnRowPressedForDrag(object sender, PointerRoutedEventArgs e)
    {
        var row = (e.OriginalSource as FrameworkElement)?.DataContext as ListRowViewModel;
        var item = row ?? FindRowById(FindRowIdFromSource(e.OriginalSource));
        _pressedItemId = item?.Model.Id;
        DragTrace($"按下：{item?.Description ?? "(空白处)"}");
    }

    private ListRowViewModel? FindRowById(string? itemId)
        => itemId is null ? null : _tab.ListItems.FirstOrDefault(r => r.Model.Id == itemId);

    private static string? FindRowIdFromSource(object? source)
    {
        var container = FindContainerFromSource(source);
        return (container?.DataContext as ListRowViewModel)?.Model.Id;
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

    private void BeginLiftForDrag(DragStartingEventArgs args)
    {
        if (FindContainerFromSource(args.OriginalSource) is { } container)
        {
            _liftedContainer = container;
            DispatcherQueue.TryEnqueue(() => ApplyLift(container, lifted: true));
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

    /// <summary>从事件源往上找到承载这一行的 ListViewItem（找不到返回 null）。</summary>
    private static ListViewItem? FindContainerFromSource(object? source)
    {
        var current = source as DependencyObject;

        while (current is not null)
        {
            if (current is ListViewItem item)
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

    // ────────────────────────────── 拖拽排序 ──────────────────────────────

    private void OnRowDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        // ⚠️ 双保险解析：事件源 → 退回"按下时记下的那一行"（否则会把整次拖动取消掉）
        var fromArgs = (args.OriginalSource as FrameworkElement)?.DataContext as ListRowViewModel;
        var row = fromArgs ?? FindRowById(_pressedItemId) ?? FindRowById(FindRowIdFromSource(args.OriginalSource));

        DragTrace($"DragStarting：事件源={args.OriginalSource?.GetType().Name} "
                  + $"事件源解析={fromArgs?.Description ?? "null"} 按下记录={FindRowById(_pressedItemId)?.Description ?? "null"}");

        if (row is null)
        {
            args.Cancel = true;
            DragTrace("→ 解析不到被拖项，取消这次拖动");
            return;
        }

        var payload = new DragPayload(DragItemKind.ListItem, _tab.Id, row.Model.Id);
        args.Data.SetText(payload.ToString());
        args.Data.RequestedOperation = DataPackageOperation.Move;

        _suppressNextClick = true;
        BeginLiftForDrag(args);

        _dragOrderSnapshot = _tab.ListItems.Select(r => r.Model.Id).ToList();
        _draggingId = row.Model.Id;
        _livePreviewed = false;
        _dropHandled = false;
    }

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        if (!TryReadPayload(e, out _))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            HideDropIndicator();
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;

        var insertIndex = ComputeInsertIndex(e);

        if (insertIndex != _lastTracedIndex)
        {
            _lastTracedIndex = insertIndex;
            DragTrace($"DragOver：落点索引={insertIndex}");
        }

        // 优先实时让位（行往下/上让开）；让不了位（跨页拖过来）才画指示线
        if (!TryLivePreview(insertIndex))
        {
            ShowDropIndicator(insertIndex);
        }
    }

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        var insertIndex = ComputeInsertIndex(e);
        HideDropIndicator();

        if (!TryReadPayload(e, out var payload))
        {
            return;
        }

        _dropHandled = true;
        DragTrace($"Drop：落点={insertIndex} 让位过={_livePreviewed}");

        if (TryLivePreview(insertIndex))
        {
            // 界面顺序已是最终顺序 ⇒ 直接写下去（与网格页同口径）
            OrderCommitted?.Invoke(this, _tab.ListItems.Select(r => r.Model.Id).ToList());
            return;
        }

        ItemDropped?.Invoke(this, new DragDropRequest(payload, _tab.Id, insertIndex));
    }

    /// <summary>拖动结束（含取消）：做过让位却没落下 ⇒ 还原界面顺序（Core 才是唯一事实源）。</summary>
    private void OnRowDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        HideDropIndicator();
        EndLiftForDrag();

        var dropped = args.DropResult == DataPackageOperation.Move;
        DragTrace($"DragItemsCompleted：DropResult={args.DropResult} 已处理过={_dropHandled} 让位过={_livePreviewed}");

        // ⚠️ Drop 与 DragItemsCompleted 的先后顺序不保证 ⇒ 结算延后一拍（同网格页）
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_dropHandled)
            {
                DragTrace("结算：Drop 已处理（顺序已落库）");
            }
            else if (_livePreviewed && dropped)
            {
                DragTrace("结算：Drop 没来但 DropResult=Move ⇒ 按界面顺序补落库");
                OrderCommitted?.Invoke(this, _tab.ListItems.Select(r => r.Model.Id).ToList());
            }
            else if (_livePreviewed)
            {
                DragTrace("结算：拖动被取消 ⇒ 还原界面顺序");
                RevertLivePreview();
            }

            _dragOrderSnapshot = null;
            _draggingId = null;
            _livePreviewed = false;
            _dropHandled = false;
            _lastTracedIndex = -1;
        });
    }

    // ────────────────────────────── 实时让位（"插入效果"）──────────────────────────────
    //
    // 与网格页同一套：把被拖的行实时 `Move` 到落点，ListView 用内置重排动画让其它行让开。
    // ⚠️ 索引口径与 Core 一致（往后移减去自己那一格）；⚠️ 界面顺序变了之后落库必须走"按顺序落库"。

    private List<string>? _dragOrderSnapshot;
    private string? _draggingId;
    private bool _livePreviewed;
    private bool _dropHandled;

    /// <summary>true = 做得了实时让位（不要画指示线）；false = 做不了（跨页拖过来）。</summary>
    private bool TryLivePreview(int insertIndex)
    {
        if (_draggingId is null)
        {
            return false;
        }

        int from = -1;
        for (int i = 0; i < _tab.ListItems.Count; i++)
        {
            if (_tab.ListItems[i].Model.Id == _draggingId)
            {
                from = i;
                break;
            }
        }

        if (from < 0)
        {
            return false;
        }

        var to = insertIndex > from ? insertIndex - 1 : insertIndex;
        to = Math.Clamp(to, 0, Math.Max(0, _tab.ListItems.Count - 1));

        if (to != from)
        {
            _tab.ListItems.Move(from, to);
            _livePreviewed = true;
        }

        HideDropIndicator();
        return true;
    }

    private void RevertLivePreview()
    {
        if (_dragOrderSnapshot is null)
        {
            return;
        }

        for (int target = 0; target < _dragOrderSnapshot.Count; target++)
        {
            var id = _dragOrderSnapshot[target];

            int current = -1;
            for (int i = 0; i < _tab.ListItems.Count; i++)
            {
                if (_tab.ListItems[i].Model.Id == id)
                {
                    current = i;
                    break;
                }
            }

            if (current >= 0 && current != target)
            {
                _tab.ListItems.Move(current, target);
            }
        }
    }

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
            App.WriteCrash("ListViewPage.TryReadPayload", ex);
            return false;
        }
    }

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

        // 横线要横跨整行宽度（不能只画在某一行的左边界那么宽）
        double width = 0;
        foreach (var bound in bounds)
        {
            width = Math.Max(width, bound.X + bound.Width);
        }

        Canvas.SetLeft(DropIndicator, x);
        Canvas.SetTop(DropIndicator, y);
        DropIndicator.Width = Math.Max(24, width - x);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDropIndicator() => DropIndicator.Visibility = Visibility.Collapsed;
}