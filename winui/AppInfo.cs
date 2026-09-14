// AppInfo.cs —— 运行时环境事实（版本号 / 运行时 / SDK），供「关于」对话框与自检日志用
//
// ⚠️ 版本号**只能从程序集读**：`winui/ToolboxPanel.WinUI.csproj` 的 `<Version>` 是本实现的
//    单一来源（发版时和 `src/toolbox/__init__.py` 一起改，见 DEVELOPMENT.md §4.1）。
//    在代码里写死版本号 = 造出第二个来源，迟早对不上（本项目已经因为"版本号散落多处"返工过）。

using System.Reflection;
using Microsoft.UI.Xaml;

namespace ToolboxPanel;

/// <summary>运行时环境事实。全部读实际值，任何一项取不到都回落「未知」，绝不抛。</summary>
internal static class AppInfo
{
    /// <summary>本程序版本号（如 <c>2.0.2</c>）。</summary>
    public static string Version => ReadVersion(typeof(App).Assembly);

    /// <summary>.NET 运行时版本（如 <c>9.0.0</c>）。</summary>
    public static string DotNetVersion
    {
        get
        {
            try
            {
                return Environment.Version.ToString();
            }
            catch (Exception)
            {
                return "未知";
            }
        }
    }

    /// <summary>Windows App SDK（WinUI 3）版本。</summary>
    public static string WindowsAppSdkVersion => ReadVersion(typeof(Application).Assembly);

    /// <summary>系统版本串（如 <c>Microsoft Windows NT 10.0.26200.0</c>）。</summary>
    public static string OsVersion
    {
        get
        {
            try
            {
                return Environment.OSVersion.VersionString;
            }
            catch (Exception)
            {
                return "未知";
            }
        }
    }

    /// <summary>主程序完整路径（排查"用户跑的到底是哪一份"时最有用的一条）。</summary>
    public static string ExecutablePath
    {
        get
        {
            try
            {
                return Environment.ProcessPath ?? "未知";
            }
            catch (Exception)
            {
                return "未知";
            }
        }
    }

    /// <summary>
    /// 「关于」里的诊断项（顺序即展示顺序）。
    /// ⚠️ 「数据目录 / 运行模式」由 Core 的 <c>AboutInfo.Create</c> 固定放在最前，这里不再重复。
    /// </summary>
    public static IReadOnlyList<(string Label, string Value)> Diagnostics() => new[]
    {
        (".NET 运行时", DotNetVersion),
        ("Windows App SDK", WindowsAppSdkVersion),
        ("系统", OsVersion),
        ("程序路径", ExecutablePath),
    };

    /// <summary>
    /// 读程序集版本：优先 `AssemblyInformationalVersion`（就是 csproj 的 `<Version>`），
    /// 退而求其次用 `AssemblyVersion`。
    /// ⚠️ 顺手剥掉 `+&lt;commit&gt;` 后缀：csproj 已经关了 `IncludeSourceRevisionInInformationalVersion`，
    ///    这里兜一层，免得将来谁改了 csproj 就在"关于"里露出一个提交号。
    /// </summary>
    private static string ReadVersion(Assembly assembly)
    {
        try
        {
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                var plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational.Trim();
            }

            // 2.0.2.0 → 2.0.2
            return assembly.GetName().Version?.ToString(3) ?? "未知";
        }
        catch (Exception)
        {
            return "未知";
        }
    }
}
