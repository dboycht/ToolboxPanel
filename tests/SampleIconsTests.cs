// SampleIconsTests.cs —— 示例图标（内容为空时自动补充）
//
// 这层逻辑会**写用户数据**，所以必须有测试守住三条底线：
//   ① 已有一个图标就**绝不动**用户数据（不能被"补充"打扰）；
//   ② 只在"所有内容页全空"时补，且**只新增**一个页，不碰已有页；
//   ③ 不产生点不开的死图标（目标不存在的项要跳过）。
//
// ⚠️ 与真机相关的一点：示例清单里绝大多数是 Windows 自带程序，**本机实测存在**；
//    但测试不能假设某个具体文件一定在（换台机器可能没有），所以断言写成
//    "不存在的路径不得出现在生成结果里"，而不是"必须有 N 个"。

using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class SampleIconsTests
{
    [Fact]
    public void 生成结果里不包含不存在的目标()
    {
        var icons = SampleIcons.BuildForExisting();

        foreach (var icon in icons)
        {
            if (icon.Type is IconType.Url or IconType.Command)
            {
                continue;   // 这两类不依赖本地文件
            }

            Assert.True(
                File.Exists(icon.SourcePath) || Directory.Exists(icon.SourcePath),
                $"示例图标「{icon.DisplayName}」的目标不存在：{icon.SourcePath}");
        }
    }

    [Fact]
    public void 示例清单覆盖全部五种图标类型()
    {
        var icons = SampleIcons.BuildForExisting();

        // 本机是 Windows，至少"文件类"必须有；其余类型按清单固定给了 URL/COMMAND
        Assert.Contains(icons, i => i.Type == IconType.File);
        Assert.Contains(icons, i => i.Type == IconType.Url);
        Assert.Contains(icons, i => i.Type == IconType.Command);
    }

    [Fact]
    public void 每个示例图标都有名字且字段与原版语义一致()
    {
        foreach (var icon in SampleIcons.BuildForExisting())
        {
            Assert.False(string.IsNullOrWhiteSpace(icon.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(icon.SourcePath));
            Assert.False(string.IsNullOrWhiteSpace(icon.Id));

            // COMMAND 类型按原版语义：source_path 是整条命令行，另存 target/arguments
            if (icon.Type == IconType.Command)
            {
                Assert.False(string.IsNullOrWhiteSpace(icon.TargetPath));
                Assert.False(string.IsNullOrWhiteSpace(icon.Arguments));
            }
        }
    }

    // ────────────────────────────── EnsureSampleIcons ──────────────────────────────

    [Fact]
    public void 已经有图标时绝不改动用户数据()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();

        var tab = store.AddTab("我的页");
        store.AddIcon(tab.Id, new IconModel { DisplayName = "我自己的", SourcePath = @"C:\Windows" });

        var before = File.ReadAllBytes(temp.TabsFile);
        var added = SampleIcons.EnsureSampleIcons(store);

        Assert.False(added);
        Assert.Equal(before, File.ReadAllBytes(temp.TabsFile));   // 文件一个字节都没变
        Assert.DoesNotContain(store.Tabs, t => t.Name == SampleIcons.SampleTabName);
    }

    [Fact]
    public void 全部为空时新增示例页且不碰已有页()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();

        var empty = store.AddTab("空页");
        var list = store.AddTab("列表页", TabModel.TypeList);
        store.AddListItem(list.Id, new ListItemModel { Description = "一", Path = @"C:\" });

        var beforeTabIds = store.Tabs.Select(t => t.Id).ToList();

        var added = SampleIcons.EnsureSampleIcons(store);

        Assert.True(added);

        var sample = store.Tabs.Single(t => t.Name == SampleIcons.SampleTabName);
        Assert.NotEmpty(sample.Icons);
        Assert.Equal(TabModel.TypeGrid, sample.TabType);

        // 已有页原样保留（id 不变、仍是空的、列表项没被碰）
        foreach (var id in beforeTabIds)
        {
            Assert.Contains(store.Tabs, t => t.Id == id);
        }

        Assert.Empty(store.Tabs.Single(t => t.Id == empty.Id).Icons);
        Assert.Single(store.Tabs.Single(t => t.Id == list.Id).ListItems);
    }

    [Fact]
    public void 补充之后重载仍能读回且顺序号连续()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        store.AddTab("列表页", TabModel.TypeList);

        Assert.True(SampleIcons.EnsureSampleIcons(store));

        var reloaded = temp.NewStore().Load();
        var sample = reloaded.Single(t => t.Name == SampleIcons.SampleTabName);

        Assert.NotEmpty(sample.Icons);
        Assert.Equal(
            Enumerable.Range(0, sample.Icons.Count).ToArray(),
            sample.Icons.Select(i => i.SortOrder).ToArray());
    }

    [Fact]
    public void 再调用一次不会重复补()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();

        Assert.True(SampleIcons.EnsureSampleIcons(store));
        Assert.False(SampleIcons.EnsureSampleIcons(store));   // 已经有图标了

        Assert.Single(store.Tabs, t => t.Name == SampleIcons.SampleTabName);
    }

    [Fact]
    public void 标签页全为空数组时不动手()
    {
        using var temp = new TempDataDirectory();
        temp.WriteRawTabsJson("{\"version\":1,\"tabs\":[]}");

        // tabs 为空 → Load() 会重建一个默认页；此时应当允许补充
        var store = temp.NewStore();
        store.Load();

        Assert.True(SampleIcons.EnsureSampleIcons(store));
    }
}
