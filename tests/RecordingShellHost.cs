// RecordingShellHost.cs —— 单测共用的假宿主
//
// 只记录「本来要做什么」，**绝不真的开窗口/起进程**（ERROR.md E5 的纪律）。
// 抽成一个共用类（而不是每个测试类各写一份）：`IShellHost` 每加一个系统动作，
// 只需在这里补一行，不会出现"某个测试类忘了实现新成员导致编译不过"的连锁修改。

using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public sealed class RecordingShellHost : IShellHost
{
    /// <summary>ShellOpen 的调用记录（用默认程序打开）。</summary>
    public List<string> Opened { get; } = new();

    /// <summary>StartProcess 的调用记录（起进程）。</summary>
    public List<(string Executable, IReadOnlyList<string> Arguments, string? WorkingDirectory)> Started { get; } = new();

    /// <summary>ShellOpenWith 的调用记录（Windows「打开方式」对话框）。</summary>
    public List<string> OpenWithRequests { get; } = new();

    /// <summary>ShellRevealInExplorer 的调用记录（explorer /select）。</summary>
    public List<string> RevealRequests { get; } = new();

    public Exception? ThrowOnShellOpen { get; set; }

    public Exception? ThrowOnStartProcess { get; set; }

    public Exception? ThrowOnOpenWith { get; set; }

    public Exception? ThrowOnReveal { get; set; }

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

    public void ShellOpenWith(string path)
    {
        if (ThrowOnOpenWith is not null)
        {
            throw ThrowOnOpenWith;
        }

        OpenWithRequests.Add(path);
    }

    public void ShellRevealInExplorer(string path)
    {
        if (ThrowOnReveal is not null)
        {
            throw ThrowOnReveal;
        }

        RevealRequests.Add(path);
    }
}
