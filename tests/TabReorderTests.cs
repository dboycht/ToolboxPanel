// TabReorderTests.cs —— 标签页拖动重排（Core 侧）
//
// ⚠️ 标签栏上的拖拽交互**只能由用户手动验证**（代理不注入鼠标输入，见 ERROR.md E5），
//    所以这里覆盖的是"拖完之后**数据和顺序对不对**"那一半：
//      ① 落点是"第几个标签之前"（纯几何，UI 只把矩形与指针 X 喂进来）；
//      ② `from + 插入位` 换算成 `DataStore.ReorderTabs(from, to)` 的下标（含"拖回原位 = 不动"）；
//      ③ 真的落库之后，`tabs.json` 里的顺序与 `order` 字段是否跟着变（拿真 DataStore 跑一遍）。
//
// 基准 = 原版 v1.11.6 `wrap_tab_bar.py::_on_drag_reorder`（`pop` 再 `insert`，`from == to` 不动）。
// 详见 `core/Services/TabReorder.cs` 顶部说明（含"本版比原版细一档"的那条差异）。

using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class TabReorderTests
{
    // ────────────────────────────── 落点几何 ──────────────────────────────

    /// <summary>造一行标签：宽度 <paramref name="width"/>、间距 3（与 XAML 里 StackPanel 的 Spacing 一致）。</summary>
    private static List<ItemBounds> Row(int count, double width = 100, double gap = 3)
        => Enumerable.Range(0, count)
            .Select(i => new ItemBounds(i * (width + gap), 0, width, 28))
            .ToList();

    [Fact]
    public void 落点_空标签栏时插到第0位()
    {
        Assert.Equal(0, TabReorder.ComputeInsertIndex(Array.Empty<ItemBounds>(), pointerX: 42));
    }

    [Fact]
    public void 落点_以每个标签的中心为界_压在中心上算越过()
    {
        // 三个 100 宽的标签：X = 0 / 103 / 206 ⇒ 中心 50 / 153 / 256
        var bounds = Row(3);

        Assert.Equal(0, TabReorder.ComputeInsertIndex(bounds, 0));
        Assert.Equal(0, TabReorder.ComputeInsertIndex(bounds, 49.9));
        Assert.Equal(1, TabReorder.ComputeInsertIndex(bounds, 50));      // 正好压在第一个的中心 = 已越过
        Assert.Equal(1, TabReorder.ComputeInsertIndex(bounds, 152.9));
        Assert.Equal(2, TabReorder.ComputeInsertIndex(bounds, 153));
        Assert.Equal(2, TabReorder.ComputeInsertIndex(bounds, 255.9));
        Assert.Equal(3, TabReorder.ComputeInsertIndex(bounds, 256));
    }

    [Fact]
    public void 落点_标签之间空隙里判给前一个之后()
    {
        // 空隙 = 100~103（前一个右边界 100，后一个左边界 103）：
        // 前一个的中心 50 已在左边 ⇒ 插入位 1（= 插到第一个之后），不会来回跳
        var bounds = Row(3);

        Assert.Equal(1, TabReorder.ComputeInsertIndex(bounds, 100));
        Assert.Equal(1, TabReorder.ComputeInsertIndex(bounds, 101.5));
        Assert.Equal(1, TabReorder.ComputeInsertIndex(bounds, 102.9));
    }

    [Fact]
    public void 落点_指针在标签栏空白处时放到最后()
    {
        var bounds = Row(3);

        Assert.Equal(3, TabReorder.ComputeInsertIndex(bounds, 9999));
        Assert.Equal(3, TabReorder.ComputeInsertIndex(bounds, 400));
    }

    [Fact]
    public void 落点_标签宽度不等时按各自中心判定()
    {
        // 原版标签宽度按文字长度自适应（本项目也一样）：
        // 短标签 [0,40] 中心 20；长标签 [43,223] 中心 133
        var bounds = new List<ItemBounds>
        {
            new(0, 0, 40, 28),
            new(43, 0, 180, 28),
        };

        Assert.Equal(0, TabReorder.ComputeInsertIndex(bounds, 19.9));
        Assert.Equal(1, TabReorder.ComputeInsertIndex(bounds, 20));     // 越过短标签中心 ⇒ 插到它后面
        Assert.Equal(1, TabReorder.ComputeInsertIndex(bounds, 132.9));
        Assert.Equal(2, TabReorder.ComputeInsertIndex(bounds, 133));    // 越过长标签中心 ⇒ 放到最后
    }

    // ────────────────────────────── 命中（指针落在哪个标签上）──────────────────────────────

    [Fact]
    public void 命中_用坐标判断指针在哪个标签上()
    {
        // 三个 100 宽的标签：X = 0 / 103 / 206，高 28
        var bounds = Row(3);

        Assert.Equal(0, TabReorder.HitTestIndex(bounds, 10, 14));
        Assert.Equal(1, TabReorder.HitTestIndex(bounds, 120, 5));
        Assert.Equal(2, TabReorder.HitTestIndex(bounds, 250, 27));
    }

    [Fact]
    public void 命中_标签之间的空隙与上下之外都不算命中()
    {
        var bounds = Row(3);

        Assert.Equal(-1, TabReorder.HitTestIndex(bounds, 101.5, 14));   // 空隙
        Assert.Equal(-1, TabReorder.HitTestIndex(bounds, 10, 29));      // 标签下方
        Assert.Equal(-1, TabReorder.HitTestIndex(bounds, 10, -1));      // 标签上方
        Assert.Equal(-1, TabReorder.HitTestIndex(bounds, 9999, 14));    // 标签栏右侧空白
    }

    [Fact]
    public void 命中_空标签栏时返回负一()
    {
        Assert.Equal(-1, TabReorder.HitTestIndex(Array.Empty<ItemBounds>(), 42, 14));
    }

    // ────────────────────────────── 下标换算 ──────────────────────────────

    [Fact]
    public void 换算_往右拖要补偿移除后前移的那一位()
    {
        // 4 页：把第 0 页插到第 2 个标签之前 ⇒ 移除后它应当落在下标 1
        Assert.Equal(new TabReorderPlan(0, 1), TabReorder.Resolve(fromIndex: 0, insertIndex: 2, tabCount: 4));

        // 插到最前/最后两个端点
        Assert.Equal(new TabReorderPlan(0, 2), TabReorder.Resolve(fromIndex: 0, insertIndex: 3, tabCount: 3));
        Assert.Equal(new TabReorderPlan(3, 0), TabReorder.Resolve(fromIndex: 3, insertIndex: 0, tabCount: 4));
    }

    [Fact]
    public void 换算_往左拖不补偿()
    {
        Assert.Equal(new TabReorderPlan(2, 0), TabReorder.Resolve(fromIndex: 2, insertIndex: 0, tabCount: 3));
        Assert.Equal(new TabReorderPlan(2, 1), TabReorder.Resolve(fromIndex: 2, insertIndex: 1, tabCount: 3));
    }

    [Theory]
    [InlineData(0, 1)]   // "插到第 1 个之前" = 它现在待的地方
    [InlineData(1, 1)]   // 自身左半格
    [InlineData(1, 2)]   // 自身右半格（"插到自己后面" = 还是原位）
    [InlineData(2, 2)]
    [InlineData(3, 3)]   // 最后一个插到"最后一个之前"
    public void 换算_拖回原位时什么都不做(int fromIndex, int insertIndex)
    {
        Assert.Null(TabReorder.Resolve(fromIndex, insertIndex, tabCount: 4));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public void 换算_只有一页时永远不动(int fromIndex, int insertIndex)
    {
        Assert.Null(TabReorder.Resolve(fromIndex, insertIndex, tabCount: 1));
    }

    [Fact]
    public void 换算_下标越界或页数为零时不崩也不动()
    {
        Assert.Null(TabReorder.Resolve(fromIndex: 0, insertIndex: 0, tabCount: 0));
        Assert.Null(TabReorder.Resolve(fromIndex: -1, insertIndex: 0, tabCount: 3));
        Assert.Null(TabReorder.Resolve(fromIndex: 3, insertIndex: 0, tabCount: 3));
    }

    [Fact]
    public void 换算_插入位越界先夹取再换算()
    {
        // 界面给 9999（指针在标签栏右侧空白处）⇒ 当作"放到最后"
        Assert.Equal(new TabReorderPlan(0, 2), TabReorder.Resolve(fromIndex: 0, insertIndex: 9999, tabCount: 3));

        // 负数（防御式）⇒ 当作"插到最前"
        Assert.Equal(new TabReorderPlan(2, 0), TabReorder.Resolve(fromIndex: 2, insertIndex: -5, tabCount: 3));
    }

    // ────────────────────────────── 与真实 DataStore 对上（原版同语义）──────────────────────────────

    /// <summary>按"把第 from 个标签拖到第 insert 个标签之前"走一遍完整链路，回读 tabs.json 的顺序。</summary>
    private static string[] ReorderByDrag(int fromIndex, int insertIndex)
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        store.AddTab("A");
        store.AddTab("B");

        // Tabs = Home / A / B / C，共 4 页
        store.AddTab("C");

        var plan = TabReorder.Resolve(fromIndex, insertIndex, store.Tabs.Count);
        if (plan is { } p)
        {
            store.ReorderTabs(p.FromIndex, p.ToIndex);
        }

        var reloaded = temp.NewStore().Load();
        Assert.Equal(new[] { 0, 1, 2, 3 }, reloaded.Select(t => t.Order));
        return reloaded.Select(t => t.Name).ToArray();
    }

    [Fact]
    public void 落库_把第一页拖到第二个标签之前_只挪一格()
    {
        Assert.Equal(new[] { "Home", "A", "B", "C" }, ReorderByDrag(0, 1));   // 拖回原位 ⇒ 一个字节都没动
        Assert.Equal(new[] { "A", "Home", "B", "C" }, ReorderByDrag(0, 2));
        Assert.Equal(new[] { "A", "B", "Home", "C" }, ReorderByDrag(0, 3));
        Assert.Equal(new[] { "A", "B", "C", "Home" }, ReorderByDrag(0, 4));   // 指针在最右端 ⇒ 放到最后
    }

    [Fact]
    public void 落库_把最后一页拖到最前()
    {
        Assert.Equal(new[] { "C", "Home", "A", "B" }, ReorderByDrag(3, 0));
        Assert.Equal(new[] { "Home", "C", "A", "B" }, ReorderByDrag(3, 1));
        Assert.Equal(new[] { "Home", "A", "C", "B" }, ReorderByDrag(3, 2));
        Assert.Equal(new[] { "Home", "A", "B", "C" }, ReorderByDrag(3, 3));   // 原位
    }
}
