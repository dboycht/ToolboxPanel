// SingleInstanceGuard.cs —— ToolboxPanel v2 · W2 系统能力
//
// 对应原 Python 的 src/toolbox/single_instance.py（单实例守卫），语义逐条对齐。
//
// 为什么必须单实例（原版注释里的背景，依然成立）：
//   `data/tabs.json` 是**单文件即时保存**，两个实例并发保存会互相覆盖
//   （A 存 B 的状态、B 又存 C 的状态）。
//
// ────────────────────────── 三件内核对象（原版是两件） ──────────────────────────
//   ① 命名 **Mutex**          —— "存在即说明有实例在跑"（原版是 QSharedMemory 的同一用法）
//   ② 命名 **内存映射槽**      —— 主实例把自己的 HWND 写进去（原版完全相同的设计）
//   ③ 命名 **事件**           —— 🆕 第二实例用它**通知**主实例"有人想进来"
//   三件都由内核管理生命周期：进程退出/崩溃自动释放，**不会残留锁**。
//
// ────────────────────────── 为什么必须加 ③（原版没有的那一环） ──────────────────────────
// 旧实现只做了"第二实例自己调 `SetForegroundWindow(主实例 HWND)`"。实测问题：
//   · Windows 有**前台锁**（防焦点窃取）：后台进程对别人窗口调 SetForegroundWindow
//     经常**返回 false 且什么都不发生**；
//   · 于是第二实例悄悄退出、已有窗口**没有升起** —— 用户看到的现象就是
//     "又跑了一次、界面上没反应"，与"多开了实例"难以区分；
//   · 而且窗口最小化时，只有主实例自己才知道"该恢复 + 重新激活"。
//
// 现在的做法（也就是各家单实例应用的标准做法）：
//   第二实例：`AllowSetForegroundWindow(-1)` 把前台权限让出去 → `SetEvent` 通知主实例 → 静默退出；
//   主实例：后台线程等到通知 → 回到 UI 线程 → 自己 `IsIconic`/`ShowWindow(SW_RESTORE)` +
//           `SetForegroundWindow` + `BringWindowToTop`。
//   由**正在运行的窗口自己**完成"恢复 + 前置"，比跨进程去操作别人窗口可靠得多。
//
// ⚠️ 与原版**不互通**：原版是 Qt 的 QSharedMemory，这里是内核对象，名字也不一样。
//    也就是说「Python 版 + C# 版」可以各跑一个 —— 迁移期无妨（成型后 Python 线不再使用）。

using System.IO.MemoryMappedFiles;
using static ToolboxPanel.Core.Services.ShellApi;

namespace ToolboxPanel.Core.Services;

