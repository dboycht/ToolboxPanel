// App.xaml.cs —— ToolboxPanel v2 · WinUI 3 最小验证样板（W0）
//
// 职责：进程入口。只做三件事：
//   1) 建立 WinUI 应用对象（XamlControlsResources 由 App.xaml 引入）；
//   2) 异常兜底：任何未处理异常都落一份日志，便于诊断（unpackaged 调试没有 IDE 输出窗）；
//   3) 创建并激活 MainWindow（样板窗口）。
//
// ⚠️ unpackaged 模式下不需要 Bootstrap 手动初始化：csproj 里 WindowsPackageType=None +
//    WindowsAppSDKSelfContained=true 时，WindowsAppSDK 的 NuGet 会注入自动引导代码。

using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace ToolboxPanel;

public partial class App : Application
{
    /// <summary>异常日志路径（%TEMP%\toolboxpanel-w0-crash.log）；也供外部验证脚本读取。</summary>
    internal static string CrashLogPath { get; } =
        Path.Combine(Path.GetTempPath(), "toolboxpanel-w0-crash.log");

    private Window? _window;

    public App()
    {
        InitializeComponent();

        UnhandledException += (_, e) => WriteCrash("XamlUnhandledException", e.Exception);
        AppDomain.CurrentDomain.UnhandledException +=
            (_, e) => WriteCrash("AppDomainUnhandledException", e.ExceptionObject as Exception);
        HookUnobservedTaskExceptions();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private static void HookUnobservedTaskExceptions()
    {
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException +=
            (_, e) => WriteCrash("UnobservedTaskException", e.Exception);
    }

    internal static void WriteCrash(string tag, Exception? ex)
    {
        try
        {
            File.AppendAllText(
                CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // 记日志失败不再抛，避免掩盖原始异常
        }
    }
}
