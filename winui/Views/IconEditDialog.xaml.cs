// IconEditDialog.xaml.cs —— 「新建 / 编辑图标」的字段对话框
//
// 对应原版 v1.11.6 的 src/toolbox/icon_edit_dialog.py（字段组合、prefill、自定义图标语义照搬）。
// 校验逻辑不在本文件 —— 在 Core 的 IconEditor（ValidateCreate / ValidateEdit），
// 这里只负责「把控件里的值装进 IconEditDraft → 问 Core 能不能存 → 能存就交出去」。

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Views;

public sealed partial class IconEditDialog : ContentDialog
{
    private readonly IconType _type;
    private readonly IconModel? _editing;         // 非空 = 编辑模式（原版 apply() 的 is_creating=False）
    private readonly WindowId _windowId;          // 选择器要用（Microsoft.Windows.Storage.Pickers 免 hwnd）
    private readonly string _shortcutSourcePath;  // 新建快捷方式时，文件选择框选中的那个 .lnk

    /// <summary>
    /// 点「确定」且校验通过后的字段草稿；取消 / 校验不通过时为 null。
    /// 调用方拿它去 <see cref="IconEditor.Create"/> / <see cref="IconEditor.Edit"/>。
    /// </summary>
    public IconEditDraft? Draft { get; private set; }

    private IconEditDialog(IconType type, IconModel? editing, IconEditDraft? prefill, WindowId windowId)
    {
        _type = type;
        _editing = editing;
        _windowId = windowId;
        _shortcutSourcePath = prefill?.ShortcutSourcePath ?? string.Empty;

        InitializeComponent();

        Title = editing is null ? CreateTitle(type) : "编辑图标属性";
        TypeHint.Text = TypeLabel(type);
        MainLabel.Text = MainFieldLabel(type);
        MainBox.PlaceholderText = MainPlaceholder(type);
        NameBox.PlaceholderText = type is IconType.Url ? "我的网站" : type is IconType.Command ? "备份脚本" : "显示名称";

        ApplyPrefill(prefill);
        ApplyTypeVisibility();
    }

    /// <summary>新建模式的对话框。<paramref name="prefill"/> 通常来自「文件/文件夹/.lnk 选择框」。</summary>
    public static IconEditDialog ForCreate(IconType type, IconEditDraft? prefill, WindowId windowId)
        => new(type, null, prefill, windowId);

    /// <summary>编辑模式的对话框（字段从模型预填；类型不可改）。</summary>
    public static IconEditDialog ForEdit(IconModel icon, WindowId windowId)
        => new(icon.Type, icon, BuildEditPrefill(icon), windowId);

