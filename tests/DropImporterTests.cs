// DropImporterTests.cs —— 「拖入文件/文件夹/快捷方式 → 建图标」的判定逻辑（W5）
//
// 基准 = 原版 v1.11.6 tab_widget.py::_add_dropped_paths（分类、命名、去重、文案）。
// 只读磁盘：测试用临时目录造真实文件/目录，用 TestShortcutFactory 造真实 .lnk。

using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class DropImporterTests
{
    // ────────────────────────────── 分类与命名 ──────────────────────────────

    [Fact]
    public void 目录_判为FOLDER_名字取目录名本身()
    {
        using var temp = new TempDataDirectory();
        var dir = Path.Combine(temp.Path, "我的 资料.v2");
        Directory.CreateDirectory(dir);

        var decision = DropImporter.Classify(dir);

        Assert.Equal(DropDecisionKind.Add, decision.Kind);
        Assert.Equal(IconType.Folder, decision.Type);
        Assert.Equal("我的 资料.v2", decision.DisplayName);      // 目录名里的点号是名字的一部分
        Assert.Equal(dir, decision.SourcePath);
        Assert.Equal(dir, decision.TargetPath);
        Assert.Equal("已添加: 我的 资料.v2", decision.Message);
    }

    [Fact]
    public void 文件_判为FILE_名字取主干_不含扩展名()
    {
        using var temp = new TempDataDirectory();
        var file = Path.Combine(temp.Path, "报告 2026.docx");
        File.WriteAllText(file, "x");

        var decision = DropImporter.Classify(file);

        Assert.Equal(IconType.File, decision.Type);
        Assert.Equal("报告 2026", decision.DisplayName);
        Assert.Equal(file, decision.SourcePath);
        Assert.Equal(file, decision.TargetPath);
    }

    [Fact]
    public void 没有扩展名的文件_名字退回完整文件名()
    {
        using var temp = new TempDataDirectory();
        var file = Path.Combine(temp.Path, "README");
        File.WriteAllText(file, "x");

        Assert.Equal("README", DropImporter.Classify(file).DisplayName);
    }

    [Fact]
    public void 快捷方式_判为SHORTCUT_并解析目标参数工作目录()
    {
        using var temp = new TempDataDirectory();
        var target = Path.Combine(temp.Path, "app.exe");
        File.WriteAllText(target, "x");
        var workDir = Path.Combine(temp.Path, "wd");
        Directory.CreateDirectory(workDir);
        var lnk = Path.Combine(temp.Path, "我的应用.lnk");
        TestShortcutFactory.Create(lnk, target, arguments: "--fast", workingDirectory: workDir);

        var decision = DropImporter.Classify(lnk);

        Assert.Equal(IconType.Shortcut, decision.Type);
        Assert.Equal("我的应用", decision.DisplayName);          // 主干，去掉 .lnk
        Assert.Equal(lnk, decision.SourcePath);                  // 源是 .lnk 自己
        Assert.Equal(target, decision.TargetPath);               // 目标是解析出来的
        Assert.Equal("--fast", decision.Arguments);
        Assert.Equal(workDir, decision.WorkingDir);
    }

    [Fact]
    public void 坏掉的lnk_解析失败_目标退回lnk自己()
    {
        using var temp = new TempDataDirectory();
        var lnk = Path.Combine(temp.Path, "garbage.lnk");
        File.WriteAllBytes(lnk, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });   // 不是合法 .lnk

        var decision = DropImporter.Classify(lnk);

        Assert.Equal(DropDecisionKind.Add, decision.Kind);
        Assert.Equal(IconType.Shortcut, decision.Type);
        Assert.Equal(lnk, decision.TargetPath);                  // 解析不到 → 退回自己（原版语义）
        Assert.Equal("garbage", decision.DisplayName);
    }

    [Fact]
    public void 路径不存在_判为Missing_文案照原版()
    {
        var missing = Path.Combine(Path.GetTempPath(), "tp-not-exist-" + Guid.NewGuid().ToString("N"), "x.txt");

        var decision = DropImporter.Classify(missing);

        Assert.Equal(DropDecisionKind.Missing, decision.Kind);
        Assert.Equal($"路径不存在: {missing}", decision.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空路径_判为Missing(string? path)
    {
        var decision = DropImporter.Classify(path);

        Assert.Equal(DropDecisionKind.Missing, decision.Kind);
        Assert.Equal("路径不存在: ", decision.Message);
    }

    [Fact]
    public void 含点点段的相对写法_解析成规范绝对路径()
    {
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "sub"));
        var file = Path.Combine(temp.Path, "a.txt");
        File.WriteAllText(file, "x");

        var messy = Path.Combine(temp.Path, "sub", "..", "a.txt");
        var decision = DropImporter.Classify(messy);

        Assert.Equal(DropDecisionKind.Add, decision.Kind);
        Assert.Equal(file, decision.SourcePath);          // .. 已被解析掉
        Assert.DoesNotContain("..", decision.SourcePath);
    }

    // ────────────────────────────── 去重 ──────────────────────────────

    [Fact]
    public void 已存在_判为Duplicate_文案用文件名含扩展名()
    {
        using var temp = new TempDataDirectory();
        var file = Path.Combine(temp.Path, "a.txt");
        File.WriteAllText(file, "x");

        var decision = DropImporter.Classify(file, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { file });

        Assert.Equal(DropDecisionKind.Duplicate, decision.Kind);
        Assert.Equal("已存在: a.txt", decision.Message);     // 原版用的是 path.name（含扩展名）
    }

    [Fact]
    public void 去重比较忽略大小写_Windows路径不区分大小写()
    {
        using var temp = new TempDataDirectory();
        var file = Path.Combine(temp.Path, "Case.txt");
        File.WriteAllText(file, "x");

        var decision = DropImporter.Classify(
            file, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { file.ToUpperInvariant() });

        Assert.Equal(DropDecisionKind.Duplicate, decision.Kind);
    }

    [Fact]
    public void 同一次拖入里重复出现的同一路径_第二次算重复()
    {
        using var temp = new TempDataDirectory();
        var file = Path.Combine(temp.Path, "dup.txt");
        File.WriteAllText(file, "x");

        var decisions = DropImporter.Plan(new[] { file, file }, Array.Empty<string>());

        Assert.Equal(2, decisions.Count);
        Assert.Equal(DropDecisionKind.Add, decisions[0].Kind);
        Assert.Equal(DropDecisionKind.Duplicate, decisions[1].Kind);
        Assert.Equal("已存在: dup.txt", decisions[1].Message);
    }

    [Fact]
    public void 批量判定_混合场景_逐条给结论()
    {
        using var temp = new TempDataDirectory();
        var dir = Path.Combine(temp.Path, "文件夹");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(temp.Path, "b.txt");
        File.WriteAllText(file, "x");
        var missing = Path.Combine(temp.Path, "nope.txt");

        var decisions = DropImporter.Plan(
            new[] { dir, file, missing, file }, new[] { file });

        Assert.Equal(
            new[] { DropDecisionKind.Add, DropDecisionKind.Duplicate, DropDecisionKind.Missing, DropDecisionKind.Duplicate },
            decisions.Select(d => d.Kind));
        Assert.Equal($"路径不存在: {missing}", decisions[2].Message);
    }

    // ────────────────────────────── 成图 ──────────────────────────────

    [Fact]
    public void CreateIcon_Add_字段完整_且不预设图标缓存()
    {
        using var temp = new TempDataDirectory();
        var file = Path.Combine(temp.Path, "c.txt");
        File.WriteAllText(file, "x");

        var icon = DropImporter.CreateIcon(DropImporter.Classify(file));

        Assert.NotNull(icon);
        Assert.Equal(IconType.File, icon!.Type);
        Assert.Equal("c", icon.DisplayName);
        Assert.Equal(file, icon.SourcePath);
        Assert.Equal(file, icon.TargetPath);
        Assert.Equal(string.Empty, icon.IconCacheFile);      // 缓存由调用方提取后填
        Assert.NotEqual(Guid.Empty, Guid.Parse(icon.Id));    // 自动生成 id
    }

    [Theory]
    [InlineData(DropDecisionKind.Duplicate)]
    [InlineData(DropDecisionKind.Missing)]
    public void CreateIcon_非Add_返回null(DropDecisionKind kind)
    {
        var decision = new DropDecision(kind, "x", IconType.File, "n", "t", "", "", "m");

        Assert.Null(DropImporter.CreateIcon(decision));
    }

    [Fact]
    public void 状态文案照原版()
    {
        Assert.Equal("已添加: 记事本", DropImporter.AddedMessage("记事本"));
        Assert.Equal("已存在: 记事本.txt", DropImporter.AlreadyExistsMessage("记事本.txt"));
        Assert.Equal(@"路径不存在: C:\x", DropImporter.PathNotFoundMessage(@"C:\x"));
    }
}
