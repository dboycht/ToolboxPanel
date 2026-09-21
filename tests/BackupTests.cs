// BackupTests.cs —— 备份 ZIP 的导入 / 导出（含**与原版 Python 的双向兼容**）
//
// 验收条款（用户明确要求"注意兼容性"）：
//   ① 包格式与原版 `services/backup_manager.py` 一致：metadata.json 在根 + 数据在 `data/` 前缀下；
//   ② **原版 Python 导出的包 → C# 能导入**（含它在 Windows 上写出的 `data\tabs.json` 反斜杠包）；
//   ③ **C# 导出的包 → 原版 Python 能导入**（这条最关键：原版导入只认 `data/` 正斜杠前缀）；
//   ④ 缺 metadata.json ⇒ 按原版报"无效备份"；metadata 缺字段 ⇒ 当"未知"而不报错；
//   ⑤ 额外安全线：拒绝解压到数据目录之外（zip-slip）。
//
// 跨实现的两条是**条件式测试**：开发副本 + PATH 上有 python（且装了 PyQt6）才会跑；
// 缺条件时由 [PythonFact("PyQt6")] **显式跳过**（报告里显示"已跳过"，不是静默 passed）；
// 开发副本里探测不到 python 则**显式失败**（同 CrossImplementationTests，见 PythonRunner）。

