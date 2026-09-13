// SampleIcons.cs —— 示例图标清单（首次使用 / 内容为空时自动补充）
//
// 为什么需要它：v2.0.1 目前**还没有"新建图标"的界面**（那属于 W5），所以一个空的
// `tabs.json` 会让用户面对一个空页面、什么也做不了。这里给出一份"开箱可见"的示例，
// 用的全是 Windows 自带程序（本机实测存在），既能立刻看到效果，也不会往用户机器上写奇怪的东西。
//
// ⚠️ 约定：
//   · 只放**系统自带**的目标（System32 / Windows 目录），不引用用户私有路径；
//   · 路径不存在时**跳过**（`BuildForExisting()`），绝不产生点不开的死图标；
//   · 生成的图标与原版语义完全一致（5 类图标各自的字段组合，见 IconModel 注释）。

using ToolboxPanel.Core.Models;

namespace ToolboxPanel.Core.Storage;

/// <summary>示例图标的定义与装配。</summary>
public static class SampleIcons
{
    /// <summary>示例标签页的名字。</summary>
    public const string SampleTabName = "示例";

    /// <summary>一条示例图标的声明。</summary>
    private readonly record struct Spec(
        string DisplayName,
        IconType Type,
        string SourcePath,
        string TargetPath = "",
        string Arguments = "",
        string WorkingDir = "");

    /// <summary>
    /// 示例清单：覆盖**全部 5 种图标类型**（文件 / 文件夹 / 快捷方式 / 网址 / 命令），
    /// 这样用户一眼就能看出五类图标长什么样、行为差别在哪。
    /// </summary>
    private static readonly Spec[] Catalog =
    {
        // ── FILE：可执行文件 ──
        new("记事本", IconType.File, @"C:\Windows\System32\notepad.exe"),
        new("计算器", IconType.File, @"C:\Windows\System32\calc.exe"),
        new("命令提示符", IconType.File, @"C:\Windows\System32\cmd.exe"),
        new("PowerShell", IconType.File, @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"),
        new("资源管理器", IconType.File, @"C:\Windows\explorer.exe"),
        new("任务管理器", IconType.File, @"C:\Windows\System32\taskmgr.exe"),
        new("注册表编辑器", IconType.File, @"C:\Windows\System32\regedt32.exe"),
        new("系统信息", IconType.File, @"C:\Windows\System32\msinfo32.exe"),
        new("磁盘清理", IconType.File, @"C:\Windows\System32\cleanmgr.exe"),
        new("事件查看器", IconType.File, @"C:\Windows\System32\eventvwr.exe"),
        new("设备管理器", IconType.File, @"C:\Windows\System32\devmgmt.msc"),
        new("字符映射表", IconType.File, @"C:\Windows\System32\charmap.exe"),
        new("屏幕键盘", IconType.File, @"C:\Windows\System32\osk.exe"),
        new("控制面板", IconType.File, @"C:\Windows\System32\control.exe"),

        // ── FOLDER：文件夹 ──
        new("配置目录", IconType.Folder, @"C:\Windows\System32\drivers\etc"),

        // ── SHORTCUT：快捷方式（这里直接指向一个文件夹，走"文件夹"图标兜底；本机没有现成 .lnk 就跳过） ──
        new("Windows 目录", IconType.Shortcut, @"C:\Windows"),

        // ── URL：网址（不依赖本机文件，一定会有） ──
        new("示例网站", IconType.Url, "https://example.com"),
        new("必应搜索", IconType.Url, "https://www.bing.com"),

        // ── COMMAND：命令 ──
        new("回显命令", IconType.Command, "cmd.exe /c echo ToolboxPanel", @"cmd.exe", "/c echo ToolboxPanel", @"C:\Windows\System32"),
    };

    /// <summary>按清单造出图标（**跳过目标不存在的**，避免生成点不开的死图标）。</summary>
    public static List<IconModel> BuildForExisting()
    {
        var icons = new List<IconModel>();

        foreach (var spec in Catalog)
        {
            if (!ShouldInclude(spec))
            {
                continue;
            }

            icons.Add(new IconModel
            {
                Type = spec.Type,
                DisplayName = spec.DisplayName,
                SourcePath = spec.SourcePath,
                TargetPath = spec.TargetPath,
                Arguments = spec.Arguments,
                WorkingDir = spec.WorkingDir,
            });
        }

        return icons;
    }

    /// <summary>URL / COMMAND 不依赖本地文件，一律保留；其余要求路径真实存在。</summary>
    private static bool ShouldInclude(Spec spec) => spec.Type switch
    {
        IconType.Url or IconType.Command => true,
        _ => !string.IsNullOrWhiteSpace(spec.SourcePath) && SafeExists(spec.SourcePath),
    };

    private static bool SafeExists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // 路径本身非法（例如用户环境里的怪字符）→ 当作不存在，跳过
            return false;
        }
    }

    /// <summary>
    /// 判断"需不需要补示例"，需要时补上并落盘。
    ///
    /// <para>判定条件很克制：**所有内容页（grid）加起来一个图标都没有**时才动手；
    /// 只要用户已经有一个图标，就什么都不做（绝不打扰已有配置）。</para>
    ///
    /// <para>补的时候**新建一个名为「示例」的标签页**，不改动用户已有的任何页。</para>
    /// </summary>
    /// <returns>真的补了返回 true。</returns>
    public static bool EnsureSampleIcons(DataStore store)
    {
        if (store.Tabs.Count == 0)
        {
            return false;
        }

        // 只要还有任何一个图标，就认为用户已经有内容了
        if (store.Tabs.Any(tab => tab.Icons.Count > 0))
        {
            return false;
        }

        var icons = BuildForExisting();
        if (icons.Count == 0)
        {
            return false;
        }

        var tab = store.AddTab(SampleTabName, TabModel.TypeGrid);
        foreach (var icon in icons)
        {
            store.AddIcon(tab.Id, icon);
        }

        return true;
    }
}
