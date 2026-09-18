// ListItemEditDialog.xaml.cs —— 列表项「新建 / 编辑属性…」对话框
//
// 对应原版 v1.11.6 `list_tab_page._list_item_dialog`（说明 + 路径 + 选择文件/文件夹）。
// ⚠️ 校验与文案来自 Core 的 `ListItemEditor`（不在这里另写一份），与本项目图块那套对话框同一纪律。

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using ToolboxPanel.Core;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Views;

public sealed partial class ListItemEditDialog : ContentDialog
{
    private readonly WindowId _windowId;

    /// <summary>点「确定」并校验通过后的字段值；取消或校验不过时为 null。</summary>
    public (string Description, string Path)? Result { get; private set; }

    private ListItemEditDialog(WindowId windowId, string title, string description, string path)
    {
        InitializeComponent();

        _windowId = windowId;
        Title = title;

        // 对话框按钮文字（Tr 管不到 ContentDialog 的按钮属性）
        PrimaryButtonText = I18n.T("btn.ok");
        CloseButtonText = I18n.T("btn.cancel");

        // 标题/字段名/占位符都取自 Core（原版 i18n 文案）
        DescriptionBox.Header = ListItemEditor.DescriptionLabel;
        DescriptionBox.PlaceholderText = ListItemEditor.DescriptionPlaceholder;
        PathBox.Header = ListItemEditor.PathLabel;
        PickFileButton.Content = ListItemEditor.SelectFileLabel;
        PickFolderButton.Content = ListItemEditor.SelectFolderLabel;

        DescriptionBox.Text = description;
        PathBox.Text = path;

        DescriptionBox.Loaded += (_, _) => DescriptionBox.Focus(FocusState.Programmatic);
    }

    /// <summary>新建用（字段为空）。</summary>
    public static ListItemEditDialog CreateForNew(WindowId windowId)
        => new(windowId, ListItemEditor.NewItemTitle, string.Empty, string.Empty);

    /// <summary>编辑属性用（预填当前值）。</summary>
    public static ListItemEditDialog CreateForEdit(WindowId windowId, string description, string path)
        => new(windowId, ListItemEditor.EditTitle, description ?? string.Empty, path ?? string.Empty);

    /// <summary>「确定」：两个字段都为空就不关窗、窗内报错（原版是静默忽略，这里明确提示）。</summary>
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var description = DescriptionBox.Text?.Trim() ?? string.Empty;
        var path = PathBox.Text?.Trim() ?? string.Empty;

        if (description.Length == 0 && path.Length == 0)
        {
            ErrorText.Text = ListItemEditor.ErrorBothEmpty;
            ErrorText.Visibility = Visibility.Visible;
            args.Cancel = true;
            return;
        }

        Result = (description, path);
    }

    private async void OnPickFileClick(object sender, RoutedEventArgs e)
    {
        var path = await FilePickers.PickFileAsync(_windowId, null);
        if (!string.IsNullOrEmpty(path))
        {
            PathBox.Text = path;
        }
    }

    private async void OnPickFolderClick(object sender, RoutedEventArgs e)
    {
        var path = await FilePickers.PickFolderAsync(_windowId);
        if (!string.IsNullOrEmpty(path))
        {
            PathBox.Text = path;
        }
    }
}
