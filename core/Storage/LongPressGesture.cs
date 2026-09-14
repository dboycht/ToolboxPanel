// LongPressGesture.cs —— 「长按起拖」的判定（纯逻辑，可单测）
//
// 用户 2026-09-14 明确要的手感（手机桌面）：
//   **按住 350ms → 立刻"浮起"跟手拖，像磁贴一样；松手落在哪就排到哪。**
//   **没有"长按原地松手 = 弹菜单"那套判定**（那是上一版我自己加的，用户明确要求去掉）；
//   **也不因"按住期间移动过"而取消**（上一版会因为鼠标抖了几下就把长按废掉，反而像"没反应"）。
//
// 所以这里的判定只剩一条：**按住够久 ⇒ 起拖**。
//   按下 → 到点 Tick() 返回 true（调用方据此起拖）
//   阈值前松手 → 什么都不做（那就是一次普通点击，由 UI 的 ItemClick 去"打开"）
//
// ⚠️ 时间戳由调用方传入（不在内部读时钟）：单测可以确定地推进时间。
// ⚠️ **RemainingMs 是必需的**：Windows 定时器会**早到**（不是精确计时）。
//    上一版的 bug 就是"早到 ⇒ Tick 返回 false ⇒ 什么都不做"，于是长按偶发完全没反应。
//    调用方应当在早到时按 RemainingMs 重排定时器（见 GridPage/ListViewPage 的用法）。

namespace ToolboxPanel.Core.Storage;

/// <summary>长按判定状态机：按住够久就该起拖。</summary>
public sealed class LongPressGesture
{
    /// <summary>默认阈值：350ms（用户选定；接近手机桌面的手感）。</summary>
    public const int DefaultThresholdMs = 350;

    private long _pressedAtMs;

    public LongPressGesture(int thresholdMs = DefaultThresholdMs)
    {
        ThresholdMs = Math.Max(1, thresholdMs);
    }

    public int ThresholdMs { get; }

    /// <summary>当前是否按着（按下 → Release/Reset 之间为 true）。</summary>
    public bool IsPressed { get; private set; }

    /// <summary>本次"按住"是否已经起拖（UI 用它决定松手时要不要走点击逻辑）。</summary>
    public bool HasDragStarted { get; private set; }

    /// <summary>按下（再次按下会开一次新手势）。</summary>
    public void Press(long nowMs)
    {
        _pressedAtMs = nowMs;
        IsPressed = true;
        HasDragStarted = false;
    }

    /// <summary>距离阈值还差多少毫秒（已到点或没按下时返回 0，不会是负数）。</summary>
    public int RemainingMs(long nowMs)
    {
        if (!IsPressed)
        {
            return 0;
        }

        var remaining = ThresholdMs - (nowMs - _pressedAtMs);
        return remaining > 0 ? (int)remaining : 0;
    }

    /// <summary>
    /// 定时器到点：该起拖返回 true。
    /// **只会返回一次 true**（重复 Tick 幂等）；**早到返回 false**，调用方应按
    /// <see cref="RemainingMs"/> 重排定时器再试。
    /// </summary>
    public bool Tick(long nowMs)
    {
        if (!IsPressed || HasDragStarted)
        {
            return false;
        }

        if (nowMs - _pressedAtMs < ThresholdMs)
        {
            return false;   // 早到：还差 RemainingMs 毫秒
        }

        HasDragStarted = true;
        return true;
    }

    /// <summary>
    /// 松手 / 指针被取消：清掉"按着"的状态。
    /// <see cref="HasDragStarted"/> 保留到下一次 <see cref="Press"/>（拖动收尾时还要读它）。
    /// </summary>
    public void Release() => IsPressed = false;

    /// <summary>彻底清干净（切页、捕获丢失等）。</summary>
    public void Reset()
    {
        IsPressed = false;
        HasDragStarted = false;
    }
}
