// PythonRunner.cs —— 单测辅助：条件式调用本机 Python（用于"与真实原版实现对照"的测试）
//
// 为什么共用：非开发副本（canonical / CI / 把测试产物拷到仓外跑）里没有原版源码 ⇒ 这类测试
// **显式跳过**（报告里看得见"已跳过"，不是静默通过）；开发副本 + python 齐备时才真的跑原版代码逐条比对。
//
// ⚠️ 纪律（2026-09 修"假绿"）：开发副本里明明有原版源码、却探测不到 python ⇒ **抛异常显式失败**。
//    以前那套 `if (!PythonRunner.IsAvailable()) return;` 会让报告显示 passed —— 那是假绿，不是跳过。
//
// ⚠️ 这些测试只读，不写用户数据；脚本一律落在调用方给的临时目录里。

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

internal static class PythonRunner
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, bool> ProbeCache = new(StringComparer.Ordinal);
    private static bool _repositoryRootProbed;
    private static string? _repositoryRoot;

    /// <summary>找到的仓库根；null = 不在开发副本里（canonical / CI / 仓外跑测试）。只找一次。</summary>
    public static string? RepositoryRoot
    {
        get
        {
            lock (Gate)
            {
                if (!_repositoryRootProbed)
                {
                    _repositoryRoot = AppPaths.FindRepositoryRoot(AppContext.BaseDirectory);
                    _repositoryRootProbed = true;
                }

                return _repositoryRoot;
            }
        }
    }

    /// <summary>
    /// "开发副本"判据：找得到仓库根，且仓库根下确实躺着原版 Python 源码（<c>src/toolbox/i18n.py</c>）。
    /// 只有开发副本才有"本机必须能跑 python"这个期望。
    /// </summary>
    public static bool IsDevelopmentCopy
    {
        get
        {
            var root = RepositoryRoot;
            return root is not null
                && File.Exists(Path.Combine(root, "src", "toolbox", "i18n.py"));
        }
    }

    /// <summary>
    /// 本测试项目"能不能用真实 Python 做对照"的**唯一判据**（探测结果只算一次）：
    /// <list type="number">
    ///   <item>不是开发副本 ⇒ <c>false</c>（调用方**显式跳过**：没有原版源码可对照，不误报）；</item>
    ///   <item>是开发副本、却探测不到 python ⇒ **抛 <see cref="InvalidOperationException"/>**（环境坏了，必须显式失败）；</item>
    ///   <item>是开发副本且 python 可用 ⇒ <c>true</c>。</item>
    /// </list>
    /// </summary>
    public static bool Available => AvailableWith(requiresModule: null);

    /// <summary>
    /// 同 <see cref="Available"/>，但额外要求本机 python 装了 <paramref name="requiresModule"/>
    /// （如 <c>"PyQt6"</c> / <c>"win32com.client"</c>；用 <c>python -c "import X"</c> 探测）。
    /// </summary>
    /// <remarks>
    /// 开发副本里**缺模块**仍然返回 <c>false</c>（调用方跳过）—— 语义与改动前一致；
    /// 只有"连 python 都探测不到"才升级成失败（见 <see cref="Available"/>）。
    /// </remarks>
    public static bool AvailableWith(string? requiresModule)
    {
        if (!IsDevelopmentCopy)
        {
            return false;
        }

        if (!ProbePython(code: null))
        {
            throw new InvalidOperationException(
                "开发副本里明明有原版源码（src\\toolbox\\i18n.py），却探测不到可用的 python —— "
                + "这是环境坏了，必须显式失败，绝不能当「跳过」处理。\n"
                + $"仓库根：{RepositoryRoot}\n"
                + "探测命令：python --version\n"
                + "修好本机 python（见 DEVELOPMENT.md §环境探测结果）后重跑。");
        }

        return requiresModule is null || ProbePython($"import {requiresModule}");
    }

    /// <summary>原版 Python 源码根（仓库根下的 <c>src</c>，作为 <c>sys.path</c> 传给脚本）。</summary>
    public static string OriginalSourceRoot => RequireOriginalDirectory("src");

    /// <summary>
    /// 要一个原版实现里的**文件**的绝对路径（如 <c>src\toolbox\ui\theme.py</c>）。
    /// 开发副本里缺这个文件 ⇒ 抛异常（开发副本里它不该缺席 ⇒ 显式失败，不许静默跳过）。
    /// </summary>
    public static string RequireOriginalFile(params string[] relativeParts)
        => ResolveOriginalPath(expectDirectory: false, relativeParts);

    /// <summary>同 <see cref="RequireOriginalFile"/>，但要求是**目录**。</summary>
    public static string RequireOriginalDirectory(params string[] relativeParts)
        => ResolveOriginalPath(expectDirectory: true, relativeParts);

    private static string ResolveOriginalPath(bool expectDirectory, string[] relativeParts)
    {
        var root = RepositoryRoot
            ?? throw new InvalidOperationException(
                "不在开发副本里，这条测试本该被 [PythonFact] 显式跳过（Skip）—— 说明跳过机制漏了。");

        var path = Path.Combine(new[] { root }.Concat(relativeParts).ToArray());
        var exists = expectDirectory ? Directory.Exists(path) : File.Exists(path);
        if (!exists)
        {
            throw new InvalidOperationException(
                $"开发副本里不该缺「{string.Join(Path.DirectorySeparatorChar, relativeParts)}」（{path}）"
                + "—— 原版源码不完整 = 环境坏了，必须显式失败（见 DEVELOPMENT.md §环境探测结果）。");
        }

        return path;
    }

    /// <summary>跑一个 python 脚本（`python &lt;script&gt; &lt;args...&gt;`），返回退出码与输出。</summary>
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

    /// <summary>探测一次 python（<paramref name="code"/> 为 null 时是 <c>python --version</c>，否则 <c>python -c "&lt;code&gt;"</c>）；结果缓存。</summary>
    private static bool ProbePython(string? code)
    {
        var key = code ?? "--version";

        lock (Gate)
        {
            if (ProbeCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        bool ok;
        try
        {
            var (exitCode, _, _) = code is null
                ? Run("--version", Array.Empty<string>(), workingDirectory: null)
                : Run("-c", new[] { code }, workingDirectory: null);
            ok = exitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            ok = false;   // PATH 上没有 python
        }

        lock (Gate)
        {
            ProbeCache[key] = ok;
        }

        return ok;
    }
}
