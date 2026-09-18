// BulkDelete.cs —— 批量管理（勾选多项删除）的**纯逻辑**部分
//
// 移植自原版 v1.11.6：`app_window._toggle_batch_mode` / `_batch_delete`
// + `icon_grid.set_batch_mode` / `batch_delete`（复选框在 `icon_widget._check`）。
//
// 界面上是"进入批量管理模式 → 逐个勾选 → 批量删除"，这里只负责两件可测试的事：
//   ① 文案（原版 i18n 的 key；i18n 已到位 ⇒ **一律走 `I18n.T(key)`**，不再写死中文）；
//   ② **把界面勾选的 id 收敛成"这一页真实存在、去重、按页面顺序"的一份清单** ——
//      二次确认框里的数量必须是"真要删掉的数量"，不能拿界面的原始勾选数（可能含已失效的 id）。
//
// ⚠️ 真正的删除动作在 `DataStore.RemoveIcons`（一次落盘 + 连带删缓存），**不在**这里。
// ⚠️ 下面这些文案属性**每次读取都取当前语言**（不是构造时快照）—— 切换语言后界面重读即可。

using ToolboxPanel.Core.Models;

namespace ToolboxPanel.Core.Services;

/// <summary>批量删除的文案与勾选收敛（文案 key 与原版 i18n 一致）。</summary>
public static class BulkDelete
{
    /// <summary>菜单项：进入批量管理（原版 <c>app.menu.batch</c>）。</summary>
    public static string MenuLabel => I18n.T("app.menu.batch");

    /// <summary>菜单/按钮：批量删除勾选图标（原版 <c>app.menu.batch_delete</c>）。</summary>
    public static string DeleteLabel => I18n.T("app.menu.batch_delete");

    /// <summary>一个都没勾（原版 <c>batch.none_checked</c>）。</summary>
    public static string NoneCheckedText => I18n.T("batch.none_checked");

    /// <summary>二次确认框标题（原版 <c>bulk_delete.title</c>）。</summary>
    public static string ConfirmTitle => I18n.T("bulk_delete.title");

    /// <summary>进入批量管理模式时的状态栏提示（原版 <c>status.batch_mode_on</c>）。</summary>
    public static string StatusOnText => I18n.T("status.batch_mode_on");

    /// <summary>退出批量管理的按钮文案（WinUI 线新增，key <c>bulk.exit</c>）。</summary>
    public static string ExitLabel => I18n.T("bulk.exit");

    /// <summary>全选按钮（WinUI 线新增，key <c>bulk.select_all</c>）。</summary>
    public static string SelectAllLabel => I18n.T("bulk.select_all");

    /// <summary>二次确认正文（原版 <c>bulk_delete.confirm</c>）。</summary>
    public static string ConfirmText(int count) => I18n.T("bulk_delete.confirm", ("count", count));

    /// <summary>删除完成（原版 <c>bulk_delete.done</c>）。</summary>
    public static string DoneText(int count) => I18n.T("bulk_delete.done", ("count", count));

    /// <summary>已选数量（WinUI 线新增，key <c>bulk.selected</c>）。</summary>
    public static string SelectedText(int count) => I18n.T("bulk.selected", ("count", count));

    /// <summary>
    /// 把界面勾选的 id 收敛成**这一页真实存在、去重、按页面顺序**的一份清单。
    ///
    /// <para>为什么要收敛：界面上可能残留已经不在这一页的 id（跨页拖走/被别的操作删掉），
    /// 直接拿原勾选列表去数，确认框里的数量就会比实际删除数多。</para>
    ///
    /// <para>页不存在 / 不是网格页 / 勾选为空 ⇒ 返回空清单（调用方按"未选中任何图标"处理）。</para>
    /// </summary>
    public static IReadOnlyList<string> ResolveSelection(TabModel? tab, IEnumerable<string>? checkedIds)
    {
        if (tab is null || tab.IsListTab || checkedIds is null)
        {
            return Array.Empty<string>();
        }

        var wanted = new HashSet<string>(checkedIds, StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(wanted.Count);

        // 按**页面顺序**输出：删除顺序确定，测试也稳定
        foreach (var icon in tab.Icons)
        {
            if (wanted.Contains(icon.Id))
            {
                result.Add(icon.Id);
            }
        }

        return result;
    }
}
