// ShortcutTests.cs —— W2：.lnk 解析（含与「原版 Python + pywin32」的兼容性）

using System.Diagnostics;
using System.Text;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class ShortcutTests
{
    private static string WindowsDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    [Fact]
    public void 解析真实lnk_五个字段都与原版语义一致()
    {
        using var temp = new TempDataDirectory();
        var lnkPath = Path.Combine(temp.Path, "测试 快捷方式.lnk");
        var target = Path.Combine(WindowsDir, "System32", "notepad.exe");
        var iconFile = Path.Combine(WindowsDir, "System32", "shell32.dll");

        TestShortcutFactory.Create(
            lnkPath,
            targetPath: target,
            arguments: "--flag \"含 空格的参数\" plain",
            workingDirectory: WindowsDir,
            description: "描述：中文说明",
            iconPath: iconFile,
            iconIndex: 3);

        var info = WindowsShortcut.Resolve(lnkPath);

        Assert.NotNull(info);
        Assert.Equal(target, info!.TargetPath);
        Assert.Equal("--flag \"含 空格的参数\" plain", info.Arguments);
        Assert.Equal(WindowsDir, info.WorkingDirectory);
        Assert.Equal("描述：中文说明", info.Description);
        Assert.Equal(iconFile, info.IconPath);
        Assert.Equal(3, info.IconIndex);
        Assert.Equal($"{iconFile},3", info.IconLocation);   // 与原版 pywin32 的 "文件,索引" 写法对齐
    }

    [Fact]
    public void 没有自定义图标时_IconPath与IconLocation都是空串()
    {
        using var temp = new TempDataDirectory();
        var lnkPath = Path.Combine(temp.Path, "无图标.lnk");
        TestShortcutFactory.Create(lnkPath, Path.Combine(WindowsDir, "explorer.exe"));

        var info = WindowsShortcut.Resolve(lnkPath);

        Assert.NotNull(info);
        Assert.Equal(string.Empty, info!.IconPath);
        Assert.Equal(string.Empty, info.IconLocation);
        Assert.Equal(string.Empty, info.Arguments);
        Assert.Equal(string.Empty, info.Description);
    }

    [Fact]
    public void 目标与参数可以为空_不报错()
    {
        using var temp = new TempDataDirectory();
        var lnkPath = Path.Combine(temp.Path, "空目标.lnk");
        TestShortcutFactory.Create(lnkPath, string.Empty);

        var info = WindowsShortcut.Resolve(lnkPath);

        Assert.NotNull(info);
        Assert.Equal(string.Empty, info!.TargetPath);
    }

    [Fact]
    public void 文件不存在_返回null并给出原因()
    {
        using var temp = new TempDataDirectory();

        var info = WindowsShortcut.TryResolve(Path.Combine(temp.Path, "没有这个.lnk"), out var error);

        Assert.Null(info);
        Assert.Equal("文件不存在", error);
    }

    [Fact]
    public void 不是快捷方式_返回null不抛异常()
    {
        using var temp = new TempDataDirectory();
        var notALnk = Path.Combine(temp.Path, "普通文件.lnk");
        File.WriteAllText(notALnk, "这不是快捷方式", new UTF8Encoding(false));

        var info = WindowsShortcut.TryResolve(notALnk, out var error);

        Assert.Null(info);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 路径为空_返回null(string? path)
    {
        Assert.Null(WindowsShortcut.Resolve(path));
    }

    [Fact]
    public void 原版Python加pywin32创建的lnk_能被CSharp解析()
    {
        // 条件式：需要 PATH 上有 python 且装了 pywin32（原版 requirements.txt 有）
        if (!PythonWithPywin32IsAvailable())
        {
            return;
        }

        using var temp = new TempDataDirectory();
        var lnkPath = Path.Combine(temp.Path, "python 造的.lnk");
        var target = Path.Combine(WindowsDir, "System32", "calc.exe");

        var script = """
            import sys
            import win32com.client
            shell = win32com.client.Dispatch("WScript.Shell")
            sc = shell.CreateShortcut(sys.argv[1])
            sc.TargetPath = sys.argv[2]
            sc.Arguments = "--py-flag \"a b\""
            sc.WorkingDirectory = sys.argv[3]
            sc.Description = "由 Python 创建"
            sc.IconLocation = sys.argv[4] + ",2"
            sc.Save()
            print("CREATED")
            """;

        var scriptPath = Path.Combine(temp.Path, "make_lnk.py");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
        var iconFile = Path.Combine(WindowsDir, "System32", "shell32.dll");

        var (exitCode, stdout, stderr) = RunPython(
            scriptPath, lnkPath, target, WindowsDir, iconFile);
        Assert.True(exitCode == 0, $"Python 造 .lnk 失败：\n{stderr}\n{stdout}");
        Assert.Contains("CREATED", stdout);

        var info = WindowsShortcut.Resolve(lnkPath);

        Assert.NotNull(info);
        Assert.Equal(target, info!.TargetPath);
        Assert.Equal("--py-flag \"a b\"", info.Arguments);
        Assert.Equal(WindowsDir, info.WorkingDirectory);
        Assert.Equal("由 Python 创建", info.Description);
        Assert.Equal(iconFile, info.IconPath);
        Assert.Equal(2, info.IconIndex);
    }

    // ────────────────────────────── 工具 ──────────────────────────────

    private static bool PythonWithPywin32IsAvailable()
    {
        try
        {
            var (exitCode, _, _) = RunPython("-c", "import win32com.client");
            return exitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunPython(params string[] arguments)
    {
        var psi = new ProcessStartInfo("python")
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

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
        return (process.HasExited ? process.ExitCode : -1, stdout, stderr);
    }
}
