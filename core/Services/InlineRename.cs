// InlineRename.cs —— 「双击文字就地改名」的纯逻辑（提交决定 + 双击间隔）
//
// 界面侧（GridPage / TabStripView）只负责"把输入框里的字交上来、按结论办事"，
// 决定本身放这里 ⇒ 可以脱离 UI 单测（与 IconEditor / TabEditor 同一套纪律）。
//
// 语义（照旧版 v1.11.6 的 `IconLabel._finish_edit`）：
//   · 输入框空了      ⇒ **回退原值，什么都不写**（原版内联编辑同样丢弃空名，不弹错）
//   · 与原名一样      ⇒ 不落库、不提示（原版 `if new_text != current_name`）
//   · 其余            ⇒ 提交（名字已 Trim）
//
// ⚠️ 名字**必填**这件事属于"要落库的那一层"（`IconEditor.Rename` / `TabEditor.Rename` 各自判，
//    空名返回失败）；这里的 Revert 是**界面语义**（连试都不试），两者刻意分开：
//    空名走对话框那条路要给用户一句提示，走内联这条路则是"你什么都没改"。

namespace ToolboxPanel.Core.Services;

/// <summary>内联改名提交时三种结论。</summary>
public enum InlineRenameOutcome
{
    /// <summary>空输入：回退原值，不落库、不提示。</summary>
    Revert,

    /// <summary>与原名相同：什么都不做。</summary>
    Unchanged,

    /// <summary>真的改了：按 <see cref="InlineRenameDecision.Name"/> 落库。</summary>
    Commit,
}

/// <summary>提交决定（<see cref="InlineRenameDecision.Name"/> 在 Commit 时是新名字，其余情况是原名的 Trim 值）。</summary>
public readonly record struct InlineRenameDecision(InlineRenameOutcome Outcome, string Name);

/// <summary>「双击文字就地改名」的决定与时间参数。</summary>
public static class InlineRename
{
    /// <summary>延后打开的最短时间（毫秒）：比这更短会"像没延迟"、双击反而更容易误开。</summary>
    public const int MinDelayMs = 250;

    /// <summary>延后打开的最长时间（毫秒）：比这更长，单击在文字上会明显发木。</summary>
    public const int MaxDelayMs = 500;

    /// <summary>系统值读不到（0 / 负数）时的兜底：400ms，接近 Windows 默认双击间隔。</summary>
    public const int FallbackDelayMs = 400;

    /// <summary>
    /// 把系统双击间隔映射成"点文字时延后打开"的等待时间。
    ///
    /// <para>为什么不直接用系统值：用户可以把系统双击间隔调到 900ms 甚至更久，
    /// 那样"单击文字打开图标"会慢到像死机（本项目打开动作本来就是单击即达）。
    /// 所以夹在 [<see cref="MinDelayMs"/>, <see cref="MaxDelayMs"/>]：
    /// 够长到能容纳一次双击，够短到单击仍然跟手。</para>
    /// </summary>
    public static int DelayFromSystemDoubleClickTime(int systemMilliseconds)
        => systemMilliseconds <= 0
            ? FallbackDelayMs
            : Math.Clamp(systemMilliseconds, MinDelayMs, MaxDelayMs);

    /// <summary>名字一律 Trim（与原版内联编辑、以及两个 Editor 的校验口径一致）。</summary>
    public static string Trim(string? raw) => raw?.Trim() ?? string.Empty;

    /// <summary>
    /// 给出提交结论。**不碰模型**：调用方按结论办事（Revert → 显示原值；Commit → 走各自的编辑器落库）。
    /// </summary>
    public static InlineRenameDecision Decide(string? original, string? typed)
    {
        var originalTrimmed = Trim(original);
        var name = Trim(typed);

        if (name.Length == 0)
        {
            return new InlineRenameDecision(InlineRenameOutcome.Revert, originalTrimmed);
        }

        if (string.Equals(name, originalTrimmed, StringComparison.Ordinal))
        {
            return new InlineRenameDecision(InlineRenameOutcome.Unchanged, name);
        }

        return new InlineRenameDecision(InlineRenameOutcome.Commit, name);
    }
}
