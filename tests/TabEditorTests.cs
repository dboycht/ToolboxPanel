// TabEditorTests.cs —— 批次 1：标签页「新建 / 重命名 / 删除」的字段语义（纯逻辑）
//
// 基准 = 原版 v1.11.6：app_window.py::_on_new_tab（预填默认名、取消则不做）
//        / tab_widget.py::_rename_tab（名字必填、同名不动） / _delete_tab（至少留一页）。
//
// ⚠️ 这一族是 2026-09-19 补的：在此之前 WinUI 线**完全没有**标签页管理的入口
//    （AddTab / new_tab / 标签右键菜单引用数 = 0）。

using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class TabEditorTests
{
    // ────────────────────────────── 新建 ──────────────────────────────

    [Fact]
    public void 新建_网格页_留空时回落默认页名()
    {
        // 原版：对话框预填 tab.default_name；留空（异常路径）也应当回到同一个默认名
        var result = TabEditor.Create("   ", isList: false);

        Assert.True(result.Success);
        Assert.Equal(I18n.T("tab.default_name"), result.Name);
        Assert.Equal(TabModel.TypeGrid, result.TabType);
    }

    [Fact]
    public void 新建_列表页_留空时回落列表默认页名()
    {
        var result = TabEditor.Create(null, isList: true);

        Assert.True(result.Success);
        Assert.Equal(I18n.T("list.default_name"), result.Name);
        Assert.Equal(TabModel.TypeList, result.TabType);
    }

    [Fact]
    public void 新建_名字两端空白被去掉()
    {
        var result = TabEditor.Create("  常用工具  ", isList: false);

        Assert.True(result.Success);
        Assert.Equal("常用工具", result.Name);
    }

    [Fact]
    public void 新建_默认页名跟随语言()
    {
        // 原版的默认页名是按语言取的（`tr("tab.default_name")`），不是写死的
        var original = I18n.Current;
        try
        {
            I18n.SetLanguage("zh");
            var zh = TabEditor.ResolveCreateName(null, isList: false);
            I18n.SetLanguage("en");
            var en = TabEditor.ResolveCreateName(null, isList: false);

            Assert.NotEqual(zh, en);
            Assert.Equal(I18n.T("tab.default_name"), en);
        }
        finally
        {
            I18n.SetLanguage(original);
        }
    }

    // ────────────────────────────── 重命名 ──────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 重命名_空名被拒(string? input)
    {
        var result = TabEditor.Rename("旧名", input);

        Assert.False(result.Success);
        Assert.Equal(I18n.T("validate.name_required"), result.ErrorMessage);
    }

    [Fact]
    public void 重命名_新名与旧名相同_标记为未变化()
    {
        // 原版 `if new_name != current_name` 才落库与提示；同名时什么都不做
        var result = TabEditor.Rename("常用", "  常用  ");   // 去空白后相同

        Assert.True(result.Success);
        Assert.True(result.Unchanged);
    }

    [Fact]
    public void 重命名_正常改名_去空白且不算未变化()
    {
        var result = TabEditor.Rename("旧名", "  新名 ");

        Assert.True(result.Success);
        Assert.False(result.Unchanged);
        Assert.Equal("新名", result.Name);
    }

    // ────────────────────────────── 删除 ──────────────────────────────

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(9, true)]
    public void 删除_至少保留一个标签页(int tabCount, bool expected)
        => Assert.Equal(expected, TabEditor.CanRemove(tabCount));

    [Fact]
    public void 删除_只剩一页时给出拒绝原因()
    {
        var reason = TabEditor.CannotRemoveReason(1);

        Assert.Equal(I18n.T("tab.delete.blocked"), reason);
        Assert.Null(TabEditor.CannotRemoveReason(2));
    }

    // ────────────────────────────── 菜单规格 ──────────────────────────────

    [Fact]
    public void 标签上的菜单_五项且删除前有分隔线()
    {
        var items = TabContextMenu.BuildForTab();

        Assert.Equal(
            new[] { TabMenuAction.NewTab, TabMenuAction.NewListTab, TabMenuAction.Rename, TabMenuAction.Remove },
            items.Select(i => i.Action));
        Assert.False(items[0].SeparatorBefore);
        Assert.False(items[1].SeparatorBefore);
        Assert.False(items[2].SeparatorBefore);
        Assert.True(items[3].SeparatorBefore);   // 原版：管理类之前画一条线
    }

    [Fact]
    public void 空白处的菜单_只出两个新建()
    {
        // "管理"类动作没有作用对象 ⇒ 空白处只给"新建"（本项目"右键即菜单"的一致做法）
        var items = TabContextMenu.BuildForEmptyArea();

        Assert.Equal(new[] { TabMenuAction.NewTab, TabMenuAction.NewListTab }, items.Select(i => i.Action));
        Assert.DoesNotContain(items, i => i.Action is TabMenuAction.Rename or TabMenuAction.Remove);
    }

    [Fact]
    public void 菜单文案都来自文案表_不是写死的常量()
    {
        // 与 i18n 同一口径：语言切了这里必须跟着变（原版这几个 key 都有）
        var original = I18n.Current;
        try
        {
            I18n.SetLanguage("zh");
            var zhNew = TabContextMenu.LabelNewTab;
            var zhRemove = TabContextMenu.LabelRemove;
            I18n.SetLanguage("en");

            Assert.NotEqual(zhNew, TabContextMenu.LabelNewTab);
            Assert.NotEqual(zhRemove, TabContextMenu.LabelRemove);
        }
        finally
        {
            I18n.SetLanguage(original);
        }
    }
}
