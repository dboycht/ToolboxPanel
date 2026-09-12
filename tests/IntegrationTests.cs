// IntegrationTests.cs —— W2 验收：把「数据层 + .lnk 解析 + 图标提取 + 缓存清理」串起来跑一遍
//
// 对应 DEVELOPMENT.md §0.6 里 W2 的验收条款：
//   「5 类图标都能取得正确图标；.lnk 能解析出目标/参数/工作目录」
// 这条走的正是正式应用将来的路径：建图标 → 提取图标 → 落到 data/icons → 写回 tabs.json → 重载 → 清理孤儿。
//
// ⚠️ 全程只用临时目录与只读系统文件，不开窗口、不起进程（ERROR.md E5）。

using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class IntegrationTests
{
    private static string WindowsDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    [Fact]
    public void 五类图标都能提取并落进缓存目录()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var extractor = new IconExtractor(temp.IconsDirectory);
        store.Load();

        var tab = store.Tabs[0];
        var notepad = Path.Combine(WindowsDir, "System32", "notepad.exe");
        var shell32 = Path.Combine(WindowsDir, "System32", "shell32.dll");
        var lnkPath = Path.Combine(temp.Path, "记事本.lnk");
        TestShortcutFactory.Create(lnkPath, notepad, iconPath: shell32, iconIndex: 3);

        var icons = new[]
        {
            new IconModel { Type = IconType.File, DisplayName = "文档", SourcePath = notepad },
            new IconModel { Type = IconType.Folder, DisplayName = "文件夹", SourcePath = temp.Path },
            new IconModel { Type = IconType.Shortcut, DisplayName = "快捷方式", SourcePath = lnkPath, TargetPath = notepad },
            new IconModel { Type = IconType.Url, DisplayName = "网址", SourcePath = "https://example.com" },
            new IconModel { Type = IconType.Command, DisplayName = "命令", SourcePath = "cmd.exe /c echo hi" },
        };

        foreach (var icon in icons)
        {
            store.AddIcon(tab.Id, icon);

            // 正式流程：快捷方式先解析，再交给提取器（自定义图标优先）
            var shortcut = icon.Type == IconType.Shortcut ? WindowsShortcut.Resolve(icon.SourcePath) : null;
            icon.IconCacheFile = extractor.ExtractAndCacheForIcon(icon, shortcut);
        }

        store.Save();

        // 1) 每类图标都拿到了缓存文件，且文件真的在盘上
        Assert.All(icons, icon => Assert.False(string.IsNullOrEmpty(icon.IconCacheFile)));
        Assert.All(icons, icon => Assert.True(File.Exists(extractor.CachePath(icon.IconCacheFile))));
        Assert.All(icons, icon => Assert.True(new FileInfo(extractor.CachePath(icon.IconCacheFile)).Length > 100));

        // 2) 重载后缓存文件名不丢（写回 tabs.json 的字段）
        var reloaded = temp.NewStore().Load();
        var reloadedIcons = reloaded[0].Icons;
        Assert.Equal(5, reloadedIcons.Count);
        for (int i = 0; i < icons.Length; i++)
        {
            Assert.Equal(icons[i].Id, reloadedIcons[i].Id);
            Assert.Equal(icons[i].IconCacheFile, reloadedIcons[i].IconCacheFile);
            Assert.Equal(icons[i].Type, reloadedIcons[i].Type);
        }

        // 3) 全部被引用 → 没有孤儿
        Assert.Empty(store.OrphanCacheFiles());
    }

    [Fact]
    public void 快捷方式的解析结果能原样写进图标模型并跨重载保持()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        store.Load();

        var target = Path.Combine(WindowsDir, "System32", "notepad.exe");
        var lnkPath = Path.Combine(temp.Path, "带参数的快捷方式.lnk");
        TestShortcutFactory.Create(
            lnkPath,
            targetPath: target,
            arguments: "--flag \"含 空格\"",
            workingDirectory: WindowsDir,
            description: "说明文字");

        var shortcut = WindowsShortcut.Resolve(lnkPath);
        Assert.NotNull(shortcut);

        var icon = new IconModel
        {
            Type = IconType.Shortcut,
            DisplayName = "记事本",
            SourcePath = lnkPath,
            TargetPath = shortcut!.TargetPath,
            Arguments = shortcut.Arguments,
            WorkingDir = shortcut.WorkingDirectory,
            Description = shortcut.Description,
        };
        store.AddIcon(store.Tabs[0].Id, icon);

        var reloaded = temp.NewStore().Load()[0].Icons[0];

        Assert.Equal(target, reloaded.TargetPath);
        Assert.Equal("--flag \"含 空格\"", reloaded.Arguments);
        Assert.Equal(WindowsDir, reloaded.WorkingDir);
        Assert.Equal("说明文字", reloaded.Description);
    }

    [Fact]
    public void 删除图标后它的缓存文件被清掉_不留孤儿()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var extractor = new IconExtractor(temp.IconsDirectory);
        store.Load();
        var tab = store.Tabs[0];

        var icon = new IconModel { Type = IconType.File, SourcePath = Path.Combine(WindowsDir, "System32", "notepad.exe") };
        store.AddIcon(tab.Id, icon);
        icon.IconCacheFile = extractor.ExtractAndCache(icon.SourcePath, icon.Type);
        store.Save();

        var cachePath = extractor.CachePath(icon.IconCacheFile);
        Assert.True(File.Exists(cachePath));

        store.RemoveIcon(icon.Id);

        Assert.False(File.Exists(cachePath));      // RemoveIcon 会顺手删缓存
        Assert.Empty(store.OrphanCacheFiles());
    }

    [Fact]
    public void 清孤儿缓存_只删没人引用的图()
    {
        using var temp = new TempDataDirectory();
        var store = temp.NewStore();
        var extractor = new IconExtractor(temp.IconsDirectory);
        store.Load();

        var icon = new IconModel { Type = IconType.File, SourcePath = Path.Combine(WindowsDir, "System32", "notepad.exe") };
        store.AddIcon(store.Tabs[0].Id, icon);
        icon.IconCacheFile = extractor.ExtractAndCache(icon.SourcePath, icon.Type);

        // 模拟"提取了但没保存进模型"的残留
        var orphanName = extractor.ExtractAndCache(icon.SourcePath, icon.Type);
        store.Save();

        Assert.Contains(orphanName, store.OrphanCacheFiles());

        store.CleanOrphanCache();

        Assert.True(File.Exists(extractor.CachePath(icon.IconCacheFile)));
        Assert.False(File.Exists(extractor.CachePath(orphanName)));
        Assert.Empty(store.OrphanCacheFiles());
    }

    [Fact]
    public void 启动器面对模型里的五种图标_决策正确且不真的启动()
    {
        using var temp = new TempDataDirectory();
        var lnkPath = Path.Combine(temp.Path, "x.lnk");
        var exePath = Path.Combine(temp.Path, "x.exe");
        var docPath = Path.Combine(temp.Path, "x.txt");
        File.WriteAllText(lnkPath, "l", new System.Text.UTF8Encoding(false));
        File.WriteAllText(exePath, "e", new System.Text.UTF8Encoding(false));
        File.WriteAllText(docPath, "d", new System.Text.UTF8Encoding(false));

        var host = new RecordingShellHost();
        var launcher = new Launcher(host);

        Assert.True(launcher.Open(new IconModel { Type = IconType.File, SourcePath = docPath }).Success);
        Assert.True(launcher.Open(new IconModel { Type = IconType.Folder, SourcePath = temp.Path }).Success);
        Assert.True(launcher.Open(new IconModel { Type = IconType.Shortcut, SourcePath = lnkPath, TargetPath = exePath }).Success);
        Assert.True(launcher.Open(new IconModel { Type = IconType.Url, SourcePath = "example.com" }).Success);
        Assert.True(launcher.Open(new IconModel { Type = IconType.Command, TargetPath = exePath, Arguments = "-a -b", WorkingDir = temp.Path }).Success);

        Assert.Equal(new[] { docPath, temp.Path, lnkPath, "https://example.com" }, host.Opened);
        var (exe, args, cwd) = Assert.Single(host.Started);
        Assert.Equal(exePath, exe);
        Assert.Equal(new[] { "-a", "-b" }, args);
        Assert.Equal(temp.Path, cwd);
    }

    /// <summary>与 LauncherTests 里同款的假宿主（这里再写一份，避免测试类之间互相依赖）。</summary>
    private sealed class RecordingShellHost : IShellHost
    {
        public List<string> Opened { get; } = new();

        public List<(string Executable, IReadOnlyList<string> Arguments, string? WorkingDirectory)> Started { get; } = new();

        public void ShellOpen(string path) => Opened.Add(path);

        public void StartProcess(string executable, IReadOnlyList<string> arguments, string? workingDirectory)
            => Started.Add((executable, arguments, workingDirectory));
    }
}
