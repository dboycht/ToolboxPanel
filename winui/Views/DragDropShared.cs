// DragDropShared.cs —— 网格页 / 列表页**逐字重复**的拖放辅助代码，统一搬到这里
//
// ⚠️ 本文件是**纯搬运**（2026-09-16）：下面每个方法的方法体都从两个页面**原样搬来**，
//    只为参数化"控制 / 可见数 / 坐标空间 / 日志标签"做了必要的改写。
//    **不要**在这里改判据、改日志文案、改注释里的结论 —— 拖放是本项目最难搞定的一段
//    （ERROR.md E23–E26：WinUI 3 收不到起拖/指针事件，只能靠"目标端 DragOver 登记落点 +
//    源端 DragItemsCompleted 收口"），两个页面的手感只能由用户手动验证（代理不注入鼠标输入，见 E5）。

using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;
using Windows.ApplicationModel.DataTransfer;

namespace ToolboxPanel.Views;

internal static class DragDropShared
{
    /// <summary>拖动链路诊断 —— 写 `%TEMP%\toolboxpanel-probe.log`（手感类问题只能用户手试，
    /// 有了这条链路日志，用户试一次就能定位"哪一步断了"）。</summary>
    public static void Trace(string message)
        => App.ProbeLog($"[拖动 {DateTime.Now:HH:mm:ss.fff}] {message}");

    /// <summary>
    /// 读一下这次拖放到底带了什么（诊断用；读不到就当作没有）。
    /// <paramref name="owner"/> 是崩溃日志的标签前缀（如 <c>"GridPage.DescribeData"</c> ⇒ <c>"GridPage.DescribeData/text"</c>）。
    /// </summary>
    public static (bool HasText, string? Text, bool HasStorageItems) Describe(DragEventArgs e, string owner)
    {
        bool hasText = false;
        string? text = null;
        bool hasStorage = false;

        try
        {
            hasText = e.DataView.Contains(StandardDataFormats.Text);
            if (hasText)
            {
                text = e.DataView.GetTextAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            App.WriteCrash($"{owner}/text", ex);
        }

        try
        {
            hasStorage = e.DataView.Contains(StandardDataFormats.StorageItems);
        }
        catch (Exception ex)
        {
            App.WriteCrash($"{owner}/storage", ex);
        }

        return (hasText, text, hasStorage);
    }

    /// <summary>
    /// 把落点（相对 <paramref name="host"/> 的坐标）交给 Core 的几何计算，得到"插到第几个"。
    /// </summary>
    public static int ComputeInsertIndex(DragEventArgs e, FrameworkElement host, List<ItemBounds> bounds)
    {
        if (bounds.Count == 0)
        {
            return 0;
        }

        var position = e.GetPosition(host);
        return DropIndexCalculator.Compute(bounds, position.X, position.Y);
    }

    /// <summary>
    /// 收集每个已实现项的矩形（**相对 <paramref name="coordinateSpace"/>**，DIP），顺序 = 界面上看到的顺序。
    /// ⚠️ 只用"已实现"的容器：GridView / ListView 会虚拟化，屏幕外的项没有容器，
    ///    查不到就跳过（而不是塞一个 0 尺寸的假矩形，那会把落点算歪）。
    /// ⚠️ 遍历的是**可见集合**（= ItemsSource）：算出来的落点是"可见位"，
    ///    过滤态下由 MainViewModel 用 Core 的换算函数换回 Core 下标。
    /// <paramref name="owner"/> 是崩溃日志的标签（如 <c>"GridPage.CollectItemBounds"</c>）。
    /// </summary>
    public static List<ItemBounds> CollectBounds(
        ListViewBase list, int visibleCount, FrameworkElement coordinateSpace, string owner)
    {
        var result = new List<ItemBounds>(visibleCount);

        for (int i = 0; i < visibleCount; i++)
        {
            if (list.ContainerFromIndex(i) is not FrameworkElement container)
            {
                continue;
            }

            try
            {
                var origin = container.TransformToVisual(coordinateSpace)
                    .TransformPoint(new Windows.Foundation.Point(0, 0));
                result.Add(new ItemBounds(origin.X, origin.Y, container.ActualWidth, container.ActualHeight));
            }
            catch (Exception ex)
            {
                App.WriteCrash(owner, ex);
            }
        }

        return result;
    }

    /// <summary>网格页的插入**竖条**（几何与列表页的横条不同，故分成两个方法）。</summary>
    public static void ShowVerticalIndicator(Border indicator, IReadOnlyList<ItemBounds> bounds, int insertIndex)
    {
        if (bounds.Count == 0)
        {
            HideIndicator(indicator);
            return;
        }

        var (x, y, height) = DropIndexCalculator.IndicatorAt(bounds, insertIndex);

        // ⚠️ 竖条要**骑在边界线上**（左移半个条宽），看起来才是"插在两块之间"，
        //    而不是"盖在右边那一块上"。
        Canvas.SetLeft(indicator, x - indicator.Width / 2);
        Canvas.SetTop(indicator, y);
        indicator.Height = Math.Max(8, height);
        indicator.Visibility = Visibility.Visible;
    }

    /// <summary>列表页的插入**横条**（几何与网格页的竖条不同，故分成两个方法）。</summary>
    public static void ShowHorizontalIndicator(Border indicator, IReadOnlyList<ItemBounds> bounds, int insertIndex)
    {
        if (bounds.Count == 0)
        {
            HideIndicator(indicator);
            return;
        }

        var (x, y, _) = DropIndexCalculator.IndicatorAt(bounds, insertIndex);

        // 横条要横跨整行宽度（不能只画在某一行的左边界那么宽）
        double width = 0;
        foreach (var bound in bounds)
        {
            width = Math.Max(width, bound.X + bound.Width);
        }

        Canvas.SetLeft(indicator, x);
        // ⚠️ 横条要**骑在行边界上**（上移半个条高），看起来才是"插在两行之间"。
        Canvas.SetTop(indicator, y - indicator.Height / 2);
        indicator.Width = Math.Max(24, width - x);
        indicator.Visibility = Visibility.Visible;
    }

    public static void HideIndicator(Border indicator) => indicator.Visibility = Visibility.Collapsed;
}
