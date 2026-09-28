// TextItemContextMenu.cs —— 文本页**行右键菜单**的操作规格（纯数据 + 纯函数，可单测）
//
// 文本页是 WinUI 线新增的第三种页（见 `TabModel.TypeText`），原版没有对应菜单，
// 所以这一份规格是照**列表页行菜单**（`ListItemContextMenu`）的形状定的，
// 只多一项「复制」—— 点击行本身也能复制，但菜单里给一条**明确**的入口：
//   · 用户在"想复制"的时候会先想到右键（"这一行能干什么"）；
//   · 触控板/触摸下点击与拖动的区分不总是可靠，菜单项是最稳的入口。
//
//   编辑属性…        （icon.menu.edit）
//   复制             （text.menu.copy，新增 key）
//   ─────────
//   删除             （icon.menu.remove）
//
// ⚠️ 空白处右键 = **直接弹「新建文本项」对话框**（不经过菜单）—— 与列表页/网格页一致。

namespace ToolboxPanel.Core.Services;

/// <summary>文本行右键菜单里的一项动作。</summary>
public enum TextItemMenuAction
{
    EditProperties,
    Copy,
    Remove,
}

/// <summary>菜单项（<see cref="SeparatorBefore"/> = 这一项之前画一条分隔线）。</summary>
public sealed record TextItemMenuItem(TextItemMenuAction Action, string Label, bool SeparatorBefore = false);

/// <summary>文本行菜单规格（"管理类"文案复用原版 <c>icon.menu.*</c>，复制那一项是新文案）。</summary>
public static class TextItemContextMenu
{
    public static string LabelEdit => I18n.T("icon.menu.edit");
    public static string LabelCopy => I18n.T("text.menu.copy");
    public static string LabelRemove => I18n.T("icon.menu.remove");

    /// <summary>菜单项（顺序：编辑 → 复制 → ─── → 删除）。</summary>
    public static IReadOnlyList<TextItemMenuItem> Build() => new List<TextItemMenuItem>
    {
        new(TextItemMenuAction.EditProperties, LabelEdit),
        new(TextItemMenuAction.Copy, LabelCopy),
        new(TextItemMenuAction.Remove, LabelRemove, SeparatorBefore: true),
    };
}
