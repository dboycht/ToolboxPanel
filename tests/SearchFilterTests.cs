// SearchFilterTests.cs —— 搜索过滤（W5）
//
// 覆盖两件"错了用户一定会看出来"的事：
//   ① **命中判定**：图标按名称/来源路径/目标路径，列表项按说明/路径；空查询 = 全部可见；
//   ② ★ **过滤视图的落点索引 → Core 列表索引** 的换算 —— 过滤态下拖动排序时，
//      界面上算出来的"第几个可见位"必须换回 Core 的下标，否则会插到别的图标后面去。
// ②这一条还有一条**走真实 DataStore 的集成测试**（落库后顺序 + 磁盘顺序），
// 因为"换算对了但落库口径不对"同样会出错（TargetIndex 的口径是"插到当前第 N 项之前"）。

using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class SearchFilterTests
{
    // ────────────────────────────── 文案（照原版 i18n） ──────────────────────────────

    [Fact]
    public void 文案与原版i18n一致()
    {
        Assert.Equal("搜索图标…", SearchFilter.PlaceholderText);      // search.placeholder
        Assert.Equal("没有匹配的图标", SearchFilter.NoResultIconText);  // search.no_result
        Assert.Equal("匹配 3 / 19", SearchFilter.CountText(3, 19));
    }

    // ────────────────────────────── 命中：图标 ──────────────────────────────

    private static IconModel Icon(string name = "", string source = "", string target = "", IconType type = IconType.File)
        => new() { DisplayName = name, SourcePath = source, TargetPath = target, Type = type };

    [Fact]
    public void 图标_空查询一律命中()
    {
        var icon = Icon("记事本", @"C:\Windows\notepad.exe");

        Assert.True(SearchFilter.Matches(icon, null));
        Assert.True(SearchFilter.Matches(icon, string.Empty));
        Assert.True(SearchFilter.Matches(icon, "   "));
    }

    [Fact]
    public void 图标_按名称子串命中()
    {
        var icon = Icon("记事本", @"C:\Windows\notepad.exe");

        Assert.True(SearchFilter.Matches(icon, "记"));
        Assert.True(SearchFilter.Matches(icon, "记事"));
        Assert.False(SearchFilter.Matches(icon, "画图"));
    }

    [Fact]
    public void 图标_大小写不敏感()
    {
        var icon = Icon("Notepad", @"C:\Windows\System32\notepad.exe");

        Assert.True(SearchFilter.Matches(icon, "notepad"));
        Assert.True(SearchFilter.Matches(icon, "NOTEPAD"));
    }

    [Fact]
    public void 图标_按来源路径命中()
    {
        var icon = Icon("记事本", @"C:\Windows\System32\notepad.exe");

        Assert.True(SearchFilter.Matches(icon, @"system32"));
        Assert.True(SearchFilter.Matches(icon, "SYSTEM32"));
        Assert.True(SearchFilter.Matches(icon, ".exe"));
    }

    [Fact]
    public void 图标_按目标路径命中_快捷方式()
    {
        // 快捷方式：界面上显示的名字可能与真实目标毫无关系，所以目标路径也要能搜到
        var icon = Icon("我的工具", @"C:\Users\me\Desktop\工具.lnk", @"D:\tools\special-app.exe", IconType.Shortcut);

        Assert.True(SearchFilter.Matches(icon, "special-app"));
        Assert.True(SearchFilter.Matches(icon, "tools"));
    }

    [Fact]
    public void 图标_名称与路径都不含时不命中()
    {
        var icon = Icon("记事本", @"C:\Windows\notepad.exe");

        Assert.False(SearchFilter.Matches(icon, "计算器"));
        Assert.False(SearchFilter.Matches(icon, "xyzw"));
    }

    [Fact]
    public void 图标_查询首尾空白忽略()
    {
        var icon = Icon("记事本", @"C:\Windows\notepad.exe");

        Assert.True(SearchFilter.Matches(icon, "   记事本  "));
    }

    [Fact]
    public void 图标_没写名字时路径仍能搜到()
    {
        // 界面上显示的是路径的主干名（IconTileViewModel.ResolveName 的兜底）——
        // 那条兜底名一定来自路径，所以"按路径命中"已经覆盖了它，无需在 Core 里再实现一遍取名逻辑。
        var icon = Icon(string.Empty, @"C:\Windows\notepad.exe");

        Assert.True(SearchFilter.Matches(icon, "notepad"));
    }

    [Fact]
    public void 图标_特殊字符按字面匹配_不抛异常()
    {
        var icon = Icon("括号(测试)", @"C:\a[b]\c.exe");

        Assert.True(SearchFilter.Matches(icon, "("));
        Assert.True(SearchFilter.Matches(icon, "[b]"));
        Assert.True(SearchFilter.Matches(icon, @"a[b]"));
        Assert.False(SearchFilter.Matches(icon, "a.b"));   // 不是正则，点号只当字面量
    }

    // ────────────────────────────── 命中：列表项 ──────────────────────────────

    [Fact]
    public void 列表项_按说明或路径命中()
    {
        var item = new ListItemModel { Description = "项目源码", Path = @"D:\code\MyProject" };

        Assert.True(SearchFilter.Matches(item, "源码"));
        Assert.True(SearchFilter.Matches(item, "myproject"));       // 大小写不敏感
        Assert.True(SearchFilter.Matches(item, @"D:\code"));
        Assert.False(SearchFilter.Matches(item, "下载"));
    }

    [Fact]
    public void 列表项_空查询一律命中()
    {
        var item = new ListItemModel { Description = "项目源码", Path = @"D:\code" };

        Assert.True(SearchFilter.Matches(item, null));
        Assert.True(SearchFilter.Matches(item, "  "));
    }

    [Fact]
    public void 查询是否生效()
    {
        Assert.False(SearchFilter.IsActive(null));
        Assert.False(SearchFilter.IsActive(string.Empty));
        Assert.False(SearchFilter.IsActive("   "));
        Assert.True(SearchFilter.IsActive(" a "));
    }

    // ────────────────────────────── ★ 视图索引 → Core 索引 ──────────────────────────────

    private static readonly string[] All = { "a", "b", "c", "d" };

    [Fact]
    public void 换算_没过滤时是恒等映射()
    {
        // 可见 = 全部 ⇒ 调用方可以无条件走这一层，结果与直接传索引完全一样
        for (int i = 0; i <= All.Length; i++)
        {
            Assert.Equal(i, SearchFilter.MapViewIndexToModelIndex(All, All, i));
        }
    }

    [Fact]
    public void 换算_过滤子集时按可见项在Core里的下标()
    {
        // 过滤出 [a, c]（b、d 不命中）
        var visible = new[] { "a", "c" };

        Assert.Equal(0, SearchFilter.MapViewIndexToModelIndex(visible, All, 0));   // a → 0
        Assert.Equal(2, SearchFilter.MapViewIndexToModelIndex(visible, All, 1));   // c → 2
    }

    [Fact]
    public void 换算_落在最后一个可见项之后_是可见末尾而不是整页末尾()
    {
        // ⚠️ 这一条最容易搞错：过滤态下的"放到最后"只应到"最后一个可见项之后"（c 之后 = 下标 3），
        //    而不是整页末尾（4）—— 否则被拖的图标会越过看不见的项，观感与落点不符。
        var visible = new[] { "a", "c" };

        Assert.Equal(3, SearchFilter.MapViewIndexToModelIndex(visible, All, 2));
        Assert.Equal(3, SearchFilter.MapViewIndexToModelIndex(visible, All, 99));
    }

    [Fact]
    public void 换算_没有可见项时兜底为0()
    {
        Assert.Equal(0, SearchFilter.MapViewIndexToModelIndex(Array.Empty<string>(), All, 0));
        Assert.Equal(0, SearchFilter.MapViewIndexToModelIndex(Array.Empty<string>(), All, 7));
    }

    [Fact]
    public void 换算_脏数据不抛异常()
    {
        // 可见 id 不在 Core 列表里（理论上不该出现）：给出确定的兜底值，绝不抛异常
        var visible = new[] { "幽灵", "c" };

        Assert.Equal(0, SearchFilter.MapViewIndexToModelIndex(visible, All, 0));    // 查不到 → 退到最前
        Assert.Equal(2, SearchFilter.MapViewIndexToModelIndex(visible, All, 1));    // c 在 All 里的下标
        Assert.Equal(3, SearchFilter.MapViewIndexToModelIndex(visible, All, 5));    // 最后一个可见项之后
    }

    // ────────────────────────────── 集成：过滤态下拖动落库 ──────────────────────────────

    /// <summary>建一个 grid 页 + 一串图标，返回 (store, tab)。</summary>
    private static (DataStore Store, TabModel Tab) StoreWithIcons(TempDataDirectory temp, params string[] names)
    {
        var store = temp.NewStore();
        store.Load();
        var tab = store.AddTab("图标页");

        foreach (var name in names)
        {
            store.AddIcon(tab.Id, Icon(name));
        }

        return (store, tab);
    }

    /// <summary>界面那一侧的等价动作：按查询过滤出可见项 id，再把"可见位"换算成 Core 下标。</summary>
    private static int ViewIndexToModelIndex(TabModel tab, string query, int viewIndex)
        => SearchFilter.MapViewIndexToModelIndex(
            tab.Icons.Where(i => SearchFilter.Matches(i, query)).Select(i => i.Id).ToList(),
            tab.Icons.Select(i => i.Id).ToList(),
            viewIndex);

    [Fact]
    public void 过滤态下同页拖动_落库顺序正确()
    {
        using var temp = new TempDataDirectory();
        var (store, tab) = StoreWithIcons(temp, "项目A", "隐藏1", "隐藏2", "项目B");

        // 查询「项目」⇒ 可见 = [项目A(0), 项目B(3)]
        Assert.Equal(new[] { "项目A", "项目B" },
            tab.Icons.Where(i => SearchFilter.Matches(i, "项目")).Select(i => i.DisplayName));

        // 把「项目B」拖到可见的第 0 位（= 项目A 之前）⇒ Core 下标 0
        var target = ViewIndexToModelIndex(tab, "项目", 0);
        Assert.Equal(0, target);

        var dropped = tab.Icons.Single(i => i.DisplayName == "项目B");
        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, tab.Id, dropped.Id), tab.Id, target));

        Assert.True(result.Success);
        Assert.Equal(new[] { "项目B", "项目A", "隐藏1", "隐藏2" },
            tab.Icons.Select(i => i.DisplayName));

        // 磁盘上的顺序必须一致（下次启动界面照它重建）
        Assert.Equal(new[] { "项目B", "项目A", "隐藏1", "隐藏2" },
            temp.NewStore().Load().Single(t => t.Id == tab.Id).Icons.Select(i => i.DisplayName));
    }

    [Fact]
    public void 过滤态下把可见项拖到可见末尾_不会越过后面的看不见的项()
    {
        using var temp = new TempDataDirectory();
        var (store, tab) = StoreWithIcons(temp, "项目A", "隐藏1", "项目B", "隐藏2");

        // 查询「项目」⇒ 可见 = [项目A(0), 项目B(2)]
        Assert.Equal(new[] { "项目A", "项目B" },
            tab.Icons.Where(i => SearchFilter.Matches(i, "项目")).Select(i => i.DisplayName));

        // 把「项目A」拖到可见的第 2 位（= 项目B 之后）⇒ Core 下标 = 项目B 的下标 2 + 1
        var target = ViewIndexToModelIndex(tab, "项目", 2);
        Assert.Equal(3, target);

        var dragged = tab.Icons.Single(i => i.DisplayName == "项目A");
        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, tab.Id, dragged.Id), tab.Id, target));

        Assert.True(result.Success);

        // 「项目A」落在项目B 之后、隐藏2 之前 —— 这正是"插在最后一个**可见项**后面"的含义，
        // 而不是"整页末尾"（那会变成 [隐藏1, 项目B, 隐藏2, 项目A]）
        Assert.Equal(new[] { "隐藏1", "项目B", "项目A", "隐藏2" },
            tab.Icons.Select(i => i.DisplayName));
        Assert.Equal(new[] { "隐藏1", "项目B", "项目A", "隐藏2" },
            temp.NewStore().Load().Single(t => t.Id == tab.Id).Icons.Select(i => i.DisplayName));
    }

    [Fact]
    public void 过滤态下跨页拖动_插到对应可见项之前()
    {
        using var temp = new TempDataDirectory();
        var (store, source) = StoreWithIcons(temp, "被拖的图标");
        var target = store.AddTab("目标页");
        foreach (var name in new[] { "项目甲", "乙", "项目丙" })
        {
            store.AddIcon(target.Id, Icon(name));
        }

        // 目标页查询「项目」⇒ 可见 = [项目甲(0), 项目丙(2)]；落在可见第 1 位 = 项目丙之前 ⇒ Core 下标 2
        var targetIndex = ViewIndexToModelIndex(target, "项目", 1);
        Assert.Equal(2, targetIndex);

        var moved = source.Icons.Single();
        var result = store.ApplyDragDrop(new DragDropRequest(
            new DragPayload(DragItemKind.Icon, source.Id, moved.Id), target.Id, targetIndex));

        Assert.True(result.Success);
        Assert.Equal(new[] { "项目甲", "乙", "被拖的图标", "项目丙" }, target.Icons.Select(i => i.DisplayName));
        Assert.Empty(source.Icons);

        var reloaded = temp.NewStore().Load();
        Assert.Equal(new[] { "项目甲", "乙", "被拖的图标", "项目丙" },
            reloaded.Single(t => t.Id == target.Id).Icons.Select(i => i.DisplayName));
    }
}
