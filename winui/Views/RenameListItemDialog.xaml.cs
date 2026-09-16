// RenameListItemDialog.xaml.cs —— 列表行「重命名」小对话框
//
// 对应原版 `list_tab_page._rename_row`（就地编辑列 0 = 说明）：
// 行为一致 —— **只改说明、不碰路径**；⚠️ 说明必填（原版允许清空，见 ListItemEditor 文件头的刻意差异）。
// 校验文案取自 Core（`ListItemEditor.ErrorNameRequired`），不在 UI 里另写一份。

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Views;

public sealed partial class RenameListItemDialog : ContentDialog
{
    /// <summary>点「确定」且校验通过后的新说明；取消 / 校验不过时为 null。</summary>
    public string? NewDescription { get; private set; }

    private RenameListItemDialog(string? currentDescription)
    {
        InitializeComponent();

        NameBox.Header = ListItemEditor.DescriptionLabel;
        NameBox.Text = currentDescription ?? string.Empty;
        NameBox.SelectAll();

        NameBox.Loaded += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    /// <summary>由 MainWindow 调用（它会先把 XamlRoot / RequestedTheme 设好再 ShowAsync）。</summary>
    public static RenameListItemDialog Create(string? currentDescription) => new(currentDescription);

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var description = NameBox.Text?.Trim() ?? string.Empty;
        if (description.Length == 0)
        {
            ErrorText.Text = ListItemEditor.ErrorNameRequired;
            ErrorText.Visibility = Visibility.Visible;
            args.Cancel = true;
            return;
        }

        NewDescription = description;
    }
}
