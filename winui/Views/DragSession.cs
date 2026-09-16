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
//   · 目标页 / 标签栏：DragOver → ReportTarget(tabId, kind, insertIndex)
//   · 源页：DragItemsCompleted → TakeTarget() + args.Items + DropResult == Move → 落库
//   · 结束（无论落下还是取消）：ClearTarget()

using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Views;

/// <summary>拖动中登记的落点（最后写入者生效 = 松手那一刻指针所在处）。</summary>
internal readonly record struct DragTarget(string TabId, DragItemKind Kind, int InsertIndex);

internal static class DragSession
{
    /// <summary>拖动中最后登记的落点（null = 本次拖动还没进过任何目标区域）。</summary>
    public static DragTarget? Target { get; private set; }

    /// <summary>目标页/标签栏在 DragOver 里登记落点。</summary>
    public static void ReportTarget(string tabId, DragItemKind kind, int insertIndex)
        => Target = new DragTarget(tabId, kind, insertIndex);

    /// <summary>取走落点并清空（源页在 DragItemsCompleted 里调用）。</summary>
    public static DragTarget? TakeTarget()
    {
        var target = Target;
        Target = null;
        return target;
    }

    public static void ClearTarget() => Target = null;

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
