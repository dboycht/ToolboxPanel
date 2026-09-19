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
    /// <summary>示例标签页的名字（i18n key <c>data.sample_tab</c>；跟随创建时的界面语言）。</summary>
    public static string SampleTabName => I18n.T("data.sample_tab");

    /// <summary>一条示例图标的声明（<paramref name="NameKey"/> 是文案 key —— 名字跟随创建时的语言）。</summary>
    private readonly record struct Spec(
        string NameKey,
        IconType Type,
        string SourcePath,
        string TargetPath = "",
        string Arguments = "",
        string WorkingDir = "");

    /// <summary>
    /// 示例清单：覆盖**全部 5 种图标类型**（文件 / 文件夹 / 快捷方式 / 网址 / 命令），
    /// 这样用户一眼就能看出五类图标长什么样、行为差别在哪。
    /// ⚠️ 名字是**数据**（首次运行写进 tabs.json）：跟随创建时的界面语言，之后切语言**不会**改写已有数据。
    /// </summary>
    private static readonly Spec[] Catalog =
    {
        // ── FILE：可执行文件 ──
        new("sample.notepad", IconType.File, @"C:\Windows\System32\notepad.exe"),
        new("sample.calc", IconType.File, @"C:\Windows\System32\calc.exe"),
        new("sample.cmd", IconType.File, @"C:\Windows\System32\cmd.exe"),
        new("sample.powershell", IconType.File, @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"),
        new("sample.explorer", IconType.File, @"C:\Windows\explorer.exe"),
        new("sample.taskmgr", IconType.File, @"C:\Windows\System32\taskmgr.exe"),
        new("sample.regedit", IconType.File, @"C:\Windows\System32\regedt32.exe"),
        new("sample.msinfo", IconType.File, @"C:\Windows\System32\msinfo32.exe"),
        new("sample.cleanmgr", IconType.File, @"C:\Windows\System32\cleanmgr.exe"),
        new("sample.eventvwr", IconType.File, @"C:\Windows\System32\eventvwr.exe"),
        new("sample.devmgmt", IconType.File, @"C:\Windows\System32\devmgmt.msc"),
        new("sample.charmap", IconType.File, @"C:\Windows\System32\charmap.exe"),
        new("sample.osk", IconType.File, @"C:\Windows\System32\osk.exe"),
        new("sample.control", IconType.File, @"C:\Windows\System32\control.exe"),

        // ── FOLDER：文件夹 ──
        new("sample.etc", IconType.Folder, @"C:\Windows\System32\drivers\etc"),

        // ── SHORTCUT：快捷方式（这里直接指向一个文件夹，走"文件夹"图标兜底；本机没有现成 .lnk 就跳过） ──
        new("sample.windows", IconType.Shortcut, @"C:\Windows"),

        // ── URL：网址（不依赖本机文件，一定会有） ──
        new("sample.website", IconType.Url, "https://example.com"),
        new("sample.bing", IconType.Url, "https://www.bing.com"),

        // ── COMMAND：命令 ──
        new("sample.echo", IconType.Command, "cmd.exe /c echo ToolboxPanel", @"cmd.exe", "/c echo ToolboxPanel", @"C:\Windows\System32"),
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
                DisplayName = I18n.T(spec.NameKey),   // 名字跟随"创建这一刻"的界面语言
                SourcePath = spec.SourcePath,
                TargetPath = ResolveTargetPath(spec),
                Arguments = spec.Arguments,
                WorkingDir = spec.WorkingDir,
            });
        }

        return icons;
    }

    /// <summary>
    /// 目标路径：**文件/文件夹/快捷方式与「新建图标」保持同一套字段口径**（两条路径都填同一个值）。
    ///
    /// <para>⚠️ 示例清单里 <c>TargetPath</c> 是可缺省的，而 <c>IconEditor.Create</c>（FILE/FOLDER 分支）
    /// 与 <c>DropImporter</c> 都是 `source_path` 与 `target_path` 一起填。只填一个虽然靠
    /// "读取侧优先 target、空了退回 source"侥幸不出错，但那是**两套字段口径躺着等下次改读取侧**。
    /// URL / COMMAND 是特殊语义（URL 两边都是网址；COMMAND 的 <c>source_path</c> 是"命令 + 参数"整串），
    /// 保持原样。</para>
    /// </summary>
    private static string ResolveTargetPath(Spec spec)
    {
        if (!string.IsNullOrWhiteSpace(spec.TargetPath))
        {
            return spec.TargetPath;
        }

        return spec.Type is IconType.Url or IconType.Command ? spec.TargetPath : spec.SourcePath;
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

        // ⚠️ 用批量入口：`AddIcon` 每加一个就整份重写 tabs.json，
        //    示例清单有 20 个图标 ⇒ 逐个加就是 20 次全量落盘（慢盘上肉眼可见地卡）。
        store.AddIcons(tab.Id, icons);

        return true;
    }
}
