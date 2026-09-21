// DataStoreCrudTests.cs —— 增删改查、排序、缓存清理（W1）

using System.Text;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class DataStoreCrudTests
{
    // ────────────────────────────── 标签页 ──────────────────────────────

    [Fact]
    public void AddTab_顺序号取当前数量且立刻落盘()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();

        var added = store.AddTab("第二页");
        var added2 = store.AddTab("第三页", TabModel.TypeList);

        Assert.Equal(1, added.Order);
        Assert.Equal(2, added2.Order);
        Assert.Equal("list", store.FindTab(added2.Id)!.TabType);

        var reloaded = temp.NewStore().Load();
        Assert.Equal(new[] { "Home", "第二页", "第三页" }, reloaded.Select(t => t.Name));
    }

    [Fact]
    public void RenameTab_落盘()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("旧名");

        store.RenameTab(tab.Id, "新名");

        Assert.Equal("新名", temp.NewStore().Load().Single(t => t.Id == tab.Id).Name);
    }

    [Fact]
    public void RemoveTab_连带删除其图标缓存并重排order()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var a = store.AddTab("A");
        var b = store.AddTab("B");
        var c = store.AddTab("C");

        temp.TouchIconCache("a-icon.png");
        store.AddIcon(a.Id, new IconModel { DisplayName = "x", IconCacheFile = "a-icon.png" });
        Assert.True(File.Exists(Path.Combine(temp.IconsDirectory, "a-icon.png")));

        store.RemoveTab(a.Id);

        Assert.Null(store.FindTab(a.Id));
        Assert.False(File.Exists(Path.Combine(temp.IconsDirectory, "a-icon.png")));
        // 剩下 Home / B / C，重排后依次 0/1/2（Home 仍在最前）
        Assert.Equal(0, store.Tabs[0].Order);
        Assert.Equal(1, store.FindTab(b.Id)!.Order);
        Assert.Equal(2, store.FindTab(c.Id)!.Order);
    }

    [Fact]
    public void ReorderTabs_移动并重排order()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var a = store.AddTab("A");
        var b = store.AddTab("B");

        store.ReorderTabs(0, 2);          // Home 移到最后

        Assert.Equal(new[] { a.Id, b.Id, store.Tabs[2].Id }, store.Tabs.Select(t => t.Id));
        Assert.Equal(new[] { 0, 1, 2 }, store.Tabs.Select(t => t.Order));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 99)]
    [InlineData(5, 0)]
    public void ReorderTabs_越界时什么都不做(int from, int to)
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        store.AddTab("A");
        var before = store.Tabs.Select(t => t.Id).ToArray();

        store.ReorderTabs(from, to);

        Assert.Equal(before, store.Tabs.Select(t => t.Id));
    }

    // ────────────────────────────── 图标 ──────────────────────────────

    [Fact]
    public void AddIcon_顺序号取当前数量()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.Tabs[0];

        store.AddIcon(tab.Id, new IconModel { DisplayName = "一" });
        store.AddIcon(tab.Id, new IconModel { DisplayName = "二" });

        Assert.Equal(new[] { 0, 1 }, tab.Icons.Select(i => i.SortOrder));
        Assert.Equal(2, temp.NewStore().Load()[0].Icons.Count);
    }

    [Fact]
    public void AddIcon_标签页不存在时什么都不做()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();

        store.AddIcon("不存在的页", new IconModel());

        Assert.Empty(store.Tabs[0].Icons);
    }

    [Fact]
    public void RemoveIcon_连带删除缓存文件()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.Tabs[0];

        temp.TouchIconCache("i.png");
        var icon = new IconModel { DisplayName = "x", IconCacheFile = "i.png" };
        store.AddIcon(tab.Id, icon);

        store.RemoveIcon(icon.Id);

        Assert.Empty(store.Tabs[0].Icons);
        Assert.False(File.Exists(Path.Combine(temp.IconsDirectory, "i.png")));
    }

    [Fact]
    public void ReorderIcon_同页重排并重编号()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.Tabs[0];
        store.AddIcon(tab.Id, new IconModel { DisplayName = "一" });
        store.AddIcon(tab.Id, new IconModel { DisplayName = "二" });
        store.AddIcon(tab.Id, new IconModel { DisplayName = "三" });

        store.ReorderIcon(tab.Id, 0, 2);

        Assert.Equal(new[] { "二", "三", "一" }, tab.Icons.Select(i => i.DisplayName));
        Assert.Equal(new[] { 0, 1, 2 }, tab.Icons.Select(i => i.SortOrder));
    }

    [Fact]
    public void ReorderIcon_原地移动不动()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.Tabs[0];
        store.AddIcon(tab.Id, new IconModel { DisplayName = "一" });
        store.AddIcon(tab.Id, new IconModel { DisplayName = "二" });

        store.ReorderIcon(tab.Id, 1, 1);

        Assert.Equal(new[] { "一", "二" }, tab.Icons.Select(i => i.DisplayName));
    }

    [Fact]
    public void ReorderIcon_源索引越界不动_目标索引越界追加到末尾()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.Tabs[0];
        store.AddIcon(tab.Id, new IconModel { DisplayName = "一" });
        store.AddIcon(tab.Id, new IconModel { DisplayName = "二" });

        store.ReorderIcon(tab.Id, 5, 0);                       // 源越界 → 什么都不做
        Assert.Equal(new[] { "一", "二" }, tab.Icons.Select(i => i.DisplayName));

        // ⚠️ 目标越界时 Python 的 list.insert(9, x) 等价于「追加到末尾」，这里刻意保持同样语义
        store.ReorderIcon(tab.Id, 0, 9);
        Assert.Equal(new[] { "二", "一" }, tab.Icons.Select(i => i.DisplayName));
        Assert.Equal(new[] { 0, 1 }, tab.Icons.Select(i => i.SortOrder));
    }

    [Fact]
    public void MoveIcon_跨页移动后两个页都重编号()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var source = store.Tabs[0];
        var target = store.AddTab("目标");

        var i1 = new IconModel { DisplayName = "一" };
        var i2 = new IconModel { DisplayName = "二" };
        var i3 = new IconModel { DisplayName = "三" };
        store.AddIcon(source.Id, i1);
        store.AddIcon(source.Id, i2);
        store.AddIcon(target.Id, i3);

        store.MoveIcon(i2.Id, target.Id, 0);

        Assert.Equal(new[] { "一" }, source.Icons.Select(i => i.DisplayName));
        Assert.Equal(new[] { "二", "三" }, target.Icons.Select(i => i.DisplayName));
        Assert.Equal(new[] { 0 }, source.Icons.Select(i => i.SortOrder));
        Assert.Equal(new[] { 0, 1 }, target.Icons.Select(i => i.SortOrder));
    }

    [Fact]
    public void MoveIcon_目标页不存在时放回原处()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var source = store.Tabs[0];
        var icon = new IconModel { DisplayName = "一" };
        store.AddIcon(source.Id, icon);

        store.MoveIcon(icon.Id, "不存在的目标页", 0);

        Assert.Single(source.Icons);
        Assert.Equal(icon.Id, source.Icons[0].Id);
    }

    [Fact]
    public void RenameIcon_落盘()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var icon = new IconModel { DisplayName = "旧" };
        store.AddIcon(store.Tabs[0].Id, icon);

        store.RenameIcon(icon.Id, "新");

        Assert.Equal("新", temp.NewStore().Load()[0].Icons[0].DisplayName);
    }

    // ────────────────────────────── 列表项 ──────────────────────────────

    [Fact]
    public void AddListItem_顺序号取当前数量()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("列表页", TabModel.TypeList);

        store.AddListItem(tab.Id, new ListItemModel { Description = "一", Path = @"D:\1" });
        store.AddListItem(tab.Id, new ListItemModel { Description = "二", Path = @"D:\2" });

        Assert.Equal(new[] { 0, 1 }, tab.ListItems.Select(i => i.SortOrder));
    }

    [Fact]
    public void UpdateListItem_只改传入的字段()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("列表页", TabModel.TypeList);
        var item = new ListItemModel { Description = "说明", Path = @"D:\path" };
        store.AddListItem(tab.Id, item);

        store.UpdateListItem(item.Id, description: "新说明");

        Assert.Equal("新说明", item.Description);
        Assert.Equal(@"D:\path", item.Path);          // path 没传 → 保持原值

        store.UpdateListItem(item.Id, path: @"D:\other");

        Assert.Equal("新说明", item.Description);
        Assert.Equal(@"D:\other", item.Path);
    }

    [Fact]
    public void RemoveListItem_落盘()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("列表页", TabModel.TypeList);
        var item = new ListItemModel { Description = "一" };
        store.AddListItem(tab.Id, item);

        store.RemoveListItem(item.Id);

        Assert.Empty(temp.NewStore().Load()[0].ListItems);
    }

    [Fact]
    public void ReorderListItems_按给定id重排并重编号()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("列表页", TabModel.TypeList);
        var a = new ListItemModel { Description = "A" };
        var b = new ListItemModel { Description = "B" };
        var c = new ListItemModel { Description = "C" };
        store.AddListItem(tab.Id, a);
        store.AddListItem(tab.Id, b);
        store.AddListItem(tab.Id, c);

        store.ReorderListItems(tab.Id, new[] { c.Id, a.Id, b.Id });

        Assert.Equal(new[] { "C", "A", "B" }, tab.ListItems.Select(i => i.Description));
        Assert.Equal(new[] { 0, 1, 2 }, tab.ListItems.Select(i => i.SortOrder));
    }

    [Fact]
    public void ReorderListItems_未出现在参数里的项追加到末尾_绝不丢项()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("列表页", TabModel.TypeList);
        var a = new ListItemModel { Description = "A" };
        var b = new ListItemModel { Description = "B" };
        var c = new ListItemModel { Description = "C" };
        store.AddListItem(tab.Id, a);
        store.AddListItem(tab.Id, b);
        store.AddListItem(tab.Id, c);

        store.ReorderListItems(tab.Id, new[] { c.Id });   // 只提到 C

        Assert.Equal(new[] { "C", "A", "B" }, tab.ListItems.Select(i => i.Description));
        Assert.Equal(3, tab.ListItems.Count);
    }

    // ────────────────────────────── 孤儿缓存 ──────────────────────────────

    [Fact]
    public void OrphanCacheFiles_只报没被引用的文件()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        temp.TouchIconCache("used.png");
        temp.TouchIconCache("orphan1.png");
        temp.TouchIconCache("orphan2.png");
        store.AddIcon(store.Tabs[0].Id, new IconModel { IconCacheFile = "used.png" });

        var orphans = store.OrphanCacheFiles();

        Assert.Equal(2, orphans.Count);
        Assert.Contains("orphan1.png", orphans);
        Assert.Contains("orphan2.png", orphans);
        Assert.DoesNotContain("used.png", orphans);
    }

    [Fact]
    public void CleanOrphanCache_只删孤儿文件()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        temp.TouchIconCache("used.png");
        temp.TouchIconCache("orphan.png");
        store.AddIcon(store.Tabs[0].Id, new IconModel { IconCacheFile = "used.png" });

        store.CleanOrphanCache();

        Assert.True(File.Exists(Path.Combine(temp.IconsDirectory, "used.png")));
        Assert.False(File.Exists(Path.Combine(temp.IconsDirectory, "orphan.png")));
    }

    [Fact]
    public void 隐藏文件不算孤儿缓存_格式标记不会被删掉()
    {
        // `icons/.cache-format` 是图标缓存的格式标记（见 IconExtractor.CacheFormat）：
        // 它当然不被任何图标引用，但绝不能被"孤儿清理"删掉 —— 删了每次启动都会重提全部图标。
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        temp.TouchIconCache(IconExtractor.CacheFormatFileName);
        temp.TouchIconCache("orphan.png");

        Assert.DoesNotContain(IconExtractor.CacheFormatFileName, store.OrphanCacheFiles());
        Assert.Contains("orphan.png", store.OrphanCacheFiles());

        store.CleanOrphanCache();

        Assert.True(File.Exists(Path.Combine(temp.IconsDirectory, IconExtractor.CacheFormatFileName)));
        Assert.False(File.Exists(Path.Combine(temp.IconsDirectory, "orphan.png")));
    }

    [Fact]
    public void 每次修改都立刻落盘_文件内容随操作更新()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();

        var tab = store.AddTab("落盘验证");
        var onDiskAfterAddTab = File.ReadAllText(temp.TabsFile, Encoding.UTF8);
        Assert.Contains("落盘验证", onDiskAfterAddTab);

        store.RenameTab(tab.Id, "改名后");
        Assert.Contains("改名后", File.ReadAllText(temp.TabsFile, Encoding.UTF8));

        var icon = new IconModel { DisplayName = "图标" };
        store.AddIcon(tab.Id, icon);
        Assert.Contains("图标", File.ReadAllText(temp.TabsFile, Encoding.UTF8));

        store.RemoveIcon(icon.Id);
        Assert.DoesNotContain("图标", File.ReadAllText(temp.TabsFile, Encoding.UTF8));
    }
}
