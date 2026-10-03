// TapGestureGate.cs —— 「单击文字 = 延后打开、双击文字 = 就地改名」的**防误开闸门**（纯逻辑，可单测）
//
// 背景（2026-10-03 用户实测反馈"双击图标文字，会闪一下编辑，然后正常打开了"）：
//   图块是**单击即打开**，而"双击文字改名"的第一下也是一次单击；`DeferredTextActivation`
//   负责"延后打开、双击时取消"，但它只挡住了**计时器到点那一次**，挡不住下面这个竞态：
//
//     WinUI 的 `Tapped` / `DoubleTapped` 是两个独立事件，**先后顺序不由我们决定**
//     （单击必须等一个双击间隔确认"没有第二下"才敢报，所以两次 `Tapped` 与一次
//      `DoubleTapped` 的到达顺序取决于手势识别器的内部时序）。于是存在这条路：
//       ① 双击 → `DoubleTapped` 到 ⇒ 进就地改名（这就是用户看到的那"闪一下"）；
//       ② **随后**第二次点击的 `Tapped` 才到 ⇒ 又把"延后打开"登记上 ⇒ 一个间隔后
//          真的把程序打开了（用户看到"然后又正常打开了"）。
//     ⚠️ 关键点：那个 `Tapped` 是**双击手势的一部分**，绝不能重新登记打开。
//
// 判据取"**这次点击距上一次双击是否还在系统双击间隔之内**"（而不是"下一个 Tapped 一律丢掉"）：
//   · 不看事件顺序 —— 无论 `Tapped` 先到还是 `DoubleTapped` 先到，落在窗口内的一律忽略；
//   · 不会误伤单击 —— 用户真的想点开时，那一下必然落在"双击手势已经结束"之后
//     （否则系统只会把它认成双击，不会报单击）。
//
// ⚠️ 这里只做**时间判定**，不碰 UI 也不起计时器（计时器在页面层，见 GridPage）：
//    这样这段最容易出错的时序逻辑可以脱离 WinUI 单测。

namespace ToolboxPanel.Core.Services;

/// <summary>「点文字」与「双击改名」共用的防误开闸门。</summary>
public sealed class TapGestureGate
{
    /// <summary>上次双击发生的时刻（null = 本次会话还没有过双击）。</summary>
    private long? _lastDoubleTapMs;

    /// <summary>
    /// 记录一次"点在文字上"的单击，并回答"这一下要不要按单击处理"。
    ///
    /// <para>返回 false 表示它是双击手势的一部分（紧接着上一次 `DoubleTapped` 到来），
    /// 调用方**什么都不要做** —— 既不许登记延后打开，也不许启动计时器。</para>
    /// </summary>
    /// <param name="nowMs">当前时刻（单调递增的毫秒数；界面上取 <c>Environment.TickCount64</c>）。</param>
    /// <param name="doubleClickMs">
    /// 系统双击间隔（毫秒）。**≤0（读系统值失败）时一律不抑制** —— 此时"抑制窗口"无从谈起，
    /// 宁可偶尔把双击尾巴当成单击（最坏是那一下没打开），也不能让"点文字打开"整个失灵。
    /// </param>
    public bool NoteTextTap(long nowMs, int doubleClickMs)
    {
        if (_lastDoubleTapMs is not { } last || doubleClickMs <= 0)
        {
            return true;
        }

        // ⚠️ 万一 `nowMs` 回绕/倒退（换用 `TickCount` 而不是 `TickCount64` 时会），
        //    也一律按"双击手势还在进行"处理：宁可少一次单击，不能误开程序。
        var elapsed = nowMs - last;
        return elapsed > doubleClickMs;
    }

    /// <summary>
    /// 记录一次双击（要进就地改名了）。此刻起一个双击间隔内的单击全部作废。
    /// </summary>
    /// <param name="nowMs">当前时刻（与 <see cref="NoteTextTap"/> 同一时间源）。</param>
    public void NoteDoubleTap(long nowMs) => _lastDoubleTapMs = nowMs;

    /// <summary>清空（切页 / 过滤 / 页面隐藏等"上下文变了"的场合调用）。</summary>
    public void Clear() => _lastDoubleTapMs = null;
}
