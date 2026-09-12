// App.xaml.cs —— ToolboxPanel v2 · WinUI 3 主线入口
//
// 职责：
//   1) 单实例守卫（Core 的 SingleInstanceGuard）：第二个实例把已有窗口拉到前台后退出
//      —— 因为 data/tabs.json 是单文件即时保存，多开会互相覆盖（原版 v1.11.6 的理由，依然成立）；
//   2) 异常兜底：任何未处理异常都落一份日志（unpackaged 调试没有 IDE 输出窗）；
//   3) 创建并激活 MainWindow，然后把窗口句柄发布给后续实例。
//
// ⚠️ unpackaged 模式下不需要手动 Bootstrap：csproj 里 WindowsPackageType=None +
//    WindowsAppSDKSelfContained=true 时，WindowsAppSDK 的 NuGet 会注入自动引导代码。

using System;
using System.IO;
using Microsoft.UI.Xaml;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel;

public partial class App : Application
{
    /// <summary>异常日志路径（%TEMP%\toolboxpanel-crash.log）；也供外部验证脚本读取。</summary>
    internal static string CrashLogPath { get; } =
        Path.Combine(Path.GetTempPath(), "toolboxpanel-crash.log");

    /// <summary>开发/验证开关：带这个参数启动时不抢单实例（便于同时开两个对比材质）。</summary>
    private const string AllowMultiInstanceSwitch = "--allow-multi-instance";

    private SingleInstanceGuard? _instanceGuard;
    private Window? _window;

    public App()
    {
        InitializeComponent();

        UnhandledException += (_, e) => WriteCrash("XamlUnhandledException", e.Exception);
        AppDomain.CurrentDomain.UnhandledException +=
            (_, e) => WriteCrash("AppDomainUnhandledException", e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException +=
            (_, e) => WriteCrash("UnobservedTaskException", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!AcquireSingleInstance())
        {
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Activate();

        PublishWindowHandle();
    }

    /// <summary>返回 true 表示本进程可以继续（是主实例，或被显式允许并存）。</summary>
    private bool AcquireSingleInstance()
    {
        if (Environment.GetCommandLineArgs().AsSpan().IndexOf(AllowMultiInstanceSwitch) >= 0)
        {
            return true;
        }

        try
        {
            _instanceGuard = new SingleInstanceGuard();
            if (_instanceGuard.TryAcquire())
            {
                return true;
            }

            // 已有实例：把它的窗口拉到前台，然后本进程静默退出
            _instanceGuard.ActivateExisting();
            return false;
        }
        catch (Exception ex)
        {
            // 守卫本身出问题不该拦着用户使用
            WriteCrash("AcquireSingleInstance", ex);
            return true;
        }
    }

    private void PublishWindowHandle()
    {
        if (_instanceGuard is null || _window is null || !_instanceGuard.IsPrimary)
        {
            return;
        }

        try
        {
            // WinUI 3 取 HWND 的官方方式
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            _instanceGuard.PublishWindowHandle(hwnd);
        }
        catch (Exception ex)
        {
            WriteCrash("PublishWindowHandle", ex);
        }
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
