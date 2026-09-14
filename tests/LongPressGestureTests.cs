// LongPressGestureTests.cs —— 「长按起拖」判定（W5，手机桌面式交互）
//
// 判定只有一条：**按住够久 ⇒ 起拖**（不因移动取消、也没有"原地松手弹菜单"那套）。
// 时间戳由测试给定 ⇒ 完全确定，不依赖真实计时器。

using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class LongPressGestureTests
{
    private const int Threshold = 350;

    private static LongPressGesture Pressed(long at = 1000)
    {
        var gesture = new LongPressGesture(Threshold);
        gesture.Press(at);
        return gesture;
    }

    [Fact]
    public void 按住到阈值_起拖_且只触发一次()
    {
        var gesture = Pressed();

        Assert.True(gesture.Tick(1000 + Threshold));
        Assert.True(gesture.HasDragStarted);
        Assert.False(gesture.Tick(1000 + Threshold + 500));   // 幂等：不会再触发
    }

    [Fact]
    public void 定时器早到_不起拖_但会告诉调用方还差多久()
    {
        // ⚠️ 这是上一版长按"偶发完全没反应"的根因：Windows 定时器会早到，
        //    早到就 Tick=false，若调用方不按 RemainingMs 重排，这次长按就被丢掉了。
        var gesture = Pressed();

        Assert.False(gesture.Tick(1000 + Threshold - 40));
        Assert.False(gesture.HasDragStarted);
        Assert.Equal(40, gesture.RemainingMs(1000 + Threshold - 40));

        // 按剩余时间补一次 ⇒ 正常起拖
        Assert.True(gesture.Tick(1000 + Threshold));
    }

    [Fact]
    public void 阈值前松手_不起拖_当成普通点击()
    {
        var gesture = Pressed(at: 1000);

        gesture.Release();

        Assert.False(gesture.IsPressed);
        Assert.False(gesture.HasDragStarted);
        Assert.False(gesture.Tick(1000 + Threshold));
    }

    [Fact]
    public void 按住期间移动不影响起拖_没有移动取消这套判定()
    {
        // 用户明确要求去掉"移动就取消"：这里根本没有 Move 这种输入，
        // 只要按着够久就该起拖（时间到了就是到了）。
        var gesture = Pressed();
        gesture.Release();
        gesture.Press(2000);

        Assert.True(gesture.Tick(2000 + Threshold));
    }

    [Fact]
    public void 未按下时Tick与Remaining都是空操作()
    {
        var gesture = new LongPressGesture(Threshold);

        Assert.False(gesture.Tick(9999));
        Assert.Equal(0, gesture.RemainingMs(9999));
    }

    [Fact]
    public void 起拖后松手_HasDragStarted保留到下次按下()
    {
        var gesture = Pressed();
        gesture.Tick(1000 + Threshold);

        gesture.Release();

        Assert.False(gesture.IsPressed);
        Assert.True(gesture.HasDragStarted);   // 拖动收尾时还要读它

        gesture.Press(5000);
        Assert.False(gesture.HasDragStarted);  // 新手势重新开始
    }

    [Fact]
    public void Reset_清掉一切()
    {
        var gesture = Pressed();
        gesture.Tick(1000 + Threshold);

        gesture.Reset();

        Assert.False(gesture.IsPressed);
        Assert.False(gesture.HasDragStarted);
        Assert.Equal(0, gesture.RemainingMs(999999));
        Assert.False(gesture.Tick(999999));
    }

    [Fact]
    public void 正好到点算到_超过也不会返回负的剩余时间()
    {
        var gesture = Pressed(at: 0);

        Assert.Equal(0, gesture.RemainingMs(Threshold));
        Assert.Equal(0, gesture.RemainingMs(Threshold + 5000));
        Assert.True(gesture.Tick(Threshold));
    }

    [Fact]
    public void 可调阈值_例如500ms()
    {
        var gesture = new LongPressGesture(500);
        gesture.Press(0);

        Assert.Equal(1, gesture.RemainingMs(499));
        Assert.False(gesture.Tick(499));
        Assert.True(gesture.Tick(500));
    }

    [Fact]
    public void 重复按下_开一次新手势()
    {
        var gesture = Pressed();
        gesture.Tick(1000 + Threshold);
        Assert.True(gesture.HasDragStarted);

        gesture.Press(9000);

        Assert.True(gesture.IsPressed);
        Assert.False(gesture.HasDragStarted);
        Assert.Equal(Threshold, gesture.RemainingMs(9000));
    }
}
