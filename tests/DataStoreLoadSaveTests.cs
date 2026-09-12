// DataStoreLoadSaveTests.cs —— 载入/保存与「旧数据可读、写回可被旧版读回」（W1 验收重点）

using System.Text;
using System.Text.Json;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class DataStoreLoadSaveTests
{
    // ────────────────────────────── 载入 ──────────────────────────────

    [Fact]
    public void Load_文件不存在_建默认Home页并立刻落盘()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();

        var tabs = store.Load();

        Assert.Single(tabs);
        Assert.Equal("Home", tabs[0].Name);          // 原版兜底页名是英文 Home
        Assert.Equal(0, tabs[0].Order);
        Assert.Equal("grid", tabs[0].TabType);
        Assert.True(File.Exists(temp.TabsFile));     // 且已落盘
        Assert.True(Directory.Exists(temp.IconsDirectory));
    }

    [Fact]
    public void Load_tabs为空数组_也建默认Home页()
    {
        using var temp = new TempDataDirectory();
        temp.WriteRawTabsJson("{\"version\": 1, \"tabs\": []}");

        var tabs = temp.NewStore().Load();

        Assert.Single(tabs);
        Assert.Equal("Home", tabs[0].Name);
    }

    [Fact]
    public void Load_按order升序排列()
    {
        using var temp = new TempDataDirectory();
        temp.WriteRawTabsJson("""
        {
          "version": 1,
          "tabs": [
            { "id": "c", "name": "第三", "order": 2, "tab_type": "grid", "icons": [], "list_items": [] },
            { "id": "a", "name": "第一", "order": 0, "tab_type": "grid", "icons": [], "list_items": [] },
            { "id": "b", "name": "第二", "order": 1, "tab_type": "grid", "icons": [], "list_items": [] }
          ]
        }
        """);

        var tabs = temp.NewStore().Load();

        Assert.Equal(new[] { "a", "b", "c" }, tabs.Select(t => t.Id));
    }

    [Fact]
    public void Load_order相同_保持文件里的相对顺序_稳定排序()
    {
        using var temp = new TempDataDirectory();
        temp.WriteRawTabsJson("""
        {
          "version": 1,
          "tabs": [
            { "id": "x1", "name": "甲", "order": 5, "tab_type": "grid", "icons": [], "list_items": [] },
            { "id": "x2", "name": "乙", "order": 5, "tab_type": "grid", "icons": [], "list_items": [] },
            { "id": "x3", "name": "丙", "order": 5, "tab_type": "grid", "icons": [], "list_items": [] }
          ]
        }
        """);

        var tabs = temp.NewStore().Load();

        // Python 的 list.sort(key=...) 是稳定排序；List<T>.Sort 不是，所以实现里用的是 OrderBy
        Assert.Equal(new[] { "x1", "x2", "x3" }, tabs.Select(t => t.Id));
    }

    [Fact]
    public void Load_旧数据没有tab_type_按grid处理()
    {
        using var temp = new TempDataDirectory();
        temp.WriteRawTabsJson("""
        { "version": 1, "tabs": [ { "id": "old", "name": "老页面", "order": 0, "icons": [], "list_items": [] } ] }
        """);

        var tab = temp.NewStore().Load()[0];

        Assert.Equal("grid", tab.TabType);
        Assert.False(tab.IsListTab);
    }

    [Fact]
    public void Load_图标type无法识别_回落file()
    {
        using var temp = new TempDataDirectory();
        temp.WriteRawTabsJson("""
        {
          "version": 1,
          "tabs": [ { "id": "t", "tab_type": "grid", "icons": [
              { "id": "i1", "type": "weird-type" },
              { "id": "i2", "type": 123 },
              { "id": "i3" }
          ], "list_items": [] } ]
        }
        """);

        var icons = temp.NewStore().Load()[0].Icons;

        Assert.All(icons, i => Assert.Equal(IconType.File, i.Type));
    }

    [Fact]
    public void Load_缺字段_全部用默认值不炸()
    {
        using var temp = new TempDataDirectory();
        temp.WriteRawTabsJson("""
        { "version": 1, "tabs": [ { "id": "t1" } ] }
        """);

        var tab = temp.NewStore().Load()[0];

        Assert.Equal("新建标签页", tab.Name);   // TabModel 的默认名（不是 Home，Home 只用于"整个文件坏掉"的兜底）
        Assert.Equal("grid", tab.TabType);
        Assert.Empty(tab.Icons);
        Assert.Empty(tab.ListItems);
    }

    [Fact]
    public void Load_JSON损坏_备份成bak并重建默认页()
    {
        using var temp = new TempDataDirectory();
        var broken = "{ \"version\": 1, \"tabs\": [ { \"id\": ";
        temp.WriteRawTabsJson(broken);

        var tabs = temp.NewStore().Load();

        Assert.Single(tabs);
        Assert.Equal("Home", tabs[0].Name);

        // 原始（损坏）内容必须原样留在 .bak 里 —— 用户数据不能因为我们读不了就消失
        Assert.True(File.Exists(temp.TabsBackupFile));
        Assert.Equal(broken, File.ReadAllText(temp.TabsBackupFile, Encoding.UTF8));

        // 而 tabs.json 已经被重建为合法内容
        Assert.NotNull(TabsJson.Deserialize(File.ReadAllText(temp.TabsFile, Encoding.UTF8)));
    }

    [Fact]
    public void Load_tabs不是数组_也按损坏处理_不抛异常()
    {
        using var temp = new TempDataDirectory();
        temp.WriteRawTabsJson("""{ "version": 1, "tabs": { "oops": true } }""");

        var tabs = temp.NewStore().Load();

        Assert.Single(tabs);
        Assert.Equal("Home", tabs[0].Name);
        Assert.True(File.Exists(temp.TabsBackupFile));
    }

    // ────────────────────────────── 保存格式 ──────────────────────────────

    [Fact]
    public void Save_字节格式与Python写出的tabs_json一致()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.SetTabs(new[]
        {
            new TabModel { Id = "fixed-id", Name = "新建标签页", Order = 0, TabType = "grid" },
        });

        var bytes = File.ReadAllBytes(temp.TabsFile);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "不能有 UTF-8 BOM");
        Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty));   // 只允许 CRLF，不允许裸 LF
        Assert.Equal(text.Count(c => c == '\n'), text.Count(c => c == '\r')); // 也不允许孤立 CR
        Assert.EndsWith("}", text);                                        // 无尾换行
        Assert.Contains("\"name\": \"新建标签页\"", text);                  // 中文不转义
        Assert.DoesNotContain("\\u", text);

        var expected = string.Join("\r\n", new[]
        {
            "{",
            "  \"version\": 1,",
            "  \"tabs\": [",
            "    {",
            "      \"id\": \"fixed-id\",",
            "      \"name\": \"新建标签页\",",
            "      \"order\": 0,",
            "      \"tab_type\": \"grid\",",
            "      \"icons\": [],",
            "      \"list_items\": []",
            "    }",
            "  ]",
            "}",
        });
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Save_原子写_不留临时文件()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();
        store.Save();

        Assert.False(File.Exists(temp.TabsTempFile));
        Assert.True(File.Exists(temp.TabsFile));
    }

    // ────────────────────────────── 字段无损 / 双向兼容 ──────────────────────────────

    [Fact]
    public void RoundTrip_五类图标与列表项的所有字段都无损()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();

        var gridTab = new TabModel
        {
            Id = "tab-grid",
            Name = "主页",
            Order = 0,
            TabType = "grid",
            Icons =
            {
                new IconModel
                {
                    Id = "icon-file", Type = IconType.File, DisplayName = "文档",
                    SourcePath = @"D:\a\b.txt", TargetPath = "", Arguments = "",
                    WorkingDir = "", Description = "", IconCacheFile = "f.png", SortOrder = 0,
                },
                new IconModel
                {
                    Id = "icon-folder", Type = IconType.Folder, DisplayName = "文件夹",
                    SourcePath = @"D:\code", IconCacheFile = "d.png", SortOrder = 1,
                },
                new IconModel
                {
                    Id = "icon-shortcut", Type = IconType.Shortcut, DisplayName = "快捷方式",
                    SourcePath = @"D:\x.lnk", TargetPath = @"C:\Program Files\App\app.exe",
                    Arguments = "-flag \"a b\"", WorkingDir = @"C:\Program Files\App",
                    Description = "描述：中文", IconCacheFile = "s.png", SortOrder = 2,
                },
                new IconModel
                {
                    Id = "icon-url", Type = IconType.Url, DisplayName = "网址",
                    SourcePath = "https://example.com/?q=1", IconCacheFile = "u.png", SortOrder = 3,
                },
                new IconModel
                {
                    Id = "icon-command", Type = IconType.Command, DisplayName = "命令",
                    SourcePath = "cmd.exe /c echo hi", IconCacheFile = "c.png", SortOrder = 4,
                },
            },
        };

        var listTab = new TabModel
        {
            Id = "tab-list",
            Name = "列表页",
            Order = 1,
            TabType = "list",
            ListItems =
            {
                new ListItemModel { Id = "i1", Description = "一", Path = @"D:\1", SortOrder = 0 },
                new ListItemModel { Id = "i2", Description = "二", Path = @"D:\2", SortOrder = 1 },
            },
        };

        store.SetTabs(new[] { gridTab, listTab });
        var reloaded = temp.NewStore().Load();

        Assert.Equal(2, reloaded.Count);
        for (int tabIndex = 0; tabIndex < 2; tabIndex++)
        {
            var before = (tabIndex == 0 ? gridTab : listTab);
            var after = reloaded[tabIndex];
            Assert.Equal(before.Id, after.Id);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.Order, after.Order);
            Assert.Equal(before.TabType, after.TabType);

            Assert.Equal(before.Icons.Count, after.Icons.Count);
            for (int i = 0; i < before.Icons.Count; i++)
            {
                var b = before.Icons[i];
                var a = after.Icons[i];
                Assert.Equal(b.Id, a.Id);
                Assert.Equal(b.Type, a.Type);
                Assert.Equal(b.DisplayName, a.DisplayName);
                Assert.Equal(b.SourcePath, a.SourcePath);
                Assert.Equal(b.TargetPath, a.TargetPath);
                Assert.Equal(b.Arguments, a.Arguments);
                Assert.Equal(b.WorkingDir, a.WorkingDir);
                Assert.Equal(b.Description, a.Description);
                Assert.Equal(b.IconCacheFile, a.IconCacheFile);
                Assert.Equal(b.SortOrder, a.SortOrder);
            }

            Assert.Equal(before.ListItems.Count, after.ListItems.Count);
            for (int i = 0; i < before.ListItems.Count; i++)
            {
                Assert.Equal(before.ListItems[i].Id, after.ListItems[i].Id);
                Assert.Equal(before.ListItems[i].Description, after.ListItems[i].Description);
                Assert.Equal(before.ListItems[i].Path, after.ListItems[i].Path);
                Assert.Equal(before.ListItems[i].SortOrder, after.ListItems[i].SortOrder);
            }
        }
    }

    [Fact]
    public void RoundTrip_未知字段被保留_不该丢数据()
    {
        using var temp = new TempDataDirectory();
        temp.WriteRawTabsJson("""
        {
          "version": 1,
          "future_top_level": { "x": 1 },
          "tabs": [
            {
              "id": "t1", "name": "页", "order": 0, "tab_type": "grid",
              "future_tab_key": "keep-me",
              "icons": [
                { "id": "i1", "type": "file", "display_name": "x", "future_icon_key": [1, 2, 3] }
              ],
              "list_items": []
            }
          ]
        }
        """);

        var store = temp.NewStore();
        store.Load();
        store.Save();

        var written = File.ReadAllText(temp.TabsFile, Encoding.UTF8);
        Assert.Contains("future_top_level", written);
        Assert.Contains("future_tab_key", written);
        Assert.Contains("future_icon_key", written);
        Assert.Contains("keep-me", written);

        using var parsed = JsonDocument.Parse(written);
        Assert.Equal(1, parsed.RootElement.GetProperty("future_top_level").GetProperty("x").GetInt32());
    }

    [Fact]
    public void 二次往返字节完全相同_幂等()
    {
        using var temp = new TempDataDirectory();

        var first = temp.NewStore();
        first.Load();
        first.AddTab("第二页", TabModel.TypeList);
        var afterFirstWrite = File.ReadAllBytes(temp.TabsFile);

        var second = temp.NewStore();
        second.Load();
        second.Save();

        Assert.Equal(afterFirstWrite, File.ReadAllBytes(temp.TabsFile));
    }

    [Fact]
    public void 真实data目录_往返写回与Python原文件逐字节相同()
    {
        // 条件式测试：在开发副本里跑才会命中（canonical 仓库不含 data/，CI 上会直接返回）
        var repoRoot = AppPaths.FindRepositoryRoot(AppContext.BaseDirectory);
        if (repoRoot is null)
        {
            return;
        }

        var realFile = System.IO.Path.Combine(repoRoot, "data", "tabs.json");
        if (!File.Exists(realFile))
        {
            return;
        }

        var originalBytes = File.ReadAllBytes(realFile);

        using var temp = new TempDataDirectory();
        File.Copy(realFile, temp.TabsFile, overwrite: true);

        var store = temp.NewStore();
        store.Load();
        store.Save();

        var roundTrippedBytes = File.ReadAllBytes(temp.TabsFile);

        Assert.Equal(originalBytes.Length, roundTrippedBytes.Length);
        Assert.Equal(
            Encoding.UTF8.GetString(originalBytes),
            Encoding.UTF8.GetString(roundTrippedBytes));
    }
}
