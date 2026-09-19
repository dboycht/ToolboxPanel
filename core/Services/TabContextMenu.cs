// TabContextMenu.cs —— 标签页**右键菜单**的操作规格（纯数据 + 纯函数，可单测）
//
// 基准 = 原版 v1.11.6 的 src/toolbox/tab_widget.py::_show_tab_context_menu：
//
//   新建标签页        （tab.menu.new）
//   新建列表标签页     （list.new_tab）
//   重命名            （tab.menu.rename）
//   ─────────
//   删除              （tab.menu.delete）
//
// ⚠️ 原版是**菜单栏**（文件菜单里"新建标签页"，标签栏右键出上面这五项）；
//    WinUI 线没有菜单栏（见 HANDOVER §2.2 的说明），所以：
//      · 标签上右键 → 上面五项（与原版右键菜单逐项一致）
//      · 标签栏空白处右键 → **只出两个「新建」**（"右键即菜单"是本项目一致的做法）
//
// 与图块菜单（`IconContextMenu`）同一套写法：顺序 / 分隔线 / 文案都进 Core，被单测钉住。

namespace ToolboxPanel.Core.Services;

/// <summary>标签页右键菜单里的一项动作。</summary>
public enum TabMenuAction
{
    /// <summary>新建网格标签页（原版 <c>app.menu.new_tab</c>）。</summary>
    NewTab,

    /// <summary>新建列表标签页（原版 <c>list.new_tab</c>）。</summary>
    NewListTab,

    Rename,

    Remove,
}

/// <summary>菜单项（<see cref="SeparatorBefore"/> = 这一项之前画一条分隔线，与原版一致）。</summary>
public sealed record TabMenuItem(TabMenuAction Action, string Label, bool SeparatorBefore = false);

/// <summary>标签页菜单规格（文案 key 与原版 i18n 一致）。</summary>
public static class TabContextMenu
{
    public static string LabelNewTab => I18n.T("tab.menu.new");
    public static string LabelNewListTab => I18n.T("list.new_tab");
    public static string LabelRename => I18n.T("tab.menu.rename");
    public static string LabelRemove => I18n.T("tab.menu.delete");

    /// <summary>默认页名：网格页 <c>tab.default_name</c>、列表页 <c>list.default_name</c>（原版同源）。</summary>
    public static string DefaultNameFor(bool isList) => I18n.T(isList ? "list.default_name" : "tab.default_name");

    /// <summary>**标签上**右键：五项，顺序与原版一致（删除前有分隔线）。</summary>
    public static IReadOnlyList<TabMenuItem> BuildForTab() => new List<TabMenuItem>
    {
        new(TabMenuAction.NewTab, LabelNewTab),
        new(TabMenuAction.NewListTab, LabelNewListTab),
        new(TabMenuAction.Rename, LabelRename),
        new(TabMenuAction.Remove, LabelRemove, SeparatorBefore: true),
    };

    /// <summary>**标签栏空白处**右键：只出两个「新建」（"管理"类动作没有作用对象）。</summary>
    public static IReadOnlyList<TabMenuItem> BuildForEmptyArea() => new List<TabMenuItem>
    {
        new(TabMenuAction.NewTab, LabelNewTab),
        new(TabMenuAction.NewListTab, LabelNewListTab),
    };

    // ── 各处提示文案（原版 i18n 的 tab.* / reset.*）──

    /// <summary>重命名对话框标题（原版新建标签页也复用它）。</summary>
    public static string RenameTitle => I18n.T("tab.rename.title");

    /// <summary>重命名 / 新建时的输入框标签。</summary>
    public static string RenamePrompt => I18n.T("tab.rename.prompt");

    /// <summary>重命名之后的状态栏文案。</summary>
    public static string RenamedStatus(string? name) => I18n.T("tab.renamed", ("name", name ?? string.Empty));

    /// <summary>删除确认对话框的标题。</summary>
    public static string RemoveTitle => I18n.T("tab.delete.title");

    /// <summary>删除确认文案（含页名）。</summary>
    public static string ConfirmRemoveText(string? name)
        => I18n.T("tab.delete.confirm", ("name", name ?? string.Empty));

    /// <summary>删除之后的状态栏文案。</summary>
    public static string RemovedStatus(string? name) => I18n.T("tab.deleted", ("name", name ?? string.Empty));

    /// <summary>只剩一页时的拒绝文案（原版 <c>tab.delete.blocked</c>）。</summary>
    public static string CannotRemoveText => I18n.T("tab.delete.blocked");

    /// <summary>拒绝删除时的对话框标题（原版 <c>tab.delete.blocked_title</c>）。</summary>
    public static string CannotRemoveTitle => I18n.T("tab.delete.blocked_title");

    /// <summary>新建成功后的状态栏文案（原版 <c>app.status.created_tab</c>）。</summary>
    public static string CreatedStatus(string? name) => I18n.T("app.status.created_tab", ("name", name ?? string.Empty));

    // ── 重置数据（原版 文件菜单 → 重置数据，WinUI 此前完全没有）──

    public static string ResetTitle => I18n.T("reset.title");

    public static string ResetConfirm => I18n.T("reset.confirm");

    public static string ResetDone => I18n.T("reset.done");
}
