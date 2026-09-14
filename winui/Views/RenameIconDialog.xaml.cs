// RenameIconDialog.xaml.cs —— 「重命名」小对话框
//
// 对应原版 v1.11.6 的「内联改名 + data_store.rename_icon」：
// 原版是在标签上就地编辑，WinUI 这里用一个迷你对话框代替（行为一致：名字必填、只改名字）。
//
// ⚠️ 校验规则与文案来自 Core（`IconEditor.ErrorNameRequired`），
//    不在 UI 里另写一份"名称不能为空"，免得两处慢慢漂移。

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Views;

public sealed partial class RenameIconDialog : ContentDialog
{
    /// <summary>点「确定」且校验通过后的新名字；取消 / 校验不过时为 null。</summary>
    public string? NewName { get; private set; }

    private RenameIconDialog(string? currentName)
    {
        InitializeComponent();

        NameBox.Text = currentName ?? string.Empty;
        NameBox.SelectAll();

        // 打开即聚焦输入框（ContentDialog 显示后焦点才会生效，用 Loaded 兜一下）
        NameBox.Loaded += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    /// <summary>由 MainWindow 调用（它会先把 RequestedTheme 设好再 ShowAsync）。</summary>
    public static RenameIconDialog Create(string? currentName) => new(currentName);

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            ErrorText.Text = IconEditor.ErrorNameRequired;
            ErrorText.Visibility = Visibility.Visible;
            args.Cancel = true;   // 不关窗，改完再点
            return;
        }

        NewName = name;
    }
}
