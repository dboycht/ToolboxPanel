// TabReorder.cs —— 标签页拖动重排（纯逻辑、可单测）
//
// 基准 = 原版 v1.11.6：
//   · `wrap_tab_bar.py::_TabButton.dropEvent` → `WrapTabBar._on_drag_reorder(from, to)`
//         —— 松手落在第 i 个标签上 ⇒ `dragged.emit(from_idx, self._index)`，
//            即"**被拖的标签取目标标签的下标**"，`from == to` 时什么都不发；
//   · `tab_widget.py::_on_tab_moved(from, to)` → `data_store.reorder_tabs(from, to)`
//         —— ⚠️ 原版这条**不发任何状态消息**（只有一行 reorder_tabs）。
//
// 本实现与原版的两点关系（都写在这里，免得以后有人以为是漏了）：
//   ① **同样不发状态消息** —— 所以这一版**没有新增 i18n key**（文案表 key 集合必须与原版
//      `i18n.py` 完全一致，见 ERROR.md E36；想加一条就得两边一起加，这里没这个必要）；
//   ② **判据比原版细一档**：原版是"落在第 i 个标签 = 放到第 i 位"，本版按**指针有没有越过
//      目标标签的中心**分"插到它前面 / 插到它后面"（与本项目其它拖拽排序同一口径，
//      见 `DropIndexCalculator`）。好处是拖动时能画出一根明确的插入竖条，并且能表达
//      "放到最后一个之后"（原版靠"落在最后一个标签上"近似）。
//      ⚠️ 左半格落在第 i 个标签上 == 原版行为；右半格 = 插到它后面（原版表达不出来的那一档）。
//
// 为什么放在 Core：这里两件事都是**算数**（落点是第几位、from/insert 换算成 DataStore 的 to），
// 与 WinUI 无关、天生可单测；UI 那侧只剩"读指针坐标 → 喂进来 → 把结果交给 DataStore"。

using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Services;

/// <summary>
/// 一次标签重排的落库计划（<see cref="DataStore.ReorderTabs"/> 的两个参数）。
/// <para>⚠️ <see cref="ToIndex"/> 是**移除之后**的下标口径 —— 与
/// <see cref="DataStore.ReorderTabs"/> 的"先 RemoveAt 再 Insert"语义一致
/// （原版 `_on_drag_reorder` 也是 `pop` 再 `insert`）。</para>
/// </summary>
public readonly record struct TabReorderPlan(int FromIndex, int ToIndex);

/// <summary>标签页拖动重排的纯逻辑（几何 + 下标换算）。</summary>
public static class TabReorder
{
    /// <summary>
    /// 指针落在"第几个标签之前"（返回值 0..标签数）。
    ///
    /// <para><b>判据</b>：数一数有多少个标签的**水平中心在指针左边** —— 压在中心的算"已越过"
    /// （用 <c>&gt;=</c>，与 <see cref="DropIndexCalculator"/> 的"越过中心"同一口径）。</para>
    ///
    /// <para>为什么用"中心线"而不是"边界线"：标签之间有 3 DIP 的间距，若按"越过左边界"判定，
    /// 指针停在两个标签之间的空隙里会算成"插到后一个之前"（= 前一个之后），看着没错；
    /// 但指针停在某个标签的右半格时也会算成"插到它前面"，与用户"我放在它后面"的心理预期相反。
    /// 中心线判据在这两种情况下都对，且天然覆盖两端：指针在最左端 ⇒ 0（插到最前），
    /// 在最右端/标签栏空白处 ⇒ 标签数（放到最后）。</para>
    ///
    /// <para>⚠️ 传进来的矩形必须是**界面上的视觉顺序**（标签栏是横向单行，只用到
    /// <see cref="ItemBounds.X"/> 与 <see cref="ItemBounds.Width"/>；Y/Height 不参与判定）。</para>
    /// </summary>
    public static int ComputeInsertIndex(IReadOnlyList<ItemBounds> tabBounds, double pointerX)
    {
        if (tabBounds.Count == 0)
        {
            return 0;
        }

        int insert = 0;
        foreach (var bound in tabBounds)
        {
            double center = bound.X + bound.Width / 2;
            if (pointerX >= center)
            {
                insert++;
            }
        }

        return Math.Clamp(insert, 0, tabBounds.Count);
    }

    /// <summary>
    /// 把"从第 <paramref name="fromIndex"/> 个被拖起、落在第 <paramref name="insertIndex"/> 个之前"
    /// 换算成 <see cref="DataStore.ReorderTabs"/> 的 <c>to</c>。
    ///
    /// <para>⚠️ 换算的必要性：移除被拖的那一项之后，**它后面所有项的下标都会前移一位**，
    /// 所以"往右拖"时 <c>to = insertIndex - 1</c>、"往左拖"时 <c>to = insertIndex</c>。
    /// 漏掉这一步的典型症状是"往后拖一格只挪了半格/没动"。</para>
    /// </summary>
    /// <returns>
    /// 该落库的计划；<c>null</c> = **什么都不用做**（只有一页 / 下标越界 / 拖回原位 /
    /// 落点换算后与起点相同）—— 调用方据此**不落库、不刷新**（原版 `from == to` 时也不发信号）。
    /// </returns>
    public static TabReorderPlan? Resolve(int fromIndex, int insertIndex, int tabCount)
    {
        if (tabCount <= 1 || fromIndex < 0 || fromIndex >= tabCount)
        {
            return null;
        }

        // 落点先夹进合法区间：算出来的插入位允许等于 tabCount（= 放到最后）
        int insert = Math.Clamp(insertIndex, 0, tabCount);

        // 移动方向决定要不要补偿"移除后下标前移"这一位
        int to = insert > fromIndex ? insert - 1 : insert;
        to = Math.Clamp(to, 0, tabCount - 1);

        return to == fromIndex ? null : new TabReorderPlan(fromIndex, to);
    }
}
