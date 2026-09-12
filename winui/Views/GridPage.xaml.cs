// GridPage.xaml.cs —— 网格页
//
// 只做三件事：铺出该标签页的图标集合、点击打开、入场动效。
// 创建/编辑/拖拽排序/批量管理属于 W5。

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Storage;
using ToolboxPanel.ViewModels;

namespace ToolboxPanel.Views;

public sealed partial class GridPage : UserControl, IAnimatedPage
{
    private readonly TabItemViewModel _tab;
    private readonly EntranceAnimator _entrance;

    public GridPage(TabItemViewModel tab)
    {
        _tab = tab;

        InitializeComponent();

        TileGrid.ItemsSource = tab.Icons;
        EmptyHint.Visibility = tab.Icons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _entrance = new EntranceAnimator(TileGrid);
    }

    /// <summary>点了某个图标 —— 交给宿主窗口去执行并反馈结果。</summary>
    public event EventHandler<IconModel>? IconActivated;

    public void ApplyAnimationSpec(AnimationSpec spec) => _entrance.ApplySpec(spec);

    public void PlayEntrance() => _entrance.Play();

    public TabItemViewModel Tab => _tab;

    private void OnTileClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is IconTileViewModel tile)
        {
            IconActivated?.Invoke(this, tile.Model);
        }
    }
}
