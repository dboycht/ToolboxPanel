// AcrylicThinBackdrop.cs —— ToolboxPanel v2 · WinUI 3 样板（W0）补充件
//
// 为什么需要这个类（实测结论，2026-09-12）：
//   XAML 便捷类 `DesktopAcrylicBackdrop` **没有 Kind 属性**，
//   所以「Desktop Acrylic 的 Thin 变体」没法用它选。
//   `DesktopAcrylicController` 才有 `Kind`（Base/Thin）以及 TintColor / TintOpacity /
//   LuminosityOpacity / FallbackColor 等**可调参数** —— 这正是后续主题引擎
//   （5 预置 + 8 参数细调）控制「整窗玻璃浓度」所需的把手。
//
//   结论：想要可控的窗口材质（而不是系统默认值），必须自己继承 SystemBackdrop；
//   本类就是这条路径的最小可运行验证，同时它也给 W4 主题引擎提供了落点。

using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ToolboxPanel;

/// <summary>整窗 Desktop Acrylic（Thin 变体）—— 由 DesktopAcrylicController 驱动。</summary>
internal sealed class AcrylicThinBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        try
        {
            _controller = new DesktopAcrylicController
            {
                Kind = DesktopAcrylicKind.Thin,   // Thin 比 Base 更透
            };

            // 系统级配置（高对比度 / 省电 / 焦点状态等）由框架托管
            _controller.SetSystemBackdropConfiguration(
                GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot));
            _controller.AddSystemBackdropTarget(connectedTarget);
        }
        catch (Exception ex)
        {
            _controller = null;
            App.WriteCrash("AcrylicThinBackdrop.OnTargetConnected", ex);
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);

        try
        {
            _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
            _controller?.Dispose();
        }
        catch (Exception ex)
        {
            App.WriteCrash("AcrylicThinBackdrop.OnTargetDisconnected", ex);
        }
        finally
        {
            _controller = null;
        }
    }
}
