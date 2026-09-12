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
    public void 激活无效句柄_返回false且不抛异常()
    {
        var key = NewKey();
        using var primary = new SingleInstanceGuard(key);
        primary.TryAcquire();
        primary.PublishWindowHandle(new IntPtr(0x7FFF0001));   // 刻意不是任何真实窗口

        using var secondary = new SingleInstanceGuard(key);
        Assert.False(secondary.TryAcquire());

        Assert.False(secondary.ActivateExisting());   // 不会误动用户窗口
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
}