using System.IO.Compression;
using System.Text;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class BackupTests
{
    // ────────────────────────────── 命名与元数据 ──────────────────────────────

    [Fact]
    public void 文件名_符合原版命名规则()
    {
        var name = BackupManager.UniqueFileName();

        // Toolbox_Backup_yyyyMMdd_HHmmss_<6位hex>.zip
        Assert.Matches(@"^Toolbox_Backup_\d{8}_\d{6}_[0-9a-f]{6}\.zip$", name);
        Assert.NotEqual(name, BackupManager.UniqueFileName());   // 随机码不同
    }

    [Fact]
    public void 元数据_统计标签页与图标数_不含列表项()
    {
        using var temp = new TempDataDirectory();
        WriteTabsJson(temp.Path, """
            {
              "version": 1,
              "tabs": [
                { "id": "a", "name": "网格", "tab_type": "grid", "icons": [{"id":"1"},{"id":"2"}] },
                { "id": "b", "name": "列表", "tab_type": "list", "icons": [], "list_items": [{"id":"x"}] }
              ]
            }
            """);

        var metadata = BackupManager.BuildMetadata(temp.Path, "9.9.9");

        Assert.Equal("9.9.9", metadata.Version);
        Assert.Equal(2, metadata.TabCount);
        Assert.Equal(2, metadata.IconCount);      // ⚠️ 只数 icons：原版语义，list_items 不计
        Assert.NotEmpty(metadata.ExportedAt);
    }

    [Fact]
    public void 元数据_tabs_json损坏时按0计_不抛异常()
    {
        using var temp = new TempDataDirectory();
        WriteTabsJson(temp.Path, "{ 这不是 json");

        var metadata = BackupManager.BuildMetadata(temp.Path, "1.0.0");

        Assert.Equal(0, metadata.TabCount);
        Assert.Equal(0, metadata.IconCount);
    }

    // ────────────────────────────── 导出格式 ──────────────────────────────

    [Fact]
    public void 导出_含metadata与data前缀_且统一正斜杠()
    {
        using var temp = new TempDataDirectory();
        WriteTabsJson(temp.Path, """{"version":1,"tabs":[]}""");
        File.WriteAllText(Path.Combine(temp.Path, "config.json"), "{}", new UTF8Encoding(false));
        temp.TouchIconCache("a.png");

        var zip = temp.NewSiblingPath();
        var result = BackupManager.Export(temp.Path, zip, "2.0.2");

        Assert.True(result.Success, result.Message);

        using (var archive = ZipFile.OpenRead(zip))
        {
            var names = archive.Entries.Select(e => e.FullName).ToList();

            Assert.Contains("metadata.json", names);
            Assert.Contains("data/tabs.json", names);
            Assert.Contains("data/config.json", names);
            Assert.Contains("data/icons/a.png", names);
            Assert.DoesNotContain(names, n => n.Contains('\\'));   // ★ 兼容性关键：绝不写反斜杠
        }

    }

    [Fact]
    public void 导出_元数据是2空格缩进且中文不转义()
    {
        using var temp = new TempDataDirectory();
        WriteTabsJson(temp.Path, """{"version":1,"tabs":[{"id":"t","name":"中文页","icons":[]}]}""");

        var zip = temp.NewSiblingPath();
        BackupManager.Export(temp.Path, zip, "2.0.2");

        using (var archive = ZipFile.OpenRead(zip))
        {
            using var reader = new StreamReader(archive.GetEntry("metadata.json")!.Open(), Encoding.UTF8);
            var json = reader.ReadToEnd();

            Assert.Contains("\n  \"version\":", json);   // 2 空格缩进
            Assert.DoesNotContain("\\u", json);          // 中文不转义（这里没有中文，但确保没开转义器）
            Assert.Contains("\"tab_count\": 1", json);
        }

    }

    [Fact]
    public void 导出_目标文件已存在时覆盖而不是失败()
    {
        // ⚠️ 这条是被探针抓出来的真 bug：`ZipFile.Open(path, Create)` 内部是 FileMode.CreateNew，
        //    目标已存在会抛 "The file ... already exists."；原版 Python 是覆盖语义（"w"），必须对齐。
        using var temp = new TempDataDirectory();
        WriteTabsJson(temp.Path, """{"version":1,"tabs":[]}""");

        var zip = temp.NewSiblingPath();
        File.WriteAllText(zip, "我是上一次留下的同名文件");

        var result = BackupManager.Export(temp.Path, zip, "2.0.2");

        Assert.True(result.Success, result.Message);

        using (var archive = ZipFile.OpenRead(zip))
        {
            Assert.Contains("metadata.json", archive.Entries.Select(e => e.FullName));   // 已是合法包
        }

    }

    // ────────────────────────────── 往返 ──────────────────────────────

    [Fact]
    public void 导出再导入_数据逐字节一致()
    {
        using var source = new TempDataDirectory();
        WriteTabsJson(source.Path, """{"version":1,"tabs":[{"id":"t","name":"页","tab_type":"grid","icons":[]}]}""");
        File.WriteAllText(Path.Combine(source.Path, "config.json"), """{"language":"zh"}""", new UTF8Encoding(false));
        source.TouchIconCache("icon-1.png");

        var zip = source.NewSiblingPath();
        Assert.True(BackupManager.Export(source.Path, zip, "2.0.2").Success);

        // 目标目录先放一些"要被清掉"的脏数据
        using var target = new TempDataDirectory();
        WriteTabsJson(target.Path, """{"version":1,"tabs":[{"id":"old"}]}""");
        target.TouchIconCache("old.png");

        var result = BackupManager.Import(zip, target.Path);

        Assert.True(result.Success, result.Message);
        Assert.Equal(File.ReadAllText(source.TabsFile), File.ReadAllText(target.TabsFile));
        Assert.True(File.Exists(Path.Combine(target.Path, "config.json")));
        Assert.True(File.Exists(Path.Combine(target.IconsDirectory, "icon-1.png")));
        Assert.False(File.Exists(Path.Combine(target.IconsDirectory, "old.png")));   // 旧缓存被清掉
        Assert.NotNull(result.Metadata);
        Assert.Equal("2.0.2", result.Metadata!.Version);

    }

    // ────────────────────────────── 兼容旧包 ──────────────────────────────

    [Fact]
    public void 导入_兼容原版在Windows写出的反斜杠前缀包()
    {
        using var target = new TempDataDirectory();

        // 手工构造"原版在 Windows 上导出"的包：arcname 是 data\tabs.json（反斜杠）
        var zip = target.NewSiblingPath();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "metadata.json", """{"version":"1.11.6","exported_at":"2026-01-01T00:00:00","tab_count":1,"icon_count":0}""");
            WriteEntry(archive, @"data\tabs.json", """{"version":1,"tabs":[{"id":"old","name":"旧版页"}]}""");
            WriteEntry(archive, @"data\icons\old.png", "PNG");
        }

        var result = BackupManager.Import(zip, target.Path);

        Assert.True(result.Success, result.Message);
        Assert.Contains("旧版页", File.ReadAllText(target.TabsFile));
        Assert.True(File.Exists(Path.Combine(target.IconsDirectory, "old.png")));
        Assert.False(Directory.Exists(Path.Combine(target.Path, "data")));   // ⚠️ 绝不能多出一层 data/

    }

    [Fact]
    public void 导入_兼容没有data前缀的裸文件包()
    {
        using var target = new TempDataDirectory();

        var zip = target.NewSiblingPath();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "metadata.json", "{}");
            WriteEntry(archive, "tabs.json", """{"version":1,"tabs":[{"id":"bare","name":"裸文件"}]}""");
        }

        var result = BackupManager.Import(zip, target.Path);

        Assert.True(result.Success, result.Message);
        Assert.Contains("裸文件", File.ReadAllText(target.TabsFile));

    }

    [Fact]
    public void 导入_缺metadata_按无效备份失败且不动现有数据()
    {
        using var target = new TempDataDirectory();
        WriteTabsJson(target.Path, """{"version":1,"tabs":[{"id":"keep"}]}""");
        var before = File.ReadAllText(target.TabsFile);

        var zip = target.NewSiblingPath();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "data/tabs.json", """{"version":1,"tabs":[]}""");
        }

        var result = BackupManager.Import(zip, target.Path);

        Assert.False(result.Success);
        Assert.Contains("metadata.json", result.Message);
        Assert.Equal(before, File.ReadAllText(target.TabsFile));   // 失败时一个字节都没动

    }

    [Fact]
    public void 导入_metadata缺字段只当未知_不报错()
    {
        using var target = new TempDataDirectory();

        var zip = target.NewSiblingPath();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "metadata.json", """{"version":"1.0.0"}""");   // 缺 exported_at / tab_count / icon_count
            WriteEntry(archive, "data/tabs.json", """{"version":1,"tabs":[]}""");
        }

        var result = BackupManager.Import(zip, target.Path);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Metadata);
        Assert.Equal("1.0.0", result.Metadata!.Version);
        Assert.Equal(0, result.Metadata.TabCount);

    }

    [Fact]
    public void 导入_拒绝越界条目_不影响合法条目()
    {
        using var target = new TempDataDirectory();
        var zip = target.NewSiblingPath();

        // 越界条目的落点：明确写死一个同级文件名，才查得出"它到底有没有被写出来"
        var escaped = target.NamedSiblingPath("escaped.txt");

        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "metadata.json", "{}");
            WriteEntry(archive, "data/tabs.json", """{"version":1,"tabs":[{"id":"ok"}]}""");
            WriteEntry(archive, "data/../escaped.txt", "我不该被写出来");
        }

        var result = BackupManager.Import(zip, target.Path);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(target.TabsFile));   // 合法条目照常
        Assert.False(File.Exists(escaped));          // 越界条目被拒
    }

    [Fact]
    public void 导入_找不到文件时失败()
    {
        using var target = new TempDataDirectory();
        var result = BackupManager.Import(Path.Combine(target.Path, "不存在.zip"), target.Path);

        Assert.False(result.Success);
    }

    [Fact]
    public void 导入_解压中途失败时_既有数据一个字节都不改()
    {
        // ⚠️ 这条钉住的是"先清空再解压"那个真缺陷：
        //    旧实现里 `ClearCurrentData()` 跑在解压**之前**，于是包只要在第 k 条上出问题
        //    （坏包 / 路径过长 / 磁盘满 / 文件被占用），异常就被 catch 吞成一句 Fail ——
        //    此刻 icons/ 已空、tabs.json 已不在，**没有任何回滚**，
        //    用户看到"导入失败"外加"图标全没了"。
        //    现在解压先落到暂存区，暂存区落定之后才清空并搬入 ⇒ 失败 = 原样。
        using var target = new TempDataDirectory();
        var originalTabs = """{"version":1,"tabs":[{"id":"keep","name":"我的页"}]}""";
        WriteTabsJson(target.Path, originalTabs);
        target.TouchIconCache("keep.png");

        var zip = target.NewSiblingPath();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "metadata.json", """{"version":"2.0.4"}""");
            WriteEntry(archive, "data/tabs.json", """{"version":1,"tabs":[{"id":"new"}]}""");

            // 一条必然解压失败的条目：路径长到超过 Windows 的路径上限
            WriteEntry(archive, "data/" + new string('x', 400) + "/bad.txt", "boom");
        }

        var result = BackupManager.Import(zip, target.Path);

        // ⚠️ 这条断言本身也是"测试有效性"的保险：如果哪天本机开了长路径支持、
        //    这条坏条目居然解成功了，这里会立刻变红 —— 而不是让下面几条断言"空过"。
        Assert.False(result.Success);

        Assert.Equal(originalTabs, File.ReadAllText(target.TabsFile));                 // tabs.json 原样
        Assert.True(File.Exists(Path.Combine(target.IconsDirectory, "keep.png")));      // 图标缓存原样
        Assert.DoesNotContain("new", File.ReadAllText(target.TabsFile));                // 半成品没被搬进来
    }

    // ────────────────────────────── 与原版 Python 的双向兼容（条件式） ──────────────────────────────

    [PythonFact("PyQt6")]
    public void 原版Python导出的包_能被CSharp导入()
    {
        using var source = new TempDataDirectory();
        WriteTabsJson(source.Path, """
            {"version":1,"tabs":[{"id":"py","name":"Python 页","tab_type":"grid",
             "icons":[{"id":"i1","type":"file","display_name":"来自Python","source_path":"C:/x.txt","sort_order":0}]}]}
            """);
        File.WriteAllText(Path.Combine(source.Path, "config.json"), """{"language":"en"}""", new UTF8Encoding(false));
        source.TouchIconCache("py.png");

        var zip = source.NewSiblingPath();
        var (exitCode, stdout, stderr) = RunPythonScript("export", source.Path, zip);
        Assert.True(exitCode == 0, $"原版 Python 导出失败：{stderr}");

        // 原版包里写的版本号 = 开发副本 src/toolbox/__init__.py 的 __version__（别把版本号写死在测试里）
        var pythonVersion = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("VERSION=", StringComparison.Ordinal))?["VERSION=".Length..]
            .Trim();
        Assert.False(string.IsNullOrWhiteSpace(pythonVersion), $"没能从原版读出 __version__：{stdout}");

        using var target = new TempDataDirectory();
        var result = BackupManager.Import(zip, target.Path);

        Assert.True(result.Success, result.Message);
        var restored = File.ReadAllText(target.TabsFile);
        Assert.Contains("来自Python", restored);
        Assert.True(File.Exists(Path.Combine(target.IconsDirectory, "py.png")));
        Assert.Equal(pythonVersion, result.Metadata!.Version);

    }

    [PythonFact("PyQt6")]
    public void CSharp导出的包_能被原版Python导入()
    {
        using var source = new TempDataDirectory();
        WriteTabsJson(source.Path, """
            {"version":1,"tabs":[{"id":"cs","name":"C# 页","tab_type":"grid",
             "icons":[{"id":"i1","type":"file","display_name":"来自CSharp","source_path":"C:/y.txt","sort_order":0}]}]}
            """);
        source.TouchIconCache("cs.png");

        var zip = source.NewSiblingPath();
        Assert.True(BackupManager.Export(source.Path, zip, "2.0.2").Success);

        // 原版导入到另一个目录（它会清空目标目录里的 tabs.json/config.json/icons）
        using var target = new TempDataDirectory();
        var (exitCode, stdout, stderr) = RunPythonScript("import", target.Path, zip);
        Assert.True(exitCode == 0, $"原版 Python 导入失败：{stderr}\n{stdout}");

        // ⚠️ 这条正是"写正斜杠"的意义：原版导入只认 data/ 前缀，
        //    若我们写成 data\tabs.json，它会把文件塞进 <目标>/data/tabs.json（等于白导）。
        Assert.True(File.Exists(target.TabsFile), $"原版没能把 tabs.json 放到数据目录根：{stdout}");
        Assert.Contains("来自CSharp", File.ReadAllText(target.TabsFile));
        Assert.True(File.Exists(Path.Combine(target.IconsDirectory, "cs.png")));
        Assert.False(Directory.Exists(Path.Combine(target.Path, "data")));

    }

    // ────────────────────────────── 工具 ──────────────────────────────

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>把 tabs.json 写成 DataStore 认可的格式（直接写文本即可，导入导出只按字节搬运）。</summary>
    private static void WriteTabsJson(string directory, string json)
        => File.WriteAllText(Path.Combine(directory, "tabs.json"), json, new UTF8Encoding(false));

    private const string PythonScript = """
        # 用**原版** backup_manager 做导出/导入（不走 QThread 的 start()，直接调内部方法）
        import sys
        from pathlib import Path
        from PyQt6.QtCore import QCoreApplication

        app = QCoreApplication([])
        sys.path.insert(0, sys.argv[1])
        from toolbox import __version__
        from toolbox.services.backup_manager import BackupWorker

        print("VERSION=" + __version__)

        mode, data_dir, zip_path = sys.argv[2], Path(sys.argv[3]), sys.argv[4]
        worker = BackupWorker(mode, data_dir, zip_path)
        if mode == "export":
            worker._do_export()
        else:
            worker._do_import()
        print("DONE")
        """;

    /// <summary>
    /// 用**原版** Python 跑一次导出/导入 —— 统一走 <see cref="PythonRunner"/>（不再自带一份进程执行器）。
    /// 开发副本里缺 <c>src\toolbox\services</c> ⇒ 抛异常显式失败。
    /// </summary>
    private static (int ExitCode, string StdOut, string StdErr) RunPythonScript(
        string mode, string dataDirectory, string zipPath)
    {
        var srcRoot = PythonRunner.RequireOriginalDirectory("src");
        PythonRunner.RequireOriginalDirectory("src", "toolbox", "services");

        var scriptPath = Path.Combine(Path.GetTempPath(), $"toolboxpanel-backup-probe-{Guid.NewGuid():N}.py");
        File.WriteAllText(scriptPath, PythonScript, new UTF8Encoding(false));

        try
        {
            return PythonRunner.RunScript(scriptPath, new[]
            {
                srcRoot,
                mode,
                dataDirectory,
                zipPath,
            });
        }
        finally
        {
            try { File.Delete(scriptPath); } catch (IOException) { }
        }
    }
}
