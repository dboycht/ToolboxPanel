// IconContextMenuTests.cs —— 图块右键菜单的"操作逻辑"规格（W5）
//
// 基准 = 原版 v1.11.6 的 icon_grid.py::_show_icon_context_menu（顺序、按类型门控、文案）。
// 这些规则过去只能靠人肉点一遍才发现漏了/多了，现在被单测钉住。

using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class IconContextMenuTests
{
    private static IconMenuAction[] Actions(IconType type)
        => IconContextMenu.Build(type).Select(item => item.Action).ToArray();

    [Fact]
    public void 文件类型_菜单项与顺序照原版()
    {
        Assert.Equal(
            new[]
            {
                IconMenuAction.Open,
                IconMenuAction.OpenWith,
                IconMenuAction.OpenLocation,
                IconMenuAction.EditProperties,
                IconMenuAction.Rename,
                IconMenuAction.Remove,
            },
            Actions(IconType.File));
    }

    [Theory]
    [InlineData(IconType.Folder)]
    [InlineData(IconType.Shortcut)]
    public void 文件夹与快捷方式_同样有_用其他应用打开(IconType type)
    {
        Assert.Contains(IconMenuAction.OpenWith, Actions(type));
    }

    [Theory]
    [InlineData(IconType.Url)]
    [InlineData(IconType.Command)]
    public void 网址与命令_没有_用其他应用打开(IconType type)
    {
        // 原版语义：这两类的 target_path 不是真实文件，调「打开方式」没有意义
        var actions = Actions(type);

        Assert.DoesNotContain(IconMenuAction.OpenWith, actions);
        Assert.Equal(
            new[]
            {
                IconMenuAction.Open,
                IconMenuAction.OpenLocation,
                IconMenuAction.EditProperties,
                IconMenuAction.Rename,
                IconMenuAction.Remove,
            },
            actions);
    }

    [Fact]
    public void 分隔线只画在编辑属性之前_其余项都不带()
    {
        var items = IconContextMenu.Build(IconType.File);

        Assert.Equal(
            new[] { IconMenuAction.EditProperties },
            items.Where(item => item.SeparatorBefore).Select(item => item.Action));
    }

    [Fact]
    public void 菜单文案照原版i18n()
    {
        var labels = IconContextMenu.Build(IconType.File).Select(item => item.Label).ToArray();

        Assert.Equal(
            new[] { "打开", "用其他应用打开…", "打开文件位置", "编辑属性…", "重命名", "删除" },
            labels);
        Assert.Equal("删除图标", IconContextMenu.RemoveTitle);
    }

    [Fact]
    public void 删除确认文案_带名字()
    {
        Assert.Equal("确定要从当前标签页中删除「记事本」吗？", IconContextMenu.ConfirmRemoveText("记事本"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 删除确认文案_名字为空回落此图标(string? name)
    {
        Assert.Equal($"确定要从当前标签页中删除「{IconContextMenu.UnknownIconName}」吗？",
            IconContextMenu.ConfirmRemoveText(name));
    }

    [Fact]
    public void 状态文案照原版()
    {
        Assert.Equal("已删除: 记事本", IconContextMenu.RemovedStatus("记事本"));
        Assert.Equal("已重命名为「新名」", IconContextMenu.RenamedStatus("新名"));
        Assert.Equal("已更新图标: 记事本", IconContextMenu.UpdatedStatus("记事本"));
        Assert.Equal(@"路径不存在: C:\x", IconContextMenu.PathMissingMessage(@"C:\x"));
        Assert.Equal("打开方式失败: boom", IconContextMenu.OpenWithFailedMessage("boom"));
    }

    [Fact]
    public void 删除状态_名字为空也回落此图标()
    {
        Assert.Equal($"已删除: {IconContextMenu.UnknownIconName}", IconContextMenu.RemovedStatus(null));
    }
}
