// BackupTests.cs —— 备份 ZIP 的导入 / 导出（含**与原版 Python 的双向兼容**）
//
// 验收条款（用户明确要求"注意兼容性"）：
//   ① 包格式与原版 `services/backup_manager.py` 一致：metadata.json 在根 + 数据在 `data/` 前缀下；
//   ② **原版 Python 导出的包 → C# 能导入**（含它在 Windows 上写出的 `data\tabs.json` 反斜杠包）；
//   ③ **C# 导出的包 → 原版 Python 能导入**（这条最关键：原版导入只认 `data/` 正斜杠前缀）；
//   ④ 缺 metadata.json ⇒ 按原版报"无效备份"；metadata 缺字段 ⇒ 当"未知"而不报错；
//   ⑤ 额外安全线：拒绝解压到数据目录之外（zip-slip）。
//
// 跨实现的两条是**条件式测试**：开发副本 + PATH 上有 python 才会跑，找不到就跳过（同 CrossImplementationTests）。

using System.Diagnostics;
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

        var zip = Path.Combine(temp.Path, "..", $"{Guid.NewGuid():N}.zip");
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

        TryDelete(zip);
    }

    [Fact]
    public void 导出_元数据是2空格缩进且中文不转义()
    {
        using var temp = new TempDataDirectory();
        WriteTabsJson(temp.Path, """{"version":1,"tabs":[{"id":"t","name":"中文页","icons":[]}]}""");

        var zip = Path.Combine(temp.Path, "..", $"{Guid.NewGuid():N}.zip");
        BackupManager.Export(temp.Path, zip, "2.0.2");

        using (var archive = ZipFile.OpenRead(zip))
        {
            using var reader = new StreamReader(archive.GetEntry("metadata.json")!.Open(), Encoding.UTF8);
            var json = reader.ReadToEnd();

            Assert.Contains("\n  \"version\":", json);   // 2 空格缩进
            Assert.DoesNotContain("\\u", json);          // 中文不转义（这里没有中文，但确保没开转义器）
            Assert.Contains("\"tab_count\": 1", json);
        }

        TryDelete(zip);
    }

    // ────────────────────────────── 往返 ──────────────────────────────

    [Fact]
    public void 导出再导入_数据逐字节一致()
    {
        using var source = new TempDataDirectory();
        WriteTabsJson(source.Path, """{"version":1,"tabs":[{"id":"t","name":"页","tab_type":"grid","icons":[]}]}""");
        File.WriteAllText(Path.Combine(source.Path, "config.json"), """{"language":"zh"}""", new UTF8Encoding(false));
        source.TouchIconCache("icon-1.png");

        var zip = Path.Combine(source.Path, "..", $"{Guid.NewGuid():N}.zip");
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

        File.Delete(zip);
    }

    // ────────────────────────────── 兼容旧包 ──────────────────────────────

    [Fact]
    public void 导入_兼容原版在Windows写出的反斜杠前缀包()
    {
        using var target = new TempDataDirectory();

        // 手工构造"原版在 Windows 上导出"的包：arcname 是 data\tabs.json（反斜杠）
        var zip = Path.Combine(target.Path, "..", $"{Guid.NewGuid():N}.zip");
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

        File.Delete(zip);
    }

    [Fact]
    public void 导入_兼容没有data前缀的裸文件包()
    {
        using var target = new TempDataDirectory();

        var zip = Path.Combine(target.Path, "..", $"{Guid.NewGuid():N}.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "metadata.json", "{}");
            WriteEntry(archive, "tabs.json", """{"version":1,"tabs":[{"id":"bare","name":"裸文件"}]}""");
        }

        var result = BackupManager.Import(zip, target.Path);

        Assert.True(result.Success, result.Message);
        Assert.Contains("裸文件", File.ReadAllText(target.TabsFile));

        File.Delete(zip);
    }

    [Fact]
    public void 导入_缺metadata_按无效备份失败且不动现有数据()
    {
        using var target = new TempDataDirectory();
        WriteTabsJson(target.Path, """{"version":1,"tabs":[{"id":"keep"}]}""");
        var before = File.ReadAllText(target.TabsFile);

        var zip = Path.Combine(target.Path, "..", $"{Guid.NewGuid():N}.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "data/tabs.json", """{"version":1,"tabs":[]}""");
        }

        var result = BackupManager.Import(zip, target.Path);

        Assert.False(result.Success);
        Assert.Contains("metadata.json", result.Message);
        Assert.Equal(before, File.ReadAllText(target.TabsFile));   // 失败时一个字节都没动

        File.Delete(zip);
    }

    [Fact]
    public void 导入_metadata缺字段只当未知_不报错()
    {
        using var target = new TempDataDirectory();

        var zip = Path.Combine(target.Path, "..", $"{Guid.NewGuid():N}.zip");
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

        File.Delete(zip);
    }

    [Fact]
    public void 导入_拒绝越界条目_不影响合法条目()
    {
        using var target = new TempDataDirectory();
        var zip = Path.Combine(target.Path, "..", $"{Guid.NewGuid():N}.zip");

        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "metadata.json", "{}");
            WriteEntry(archive, "data/tabs.json", """{"version":1,"tabs":[{"id":"ok"}]}""");
            WriteEntry(archive, "data/../escaped.txt", "我不该被写出来");
        }

        var result = BackupManager.Import(zip, target.Path);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(target.TabsFile));                                   // 合法条目照常
        Assert.False(File.Exists(Path.Combine(target.Path, "..", "escaped.txt")));  // 越界条目被拒

        File.Delete(zip);
    }

    [Fact]
    public void 导入_找不到文件时失败()
    {
        using var target = new TempDataDirectory();
        var result = BackupManager.Import(Path.Combine(target.Path, "不存在.zip"), target.Path);

        Assert.False(result.Success);
    }

    // ────────────────────────────── 与原版 Python 的双向兼容（条件式） ──────────────────────────────

    [Fact]
    public void 原版Python导出的包_能被CSharp导入()
    {
        if (!TryPreparePython(out var repoRoot))
        {
            return;
        }

        using var source = new TempDataDirectory();
        WriteTabsJson(source.Path, """
            {"version":1,"tabs":[{"id":"py","name":"Python 页","tab_type":"grid",
             "icons":[{"id":"i1","type":"file","display_name":"来自Python","source_path":"C:/x.txt","sort_order":0}]}]}
            """);
        File.WriteAllText(Path.Combine(source.Path, "config.json"), """{"language":"en"}""", new UTF8Encoding(false));
        source.TouchIconCache("py.png");

        var zip = Path.Combine(source.Path, "..", $"{Guid.NewGuid():N}.zip");
        var (exitCode, stdout, stderr) = RunPythonScript(repoRoot!, "export", source.Path, zip);
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

        TryDelete(zip);
    }

    [Fact]
    public void CSharp导出的包_能被原版Python导入()
    {
        if (!TryPreparePython(out var repoRoot))
        {
            return;
        }

        using var source = new TempDataDirectory();
        WriteTabsJson(source.Path, """
            {"version":1,"tabs":[{"id":"cs","name":"C# 页","tab_type":"grid",
             "icons":[{"id":"i1","type":"file","display_name":"来自CSharp","source_path":"C:/y.txt","sort_order":0}]}]}
            """);
        source.TouchIconCache("cs.png");

        var zip = Path.Combine(source.Path, "..", $"{Guid.NewGuid():N}.zip");
        Assert.True(BackupManager.Export(source.Path, zip, "2.0.2").Success);

        // 原版导入到另一个目录（它会清空目标目录里的 tabs.json/config.json/icons）
        using var target = new TempDataDirectory();
        var (exitCode, stdout, stderr) = RunPythonScript(repoRoot!, "import", target.Path, zip);
        Assert.True(exitCode == 0, $"原版 Python 导入失败：{stderr}\n{stdout}");

        // ⚠️ 这条正是"写正斜杠"的意义：原版导入只认 data/ 前缀，
        //    若我们写成 data\tabs.json，它会把文件塞进 <目标>/data/tabs.json（等于白导）。
        Assert.True(File.Exists(target.TabsFile), $"原版没能把 tabs.json 放到数据目录根：{stdout}");
        Assert.Contains("来自CSharp", File.ReadAllText(target.TabsFile));
        Assert.True(File.Exists(Path.Combine(target.IconsDirectory, "cs.png")));
        Assert.False(Directory.Exists(Path.Combine(target.Path, "data")));

        File.Delete(zip);
    }

    // ────────────────────────────── 工具 ──────────────────────────────

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>删临时 zip（删不掉不影响测试结论 —— 留着也会被系统清理）。</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
        catch (IOException)
        {
        }
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

    private static bool TryPreparePython(out string? repoRoot)
    {
        repoRoot = null;

        var root = AppPaths.FindRepositoryRoot(AppContext.BaseDirectory);
        if (root is null || !Directory.Exists(Path.Combine(root, "src", "toolbox", "services")))
        {
            return false;
        }

        try
        {
            var (exitCode, _, _) = RunProcess("python", new[] { "--version" }, null);
            if (exitCode != 0)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }

        // 原版 backup_manager 需要 PyQt6
        var (pyqt, _, _) = RunProcess("python", new[] { "-c", "import PyQt6" }, null);
        if (pyqt != 0)
        {
            return false;
        }

        repoRoot = root;
        return true;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunPythonScript(
        string repoRoot, string mode, string dataDirectory, string zipPath)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"toolboxpanel-backup-probe-{Guid.NewGuid():N}.py");
        File.WriteAllText(scriptPath, PythonScript, new UTF8Encoding(false));

        try
        {
            return RunProcess("python", new[]
            {
                scriptPath,
                Path.Combine(repoRoot, "src"),
                mode,
                dataDirectory,
                zipPath,
            }, null);
        }
        finally
        {
            try { File.Delete(scriptPath); } catch (IOException) { }
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string fileName, IEnumerable<string> arguments, string? workingDirectory)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
        return (process.HasExited ? process.ExitCode : -1, stdout, stderr);
    }
}
