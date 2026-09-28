// TextPage.xaml.cs —— 文本页（粘贴板式表格页）
//
// 职责：铺出该标签页的文本项、**点击一行即复制它的文本**、入场动效、右键菜单事件。
//
// 与列表页（`ListViewPage`）的关系：形态与分工照抄它 ——
//   · 页面只发事件（`TextItemActivated` / `TextItemMenuActionRequested` / `NewTextItemRequested`），
//     弹对话框、落库、反馈全在宿主窗口（`MainWindow`），与图块/列表项同一套分工；
//   · 绑的是**可见集合** `VisibleTextItems`（搜索过滤后的子集，仍是 Core 顺序）。
//
// ⚠️ 本页**不做拖拽**（本轮有意）：`CanDrag=False`、不接 DragOver/Drop。
//    理由见 HANDOVER：文本页是"点击复制"的取值页，排序需求弱；而拖拽链路在本项目是
//    "目标端 DragOver 登记落点 + 源端 DragItemsCompleted 收口"那套（ERROR.md E25），
//    要为它再造一套载荷类型 + 跨页门控 + Core 落库入口，风险大于收益。
//    真要加时记得三件事一起做，并让用户手验。

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;
using ToolboxPanel.ViewModels;

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

        UpdateEmptyHints();
    }

    public TabItemViewModel Tab => _tab;

    // ────────────────────────────── 搜索过滤 ──────────────────────────────
    //
    // 与网格页/列表页同一套（判定在 Core 的 `SearchFilter`）：文本项按**备注 / 文本**过滤。

    public void ApplySearch(string? query)
    {
        _tab.SetFilter(query);
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
        // ⚠️ 复制的是**模型里的原文**（多行 / 制表符一个不少），不是屏幕上那行压扁的预览。
        if (e.ClickedItem is TextRowViewModel row)
        {
            TextItemActivated?.Invoke(this, row.Model);
        }
    }

    // ────────────────────────────── 动画 / 主题钩子 ──────────────────────────────

    public void ApplyAnimationSpec(AnimationSpec spec) => _entrance.ApplySpec(spec);

    /// <summary>置于入场起始态（整页不透明度 = 0）。必须在页面可见之前调用。</summary>
    public void PrepareEntrance() => _entrance.Prepare();

    /// <summary>等到"可以安全放行"（容器已就位或布局已跑过）再开始入场并放行整页。</summary>
    public void RevealWhenReady() => _entrance.RevealWhenReady();

    /// <summary>
    /// 套用主题 —— 本页**没有需要按令牌重绘的固定资源键**（落点指示线是拖拽页才有的东西，
    /// 而这一页不做拖拽），所以这里是空实现。
    ///
    /// <para>⚠️ 不能因为"是空的"就把这个接口 implementation 摘掉：`MainWindow.ApplyAllSettings`
    /// 是按 `IAnimatedPage` 逐页下发主题与动效的，少一层接口这一页就成"主题切换时唯一不被套用的页"；
    /// 而且空实现本身是有信息的：它说明"这一页的所有颜色都走 ThemeResource，没有代码里写死的令牌"。</para>
    /// </summary>
    public void ApplyTheme(ThemePalette palette, double radiusScale)
    {
        // 故意为空：见上面的说明。
    }
}

/// <summary>一次文本行菜单动作请求（页面 → 宿主窗口）。与列表项的 <c>ListItemMenuRequest</c> 同一套写法。</summary>
public sealed record TextItemMenuRequest(Core.Models.TextItemModel Item, TextItemMenuAction Action);
