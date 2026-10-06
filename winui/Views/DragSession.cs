// DragSession.cs —— 进程内"当前拖动会话"
//
// 为什么需要它（2026-09-16 实测，完整排坑见 ERROR.md E25）：
//   为一个 ListView/GridView 的条目拖拽，**下列事件在本项目实测全部收不到**：
//     · XAML `DragStarting=`        —— 从未触发（ListViewBase 内部标记 handled）
//     · XAML `DragItemsStarting=`   —— 同样从未触发
//     · `AddHandler(PointerPressedEvent, …, handledEventsToo: true)` —— 也从未触发（日志里"按下"一行都没有）
//     · `AddHandler(UIElement.DragStartingEvent, …)` / `ListViewBase.DragItemsStartingEvent`
//                                  —— **编译不过**：WinUI 3 不暴露这两个 RoutedEvent
//   ⇒ 既拿不到"拖的是谁"，也拿不到"松手在哪"，而 DataPackage 里永远是空的。
//
// **只有两个事件实测可靠**：
//   ① 目标端 `DragOver`（日志证明能进来）；
//   ② 源端 `DragItemsCompleted` —— **并且 `args.Items` 直接给出被拖的项本身**。
//
// 所以本会话只做一件事：**把"松手会落在哪"在拖动过程中登记下来**（DragOver 里由目标页/标签栏写入），
// 等源端 `DragItemsCompleted` 带着"被拖项 + DropResult"到达时，两边一拼就是完整的一次拖放请求。
//   · 页面 / 标签栏：DragOver → ReportPageTarget / ReportTabStripHover
//   · 源页：DragItemsCompleted → TakeTarget() + args.Items + DropResult == Move → 落库
//   · 结束（无论落下还是取消）：ClearTarget() / EndSession()

using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Views;

/// <summary>拖动中登记的落点（最后写入者生效 = 松手那一刻指针所在处）。</summary>
internal readonly record struct DragTarget(string TabId, DragItemKind Kind, int InsertIndex);

/// <summary>
/// 拖动**最后**落在哪一类区域上（页面内容区 / 标签栏）。
///
/// <para>2026-10-06 加（标签页拖动重排）：两类区域现在**都能发起拖动**——页面里拖条目、
/// 标签栏里拖标签。收口点也是两个（页面自己的 <c>DragItemsCompleted</c>、标签栏自己的），
/// 各自只认自己那一类落点。于是需要一个"最后写入者是谁"的记录：
/// 标签栏收口时若发现最后登记的落点是页面，说明标签被拖出标签栏后松手在内容区里 ⇒ **不重排**。</para>
/// </summary>
internal enum DragRegion
{
    /// <summary>本次拖动还没进过任何已登记的区域。</summary>
    None,

    /// <summary>页面内容区（网格页 / 列表页 / 文本页的 DragOver）。</summary>
    Page,

    /// <summary>标签栏（<c>TabStripView</c> 的 DragOver）。</summary>
    TabStrip,
}

internal static class DragSession
{
    /// <summary>拖动中最后登记的落点（null = 本次拖动还没进过任何目标区域）。</summary>
    public static DragTarget? Target { get; private set; }

    /// <summary>最后登记落点的区域（见 <see cref="DragRegion"/>）。</summary>
    public static DragRegion LastRegion { get; private set; }

    /// <summary>
    /// 标签栏登记的"插到第几个标签之前"（0..标签数）；<c>null</c> = 标签栏上还没有落点。
    /// <para>⚠️ 只有 <see cref="LastRegion"/> == <see cref="DragRegion.TabStrip"/> 时才有意义 ——
    /// 页面登记落点时会把它清掉（指针已经离开标签栏了）。</para>
    /// </summary>
    public static int? TabInsertIndex { get; private set; }

    /// <summary>
    /// 本次拖动里**有没有哪一次 DragOver 落在页面内容区**。
    ///
    /// <para>它是"这次拖的是页面里的条目还是标签栏里的标签"的判据之一：
    /// 条目拖动必然先在**源页面**的 DragOver 里登记过一次（指针一开始就在页面里），
    /// 而从标签栏拖起的标签**一次页面 DragOver 都不会有**。见 <see cref="LooksLikeTabDrag"/>。</para>
    /// </summary>
    public static bool SawPageTarget { get; private set; }

