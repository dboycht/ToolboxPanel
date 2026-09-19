// TabNameDialog.xaml.cs —— 标签页「新建 / 重命名」的名字输入对话框（批次 1）
//
// 对应原版 v1.11.6 的 `AppWindow._prompt_text(title, label, default)`：
//   新建标签页 → `_prompt_text(tab.rename.title, tab.rename.prompt, tab.default_name)`
//   重命名     → `_prompt_text(tab.rename.title, tab.rename.prompt, 当前页名)`
//   ⇒ 标题与输入框标签**完全一样**，只有预填值不同，所以这里一个对话框两用。
//
// ⚠️ 校验规则与文案来自 Core（`TabEditor.ErrorNameRequired`），
//    不在 UI 里另写一份"名称不能为空"，免得两处慢慢漂移（与 RenameIconDialog 同一纪律）。

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolboxPanel.Core;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Views;

public sealed partial class TabNameDialog : ContentDialog
{
    /// <summary>点「确定」且校验通过后的新名字；取消 / 校验不过时为 null。</summary>
    public string? NewName { get; private set; }

    private TabNameDialog(string title, string prompt, string initialName)
    {
        InitializeComponent();

        // 标题与按钮文字（Tr 管不到 ContentDialog 的 Title / 按钮属性）
        Title = title;
        PrimaryButtonText = I18n.T("btn.ok");
        CloseButtonText = I18n.T("btn.cancel");

        PromptText.Text = prompt;
        NameBox.Text = initialName;
        NameBox.SelectAll();

        // 打开即聚焦输入框（ContentDialog 显示后焦点才会生效，用 Loaded 兜一下）
        NameBox.Loaded += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    /// <summary>新建标签页（预填该类型的默认页名）。</summary>
    public static TabNameDialog CreateForNew(bool isList)
        => new(TabContextMenu.RenameTitle, TabContextMenu.RenamePrompt,
               TabContextMenu.DefaultNameFor(isList));

    /// <summary>重命名（预填当前页名）。</summary>
    public static TabNameDialog CreateForRename(string? currentName)
        => new(TabContextMenu.RenameTitle, TabContextMenu.RenamePrompt, currentName ?? string.Empty);

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            ErrorText.Text = TabEditor.ErrorNameRequired;
            ErrorText.Visibility = Visibility.Visible;
            args.Cancel = true;   // 不关窗，改完再点
            return;
        }

        NewName = name;
    }
}
