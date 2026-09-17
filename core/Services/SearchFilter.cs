// SearchFilter.cs —— 搜索过滤（W5）的**纯逻辑**部分
//
// 原版基线（两份参考都看过，取其并集）：
//   · v1.11.6（Widget 版，`icon_grid._apply_filter`）：**只对图标页按 display_name 做子串过滤**，
//     大小写不敏感（`query = text.strip().lower()`），命中的留在布局里、没命中的 `setVisible(False)`；
//     搜索栏本身默认隐藏，Ctrl+F / 菜单切换，Esc 关闭（`app_window._toggle_search`）。
//   · v2（QML 版，`ui/bridge._rebuild_icons`）：图标按 display_name，**列表项按 description 或 path**。
//
// 本项目取的口径（用户 2026-09-16 选择题确认「按名称 / 路径过滤当前页」）：
//   · 图标：display_name **或** source_path **或** target_path 命中即可；
//   · 列表项：description **或** path 命中即可；
//   · 空查询 = 不过滤（全部可见）；查询首尾空白忽略；大小写不敏感（Ordinal，与 Windows 路径语义一致）。
//   ⚠️ 图标多出"按路径"这一条是**有意比 v1.11.6 宽**（与 QML 版的列表项口径对齐）：
//      用户的图标名常常是"记事本"这类中文简名，光按名字搜不到路径里的 `notepad`。
//
// 另一件必须放在 Core 的事：**过滤视图的落点索引 → Core 列表索引的换算**。
//   过滤后界面上只显示命中的那一部分，而 `DataStore.ApplyDragDrop` 的 `TargetIndex` 是
//   **Core 列表里的下标**；不换算就会出现"拖到第 2 个可见位、实际插到别的图标后面"。
//   换算规则见 `MapViewIndexToModelIndex`（有单测）。

using ToolboxPanel.Core.Models;

namespace ToolboxPanel.Core.Services;

/// <summary>搜索过滤：命中判定 + 过滤视图索引到 Core 列表索引的换算（都可单测）。</summary>
public static class SearchFilter
{
    /// <summary>搜索框占位文字（原版 i18n <c>search.placeholder</c>：「搜索图标…」）。</summary>
    public const string PlaceholderText = "搜索图标…";

    /// <summary>网格页"一个都没命中"的提示（原版 i18n <c>search.no_result</c>）。</summary>
    public const string NoResultIconText = "没有匹配的图标";

    /// <summary>列表页"一个都没命中"的提示（WinUI 线新增：原版只有图标页，没有这条 key）。</summary>
    public const string NoResultListText = "没有匹配的列表项";

    /// <summary>查询是否生效（空 / 全空白 = 不过滤）。</summary>
    public static bool IsActive(string? query) => !string.IsNullOrWhiteSpace(query);

    /// <summary>
    /// 这个图标命不命中查询（名称 / 来源路径 / 目标路径，任一含子串即可；大小写不敏感）。
    /// <para>空查询一律命中（调用方不必先判 IsActive）。</para>
    /// </summary>
    public static bool Matches(IconModel icon, string? query)
    {
        var needle = query?.Trim();
        if (string.IsNullOrEmpty(needle))
        {
            return true;
        }

        return Contains(icon.DisplayName, needle)
               || Contains(icon.SourcePath, needle)
               || Contains(icon.TargetPath, needle);
    }

    /// <summary>这一行列表项命不命中查询（说明 / 路径，任一含子串即可；大小写不敏感）。</summary>
    public static bool Matches(ListItemModel item, string? query)
    {
        var needle = query?.Trim();
        if (string.IsNullOrEmpty(needle))
        {
            return true;
        }

        return Contains(item.Description, needle) || Contains(item.Path, needle);
    }

    /// <summary>
    /// ★ 把"落点在**过滤视图**里的第几位"换算成"Core 列表里的第几位"。
    ///
    /// <para>口径与 <c>DropIndexCalculator</c> 保持一致：**插到当前第 N 项之前**，N == 可见项数表示"放到最后"。
    /// 于是：</para>
    /// <list type="bullet">
    /// <item><c>0 .. 可见数-1</c> ⇒ 第 N 个**可见项**在 Core 列表里的下标；</item>
    /// <item><c>≥ 可见数</c> ⇒ 最后一个可见项的下标 + 1（= 插在最后一个可见项后面，
    /// 而不是"整页末尾"—— 过滤态下"末尾"应当指看得见的末尾）；</item>
    /// <item>可见项为空 ⇒ 0（此时其实无可拖项，兜底不抛异常）。</item>
    /// </list>
    ///
    /// <para>⚠️ 不做过滤（可见 = 全部）时它是恒等映射，所以调用方可以**无条件**走这一层，
    /// 不必到处写"有没有过滤"的分支。</para>
    /// </summary>
    public static int MapViewIndexToModelIndex(
        IReadOnlyList<string> visibleIds, IReadOnlyList<string> allIds, int viewIndex)
    {
        if (visibleIds.Count == 0)
        {
            return 0;
        }

        if (viewIndex <= 0)
        {
            return Math.Max(0, IndexOf(allIds, visibleIds[0]));
        }

        if (viewIndex < visibleIds.Count)
        {
            var index = IndexOf(allIds, visibleIds[viewIndex]);
            return index >= 0 ? index : allIds.Count;
        }

        var lastIndex = IndexOf(allIds, visibleIds[^1]);
        return lastIndex >= 0 ? lastIndex + 1 : allIds.Count;
    }

    /// <summary>搜索栏上的匹配计数（如「匹配 3 / 19」）—— 让"为什么只看到几个"一眼可读。</summary>
    public static string CountText(int matched, int total) => $"匹配 {matched} / {total}";

    private static bool Contains(string? text, string needle)
        => !string.IsNullOrEmpty(text) && text.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static int IndexOf(IReadOnlyList<string> ids, string id)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
