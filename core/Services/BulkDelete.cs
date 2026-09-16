// BulkDelete.cs —— 批量管理（勾选多项删除）的**纯逻辑**部分
//
// 移植自原版 v1.11.6：`app_window._toggle_batch_mode` / `_batch_delete`
// + `icon_grid.set_batch_mode` / `batch_delete`（复选框在 `icon_widget._check`）。
//
// 界面上是"进入批量管理模式 → 逐个勾选 → 批量删除"，这里只负责两件可测试的事：
//   ① 文案（原版 i18n 的 6 条，逐字搬过来；i18n 到位后换成 key）；
//   ② **把界面勾选的 id 收敛成"这一页真实存在、去重、按页面顺序"的一份清单** ——
//      二次确认框里的数量必须是"真要删掉的数量"，不能拿界面的原始勾选数（可能含已失效的 id）。
//
// ⚠️ 真正的删除动作在 `DataStore.RemoveIcons`（一次落盘 + 连带删缓存），**不在**这里。

using ToolboxPanel.Core.Models;

namespace ToolboxPanel.Core.Services;

/// <summary>批量删除的文案与勾选收敛（原版 i18n 文案逐字保真）。</summary>
public static class BulkDelete
{
    /// <summary>菜单项：进入批量管理（原版 <c>app.menu.batch</c>）。</summary>
    public const string MenuLabel = "批量管理";

    /// <summary>菜单/按钮：批量删除勾选图标（原版 <c>app.menu.batch_delete</c>）。</summary>
    public const string DeleteLabel = "批量删除勾选图标";

    /// <summary>一个都没勾（原版 <c>batch.none_checked</c>）。</summary>
    public const string NoneCheckedText = "未选中任何图标";

    /// <summary>二次确认框标题（原版 <c>bulk_delete.title</c>）。</summary>
    public const string ConfirmTitle = "批量删除图标";

    /// <summary>进入批量管理模式时的状态栏提示（原版 <c>status.batch_mode_on</c>）。</summary>
    public const string StatusOnText = "批量管理模式：勾选图标后点击「批量删除勾选图标」";

    /// <summary>退出批量管理的按钮文案（WinUI 线新增：原版靠菜单勾选取消，我们没有菜单栏）。</summary>
    public const string ExitLabel = "退出批量管理";

    /// <summary>全选按钮（WinUI 线新增，原版没有；行数多时很实用）。</summary>
    public const string SelectAllLabel = "全选";

    /// <summary>二次确认正文（原版 <c>bulk_delete.confirm</c>）。</summary>
    public static string ConfirmText(int count) => $"确定要删除选中的 {count} 个图标吗？";

    /// <summary>删除完成（原版 <c>bulk_delete.done</c>）。</summary>
    public static string DoneText(int count) => $"已删除 {count} 个图标";

    /// <summary>已选数量（WinUI 线新增，用于批量管理条上的计数）。</summary>
    public static string SelectedText(int count) => $"已选 {count} 个";

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
