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

    /// <summary>
    /// 原版那 6 条操作提示 —— **直接从原版 <c>app.about.text</c> 里解析出来**（单一来源）。
    ///
    /// <para>为什么不另存 6 条独立文案：原版就是一段整文本，各条之间隔着一堆特殊字符
    /// （英文里用的是**不换行连字符 U+2011**）。另存一份必然会漂移，索性按行解析 ——
    /// 顺带保证"对话框里那 6 条"与"原版整段文本"逐字一致（有一条单测钉住）。</para>
    /// <para>每次读取都取当前语言，所以切换语言后重开对话框就是新语言。</para>
    /// </summary>
    public static IReadOnlyList<string> DefaultFeatures
    {
        get
        {
            var text = I18n.T("app.about.text", ("version", string.Empty));
            var features = new List<string>();

            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("• ", StringComparison.Ordinal))
                {
                    features.Add(trimmed[2..].Trim());
                }
            }

            return features;
        }
    }

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
            (I18n.T("about.data_dir"), string.IsNullOrWhiteSpace(dataDirectory) ? I18n.T("about.unlocated") : dataDirectory),
            (I18n.T("about.mode"), isDemo ? I18n.T("about.mode.demo") : I18n.T("about.mode.normal")),
        };

        if (extraDiagnostics is not null)
        {
            diagnostics.AddRange(extraDiagnostics.Where(d => !string.IsNullOrWhiteSpace(d.Label)));
        }

        return new AboutInfo
        {
            Title = "ToolboxPanel",
            Version = string.IsNullOrWhiteSpace(version) ? I18n.T("about.unknown") : version.Trim(),
            Subtitle = I18n.T("about.subtitle"),
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
            I18n.T("about.author", ("author", Author)),
            I18n.T("about.project", ("url", ProjectUrl)),
            string.Empty,
        };

        foreach (var feature in Features)
        {
            lines.Add($"• {feature}");
        }

        if (Diagnostics.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(I18n.T("about.diagnostics"));
            foreach (var (label, value) in Diagnostics)
            {
                lines.Add($"  {label}: {value}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
