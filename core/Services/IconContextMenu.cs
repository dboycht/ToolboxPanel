// IconContextMenu.cs —— 图块右键菜单的"操作逻辑"规格（纯数据 + 纯函数，可单测）
//
// 基准 = 原版 v1.11.6 的 src/toolbox/icon_grid.py::_show_icon_context_menu：
//
//   打开                     （icon.menu.open）
//   用其他应用打开…           （icon.menu.open_with）—— **仅 FILE / FOLDER / SHORTCUT**
//   打开文件位置              （icon.menu.open_location）
//   ─────────
//   编辑属性…                （icon.menu.edit）
//   重命名                   （icon.menu.rename）
//   删除                     （icon.menu.remove）
//
// 为什么把菜单也放 Core：菜单的**顺序、按类型的门控、文案**全是纯逻辑，
// 放在这里就能被单测钉住（"URL 不该有『用其他应用打开…』"这种规则，
// 过去只能靠人肉点一遍才发现漏了/多了）。UI 侧只负责按这份规格铺控件。
//
// ⚠️ 文案是从原版 i18n.py 逐条抄来的中文（本项目 WinUI 线的 i18n 还没搬）。
//    将来搬 i18n 时，把这里的常量换成 key 即可，UI 不用改结构。

using ToolboxPanel.Core.Models;

namespace ToolboxPanel.Core.Services;

/// <summary>图块右键菜单里的一项动作。</summary>
public enum IconMenuAction
{
    Open,
    OpenWith,
    OpenLocation,
    EditProperties,
    Rename,
    Remove,
}

/// <summary>菜单项（<see cref="SeparatorBefore"/> 表示"这一项之前画一条分隔线"，与原版一致）。</summary>
public sealed record IconMenuItem(IconMenuAction Action, string Label, bool SeparatorBefore = false);

/// <summary>菜单规格 + 各处提示文案（原版 i18n 的 icon.menu.* / icon.remove.* / status.*）。</summary>
public static class IconContextMenu
{
    public const string LabelOpen = "打开";
    public const string LabelOpenWith = "用其他应用打开…";
    public const string LabelOpenLocation = "打开文件位置";
    public const string LabelEdit = "编辑属性…";
    public const string LabelRename = "重命名";
    public const string LabelRemove = "删除";

    /// <summary>删除确认对话框的标题（原版 <c>icon.remove.title</c>）。</summary>
    public const string RemoveTitle = "删除图标";

    /// <summary>图标没有名字时，确认文案里用它兜底（原版 <c>icon.remove.unknown</c>）。</summary>
    public const string UnknownIconName = "此图标";

    /// <summary>
    /// 按图标类型生成菜单项（**顺序与原版一致**）。
    /// 「用其他应用打开…」只对 FILE / FOLDER / SHORTCUT 有意义 ——
    /// 网址与命令的 target_path 不是真实文件，调「打开方式」没有意义（原版也是这么门控的）。
    /// </summary>
    public static IReadOnlyList<IconMenuItem> Build(IconType type)
    {
        var items = new List<IconMenuItem>
        {
            new(IconMenuAction.Open, LabelOpen),
        };

        if (type is IconType.File or IconType.Folder or IconType.Shortcut)
        {
            items.Add(new IconMenuItem(IconMenuAction.OpenWith, LabelOpenWith));
        }

        items.Add(new IconMenuItem(IconMenuAction.OpenLocation, LabelOpenLocation));

        // 原版在这里加分隔线：上面是"打开类"，下面是"管理类"
        items.Add(new IconMenuItem(IconMenuAction.EditProperties, LabelEdit, SeparatorBefore: true));
        items.Add(new IconMenuItem(IconMenuAction.Rename, LabelRename));
        items.Add(new IconMenuItem(IconMenuAction.Remove, LabelRemove));

        return items;
    }

    /// <summary>删除确认文案（原版 <c>icon.remove.confirm</c>，名字为空时用「此图标」）。</summary>
    public static string ConfirmRemoveText(string? displayName)
        => $"确定要从当前标签页中删除「{(string.IsNullOrWhiteSpace(displayName) ? UnknownIconName : displayName.Trim())}」吗？";

    /// <summary>删除之后的状态栏文案（原版 <c>status.removed</c>）。</summary>
    public static string RemovedStatus(string? displayName)
        => $"已删除: {(string.IsNullOrWhiteSpace(displayName) ? UnknownIconName : displayName.Trim())}";

    /// <summary>重命名之后的状态栏文案（原版 <c>status.renamed</c>）。</summary>
    public static string RenamedStatus(string? displayName)
        => $"已重命名为「{(displayName ?? string.Empty).Trim()}」";

    /// <summary>编辑属性保存之后的状态栏文案（原版 <c>status.edited</c>）。</summary>
    public static string UpdatedStatus(string? displayName)
        => $"已更新图标: {(displayName ?? string.Empty).Trim()}";

    /// <summary>路径不存在（原版 <c>status.path_missing</c>）。</summary>
    public static string PathMissingMessage(string? path) => $"路径不存在: {path}";

    /// <summary>调「打开方式」失败（原版 <c>status.open_with_failed</c>）。</summary>
    public static string OpenWithFailedMessage(string? error) => $"打开方式失败: {error}";
}
