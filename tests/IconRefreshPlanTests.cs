// IconRefreshPlanTests.cs —— 「刷新图标」该按哪条路径重取（2026-10-03）
//
// 逼出这段逻辑的真实场景（用户实测）：一个快捷方式图标的桌面 `.lnk` **已被删除**、
// 而 `target_path` 上的程序还在（记录里 `source_path` 仍指向那个死 .lnk）。
// 第一版「刷新图标」只照 `source_path` 重取 ⇒ 必然失败、界面一直是旧的兜底字形。
//
// 判据：**第一条真实存在的路径**（source 优先），与 `Launcher.OpenShortcut` 同口径；
// 两条都不在 ⇒ 计划为 null + 报出那条被试过的路径（给用户一句指名道姓的提示）。

using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class IconRefreshPlanTests
{
    /// <summary>在临时目录里造一个真实存在的文件（`File.Exists` 要真）。</summary>
    private static string CreateFile(TempDataDirectory temp, string name)
    {
        var path = Path.Combine(temp.Path, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void 源与目标都在时_按源提取_lnk优先()
    {
        using var temp = new TempDataDirectory();
        var lnk = CreateFile(temp, "CC Switch.lnk");
        var exe = CreateFile(temp, "cc-switch.exe");

        var icon = new IconModel { Type = IconType.Shortcut, SourcePath = lnk, TargetPath = exe };

        var decision = IconEditor.PlanRefresh(icon);

        Assert.NotNull(decision.Plan);
        Assert.Equal(IconRefreshKind.ReextractFromSource, decision.Plan!.Kind);
        Assert.Equal(lnk, decision.Plan!.SourcePath);   // .lnk 可能带自定义图标 ⇒ 优先它
        Assert.Equal(lnk, decision.TriedPath);
    }

    [Fact]
    public void 源lnk已被删除而目标还在时_退到目标提取_这正是用户实测那条()
    {
        using var temp = new TempDataDirectory();
        var deadLnk = Path.Combine(temp.Path, "CC Switch.lnk");   // 故意不创建 = 已删除
        var exe = CreateFile(temp, "cc-switch.exe");

        var icon = new IconModel { Type = IconType.Shortcut, SourcePath = deadLnk, TargetPath = exe };

        var decision = IconEditor.PlanRefresh(icon);

        Assert.NotNull(decision.Plan);
        Assert.Equal(IconRefreshKind.ReextractFromSource, decision.Plan!.Kind);
        Assert.Equal(exe, decision.Plan!.SourcePath);   // ★ 关键：用还活着的目标，而不是那条死 .lnk
    }

    [Fact]
    public void 源与目标都不在时_计划为空且报出目标那条路径()
    {
        using var temp = new TempDataDirectory();
        var deadLnk = Path.Combine(temp.Path, "gone.lnk");
        var deadExe = Path.Combine(temp.Path, "gone.exe");

        var icon = new IconModel { Type = IconType.Shortcut, SourcePath = deadLnk, TargetPath = deadExe };

        var decision = IconEditor.PlanRefresh(icon);

        Assert.Null(decision.Plan);
        Assert.Equal(deadExe, decision.TriedPath);   // 报"程序没了"比报".lnk 没了"更有用
    }

    [Fact]
    public void 文件类型_按source提取()
    {
        using var temp = new TempDataDirectory();
        var file = CreateFile(temp, "a.txt");

        var icon = new IconModel { Type = IconType.File, SourcePath = file, TargetPath = file };

        var decision = IconEditor.PlanRefresh(icon);

        Assert.Equal(file, decision.Plan!.SourcePath);
    }

    [Fact]
    public void 目录也算存在的来源()
    {
        using var temp = new TempDataDirectory();
        var dir = Path.Combine(temp.Path, "some-folder");
        Directory.CreateDirectory(dir);

        var icon = new IconModel { Type = IconType.Folder, SourcePath = dir, TargetPath = dir };

        Assert.Equal(dir, IconEditor.PlanRefresh(icon).Plan!.SourcePath);
    }

    [Theory]
    [InlineData(IconType.Url)]
    [InlineData(IconType.Command)]
    public void 网址与命令_不按路径提取_给该类型的标准图标(IconType type)
    {
        // 这两类的 source_path 是网址/命令行，拿去当文件名查图标只会得到毫无意义的图
        // —— 与 IconEditor.Create / IconExtractor.ExtractHandleFor 的口径一致
        var icon = new IconModel { Type = type, SourcePath = "https://example.com", TargetPath = "whatever" };

        var decision = IconEditor.PlanRefresh(icon);

        Assert.Equal(IconRefreshKind.StandardFallback, decision.Plan!.Kind);
        Assert.Equal(string.Empty, decision.TriedPath);
    }

    [Fact]
    public void 空路径一律回落到标准图标_不抛异常()
    {
        var icon = new IconModel { Type = IconType.File, SourcePath = string.Empty, TargetPath = string.Empty };

        var decision = IconEditor.PlanRefresh(icon);

        Assert.Null(decision.Plan);
        Assert.Equal(string.Empty, decision.TriedPath);
    }

    [Fact]
    public void 源为空时用目标_源非空但不存在时也用目标()
    {
        using var temp = new TempDataDirectory();
        var exe = CreateFile(temp, "tool.exe");

        // ① 源压根没填
        var noSource = new IconModel { Type = IconType.Shortcut, SourcePath = string.Empty, TargetPath = exe };
        Assert.Equal(exe, IconEditor.PlanRefresh(noSource).Plan!.SourcePath);

        // ② 源填了但不存在（空白/脏数据）
        var deadSource = new IconModel { Type = IconType.Shortcut, SourcePath = "  ", TargetPath = exe };
        Assert.Equal(exe, IconEditor.PlanRefresh(deadSource).Plan!.SourcePath);
    }
}
