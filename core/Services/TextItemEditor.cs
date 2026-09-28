// TextItemEditor.cs —— 文本页条目的字段语义与校验（纯逻辑，可单测）
//
// 文本页 = "粘贴板式"表格页（左列备注 + 右列文本，点一行即复制）。**原版 Python 没有这种页**，
// 所以这一族规则不是"照抄原版"，而是照本项目**列表项**那套（`ListItemEditor`）的形状定的：
//
//   · 新建 / 编辑属性…：一个对话框收 (备注, 文本) 两个字段；
//   · 空白处右键 = 直接弹「新建文本项」对话框（与列表页同款交互）；
//   · 删除：二次确认（<c>text.confirm_delete</c>，备注为空时用「此项」兜底）。
//
// ⚠️ 与列表项的**唯一实质差异**（刻意，且被单测钉住）：
//   列表项允许"说明与路径都为空就不建"，因为路径那一列才是它的本体；
//   **文本页的本体就是 Text —— 文本为空的行是一条没有任何用处的行**（点了也复制不到东西），
//   所以这里一律**要求文本非空**（空文本 ⇒ 拒绝，文案 <c>text.error.text_required</c>）。
//   备注（左列）可以为空 —— 它只是给人看的标签，复制的内容不受它影响。

using ToolboxPanel.Core.Models;

namespace ToolboxPanel.Core.Services;

/// <summary>文本页条目的字段规则与文案。</summary>
public static class TextItemEditor
{
    // ── 对话框与按钮文案（一律走 I18n.T，key 见 src/toolbox/i18n.py 的"文本页"一段）──

    /// <summary>新建文本项（<c>text.new_item</c>）。</summary>
    public static string NewItemTitle => I18n.T("text.new_item");

    /// <summary>编辑文本项（<c>text.edit_item</c>）。</summary>
    public static string EditTitle => I18n.T("text.edit_item");

    /// <summary>备注（左列 / 字段名，<c>text.col.note</c>）。</summary>
    public static string NoteLabel => I18n.T("text.col.note");

    /// <summary>文本（右列 / 字段名，<c>text.col.text</c>）。</summary>
    public static string TextLabel => I18n.T("text.col.text");

    /// <summary>备注输入框的占位符（<c>text.note_ph</c>）。</summary>
    public static string NotePlaceholder => I18n.T("text.note_ph");

    /// <summary>文本输入框的占位符（<c>text.text_ph</c>）。</summary>
    public static string TextPlaceholder => I18n.T("text.text_ph");

    /// <summary>备注为空时，删除确认框里的兜底称呼（<c>text.this_item</c>）。</summary>
    public static string ThisItemText => I18n.T("text.this_item");

    /// <summary>删除确认框标题（<c>text.delete_title</c>）。</summary>
    public static string DeleteTitle => I18n.T("text.delete_title");

    /// <summary>文本为空（WinUI 线新增 key <c>text.error.text_required</c>）。</summary>
    public static string ErrorTextRequired => I18n.T("text.error.text_required");

    /// <summary>左列的兜底显示（备注为空时显示的那句话，<c>text.unnamed</c>）。</summary>
    public static string UnnamedNote => I18n.T("text.unnamed");

    /// <summary>已复制（<c>text.copied</c>）—— 状态栏提示。</summary>
    public static string CopiedText => I18n.T("text.copied");

    /// <summary>复制失败时的动作名（<c>text.action.copy</c>，与 <c>status.action_failed</c> 拼）。</summary>
    public static string CopyActionName => I18n.T("text.action.copy");

    /// <summary>确定要删除文本项「{note}」吗？（<c>text.confirm_delete</c>）。</summary>
    public static string DeleteConfirmText(string? note)
        => I18n.T("text.confirm_delete", ("note", DisplayNote(note)));

    /// <summary>已添加: {name}（复用原版 key <c>status.added</c>，与列表项同一口径）。</summary>
    public static string ItemAddedText(string name) => I18n.T("status.added", ("name", name));

    /// <summary>已删除: {name}（复用原版 key <c>status.removed</c>）。</summary>
    public static string RemovedText(string name) => I18n.T("status.removed", ("name", name));

    // ── 字段规则 ──

    /// <summary>
    /// 这一条在界面上（删除确认 / 状态栏 / 日志）该怎么称呼：
    /// 备注非空就用它，否则用**文本的首行预览**，再否则回落到「此项」。
    /// </summary>
    public static string DisplayNote(string? note, string? text = null)
    {
        if (!string.IsNullOrWhiteSpace(note))
        {
            return note.Trim();
        }

        return string.IsNullOrWhiteSpace(text) ? ThisItemText : Preview(text, 40);
    }

    /// <summary>把多行文本压成"一行预览"（换行/制表符变空格、连续空白合一、超长加省略号）。</summary>
    public static string Preview(string? text, int maxLength = 200)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "…";
    }

    /// <summary>新建：**文本为空 ⇒ 返回 null**（调用方什么都不做，界面上给 <see cref="ErrorTextRequired"/>）。</summary>
    public static TextItemModel? Create(string? note, string? text)
    {
        var body = text?.Trim() ?? string.Empty;
        if (body.Length == 0)
        {
            return null;
        }

        return new TextItemModel { Note = note?.Trim() ?? string.Empty, Text = body };
    }

    /// <summary>
    /// 编辑属性…：把对话框里的 (备注, 文本) 写进模型（**两个字段都写**，包括把备注清空）。
    /// 文本为空 ⇒ 返回 false 且一个字段都不改。
    /// </summary>
    public static bool TryApplyEdit(TextItemModel? item, string? note, string? text)
    {
        if (item is null)
        {
            return false;
        }

        var body = text?.Trim() ?? string.Empty;
        if (body.Length == 0)
        {
            return false;
        }

        item.Note = note?.Trim() ?? string.Empty;
        item.Text = body;
        return true;
    }

    /// <summary>这一条现在能不能被复制（文本非空）。</summary>
    public static bool CanCopy(TextItemModel? item) => !string.IsNullOrEmpty(item?.Text);

    /// <summary>把换行 / 制表符换成空格并合并连续空白（供预览显示用；复制出去的永远是原文）。</summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;   // 行首的空白直接丢掉
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
