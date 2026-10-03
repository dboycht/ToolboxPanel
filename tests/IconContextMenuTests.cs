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
                IconMenuAction.Refresh,      // WinUI 线新增（原版没有这一项）
                IconMenuAction.Remove,
            },
            Actions(IconType.File));
    }

    [Theory]
    [InlineData(IconType.File)]
    [InlineData(IconType.Folder)]
    [InlineData(IconType.Shortcut)]
    [InlineData(IconType.Url)]
    [InlineData(IconType.Command)]
    public void 五类图标都有_刷新图标_且排在重命名与删除之间(IconType type)
    {
        // 「刷新图标」与类型无关（有没有可提取的来源由落库那一层判，判不过给一句明确提示），
        // 但位置是刻意的：紧跟在「重命名」之后、「删除」之前 —— 三者都是管理类动作。
        var actions = Actions(type);

        var rename = Array.IndexOf(actions, IconMenuAction.Rename);
        var refresh = Array.IndexOf(actions, IconMenuAction.Refresh);
        var remove = Array.IndexOf(actions, IconMenuAction.Remove);

        Assert.True(refresh > rename, $"{type}：刷新图标应当在重命名之后");
        Assert.True(refresh < remove, $"{type}：刷新图标应当在删除之前");
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
                IconMenuAction.Refresh,
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
            new[] { "打开", "用其他应用打开…", "打开文件位置", "编辑属性…", "重命名", "刷新图标", "删除" },
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
        Assert.Equal("已刷新图标: 记事本", IconContextMenu.RefreshedStatus("记事本"));
        Assert.Equal("这个图标没有可重新提取的来源", IconContextMenu.NoSourceMessage);
        Assert.Equal(@"路径不存在: C:\x", IconContextMenu.PathMissingMessage(@"C:\x"));
        Assert.Equal("打开方式失败: boom", IconContextMenu.OpenWithFailedMessage("boom"));
    }

    [Fact]
    public void 删除状态_名字为空也回落此图标()
    {
        Assert.Equal($"已删除: {IconContextMenu.UnknownIconName}", IconContextMenu.RemovedStatus(null));
    }
}
