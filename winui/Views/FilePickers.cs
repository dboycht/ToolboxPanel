// FilePickers.cs —— 文件 / 文件夹选择器（新建图标时用）
//
// 用 Windows App SDK 自带的 **Microsoft.Windows.Storage.Pickers**（而不是老 WinRT 的
// Windows.Storage.Pickers）：新 API 直接收 WindowId、**不需要** InitializeWithWindow 那套
// hwnd 初始化，unpackaged 应用也能用（本项目就是 unpackaged + self-contained）。
//
// 与主窗口、编辑对话框共用，避免两处各写一遍选择器逻辑。
//
// ⚠️ 为什么这里必须自己 try/catch（而不是让调用方去包）：
//    选择器是 WinRT 调用，会抛 COM 错误（窗口失效、被系统策略拒绝、Shell 组件异常…）。
//    而调用方是对话框里的 `async void OnBrowseClick` 之类的**事件处理器** ——
//    `async void` 里逃出来的异常没有人接得住：`App.UnhandledException` 只负责**记日志**、
//    并不设 `e.Handled = true`，进程照崩（用户看到的是"点一下『…』窗口直接没了"）。
//    取消选择本来就返回 null，所以"失败也返回 null"对调用方是完全一致的语义 ——
//    在这里收口，一条 catch 就覆盖了全部调用点。

using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace ToolboxPanel.Views;

/// <summary>选择器小工具。取消选择 / 选择失败一律返回 null（调用方据此原地返回，不做任何事）。</summary>
internal static class FilePickers
{
    /// <summary>选一个文件。<paramref name="extensions"/> 为 null 时选任意文件。</summary>
    public static async Task<string?> PickFileAsync(WindowId windowId, IReadOnlyList<string>? extensions)
    {
        try
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
        catch (Exception ex)
        {
            // 选不出来就当用户取消：落一份日志，绝不把异常抛回 async void 事件处理器
            App.WriteCrash("FilePickers.PickFileAsync", ex);
            return null;
        }
    }

    /// <summary>选一个文件夹。</summary>
    public static async Task<string?> PickFolderAsync(WindowId windowId)
    {
        try
        {
            var picker = new FolderPicker(windowId);
            var folder = await picker.PickSingleFolderAsync();
            return string.IsNullOrEmpty(folder?.Path) ? null : folder.Path;
        }
        catch (Exception ex)
        {
            App.WriteCrash("FilePickers.PickFolderAsync", ex);
            return null;
        }
    }
}
