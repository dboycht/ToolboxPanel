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
//   刷新图标                 （icon.menu.refresh）—— **WinUI 线新增**：只重取图标缓存，不改字段
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

    /// <summary>
    /// 刷新图标（2026-10-03 新增，原版没有这一项）：**只重取图标缓存**，不改任何属性字段。
    /// 用在"图标源文件换了内容，但路径没变"的场合（常规载入只在缓存文件不存在时才提取）。
    /// </summary>
    Refresh,

    Remove,
}

/// <summary>菜单项（<see cref="SeparatorBefore"/> 表示"这一项之前画一条分隔线"，与原版一致）。</summary>
public sealed record IconMenuItem(IconMenuAction Action, string Label, bool SeparatorBefore = false);

/// <summary>菜单规格 + 各处提示文案（原版 i18n 的 icon.menu.* / icon.remove.* / status.*）。</summary>
public static class IconContextMenu
{
    public static string LabelOpen => I18n.T("icon.menu.open");
    public static string LabelOpenWith => I18n.T("icon.menu.open_with");
    public static string LabelOpenLocation => I18n.T("icon.menu.open_location");
    public static string LabelEdit => I18n.T("icon.menu.edit");
    public static string LabelRename => I18n.T("icon.menu.rename");

    /// <summary>刷新图标（<c>icon.menu.refresh</c>；WinUI 线新增，原版没有这一项）。</summary>
    public static string LabelRefresh => I18n.T("icon.menu.refresh");

    public static string LabelRemove => I18n.T("icon.menu.remove");

    /// <summary>删除确认对话框的标题（原版 <c>icon.remove.title</c>）。</summary>
    public static string RemoveTitle => I18n.T("icon.remove.title");

    /// <summary>图标没有名字时，确认文案里用它兜底（原版 <c>icon.remove.unknown</c>）。</summary>
    public static string UnknownIconName => I18n.T("icon.remove.unknown");

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

        // ⚠️ 「刷新图标」是 **WinUI 线新增**的（原版没有）：位置刻意放在「重命名」与「删除」之间 ——
        //    它俩都是"不动路径、只动外观/名字"的管理动作；放最前面会与"打开类"混在一起，
        //    放最后（删除之后）又太容易误点。所有类型都给它（与类型无关：能不能真的刷新
        //    由 MainViewModel.RefreshIcon 判，没来源时给一句明确提示）。
        items.Add(new IconMenuItem(IconMenuAction.Refresh, LabelRefresh));

        items.Add(new IconMenuItem(IconMenuAction.Remove, LabelRemove));

        return items;
    }

    /// <summary>删除确认文案（原版 <c>icon.remove.confirm</c>，名字为空时用「此图标」）。</summary>
    public static string ConfirmRemoveText(string? displayName)
        => I18n.T("icon.remove.confirm", ("name", DisplayNameOr(displayName)));

    /// <summary>删除之后的状态栏文案（原版 <c>status.removed</c>）。</summary>
    public static string RemovedStatus(string? displayName)
        => I18n.T("status.removed", ("name", DisplayNameOr(displayName)));

    /// <summary>重命名之后的状态栏文案（原版 <c>status.renamed</c>）。</summary>
    public static string RenamedStatus(string? displayName)
        => I18n.T("status.renamed", ("name", (displayName ?? string.Empty).Trim()));

    /// <summary>编辑属性保存之后的状态栏文案（原版 <c>status.edited</c>）。</summary>
    public static string UpdatedStatus(string? displayName)
        => I18n.T("status.edited", ("name", (displayName ?? string.Empty).Trim()));

    /// <summary>刷新图标之后的状态栏文案（WinUI 线新增 key <c>status.icon_refreshed</c>）。</summary>
    public static string RefreshedStatus(string? displayName)
        => I18n.T("status.icon_refreshed", ("name", (displayName ?? string.Empty).Trim()));

    /// <summary>刷新失败（没有可重新提取的来源；WinUI 线新增 key <c>icon.error.no_source</c>）。</summary>
    public static string NoSourceMessage => I18n.T("icon.error.no_source");

    /// <summary>路径不存在（原版 <c>status.path_missing</c>）。</summary>
    public static string PathMissingMessage(string? path) => I18n.T("status.path_missing", ("path", path));

    /// <summary>调「打开方式」失败（原版 <c>status.open_with_failed</c>）。</summary>
    public static string OpenWithFailedMessage(string? error)
        => I18n.T("status.open_with_failed", ("err", error));

    /// <summary>没写名字时用「此图标 / this icon」兜底（原版 <c>icon.remove.unknown</c>）。</summary>
    private static string DisplayNameOr(string? displayName)
        => string.IsNullOrWhiteSpace(displayName) ? UnknownIconName : displayName.Trim();
}
