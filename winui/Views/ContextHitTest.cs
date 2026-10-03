// ContextHitTest.cs —— 「右键点在哪一项上」的**统一判据**（四个右键菜单共用一份）
//
// 为什么需要它（2026-10-03 用户实测报的 bug）：
//   图标/列表行/文本行/标签四处右键菜单，原先各自写了一段**同形**的"往上找容器"：
//       if (current is GridViewItem { DataContext: IconTileViewModel tile }) return tile;
//   ⚠️ 这段判据**永远不成立**，两个原因（缺一不可，合起来就是"永远判定成点在空白处"）：
//     ① 往上走到**模板元素**（Image / TextBlock）时，它**不是** `GridViewItem` ⇒ 这个模式压根不看它；
//        而**数据项恰恰挂在模板元素上**（`ERROR.md` **E47** 实测：模板块 DataContext = 数据项）；
//     ② 走到**容器**（`GridViewItem` / `ListViewItem`）时，它的 `DataContext` 是 **null**（同 E47 实测），
//        所以就算类型对上了，里面的 `DataContext` 也解不出来。
//   ⇒ 结果：图标右键永远弹出"空白处菜单"（只有新建类），用户看到的是
//     "刷新图标怎么没有啊"（2026-10-03）。
//
// 所以判据要**两条路都走**（这正是 E47 给的结论）：
//   ① 模板元素：读它的 `DataContext`（继承自容器的 `Content`，就是数据项）；
//   ② 容器：**绝不读容器的 `DataContext`**，改用官方成对换算
//      `ListViewBase.IndexFromContainer(container)` → 可见集合的下标 → 视图模型。
//
// ⚠️ 传进来的集合必须是**界面上真正在用的那一份**（`VisibleIcons` / `VisibleListItems` /
//    `VisibleTextItems` / `Tabs`）：`IndexFromContainer` 给的是"容器在当前视图里的下标"，
//    过滤态下它与 Core 列表下标**不是一回事**（见 TabItemViewModel 的"搜索过滤"一节）。

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ToolboxPanel.Views;

internal static class ContextHitTest
{
    /// <summary>
    /// 从事件源往上找它承载的那一项；点在空白处返回 null。
    /// </summary>
    /// <typeparam name="TView">视图模型类型（模板元素的 DataContext 类型）。</typeparam>
    /// <param name="source">事件的 <c>OriginalSource</c>。</param>
    /// <param name="list">承载这些项的 <see cref="ListViewBase"/>（网格页是 GridView，其余是 ListView）。</param>
    /// <param name="items">**界面上正在用的**那一份集合（过滤视图，不是 Core 全量列表）。</param>
    public static TView? Resolve<TView>(object? source, ListViewBase list, IReadOnlyList<TView> items)
        where TView : class
    {
        if (source is not DependencyObject element)
        {
            return null;
        }

        DependencyObject? container = null;

        for (var current = element; current is not null;)
        {
            // ① 模板元素这条路：命中即返回（**最常走到的就是它**）
            if (current is FrameworkElement { DataContext: TView view })
            {
                return view;
            }

            // 顺手记下第一个遇到的容器，留给 ② 兜底
            //（网格页命中 GridViewItem、其余命中 ListViewItem）
            if (container is null)
            {
                container = current as GridViewItem;
                container ??= current as ListViewItem;
            }

            try
            {
                current = VisualTreeHelper.GetParent(current);
            }
            catch (Exception)
            {
                // 命中的不是可视元素（例如 Run / TextElement）：这条路走不通了，交给 ②
                break;
            }
        }

        // ② 容器这条路：成对换算，**别读容器的 DataContext**（E47）
        if (container is not null)
        {
            int index = list.IndexFromContainer(container);
            if (index >= 0 && index < items.Count)
            {
                return items[index];
            }
        }

        return null;
    }
}
