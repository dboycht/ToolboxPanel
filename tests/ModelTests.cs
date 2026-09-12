// ModelTests.cs —— 模型层默认值与类型映射（W1）

using System.Text.Json;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class ModelTests
{
    [Fact]
    public void TabModel_默认值与原版一致()
    {
        var tab = new TabModel();

        Assert.False(string.IsNullOrWhiteSpace(tab.Id));
        Assert.True(Guid.TryParse(tab.Id, out _));            // 原版是 uuid4 字符串
        Assert.Equal("新建标签页", tab.Name);
        Assert.Equal(0, tab.Order);
        Assert.Equal("grid", tab.TabType);
        Assert.Empty(tab.Icons);
        Assert.Empty(tab.ListItems);
        Assert.False(tab.IsListTab);
    }

    [Theory]
    [InlineData(IconType.File, "file")]
    [InlineData(IconType.Folder, "folder")]
    [InlineData(IconType.Shortcut, "shortcut")]
    [InlineData(IconType.Url, "url")]
    [InlineData(IconType.Command, "command")]
    public void IconType_线上字符串与原版一字不差(IconType type, string expectedWire)
    {
        Assert.Equal(expectedWire, IconTypes.ToWire(type));
        Assert.Equal(type, IconTypes.Parse(expectedWire));

        // 反向：这些字符串必须能被 JSON 原样写出/读回
        var model = new IconModel { Type = type };
        var json = JsonSerializer.Serialize(model, TabsJson.Options);
        Assert.Contains($"\"type\": \"{expectedWire}\"", json);
        Assert.Equal(type, JsonSerializer.Deserialize<IconModel>(json, TabsJson.Options)!.Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("FILE2")]
    public void IconType_无法识别的值回落到File(string? wire)
    {
        Assert.Equal(IconType.File, IconTypes.Parse(wire));
    }

    [Theory]
    [InlineData("FILE", IconType.File)]
    [InlineData(" Folder ", IconType.Folder)]
    [InlineData("URL", IconType.Url)]
    public void IconType_忽略大小写与空白(string wire, IconType expected)
    {
        // 比 Python 宽松（Python 严格区分大小写）；宽松不会造成数据损坏，只会少炸一次
        Assert.Equal(expected, IconTypes.Parse(wire));
    }

    [Fact]
    public void IconModel_默认值与原版一致()
    {
        var icon = new IconModel();

        Assert.True(Guid.TryParse(icon.Id, out _));
        Assert.Equal(IconType.File, icon.Type);
        Assert.Equal(string.Empty, icon.DisplayName);
        Assert.Equal(string.Empty, icon.SourcePath);
        Assert.Equal(string.Empty, icon.TargetPath);
        Assert.Equal(string.Empty, icon.Arguments);
        Assert.Equal(string.Empty, icon.WorkingDir);
        Assert.Equal(string.Empty, icon.Description);
        Assert.Equal(string.Empty, icon.IconCacheFile);
        Assert.Equal(0, icon.SortOrder);
    }

    [Fact]
    public void ListItemModel_默认值与原版一致()
    {
        var item = new ListItemModel();

        Assert.True(Guid.TryParse(item.Id, out _));
        Assert.Equal(string.Empty, item.Description);
        Assert.Equal(string.Empty, item.Path);
        Assert.Equal(0, item.SortOrder);
    }

    [Fact]
    public void IsListTab_只认list()
    {
        Assert.True(new TabModel { TabType = "list" }.IsListTab);
        Assert.False(new TabModel { TabType = "grid" }.IsListTab);
        Assert.False(new TabModel { TabType = "grid2" }.IsListTab);   // 未知类型按网格
    }
}
