// IconSizeMetricsTests.cs —— 图标大小三档的尺寸表
//
// 重点守两条：
//   ① **medium 必须与"已定版密度"逐值一致** —— 用户 2026-09-14 确认的界面是
//      图块 68×72 / 图标 30 / 字形 24 / 名称 10.5 号(行高 12.5)；改它 = 改用户已确认的观感。
//   ② 名字必须与 `AppSettings.IconSizes`（写进 config.json 的线名）完全一致 —— 否则设置面板选了存不上。

using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class IconSizeMetricsTests
{
    [Fact]
    public void 三档齐全_名字与设置的线名完全一致()
    {
        Assert.Equal(3, IconSizeMetrics.All.Count);
        Assert.Equal(AppSettings.IconSizes, IconSizeMetrics.All.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void 默认档_必须与已定版密度逐值一致()
    {
        var medium = IconSizeMetrics.For("medium");

        Assert.Equal(68, medium.TileWidth);      // GridPage.xaml 的 CompactTileStyle.Width
        Assert.Equal(72, medium.TileHeight);     // 同上 Height
        Assert.Equal(30, medium.IconPixels);     // 模板里 Image 的 Width/Height
        Assert.Equal(24, medium.GlyphPixels);    // 模板里 FontIcon 的 FontSize
        Assert.Equal(10.5, medium.FontSize);     // 模板里 TextBlock 的 FontSize
        Assert.Equal(12.5, medium.LineHeight);   // 同上 LineHeight
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("giant")]
    [InlineData("乱写的值")]
    public void 未知或空一律回落medium(string? name)
        => Assert.Equal(IconSizeMetrics.Medium, IconSizeMetrics.For(name));

    [Theory]
    [InlineData("small", "small")]
    [InlineData("SMALL", "small")]
    [InlineData(" Large ", "large")]
    [InlineData("Medium", "medium")]
    public void 解析大小写与空白不敏感(string wire, string expectedName)
        => Assert.Equal(expectedName, IconSizeMetrics.For(wire).Name);

    [Fact]
    public void 三档单调递增_图块与图标与字号都随档位变大()
    {
        var small = IconSizeMetrics.For("small");
        var medium = IconSizeMetrics.For("medium");
        var large = IconSizeMetrics.For("large");

        Assert.True(small.TileWidth < medium.TileWidth && medium.TileWidth < large.TileWidth);
        Assert.True(small.TileHeight < medium.TileHeight && medium.TileHeight < large.TileHeight);
        Assert.True(small.IconPixels < medium.IconPixels && medium.IconPixels < large.IconPixels);
        Assert.True(small.FontSize < medium.FontSize && medium.FontSize < large.FontSize);
    }

    [Fact]
    public void 图标不得大于图块_名称行高要放得下两行()
    {
        foreach (var metrics in IconSizeMetrics.All)
        {
            Assert.True(metrics.IconPixels <= metrics.TileWidth, $"{metrics.Name}: 图标宽度超过图块");
            Assert.True(metrics.IconPixels + metrics.LineHeight * 2 <= metrics.TileHeight,
                $"{metrics.Name}: 图块高度放不下「图标 + 两行名称」");
        }
    }

    [Fact]
    public void 取档后写回线名能往返()
    {
        foreach (var metrics in IconSizeMetrics.All)
        {
            Assert.Equal(metrics.Name, IconSizeMetrics.For(metrics.WireName).Name);
        }
    }
}
