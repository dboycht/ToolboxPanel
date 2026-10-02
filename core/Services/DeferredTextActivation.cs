// DeferredTextActivation.cs —— 「点文字要延后打开、双击则只改名」的状态机（纯逻辑，可单测）
//
// 背景：图块是**单击即打开**，而"双击文字改名"的第一下也是一次单击。
// 折中（用户 2026-10-02 选定）：按在**文字**上时把打开延后一个双击间隔 ——
//   · 一个间隔内没有第二下 ⇒ 照常打开（单击语义不变，只是慢了几百毫秒）
//   · 期间来了第二下（双击）  ⇒ **取消待定的打开**，进入就地改名（绝不误开程序）
// 按在图标本体 / 图块其它位置 ⇒ 不进这个状态机，仍然立刻打开。
//
// ⚠️ 这里只记"待定的是哪一项"，**不发计时器也不碰 UI** ——
//    计时器（DispatcherTimer）与系统双击间隔的读取都在界面层，
//    这样这段最容易出错的时序逻辑可以脱离 WinUI 单测。

namespace ToolboxPanel.Core.Services;

/// <summary>「文字上单击 ⇒ 延后打开；双击 ⇒ 取消并改名」的待定项登记。</summary>
public sealed class DeferredTextActivation
{
    private string? _pendingId;

    /// <summary>待打开的项 id（null = 没有待定项）。</summary>
    public string? PendingId => _pendingId;

    /// <summary>现在有没有待定项。</summary>
    public bool HasPending => _pendingId is not null;

    /// <summary>
    /// 在某一项的文字上按下/单击：登记（或替换）待定项。
    ///
    /// <para>返回 true 表示调用方应当（重新）启动计时器。替换成另一项时也必须重启计时器 ——
    /// 否则"点了 A 的文字、马上点 B 的文字"会让 A 的计时器把 B 打开。</para>
    /// </summary>
    public bool NoteTextTap(string itemId)
    {
        ArgumentException.ThrowIfNullOrEmpty(itemId);

        _pendingId = itemId;
        return true;
    }

    /// <summary>
    /// 计时器到点：只有"待定的还是同一项"才放行（返回 true，并清空待定项）。
    ///
    /// <para>两个必须由它兜住的场景：① 计时器是**重启**过的，旧计时器的回调可能后到；
    /// ② 已改成别的项 / 已被双击取消，此时旧回调绝不能把东西打开。</para>
    /// </summary>
    public bool TryTakePending(string itemId)
    {
        ArgumentException.ThrowIfNullOrEmpty(itemId);

        if (!string.Equals(_pendingId, itemId, StringComparison.Ordinal))
        {
            return false;
        }

        _pendingId = null;
        return true;
    }

    /// <summary>
    /// 双击（要改名了）：取消待定的打开。返回是否真的取消了东西
    /// （= 这一下是"文字上的第二下"，调用方据此知道双击语义成立）。
    /// </summary>
    public bool CancelForDoubleTap()
    {
        var had = _pendingId is not null;
        _pendingId = null;
        return had;
    }

    /// <summary>清空（切页 / 过滤 / 页面隐藏 / 编辑开始等"上下文变了"的场合调用）。</summary>
    public void Clear() => _pendingId = null;
}
