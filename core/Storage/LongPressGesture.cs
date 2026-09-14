// LongPressGesture.cs —— 「长按起拖」的手势判定（纯逻辑，可单测）
//
// 目标手感（用户 2026-09-14 选定，手机桌面式）：
//   · 按住 **350ms**（默认）→ 图块"拎起"、开始跟手拖动；
//   · 起拖后**全程没移动就松手** → 认为用户是"长按要菜单"，弹出该图块的右键菜单；
//   · 阈值**之前**移动超过容差 → 这次手势作废（既不重排也不弹菜单，避免"想点却拖动了"）；
//   · 阈值之前就松手 → 普通点击（打开图标，由 UI 的 ItemClick 负责，这里什么都不做）。
//
// 为什么放 Core：这套判定是**纯逻辑**（时间 + 距离 + 状态），天生可单测；
// 放到 UI 里就只能靠手点 + 手感描述来验，而"手感"恰恰是最难复现的。
//
// ⚠️ 用调用方传进来的毫秒时间戳（而不是内部读时钟）：单测可以完全确定地推进时间，
//    不依赖真实计时器（Windows 定时器精度本来就不可靠，实测抖动可达十几毫秒）。

namespace ToolboxPanel.Core.Storage;

/// <summary>一次长按手势的结论。</summary>
public enum LongPressOutcome
{
    /// <summary>什么都不用做（普通点击、被取消、或已处理过）。</summary>
    None,

    /// <summary>该起拖了（按住已超过阈值）。</summary>
    DragStarted,

    /// <summary>起拖后原地松手 → 弹该图块的右键菜单。</summary>
    MenuRequested,
}

/// <summary>
/// 长按手势状态机：按住 → 超阈值起拖 → 原地松手要菜单。
/// 一次手势的生命周期：<see cref="Press"/> →（<see cref="Move"/> / <see cref="Tick"/>）→ <see cref="Complete"/>。
/// </summary>
public sealed class LongPressGesture
{
    /// <summary>默认阈值：350ms（用户选定；比系统默认的"按住再拖"更短，接近手机桌面手感）。</summary>
    public const int DefaultThresholdMs = 350;

    /// <summary>默认移动容差（DIP）：超过它就算"用户在拖/滑"，不再算长按。</summary>
    public const double DefaultMoveTolerance = 8.0;

    private double _startX;
    private double _startY;
    private long _pressedAtMs;
    private bool _tickHandled;

    public LongPressGesture(
        int thresholdMs = DefaultThresholdMs,
        double moveTolerance = DefaultMoveTolerance)
    {
        ThresholdMs = Math.Max(1, thresholdMs);
        MoveTolerance = Math.Max(0.5, moveTolerance);
    }

    public int ThresholdMs { get; }

    public double MoveTolerance { get; }

    /// <summary>当前是否按着（按下到 <see cref="Complete"/> 之间为 true）。</summary>
    public bool IsPressed { get; private set; }

    /// <summary>本次手势是否已经起拖。</summary>
    public bool HasDragStarted { get; private set; }

    /// <summary>本次手势是否已经移动超过容差。</summary>
    public bool MovedBeyondTolerance { get; private set; }

    /// <summary>按下（记录起点与时刻）。再次按下会重置上一次手势。</summary>
    public void Press(double x, double y, long nowMs)
    {
        _startX = x;
        _startY = y;
        _pressedAtMs = nowMs;
        _tickHandled = false;
        IsPressed = true;
        HasDragStarted = false;
        MovedBeyondTolerance = false;
    }

    /// <summary>
    /// 指针移动（相对按下点的**当前**坐标）。
    /// </summary>
    /// <returns>true 表示"这次移动取消了长按"（还没起拖、且已超出容差）。</returns>
    public bool Move(double x, double y)
    {
        if (!IsPressed)
        {
            return false;
        }

        if (MovedBeyondTolerance)
        {
            return !HasDragStarted;   // 已经判过取消：仍按"取消"回答（幂等）
        }

        var dx = x - _startX;
        var dy = y - _startY;

        if ((dx * dx) + (dy * dy) <= MoveTolerance * MoveTolerance)
        {
            return false;
        }

        MovedBeyondTolerance = true;

        // 已经起拖之后再移动是**正常拖动**，不算取消
        return !HasDragStarted;
    }

    /// <summary>
    /// 定时器到点（调用方按 <see cref="ThresholdMs"/> 起一个一次性定时器）。
    /// </summary>
    /// <returns>true 表示"该起拖了"；只会返回一次 true（重复 Tick 幂等）。</returns>
    public bool Tick(long nowMs)
    {
        if (!IsPressed || _tickHandled || HasDragStarted || MovedBeyondTolerance)
        {
            return false;
        }

        if (nowMs - _pressedAtMs < ThresholdMs)
        {
            return false;   // 定时器早到了（或时间戳没推进）：再等等
        }

        _tickHandled = true;
        HasDragStarted = true;
        return true;
    }

    /// <summary>
    /// 手势结束（松手 / 拖动完成 / 指针取消）。
    /// </summary>
    /// <returns>
    /// 起拖过且**全程没移动** → <see cref="LongPressOutcome.MenuRequested"/>（原地长按 = 要菜单）；
    /// 其余一律 <see cref="LongPressOutcome.None"/>。
    /// </returns>
    public LongPressOutcome Complete()
    {
        var menuRequested = IsPressed && HasDragStarted && !MovedBeyondTolerance;

        IsPressed = false;
        _tickHandled = false;

        return menuRequested ? LongPressOutcome.MenuRequested : LongPressOutcome.None;
    }

    /// <summary>把状态清干净（例如指针捕获丢失、页面切走）。</summary>
    public void Reset()
    {
        IsPressed = false;
        HasDragStarted = false;
        MovedBeyondTolerance = false;
        _tickHandled = false;
    }
}
