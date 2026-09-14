// AboutDialog.xaml.cs —— 「关于」对话框
//
// 内容来自 Core 的 <see cref="AboutInfo"/>（纯逻辑、有单测），这里只把它摆到控件上。
// 与原版 v1.11.6 的差别只有两点（都是 WinUI 侧的表现形式，内容一一对应）：
//   · 原版是 QMessageBox 整段纯文本；这里拆成标题/作者/可点链接/提示列表，并多一个诊断信息区；
//   · 原版没有"打开项目主页"的直接入口（纯文本里给地址），这里点链接就能开浏览器。

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Views;

public sealed partial class AboutDialog : ContentDialog
{
    private readonly AboutInfo _info;

    private AboutDialog(AboutInfo info)
    {
        _info = info;

        InitializeComponent();

        Title = $"关于 {info.Title}";
        VersionLine.Text = $"{info.Title} v{info.Version}";
        SubtitleLine.Text = info.Subtitle;
        AuthorLine.Text = $"作者: {info.Author}";

        ProjectLink.Content = info.ProjectUrl;

        // 操作提示：原版的纯文本列表在这里用 XAML 模板铺出来（"• " 前缀也在这里加）
        FeatureList.ItemsSource = info.Features
            .Select(feature => "• " + feature)
            .ToList();

        DiagnosticsText.Text = string.Join(
            Environment.NewLine,
            info.Diagnostics.Select(d => $"{d.Label}: {d.Value}"));
    }

    /// <summary>由 MainWindow 调用（它会先把 AppInfo 采集的环境事实交给 Core）。</summary>
    public static AboutDialog Create(AboutInfo info) => new(info);

    /// <summary>点项目地址 —— 交给 Core 的 Launcher（失败只写日志，绝不因为打不开浏览器把窗口搞挂）。</summary>
    private void OnProjectLinkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = new Launcher().OpenUrl(_info.ProjectUrl);
            if (!result.Success)
            {
                App.WriteCrash("AboutDialog.OnProjectLinkClick", new InvalidOperationException(result.Error));
            }
        }
        catch (Exception ex)
        {
            App.WriteCrash("AboutDialog.OnProjectLinkClick", ex);
        }
    }
}
