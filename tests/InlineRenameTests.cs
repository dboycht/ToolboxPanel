// InlineRenameTests.cs —— 「双击文字就地改名」的两块纯逻辑（2026-10-02）
//
// ① `InlineRename.Decide`：输入框里交上来的字 → Revert / Unchanged / Commit
//    基准 = 旧版 v1.11.6 `IconLabel._finish_edit`（空名丢弃、同名不动、其余才写库）
// ② `DeferredTextActivation`：点文字延后打开 / 双击取消的时序（最容易写错的一段）
//
// ⚠️ 这一族**不碰 WinUI**：计时器、系统双击间隔、焦点都留给界面层，
//    这样"点了 A 又点 B，旧计时器回调把 B 打开了"这类 bug 能在单测里被抓到。

using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class InlineRenameTests
{
    // ────────────────────────────── ① 提交决定 ──────────────────────────────

    [Fact]
    public void 输入框为空_回退原值且不落库()
    {
        var decision = InlineRename.Decide("记事本", "");

        Assert.Equal(InlineRenameOutcome.Revert, decision.Outcome);
        Assert.Equal("记事本", decision.Name);   // 调用方拿它回填显示
    }

    [Fact]
    public void 输入框只有空白_同样回退原值()
    {
        // 旧版是 `new_text.strip()` 之后才判空 ⇒ 只打空格也算"没改"
        var decision = InlineRename.Decide("记事本", "   \t ");

        Assert.Equal(InlineRenameOutcome.Revert, decision.Outcome);
        Assert.Equal("记事本", decision.Name);
    }

    [Fact]
    public void 原名和输入都是空_回退成空串()
    {
        var decision = InlineRename.Decide("", "   ");

        Assert.Equal(InlineRenameOutcome.Revert, decision.Outcome);
        Assert.Equal("", decision.Name);
    }

    [Fact]
    public void 名字没变_什么都不做()
    {
        var decision = InlineRename.Decide("常用工具", "常用工具");

        Assert.Equal(InlineRenameOutcome.Unchanged, decision.Outcome);
    }

    [Fact]
    public void 只是两端多了空白_算没变_不落库()
    {
        // 两边都 Trim 之后再比 ⇒ 避免"只补了个空格也算改名"这种脏写入
        var decision = InlineRename.Decide("常用工具", "  常用工具  ");

        Assert.Equal(InlineRenameOutcome.Unchanged, decision.Outcome);
    }

    [Fact]
    public void 原名两端本来就有空白_输入与去空白后相同_也算没变()
    {
        // tabs.json 里可能存着带空白的旧名字（手改 / 旧版本写坏），这里口径必须与输入侧一致
        var decision = InlineRename.Decide("  常用工具 ", "常用工具");

        Assert.Equal(InlineRenameOutcome.Unchanged, decision.Outcome);
    }

    [Fact]
    public void 真的改了_提交去掉两端空白的名字()
    {
        var decision = InlineRename.Decide("记事本", "  我的记事本  ");

        Assert.Equal(InlineRenameOutcome.Commit, decision.Outcome);
        Assert.Equal("我的记事本", decision.Name);
    }

    [Fact]
    public void 只改大小写_算改了()
    {
        // 用序数比较（不做 culture / 大小写折叠）：用户看到的就是他打的字
        var decision = InlineRename.Decide("notepad", "NotePad");

        Assert.Equal(InlineRenameOutcome.Commit, decision.Outcome);
        Assert.Equal("NotePad", decision.Name);
    }

    [Fact]
    public void 原本没名字_现在填了_算改了()
    {
        var decision = InlineRename.Decide(null, "新名字");

        Assert.Equal(InlineRenameOutcome.Commit, decision.Outcome);
        Assert.Equal("新名字", decision.Name);
    }

    // ────────────────────────────── ② 双击间隔 ──────────────────────────────

    [Theory]
    [InlineData(400, 400)]   // 系统默认值附近：原样使用
    [InlineData(250, 250)]   // 下界
    [InlineData(500, 500)]   // 上界
    [InlineData(100, InlineRename.MinDelayMs)]   // 系统调得太快 ⇒ 抬到下限（否则双击还没来就打开了）
    [InlineData(900, InlineRename.MaxDelayMs)]   // 系统调得太慢 ⇒ 压到上限（否则单击文字明显发木）
    public void 延时间隔_夹在上下限之间(int systemMs, int expected)
    {
        Assert.Equal(expected, InlineRename.DelayFromSystemDoubleClickTime(systemMs));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 读不到系统值时用兜底值(int systemMs)
    {
        Assert.Equal(InlineRename.FallbackDelayMs, InlineRename.DelayFromSystemDoubleClickTime(systemMs));
    }

    // ────────────────────────────── ③ 延后打开的状态机 ──────────────────────────────

    [Fact]
    public void 点一下文字_登记待定项()
    {
        var gate = new DeferredTextActivation();

        Assert.True(gate.NoteTextTap("icon-a"));
        Assert.True(gate.HasPending);
        Assert.Equal("icon-a", gate.PendingId);
    }

    [Fact]
    public void 计时器到点_取走待定项并放行()
    {
        var gate = new DeferredTextActivation();
        gate.NoteTextTap("icon-a");

        Assert.True(gate.TryTakePending("icon-a"));   // 该打开了
        Assert.False(gate.HasPending);                // 取走之后不许再开第二次
        Assert.False(gate.TryTakePending("icon-a"));
    }

    [Fact]
    public void 双击_取消待定的打开()
    {
        var gate = new DeferredTextActivation();
        gate.NoteTextTap("icon-a");

        Assert.True(gate.CancelForDoubleTap());       // 这一下确实是"文字上的第二下"
        Assert.False(gate.HasPending);
        Assert.False(gate.TryTakePending("icon-a"));  // 计时器随后到点，绝不能把程序打开
    }

    [Fact]
    public void 没有待定项时双击_不算第二下()
    {
        // 例如双击落在"文字"以外的区域：调用方据此决定"不要进编辑态"
        var gate = new DeferredTextActivation();

        Assert.False(gate.CancelForDoubleTap());
    }

    [Fact]
    public void 点了A又点B_旧计时器绝不能打开B()
    {
        var gate = new DeferredTextActivation();
        gate.NoteTextTap("icon-a");
        gate.NoteTextTap("icon-b");            // 计时器被重启

        Assert.False(gate.TryTakePending("icon-a"));   // A 的旧回调后到 ⇒ 什么都不做
        Assert.True(gate.TryTakePending("icon-b"));    // B 自己的回调照常放行
    }

    [Fact]
    public void 计时器到点但id对不上_不清空待定项()
    {
        var gate = new DeferredTextActivation();
        gate.NoteTextTap("icon-a");

        Assert.False(gate.TryTakePending("icon-b"));
        Assert.Equal("icon-a", gate.PendingId);        // 待定项还在，等它自己的计时器
        Assert.True(gate.TryTakePending("icon-a"));
    }

    [Fact]
    public void 清空之后_待定项不再放行()
    {
        // 切页 / 过滤 / 页面隐藏时调用：上下文变了，那次延迟打开就不该再发生
        var gate = new DeferredTextActivation();
        gate.NoteTextTap("icon-a");
        gate.Clear();

        Assert.False(gate.HasPending);
        Assert.False(gate.TryTakePending("icon-a"));
    }

    [Fact]
    public void 空id是调用方的bug_直接抛()
    {
        var gate = new DeferredTextActivation();

        Assert.Throws<ArgumentException>(() => gate.NoteTextTap(""));
        Assert.Throws<ArgumentException>(() => gate.TryTakePending(""));
    }
}
