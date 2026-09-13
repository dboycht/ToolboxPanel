// DragDropTests.cs —— W3 拖拽排序（Core 侧）
//
// ⚠️ WinUI 的拖放交互**只能由用户手动验证**（代理不注入鼠标输入，见 ERROR.md E5）。
//    所以这里覆盖的是"拖放之后**数据对不对**"这一半：
//      ① 插入索引怎么算（纯几何，UI 只是把矩形喂进来）；
//      ② 落库之后 tabs.json 的顺序 / sort_order 是否正确（含跨页移动）。
//    这两条覆盖了拖放里最容易出错、且用户肉眼不易发现的部分。

using System.Text.Json;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class DragDropTests
{
    // ────────────────────────────── 插入索引（纯几何）──────────────────────────────

    /// <summary>造一列 100×20、纵向排列的 item（列表页的形态）。</summary>
    private static List<ItemBounds> Column(int count, double height = 20)
        => Enumerable.Range(0, count).Select(i => new ItemBounds(0, i * height, 100, height)).ToList();

    [Fact]
    public void 插入索引_指针在首项上半部分时插到最前()
    {
        Assert.Equal(0, DropIndexCalculator.Compute(Column(3), pointerY: 5));
    }

    [Fact]
    public void 插入索引_以每项的中心为界_正好压在边界上算越过()
    {
        // 三项 100×20：Y 区间 0~20 / 20~40 / 40~60，中心 10 / 30 / 50
        // ⇒ 分界点就是"相邻两项之间"的 y = 20 与 y = 40（不是 10/30/50）
        var bounds = Column(3);

        Assert.Equal(0, DropIndexCalculator.Compute(bounds, 9.9));
        Assert.Equal(1, DropIndexCalculator.Compute(bounds, 20));      // 第一项下边界 = 第一/第二项之间
        Assert.Equal(1, DropIndexCalculator.Compute(bounds, 39.9));
        Assert.Equal(2, DropIndexCalculator.Compute(bounds, 40));      // 第二/第三项之间
        Assert.Equal(2, DropIndexCalculator.Compute(bounds, 59.9));
    }

    [Fact]
    public void 插入索引_指针在最后一项中点之下或页面空白处时放到最后()
    {
        var bounds = Column(3);

        // ⚠️ 60 正好是最后一项的下边界：必须算"越过"（用 ≥ 而不是 >），
        //    否则"拖到列表末尾"会永远差一格（真被单测抓出来过）
        Assert.Equal(3, DropIndexCalculator.Compute(bounds, 60));
        Assert.Equal(3, DropIndexCalculator.Compute(bounds, 9999));
    }

    [Fact]
    public void 插入索引_空页面时放到第0个位置()
    {
        Assert.Equal(0, DropIndexCalculator.Compute(Array.Empty<ItemBounds>(), pointerY: 42));
    }

    [Fact]
    public void 插入索引_网格页按行主序判定_第二行左侧不会被误判到第一行末尾()
    {
        // 两行三列：行高 40、列宽 60。索引 0~2 是行主序第一行，3~5 是第二行。
        var bounds = new List<ItemBounds>
        {
            new(0, 0, 60, 40), new(60, 0, 60, 40), new(120, 0, 60, 40),
            new(0, 40, 60, 40), new(60, 40, 60, 40), new(120, 40, 60, 40),
        };

        // ⚠️ 下面这几条是「只比 Y 中点」那版会算错的用例：
        //    指针在第二行左侧时，那版会把第一行三项全判为"还没越过 Y 中点" → 算成"插到第 3 位"，
        //    而下面是"落在第二行第一格内"→ 仍然是第 3 位，但**含义不同**（锚点在第 4 项而不是第 3 项）。
        //    真正能区分两版的是 (10,70) 与 (10,400)：旧版给 3 / 3，新版给 3 / 4。
        Assert.Equal(3, DropIndexCalculator.Compute(bounds, pointerX: 10, pointerY: 45));   // 第二行第一格上半 → 插到它前面
        Assert.Equal(3, DropIndexCalculator.Compute(bounds, pointerX: 10, pointerY: 70));   // 第二行第一格下半（未过下边界）→ 仍插到它前面
        Assert.Equal(4, DropIndexCalculator.Compute(bounds, pointerX: 10, pointerY: 90));   // 过了第二行第一格的下边界 → 插到它后面
        Assert.Equal(4, DropIndexCalculator.Compute(bounds, pointerX: 10, pointerY: 400));  // 页面空白处 → 末尾

        // 一格内部：落点在**该格自己的范围内** → 插到它前面；越过它的右/下边界 → 插到它后面。
        // （实测扫过一遍：x=10..50 都给 0，x=60 起给 1，与"用 ≥ 判越界"一致）
        Assert.Equal(0, DropIndexCalculator.Compute(bounds, pointerX: 10, pointerY: 6));
        Assert.Equal(0, DropIndexCalculator.Compute(bounds, pointerX: 50, pointerY: 6));
        Assert.Equal(1, DropIndexCalculator.Compute(bounds, pointerX: 60, pointerY: 6));   // 越过第一格右边界
    }

    [Fact]
    public void 指示线位置_按插入索引取该项左上角_末尾则贴到最后一项右缘()
    {
        var bounds = Column(3);

        var atOne = DropIndexCalculator.IndicatorAt(bounds, 1);
        Assert.Equal(0, atOne.X);
        Assert.Equal(20, atOne.Y);
        Assert.Equal(20, atOne.Height);

        var atEnd = DropIndexCalculator.IndicatorAt(bounds, 3);
        Assert.Equal(102, atEnd.X);   // 最后一项 X(0) + W(100) + 2
        Assert.Equal(40, atEnd.Y);
    }

    // ────────────────────────────── 载荷编解码 ──────────────────────────────

    [Fact]
    public void 载荷_编码后能原样解析回来()
    {
        var payload = new DragPayload(DragItemKind.ListItem, "tab-1", "item-9");

        var parsed = DragPayload.TryParse(payload.ToString());

        Assert.NotNull(parsed);
        Assert.Equal(DragItemKind.ListItem, parsed!.Kind);
        Assert.Equal("tab-1", parsed.SourceTabId);
        Assert.Equal("item-9", parsed.ItemId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("icon|tab-1")]              // 少一段
    [InlineData("bogus|tab-1|item-1")]      // 类型不认识
    [InlineData("icon||item-1")]            // 空 id
    [InlineData("icon|tab-1|a|b")]          // 多一段
    public void 载荷_格式不对一律不接受(string text)
    {
        Assert.Null(DragPayload.TryParse(text));
    }

    // ────────────────────────────── 落库：同页排序 ──────────────────────────────

    /// <summary>建一个 grid 页 + 三个图标，返回 (store, tab, 图标 id 依当前顺序)。</summary>
    private static (DataStore Store, TabModel Tab, List<string> IconIds) StoreWithIcons(TempDataDirectory temp)
    {
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("图标页");

        foreach (var name in new[] { "A", "B", "C" })
        {
            store.AddIcon(tab.Id, new IconModel { DisplayName = name, SourcePath = @"C:\a.exe" });
        }

        return (store, tab, tab.Icons.Select(i => i.Id).ToList());
    }

    [Fact]
    public void 同页排序_把第一个拖到最后_落库顺序与序号都正确()
    {
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithIcons(temp);

        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, tab.Id, ids[0]), tab.Id, targetIndex: 3));

        Assert.True(result.Success);

        // 内存
        Assert.Equal(new[] { "B", "C", "A" }, tab.Icons.Select(i => i.DisplayName));
        Assert.Equal(new[] { 0, 1, 2 }, tab.Icons.Select(i => i.SortOrder));

        // 重新从磁盘读：顺序必须一致（这是"下次启动界面不乱"的唯一保证）
        var reloaded = temp.NewStore().Load().Single(t => t.Id == tab.Id);
        Assert.Equal(new[] { "B", "C", "A" }, reloaded.Icons.Select(i => i.DisplayName));
        Assert.Equal(new[] { 0, 1, 2 }, reloaded.Icons.Select(i => i.SortOrder));
    }

    [Fact]
    public void 同页排序_把最后一个拖到最前()
    {
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithIcons(temp);

        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, tab.Id, ids[2]), tab.Id, targetIndex: 0));

        Assert.True(result.Success);
        Assert.Equal(new[] { "C", "A", "B" }, tab.Icons.Select(i => i.DisplayName));
        Assert.Equal(new[] { "C", "A", "B" },
            temp.NewStore().Load().Single(t => t.Id == tab.Id).Icons.Select(i => i.DisplayName));
    }

    [Fact]
    public void 同页排序_原地落下也会把序号重排成连续值()
    {
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithIcons(temp);

        // 人为把序号弄乱（模拟旧数据/手工改过的文件）
        tab.Icons[0].SortOrder = 7;
        tab.Icons[1].SortOrder = 7;
        tab.Icons[2].SortOrder = 7;

        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, tab.Id, ids[1]), tab.Id, targetIndex: 1));

        Assert.True(result.Success);
        Assert.Equal(new[] { "A", "B", "C" }, tab.Icons.Select(i => i.DisplayName));
        Assert.Equal(new[] { 0, 1, 2 }, tab.Icons.Select(i => i.SortOrder));
    }

    // ────────────────────────────── 落库：跨页移动 ──────────────────────────────

    [Fact]
    public void 跨页移动_图标挪到另一页的中间位置_两页序号都重排()
    {
        using var temp = new TempDataDirectory();
        var (store, source, sourceIds) = StoreWithIcons(temp);
        var target = store.AddTab("目标页");
        store.AddIcon(target.Id, new IconModel { DisplayName = "X" });
        store.AddIcon(target.Id, new IconModel { DisplayName = "Y" });

        // 把源页的 B 插到目标页的第 1 位（X 与 Y 之间）
        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, source.Id, sourceIds[1]), target.Id, targetIndex: 1));

        Assert.True(result.Success);
        Assert.Equal(new[] { "A", "C" }, source.Icons.Select(i => i.DisplayName));
        Assert.Equal(new[] { "X", "B", "Y" }, target.Icons.Select(i => i.DisplayName));
        Assert.Equal(new[] { 0, 1 }, source.Icons.Select(i => i.SortOrder));
        Assert.Equal(new[] { 0, 1, 2 }, target.Icons.Select(i => i.SortOrder));

        var reloaded = temp.NewStore().Load();
        Assert.Equal(new[] { "A", "C" }, reloaded.Single(t => t.Id == source.Id).Icons.Select(i => i.DisplayName));
        Assert.Equal(new[] { "X", "B", "Y" }, reloaded.Single(t => t.Id == target.Id).Icons.Select(i => i.DisplayName));
    }

    [Fact]
    public void 跨页移动_目标索引要先去掉自己再算_否则往后面的位置会差一位()
    {
        using var temp = new TempDataDirectory();
        var (store, source, sourceIds) = StoreWithIcons(temp);
        var target = store.AddTab("目标页");
        store.AddIcon(target.Id, new IconModel { DisplayName = "X" });
        store.AddIcon(target.Id, new IconModel { DisplayName = "Y" });

        // 源页的 A 放到目标页第 2 位（Y 之后）——去掉自己后目标页只有 X/Y 两项，插到 2 就是末尾
        store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, source.Id, sourceIds[0]), target.Id, targetIndex: 2));

        Assert.Equal(new[] { "X", "Y", "A" }, target.Icons.Select(i => i.DisplayName));
        Assert.Equal(new[] { "B", "C" }, source.Icons.Select(i => i.DisplayName));
    }

    // ────────────────────────────── 落库：列表页 ──────────────────────────────

    private static (DataStore Store, TabModel Tab) StoreWithListItems(TempDataDirectory temp, params string[] descriptions)
    {
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("列表页", TabModel.TypeList);

        foreach (var description in descriptions)
        {
            store.AddListItem(tab.Id, new ListItemModel { Description = description, Path = @"C:\" + description });
        }

        return (store, tab);
    }

    [Fact]
    public void 列表页同页排序_落库顺序正确()
    {
        using var temp = new TempDataDirectory();
        var (store, tab) = StoreWithListItems(temp, "一", "二", "三");
        var id = tab.ListItems[2].Id;

        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.ListItem, tab.Id, id), tab.Id, targetIndex: 0));

        Assert.True(result.Success);
        Assert.Equal(new[] { "三", "一", "二" }, tab.ListItems.Select(i => i.Description));
        Assert.Equal(new[] { 0, 1, 2 }, tab.ListItems.Select(i => i.SortOrder));
        Assert.Equal(new[] { "三", "一", "二" },
            temp.NewStore().Load().Single(t => t.Id == tab.Id).ListItems.Select(i => i.Description));
    }

    [Fact]
    public void 列表页跨页移动_也在两页之间正确落库()
    {
        using var temp = new TempDataDirectory();
        var (store, source) = StoreWithListItems(temp, "一", "二");
        var target = store.AddTab("另一个列表页", TabModel.TypeList);
        store.AddListItem(target.Id, new ListItemModel { Description = "末", Path = @"C:\末" });

        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.ListItem, source.Id, source.ListItems[0].Id), target.Id, targetIndex: 0));

        Assert.True(result.Success);
        Assert.Equal(new[] { "二" }, source.ListItems.Select(i => i.Description));
        Assert.Equal(new[] { "一", "末" }, target.ListItems.Select(i => i.Description));

        var reloaded = temp.NewStore().Load();
        Assert.Equal(new[] { "一", "末" }, reloaded.Single(t => t.Id == target.Id).ListItems.Select(i => i.Description));
    }

    // ────────────────────────────── 拒绝的几种情况（数据必须一个字节都不动）──────────────

    [Fact]
    public void 拒绝_图标拖到列表页()
    {
        using var temp = new TempDataDirectory();
        var (store, gridTab, ids) = StoreWithIcons(temp);
        var listTab = store.AddTab("列表页", TabModel.TypeList);
        store.Save();   // 让磁盘状态与内存一致，才能用"文件字节没变"来断言"数据没动"

        var before = File.ReadAllBytes(temp.TabsFile);

        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, gridTab.Id, ids[0]), listTab.Id, targetIndex: 0));

        Assert.False(result.Success);
        Assert.Contains("列表页", result.Reason);
        Assert.Equal(new[] { "A", "B", "C" }, gridTab.Icons.Select(i => i.DisplayName));
        Assert.Equal(before, File.ReadAllBytes(temp.TabsFile));   // 文件没被重写
    }

    [Fact]
    public void 拒绝_列表项拖到网格页()
    {
        using var temp = new TempDataDirectory();
        var (store, listTab) = StoreWithListItems(temp, "一");
        var gridTab = store.AddTab("网格页");

        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.ListItem, listTab.Id, listTab.ListItems[0].Id), gridTab.Id, targetIndex: 0));

        Assert.False(result.Success);
        Assert.Single(listTab.ListItems);
    }

    [Fact]
    public void 拒绝_被拖动的项已不存在()
    {
        using var temp = new TempDataDirectory();
        var (store, tab, _) = StoreWithIcons(temp);

        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, tab.Id, "不存在的-id"), tab.Id, targetIndex: 0));

        Assert.False(result.Success);
        Assert.Equal(new[] { "A", "B", "C" }, tab.Icons.Select(i => i.DisplayName));
    }

    [Fact]
    public void 拒绝_标签页已不存在()
    {
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithIcons(temp);

        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, tab.Id, ids[0]), "不存在的页", targetIndex: 0));

        Assert.False(result.Success);
        Assert.Equal(new[] { "A", "B", "C" }, tab.Icons.Select(i => i.DisplayName));
    }

    [Fact]
    public void 越界的插入索引一律夹取到合法范围()
    {
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithIcons(temp);

        var over = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, tab.Id, ids[0]), tab.Id, targetIndex: 999));
        Assert.True(over.Success);
        Assert.Equal(new[] { "B", "C", "A" }, tab.Icons.Select(i => i.DisplayName));
        Assert.True(over.Request.TargetIndex <= tab.Icons.Count);

        var under = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, tab.Id, ids[0]), tab.Id, targetIndex: -5));
        Assert.True(under.Success);
        Assert.Equal(new[] { "A", "B", "C" }, tab.Icons.Select(i => i.DisplayName));
    }

    [Fact]
    public void 拖放后写出格式仍是合法JSON且能被核心重新载入()
    {
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithIcons(temp);

        store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, tab.Id, ids[1]), tab.Id, targetIndex: 0));

        // 不只看"能读回"，还要求文件仍是那份无 BOM / CRLF / 2 空格的契约格式
        var bytes = File.ReadAllBytes(temp.TabsFile);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Contains("\r\n", File.ReadAllText(temp.TabsFile, System.Text.Encoding.UTF8));

        using var document = JsonDocument.Parse(File.ReadAllText(temp.TabsFile, System.Text.Encoding.UTF8));
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
    }
}
