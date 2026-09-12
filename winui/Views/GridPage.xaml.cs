// GridPage.xaml.cs —— W3 网格页
//
// 只做「把该标签页的图标集合铺出来 + 点一下打开」；
// 创建/编辑/拖拽排序/批量管理属于 W5（右键菜单与对话框）。

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolboxPanel.Core.Models;
using ToolboxPanel.ViewModels;

namespace ToolboxPanel.Views;

public sealed partial class GridPage : UserControl
{
    private readonly TabItemViewModel _tab;

    public GridPage(TabItemViewModel tab)
    {
        _tab = tab;

        InitializeComponent();

        TileGrid.ItemsSource = tab.Icons;
        EmptyHint.Visibility = tab.Icons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>点了某个图标 —— 交给宿主窗口去执行并反馈结果。</summary>
    public event EventHandler<IconModel>? IconActivated;

    private void OnTileClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is IconTileViewModel tile)
        {
            IconActivated?.Invoke(this, tile.Model);
        }
    }

    /// <summary>当前页的标签页视图模型（宿主窗口切页/刷新时用）。</summary>
    public TabItemViewModel Tab => _tab;
}
