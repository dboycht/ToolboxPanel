// TabEditor.cs —— 标签页「新建 / 重命名 / 删除」的字段语义（纯逻辑、可单测）
//
// 对应原 Python（v1.11.6）的三处，行为逐条对齐：
//   · src/toolbox/app_window.py::_on_new_tab / _on_new_list_tab
//       —— 先让用户输入名字（默认值 = tab.default_name / list.default_name），取消则什么都不做
//   · src/toolbox/tab_widget.py::_rename_tab
//       —— 名字必填；**新名与旧名相同就什么都不做**（原版 `if new_name != current_name`）
//   · src/toolbox/tab_widget.py::_delete_tab
//       —— **至少保留一个标签页**（`if self._stack.count() <= 1: 警告并返回`）
//
// 为什么把这三件事搬进 Core（与 `IconEditor` / `ListItemEditor` 同一个理由）：
//   它们全是**纯逻辑**（名字归一化、必填校验、"能不能删"的判据），天生可单测；
//   界面上「确定」按下的那一刻就要知道"能不能存、存完要不要刷新"。
//   判断留在 Core，UI 只负责读字段 + 执行结果。
//
// ⚠️ 本类**不碰磁盘**：落库由调用方走 `DataStore.AddTab / RenameTab / TryRemoveTab`。

namespace ToolboxPanel.Core.Services;

/// <summary>一次「新建 / 重命名」的结论：成功则带上该用的名字，失败带上给用户看的消息。</summary>
public sealed class TabEditResult
{
    private TabEditResult(bool success, string? errorMessage, string name, string tabType)
    {
        Success = success;
        ErrorMessage = errorMessage;
        Name = name;
        TabType = tabType;
    }

    public bool Success { get; }

    /// <summary>失败原因（走 i18n key，可直接显示给用户）。</summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// 成功时**实际该用的名字**（已 Trim）。新建时若用户留空 / 取消输入，
    /// 由调用方决定是"用默认名继续"还是"什么都不做" —— 见 <see cref="ResolveCreateName"/>。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 成功时该用哪个页类型（<see cref="Models.TabModel.TypeGrid"/> / <see cref="Models.TabModel.TypeList"/>）。
    /// <para>⚠️ **只在"新建"时有意义**：重命名不涉及页类型（原名那页是什么类型就还是什么类型），
    /// 所以 <see cref="Rename"/> 的结果里这个字段是占位值，调用方**不要读它**。</para>
    /// </summary>
    public string TabType { get; }

    /// <summary>重命名时"新名与旧名相同"（原版这种情况下什么都不做、也不提示）。</summary>
    public bool Unchanged { get; private init; }

    public static TabEditResult Fail(string errorMessage)
        => new(false, errorMessage, string.Empty, Models.TabModel.TypeGrid);

    public static TabEditResult Ok(string name, string tabType)
        => new(true, null, name, tabType);

    public static TabEditResult Same(string name, string tabType)
        => new(true, null, name, tabType) { Unchanged = true };
}

/// <summary>标签页字段语义。<b>只做校验与归一化，不碰磁盘</b>。</summary>
public static class TabEditor
{
    /// <summary>名称不能为空（与原版一致的文案 key）。</summary>
    public static string ErrorNameRequired => I18n.T("validate.name_required");

    /// <summary>
    /// 新建时"用户输入的名字"→ 实际要用的名字。
    ///
    /// <para>原版语义：对话框里**预填默认名**（`tab.default_name` / `list.default_name`），
    /// 用户取消则整个新建动作都不做。所以"留空"这件事正常路径下不会发生；
    /// 真留空时回落默认名（而不是报错）—— 与"默认名就是给这个场景准备的"一致。</para>
    /// </summary>
    public static string ResolveCreateName(string? input, bool isList)
        => Trim(input) is { Length: > 0 } name ? name : TabContextMenu.DefaultNameFor(isList);

    /// <summary>
    /// 新建的校验 + 归一化。**创建动作本身由调用方做**（`DataStore.AddTab(name, type)`）。
    /// </summary>
    public static TabEditResult Create(string? input, bool isList)
    {
        var name = ResolveCreateName(input, isList);
        return TabEditResult.Ok(name, isList ? Models.TabModel.TypeList : Models.TabModel.TypeGrid);
    }

    /// <summary>
    /// 重命名的校验：**名字必填**（原版内联编辑同样不接受空名，空名会被丢弃）。
    /// 新名与旧名相同 ⇒ <see cref="TabEditResult.Unchanged"/> 为 true（调用方据此跳过落库与提示）。
    /// </summary>
    public static TabEditResult Rename(string? currentName, string? newName)
    {
        var name = Trim(newName);
        if (name.Length == 0)
        {
            return TabEditResult.Fail(ErrorNameRequired);
        }

        return string.Equals(name, Trim(currentName), StringComparison.Ordinal)
            ? TabEditResult.Same(name, Models.TabModel.TypeGrid)
            : TabEditResult.Ok(name, Models.TabModel.TypeGrid);
    }

    /// <summary>
    /// 能不能删这一页：<b>至少保留一个标签页</b>（原版 <c>tab.delete.blocked</c> 的判据）。
    ///
    /// <para>⚠️ 判据是**当前页数 &gt; 1**，与"删的是哪一页"无关 —— 所以这里是纯函数、不需要页对象。</para>
    /// </summary>
    public static bool CanRemove(int tabCount) => tabCount > 1;

    /// <summary>不能删时给用户看的消息（null = 可以删）。</summary>
    public static string? CannotRemoveReason(int tabCount)
        => CanRemove(tabCount) ? null : TabContextMenu.CannotRemoveText;

    /// <summary>
    /// **删掉某一页之后该选中哪一页**（界面的换页决策；纯函数，可单测）。
    ///
    /// <para>⚠️ 判据是"**删的是不是当前页**" —— 这是 2026-09-19 自查抓出来的一个真滑落：
    /// 一开始写成了"删完总是选中原位置那一页"，于是**右键删掉一个"非当前页"时，
    /// 用户正在看的那一页会被莫名其妙切走**（右键菜单可以在任意一页上弹出）。</para>
    /// </summary>
    /// <param name="removedIndex">被删页的下标（**删除之前**的）。</param>
    /// <param name="remainingCount">删除**之后**剩下的页数。</param>
    /// <param name="wasCurrent">被删的是不是当前正在看的那一页。
    /// ⚠️ 必须在删除**之前**判断：从集合移除后框架会清空选中项。</param>
    /// <param name="selectionLost">删除后选中项是否已被清空（框架行为；true 时也要补一个有效页）。</param>
    /// <returns>该选中的下标；<c>null</c> = **不用换页**（当前的页没被删，保持原样）。</returns>
    public static int? ResolveSelectionAfterRemove(
        int removedIndex, int remainingCount, bool wasCurrent, bool selectionLost)
    {
        if (remainingCount <= 0)
        {
            return null;   // 一页都不剩（正常路径不会发生：CanRemove 已经拦住了）
        }

        if (!wasCurrent && !selectionLost)
        {
            return null;   // ★ 删的不是当前页 ⇒ 什么都不动，用户在看哪页就还在哪页
        }

        // 原位优先；删的是最后一页时退到"新的最后一页"
        return Math.Clamp(removedIndex, 0, remainingCount - 1);
    }

    private static string Trim(string? value) => (value ?? string.Empty).Trim();
}
