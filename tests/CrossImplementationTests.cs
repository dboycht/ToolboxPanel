// CrossImplementationTests.cs —— 跨实现验收：C# 写出的 tabs.json 必须能被**原版 Python** 读回
//
// 这正是 DEVELOPMENT.md §0.6 中 W1 的验收条款：「旧 tabs.json 读入字段无损、写回后能被旧版读回」。
// 光靠 C# 自己往返是自证；这里**真的把文件交给原版 Python 实现去读**，逐字段核对。
//
// 条件式测试：开发副本里（有 src/toolbox 且 PATH 上有 python）才会真正执行；
// 找不到就跳过，不会在 canonical / CI 上误报。

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class CrossImplementationTests
{
    private const string PythonProbeScript = """
        import json, sys
        sys.path.insert(0, sys.argv[1])
        from pathlib import Path
        from toolbox.models.data_store import DataStore

        store = DataStore(Path(sys.argv[2]))
        tabs = store.load()
        out = []
        for t in tabs:
            out.append({
                "name": t.name,
                "tab_type": t.tab_type,
                "order": t.order,
                "icons": [
                    {
                        "id": i.id, "type": str(i.type), "display_name": i.display_name,
                        "source_path": i.source_path, "target_path": i.target_path,
                        "arguments": i.arguments, "working_dir": i.working_dir,
                        "description": i.description, "icon_cache_file": i.icon_cache_file,
                        "sort_order": i.sort_order,
                    } for i in t.icons
                ],
                "list_items": [
                    {"id": it.id, "description": it.description, "path": it.path, "sort_order": it.sort_order}
                    for it in t.list_items
                ],
            })
        print(json.dumps(out, ensure_ascii=False))
        """;

    [Fact]
    public void CSharp写出的tabs_json能被原版Python读回且字段无损()
    {
        var repoRoot = AppPaths.FindRepositoryRoot(AppContext.BaseDirectory);
        if (repoRoot is null)
        {
            return;   // 不是开发副本，跳过
        }

        var pythonSourceRoot = Path.Combine(repoRoot, "src");
        if (!Directory.Exists(Path.Combine(pythonSourceRoot, "toolbox", "models")))
        {
            return;
        }

        if (!PythonIsAvailable())
        {
            return;
        }

        using var temp = new TempDataDirectory();

        // ── 1) 用 C# 写一份「字段全用上」的数据 ──
        var store = temp.NewStore();
        var gridTab = new TabModel
        {
            Id = "py-grid-tab",
            Name = "主页面 · 中文名",
            Order = 0,
            TabType = "grid",
            Icons =
            {
                new IconModel { Id = "i-file", Type = IconType.File, DisplayName = "文档.txt",
                                SourcePath = @"D:\资料\文档.txt", IconCacheFile = "a.png", SortOrder = 0 },
                new IconModel { Id = "i-folder", Type = IconType.Folder, DisplayName = "代码目录",
                                SourcePath = @"D:\code", IconCacheFile = "b.png", SortOrder = 1 },
                new IconModel { Id = "i-shortcut", Type = IconType.Shortcut, DisplayName = "快捷方式",
                                SourcePath = @"D:\x.lnk", TargetPath = @"C:\Program Files\App\app.exe",
                                Arguments = "--flag \"含 空格\"", WorkingDir = @"C:\Program Files\App",
                                Description = "描述（中文）", IconCacheFile = "c.png", SortOrder = 2 },
                new IconModel { Id = "i-url", Type = IconType.Url, DisplayName = "网址",
                                SourcePath = "https://example.com/路径?q=1", IconCacheFile = "d.png", SortOrder = 3 },
                new IconModel { Id = "i-command", Type = IconType.Command, DisplayName = "命令",
                                SourcePath = "cmd.exe /c echo 你好", IconCacheFile = "e.png", SortOrder = 4 },
            },
        };
        var listTab = new TabModel
        {
            Id = "py-list-tab",
            Name = "列表页",
            Order = 1,
            TabType = "list",
            ListItems =
            {
                new ListItemModel { Id = "l1", Description = "第一项", Path = @"D:\one", SortOrder = 0 },
                new ListItemModel { Id = "l2", Description = "第二项", Path = @"D:\two", SortOrder = 1 },
            },
        };
        store.SetTabs(new[] { gridTab, listTab });

        var bytesBeforePython = File.ReadAllBytes(temp.TabsFile);

        // ── 2) 交给原版 Python 读 ──
        var scriptPath = Path.Combine(temp.Path, "probe.py");
        File.WriteAllText(scriptPath, PythonProbeScript, new UTF8Encoding(false));

        var (exitCode, stdout, stderr) = RunPython(scriptPath, pythonSourceRoot, temp.Path);
        Assert.True(exitCode == 0, $"原版 Python 读取失败（exit={exitCode}）：\n{stderr}\n--- stdout ---\n{stdout}");

        // Python 的 load() 对合法文件**不应触发任何写回**（触发就说明它认为文件坏了）
        Assert.Equal(bytesBeforePython, File.ReadAllBytes(temp.TabsFile));

        // ── 3) 逐字段核对 Python 看到的内容 ──
        using var seen = JsonDocument.Parse(stdout);
        var tabsSeen = seen.RootElement;
        Assert.Equal(2, tabsSeen.GetArrayLength());

        var gridSeen = tabsSeen[0];
        Assert.Equal("主页面 · 中文名", gridSeen.GetProperty("name").GetString());
        Assert.Equal("grid", gridSeen.GetProperty("tab_type").GetString());
        Assert.Equal(0, gridSeen.GetProperty("order").GetInt32());

        var iconsSeen = gridSeen.GetProperty("icons");
        Assert.Equal(5, iconsSeen.GetArrayLength());
        Assert.Equal(new[] { "file", "folder", "shortcut", "url", "command" },
                     iconsSeen.EnumerateArray().Select(i => i.GetProperty("type").GetString()));

        var shortcut = iconsSeen[2];
        Assert.Equal("i-shortcut", shortcut.GetProperty("id").GetString());
        Assert.Equal("快捷方式", shortcut.GetProperty("display_name").GetString());
        Assert.Equal(@"D:\x.lnk", shortcut.GetProperty("source_path").GetString());
        Assert.Equal(@"C:\Program Files\App\app.exe", shortcut.GetProperty("target_path").GetString());
        Assert.Equal("--flag \"含 空格\"", shortcut.GetProperty("arguments").GetString());
        Assert.Equal(@"C:\Program Files\App", shortcut.GetProperty("working_dir").GetString());
        Assert.Equal("描述（中文）", shortcut.GetProperty("description").GetString());
        Assert.Equal("c.png", shortcut.GetProperty("icon_cache_file").GetString());
        Assert.Equal(2, shortcut.GetProperty("sort_order").GetInt32());

        Assert.Equal("https://example.com/路径?q=1", iconsSeen[3].GetProperty("source_path").GetString());
        Assert.Equal("cmd.exe /c echo 你好", iconsSeen[4].GetProperty("source_path").GetString());

        var listSeen = tabsSeen[1];
        Assert.Equal("list", listSeen.GetProperty("tab_type").GetString());
        var itemsSeen = listSeen.GetProperty("list_items");
        Assert.Equal(2, itemsSeen.GetArrayLength());
        Assert.Equal("第一项", itemsSeen[0].GetProperty("description").GetString());
        Assert.Equal(@"D:\one", itemsSeen[0].GetProperty("path").GetString());
        Assert.Equal(1, itemsSeen[1].GetProperty("sort_order").GetInt32());
    }

    [Fact]
    public void Python写的文件CSharp读进来再写回_Python仍能读且内容不变()
    {
        var repoRoot = AppPaths.FindRepositoryRoot(AppContext.BaseDirectory);
        if (repoRoot is null || !PythonIsAvailable())
        {
            return;
        }

        var pythonSourceRoot = Path.Combine(repoRoot, "src");
        if (!Directory.Exists(Path.Combine(pythonSourceRoot, "toolbox", "models")))
        {
            return;
        }

        using var temp = new TempDataDirectory();

        // 1) 让原版 Python 建一份数据（走它自己的 save 路径）
        var scriptPath = Path.Combine(temp.Path, "seed.py");
        File.WriteAllText(scriptPath, """
            import sys
            sys.path.insert(0, sys.argv[1])
            from pathlib import Path
            from toolbox.models.data_store import DataStore
            from toolbox.models.icon_model import IconModel, IconType
            from toolbox.models.list_item_model import ListItemModel

            store = DataStore(Path(sys.argv[2]))
            store.load()
            grid = store.add_tab("Python 建的页")
            store.add_icon(grid.id, IconModel(type=IconType.SHORTCUT, display_name="快捷",
                                              source_path=r"D:\a.lnk", target_path=r"C:\b.exe",
                                              arguments="-x", working_dir="C:\\", description="说明"))
            listing = store.add_tab("Python 的列表页", tab_type="list")
            store.add_list_item(listing.id, ListItemModel(description="项一", path=r"D:\p1"))
            print("SEEDED")
            """, new UTF8Encoding(false));

        var (seedExit, seedOut, seedErr) = RunPython(scriptPath, pythonSourceRoot, temp.Path);
        Assert.True(seedExit == 0, $"Python 造数据失败：{seedErr}");
        Assert.Contains("SEEDED", seedOut);

        var pythonWritten = File.ReadAllBytes(temp.TabsFile);

        // 2) C# 读进来再写回 —— 内容必须逐字节不变（这就是"字段一字不改"）
        var store = temp.NewStore();
        var tabs = store.Load();
        Assert.Equal(3, tabs.Count);          // Home + Python 建的两页
        store.Save();

        AssertBytesEqual(pythonWritten, File.ReadAllBytes(temp.TabsFile), "C# 往返后与 Python 原文件不一致");

        // 3) 再让 Python 只读一次，确认 C# 写回的文件它照样读得动
        var readScriptPath = Path.Combine(temp.Path, "read.py");
        File.WriteAllText(readScriptPath, """
            import sys
            sys.path.insert(0, sys.argv[1])
            from pathlib import Path
            from toolbox.models.data_store import DataStore
            tabs = DataStore(Path(sys.argv[2])).load()
            print("READ", len(tabs), "|".join(t.name for t in tabs))
            """, new UTF8Encoding(false));

        var (readExit, readOut, readErr) = RunPython(readScriptPath, pythonSourceRoot, temp.Path);
        Assert.True(readExit == 0, $"C# 写回后原版 Python 读不动了：\n{readErr}");
        Assert.Contains("READ 3", readOut);
        Assert.Contains("Python 建的页", readOut);
        Assert.Contains("Python 的列表页", readOut);
    }

    // ────────────────────────────── 工具 ──────────────────────────────

    /// <summary>逐字节比对，失败时打印首个差异的位置与上下文（比 Assert.Equal 的数组 diff 好读）。</summary>
    private static void AssertBytesEqual(byte[] expected, byte[] actual, string because)
    {
        if (expected.AsSpan().SequenceEqual(actual))
        {
            return;
        }

        var e = Encoding.UTF8.GetString(expected);
        var a = Encoding.UTF8.GetString(actual);
        int i = 0;
        while (i < e.Length && i < a.Length && e[i] == a[i])
        {
            i++;
        }

        int from = Math.Max(0, i - 80);
        string Context(string s) => s.Substring(from, Math.Min(200, s.Length - from));

        Assert.Fail(
            $"{because}（长度 {expected.Length} vs {actual.Length}，首个差异在第 {i} 个字符）\n"
            + $"--- 期望（Python 写出）---\n{Context(e)}\n--- 实际（C# 写出）---\n{Context(a)}");
    }

    private static bool PythonIsAvailable() => PythonRunner.IsAvailable();

    private static (int ExitCode, string StdOut, string StdErr) RunPython(
        string scriptPath, string pythonSourceRoot, string dataDirectory)
        => PythonRunner.RunScript(scriptPath, new[] { pythonSourceRoot, dataDirectory });
}
