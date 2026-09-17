// ISearchablePage.cs —— 内容页的"搜索过滤"契约（W5）
//
// 主窗口只认这个接口：搜索框内容一变，就把查询下发给**每一页**（网格页 / 列表页各按自己的项过滤）。
// 判定与"可见位 → Core 下标"的换算在 Core 的 `SearchFilter`（有单测）；
// 页面只负责"把可见集合、空页提示、批量勾选状态摆对"。

namespace ToolboxPanel.Views;

public interface ISearchablePage
{
    /// <summary>
    /// 套用查询（null / 空 / 全空白 = 取消过滤，全部显示）。
    /// <para>幂等：同一个查询重复下发不产生任何变化。</para>
    /// </summary>
    void ApplySearch(string? query);
}
