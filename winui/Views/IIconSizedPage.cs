// IIconSizedPage.cs —— 内容页的"图标大小三档"契约
//
// 只有**网格页**有图标图块，所以这个契约单独拆出来（不塞进 `IAnimatedPage`，
// 那个接口只管动效，两份职责不要混）。主窗口用 `OfType<IIconSizedPage>()` 统一下发。

using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Views;

/// <summary>页面能按"图标大小"档位调整自己的图块尺寸（尺寸表来自 Core 的 <see cref="IconSizeMetrics"/>）。</summary>
internal interface IIconSizedPage
{
    /// <summary>套用一档尺寸；重复套用同一档必须是幂等的。</summary>
    void ApplyIconSize(IconSizeMetrics metrics);
}
