// ShortcutDialog.xaml.cs —— 「快捷键参考」窗口（批次 2）
//
// 对应原版 v1.11.6 的 `src/toolbox/shortcut_dialog.py`（帮助菜单 → 快捷键参考）：
// 一张"功能 × 快捷键"的两列表格。
//
// ⚠️ 表格内容**全部来自 Core 的 `ShortcutCatalog`**（不是在这里手写一份列表）：
//    · 功能名 → `ShortcutCatalog.Label(entry)`（走文案表，跟随语言）
//    · 键位   → `entry.Gesture.Display`
//    · 表头   → `ShortcutCatalog.ColumnAction / ColumnKey`
//    这样"目录改了、参考窗口没跟上"这种漂移在结构上不存在。

using Microsoft.UI.Xaml.Controls;
using ToolboxPanel.Core;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Views;

/// <summary>参考窗口里的一行（只给 XAML 的 <c>x:Bind</c> 用，所以属性名要短）。</summary>
public sealed record ShortcutRow(string Action, string Keys);

public sealed partial class ShortcutDialog : ContentDialog
{
    private ShortcutDialog()
    {
        InitializeComponent();

        // 标题与按钮文字（ContentDialog 的 Title/按钮不是可视元素，Tr 管不到）
        Title = ShortcutCatalog.DialogTitle;
        CloseButtonText = I18n.T("btn.ok");

        ActionHeader.Text = ShortcutCatalog.ColumnAction;
        KeyHeader.Text = ShortcutCatalog.ColumnKey;

        // 目录顺序 = 显示顺序（新建类 → 标签页 → 查找/批量 → 数据/退出）
        RowsHost.ItemsSource = ShortcutCatalog.All
            .Select(entry => new ShortcutRow(ShortcutCatalog.Label(entry), entry.Gesture.Display))
            .ToList();
    }

    /// <summary>由 MainWindow 调用（它会先把 RequestedTheme 设好再 ShowAsync）。</summary>
    public static ShortcutDialog Create() => new();
}
