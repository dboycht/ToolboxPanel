// ListItemContextMenu.cs —— 列表页**行右键菜单**的操作规格（纯数据 + 纯函数，可单测）
//
// 基准 = 原版 v1.11.6 的 src/toolbox/list_tab_page.py::_on_context_menu：
//
//   编辑属性…        （icon.menu.edit）
//   重命名           （icon.menu.rename）
//   ─────────
//   删除             （icon.menu.remove）
//
// ⚠️ 原版**没有「打开」项**：点行本身/双击就是打开（`_on_item_clicked`），
//    菜单只放"管理"动作。这里保持一致（别"顺手"加一项）。
// ⚠️ 空白处右键在原版是**直接弹「新建列表项」对话框**（不经过菜单）—— 由 UI 侧照做。
//
// 与图块菜单（`IconContextMenu`）同一套写法：顺序/分隔线/文案都进 Core，被单测钉住。

namespace ToolboxPanel.Core.Services;

/// <summary>列表行右键菜单里的一项动作。</summary>
public enum ListItemMenuAction
{
    EditProperties,
    Rename,
    Remove,
}

/// <summary>菜单项（<see cref="SeparatorBefore"/> = 这一项之前画一条分隔线，与原版一致）。</summary>
public sealed record ListItemMenuItem(ListItemMenuAction Action, string Label, bool SeparatorBefore = false);

/// <summary>列表行菜单规格（文案 key 与原版 i18n 的 icon.menu.* 一致）。</summary>
public static class ListItemContextMenu
{
    public static string LabelEdit => I18n.T("icon.menu.edit");
    public static string LabelRename => I18n.T("icon.menu.rename");
    public static string LabelRemove => I18n.T("icon.menu.remove");

    /// <summary>菜单项（顺序与原版一致；删除前有分隔线）。</summary>
    public static IReadOnlyList<ListItemMenuItem> Build() => new List<ListItemMenuItem>
    {
        new(ListItemMenuAction.EditProperties, LabelEdit),
        new(ListItemMenuAction.Rename, LabelRename),
        new(ListItemMenuAction.Remove, LabelRemove, SeparatorBefore: true),
    };
}
