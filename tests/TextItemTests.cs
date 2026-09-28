// TextItemTests.cs —— 文本页（粘贴板式表格页）的字段规则、菜单规格、搜索命中与落库往返
//
// 背景：文本页是 **WinUI 线新增的第三种标签页**（`TabModel.TypeText`），原版 Python 只有
// grid / list 两种，所以这一族规则**没有"照原版"可依**，一律照本项目的**列表项**那套
// （`ListItemEditor` / `ListItemContextMenu`）定形，差异处都在下面的测试里逐条钉住。
//
// 与列表项的**唯一实质差异**（刻意）：**文本不能为空**（文本是这一页的本体，
// 空文本的行点了也复制不到东西），而备注可以为空。

using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class TextItemTests
{
    // ────────────────────────────── 字段规则：文本必填、备注可空 ──────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t ")]
    public void 新建_文本为空被拒(string? text)
    {
        // 与列表项刻意不同：列表项允许"两个字段都空就不建"，这里**只认文本**
        Assert.Null(TextItemEditor.Create("有备注但没文本", text));
        Assert.Null(TextItemEditor.Create(null, text));
    }

    [Fact]
    public void 新建_只要文本非空就能建_备注可以空()
    {
        var item = TextItemEditor.Create(null, " 一段要复制的文本 ");

        Assert.NotNull(item);
        Assert.Equal(string.Empty, item!.Note);
        Assert.Equal("一段要复制的文本", item.Text);   // 两端空白去掉
    }

    [Fact]
    public void 新建_两个字段都去两端空白()
    {
        var item = TextItemEditor.Create("  邮箱签名  ", "  敬上\r\n  ");

        Assert.Equal("邮箱签名", item!.Note);
        Assert.Equal("敬上", item.Text);
    }

    [Fact]
    public void 编辑_两个字段都写_备注可以清空()
    {
        var item = new TextItemModel { Note = "旧备注", Text = "旧文本" };

        Assert.True(TextItemEditor.TryApplyEdit(item, null, "  新文本  "));
        Assert.Equal(string.Empty, item.Note);      // 备注被清空（两个字段都写）
        Assert.Equal("新文本", item.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 编辑_文本为空时一个字段都不改(string? text)
    {
        var item = new TextItemModel { Note = "旧备注", Text = "旧文本" };

        Assert.False(TextItemEditor.TryApplyEdit(item, "新备注", text));
        Assert.Equal("旧备注", item.Note);          // ★ 备注也不能被改（不是"部分写入"）
        Assert.Equal("旧文本", item.Text);
    }

    [Fact]
    public void 编辑_目标是null时返回false()
        => Assert.False(TextItemEditor.TryApplyEdit(null, "备注", "文本"));

    // ────────────────────────────── 复制资格与展示口径 ──────────────────────────────

    [Fact]
    public void 只有文本非空才可复制()
    {
        Assert.True(TextItemEditor.CanCopy(new TextItemModel { Text = "x" }));
        Assert.False(TextItemEditor.CanCopy(new TextItemModel { Text = string.Empty }));
        Assert.False(TextItemEditor.CanCopy(null));
    }

    [Fact]
    public void 预览把多行压成一行并合并空白()
    {
        var preview = TextItemEditor.Preview("第一行\n\n第二行\t带  空格  ");

        Assert.DoesNotContain("\n", preview);
        Assert.Equal("第一行 第二行 带 空格", preview);
    }

    [Fact]
    public void 预览超长截断并加省略号()
    {
        var preview = TextItemEditor.Preview(new string('a', 300), maxLength: 10);

        Assert.Equal(new string('a', 10) + "…", preview);
    }

    [Fact]
    public void 称呼_备注优先_其次文本预览_最后此项()
    {
        Assert.Equal("我的备注", TextItemEditor.DisplayNote("  我的备注  ", "文本"));
        Assert.Equal("一段文本", TextItemEditor.DisplayNote(null, "一段文本"));
        Assert.Equal("此项", TextItemEditor.DisplayNote("   ", "   "));
        Assert.Equal("此项", TextItemEditor.DisplayNote(null, null));
    }

    // ────────────────────────────── 菜单规格（照列表页行菜单的形状 + 复制一项） ──────────────────────────────

    [Fact]
    public void 行菜单_三项_编辑复制删除_删除前有分隔线()
    {
        var items = TextItemContextMenu.Build();

        Assert.Equal(3, items.Count);
        Assert.Equal(TextItemMenuAction.EditProperties, items[0].Action);
        Assert.Equal(TextItemMenuAction.Copy, items[1].Action);
        Assert.Equal(TextItemMenuAction.Remove, items[2].Action);

        Assert.False(items[0].SeparatorBefore);
        Assert.False(items[1].SeparatorBefore);
        Assert.True(items[2].SeparatorBefore);
    }

    [Fact]
    public void 行菜单与对话框文案逐字一致()
    {
        Assert.Equal(new[] { "编辑属性…", "复制", "删除" }, TextItemContextMenu.Build().Select(i => i.Label).ToArray());

        Assert.Equal("新建文本项", TextItemEditor.NewItemTitle);        // text.new_item
        Assert.Equal("编辑文本项", TextItemEditor.EditTitle);           // text.edit_item
        Assert.Equal("备注", TextItemEditor.NoteLabel);                 // text.col.note
        Assert.Equal("文本", TextItemEditor.TextLabel);                 // text.col.text
        Assert.Equal("删除文本项", TextItemEditor.DeleteTitle);         // text.delete_title
        Assert.Equal("此项", TextItemEditor.ThisItemText);              // text.this_item
        Assert.Equal("(无备注)", TextItemEditor.UnnamedNote);           // text.unnamed
        Assert.Equal("已复制到剪贴板", TextItemEditor.CopiedText);      // text.copied
        Assert.Equal("文本不能为空", TextItemEditor.ErrorTextRequired); // text.error.text_required
    }

    [Fact]
    public void 对话框与提示文案跟着语言切换()
    {
        var original = I18n.Current;
        try
        {
            I18n.SetLanguage("zh");
            var zh = (TextItemEditor.NewItemTitle, TextItemEditor.NoteLabel, TextItemEditor.CopiedText);

            I18n.SetLanguage("en");
            var en = (TextItemEditor.NewItemTitle, TextItemEditor.NoteLabel, TextItemEditor.CopiedText);

            Assert.NotEqual(zh.Item1, en.Item1);
            Assert.Equal("New Text Item", en.Item1);
            Assert.Equal("Note", en.Item2);
            Assert.Equal("Copied to clipboard", en.Item3);
        }
        finally
        {
            I18n.SetLanguage(original);
        }
    }

    // ────────────────────────────── 搜索过滤 ──────────────────────────────

    [Fact]
    public void 搜索按备注或文本命中_大小写不敏感()
    {
        var item = new TextItemModel { Note = "邮箱签名", Text = "Best Regards, dboycht" };

        Assert.True(SearchFilter.Matches(item, "邮箱"));       // 命中备注
        Assert.True(SearchFilter.Matches(item, "regards"));    // 命中文本（小写）
        Assert.True(SearchFilter.Matches(item, "DBOYCHT"));    // 大小写不敏感
        Assert.False(SearchFilter.Matches(item, "不存在的词"));
        Assert.True(SearchFilter.Matches(item, "   "));        // 空查询一律命中
    }

    [Fact]
    public void 文本页的无匹配提示是另一条文案()
    {
        Assert.Equal("没有匹配的文本项", SearchFilter.NoResultTextText);   // search.no_result_text
        Assert.NotEqual(SearchFilter.NoResultListText, SearchFilter.NoResultTextText);
    }

    // ────────────────────────────── 页类型与 JSON 往返 ──────────────────────────────

    [Fact]
    public void 文本页类型判据_只认text()
    {
        Assert.True(new TabModel { TabType = TabModel.TypeText }.IsTextTab);
        Assert.False(new TabModel { TabType = "grid" }.IsTextTab);
        Assert.False(new TabModel { TabType = "list" }.IsTextTab);
        Assert.False(new TabModel { TabType = "text2" }.IsTextTab);

        // 三种类型互斥：文本页不能同时被当成列表页（否则拖拽门控会放错）
        Assert.False(new TabModel { TabType = TabModel.TypeText }.IsListTab);
    }

    [Fact]
    public void 文本页的JSON字段名是text_items()
    {
        var tab = new TabModel { TabType = TabModel.TypeText, TextItems = new List<TextItemModel>() };
        tab.TextItems.Add(new TextItemModel { Note = "备注", Text = "内容" });

        var json = TabsJson.Serialize(new TabsDocument { Tabs = new List<TabModel> { tab } });

        Assert.Contains("text_items", json);
        Assert.Contains("\"note\"", json);
        Assert.Contains("\"text\": \"内容\"", json);
        Assert.Contains("备注", json);
    }

    [Fact]
    public void 空文本项的页不写出text_items键_保证非文本页与原版逐字节一致()
    {
        // ★ 这条钉的是"跨实现字节一致"这个硬约束（见 TabModel.TextItems 的长注释）：
        //   `text_items` 是原版 Python 没有的字段，一旦给"没有文本项的页"也写上 `"text_items": []`，
        //   网格页/列表页的 tabs.json 就与原版不一致了（DataStoreLoadSaveTests 里三条测试会红）。
        var grid = new TabModel { Id = "g", Name = "主页", Order = 0, TabType = TabModel.TypeGrid };
        var text = new TabModel { Id = "t", Name = "文本", Order = 1, TabType = TabModel.TypeText };

        var json = TabsJson.Serialize(new TabsDocument { Tabs = new List<TabModel> { grid, text } });

        Assert.DoesNotContain("text_items", json);          // 两页都没有文本项 ⇒ 一个键都不写
        Assert.Contains("\"list_items\": []", json);        // 原版本来就有的字段照旧写出来

        // 界面侧的读取口径：null 也要能当空集合用（否则会是一颗会炸的雷）
        Assert.Empty(text.EffectiveTextItems);
    }

    [Fact]
    public void 界面读文本项走EffectiveTextItems_null也安全()
    {
        var tab = new TabModel { TabType = TabModel.TypeText };

        Assert.Null(tab.TextItems);                          // 没人碰过 ⇒ null
        Assert.Empty(tab.EffectiveTextItems);                // 但界面拿到的是空集合

        tab.TextItems = new List<TextItemModel> { new() { Text = "内容" } };
        Assert.Single(tab.EffectiveTextItems);
    }

    [Fact]
    public void 未知类型的页按网格处理_不会崩也不会被当成文本页()
    {
        // 降级方向：旧版读到 "text" 会当网格页；这里反向验证"认不出的类型"不会被误判成文本页
        var tab = new TabModel { TabType = "something-new" };

        Assert.False(tab.IsTextTab);
        Assert.False(tab.IsListTab);
    }

    // ────────────────────────────── 落库往返（增 / 改 / 删 / 重载） ──────────────────────────────

    [Fact]
    public void 文本项增改删_重载后一致_且sort_order连续()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var tab = store.AddTab("文本页", TabModel.TypeText);

        var first = TextItemEditor.Create("第一条", "内容一")!;
        var second = TextItemEditor.Create(null, "内容二")!;

        Assert.True(store.AddTextItem(tab.Id, first));
        Assert.True(store.AddTextItem(tab.Id, second));
        Assert.Equal(new[] { 0, 1 }, store.FindTab(tab.Id)!.EffectiveTextItems.Select(i => i.SortOrder));

        // 编辑属性（改两个字段）
        Assert.True(TextItemEditor.TryApplyEdit(first, "第一条改", "内容一改"));
        store.UpdateTextItem(first.Id, first.Note, first.Text);

        var reloaded = temp.NewStore().Load().Single(t => t.Id == tab.Id);
        Assert.Equal(2, reloaded.EffectiveTextItems.Count);
        Assert.Equal("第一条改", reloaded.EffectiveTextItems[0].Note);
        Assert.Equal("内容一改", reloaded.EffectiveTextItems[0].Text);
        Assert.Equal(string.Empty, reloaded.EffectiveTextItems[1].Note);
        Assert.Equal("内容二", reloaded.EffectiveTextItems[1].Text);

        // 删除中间那条 ⇒ 剩下的重排成 0..N-1
        store.RemoveTextItem(first.Id);
        var afterRemove = temp.NewStore().Load().Single(t => t.Id == tab.Id);
        var only = Assert.Single(afterRemove.EffectiveTextItems);
        Assert.Equal("内容二", only.Text);
        Assert.Equal(0, only.SortOrder);
    }

    [Fact]
    public void 往非文本页加文本项什么都不做()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var grid = store.AddTab("网格页", TabModel.TypeGrid);
        var list = store.AddTab("列表页", TabModel.TypeList);

        Assert.False(store.AddTextItem(grid.Id, TextItemEditor.Create("a", "b")!));
        Assert.False(store.AddTextItem(list.Id, TextItemEditor.Create("a", "b")!));
        Assert.False(store.AddTextItem("不存在的页", TextItemEditor.Create("a", "b")!));

        // 一个都没落进去
        var reloaded = temp.NewStore().Load();
        Assert.All(reloaded, t => Assert.Empty(t.EffectiveTextItems));
    }

    [Fact]
    public void 删页时文本项一起消失()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var keep = store.AddTab("留着", TabModel.TypeGrid);
        var textTab = store.AddTab("文本页", TabModel.TypeText);

        store.AddTextItem(textTab.Id, TextItemEditor.Create("备注", "内容")!);
        Assert.True(store.TryRemoveTab(textTab.Id, out _));

        var reloaded = temp.NewStore().Load();
        Assert.Equal(keep.Id, Assert.Single(reloaded).Id);
        Assert.Empty(reloaded[0].EffectiveTextItems);
    }

    [Fact]
    public void 更新与删除未知id什么都不做()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var tab = store.AddTab("文本页", TabModel.TypeText);
        store.AddTextItem(tab.Id, TextItemEditor.Create("备注", "内容")!);

        store.UpdateTextItem("不存在的-id", "x", "y");
        store.RemoveTextItem("不存在的-id");

        var reloaded = temp.NewStore().Load().Single();
        var item = Assert.Single(reloaded.EffectiveTextItems);
        Assert.Equal("备注", item.Note);
        Assert.Equal("内容", item.Text);
    }

    // ────────────────────────────── 拖拽门控：文本页两类东西都不收 ──────────────────────────────

    [Fact]
    public void 图标与列表项都不能拖进文本页()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var grid = store.AddTab("网格页", TabModel.TypeGrid);
        var list = store.AddTab("列表页", TabModel.TypeList);
        var textTab = store.AddTab("文本页", TabModel.TypeText);

        store.AddIcon(grid.Id, new IconModel { DisplayName = "记事本" });
        store.AddListItem(list.Id, new ListItemModel { Description = "说明", Path = "C:/a.txt" });

        var iconPayload = new DragPayload(DragItemKind.Icon, grid.Id, grid.Icons[0].Id);
        var listPayload = new DragPayload(DragItemKind.ListItem, list.Id, list.ListItems[0].Id);

        var iconResult = store.ApplyDragDrop(new DragDropRequest(iconPayload, textTab.Id, 0));
        var listResult = store.ApplyDragDrop(new DragDropRequest(listPayload, textTab.Id, 0));

        Assert.False(iconResult.Success);
        Assert.False(listResult.Success);

        // ★ 一个字节都不能改：两边集合都没动
        var reloaded = temp.NewStore().Load();
        Assert.Single(reloaded.Single(t => t.Id == grid.Id).Icons);
        Assert.Single(reloaded.Single(t => t.Id == list.Id).ListItems);
        Assert.Empty(reloaded.Single(t => t.Id == textTab.Id).Icons);
        Assert.Empty(reloaded.Single(t => t.Id == textTab.Id).EffectiveTextItems);
    }

    [Fact]
    public void 批量删除不接受文本页()
    {
        var textTab = new TabModel { TabType = TabModel.TypeText };

        Assert.Empty(BulkDelete.ResolveSelection(textTab, new[] { "任意-id" }));
    }

    // ────────────────────────────── 新建文本页的入口（TabEditor / TabContextMenu） ──────────────────────────────

    [Fact]
    public void 新建文本页_留空回落文本默认页名()
    {
        var result = TabEditor.Create(null, TabModel.TypeText);

        Assert.True(result.Success);
        Assert.Equal("文本", result.Name);                  // text.default_name
        Assert.Equal(TabModel.TypeText, result.TabType);
    }

    [Fact]
    public void 新建文本页_名字两端空白被去掉()
    {
        var result = TabEditor.Create("  常用文本  ", TabModel.TypeText);

        Assert.Equal("常用文本", result.Name);
        Assert.Equal(TabModel.TypeText, result.TabType);
    }

    [Theory]
    [InlineData("grid", "grid")]
    [InlineData("list", "list")]
    [InlineData("text", "text")]
    [InlineData("TEXT", "text")]         // 大小写不敏感
    [InlineData("未知类型", "grid")]      // 认不出 ⇒ 网格页（与 TabModel.IsListTab 的宽容口径一致）
    [InlineData(null, "grid")]
    public void 页类型归一化_认不出的按网格页(string? input, string expected)
        => Assert.Equal(expected, TabEditor.NormalizeTabType(input));

    [Fact]
    public void 标签菜单与空白菜单都多了新建文本页()
    {
        Assert.Equal(
            new[]
            {
                TabMenuAction.NewTab, TabMenuAction.NewListTab, TabMenuAction.NewTextTab,
                TabMenuAction.Rename, TabMenuAction.Remove,
            },
            TabContextMenu.BuildForTab().Select(i => i.Action));

        Assert.Equal(
            new[] { TabMenuAction.NewTab, TabMenuAction.NewListTab, TabMenuAction.NewTextTab },
            TabContextMenu.BuildForEmptyArea().Select(i => i.Action));
    }

    [Fact]
    public void 三种默认页名互不相同且都跟着语言()
    {
        var original = I18n.Current;
        try
        {
            I18n.SetLanguage("zh");

            // 三个默认页名必须互不相同（否则新建出来的页会长得一模一样，分不清谁是谁）
            var zh = new[]
            {
                TabContextMenu.DefaultNameFor(TabModel.TypeGrid),
                TabContextMenu.DefaultNameFor(TabModel.TypeList),
                TabContextMenu.DefaultNameFor(TabModel.TypeText),
            };

            Assert.Equal(3, zh.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal("文本", TabContextMenu.DefaultNameFor(TabModel.TypeText));   // text.default_name
            Assert.Equal("新建文本标签页", TabContextMenu.LabelNewTextTab);           // text.new_tab

            I18n.SetLanguage("en");
            Assert.Equal("Text", TabContextMenu.DefaultNameFor(TabModel.TypeText));
            Assert.Equal("New Text Tab", TabContextMenu.LabelNewTextTab);
        }
        finally
        {
            I18n.SetLanguage(original);
        }
    }
}
