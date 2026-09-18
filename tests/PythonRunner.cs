// PythonRunner.cs —— 单测辅助：条件式调用本机 Python（用于"与真实原版实现对照"的测试）
//
// 为什么要共用：CI/纯 canonical 环境里没有 `src/toolbox` 或没有 python ⇒ 这类测试**静默跳过**；
// 有 python 时才真的跑原版代码逐条比对（W1 的 `CrossImplementationTests` 就是这套）。
// ⚠️ 这些测试只读，不写用户数据；脚本一律落在调用方给的临时目录里。

using System.Diagnostics;
using System.Text;

namespace ToolboxPanel.Core.Tests;

internal static class PythonRunner
{
    /// <summary>PATH 上有没有可用的 python（找不到返回 false，调用方直接 return 跳过）。</summary>
    public static bool IsAvailable()
    {
        try
        {
            var (exitCode, _, _) = Run("--version", Array.Empty<string>(), workingDirectory: null);
            return exitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }

    /// <summary>跑一个 python 脚本（`python <script> <args...>`），返回退出码与输出。</summary>
    public static (int ExitCode, string StdOut, string StdErr) RunScript(
        string scriptPath, IEnumerable<string> arguments)
        => Run(scriptPath, arguments, workingDirectory: null);

    public static (int ExitCode, string StdOut, string StdErr) Run(
        string fileName, IEnumerable<string> arguments, string? workingDirectory)
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

        psi.ArgumentList.Add(fileName);
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        // 中文文案必须原样过管道（PS 管道那套编码坑在这里也要防）
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
