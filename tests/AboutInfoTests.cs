// AboutInfoTests.cs —— 「关于」内容聚合与纯文本渲染（W5）
//
// 基准 = 原版 v1.11.6 QMessageBox.about（i18n.py 的 app.about.*）+ 本线新增的诊断信息。

using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class AboutInfoTests
{
    [Fact]
    public void Create_版本号去空白_空值回落未知()
    {
        Assert.Equal("2.0.2", AboutInfo.Create("  2.0.2  ", @"C:\data", false).Version);
        Assert.Equal("未知", AboutInfo.Create(null, @"C:\data", false).Version);
        Assert.Equal("未知", AboutInfo.Create("   ", @"C:\data", false).Version);
    }

    [Fact]
    public void Create_固定文案与原版一致()
    {
        var info = AboutInfo.Create("2.0.2", @"C:\data", false);

        Assert.Equal("ToolboxPanel", info.Title);
        Assert.Equal("手机桌面风格的启动器", info.Subtitle);
        Assert.Equal("dboycht", info.Author);
        Assert.Equal("https://github.com/dboycht/ToolboxPanel", info.ProjectUrl);
    }

    [Fact]
    public void DefaultFeatures_六条且顺序照原版()
    {
        var features = AboutInfo.DefaultFeatures;

        Assert.Equal(6, features.Count);
        Assert.Equal("从资源管理器拖入文件/文件夹/快捷方式即可创建图标", features[0]);
        Assert.Equal("双击图标打开，右键查看更多选项", features[1]);
        Assert.Equal("右键空白区域创建 URL / 命令图标", features[2]);
        Assert.Equal("图标和标签页均可拖动排序", features[3]);
        Assert.Equal("数据自动保存到 data/ 文件夹", features[4]);
        Assert.Equal("支持搜索过滤、图标大小切换、打开方式", features[5]);
    }

    [Fact]
    public void Create_诊断先放数据目录与运行模式_两种模式都要正确()
    {
        var normal = AboutInfo.Create("2.0.2", @"C:\Temp\tp", false);
        Assert.Equal(("数据目录", @"C:\Temp\tp"), normal.Diagnostics[0]);
        Assert.Equal(("运行模式", "正常模式"), normal.Diagnostics[1]);

        var demo = AboutInfo.Create("2.0.2", null, true);
        Assert.Equal(("数据目录", "（未定位）"), demo.Diagnostics[0]);   // 拿不到目录也不给 null
        Assert.Equal(("运行模式", "演示模式（不读写数据文件）"), demo.Diagnostics[1]);
    }

    [Fact]
    public void Create_额外诊断按传入顺序追加_空白Label被丢弃()
    {
        var info = AboutInfo.Create("2.0.2", @"C:\Temp\tp", false, new[]
        {
            (".NET 运行时", "9.0.0"),
            ("   ", "应被丢弃"),
            ("WindowsAppSDK", "2.4.0"),
        });

        Assert.Equal(4, info.Diagnostics.Count);
        Assert.Equal((".NET 运行时", "9.0.0"), info.Diagnostics[2]);
        Assert.Equal(("WindowsAppSDK", "2.4.0"), info.Diagnostics[3]);
        Assert.DoesNotContain(info.Diagnostics, d => d.Label.Trim().Length == 0);
    }

    [Fact]
    public void ToPlainText_含标题版本作者地址六条提示与诊断()
    {
        var text = AboutInfo.Create("2.0.2", @"C:\Temp\tp", false, new[] { (".NET 运行时", "9.0.0") })
            .ToPlainText();

        Assert.Contains("ToolboxPanel v2.0.2 — 手机桌面风格的启动器", text);
        Assert.Contains("作者: dboycht", text);
        Assert.Contains("项目地址: https://github.com/dboycht/ToolboxPanel", text);
        foreach (var feature in AboutInfo.DefaultFeatures)
        {
            Assert.Contains($"• {feature}", text);
        }

        Assert.Contains("诊断信息:", text);
        Assert.Contains(@"数据目录: C:\Temp\tp", text);
        Assert.Contains("运行模式: 正常模式", text);
        Assert.Contains(".NET 运行时: 9.0.0", text);
    }

    [Fact]
    public void ToPlainText_版本未知也照样成文_不出现null()
    {
        var text = AboutInfo.Create(null, null, true).ToPlainText();

        Assert.Contains("ToolboxPanel v未知", text);
        Assert.Contains("数据目录: （未定位）", text);
        Assert.DoesNotContain("null", text);
    }
}
