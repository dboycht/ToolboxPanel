// DragSession.cs —— 进程内"当前拖动会话"
//
// 为什么需要它（2026-09-16 实测根因，详见 ERROR.md E25）：
//   `ListViewBase`（GridView/ListView）会**在内部处理并标记 handled** `DragStarting` 与
//   `DragItemsStarting`，于是：
//     · 写在 XAML 上的 `DragStarting=` / `DragItemsStarting=` **一次都收不到**；
//     · WinUI 3 又**不暴露**对应的 RoutedEvent 静态字段（`UIElement.DragStartingEvent` /
//       `ListViewBase.DragItemsStartingEvent` 都不存在）⇒ `AddHandler(..., handledEventsToo: true)`
//       这条路也走不通（实测编译不过）；
//   ⇒ `args.Data.SetText(载荷)` 永远执行不到 ⇒ DataPackage 里一个格式都没有 ⇒
//      目标端 `Contains(Text)` 全是 false ⇒ 只能 `AcceptedOperation = None`
//      （系统光标显示"禁止"）、界面既没有插入条、松手也不插入。
//
// 所以内部重排/跨页移动**不再依赖 DataPackage 载荷**：
//   · 源端：**按下时**就把"正在拖谁"记在这里（指针事件是能收到 `handledEventsToo` 的）；
//   · 目标端（网格页 / 列表页 / 标签栏）：一律读这里判断"这次拖的是不是本应用的项目"。
//
// ⚠️ 生命周期：`Begin` 在按下时调用；`End` 在（拖动结束 / 单纯点击松手）时调用。
//    拖动期间它一直有效 —— DragOver / Drop 都靠它。系统那套空载荷的拖放照常发生
//    （拖拽视觉、Drop 事件、DragItemsCompleted 都由框架给），我们只是不指望它的数据。

using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Views;

internal static class DragSession
{
    /// <summary>正在拖的项目；null = 当前没有本应用发起的拖动。</summary>
    public static DragPayload? Current { get; private set; }

    public static void Begin(DragPayload payload) => Current = payload;

    public static void End() => Current = null;

    /// <summary>取"这次拖放是不是本应用在拖这类东西"，是就返回载荷。</summary>
    public static DragPayload? Take(DragItemKind kind)
        => Current is { } payload && payload.Kind == kind ? payload : null;
}
