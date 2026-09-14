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

        // 长按起拖要用**指针事件**；ListViewBase 自己也会处理这些事件，
        // 所以必须 handledEventsToo: true —— 否则我们根本收不到（内部已经把按下吃掉了）。
        Rows.AddHandler(PointerPressedEvent, new PointerEventHandler(OnRowPointerPressed), handledEventsToo: true);
        Rows.AddHandler(PointerMovedEvent, new PointerEventHandler(OnRowPointerMoved), handledEventsToo: true);
        Rows.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnRowPointerReleased), handledEventsToo: true);
        Rows.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnRowPointerAborted), handledEventsToo: true);
        Rows.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnRowPointerAborted), handledEventsToo: true);
    }

    /// <summary>点了一行 —— 把该行的路径交给宿主窗口打开。</summary>
    public event EventHandler<ListItemModel>? ItemActivated;

    /// <summary>拖放落下 —— 交给宿主窗口落库。</summary>
    public event EventHandler<DragDropRequest>? ItemDropped;

    /// <summary>演示模式下不写盘：由宿主窗口置为 false 关掉拖拽。</summary>
    public bool DragDropEnabled
    {
        get => Rows.CanDrag;
        set
        {
            Rows.CanDrag = value;
            Rows.AllowDrop = value;
        }
    }

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

    // ────────────────────────────── 长按起拖（与网格页同一套，2026-09-14）──────────────────────────────
    //
    // 判定逻辑在 Core 的 `LongPressGesture`（有单测）：按下 → 350ms 到点且没移动 ⇒ 起拖；
    // 起拖后**原地松手** ⇒ 这一行没有菜单，直接不做事（列表页本轮只有"打开"语义）；
    // 阈值前移动超容差 ⇒ 手势作废。

    private readonly LongPressGesture _longPress = new();

    private DispatcherQueueTimer? _longPressTimer;
    private ListViewItem? _pressedContainer;
    private Microsoft.UI.Input.PointerPoint? _pressedPoint;
    private bool _suppressNextClick;

    private void OnRowPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _suppressNextClick = false;

        if (!DragDropEnabled)
        {
            return;
        }

        var point = e.GetCurrentPoint(Rows);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var container = FindContainerFromSource(e.OriginalSource);
        if (container is null)
        {
            return;                     // 空白处按下：不参与长按
        }

        _pressedContainer = container;
        _pressedPoint = point;
        _longPress.Press(point.Position.X, point.Position.Y, Environment.TickCount64);
        EnsureLongPressTimer().Start();
    }

    private void OnRowPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_longPress.IsPressed)
        {
            return;
        }

        var position = e.GetCurrentPoint(Rows).Position;
        if (_longPress.Move(position.X, position.Y))
        {
            CancelLongPress();
        }
    }

    private void OnRowPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _longPressTimer?.Stop();

        if (!_longPress.HasDragStarted)
        {
            _longPress.Reset();
            ResetPressedRow();
        }
    }

    private void OnRowPointerAborted(object sender, PointerRoutedEventArgs e)
    {
        if (_longPress.HasDragStarted)
        {
            return;                     // 拖动自己会收尾
        }

        CancelLongPress();
    }

    private DispatcherQueueTimer EnsureLongPressTimer()
    {
        if (_longPressTimer is null)
        {
            _longPressTimer = DispatcherQueue.CreateTimer();
            _longPressTimer.Interval = TimeSpan.FromMilliseconds(_longPress.ThresholdMs);
            _longPressTimer.IsRepeating = false;
            _longPressTimer.Tick += async (_, _) => await StartLongPressDragAsync();
        }

        return _longPressTimer;
    }

    private async Task StartLongPressDragAsync()
    {
        _longPressTimer?.Stop();

        var container = _pressedContainer;
        var point = _pressedPoint;
        if (container is null || point is null || !_longPress.Tick(Environment.TickCount64))
        {
            return;
        }

        try
        {
            container.CanDrag = true;                 // StartDragAsync 要求 CanDrag=true，起拖前临时开
            _suppressNextClick = true;
            await container.StartDragAsync(point);
        }
        catch (Exception ex)
        {
            App.WriteCrash("ListViewPage.StartLongPressDragAsync", ex);
        }
        finally
        {
            container.CanDrag = false;
            HideDropIndicator();
            _longPress.Complete();                    // 列表页没有"长按要菜单"，结论不用
            ResetPressedRow();
        }
    }

    private void CancelLongPress()
    {
        _longPressTimer?.Stop();
        _longPress.Reset();
        ResetPressedRow();
    }

    private void ResetPressedRow()
    {
        _pressedContainer = null;
        _pressedPoint = null;
    }

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
        var row = (args.OriginalSource as FrameworkElement)?.DataContext as ListRowViewModel;
        if (row is null)
        {
            args.Cancel = true;
            return;
        }

        var payload = new DragPayload(DragItemKind.ListItem, _tab.Id, row.Model.Id);
        args.Data.SetText(payload.ToString());
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        if (!TryReadPayload(e, out _))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            HideDropIndicator();
            return;
        }

        // 拖动期间的"有没有移动"喂给长按状态机（与网格页同款；拖动中指针事件不保证还来）
        var pointer = e.GetPosition(Rows);
        _longPress.Move(pointer.X, pointer.Y);

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;

        ShowDropIndicator(ComputeInsertIndex(e));
    }

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        var insertIndex = ComputeInsertIndex(e);
        HideDropIndicator();

        if (!TryReadPayload(e, out var payload))
        {
            return;
        }

        ItemDropped?.Invoke(this, new DragDropRequest(payload, _tab.Id, insertIndex));
    }

    private void OnRowDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        => HideDropIndicator();

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
