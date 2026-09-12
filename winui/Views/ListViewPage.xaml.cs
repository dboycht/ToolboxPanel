// ListViewPage.xaml.cs —— W3 列表页

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolboxPanel.Core.Models;
using ToolboxPanel.ViewModels;

namespace ToolboxPanel.Views;

public sealed partial class ListViewPage : UserControl
{
    private readonly TabItemViewModel _tab;

    public ListViewPage(TabItemViewModel tab)
    {
        _tab = tab;

        InitializeComponent();

        Rows.ItemsSource = tab.ListItems;
        EmptyHint.Visibility = tab.ListItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>点了一行 —— 把该行的路径交给宿主窗口打开。</summary>
    public event EventHandler<ListItemModel>? ItemActivated;

    private void OnRowClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ListRowViewModel row)
        {
            ItemActivated?.Invoke(this, row.Model);
        }
    }

    public TabItemViewModel Tab => _tab;
}
