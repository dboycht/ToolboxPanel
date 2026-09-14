// FilePickers.cs —— 文件 / 文件夹选择器（新建图标时用）
//
// 用 Windows App SDK 自带的 **Microsoft.Windows.Storage.Pickers**（而不是老 WinRT 的
// Windows.Storage.Pickers）：新 API 直接收 WindowId、**不需要** InitializeWithWindow 那套
// hwnd 初始化，unpackaged 应用也能用（本项目就是 unpackaged + self-contained）。
//
// 与主窗口、编辑对话框共用，避免两处各写一遍选择器逻辑。

using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace ToolboxPanel.Views;

/// <summary>选择器小工具。取消选择一律返回 null（调用方据此原地返回，不做任何事）。</summary>
internal static class FilePickers
{
    /// <summary>选一个文件。<paramref name="extensions"/> 为 null 时选任意文件。</summary>
    public static async Task<string?> PickFileAsync(WindowId windowId, IReadOnlyList<string>? extensions)
    {
        var picker = new FileOpenPicker(windowId);

        if (extensions is null || extensions.Count == 0)
        {
            picker.FileTypeFilter.Add("*");
        }
        else
        {
            foreach (var extension in extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }
        }

        var file = await picker.PickSingleFileAsync();
        return string.IsNullOrEmpty(file?.Path) ? null : file.Path;
    }

    /// <summary>选一个文件夹。</summary>
    public static async Task<string?> PickFolderAsync(WindowId windowId)
    {
        var picker = new FolderPicker(windowId);
        var folder = await picker.PickSingleFolderAsync();
        return string.IsNullOrEmpty(folder?.Path) ? null : folder.Path;
    }
}