    // ────────────────────────────── 事件：确定 / 浏览 / 重置 ──────────────────────────────

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var draft = BuildDraft();
        var error = _editing is null
            ? IconEditor.ValidateCreate(draft)
            : IconEditor.ValidateEdit(_editing, draft);

        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            args.Cancel = true;   // 不关对话框：改完再点
            return;
        }

        Draft = draft;
    }

    private async void OnBrowseMainClick(object sender, RoutedEventArgs e)
    {
        string? picked = _type switch
        {
            IconType.Folder => await FilePickers.PickFolderAsync(_windowId),
            IconType.Shortcut => await FilePickers.PickFileAsync(_windowId, new[] { ".lnk" }),
            IconType.Command => await FilePickers.PickFileAsync(_windowId, new[] { ".exe", ".bat", ".cmd", ".ps1", ".com" }),
            _ => await FilePickers.PickFileAsync(_windowId, null),
        };

        if (!string.IsNullOrEmpty(picked))
        {
            MainBox.Text = picked;
        }
    }

    private async void OnBrowseWdClick(object sender, RoutedEventArgs e)
    {
        var picked = await FilePickers.PickFolderAsync(_windowId);
        if (!string.IsNullOrEmpty(picked))
        {
            WdBox.Text = picked;
        }
    }

    private async void OnBrowseIconClick(object sender, RoutedEventArgs e)
    {
        var picked = await FilePickers.PickFileAsync(_windowId, new[] { ".exe", ".dll", ".ico", ".lnk" });
        if (!string.IsNullOrEmpty(picked))
        {
            IconPathBox.Text = picked;
        }
    }

    private void OnResetIconClick(object sender, RoutedEventArgs e)
    {
        // 原版：清掉自定义图标文件 + 索引归 0（= 恢复用目标程序的默认图标）
        IconPathBox.Text = string.Empty;
        IndexBox.Value = 0;
    }

    // ────────────────────────────── 组装 ──────────────────────────────

    private IconEditDraft BuildDraft() => new()
    {
        Type = _type,
        DisplayName = NameBox.Text,
        Path = MainBox.Text,
        Arguments = ArgsBox.Text,
        WorkingDir = WdBox.Text,
        Description = DescBox.Text,
        CustomIconPath = IconPathBox.Text,
        CustomIconIndex = ReadIndex(),
        ShortcutSourcePath = _shortcutSourcePath,
    };

    private int ReadIndex()
    {
        var value = IndexBox.Value;
        return double.IsNaN(value) ? 0 : (int)Math.Clamp(value, 0, 999);
    }

    private void ApplyPrefill(IconEditDraft? prefill)
    {
        if (prefill is null)
        {
            return;
        }

        NameBox.Text = prefill.DisplayName;
        MainBox.Text = prefill.Path;
        ArgsBox.Text = prefill.Arguments;
        WdBox.Text = prefill.WorkingDir;
        DescBox.Text = prefill.Description;
        if (!string.IsNullOrWhiteSpace(prefill.CustomIconPath))
        {
            IconPathBox.Text = prefill.CustomIconPath;
            IndexBox.Value = prefill.CustomIconIndex;
        }
    }

    /// <summary>编辑模式：把模型上已有的字段装进草稿（原版弹窗时的初始值）。</summary>
    private static IconEditDraft BuildEditPrefill(IconModel icon) => new()
    {
        Type = icon.Type,
        DisplayName = icon.DisplayName,
        Path = MainPathOf(icon),
        Arguments = icon.Arguments,
        WorkingDir = icon.WorkingDir,
        Description = icon.Description,
    };

    /// <summary>主路径框的初值：文件/文件夹/快捷方式取「目标优先」，网址/命令取各自的主字段。</summary>
    private static string MainPathOf(IconModel icon) => icon.Type switch
    {
        IconType.Url => icon.SourcePath,
        IconType.Command => icon.TargetPath,
        _ => string.IsNullOrWhiteSpace(icon.TargetPath) ? icon.SourcePath : icon.TargetPath,
    };

    // ────────────────────────────── 文案与可见性 ──────────────────────────────

    private void ApplyTypeVisibility()
    {
        bool isShortcut = _type == IconType.Shortcut;
        bool hasArgsAndWorkingDir = _type is IconType.Shortcut or IconType.Command;

        SetRowVisible(ArgsLabel, hasArgsAndWorkingDir);
        SetRowVisible(ArgsBox, hasArgsAndWorkingDir);
        SetRowVisible(WdLabel, hasArgsAndWorkingDir);
        SetRowVisible(WdBox, hasArgsAndWorkingDir);
        SetRowVisible(BrowseWdButton, hasArgsAndWorkingDir);

        SetRowVisible(DescLabel, isShortcut);
        SetRowVisible(DescBox, isShortcut);

        SetRowVisible(IconLabel, isShortcut);
        IconRowPanel.Visibility = isShortcut ? Visibility.Visible : Visibility.Collapsed;

        // 网址没有对应的系统选择器 → 主字段的浏览按钮只在非网址时显示
        BrowseMainButton.Visibility = _type == IconType.Url ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void SetRowVisible(FrameworkElement element, bool visible)
        => element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private static string CreateTitle(IconType type) => type switch
    {
        IconType.File => "新建文件图标",
        IconType.Folder => "新建文件夹图标",
        IconType.Shortcut => "新建快捷方式图标",
        IconType.Url => "新建网址图标",
        _ => "新建命令图标",
    };

    private static string TypeLabel(IconType type) => type switch
    {
        IconType.File => "文件图标",
        IconType.Folder => "文件夹图标",
        IconType.Shortcut => "快捷方式图标",
        IconType.Url => "网址图标",
        _ => "命令图标",
    };

    private static string MainFieldLabel(IconType type) => type switch
    {
        IconType.Url => "网址:",
        IconType.Command => "命令:",
        _ => "路径:",
    };

    private static string MainPlaceholder(IconType type) => type switch
    {
        IconType.Url => "https://example.com",
        IconType.Command => "python",
        IconType.Folder => "C:\\Program Files",
        IconType.Shortcut => "目标程序或 .lnk 路径",
        _ => "C:\\Program Files\\app.exe",
    };
}
