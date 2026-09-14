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
        get => TileGrid.CanDrag;
        set
        {
            TileGrid.CanDrag = value;
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
        if (e.ClickedItem is IconTileViewModel tile)
        {
            IconActivated?.Invoke(this, tile.Model);
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
        (tile is null ? BuildNewIconMenu() : BuildTileMenu(tile)).ShowAt(TileGrid, position);
        args.Handled = true;
    }

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
            ShowDropIndicator(ComputeInsertIndex(e));
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

    /// <summary>拖动结束（含取消）—— 把指示线收掉，别留在界面上。</summary>
    private void OnTileDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        => HideDropIndicator();

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
