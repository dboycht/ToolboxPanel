// BulkDeleteTests.cs —— 批量管理（勾选多项删除）
//
// 原版语义（v1.11.6）：进入批量管理模式 → 逐个勾选 → 「批量删除勾选图标」→ 二次确认 → 删除。
// 这里测两件事：
//   ① 勾选收敛（`BulkDelete.ResolveSelection`）—— 确认框里的数量必须是"真要删的数量"；
//   ② 批量落库（`DataStore.RemoveIcons`）—— **一次落盘**、连带删缓存、序号重排、未知 id 忽略。
// 外加一条保真测试：6 条文案与原版 i18n **逐字一致**。

using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class BulkDeleteTests
{
    // ────────────────────────────── 文案（照原版 i18n 保真） ──────────────────────────────

    [Fact]
    public void 文案与原版i18n逐字一致()
    {
        Assert.Equal("批量管理", BulkDelete.MenuLabel);                              // app.menu.batch
        Assert.Equal("批量删除勾选图标", BulkDelete.DeleteLabel);                     // app.menu.batch_delete
        Assert.Equal("未选中任何图标", BulkDelete.NoneCheckedText);                   // batch.none_checked
        Assert.Equal("批量删除图标", BulkDelete.ConfirmTitle);                        // bulk_delete.title
        Assert.Equal("确定要删除选中的 3 个图标吗？", BulkDelete.ConfirmText(3));      // bulk_delete.confirm
        Assert.Equal("已删除 3 个图标", BulkDelete.DoneText(3));                      // bulk_delete.done
        Assert.Equal("批量管理模式：勾选图标后点击「批量删除勾选图标」", BulkDelete.StatusOnText);   // status.batch_mode_on
    }

    // ────────────────────────────── 勾选收敛 ──────────────────────────────

    [Fact]
    public void 收敛_只保留该页真实存在的id_并去重()
    {
        var tab = GridTab(("a", 0), ("b", 1), ("c", 2));

        var resolved = BulkDelete.ResolveSelection(tab, new[] { "b", "b", "幽灵id", "a" });

        Assert.Equal(new[] { "a", "b" }, resolved);   // 按**页面顺序**，去重，丢掉不存在的
    }

    [Fact]
    public void 收敛_空勾选或空页返回空()
    {
        var tab = GridTab(("a", 0));

        Assert.Empty(BulkDelete.ResolveSelection(tab, Array.Empty<string>()));
        Assert.Empty(BulkDelete.ResolveSelection(tab, null));
        Assert.Empty(BulkDelete.ResolveSelection(null, new[] { "a" }));
    }

    [Fact]
    public void 收敛_列表页不收图标()
    {
        var listTab = new TabModel { Id = "L", Name = "列表页", TabType = "list" };

        Assert.Empty(BulkDelete.ResolveSelection(listTab, new[] { "a" }));
    }

    // ────────────────────────────── 批量落库 ──────────────────────────────

    [Fact]
    public void 批量删除_删掉全部缓存与序号重排_且重载后一致()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();

        var tab = store.AddTab("页", TabModel.TypeGrid);

        for (int i = 0; i < 4; i++)
        {
            var icon = new IconModel { Id = $"i{i}", DisplayName = $"图标{i}" };
            store.AddIcon(tab.Id, icon);
            // 每个图标都造一份真实存在的缓存文件
            var cacheName = $"{icon.Id}.png";
            File.WriteAllBytes(Path.Combine(temp.IconsDirectory, cacheName), new byte[] { 1, 2, 3 });
            icon.IconCacheFile = cacheName;
        }

        var removed = store.RemoveIcons(tab.Id, new[] { "i1", "i3" });

        Assert.Equal(2, removed.Count);
        Assert.Equal(new[] { "i1", "i3" }, removed.Select(i => i.Id));
        Assert.False(File.Exists(Path.Combine(temp.IconsDirectory, "i1.png")));   // 缓存连带走
        Assert.False(File.Exists(Path.Combine(temp.IconsDirectory, "i3.png")));
        Assert.True(File.Exists(Path.Combine(temp.IconsDirectory, "i0.png")));    // 没删的不动

        // 重载：磁盘上只剩两个，且 sort_order 重新连续
        var reloaded = temp.NewStore().Load().Single(t => t.Id == tab.Id);
        Assert.Equal(new[] { "i0", "i2" }, reloaded.Icons.Select(i => i.Id));
        Assert.Equal(new[] { 0, 1 }, reloaded.Icons.Select(i => i.SortOrder));
    }

    [Fact]
    public void 批量删除_未知id被忽略_不影响其它()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var tab = store.AddTab("页", TabModel.TypeGrid);
        store.AddIcon(tab.Id, new IconModel { Id = "a", DisplayName = "A" });
        store.AddIcon(tab.Id, new IconModel { Id = "b", DisplayName = "B" });

        var removed = store.RemoveIcons(tab.Id, new[] { "幽灵", "a" });

        Assert.Single(removed);
        Assert.Equal("a", removed[0].Id);
        Assert.Equal(new[] { "b" }, temp.NewStore().Load().Single(t => t.Id == tab.Id).Icons.Select(i => i.Id));
    }

    [Fact]
    public void 批量删除_页不存在或列表页或全不匹配_什么都不做()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();

        var grid = store.AddTab("网格", TabModel.TypeGrid);
        var list = store.AddTab("列表", TabModel.TypeList);
        store.AddIcon(grid.Id, new IconModel { Id = "a", DisplayName = "A" });

        var before = File.ReadAllText(temp.TabsFile);

        Assert.Empty(store.RemoveIcons("不存在", new[] { "a" }));      // 页不存在
        Assert.Empty(store.RemoveIcons(list.Id, new[] { "a" }));       // 列表页不收图标
        Assert.Empty(store.RemoveIcons(grid.Id, new[] { "幽灵" }));     // 一个都没匹配上
        Assert.Empty(store.RemoveIcons(grid.Id, null));                // 空勾选

        // ⚠️ 这几条都必须是"一个字节都没动"（含不落盘）
        Assert.Equal(before, File.ReadAllText(temp.TabsFile));
    }

    [Fact]
    public void 批量删除_只影响目标页_另一页图标不动()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();

        var first = store.AddTab("第一页", TabModel.TypeGrid);
        var second = store.AddTab("第二页", TabModel.TypeGrid);
        store.AddIcon(first.Id, new IconModel { Id = "a", DisplayName = "第一页的A" });
        store.AddIcon(second.Id, new IconModel { Id = "b", DisplayName = "第二页的B" });

        var removed = store.RemoveIcons(first.Id, new[] { "a", "b" });   // 故意把另一页的 id 也塞进来

        Assert.Single(removed);
        Assert.Equal("a", removed[0].Id);

        var reloaded = temp.NewStore().Load();
        Assert.Empty(reloaded.Single(t => t.Id == first.Id).Icons);
        Assert.Single(reloaded.Single(t => t.Id == second.Id).Icons);
    }

    // ────────────────────────────── 工具 ──────────────────────────────

    private static TabModel GridTab(params (string Id, int Order)[] icons)
    {
        var tab = new TabModel { Id = "T", Name = "页", TabType = "grid" };

        foreach (var (id, order) in icons)
        {
            tab.Icons.Add(new IconModel { Id = id, DisplayName = id, SortOrder = order });
        }

        return tab;
    }
}
