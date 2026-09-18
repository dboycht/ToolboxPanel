// BackupProgressDialog.xaml.cs —— 导出/导入备份的进度对话框
//
// 与 Core 的 BackupManager 解耦：Core 通过 progress/log 回调把消息送回，
// 主窗口用 DispatcherQueue 把它们转到 UI 线程再调这里的方法（★ UI 线程纪律）。

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolboxPanel.Core;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Views;

public sealed partial class BackupProgressDialog : ContentDialog
{
    /// <summary>操作进行中：此时不允许关闭（原版 ProgressDialog 同语义）。</summary>
    private bool _running = true;

    public BackupProgressDialog(string title)
    {
        InitializeComponent();

        Title = title;
        CloseButtonText = I18n.T("btn.close");   // ContentDialog 的按钮文字 Tr 管不到，这里设
        Closing += OnClosing;
    }

    /// <summary>进度（Core 的 progress 回调，**已切回 UI 线程**）。</summary>
    public void Report(BackupProgress progress)
    {
        Bar.Maximum = Math.Max(1, progress.Total);
        Bar.Value = Math.Clamp(progress.Current, 0, Bar.Maximum);
    }

    /// <summary>追加一行日志并滚到底（Core 的 log 回调，**已切回 UI 线程**）。</summary>
    public void AppendLog(string line)
    {
        LogText.Text = LogText.Text.Length == 0 ? line : LogText.Text + Environment.NewLine + line;

        // 滚到底：布局还没跑完时 ActualHeight 偏小，排到下一帧再滚一次
        LogScroller.ChangeView(null, LogScroller.ScrollableHeight, null);
        DispatcherQueue.TryEnqueue(() => LogScroller.ChangeView(null, LogScroller.ScrollableHeight, null));
    }

    /// <summary>结束：显示结果并放行关闭。</summary>
    public void MarkDone(bool success, string message)
    {
        _running = false;

        // ⚠️ 刻意**不动 Foreground**：对话框已按当前主题设了 RequestedTheme，
        //    默认前景色就是对的；而 `Application.Current.Resources["AccentTextFillColorPrimaryBrush"]`
        //    这类**框架主题刷子**在应用资源字典里未必取得到（取不到会抛/空引用，正好炸在"导出结束"这一刻）。
        //    成功/失败靠文字前缀与加粗区分，够清楚且没有这个风险。
        ResultText.Text = (success ? "✅ " : "❌ ") + message;
        ResultText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        ResultText.Visibility = Visibility.Visible;
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_running)
        {
            // 原版："操作进行中，无法关闭" —— 这里直接取消关闭，日志里已有进度可看
            args.Cancel = true;
        }
    }
}
