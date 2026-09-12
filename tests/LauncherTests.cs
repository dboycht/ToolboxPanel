// LauncherTests.cs —— W2：启动逻辑（全部用假宿主，绝不真的开窗口/起进程）

using System.Text;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class LauncherTests
{
    /// <summary>只记录「本来要做什么」，不真的执行 —— ERROR.md E5 的纪律。</summary>
    private sealed class RecordingShellHost : IShellHost
    {
        public List<string> Opened { get; } = new();

        public List<(string Executable, IReadOnlyList<string> Arguments, string? WorkingDirectory)> Started { get; } = new();

        public Exception? ThrowOnShellOpen { get; set; }

        public Exception? ThrowOnStartProcess { get; set; }

        public void ShellOpen(string path)
        {
            if (ThrowOnShellOpen is not null)
            {
                throw ThrowOnShellOpen;
            }

            Opened.Add(path);
        }

        public void StartProcess(string executable, IReadOnlyList<string> arguments, string? workingDirectory)
        {
            if (ThrowOnStartProcess is not null)
            {
                throw ThrowOnStartProcess;
            }

            Started.Add((executable, arguments, workingDirectory));
        }
    }

    // ────────────────────────────── URL ──────────────────────────────

    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("www.a.cn/x?q=1", "https://www.a.cn/x?q=1")]
    [InlineData("http://a.com", "http://a.com")]
    [InlineData("https://a.com", "https://a.com")]
    [InlineData("ftp://a.com/f", "ftp://a.com/f")]
    [InlineData("HTTP://A.com", "HTTP://A.com")]
    public void URL_缺协议时补https_已有协议不动(string input, string expected)
    {
        var host = new RecordingShellHost();
        var result = new Launcher(host).OpenUrl(input);

        Assert.True(result.Success);
        Assert.Equal(expected, Assert.Single(host.Opened));
    }

    [Fact]
    public void URL_为空则失败且不调用宿主()
    {
        var host = new RecordingShellHost();
        var result = new Launcher(host).OpenUrl("   ");

        Assert.False(result.Success);
        Assert.Empty(host.Opened);
    }

    // ────────────────────────────── 文件 / 文件夹 ──────────────────────────────

    [Fact]
    public void 文件_优先用target_path()
    {
        using var temp = new TempDataDirectory();
        var target = Path.Combine(temp.Path, "target.txt");
        var source = Path.Combine(temp.Path, "source.txt");
        File.WriteAllText(target, "t", new UTF8Encoding(false));
        File.WriteAllText(source, "s", new UTF8Encoding(false));

        var host = new RecordingShellHost();
        var result = new Launcher(host).Open(new IconModel
        {
            Type = IconType.File,
            SourcePath = source,
            TargetPath = target,
        });

        Assert.True(result.Success);
        Assert.Equal(target, Assert.Single(host.Opened));
    }

    [Fact]
    public void 文件_target_path为空时退回source_path()
    {
        using var temp = new TempDataDirectory();
        var source = Path.Combine(temp.Path, "source.txt");
        File.WriteAllText(source, "s", new UTF8Encoding(false));

        var host = new RecordingShellHost();
        var result = new Launcher(host).Open(new IconModel { Type = IconType.File, SourcePath = source });

        Assert.True(result.Success);
        Assert.Equal(source, Assert.Single(host.Opened));
    }

    [Fact]
    public void 文件_不存在则失败且不调用宿主()
    {
        using var temp = new TempDataDirectory();
        var missing = Path.Combine(temp.Path, "没有这个.txt");

        var host = new RecordingShellHost();
        var result = new Launcher(host).Open(new IconModel { Type = IconType.File, SourcePath = missing });

        Assert.False(result.Success);
        Assert.Contains("File not found", result.Error);
        Assert.Empty(host.Opened);
    }

    [Fact]
    public void 文件夹_存在则用默认程序打开()
    {
        using var temp = new TempDataDirectory();

        var host = new RecordingShellHost();
        var result = new Launcher(host).Open(new IconModel { Type = IconType.Folder, SourcePath = temp.Path });

        Assert.True(result.Success);
        Assert.Equal(temp.Path, Assert.Single(host.Opened));
    }

    // ────────────────────────────── 快捷方式 ──────────────────────────────

    [Fact]
    public void 快捷方式_优先把lnk本身交给Shell()
    {
        using var temp = new TempDataDirectory();
        var lnk = Path.Combine(temp.Path, "有.lnk");
        var target = Path.Combine(temp.Path, "target.exe");
        File.WriteAllText(lnk, "x", new UTF8Encoding(false));
        File.WriteAllText(target, "y", new UTF8Encoding(false));

        var host = new RecordingShellHost();
        var result = new Launcher(host).Open(new IconModel
        {
            Type = IconType.Shortcut,
            SourcePath = lnk,
            TargetPath = target,
        });

        Assert.True(result.Success);
        Assert.Equal(lnk, Assert.Single(host.Opened));   // 走 .lnk 交给 Shell，而不是直接起 target
    }

    [Fact]
    public void 快捷方式_lnk不在但目标在_则打开目标()
    {
        using var temp = new TempDataDirectory();
        var target = Path.Combine(temp.Path, "target.exe");
        File.WriteAllText(target, "y", new UTF8Encoding(false));

        var host = new RecordingShellHost();
        var result = new Launcher(host).Open(new IconModel
        {
            Type = IconType.Shortcut,
            SourcePath = Path.Combine(temp.Path, "已删除.lnk"),
            TargetPath = target,
        });

        Assert.True(result.Success);
        Assert.Equal(target, Assert.Single(host.Opened));
    }

    [Fact]
    public void 快捷方式_两者都不在则失败()
    {
        using var temp = new TempDataDirectory();

        var host = new RecordingShellHost();
        var result = new Launcher(host).Open(new IconModel
        {
            Type = IconType.Shortcut,
            SourcePath = Path.Combine(temp.Path, "a.lnk"),
            TargetPath = Path.Combine(temp.Path, "b.exe"),
        });

        Assert.False(result.Success);
        Assert.Empty(host.Opened);
        Assert.Empty(host.Started);
    }

    // ────────────────────────────── 命令 ──────────────────────────────

    [Fact]
    public void 命令_目标为空则失败()
    {
        var host = new RecordingShellHost();
        var result = new Launcher(host).LaunchCommand("   ", "-x");

        Assert.False(result.Success);
        Assert.Equal("No executable specified.", result.Error);
        Assert.Empty(host.Started);
    }

    [Fact]
    public void 命令_参数按Windows规则切分_工作目录存在才传()
    {
        using var temp = new TempDataDirectory();

        var host = new RecordingShellHost();
        var result = new Launcher(host).LaunchCommand(
            "app.exe", "--a \"含 空格\" --b", temp.Path);

        Assert.True(result.Success);
        var (exe, args, cwd) = Assert.Single(host.Started);
        Assert.Equal("app.exe", exe);
        Assert.Equal(new[] { "--a", "含 空格", "--b" }, args);
        Assert.Equal(temp.Path, cwd);
    }

    [Fact]
    public void 命令_工作目录不存在时忽略它_与原版一致()
    {
        using var temp = new TempDataDirectory();

        var host = new RecordingShellHost();
        new Launcher(host).LaunchCommand("app.exe", null, Path.Combine(temp.Path, "不存在"));

        var (_, args, cwd) = Assert.Single(host.Started);
        Assert.Empty(args);
        Assert.Null(cwd);
    }

    [Fact]
    public void 命令_宿主报错时返回失败而不是抛异常()
    {
        var host = new RecordingShellHost { ThrowOnStartProcess = new InvalidOperationException("起不来") };

        var result = new Launcher(host).LaunchCommand("app.exe");

        Assert.False(result.Success);
        Assert.Equal("起不来", result.Error);
    }

    [Fact]
    public void 打开失败_宿主报错时返回失败而不是抛异常()
    {
        using var temp = new TempDataDirectory();
        var file = Path.Combine(temp.Path, "a.txt");
        File.WriteAllText(file, "x", new UTF8Encoding(false));

        var host = new RecordingShellHost { ThrowOnShellOpen = new InvalidOperationException("打不开") };

        var result = new Launcher(host).OpenFileOrFolder(file);

        Assert.False(result.Success);
        Assert.Equal("打不开", result.Error);
    }

    // ────────────────────────────── 参数切分 ──────────────────────────────

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("a", 1)]
    [InlineData("a  b", 2)]
    [InlineData("-x --y=1", 2)]
    [InlineData("\"a b\" c", 2)]
    public void 参数切分_基本用例(string input, int expectedCount)
    {
        Assert.Equal(expectedCount, CommandLineSplitter.Split(input).Count);
    }

    [Fact]
    public void 参数切分_引号内的空格不切分()
    {
        Assert.Equal(new[] { "含 空格", "c" }, CommandLineSplitter.Split("\"含 空格\" c"));
    }

    [Fact]
    public void 参数切分_反斜杠不当转义_原版为此特意用posix等于False()
    {
        // "C:\Program Files\App" 用引号包起来 → 一个参数；反斜杠不被吃掉
        Assert.Equal(new[] { @"C:\Program Files\App" }, CommandLineSplitter.Split("\"C:\\Program Files\\App\""));

        // 不加引号 → 按空白切成两段（Windows 与 Python shlex(posix=False) 都是这个行为）
        Assert.Equal(new[] { @"C:\Program", @"Files\App" }, CommandLineSplitter.Split(@"C:\Program Files\App"));
    }

    [Fact]
    public void 参数切分_单引号在Windows里不是引号()
    {
        // Python 的 shlex(posix=False) 会把 'a b' 当成一个带引号的 token 保留下来，
        // 但 Windows 命令行里单引号无特殊含义 —— 两者最终交给子进程的都是两个参数
        Assert.Equal(new[] { "'a", "b'" }, CommandLineSplitter.Split("'a b'"));
    }

    [Fact]
    public void 参数切分_空参数是null时返回空列表()
    {
        Assert.Empty(CommandLineSplitter.Split(null));
    }
}
