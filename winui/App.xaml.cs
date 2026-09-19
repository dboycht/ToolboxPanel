// App.xaml.cs —— ToolboxPanel v2 · WinUI 3 主线入口
//
// 职责：
//   1) 单实例守卫（Core 的 SingleInstanceGuard）：第二个实例**通知**已有实例把窗口拉到前台后退出
//      —— 因为 data/tabs.json 是单文件即时保存，多开会互相覆盖（原版 v1.11.6 的理由，依然成立）；
//   2) 异常兜底：任何未处理异常都落一份日志（unpackaged 调试没有 IDE 输出窗）；
//   3) 创建并激活 MainWindow，然后把窗口句柄发布给后续实例。

using System;
using System.IO;
using System.Threading;
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
    private CancellationTokenSource? _activationCancellation;

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

        // ⚠️ 把守卫钉住到进程结束：它握着命名 Mutex / 事件 / 内存映射三件内核句柄，
        //    "最后一个句柄关闭 ⇒ 内核对象销毁（下一个实例才能当主实例）"这条不变量靠它活着。
        GC.KeepAlive(_instanceGuard);
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
                // 主实例：起一个后台等待线程，收到"第二实例的请求"就把自己的窗口拉到前台
                StartActivationListener(_instanceGuard);
                return true;
            }

            // 已有实例：**通知它**把窗口拉到前台，然后本进程静默退出
            var signaled = _instanceGuard.ActivateExisting();
            ProbeLog($"第二实例：已请求激活已有窗口（信号={signaled}），本进程退出");
            return false;
        }
        catch (Exception ex)
        {
            // 守卫本身出问题不该拦着用户使用
            WriteCrash("AcquireSingleInstance", ex);
            return true;
        }
    }

    /// <summary>
    /// 主实例侧：后台线程等"有人请求激活"，收到就回 UI 线程把自己的窗口恢复并前置。
    ///
    /// <para>⚠️ 为什么是"主实例自己动手"，而不是让第二实例直接去 SetForegroundWindow：
    /// Windows 有**前台锁**（防焦点窃取），后台进程对别人窗口调那个 API 经常返回 false
    /// 且什么都不发生 —— 于是第二实例悄悄退出、已有窗口没升起，用户看到的就是"没反应"。
    /// 由窗口主人自己做最可靠，这也是各家单实例应用的标准做法。</para>
    /// </summary>
    private void StartActivationListener(SingleInstanceGuard guard)
    {
        _activationCancellation = new CancellationTokenSource();
        var token = _activationCancellation.Token;

        var thread = new Thread(() =>
        {
            while (!token.IsCancellationRequested)
            {
                // 等一次请求（内部按 500ms 分片，便于及时响应取消）
                if (!guard.WaitForActivationRequest(token))
                {
                    continue;
                }

                try
                {
                    // 回到 UI 线程做窗口操作 —— Core 不认识 WinUI，这一跳只能在这里做
                    var window = _window;
                    window?.DispatcherQueue.TryEnqueue(() => BringToFront(window));
                }
                catch (Exception ex)
                {
                    WriteCrash("ActivationListener", ex);
                }
            }
        })
        {
            IsBackground = true,        // 不阻止进程退出
            Name = "ToolboxPanel.ActivationListener",
        };

        thread.Start();
    }

    /// <summary>把主窗口恢复并提到前台（**必须在 UI 线程上调用**）。</summary>
    private void BringToFront(Window window)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            // ① 最小化则先恢复 + 前置（原版就是 ShowWindow(SW_RESTORE) + SetForegroundWindow）
            var ok = SingleInstanceGuard.RestoreAndActivate(hwnd);

            // ② WinUI 侧再激活一次，保证键盘焦点也跟着回来
            window.Activate();

            ProbeLog($"已激活已有窗口（hwnd=0x{hwnd.ToInt64():X}，SetForegroundWindow={ok}）");
        }
        catch (Exception ex)
        {
            WriteCrash("BringToFront", ex);
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

    /// <summary>
    /// 窗口关闭时收口：停掉激活监听线程、释放三件内核句柄。
    ///
    /// <para>不调用它也不会崩（线程是后台线程、句柄随进程退出由内核回收），
    /// 但显式收口能让"主实例退出 ⇒ 命名对象销毁 ⇒ 下一个实例可以正常当主实例"这条链路
    /// 在任何退出路径上都确定成立（也是单实例守卫的验收点之一）。</para>
    /// </summary>
    internal void Shutdown()
    {
        try
        {
            _activationCancellation?.Cancel();
            _activationCancellation?.Dispose();
            _activationCancellation = null;
        }
        catch (Exception ex)
        {
            WriteCrash("Shutdown/activation", ex);
        }

        try
        {
            _instanceGuard?.Dispose();
            _instanceGuard = null;
        }
        catch (Exception ex)
        {
            WriteCrash("Shutdown/guard", ex);
        }
    }

    /// <summary>
    /// 诊断通道（%TEMP%\toolboxpanel-probe.log）：给"看不见的观感问题"留证据。
    ///
    /// <para>两个用途：① 开发期探针（定位完随探针一起删）；② **常驻的拖动链路日志**
    /// （见 GridPage/ListViewPage 的 DragTrace）—— 拖放手感只能由用户手动试，
    /// 有了链路日志，用户试一次就能定位"哪一步断了"。</para>
    /// </summary>
    internal static void ProbeLog(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "toolboxpanel-probe.log"),
                message + Environment.NewLine);
        }
        catch
        {
            // 探针写不进去不影响程序
        }
    }
}
