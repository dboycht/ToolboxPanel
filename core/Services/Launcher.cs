// Launcher.cs —— ToolboxPanel v2 · W2 系统能力
//
// 对应原 Python 的 src/toolbox/services/launcher.py（打开文件/URL/命令）。
//
// ⚠️ 两条设计取舍（都是为了「能单测」与「不打扰用户」）：
//   1. 系统调用被抽成 <see cref="IShellHost"/>：单测注入假实现，
//      **绝不真的开窗口/起进程**（ERROR.md E5 的纪律）；
//   2. **失败不抛异常**，而是返回 <see cref="LaunchResult"/>。
//      原版是抛 FileNotFoundError / ValueError 让 UI 去 catch；返回结果对 WinUI 更顺手，
//      错误文案与原版保持一致，便于将来对照。

using System.Diagnostics;
using System.Runtime.InteropServices;
using ToolboxPanel.Core.Models;

namespace ToolboxPanel.Core.Services;

/// <summary>一次打开动作的结果。<see cref="Error"/> 为空表示成功。</summary>
public sealed record LaunchResult(bool Success, string? Error = null)
{
    public static readonly LaunchResult Ok = new(true);

    public static LaunchResult Fail(string error) => new(false, error);
}

/// <summary>系统能力的抽象（真实实现见 <see cref="WindowsShellHost"/>；单测用假实现）。</summary>
public interface IShellHost
{
    /// <summary>用默认程序打开路径/URL/.lnk —— 等价于 ShellExecute。</summary>
    void ShellOpen(string path);

    /// <summary>启动可执行文件（不经过 shell，参数按 Windows 规则重新加引号）。</summary>
    void StartProcess(string executable, IReadOnlyList<string> arguments, string? workingDirectory);
}

/// <summary>真实实现：ShellExecute 与 Process.Start。</summary>
public sealed class WindowsShellHost : IShellHost
{
    public void ShellOpen(string path)
    {
        // UseShellExecute = true → 走 ShellExecuteEx：
        //   · .lnk 交给 Windows Shell 处理（UWP / 系统工具才不会 WinError 5，
        //     原版 launcher.py 也是用 os.startfile 处理 .lnk 的）；
        //   · URL 交给默认浏览器；文件/文件夹交给默认关联程序。
        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = true,
        };

        Process.Start(startInfo);
    }

    public void StartProcess(string executable, IReadOnlyList<string> arguments, string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            // 不走 shell，避免命令行被二次解析（路径含空格/中文时最容易出问题）
            UseShellExecute = false,

            // 原版用的是 DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP；
            // .NET 的 ProcessStartInfo 没有 DETACHED_PROCESS，用 CreateNoWindow 达到
            // 「不弹控制台窗口」的同等效果。子进程本来就不会随父进程退出。
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
    }
}

/// <summary>把图标变成一次打开动作。</summary>
public sealed class Launcher
{
    private readonly IShellHost _shell;

    /// <summary>不传 <paramref name="shell"/> 时使用真实的 Windows 实现。</summary>
    public Launcher(IShellHost? shell = null) => _shell = shell ?? new WindowsShellHost();

    /// <summary>按图标类型打开（对应原版 <c>Launcher.open()</c>）。</summary>
    public LaunchResult Open(IconModel icon)
    {
        ArgumentNullException.ThrowIfNull(icon);

        return icon.Type switch
        {
            IconType.Url => OpenUrl(icon.SourcePath),
            IconType.File => OpenFileOrFolder(PreferTarget(icon)),
            IconType.Folder => OpenFileOrFolder(PreferTarget(icon)),
            IconType.Shortcut => OpenShortcut(icon),
            IconType.Command => LaunchCommand(icon.TargetPath, icon.Arguments, icon.WorkingDir),
            _ => LaunchResult.Fail($"未知的图标类型: {icon.Type}"),
        };
    }

    /// <summary>打开 URL（没写协议时补 https://，与原版一致）。</summary>
    public LaunchResult OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return LaunchResult.Fail("URL 为空");
        }

        var normalized = url;
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "https://" + normalized;
        }

        try
        {
            _shell.ShellOpen(normalized);
            return LaunchResult.Ok;
        }
        catch (Exception ex)
        {
            return LaunchResult.Fail(ex.Message);
        }
    }

    /// <summary>用默认程序打开文件或文件夹（不存在则失败）。</summary>
    public LaunchResult OpenFileOrFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return LaunchResult.Fail("路径为空");
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return LaunchResult.Fail($"File not found: {path}");
        }

        try
        {
            _shell.ShellOpen(path);
            return LaunchResult.Ok;
        }
        catch (Exception ex)
        {
            return LaunchResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 启动命令行。参数按 Windows 规则切分；工作目录不存在时忽略（与原版一致）。
    /// </summary>
    public LaunchResult LaunchCommand(string? target, string? arguments = null, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return LaunchResult.Fail("No executable specified.");
        }

        var parsedArguments = CommandLineSplitter.Split(arguments);
        var cwd = !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory)
            ? workingDirectory
            : null;

        try
        {
            _shell.StartProcess(target, parsedArguments, cwd);
            return LaunchResult.Ok;
        }
        catch (Exception ex)
        {
            return LaunchResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 快捷方式：**优先把 .lnk 本身交给 Shell**（兼容 UWP / 系统工具），
    /// .lnk 不在时退而启动它的目标；两者都不在才失败（与原版三步逻辑一致）。
    /// </summary>
    private LaunchResult OpenShortcut(IconModel icon)
    {
        if (!string.IsNullOrWhiteSpace(icon.SourcePath) && File.Exists(icon.SourcePath))
        {
            try
            {
                _shell.ShellOpen(icon.SourcePath);
                return LaunchResult.Ok;
            }
            catch (Exception ex)
            {
                return LaunchResult.Fail(ex.Message);
            }
        }

        if (!string.IsNullOrWhiteSpace(icon.TargetPath)
            && (File.Exists(icon.TargetPath) || Directory.Exists(icon.TargetPath)))
        {
            return OpenFileOrFolder(icon.TargetPath);
        }

        return LaunchResult.Fail($"快捷方式目标不存在: {icon.SourcePath}");
    }

    private static string? PreferTarget(IconModel icon)
        => string.IsNullOrWhiteSpace(icon.TargetPath) ? icon.SourcePath : icon.TargetPath;
}

/// <summary>
/// 命令行参数切分。用 Windows 自己的 <c>CommandLineToArgvW</c>，
/// 而不是「按空格 split」——后者会把 <c>"C:\Program Files\App"</c> 切成两段。
///
/// 与原版 Python 的 <c>shlex.split(posix=False)</c> 的差别：posix=False 会把引号**保留**在
/// token 里（因为原版要把 argv 再拼回命令行）；我们这里返回裸参数，
/// 由 <c>ProcessStartInfo.ArgumentList</c> 负责重新加引号，最终子进程看到的内容一致。
/// </summary>
public static class CommandLineSplitter
{
    public static IReadOnlyList<string> Split(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Array.Empty<string>();
        }

        IntPtr argv = IntPtr.Zero;
        try
        {
            argv = CommandLineToArgvW(commandLine, out int count);
            if (argv == IntPtr.Zero || count <= 0)
            {
                // 极端情况下退回「按空白切分」，总比丢掉参数好
                return commandLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            }

            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                IntPtr item = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                result[i] = Marshal.PtrToStringUni(item) ?? string.Empty;
            }

            return result;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return commandLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        }
        finally
        {
            if (argv != IntPtr.Zero)
            {
                LocalFree(argv);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
