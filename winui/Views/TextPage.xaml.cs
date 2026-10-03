// TextPage.xaml.cs —— 文本页（粘贴板式表格页）
//
// 职责：铺出该标签页的文本项、**点击一行即复制它的文本**、入场动效、右键菜单事件、
//       **行拖拽排序（2026-10-03：页内重排 + 跨文本页搬）**。
//
// 与列表页（`ListViewPage`）的关系：形态与分工照抄它 ——
//   · 页面只发事件（`TextItemActivated` / `TextItemMenuActionRequested` / `NewTextItemRequested` /
//     `ItemDropped`），弹对话框、落库、反馈全在宿主窗口（`MainWindow`），与图块/列表项同一套分工；
//   · 绑的是**可见集合** `VisibleTextItems`（搜索过滤后的子集，仍是 Core 顺序）。
//
// ⚠️ 拖拽这条链路**必须照抄列表页那套实测形态**（ERROR.md E25）：
//    起拖 / 指针事件在 WinUI 3 里全都收不到，唯一可靠的是
//    "目标端 `DragOver` 登记落点 + 源端 `DragItemsCompleted` 收口"。
//    载荷类型是第三种（`DragItemKind.TextItem`）⇒ 文本项拖到网格页/列表页会被拒收，
//    反过来图标/列表项也进不了这一页（判据在 Core 的 `DataStore.ApplyDragDrop`，有单测）。

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;
using ToolboxPanel.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace ToolboxPanel.Views;

public sealed partial class TextPage : UserControl, IAnimatedPage, ISearchablePage
{
    private readonly TabItemViewModel _tab;
    private readonly EntranceAnimator _entrance;

    public TextPage(TabItemViewModel tab)
    {
        _tab = tab;

        InitializeComponent();

        Rows.ItemsSource = tab.VisibleTextItems;
        NoMatchText.Text = SearchFilter.NoResultTextText;

        tab.VisibleTextItems.CollectionChanged += (_, _) => UpdateEmptyHints();

        _entrance = new EntranceAnimator(Rows);

        // 主题圆角（`radius` 参数）：新实现/回收再利用的行容器也要补一次，
        // 否则"改了圆角之后才滚动出来"的行还是样式里那个旧值（与列表页同一套）。
        Rows.ContainerContentChanging += (_, args) =>
        {
            if (!args.InRecycleQueue && args.ItemContainer is Control container)
            {
                container.CornerRadius = ThemeScale.Corners(DesignCornerRadius, _cornerScale);
            }
        };

        UpdateEmptyHints();
    }

    public TabItemViewModel Tab => _tab;

    /// <summary>演示模式下不写盘：由宿主窗口置为 false 关掉拖拽（与列表页/网格页同一个开关）。</summary>
    public bool DragDropEnabled
    {
        set
        {
            // ⚠️ 同列表页：不设 Rows.CanDrag（那样会让系统自己起拖、绕开行容器的门控）
            Rows.AllowDrop = value;
        }
    }

    // ────────────────────────────── 搜索过滤 ──────────────────────────────
    //
    // 与网格页/列表页同一套（判定在 Core 的 `SearchFilter`）：文本项按**备注 / 文本**过滤。

    public void ApplySearch(string? query)
    {
        _tab.SetFilter(query);
        HideDropIndicator();
        UpdateEmptyHints();
    }

    /// <summary>
    /// 套用当前语言：XAML 上标了 `ui:Tr.Key` 的静态文字由 `Tr.RefreshAll()` 负责，
    /// 这里只重写"由代码设置的"无匹配提示（文案来自 Core）。
    /// </summary>
    public void ApplyLanguage() => NoMatchText.Text = SearchFilter.NoResultTextText;

    /// <summary>
    /// 空页提示 vs 搜索无匹配提示（每次集合变化都重算：新建/删除/过滤都会走到这里）。
    ///
    /// <para>⚠️ 这里刻意**不写成"构造函数里算一次"**：页面是常驻的（切页只切 Visibility、
    /// 不重建），只在构造时算的话，"给空页新建一条之后提示还挂着"（见 ERROR.md E28）。</para>
    /// </summary>
    private void UpdateEmptyHints()
    {
        var empty = _tab.TextItems.Count == 0;
        var noMatch = !empty && _tab.VisibleTextItems.Count == 0;

        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        NoMatchHint.Visibility = noMatch ? Visibility.Visible : Visibility.Collapsed;
    }

    // ────────────────────────────── 事件（页面 → 宿主窗口）──────────────────────────────

    /// <summary>点了一行 —— 把这一行的**原文**交给宿主窗口复制（页面不碰剪贴板）。</summary>
    public event EventHandler<Core.Models.TextItemModel>? TextItemActivated;

    /// <summary>行的右键菜单选了某一项（页面不碰数据、不开对话框，同其它页的分工）。</summary>
    public event EventHandler<TextItemMenuRequest>? TextItemMenuActionRequested;

