// HotkeyProbeTests.cs —— 2.0.6：「这个组合是否已被其他程序的全局热键占用」的探测
//
// ⚠️ 写这类测试的纪律（本项目 memory/04 §16）：**不要写会偶发失败的测试**。
//    "某个组合一定没被占用"这种断言依赖机器上装了什么软件 ⇒ 会偶发红。
//    所以这里只断言**确定成立**的性质：
//      · 认不出的键名 → Unsupported（确定）
//      · 没有修饰键的组合 → Unsupported（确定：RegisterHotKey 本来就不接受）
//      · 一个真实组合 → 三个取值之一，且**连续两次探测结果一致**（可重复性）
//      · 探测**不会**把组合真的占住（探测完立刻注销）——用"再探一次仍然同一个结论"来间接证明

using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class HotkeyProbeTests
{
    [Theory]
    [InlineData("NotAKey")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("F25")]          // 超出 F1–F24
    [InlineData("ä")]            // 非 ASCII
    public void 认不出的键名_返回探测不了(string key)
        => Assert.Equal(HotkeyAvailability.Unsupported, HotkeyProbe.Check(new ShortcutGesture(key, Ctrl: true)));

    [Theory]
    [InlineData("T", false, false, false)]       // 完全没有修饰键
    [InlineData("F5", false, false, false)]      // 功能键裸用也不是全局热键
    public void 完全没有修饰键的组合_返回探测不了(string key, bool ctrl, bool shift, bool alt)
        => Assert.Equal(HotkeyAvailability.Unsupported,
            HotkeyProbe.Check(new ShortcutGesture(key, ctrl, shift, alt)));

    [Fact]
    public void 只有Shift的组合_是可以注册的全局热键_所以能探测()
    {
        // ⚠️ 这条是"实现按 RegisterHotKey 的真实语义来"的证据：
        //    `MOD_SHIFT` 单独就是合法的修饰键 ⇒ `Shift+Delete` **能**被别的程序注册成全局热键，
        //    所以它不能被当成"探测不了"（早先我把"只有 Shift"也归进 Unsupported，是错的）。
        var result = HotkeyProbe.Check(new ShortcutGesture("Delete", Shift: true));

        Assert.NotEqual(HotkeyAvailability.Unsupported, result);
    }

    [Fact]
    public void 真实组合_探测有明确结论_且连续两次一致()
    {
        // 挑一个极不可能被占用的组合（四个修饰键 + F24），只断言"结论稳定且不是 Unsupported"
        var gesture = new ShortcutGesture("F24", Ctrl: true, Shift: true, Alt: true);

        var first = HotkeyProbe.Check(gesture);
        var second = HotkeyProbe.Check(gesture);

        Assert.NotEqual(HotkeyAvailability.Unsupported, first);
        Assert.Equal(first, second);   // ★ 探测必须"注册→立刻注销"，否则第二次会失败（自占自）
    }

    [Fact]
    public void 探测不会留下占用_普通组合连探三次结论一致()
    {
        // 用我们自己的默认键位来探：它在本应用里是**窗口级**的（没有全局注册），
        // 所以"没人占用"是常态；这里只要求三次一致 —— 若探测忘了注销，第二次就会变成 TakenByOtherApp。
        var gesture = ShortcutBindings.DefaultGesture(ShortcutAction.NewTab);

        var results = new[] { HotkeyProbe.Check(gesture), HotkeyProbe.Check(gesture), HotkeyProbe.Check(gesture) };

        Assert.All(results, result => Assert.NotEqual(HotkeyAvailability.Unsupported, result));
        Assert.Single(results.Distinct());
    }

    [Theory]
    [InlineData("F1", 0x70u)]
    [InlineData("F12", 0x7Bu)]
    [InlineData("F24", 0x87u)]
    [InlineData("A", 0x41u)]
    [InlineData("Z", 0x5Au)]
    [InlineData("0", 0x30u)]
    [InlineData("9", 0x39u)]
    [InlineData("Tab", 0x09u)]
    [InlineData("Escape", 0x1Bu)]
    [InlineData("Delete", 0x2Eu)]
    [InlineData("Up", 0x26u)]
    public void 键名到虚拟键码的映射(string key, uint expected)
    {
        Assert.True(HotkeyProbe.TryToVirtualKey(key, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 键名映射忽略大小写()
    {
        Assert.True(HotkeyProbe.TryToVirtualKey("escape", out var lower));
        Assert.True(HotkeyProbe.TryToVirtualKey("ESCAPE", out var upper));
        Assert.Equal(lower, upper);
    }
}