    /// <summary>
    /// 本次拖动被 <c>DragItemsStarting</c> 明确标记为"按标签拖动"时，被拖标签的 id。
    ///
    /// <para>⚠️ 该事件在本项目历史上**实测收不到**（ERROR.md E24/E25），所以它只是**首选判据之一**，
    /// 拿不到时由 <see cref="SawPageTarget"/> 兜底 —— 两条路都写在
    /// <see cref="LooksLikeTabDrag"/> 里（一次探测日志会告诉我们这条事件现在到底通不通）。</para>
    /// </summary>
    public static string? TabDragTabId { get; private set; }

    /// <summary>
    /// 这次拖动"像是从标签栏拖起的标签"。两个判据取**或**：
    /// ① <c>DragItemsStarting</c> 给了被拖的标签（首选）；
    /// ② 本次拖动一次页面 DragOver 都没发生过（标签拖动从标签栏起手 ⇒ 必然如此）。
    /// </summary>
    public static bool LooksLikeTabDrag => TabDragTabId is not null || !SawPageTarget;

    /// <summary>
    /// 页面内容区在 DragOver / Drop 里登记落点（"落到本页第几位"）。
    /// ⚠️ **只有页面用这个方法**：它同时把区域记成"页面"、把标签重排的登记作废
    /// （指针已经不在标签栏上了）—— 标签栏请用 <see cref="ReportTabStripHover"/>。
    /// </summary>
    public static void ReportPageTarget(string tabId, DragItemKind kind, int insertIndex)
    {
        Target = new DragTarget(tabId, kind, insertIndex);
        LastRegion = DragRegion.Page;
        SawPageTarget = true;
        TabInsertIndex = null;
    }

    /// <summary>
    /// 标签栏在 DragOver / Drop 里登记落点，一次写齐三件事：
    /// ① <paramref name="tabId"/> 不为空 ⇒ 落在某个标签上（松手 = 追加到那一页末尾，快手跨页移动）；
    /// ② 标签重排的插入位（0..标签数）；
    /// ③ 区域 = 标签栏（供标签自己的 <c>DragItemsCompleted</c> 收口判断）。
    /// <para>⚠️ <paramref name="tabId"/> 为空（指针在标签栏空白处）时**不动** ① ——
    /// 与改动前一致（原来这里直接 return，落点登记原样保留）。</para>
    /// </summary>
    public static void ReportTabStripHover(string? tabId, DragItemKind kind, int appendIndex, int tabInsertIndex)
    {
        if (!string.IsNullOrEmpty(tabId))
        {
            Target = new DragTarget(tabId, kind, appendIndex);
        }

        TabInsertIndex = tabInsertIndex;
        LastRegion = DragRegion.TabStrip;
    }

    /// <summary>标签栏的 <c>DragItemsStarting</c> 标记"这次拖的是一个标签"（拿不到就靠兜底判据）。</summary>
    public static void BeginTabDrag(string tabId) => TabDragTabId = tabId;

    /// <summary>取走落点并清空（源页在 DragItemsCompleted 里调用）。</summary>
    public static DragTarget? TakeTarget()
    {
        var target = Target;
        Target = null;

        // 一次条目拖动到此结束 ⇒ "本次拖过页面"也复位（下一个拖动重新判定）
        SawPageTarget = false;
        return target;
    }

    /// <summary>一次拖动彻底结束（标签栏收口时调用）：落点、区域、标签重排登记全部复位。</summary>
    public static void EndSession()
    {
        Target = null;
        TabInsertIndex = null;
        LastRegion = DragRegion.None;
        SawPageTarget = false;
        TabDragTabId = null;
    }

    /// <summary>
    /// 单纯点击（没拖动）时顺手清掉落点登记 —— 防止"上次拖动的落点"影响后续判断。
    /// ⚠️ 连"这次拖过页面 / 按标签拖动"的标记一起清：点击意味着本次手势不是拖动。
    /// </summary>
    public static void ClearTarget()
    {
        Target = null;
        TabInsertIndex = null;
        LastRegion = DragRegion.None;
        SawPageTarget = false;
        TabDragTabId = null;
    }

    /// <summary>
    /// 这次拖放能不能当"本应用内部的条目拖动"。
    ///
    /// <para>判据：**DataPackage 里什么格式都没有** —— 本项目的条目拖拽就是这样
    /// （载荷事件收不到，框架给的 DataPackage 永远是空的）。
    /// 外部拖入的文件带 `StorageItems`，别的应用的文本拖入带 `Text`，都会被排除。</para>
    /// </summary>
    public static bool LooksLikeInternalDrag(bool hasText, bool hasStorageItems)
        => !hasText && !hasStorageItems;
}