    /// <summary>在空白处右键 = 新建文本项（原版语义：**直接弹对话框**，不经过菜单）。</summary>
    public event EventHandler? NewTextItemRequested;

    // ────────────────────────────── 行右键菜单 ──────────────────────────────
    //
    // 菜单规格（顺序 / 分隔线 / 文案）在 Core 的 `TextItemContextMenu`（有单测）；
    // 这里只负责按规格铺控件 + 把动作转成事件。

    private void OnRowContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        var position = args.TryGetPosition(Rows, out var point) ? point : new Windows.Foundation.Point(0, 0);
        var row = FindRowFromSource(args.OriginalSource);

        if (row is null)
        {
            NewTextItemRequested?.Invoke(this, EventArgs.Empty);   // 空白处 = 新建文本项
            args.Handled = true;
            return;
        }

        var menu = new MenuFlyout();

        foreach (var item in TextItemContextMenu.Build())
        {
            if (item.SeparatorBefore)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            var action = item.Action;
            var menuItem = new MenuFlyoutItem { Text = item.Label };
            menuItem.Click += (_, _) => TextItemMenuActionRequested?.Invoke(this, new TextItemMenuRequest(row.Model, action));
            menu.Items.Add(menuItem);
        }

        menu.ShowAt(Rows, position);
        args.Handled = true;
    }

    /// <summary>右键点在哪 —— 往上找到承载这一行的 ListViewItem（空白处返回 null）。</summary>
    private static TextRowViewModel? FindRowFromSource(object? source)
    {
        var current = source as DependencyObject;

        while (current is not null)
        {
            if (current is ListViewItem { DataContext: TextRowViewModel row })
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

    // ────────────────────────────── 点击 = 复制 ──────────────────────────────

    private void OnRowClick(object sender, ItemClickEventArgs e)
    {
        // 长按（起拖）之后系统偶尔还会补一次 ItemClick —— 那次不能当成"复制"
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            return;
        }

        // 单纯点击 = 没拖动 ⇒ 顺手清掉落点登记
        DragSession.ClearTarget();

        // ⚠️ 复制的是**模型里的原文**（多行 / 制表符一个不少），不是屏幕上那行压扁的预览。
        if (e.ClickedItem is TextRowViewModel row)
        {
            TextItemActivated?.Invoke(this, row.Model);
        }
    }

    // ────────────────────────────── 拖动链路（与列表页同一套，ERROR.md E25）──────────────────────────────
    //
    // 收不到任何起拖/指针事件 ⇒ 只靠两个实测可靠的事件：
    //   目标端 `DragOver` 登记落点 + 源端 `DragItemsCompleted`（带 args.Items / DropResult）收口。

    private bool _suppressNextClick;
    private int _lastTracedIndex = -1;
    private bool _tracedDragOverEntry;
    private bool _dropSeen;                    // 本次拖动是否真的落在本页（Drop 事件到场）

    /// <summary>拖动链路诊断（写 %TEMP%\toolboxpanel-probe.log）。</summary>
    private static void DragTrace(string message) => DragDropShared.Trace(message);

    /// <summary>这次拖放带了什么（诊断用；读不到就当作没有）。</summary>
    private static (bool HasText, string? Text, bool HasStorageItems) DescribeData(DragEventArgs e)
        => DragDropShared.Describe(e, "TextPage.DescribeData");

    /// <summary>拖放落下 —— 交给宿主窗口落库（与列表页同一个事件契约）。</summary>
    public event EventHandler<DragDropRequest>? ItemDropped;

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
        DragSession.ReportTarget(_tab.Id, DragItemKind.TextItem, insertIndex);

        if (insertIndex != _lastTracedIndex)
        {
            _lastTracedIndex = insertIndex;
            DragTrace($"DragOver：落点索引={insertIndex}（已登记）");
        }

        // 拖动中不移动任何行（与两外两页一致）：只在行与行之间画一根插入横条
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
            var dropIndex = ComputeInsertIndex(e);
            DragSession.ReportTarget(_tab.Id, DragItemKind.TextItem, dropIndex);
            HideDropIndicator();
            DragTrace($"Drop（内部拖动）：落点={dropIndex} ⇒ 留给 DragItemsCompleted 收口");
            return;
        }

        HideDropIndicator();
    }

    /// <summary>
    /// ★ **一次内部拖动的收口点**（源端事件，实测可靠）：`args.Items` 给出被拖的行本身，
    /// `args.DropResult` 给出落没落下，再加上目标端登记的落点 ⇒ 完整的一次重排 / 跨页移动。
    /// </summary>
    private void OnRowDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        HideDropIndicator();
        _lastTracedIndex = -1;
        _suppressNextClick = false;
        _tracedDragOverEntry = false;   // 复位诊断标记（漏了它，日志从第二次拖动起就废了）

        var target = DragSession.TakeTarget();
        var row = args.Items.Count > 0 ? args.Items[0] as TextRowViewModel : null;
        var dropResult = args.DropResult;
        var dropSeen = _dropSeen;
        _dropSeen = false;

        DragTrace($"DragItemsCompleted：DropResult={dropResult} items={args.Items.Count} "
                  + $"被拖={row?.Note ?? "null"} 登记落点={(target is null ? "无" : $"{target.Value.TabId}#{target.Value.InsertIndex}")} "
                  + $"Drop到过本页={dropSeen}");

        if (row is null || target is null)
        {
            return;
        }

        if (target.Value.Kind != DragItemKind.TextItem)
        {
            DragTrace("→ 落点不是文本页，文本项不进别的页 ⇒ 忽略");
            return;
        }

        // ⚠️ 结算延后一拍（Drop 与 DragItemsCompleted 先后顺序不保证，同另外两页）。
        //    ⚠️ 只认上面那个 `dropSeen` 快照，**不再二次读字段**（会把下一次拖动的标记误当本轮）。
        DispatcherQueue.TryEnqueue(() =>
        {
            var landed = dropResult == DataPackageOperation.Move || dropSeen;

            if (!landed)
            {
                DragTrace("→ 判定：拖动被取消 ⇒ 不落库");
                return;
            }

            var payload = new DragPayload(DragItemKind.TextItem, _tab.Id, row.Model.Id);
            DragTrace($"→ 落库：{row.Note} → 页 {target.Value.TabId} 第 {target.Value.InsertIndex} 位");
            ItemDropped?.Invoke(this, new DragDropRequest(payload, target.Value.TabId, target.Value.InsertIndex));
        });
    }

    // ────────────────────────────── 插入横条（与列表页同款）──────────────────────────────

    /// <summary>把落点（相对本页的坐标）交给 Core 的几何计算，得到"插到第几个"。</summary>
    private int ComputeInsertIndex(DragEventArgs e)
        => DragDropShared.ComputeInsertIndex(e, this, CollectRowBounds());

    /// <summary>
    /// 已实现行的矩形（相对本页，DIP），顺序 = 从上到下。
    /// ⚠️ 遍历的是**可见集合**（= ItemsSource）：落点是"可见位"，过滤态下由 MainViewModel 换算回 Core 下标。
    /// </summary>
    private List<ItemBounds> CollectRowBounds()
        => DragDropShared.CollectBounds(Rows, _tab.VisibleTextItems.Count, this, "TextPage.CollectRowBounds");

    private void ShowDropIndicator(int insertIndex)
        => DragDropShared.ShowHorizontalIndicator(DropIndicator, CollectRowBounds(), insertIndex);

    private void HideDropIndicator() => DragDropShared.HideIndicator(DropIndicator);

    // ────────────────────────────── 动画 / 主题钩子 ──────────────────────────────

    public void ApplyAnimationSpec(AnimationSpec spec) => _entrance.ApplySpec(spec);

    /// <summary>置于入场起始态（整页不透明度 = 0）。必须在页面可见之前调用。</summary>
    public void PrepareEntrance() => _entrance.Prepare();

    /// <summary>等到"可以安全放行"（容器已就位或布局已跑过）再开始入场并放行整页。</summary>
    public void RevealWhenReady() => _entrance.RevealWhenReady();

    /// <summary>列表行的设计圆角（DIP）—— `radius` 参数默认值时就是它本身（与列表页同值）。</summary>
    private const double DesignCornerRadius = 4;

    /// <summary>当前主题的圆角倍率（默认 1.0）。</summary>
    private double _cornerScale = 1;

    /// <summary>
    /// 套用主题：① 把落点指示线刷成当前强调色（它是本项目注入的固定键 `AccentBrushDark`，
    /// **不随主题变**，必须逐次赋值）；② 把圆角倍率落到列表行容器上（`radius` 参数，默认 1.0）。
    /// </summary>
    public void ApplyTheme(ThemePalette palette, double radiusScale)
    {
        var (a, r, g, b) = palette.Accent;
        DropIndicator.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));

        _cornerScale = radiusScale;
        ApplyCornerRadiusToRealizedRows();
    }

    /// <summary>把圆角写给"已经实现出来"的行容器（新实现/回收的走 ContainerContentChanging）。</summary>
    private void ApplyCornerRadiusToRealizedRows()
    {
        for (int i = 0; i < _tab.VisibleTextItems.Count; i++)
        {
            if (Rows.ContainerFromIndex(i) is Control container)
            {
                container.CornerRadius = ThemeScale.Corners(DesignCornerRadius, _cornerScale);
            }
        }
    }
}

/// <summary>一次文本行菜单动作请求（页面 → 宿主窗口）。与列表项的 <c>ListItemMenuRequest</c> 同一套写法。</summary>
public sealed record TextItemMenuRequest(Core.Models.TextItemModel Item, TextItemMenuAction Action);
