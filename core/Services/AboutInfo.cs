// AboutInfo.cs —— ToolboxPanel v2 · W5 逻辑层：「关于」对话框的内容聚合（纯逻辑、可单测）
//
// 内容基准 = 原版 v1.11.6 的 QMessageBox.about（i18n.py 的 app.about.*）：
//   标题 + 版本 + 作者 + 项目地址 + 6 条操作提示。
// 在 WinUI 线里**另外**附一份「诊断信息」（数据目录 / 运行模式 / 运行时版本等），
// 用户报 bug 时截图就能把环境说清楚 —— 环境事实（.NET/WindowsAppSDK 版本等）由 WinUI 侧
// 采集后通过 extraDiagnostics 传进来，Core 只负责「组装 + 纯文本渲染 + 兜底」。
//
// 为什么放 Core：这些都是纯字符串组装逻辑，天生可单测；
// 且将来 i18n 到位时，把 Features / 各标签换成 key 只动这一个文件。

namespace ToolboxPanel.Core.Services;

/// <summary>「关于」对话框要显示的全部内容（UI 只负责展示，内容与兜底都在这里）。</summary>
public sealed record AboutInfo
{
    /// <summary>对话框标题（原版 app.about.title 的「工具箱」，这里用产品名）。</summary>
    public required string Title { get; init; }

    /// <summary>显示用版本号（如 <c>2.0.2</c>）；拿不到时回落「未知」。</summary>
    public required string Version { get; init; }

    /// <summary>副标题（原版「手机桌面风格的启动器」）。</summary>
    public required string Subtitle { get; init; }

    /// <summary>作者。</summary>
    public required string Author { get; init; }

    /// <summary>项目地址（GitHub）。</summary>
    public required string ProjectUrl { get; init; }

    /// <summary>操作提示（原版那 6 条，顺序不变）。</summary>
    public required IReadOnlyList<string> Features { get; init; }

    /// <summary>诊断键值对（数据目录 / 运行模式 / 运行时版本等，展示顺序即添加顺序）。</summary>
    public required IReadOnlyList<(string Label, string Value)> Diagnostics { get; init; }

    /// <summary>原版那 6 条操作提示（v1.11.6 i18n 的 app.about.text 逐条照搬）。</summary>
    public static readonly IReadOnlyList<string> DefaultFeatures = new[]
    {
        "从资源管理器拖入文件/文件夹/快捷方式即可创建图标",
        "双击图标打开，右键查看更多选项",
        "右键空白区域创建 URL / 命令图标",
        "图标和标签页均可拖动排序",
        "数据自动保存到 data/ 文件夹",
        "支持搜索过滤、图标大小切换、打开方式",
    };

    /// <summary>
    /// 组装「关于」内容。
    /// </summary>
    /// <param name="version">运行时读到的版本号；空/空白一律回落「未知」（发版姿势错误也不该让对话框炸）。</param>
    /// <param name="dataDirectory">数据目录（诊断用）。</param>
    /// <param name="isDemo">演示模式（诊断用）。</param>
    /// <param name="extraDiagnostics">环境事实（如 .NET 版本、WindowsAppSDK 版本）；追加在「数据目录/运行模式」之后。</param>
    public static AboutInfo Create(
        string? version,
        string? dataDirectory,
        bool isDemo,
        IReadOnlyList<(string Label, string Value)>? extraDiagnostics = null)
    {
        var diagnostics = new List<(string Label, string Value)>
        {
            ("数据目录", string.IsNullOrWhiteSpace(dataDirectory) ? "（未定位）" : dataDirectory),
            ("运行模式", isDemo ? "演示模式（不读写数据文件）" : "正常模式"),
        };

        if (extraDiagnostics is not null)
        {
            diagnostics.AddRange(extraDiagnostics.Where(d => !string.IsNullOrWhiteSpace(d.Label)));
        }

        return new AboutInfo
        {
            Title = "ToolboxPanel",
            Version = string.IsNullOrWhiteSpace(version) ? "未知" : version.Trim(),
            Subtitle = "手机桌面风格的启动器",
            Author = "dboycht",
            ProjectUrl = "https://github.com/dboycht/ToolboxPanel",
            Features = DefaultFeatures,
            Diagnostics = diagnostics,
        };
    }

    /// <summary>
    /// 整份内容的纯文本形态（标题/版本/作者/项目地址/操作提示/诊断）——
    /// 既是「复制诊断信息」的基础，也是万一对话框组件出问题时的回退展示。
    /// </summary>
    public string ToPlainText()
    {
        var lines = new List<string>
        {
            $"{Title} v{Version} — {Subtitle}",
            $"作者: {Author}",
            $"项目地址: {ProjectUrl}",
            string.Empty,
        };

        foreach (var feature in Features)
        {
            lines.Add($"• {feature}");
        }

        if (Diagnostics.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("诊断信息:");
            foreach (var (label, value) in Diagnostics)
            {
                lines.Add($"  {label}: {value}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
