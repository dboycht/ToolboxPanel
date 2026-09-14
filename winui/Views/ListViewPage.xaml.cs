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

        // 长按起拖要用**指针事件**；ListViewBase 自己也会处理这些事件，
        // 所以必须 handledEventsToo: true —— 否则我们根本收不到（内部已经把按下吃掉了）。
        Rows.AddHandler(PointerPressedEvent, new PointerEventHandler(OnRowPointerPressed), handledEventsToo: true);
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

    // ────────────────────────────── 长按起拖（与网格页同一套，2026-09-14 第二版）──────────────────────────────
    //
    // 手感与网格页一致：**按住 350ms ⇒ 行"浮起"跟手拖**；
    // 没有"长按原地松手弹菜单"，也不因"按住期间移动过"而取消（详见 GridPage 的注释）。
    // ⚠️ 早到的定时器必须按 RemainingMs 重排再试，否则这次长按会被静默丢掉。

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
        _longPress.Press(Environment.TickCount64);
        RestartLongPressTimer(_longPress.ThresholdMs);
    }

    private void OnRowPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _longPressTimer?.Stop();
        _longPress.Release();
        ResetPressedRow();
    }

    private void OnRowPointerAborted(object sender, PointerRoutedEventArgs e)
    {
        _longPressTimer?.Stop();
        _longPress.Reset();
        ResetPressedRow();
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
            container.CanDrag = true;                 // StartDragAsync 要求 CanDrag=true
            _suppressNextClick = true;
            ApplyLift(container, lifted: true);

            await container.StartDragAsync(point);    // 拖拽视觉 = 行快照，跟着鼠标走
        }
        catch (Exception ex)
        {
            App.WriteCrash("ListViewPage.StartLongPressDragAsync", ex);
        }
        finally
        {
            container.CanDrag = false;
            ApplyLift(container, lifted: false);
            HideDropIndicator();
            _longPress.Release();
            ResetPressedRow();
        }
    }

    /// <summary>"浮起"反馈：只用合成变换（Scale / Opacity），不碰布局。</summary>
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