/// <summary>单实例守卫：保证同时只有一个实例在跑；新实例通知已有实例把窗口拉到前台后退出。</summary>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>与原版一致的基名（便于日志/排查时对得上）。</summary>
    public const string DefaultKey = "ToolboxPanel_SingleInstance_v1";

    /// <summary>HWND 在 64 位下是 8 字节。</summary>
    private const int HandleSlotSize = sizeof(long);

    /// <summary>等待"有人请求激活"的线程在退出时用它收口（不是用户可见的延迟）。</summary>
    private static readonly TimeSpan WaitSlice = TimeSpan.FromMilliseconds(500);

    private readonly string _key;
    private Mutex? _existenceFlag;
    private MemoryMappedFile? _handleSlot;
    private EventWaitHandle? _activateSignal;
    private bool _disposed;

    public SingleInstanceGuard(string key = DefaultKey)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("key 不能为空", nameof(key));
        }

        _key = key;
    }

    /// <summary>本进程是不是主实例（<see cref="TryAcquire"/> 的结果）。</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>
    /// 尝试成为主实例。返回 true = 本进程是唯一实例。
    ///
    /// 与原版一致：拿不到就当「已有实例在跑」，**不阻塞等待**。
    /// </summary>
    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // ⚠️ 幂等：已经拿到主实例就**直接返回**，别再去建一次句柄。
        //    否则第二次调用会 `new Mutex(同名)` 拿到 createdNew=false ⇒ 把 IsPrimary 改成 false，
        //    但 `_existenceFlag` 还握着 —— 状态自相矛盾（`IsPrimary==false` 却仍占着互斥体）。
        if (IsPrimary)
        {
            return true;
        }

        Mutex? candidate;
        bool createdNew;
        try
        {
            // createdNew = true 表示这个命名对象是我们创建出来的 → 之前没有实例
            candidate = new Mutex(initiallyOwned: false, name: $"{_key}_Mutex", out createdNew);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // 极端情况（权限/内核对象异常）：保守地认为已有实例，避免多开写坏数据
            IsPrimary = false;
            return false;
        }

        if (!createdNew)
        {
            // ⚠️ 「已有实例」这条路**也必须释放刚建出来的句柄**：
            //    `new Mutex(...)` 每次都会开一个句柄，不 Dispose 就一直挂着
            //    （虽然本进程随后就退出、由内核回收，但同一进程里反复 TryAcquire 会持续泄漏）。
            candidate.Dispose();

            // 兜一层：万一 `_existenceFlag` 里还留着上一轮拿到的句柄（同一实例被复用时），
            // 一并释放 —— 让「IsPrimary=false ⇒ 不持有任何句柄」这个不变量永远成立。
            _existenceFlag?.Dispose();
            _existenceFlag = null;

            IsPrimary = false;
            return false;
        }

        _existenceFlag = candidate;

        try
        {
            _handleSlot = MemoryMappedFile.CreateOrOpen($"{_key}_Hwnd", HandleSlotSize);

            // 主实例创建（或打开）"请求激活"事件；第二实例每次都会现开同名事件并 Set。
            _activateSignal = new EventWaitHandle(
                initialState: false, EventResetMode.AutoReset, $"{_key}_Activate");

            // ⚠️ CreateOrOpen 语义下有可能**复用一个还处于"已触发"的内核对象**
            //    （例如同一进程内反复创建守卫、或上一轮退出时序来不及销毁）。
            //    不先归零的话，主实例窗口刚起来就会"凭空被激活一次"——外观上莫名其妙。
            _activateSignal.Reset();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // 共享设施建不出来（权限/内核对象耗尽）：**绝不放行成第二个主实例** ——
            // 多开写坏 tabs.json 的代价比"这次没启动"大得多。
            // 按不变量把状态与句柄都收回（IsPrimary 保持 false，调用方会静默退出）。
            _activateSignal?.Dispose();
            _activateSignal = null;
            _handleSlot?.Dispose();
            _handleSlot = null;
            _existenceFlag?.Dispose();
            _existenceFlag = null;

            IsPrimary = false;
            return false;
        }

        IsPrimary = true;
        return true;
    }

    /// <summary>主实例在窗口真正创建之后调用：把自己的窗口句柄写进共享槽。</summary>
    public void PublishWindowHandle(IntPtr hwnd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsPrimary || _handleSlot is null || hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            using var accessor = _handleSlot.CreateViewAccessor(0, HandleSlotSize);
            accessor.Write(0, hwnd.ToInt64());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // 写不进去只影响"第二实例自报窗口位置"这条诊断路径，不影响单实例本身
        }
    }

    /// <summary>读出主实例发布的窗口句柄；没有则返回 <see cref="IntPtr.Zero"/>。</summary>
    public IntPtr TryReadWindowHandle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            // ⚠️ OpenExisting 的第 2 个参数是 MemoryMappedFileRights；
            //    CreateViewAccessor 的第 3 个参数才是 MemoryMappedFileAccess —— 别写反。
            using var slot = MemoryMappedFile.OpenExisting($"{_key}_Hwnd", MemoryMappedFileRights.Read);
            using var accessor = slot.CreateViewAccessor(0, HandleSlotSize, MemoryMappedFileAccess.Read);
            long value = accessor.ReadInt64(0);
            return value == 0 ? IntPtr.Zero : new IntPtr(value);
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// 新实例调用：**通知**已有实例把窗口拉到前台（它也负责"最小化则先恢复"）。
    ///
    /// <para>步骤与理由：</para>
    /// <list type="number">
    /// <item>先把前台权限让出去（<c>AllowSetForegroundWindow(-1)</c>）——
    /// 这是绕开 Windows 前台锁的**正规**做法，不然主实例那边调 <c>SetForegroundWindow</c> 会被静默拒绝；</item>
    /// <item>`SetEvent` 通知主实例，由它自己在 UI 线程上完成"恢复 + 前置"（主路径）；</item>
    /// <item>顺手也直接对已发布的 HWND 做一次恢复 + 前置：主实例**还活着但没人监听**时
    /// （例如更老的版本、或监听线程尚未起来）这一下仍能把窗口拉起来，属于兜底。</item>
    /// </list>
    /// </summary>
    /// <returns>通知是否发出去了（false 只用于日志，不改变"本进程要不要退"的决定）。</returns>
    public bool ActivateExisting()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool signaled = false;

        try
        {
            AllowSetForegroundWindow(-1);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // 老系统上没这个入口：继续走后面的兜底
        }

        try
        {
            // 第二实例**自己不持有**这个事件（只有主实例建它），所以这里必然要现开一个：
            // ⚠️ 这正是旧实现漏掉的一环 —— 只 Set 自己的字段（永远是 null）等于什么都没做。
            using var signal = EventWaitHandle.OpenExisting($"{_key}_Activate");
            signaled = signal.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException
                                      or IOException or ObjectDisposedException)
        {
            signaled = false;
        }

        // 兜底：直接操作一次已发布的窗口（主实例没在监听时也有机会起来）
        var hwnd = TryReadWindowHandle();
        if (hwnd != IntPtr.Zero && IsWindow(hwnd))
        {
            signaled |= RestoreAndActivate(hwnd);
        }

        return signaled;
    }

    /// <summary>
    /// 把某个窗口从"最小化"恢复并提到前台。**必须在拥有该窗口的进程的 UI 线程上调用**。
    /// </summary>
    /// <returns>是否成功把它设为前台窗口（失败只说明焦点没抢到，界面状态仍已恢复）。</returns>
    public static bool RestoreAndActivate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);   // 原版同款：最小化时先恢复
            }

            var ok = SetForegroundWindow(hwnd);
            BringWindowToTop(hwnd);             // 再抬一次层级（有些层级顺序下 SetForegroundWindow 只改焦点不改 Z 序）
            return ok;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// 主实例调用：等"有人请求激活"。返回 true = 收到一次请求。
    ///
    /// <para>⚠️ 必须在**后台线程**上循环调用（它内部是一次带超时的等待，会阻塞）。
    /// 收到之后由调用方把动作丢回 UI 线程执行 —— Core 不认识 WinUI。</para>
    /// </summary>
    /// <param name="cancellation">窗口关闭时置位，让等待线程退出（否则线程会一直等下去）。</param>
    public bool WaitForActivationRequest(CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_activateSignal is null)
        {
            return false;
        }

        while (!cancellation.IsCancellationRequested)
        {
            bool got;
            try
            {
                // 分片等待：既能及时响应取消，又不必每次重新开句柄
                got = _activateSignal.WaitOne(WaitSlice);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or AbandonedMutexException)
            {
                return false;
            }

            if (got)
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activateSignal?.Dispose();
        _activateSignal = null;
        _handleSlot?.Dispose();
        _handleSlot = null;
        _existenceFlag?.Dispose();     // 最后一个句柄关闭时，内核对象随之销毁
        _existenceFlag = null;
        IsPrimary = false;
    }
}
