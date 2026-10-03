// TapGestureGateTests.cs —— 「双击之后的第一个单击不许再登记打开」的防误开闸门（2026-10-03）
//
// 背景（用户实测反馈）："双击图标文字，会闪一下编辑，然后正常打开了"。
// `DeferredTextActivation` 只挡住了"计时器到点那一次"，挡不住**双击手势尾巴上的第二个 Tapped**：
// WinUI 的 `Tapped` 与 `DoubleTapped` 是两个独立事件，到达顺序不由我们决定，
// 于是存在"先 DoubleTapped（进改名）→ 后 Tapped（又登记打开）→ 一个间隔后真的打开"这条路。
//
// 这里钉住闸门的判据：**距上一次双击还在系统双击间隔之内 ⇒ 这一下不算单击**。

using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class TapGestureGateTests
{
    [Fact]
    public void 没有双击历史时_第一次单击放行()
    {
        var gate = new TapGestureGate();

        Assert.True(gate.NoteTextTap(nowMs: 10_000, doubleClickMs: 500));
    }

    [Fact]
    public void 双击之后的第一个单击被丢弃()
    {
        var gate = new TapGestureGate();
        gate.NoteDoubleTap(nowMs: 10_000);

        // 紧接着（双击手势的尾巴）—— 必须丢弃，否则程序会被误打开
        Assert.False(gate.NoteTextTap(nowMs: 10_000, doubleClickMs: 500));
        Assert.False(gate.NoteTextTap(nowMs: 10_100, doubleClickMs: 500));
        Assert.False(gate.NoteTextTap(nowMs: 10_500, doubleClickMs: 500));   // 正好在边界上：仍算双击手势内
    }

    [Fact]
    public void 超过双击间隔之后的单击照常放行()
    {
        var gate = new TapGestureGate();
        gate.NoteDoubleTap(nowMs: 10_000);

        Assert.True(gate.NoteTextTap(nowMs: 10_501, doubleClickMs: 500));
    }

    [Fact]
    public void 丢弃不会消耗闸门_窗口之内一直丢弃_窗口之外恢复()
    {
        var gate = new TapGestureGate();
        gate.NoteDoubleTap(nowMs: 1_000);

        Assert.False(gate.NoteTextTap(nowMs: 1_100, doubleClickMs: 500));
        Assert.False(gate.NoteTextTap(nowMs: 1_400, doubleClickMs: 500));
        Assert.True(gate.NoteTextTap(nowMs: 1_600, doubleClickMs: 500));
    }

    [Fact]
    public void 连续两次双击_以最后一次为准()
    {
        var gate = new TapGestureGate();
        gate.NoteDoubleTap(nowMs: 1_000);
        gate.NoteDoubleTap(nowMs: 3_000);

        // 距**后一次**双击仍在窗口内 ⇒ 丢弃（若错记成"第一次"，这里就会误放行）
        Assert.False(gate.NoteTextTap(nowMs: 3_200, doubleClickMs: 500));
        Assert.True(gate.NoteTextTap(nowMs: 3_600, doubleClickMs: 500));
    }

    [Fact]
    public void 系统双击间隔读不到时_不抑制()
    {
        // doubleClickMs <= 0（读系统值失败）⇒ 保守选择"不抑制"：
        // 宁可偶尔按单击处理，也不要让"点文字打开"彻底失灵。
        var gate = new TapGestureGate();
        gate.NoteDoubleTap(nowMs: 1_000);

        Assert.True(gate.NoteTextTap(nowMs: 1_000, doubleClickMs: 0));
        Assert.True(gate.NoteTextTap(nowMs: 1_000, doubleClickMs: -5));
    }

    [Fact]
    public void 时刻倒退时按双击手势仍在进行处理()
    {
        // ⚠️ 万一换用了会回绕/倒退的时间源，宁可少一次单击，也绝不能误开程序
        var gate = new TapGestureGate();
        gate.NoteDoubleTap(nowMs: 5_000);

        Assert.False(gate.NoteTextTap(nowMs: 4_000, doubleClickMs: 500));
    }

    [Fact]
    public void 清空之后恢复放行()
    {
        var gate = new TapGestureGate();
        gate.NoteDoubleTap(nowMs: 1_000);

        gate.Clear();

        Assert.True(gate.NoteTextTap(nowMs: 1_000, doubleClickMs: 500));
    }
}
