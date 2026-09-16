// ListItemEditor.cs —— 列表项的字段语义与校验（纯逻辑，可单测）
//
// 基准 = 原版 v1.11.6 的 src/toolbox/list_tab_page.py：
//   · 新建（空白右键）`_create_item`：弹「新建列表项」对话框，取 (说明, 路径)；
//     **两者都为空 ⇒ 什么都不做**（原版 `if not desc and not path: return`）；
//   · 编辑属性… `_edit_row`：同样的对话框，同样的"两者都为空 ⇒ 什么也不改"；
//   · 重命名 `_rename_row`：原版是**就地编辑列 0（说明）**，只改说明、不碰路径；
//   · 删除 `_delete_row`：二次确认 `list.confirm_delete`(desc=行文本 或 「此行」)。
//
// 与图块那套（`IconEditor`）对称：**校验/写入规则只有这一份**，UI 只负责把字段递进来。
//
// ⚠️ 与原版的两处**刻意差异**（都写进注释与单测，别当成 bug）：
//   ① 重命名**要求说明非空**：原版就地编辑允许清空，留下一个"没有说明也没有路径"的死行；
//      我们用一句「说明不能为空」拦住（与本项目图块重命名的规则一致）。
//   ② 对话框里"两者都为空"时**窗内报错、不关窗**（原文案缺失，属于 WinUI 线新增措辞）——
//      原版是静默什么都不做，用户容易以为"点了没反应"。

using ToolboxPanel.Core.Models;

namespace ToolboxPanel.Core.Services;

/// <summary>列表项的字段规则与文案（原版 i18n 的 list.* / icon.menu.* / status.* 照抄）。</summary>
public static class ListItemEditor
{
    // ── 对话框与按钮文案（原版 i18n） ──

    /// <summary>新建列表项（<c>list.new_item</c>）。</summary>
    public const string NewItemTitle = "新建列表项";

    /// <summary>编辑列表项（<c>list.edit_item</c>）。</summary>
    public const string EditTitle = "编辑列表项";

    /// <summary>说明（列名 / 字段名，<c>list.col.desc</c>）。</summary>
    public const string DescriptionLabel = "说明";

    /// <summary>路径（列名 / 字段名，<c>list.col.path</c>）。</summary>
    public const string PathLabel = "路径";

    /// <summary>说明输入框的占位符（<c>list.desc_ph</c>）。</summary>
    public const string DescriptionPlaceholder = "文本说明";

    /// <summary>选择文件（<c>list.select_file</c>）。</summary>
    public const string SelectFileLabel = "选择文件";

    /// <summary>选择文件夹（<c>list.select_folder</c>）。</summary>
    public const string SelectFolderLabel = "选择文件夹";

    /// <summary>删除确认框标题（<c>list.delete_title</c>）。</summary>
    public const string DeleteTitle = "删除列表项";

    /// <summary>说明为空时，确认/提示文案里的兜底称呼（<c>list.this_row</c>）。</summary>
    public const string ThisRowText = "此行";

    /// <summary>说明已更新（<c>list.desc_updated</c>）。</summary>
    public const string DescriptionUpdatedText = "说明已更新";

    /// <summary>路径已更新（<c>list.path_updated</c>）。</summary>
    public const string PathUpdatedText = "路径已更新";

    /// <summary>重命名时说明留空（⚠️ WinUI 线新增措辞，原版允许清空）。</summary>
    public const string ErrorNameRequired = "说明不能为空";

    /// <summary>新建/编辑时两个字段都为空（⚠️ WinUI 线新增措辞，原版是静默忽略）。</summary>
    public const string ErrorBothEmpty = "说明和路径至少要填一个";

    /// <summary>已添加列表项: {desc}（<c>list.item_added</c>）。</summary>
    public static string ItemAddedText(string name) => $"已添加列表项: {name}";

    /// <summary>确定要删除列表项「{desc}」吗？（<c>list.confirm_delete</c>）。</summary>
    public static string DeleteConfirmText(string description)
        => $"确定要删除列表项「{DisplayName(description)}」吗？";

    /// <summary>已删除: {name}（<c>status.removed</c>）。</summary>
    public static string RemovedText(string name) => $"已删除: {name}";

    // ── 字段规则 ──

    /// <summary>说明为空时显示「此行」（原版删除确认里的兜底）。</summary>
    public static string DisplayName(string? description)
        => string.IsNullOrWhiteSpace(description) ? ThisRowText : description.Trim();

    /// <summary>新建：两个字段都为空 ⇒ 返回 null（调用方什么都不做，与原版一致）。</summary>
    public static ListItemModel? Create(string? description, string? path)
    {
        var desc = description?.Trim() ?? string.Empty;
        var target = path?.Trim() ?? string.Empty;

        if (desc.Length == 0 && target.Length == 0)
        {
            return null;
        }

        return new ListItemModel { Description = desc, Path = target };
    }

    /// <summary>
    /// 编辑属性…：把对话框里的 (说明, 路径) 写进模型（**两个字段都写**，包括清空其中一个）。
    /// 两个都为空 ⇒ 返回 false 且不改（与原版 `if not desc and not path: return` 一致）。
    /// </summary>
    public static bool TryApplyEdit(ListItemModel? item, string? description, string? path)
    {
        if (item is null)
        {
            return false;
        }

        var desc = description?.Trim() ?? string.Empty;
        var target = path?.Trim() ?? string.Empty;

        if (desc.Length == 0 && target.Length == 0)
        {
            return false;
        }

        item.Description = desc;
        item.Path = target;
        return true;
    }

    /// <summary>
    /// 重命名：**只改说明**、不碰路径（原版是就地编辑列 0）。
    /// ⚠️ 说明必须非空（见文件头的刻意差异 ①）。
    /// </summary>
    public static bool TryRename(ListItemModel? item, string? description)
    {
        if (item is null)
        {
            return false;
        }

        var desc = description?.Trim() ?? string.Empty;
        if (desc.Length == 0)
        {
            return false;
        }

        item.Description = desc;
        return true;
    }
}
