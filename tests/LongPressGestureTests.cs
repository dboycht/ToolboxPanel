// LongPressGestureTests.cs —— 「长按起拖」手势判定（W5，手机桌面式交互）
//
// 纯时间/距离判定，时间戳由测试给定 ⇒ 完全确定，不依赖真实计时器。

using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class LongPressGestureTests
{
    private const int Threshold = 350;

    private static LongPressGesture Pressed(double x = 0, double y = 0, long at = 1000)
    {
        var gesture = new LongPressGesture(Threshold);
        gesture.Press(x, y, at);
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
    public void 定时器早到_不起拖_再等一会儿才起拖()
    {
        var gesture = Pressed();

        Assert.False(gesture.Tick(1000 + Threshold - 40));    // 计时器抖动：早到了
        Assert.False(gesture.HasDragStarted);
        Assert.True(gesture.Tick(1000 + Threshold));          // 真正到点才算
    }

    [Fact]
    public void 阈值前松手_什么都不做_交给普通点击()
    {
        var gesture = Pressed(at: 1000);

        Assert.Equal(LongPressOutcome.None, gesture.Complete());
        Assert.False(gesture.HasDragStarted);
        Assert.False(gesture.IsPressed);
    }

    [Fact]
    public void 阈值前移动超容差_取消长按_之后不再起拖()
    {
        var gesture = Pressed();

        Assert.True(gesture.Move(20, 0));                     // 超出默认容差 8
        Assert.True(gesture.MovedBeyondTolerance);
        Assert.False(gesture.Tick(1000 + Threshold));         // 已取消 ⇒ 不起拖
        Assert.Equal(LongPressOutcome.None, gesture.Complete());
    }

    [Fact]
    public void 容差内的小抖动_不算取消()
    {
        var gesture = Pressed();

        Assert.False(gesture.Move(3, 2));                     // 距离 ~3.6 < 8
        Assert.False(gesture.MovedBeyondTolerance);
        Assert.True(gesture.Tick(1000 + Threshold));
    }

    [Fact]
    public void 容差判定用欧氏距离_斜向也算()
    {
        var gesture = Pressed();

        Assert.True(gesture.Move(7, 7));                      // 距离 ~9.9 > 8
    }

    [Fact]
    public void 起拖后再移动_是正常拖动_不算取消()
    {
        var gesture = Pressed();
        Assert.True(gesture.Tick(1000 + Threshold));

        Assert.False(gesture.Move(120, 40));                  // 拖动中移动：不取消
        Assert.True(gesture.MovedBeyondTolerance);
        Assert.Equal(LongPressOutcome.None, gesture.Complete());   // 移动过 ⇒ 不弹菜单
    }

    [Fact]
    public void 起拖后原地松手_要菜单()
    {
        var gesture = Pressed();
        Assert.True(gesture.Tick(1000 + Threshold));

        Assert.Equal(LongPressOutcome.MenuRequested, gesture.Complete());
    }

    [Fact]
    public void 起拖前移动过_手势作废_不弹菜单()
    {
        // 起拖前动过（超容差）⇒ 手势已作废，之后即使起拖也不可能（Tick 返回 false）
        var gesture = Pressed();
        gesture.Move(50, 0);
        Assert.False(gesture.Tick(2000));
        Assert.Equal(LongPressOutcome.None, gesture.Complete());
    }

    [Fact]
    public void 重复松手_第二次不再要菜单()
    {
        var gesture = Pressed();
        gesture.Tick(1000 + Threshold);

        Assert.Equal(LongPressOutcome.MenuRequested, gesture.Complete());
        Assert.Equal(LongPressOutcome.None, gesture.Complete());
    }

    [Fact]
    public void 没有按下就移动或松手_都是空操作()
    {
        var gesture = new LongPressGesture(Threshold);

        Assert.False(gesture.Move(100, 100));
        Assert.False(gesture.Tick(9999));
        Assert.Equal(LongPressOutcome.None, gesture.Complete());
    }

    [Fact]
    public void 再次按下_重置上一次手势的状态()
    {
        var gesture = Pressed();
        gesture.Tick(1000 + Threshold);
        Assert.True(gesture.HasDragStarted);

        gesture.Press(5, 5, 5000);

        Assert.True(gesture.IsPressed);
        Assert.False(gesture.HasDragStarted);
        Assert.False(gesture.MovedBeyondTolerance);
    }

    [Fact]
    public void Reset_清掉一切()
    {
        var gesture = Pressed();
        gesture.Tick(1000 + Threshold);
        gesture.Move(99, 99);

        gesture.Reset();

        Assert.False(gesture.IsPressed);
        Assert.False(gesture.HasDragStarted);
        Assert.False(gesture.MovedBeyondTolerance);
        Assert.False(gesture.Tick(999999));
    }

    [Fact]
    public void 可调阈值_例如500ms()
    {
        var gesture = new LongPressGesture(500);
        gesture.Press(0, 0, 0);

        Assert.False(gesture.Tick(499));
        Assert.True(gesture.Tick(500));
    }
}
