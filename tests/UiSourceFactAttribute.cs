// UiSourceFactAttribute.cs —— "需要开发副本里的 winui/ 源码"的 xUnit 特性（[UiSourceFact]）
//
// 用途：源码级守门员测试（如"容器的 DataContext 不是数据项"，见 `ERROR.md` E47/E52）要扫
// `winui/**/*.cs`。这类测试在**找不到源码的场合**（把测试产物拷到仓外跑、CI 只跑 Core）必须
// **显式跳过**，而不是静默通过（`PythonFactAttribute` 同一套纪律：报告里要看得见"已跳过"）。
//
// ⚠️ 与 Python 那一族的差异：判据不是"有没有 python"，而是"**仓库根下有没有 winui 目录**"。

namespace ToolboxPanel.Core.Tests;

/// <summary>需要开发副本 <c>winui/</c> 源码的 <c>[Fact]</c>：找不到就显式跳过。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class UiSourceFactAttribute : FactAttribute
{
    public UiSourceFactAttribute()
    {
        if (UiSourceScan.Root is null)
        {
            Skip = "找不到开发副本的 winui/ 源码（仓外跑测试 / CI 只跑 Core）—— 见 UiSourceScan";
        }
    }
}

/// <summary>需要开发副本 <c>winui/</c> 源码的 <c>[Theory]</c>：语义同上。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class UiSourceTheoryAttribute : TheoryAttribute
{
    public UiSourceTheoryAttribute()
    {
        if (UiSourceScan.Root is null)
        {
            Skip = "找不到开发副本的 winui/ 源码（仓外跑测试 / CI 只跑 Core）—— 见 UiSourceScan";
        }
    }
}

/// <summary>定位并读取 <c>winui/</c> 下的 C# 源码（**只读**，供源码级守门员测试用）。</summary>
internal static class UiSourceScan
{
    /// <summary>winui 源码根；null = 找不到（此时相关测试显式跳过）。</summary>
    public static string? Root { get; } = Resolve();

    /// <summary>所有页面/视图的 <c>.cs</c> 文件（排除 <c>bin</c> / <c>obj</c> 等生成目录）。</summary>
    public static IReadOnlyList<string> Files { get; } = Root is null
        ? Array.Empty<string>()
        : Directory.EnumerateFiles(Root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>读源码并**剥掉注释与字符串字面量**（见 <see cref="StripCommentsAndStrings"/>）。</summary>
    public static string ReadCode(string path) => StripCommentsAndStrings(File.ReadAllText(path));

    /// <summary>相对 <see cref="Root"/> 的短路径（报错信息里读起来清爽些）。</summary>
    public static string Relative(string path)
        => Root is null ? path : Path.GetRelativePath(Root, path);

    private static string? Resolve()
    {
        var repositoryRoot = PythonRunner.RepositoryRoot;
        if (repositoryRoot is null)
        {
            return null;
        }

        var winui = Path.Combine(repositoryRoot, "winui");
        return Directory.Exists(winui) ? winui : null;
    }

    /// <summary>
    /// 把源码里的**注释**丢掉、**字符串/字符字面量的内容**抹成空格（换行原样保留，行号不错位）。
    ///
    /// <para>⚠️ 为什么必须剥：守门员扫的是**代码**，而本项目会在注释里**引用**被禁的写法
    /// （"以前这里写的是 <c>is GridViewItem { DataContext: ... }</c>"）——
    /// 不剥的话那条注释会把守门员自己判红（假红）。</para>
    ///
    /// <para>支持的 C# 形态：行注释 <c>//</c>、块注释 <c>/* */</c>、普通字符串（含 <c>\</c> 转义）、
    /// 逐字字符串 <c>@"…""…"</c>、原始字符串 <c>"""…"""</c>、字符字面量 <c>'x'</c>。
    /// 不认识的东西一律原样保留（宁可漏判，不要误判）。</para>
    /// </summary>
    public static string StripCommentsAndStrings(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        int i = 0;

        while (i < source.Length)
        {
            char c = source[i];

            // 行注释：吃到行尾（换行保留）
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            // 块注释：吃到 */（换行保留）
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    if (source[i] == '\n')
                    {
                        result.Append('\n');
                    }

                    i++;
                }

                i = Math.Min(i + 2, source.Length);
                continue;
            }

            // 普通字符串 / 逐字字符串 / 原始字符串
            if (c == '"')
            {
                bool verbatim = i > 0 && source[i - 1] == '@';
                bool raw = i + 2 < source.Length && source[i + 1] == '"' && source[i + 2] == '"';

                result.Append(c);
                i++;

                if (raw)
                {
                    result.Append("\"\"");
                    i += 2;

                    while (i + 2 < source.Length && !(source[i] == '"' && source[i + 1] == '"' && source[i + 2] == '"'))
                    {
                        if (source[i] == '\n')
                        {
                            result.Append('\n');
                        }
                        else
                        {
                            result.Append(' ');
                        }

                        i++;
                    }

                    if (i + 2 < source.Length)
                    {
                        result.Append("\"\"\"");
                        i += 3;
                    }

                    continue;
                }

                while (i < source.Length)
                {
                    char s = source[i];

                    if (verbatim && s == '"' && i + 1 < source.Length && source[i + 1] == '"')
                    {
                        result.Append("\"\"");
                        i += 2;
                        continue;
                    }

                    if (!verbatim && s == '\\' && i + 1 < source.Length)
                    {
                        result.Append("  ");
                        i += 2;
                        continue;
                    }

                    if (s == '"')
                    {
                        result.Append('"');
                        i++;
                        break;
                    }

                    result.Append(s == '\n' ? '\n' : ' ');
                    i++;
                }

                continue;
            }

            // 字符字面量
            if (c == '\'')
            {
                result.Append(c);
                i++;

                while (i < source.Length)
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        result.Append("  ");
                        i += 2;
                        continue;
                    }

                    if (source[i] == '\'')
                    {
                        result.Append('\'');
                        i++;
                        break;
                    }

                    result.Append(source[i] == '\n' ? '\n' : ' ');
                    i++;
                }

                continue;
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }
}
