// TextItemEditDialog.xaml.cs —— 文本页条目的「新建 / 编辑属性…」对话框
//
// 形态照 `ListItemEditDialog`，字段换成 **备注 + 文本**（文本必填）。
// ⚠️ 校验与文案来自 Core 的 `TextItemEditor`（不在这里另写一份），与其它对话框同一纪律。

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Views;

public sealed partial class TextItemEditDialog : ContentDialog
{
    /// <summary>点「确定」并校验通过后的字段值；取消或校验不过时为 null。</summary>
    public (string Note, string Text)? Result { get; private set; }

    private TextItemEditDialog(string title, string note, string text)
    {
        InitializeComponent();

        // 标题与按钮文字（Tr 管不到 ContentDialog 的 Title / 按钮属性）
        Title = title;
        PrimaryButtonText = Core.I18n.T("btn.ok");
        CloseButtonText = Core.I18n.T("btn.cancel");

        // 字段名与占位符取自 Core（文案表）；XAML 上的 `ui:Tr.*` 会跟着语言刷新，
        // 这里不再赋一遍（同一段文案两个来源，改一处就会来回跳 —— 见 ListItemEditDialog 的注释）。
        NoteBox.Text = note;
        TextBoxInput.Text = text;

        // 打开即聚焦备注（新建时先填"这条是做什么用的"；文本那栏随后 Tab 过去）
        NoteBox.Loaded += (_, _) => NoteBox.Focus(FocusState.Programmatic);
    }

    /// <summary>新建用（字段为空）。</summary>
    public static TextItemEditDialog CreateForNew()
        => new(TextItemEditor.NewItemTitle, string.Empty, string.Empty);

    /// <summary>编辑属性用（预填当前值）。</summary>
    public static TextItemEditDialog CreateForEdit(string? note, string? text)
        => new(TextItemEditor.EditTitle, note ?? string.Empty, text ?? string.Empty);

    /// <summary>「确定」：文本为空就不关窗、窗内报错（文本是这一页的本体，见 TextItemEditor 的说明）。</summary>
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var note = NoteBox.Text?.Trim() ?? string.Empty;
        var text = TextBoxInput.Text?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            ErrorText.Text = TextItemEditor.ErrorTextRequired;
            ErrorText.Visibility = Visibility.Visible;
            args.Cancel = true;   // 不关窗，改完再点
            return;
        }

        Result = (note, text);
    }
}
