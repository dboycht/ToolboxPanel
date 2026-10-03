// TextDragDropTests.cs —— 文本页拖拽排序（2026-10-03）的 Core 侧
//
// 背景：文本页（第三种页）此前**刻意没有拖拽**（`CanDrag=False` + Core 直接拒收），
// 本轮补上"行可以在页内重排、也可以跨文本页搬"。这一批的全部判据都在 Core：
//   ① 同页重排的索引换算（"插到第 N 项之前" + 往后移要 -1）；
//   ② 跨页移动（先把项从源页摘掉，目标索引才是去掉自己之后的坐标系）；
//   ③ 三类载荷的门控（图标 → 网格页、列表项 → 列表页、**文本项 → 文本页**，互不通用）；
//   ④ `sort_order` 必须重排成 0..N-1 的连续值（[A] 留下的空洞就是"重启后顺序又变了"的种子）。
//
// ⚠️ WinUI 的拖放手感**只能由用户手动验证**（代理不注入鼠标，见 ERROR.md E5）；
//    这里覆盖的是"拖完之后数据对不对"这一半（与 DragDropTests 同一分工）。

using ToolboxPanel.Core;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class TextDragDropTests
{
    /// <summary>建一个文本页 + 三条文本项，返回 (store, tab, 三条的 id 依当前顺序)。</summary>
    private static (DataStore Store, TabModel Tab, List<string> Ids) StoreWithTextItems(
        TempDataDirectory temp, string tabName = "文本页")
    {
        var store = temp.NewStore();
        var tab = store.AddTab(tabName, TabModel.TypeText);

        foreach (var (note, text) in new[] { ("一", "内容一"), ("二", "内容二"), ("三", "内容三") })
        {
            store.AddTextItem(tab.Id, TextItemEditor.Create(note, text)!);
        }

        return (store, tab, tab.EffectiveTextItems.Select(i => i.Id).ToList());
    }

    private static DragPayload TextPayload(TabModel tab, string itemId)
        => new(DragItemKind.TextItem, tab.Id, itemId);

    // ────────────────────────────── 载荷编解码 ──────────────────────────────

    [Fact]
    public void 文本载荷_编码后能原样解析回来()
    {
        var payload = new DragPayload(DragItemKind.TextItem, "tab-1", "item-9");

        // ⚠️ 传输串的前缀是 "text"（枚举成员叫 TextItem，两者刻意不同名 —— 这里钉住映射关系）
        Assert.Equal("text|tab-1|item-9", payload.ToString());

        var parsed = DragPayload.TryParse(payload.ToString());

        Assert.NotNull(parsed);
        Assert.Equal(DragItemKind.TextItem, parsed!.Kind);
        Assert.Equal("tab-1", parsed.SourceTabId);
        Assert.Equal("item-9", parsed.ItemId);
    }

    [Fact]
    public void 载荷_三种类型的前缀互不相同()
    {
        // 前缀撞车 = 一类东西被当成另一类收下（落库时去找不存在的 id）
        var prefixes = new[]
        {
            new DragPayload(DragItemKind.Icon, "t", "i").ToString(),
            new DragPayload(DragItemKind.ListItem, "t", "i").ToString(),
            new DragPayload(DragItemKind.TextItem, "t", "i").ToString(),
        };

        Assert.Equal(new[] { "icon|t|i", "list|t|i", "text|t|i" }, prefixes);
    }

    // ────────────────────────────── 落库：同页重排 ──────────────────────────────

    [Fact]
    public void 同页重排_把第一个拖到最后_落库顺序与序号都正确()
    {
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithTextItems(temp);

        var result = store.ApplyDragDrop(new DragDropRequest(TextPayload(tab, ids[0]), tab.Id, targetIndex: 3));

        Assert.True(result.Success);
        Assert.Equal(new[] { "内容二", "内容三", "内容一" }, tab.EffectiveTextItems.Select(i => i.Text));
        Assert.Equal(new[] { 0, 1, 2 }, tab.EffectiveTextItems.Select(i => i.SortOrder));

        // 重新从磁盘读：顺序与序号都必须一致（"下次启动界面不乱"的唯一保证）
        var reloaded = temp.NewStore().Load().Single(t => t.Id == tab.Id);
        Assert.Equal(new[] { "内容二", "内容三", "内容一" }, reloaded.EffectiveTextItems.Select(i => i.Text));
        Assert.Equal(new[] { 0, 1, 2 }, reloaded.EffectiveTextItems.Select(i => i.SortOrder));
    }

    [Fact]
    public void 同页重排_把最后一个拖到最前()
    {
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithTextItems(temp);

        var result = store.ApplyDragDrop(new DragDropRequest(TextPayload(tab, ids[2]), tab.Id, targetIndex: 0));

        Assert.True(result.Success);
        Assert.Equal(new[] { "内容三", "内容一", "内容二" }, tab.EffectiveTextItems.Select(i => i.Text));
        Assert.Equal(new[] { "内容三", "内容一", "内容二" },
            temp.NewStore().Load().Single(t => t.Id == tab.Id).EffectiveTextItems.Select(i => i.Text));
    }

    [Fact]
    public void 同页重排_原地落下也会把序号重排成连续值()
    {
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithTextItems(temp);

        // 人为把序号弄乱（模拟手工改过的文件 / 旧数据）
        tab.EffectiveTextItems[0].SortOrder = 7;
        tab.EffectiveTextItems[1].SortOrder = 7;

        var result = store.ApplyDragDrop(new DragDropRequest(TextPayload(tab, ids[1]), tab.Id, targetIndex: 1));

        Assert.True(result.Success);
        Assert.Equal(new[] { "内容一", "内容二", "内容三" }, tab.EffectiveTextItems.Select(i => i.Text));
        Assert.Equal(new[] { 0, 1, 2 }, tab.EffectiveTextItems.Select(i => i.SortOrder));
    }

    // ────────────────────────────── 落库：跨页移动 ──────────────────────────────

    [Fact]
    public void 跨页移动_源页摘掉目标页插入_两边序号都连续()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var source = store.AddTab("文本页A", TabModel.TypeText);
        var target = store.AddTab("文本页B", TabModel.TypeText);

        foreach (var text in new[] { "A1", "A2" })
        {
            store.AddTextItem(source.Id, TextItemEditor.Create(null, text)!);
        }

        foreach (var text in new[] { "B1", "B2" })
        {
            store.AddTextItem(target.Id, TextItemEditor.Create(null, text)!);
        }

        var movedId = source.EffectiveTextItems[0].Id;

        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.TextItem, source.Id, movedId), target.Id, targetIndex: 1));

        Assert.True(result.Success);
        Assert.Equal(1, result.Request.TargetIndex);

        Assert.Equal(new[] { "A2" }, source.EffectiveTextItems.Select(i => i.Text));
        Assert.Equal(new[] { "B1", "A1", "B2" }, target.EffectiveTextItems.Select(i => i.Text));

        // ★ 两张页的 sort_order 都要重新连续（源页只剩一条时尤其容易忘）
        Assert.Equal(new[] { 0 }, source.EffectiveTextItems.Select(i => i.SortOrder));
        Assert.Equal(new[] { 0, 1, 2 }, target.EffectiveTextItems.Select(i => i.SortOrder));

        var reloaded = temp.NewStore().Load();
        Assert.Equal(new[] { "B1", "A1", "B2" },
            reloaded.Single(t => t.Id == target.Id).EffectiveTextItems.Select(i => i.Text));
        Assert.Equal(new[] { 0, 1, 2 },
            reloaded.Single(t => t.Id == target.Id).EffectiveTextItems.Select(i => i.SortOrder));
    }

    [Fact]
    public void 跨页移动_落到从没有过文本项的空文本页_目标集合被自愈建出来()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var source = store.AddTab("文本页A", TabModel.TypeText);
        var empty = store.AddTab("空文本页", TabModel.TypeText);

        store.AddTextItem(source.Id, TextItemEditor.Create("备注", "多行\n第二行")!);

        // ⚠️ 新建的文本页 `TextItems` 是 **null**（为了"非文本页的 tabs.json 与原版逐字节一致"，
        //    见 TabModel 的长注释）⇒ 这条用例专门盯 `??=` 那一行漏没漏。
        Assert.Null(empty.TextItems);

        var movedId = source.EffectiveTextItems[0].Id;
        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.TextItem, source.Id, movedId), empty.Id, targetIndex: 0));

        Assert.True(result.Success);
        Assert.Empty(source.EffectiveTextItems);

        var moved = Assert.Single(empty.EffectiveTextItems);
        Assert.Equal("备注", moved.Note);
        Assert.Equal("多行\n第二行", moved.Text);      // ★ 多行原样保留（拖一下不能把换行压没了）
        Assert.Equal(0, moved.SortOrder);

        var reloaded = temp.NewStore().Load();
        Assert.Equal("多行\n第二行", reloaded.Single(t => t.Id == empty.Id).EffectiveTextItems[0].Text);
    }

    [Fact]
    public void 跨页移动_落到末尾时索引被夹取()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var source = store.AddTab("文本页A", TabModel.TypeText);
        var target = store.AddTab("文本页B", TabModel.TypeText);

        store.AddTextItem(source.Id, TextItemEditor.Create(null, "X")!);
        store.AddTextItem(target.Id, TextItemEditor.Create(null, "Y")!);

        var movedId = source.EffectiveTextItems[0].Id;

        // 索引故意给一个远超长度的值（界面在过滤态 / 边界抖动时给得出来）
        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.TextItem, source.Id, movedId), target.Id, targetIndex: 999));

        Assert.True(result.Success);
        Assert.Equal(1, result.Request.TargetIndex);                       // 实际插入下标 = 末尾
        Assert.Equal(new[] { "Y", "X" }, target.EffectiveTextItems.Select(i => i.Text));
    }

    // ────────────────────────────── 门控：三类各找同类页 ──────────────────────────────

    [Fact]
    public void 文本项_来源页或目标页不存在时明确报错()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var tab = store.AddTab("文本页", TabModel.TypeText);
        store.AddTextItem(tab.Id, TextItemEditor.Create(null, "X")!);
        var id = tab.EffectiveTextItems[0].Id;

        var noSource = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.TextItem, "no-such-tab", id), tab.Id, 0));
        var noTarget = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.TextItem, tab.Id, id), "no-such-tab", 0));

        Assert.False(noSource.Success);
        Assert.False(noTarget.Success);
        Assert.Equal(I18n.T("drag.error.source_missing"), noSource.Reason);
        Assert.Equal(I18n.T("drag.error.target_missing"), noTarget.Reason);
    }

    [Fact]
    public void 文本项_在自称的源页里找不到时拒绝且不改数据()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var tab = store.AddTab("文本页", TabModel.TypeText);
        store.AddTextItem(tab.Id, TextItemEditor.Create(null, "X")!);

        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.TextItem, tab.Id, "no-such-item"), tab.Id, 0));

        Assert.False(result.Success);
        Assert.Equal(I18n.T("drag.error.text_item_gone"), result.Reason);
        Assert.Single(temp.NewStore().Load().Single(t => t.Id == tab.Id).EffectiveTextItems);
    }

    [Fact]
    public void 文本项_源页类型不符时拒绝()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var text = store.AddTab("文本页", TabModel.TypeText);
        var grid = store.AddTab("网格页", TabModel.TypeGrid);

        store.AddIcon(grid.Id, new IconModel { DisplayName = "记事本" });
        var iconId = grid.Icons[0].Id;

        // 载荷自称是"文本项"，但源页（token 指向的页）其实是网格页 —— 必须拒绝，
        // 否则会去网格页的 `TextItems`（null）里找图标 id
        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.TextItem, grid.Id, iconId), text.Id, 0));

        Assert.False(result.Success);
        Assert.Equal(I18n.T("drag.error.text_item_gone"), result.Reason);
        Assert.Empty(text.EffectiveTextItems);
        Assert.Single(grid.Icons);
    }

    // ────────────────────────────── 搜索过滤态下的落点换算 ──────────────────────────────

    [Fact]
    public void 过滤态下落点从可见位换算回Core下标()
    {
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithTextItems(temp);

        // 只让第 1 与第 3 条可见（"内容二"不命中 —— 这里直接给定可见清单，判定本身另有单测）
        var visible = new[] { tab.EffectiveTextItems[0], tab.EffectiveTextItems[2] };

        var viewIndex = 1;   // 界面上"插到第二个可见项（内容三）之前"
        var coreIndex = SearchFilter.MapViewIndexToModelIndex(
            visible.Select(i => i.Id).ToList(),
            tab.EffectiveTextItems.Select(i => i.Id).ToList(),
            viewIndex);

        Assert.Equal(2, coreIndex);   // 内容三在 Core 里的下标

        // 把"内容一"拖到 coreIndex=2 ⇒ 期望 [内容二, 内容一, 内容三]
        var result = store.ApplyDragDrop(new DragDropRequest(TextPayload(tab, ids[0]), tab.Id, coreIndex));

        Assert.True(result.Success);
        Assert.Equal(new[] { "内容二", "内容一", "内容三" }, tab.EffectiveTextItems.Select(i => i.Text));
        Assert.Equal(new[] { 0, 1, 2 }, tab.EffectiveTextItems.Select(i => i.SortOrder));
    }

    // ────────────────────────────── 按顺序重排（界面已让位后落库用） ──────────────────────────────

    [Fact]
    public void 按id顺序重排文本项_没提到的项追加到末尾_绝不丢项()
    {
        using var temp = new TempDataDirectory();
        var (store, tab, ids) = StoreWithTextItems(temp);

        Assert.True(store.ReorderTextItems(tab.Id, new[] { ids[2] }));   // 只提到第三条

        Assert.Equal(new[] { "内容三", "内容一", "内容二" }, tab.EffectiveTextItems.Select(i => i.Text));
        Assert.Equal(new[] { 0, 1, 2 }, tab.EffectiveTextItems.Select(i => i.SortOrder));
        Assert.Equal(3, tab.EffectiveTextItems.Count);
    }

    [Fact]
    public void 按id顺序重排文本项_网格页与不存在的页一律拒绝()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var grid = store.AddTab("网格页", TabModel.TypeGrid);
        var text = store.AddTab("文本页", TabModel.TypeText);

        Assert.False(store.ReorderTextItems(grid.Id, new[] { "x" }));        // 不是文本页
        Assert.False(store.ReorderTextItems("no-such-tab", new[] { "x" }));  // 页不存在
        Assert.False(store.ReorderTextItems(text.Id, new[] { "x" }));        // 文本页但还没有任何条目（集合是 null）
    }

    // ────────────────────────────── 逐字节兼容：不碰文本的页一个键都不能多 ──────────────────────────────

    [Fact]
    public void 拖拽之后的文本页写出text_items_网格页仍然不写这个键()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var grid = store.AddTab("网格页", TabModel.TypeGrid);
        var text = store.AddTab("文本页", TabModel.TypeText);

        store.AddIcon(grid.Id, new IconModel { DisplayName = "A" });
        store.AddTextItem(text.Id, TextItemEditor.Create("备注", "内容")!);

        // 拖一下（同页原地落下，只为走一遍落盘路径）
        store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.TextItem, text.Id, text.EffectiveTextItems[0].Id), text.Id, 0));

        var json = File.ReadAllText(temp.TabsFile);

        Assert.Contains("\"text_items\"", json);            // 文本页该有
        Assert.Equal(1, CountOccurrences(json, "\"text_items\""));   // 网格页那条不能有
    }

    private static int CountOccurrences(string text, string needle)
    {
        int count = 0;
        int index = text.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
