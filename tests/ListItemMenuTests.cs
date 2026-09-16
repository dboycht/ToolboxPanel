// ListItemMenuTests.cs —— 列表页行右键菜单 + 列表项字段规则
//
// 原版基准（v1.11.6 `list_tab_page.py`）：行菜单 = 编辑属性… / 重命名 / ─ / 删除（**没有"打开"**）；
// 空白处右键 = 直接弹「新建列表项」；"说明与路径都为空 ⇒ 什么也不做"。
// 这里把这些规则与文案钉住，并做一次落库往返。

using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class ListItemMenuTests
{
    // ────────────────────────────── 菜单规格（照原版） ──────────────────────────────

    [Fact]
    public void 行菜单_三项且顺序与分隔线照原版()
    {
        var items = ListItemContextMenu.Build();

        Assert.Equal(3, items.Count);
        Assert.Equal(ListItemMenuAction.EditProperties, items[0].Action);
        Assert.Equal(ListItemMenuAction.Rename, items[1].Action);
        Assert.Equal(ListItemMenuAction.Remove, items[2].Action);

        // ⚠️ 原版 row 菜单**没有「打开」**（点行本身就是打开）
        Assert.DoesNotContain(items, i => i.Label.Contains("打开"));

        Assert.False(items[0].SeparatorBefore);
        Assert.False(items[1].SeparatorBefore);
        Assert.True(items[2].SeparatorBefore);      // 删除前有分隔线
    }

    [Fact]
    public void 行菜单_文案与原版i18n逐字一致()
    {
        var labels = ListItemContextMenu.Build().Select(i => i.Label).ToArray();

        Assert.Equal(new[] { "编辑属性…", "重命名", "删除" }, labels);   // icon.menu.edit / rename / remove
    }

    [Fact]
    public void 对话框与提示文案与原版i18n逐字一致()
    {
        Assert.Equal("新建列表项", ListItemEditor.NewItemTitle);       // list.new_item
        Assert.Equal("编辑列表项", ListItemEditor.EditTitle);          // list.edit_item
        Assert.Equal("说明", ListItemEditor.DescriptionLabel);         // list.col.desc
        Assert.Equal("路径", ListItemEditor.PathLabel);                // list.col.path
        Assert.Equal("文本说明", ListItemEditor.DescriptionPlaceholder);   // list.desc_ph
        Assert.Equal("选择文件", ListItemEditor.SelectFileLabel);       // list.select_file
        Assert.Equal("选择文件夹", ListItemEditor.SelectFolderLabel);   // list.select_folder
        Assert.Equal("删除列表项", ListItemEditor.DeleteTitle);         // list.delete_title
        Assert.Equal("此行", ListItemEditor.ThisRowText);              // list.this_row
        Assert.Equal("说明已更新", ListItemEditor.DescriptionUpdatedText);   // list.desc_updated
        Assert.Equal("路径已更新", ListItemEditor.PathUpdatedText);     // list.path_updated
        Assert.Equal("已添加列表项: 我的说明", ListItemEditor.ItemAddedText("我的说明"));   // list.item_added
        Assert.Equal("已删除: 我的说明", ListItemEditor.RemovedText("我的说明"));           // status.removed
        Assert.Equal("确定要删除列表项「我的说明」吗？", ListItemEditor.DeleteConfirmText("我的说明"));   // list.confirm_delete
    }

    // ────────────────────────────── 新建 / 编辑 / 重命名 ──────────────────────────────

    [Fact]
    public void 新建_两者都空返回null_否则字段都trim()
    {
        Assert.Null(ListItemEditor.Create("", null));
        Assert.Null(ListItemEditor.Create("   ", "  "));

        var created = ListItemEditor.Create("  说明  ", "  C:/x.txt  ");
        Assert.NotNull(created);
        Assert.Equal("说明", created!.Description);
        Assert.Equal("C:/x.txt", created.Path);

        // 只填一个也算（原版语义）
        Assert.NotNull(ListItemEditor.Create("只有说明", ""));
        Assert.NotNull(ListItemEditor.Create(null, "只有路径"));
    }

    [Fact]
    public void 编辑_两者都空不改_否则两个字段都写()
    {
        var item = new ListItemModel { Description = "旧说明", Path = "C:/old.txt" };

        Assert.False(ListItemEditor.TryApplyEdit(item, "  ", null));
        Assert.Equal("旧说明", item.Description);   // 一个字都没动
        Assert.Equal("C:/old.txt", item.Path);

        // 只留路径、把说明清空：**允许**（原版也是两个字段都写）
        Assert.True(ListItemEditor.TryApplyEdit(item, "", "C:/new.txt"));
        Assert.Equal("", item.Description);
        Assert.Equal("C:/new.txt", item.Path);
    }

    [Fact]
    public void 重命名_只改说明不碰路径_说明必填()
    {
        var item = new ListItemModel { Description = "旧说明", Path = "C:/keep.txt" };

        Assert.False(ListItemEditor.TryRename(item, "   "));
        Assert.Equal("旧说明", item.Description);

        Assert.True(ListItemEditor.TryRename(item, "  新说明  "));
        Assert.Equal("新说明", item.Description);
        Assert.Equal("C:/keep.txt", item.Path);     // 路径没动
    }

    [Fact]
    public void 说明为空时_确认文案用此行兜底()
    {
        Assert.Equal("此行", ListItemEditor.DisplayName(null));
        Assert.Equal("此行", ListItemEditor.DisplayName("   "));
        Assert.Equal("确定要删除列表项「此行」吗？", ListItemEditor.DeleteConfirmText(""));
    }

    // ────────────────────────────── 落库往返（增 / 改 / 重命名 / 删） ──────────────────────────────

    [Fact]
    public void 列表项增改删_重载后一致()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var tab = store.AddTab("列表页", TabModel.TypeList);

        // 新建
        var created = ListItemEditor.Create("说明一", "C:/a.txt");
        Assert.NotNull(created);
        store.AddListItem(tab.Id, created!);

        // 编辑属性（改两个字段）
        Assert.True(ListItemEditor.TryApplyEdit(created, "说明一改", "C:/b.txt"));
        store.UpdateListItem(created.Id, created.Description, created.Path);

        // 重命名（只改说明）
        Assert.True(ListItemEditor.TryRename(created, "说明二"));
        store.UpdateListItem(created.Id, created.Description, null);

        var reloaded = temp.NewStore().Load().Single(t => t.Id == tab.Id);
        var item = Assert.Single(reloaded.ListItems);
        Assert.Equal("说明二", item.Description);
        Assert.Equal("C:/b.txt", item.Path);

        // 删除
        store.RemoveListItem(item.Id);
        Assert.Empty(temp.NewStore().Load().Single(t => t.Id == tab.Id).ListItems);
    }
}
