// SingleInstanceGuard.cs —— ToolboxPanel v2 · W2 系统能力
//
// 对应原 Python 的 src/toolbox/single_instance.py（单实例守卫）。
//
// 为什么必须单实例（原版注释里的背景，依然成立）：
//   `data/tabs.json` 是**单文件即时保存**，两个实例并发保存会互相覆盖
//   （A 存 B 的状态、B 又存 C 的状态）。
//
// 实现映射：
//   原版 QSharedMemory(key) 的「存在即互斥」+ 里面放 HWND
//     →  命名 Mutex（存在即说明有实例在跑）+ 命名内存映射（承载 8 字节 HWND）
//   两者都由内核管理生命周期：进程退出/崩溃自动释放，**不会残留锁**。
//
// ⚠️ 与原版**不互通**：原版是 Qt 的 QSharedMemory，这里是内核对象，名字也不一样。
//    也就是说「Python 版 + C# 版」可以各跑一个 —— 迁移期无妨（成型后 Python 线不再使用）。

using System.IO.MemoryMappedFiles;
using static ToolboxPanel.Core.Services.ShellApi;

namespace ToolboxPanel.Core.Services;

/// <summary>单实例守卫：保证同时只有一个实例在跑；新实例把已有窗口拉到前台后退出。</summary>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>与原版一致的基名（便于日志/排查时对得上）。</summary>
    public const string DefaultKey = "ToolboxPanel_SingleInstance_v1";

    private const int HandleSlotSize = sizeof(long);   // HWND 在 64 位下是 8 字节

    private readonly string _key;
    private Mutex? _existenceFlag;
    private MemoryMappedFile? _handleSlot;
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

        _handleSlot = MemoryMappedFile.CreateOrOpen($"{_key}_Hwnd", HandleSlotSize);
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

        using var accessor = _handleSlot.CreateViewAccessor(0, HandleSlotSize);
        accessor.Write(0, hwnd.ToInt64());
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
    /// 新实例调用：把已有实例的窗口带到前台（最小化时先恢复）。
    /// 返回 false 表示「没找到窗口 / 拉不起来」，调用方据此决定是否静默退出。
    /// </summary>
    public bool ActivateExisting()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var hwnd = TryReadWindowHandle();
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
        {
            return false;
        }

        try
        {
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
            }

            return SetForegroundWindow(hwnd);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handleSlot?.Dispose();
        _handleSlot = null;
        _existenceFlag?.Dispose();     // 最后一个句柄关闭时，内核对象随之销毁
        _existenceFlag = null;
        IsPrimary = false;
    }
}
