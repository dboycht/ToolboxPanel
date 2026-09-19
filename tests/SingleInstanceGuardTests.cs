// SingleInstanceGuardTests.cs —— W2：单实例守卫
//
// ⚠️ 只碰**自己造的**内核对象与假句柄，绝不去激活/移动用户真实窗口（ERROR.md E5 纪律）。
// 每个测试用独立的 key，避免并行执行时互相串。

using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class SingleInstanceGuardTests
{
    private static string NewKey() => $"ToolboxPanel_Test_{Guid.NewGuid():N}";

    [Fact]
    public void 首个实例成为主实例()
    {
        using var guard = new SingleInstanceGuard(NewKey());

        Assert.True(guard.TryAcquire());
        Assert.True(guard.IsPrimary);
    }

    [Fact]
    public void 第二个实例不是主实例()
    {
        var key = NewKey();
        using var first = new SingleInstanceGuard(key);
        Assert.True(first.TryAcquire());

        using var second = new SingleInstanceGuard(key);

        Assert.False(second.TryAcquire());
        Assert.False(second.IsPrimary);
    }

    [Fact]
    public void 窗口句柄可以发布与读回()
    {
        var key = NewKey();
        using var primary = new SingleInstanceGuard(key);
        Assert.True(primary.TryAcquire());

        var fakeHandle = new IntPtr(0x1234);
        primary.PublishWindowHandle(fakeHandle);

        using var secondary = new SingleInstanceGuard(key);
        Assert.False(secondary.TryAcquire());
        Assert.Equal(fakeHandle, secondary.TryReadWindowHandle());
    }

    [Fact]
    public void 没有主实例时读不到窗口句柄()
    {
        using var guard = new SingleInstanceGuard(NewKey());

        Assert.Equal(IntPtr.Zero, guard.TryReadWindowHandle());
    }

    [Fact]
    public void 发布零句柄等于没发布()
    {
        var key = NewKey();
        using var primary = new SingleInstanceGuard(key);
        primary.TryAcquire();

        primary.PublishWindowHandle(IntPtr.Zero);

        Assert.Equal(IntPtr.Zero, primary.TryReadWindowHandle());
    }

    [Fact]
    public void 激活无效句柄_不抛异常且不会误动用户窗口()
    {
        var key = NewKey();
        using var primary = new SingleInstanceGuard(key);
        primary.TryAcquire();
        primary.PublishWindowHandle(new IntPtr(0x7FFF0001));   // 刻意不是任何真实窗口

        using var secondary = new SingleInstanceGuard(key);
        Assert.False(secondary.TryAcquire());

        // ⚠️ 返回值的语义是「**通知**有没有发出去」，不是「窗口有没有真的升起」——
        //    窗口那半边由主实例自己在 UI 线程上做（见 SingleInstanceGuard 的文件头）。
        //    所以句柄是假的时候这里仍然返回 true：事件通知确实送达了。
        var signaled = secondary.ActivateExisting();
        Assert.True(signaled);

        // 但"直接操作用户窗口"这条兜底路径必须老实失败（不许抛，也不许真的去动）
        Assert.False(SingleInstanceGuard.RestoreAndActivate(new IntPtr(0x7FFF0001)));
    }

    [Fact]
    public void 主实例释放后_可以重新接管()
    {
        var key = NewKey();

        using (var first = new SingleInstanceGuard(key))
        {
            Assert.True(first.TryAcquire());
        }

        using var second = new SingleInstanceGuard(key);
        Assert.True(second.TryAcquire());     // 内核对象随最后一个句柄关闭而销毁 → 无残留锁
    }

    [Fact]
    public void 非主实例发布句柄会被忽略()
    {
        var key = NewKey();
        using var primary = new SingleInstanceGuard(key);
        primary.TryAcquire();
        primary.PublishWindowHandle(new IntPtr(0x1111));

        using var secondary = new SingleInstanceGuard(key);
        secondary.TryAcquire();
        secondary.PublishWindowHandle(new IntPtr(0x2222));    // 不该覆盖主实例的值

        Assert.Equal(new IntPtr(0x1111), secondary.TryReadWindowHandle());
    }

    [Fact]
    public void 默认key与原版一致()
    {
        Assert.Equal("ToolboxPanel_SingleInstance_v1", SingleInstanceGuard.DefaultKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void key为空_抛参数异常(string key)
    {
        Assert.Throws<ArgumentException>(() => new SingleInstanceGuard(key));
    }

    [Fact]
    public void 释放后再用会抛ObjectDisposed()
    {
        var guard = new SingleInstanceGuard(NewKey());
        guard.TryAcquire();
        guard.Dispose();

        Assert.Throws<ObjectDisposedException>(() => guard.TryAcquire());
    }

    [Fact]
    public void 重复TryAcquire是幂等的_不会把主实例状态改坏()
    {
        // ⚠️ 曾经的缺陷：第二次 `new Mutex(同名)` 拿到 createdNew=false ⇒ 把 IsPrimary 改成 false，
        //    但互斥体句柄还握着（状态自相矛盾：IsPrimary==false 却仍占着锁）。
        using var guard = new SingleInstanceGuard(NewKey());

        Assert.True(guard.TryAcquire());
        Assert.True(guard.TryAcquire());          // 第二次也返回 true（幂等）
        Assert.True(guard.IsPrimary);             // 且仍然认为自己持有
    }

    [Fact]
    public void 重复TryAcquire后_别的实例依然进不来()
    {
        var key = NewKey();
        using var first = new SingleInstanceGuard(key);
        first.TryAcquire();
        first.TryAcquire();     // 幂等，不该把锁丢掉

        using var second = new SingleInstanceGuard(key);
        Assert.False(second.TryAcquire());
    }

    // ────────────────────────────── 拉取已有窗口的"通知"链路 ──────────────────────────────
    //
    // ⚠️ 这一族是 2026-09-19 补的：旧实现只有"第二实例自己去 SetForegroundWindow"，
    //    而 Windows 的前台锁会让它**静默失败** ⇒ 第二实例退出了、已有窗口却没升起，
    //    用户看到的现象就是"又跑了一次、界面没反应"。现在第二实例改成 SetEvent 通知，
    //    由主实例自己在 UI 线程上恢复 + 前置（Core 只负责"通知送达"这一半）。

    [Fact]
    public void 第二实例的通知_主实例能等到()
    {
        var key = NewKey();
        using var primary = new SingleInstanceGuard(key);
        Assert.True(primary.TryAcquire());

        using var secondary = new SingleInstanceGuard(key);
        Assert.False(secondary.TryAcquire());

        // 第二实例发通知
        secondary.ActivateExisting();

        // 主实例侧应当立刻等到（用取消令牌兜住"等不到"的情况，避免测试挂死）
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(primary.WaitForActivationRequest(cts.Token));
    }

    [Fact]
    public void 取消后_主实例的等待会返回false而不是挂住()
    {
        var key = NewKey();
        using var primary = new SingleInstanceGuard(key);
        Assert.True(primary.TryAcquire());

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        // 没人发通知 + 令牌被取消 ⇒ 必须在分片超时后返回 false（不能永远阻塞）
        Assert.False(primary.WaitForActivationRequest(cts.Token));
    }

    [Fact]
    public void 主实例释放后_新实例可以接管且拿到新的通知事件()
    {
        var key = NewKey();

        using (var first = new SingleInstanceGuard(key))
        {
            Assert.True(first.TryAcquire());
            first.Dispose();
        }

        // 旧主实例释放 ⇒ 内核对象销毁 ⇒ 新进程可以当主实例（原版同款语义：无残留锁）
        using var second = new SingleInstanceGuard(key);
        Assert.True(second.TryAcquire());

        // 而且它自己的通知链路也要能用（事件是随新主实例一起建的）
        using var third = new SingleInstanceGuard(key);
        Assert.False(third.TryAcquire());
        third.ActivateExisting();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(second.WaitForActivationRequest(cts.Token));
    }

    [Fact]
    public void 恢复并激活_空句柄返回false且不抛异常()
    {
        // 只碰自己造的假句柄，绝不去操作用户真实窗口（纪律，见文件头）
        Assert.False(SingleInstanceGuard.RestoreAndActivate(IntPtr.Zero));
    }

    [Fact]
    public void 恢复并激活_伪句柄不抛异常()
    {
        // 0x7FFF0001 刻意不是任何真实窗口：IsIconic/SetForegroundWindow 会老实失败
        Assert.False(SingleInstanceGuard.RestoreAndActivate(new IntPtr(0x7FFF0001)));
    }
}
