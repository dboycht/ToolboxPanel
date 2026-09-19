// ShortcutBindings.cs —— 快捷键**绑定**：默认值 / 用户覆盖 / 冲突判据（纯逻辑，可单测）
//
// 与 `ShortcutCatalog` 的分工：
//   · `ShortcutCatalog`  = **默认**目录（有哪些功能、默认键位、功能名 key）—— 静态数据；
//   · `ShortcutBindings` = **当前生效**的绑定（默认 + 用户覆盖）+ "这条键位有没有问题"的判据。
//
// 用户改键存在 `config.json` 的 `shortcut_bindings`（**差分存储**：只存与默认不同的，
// 于是"我们以后改了默认键位"能自动惠及没动过那条的用户）。
//
// ────────────────────────── 冲突判据：诚实说明"能判什么 / 不能判什么" ──────────────────────────
// 本项目的快捷键是**窗口级**的（`KeyboardAccelerator` 挂在窗口根元素上）⇒
//   ① 它们**不会**去别的软件里抢键；
//   ② 但**反过来会**：若别的软件注册了**全局热键**占同一组合，系统会把键截走给那个软件，
//      我们的窗口根本收不到 ⇒ 我们的快捷键"按不出来"。
// 所以"被其他程序占用"这一条**必须实际探测**（`HotkeyProbe`，用 RegisterHotKey 试一下再注销），
// **不能**靠在 Core 里猜 —— 本文件只判"静态可判"的四类：重复 / 输入框编辑键 / 系统保留 / 缺修饰键。

namespace ToolboxPanel.Core.Services;

/// <summary>一条绑定的问题类别（<see cref="None"/> = 没发现问题）。</summary>
public enum ShortcutIssue
{
    None,

    /// <summary>与另一条功能绑了同一个组合（按下去只有一条生效）。</summary>
    Duplicate,

    /// <summary>输入文字时会先被输入框接管（最典型：`Shift+Delete` 在文本框里是**剪切**）。</summary>
    TextEditing,

    /// <summary>Windows 保留的组合（`Alt+F4` 之类）—— 设了也不会生效。</summary>
    Reserved,

    /// <summary>没有足够的修饰键：裸字母/裸数字会与打字冲突（`Shift+字母` 等于大写，也不行）。</summary>
    NoModifier,

    /// <summary>被其他程序的全局热键占用（**需要探测**，见 <see cref="HotkeyProbe"/>）。</summary>
    TakenByOtherApp,
}

/// <summary>一条绑定：动作 + 当前生效键位。</summary>
public sealed record ShortcutBinding(ShortcutAction Action, ShortcutGesture Gesture);

/// <summary>一次冲突判定的结果（问题类别 + 需要展示给用户的相关名字）。</summary>
public sealed record ShortcutIssueInfo(ShortcutIssue Issue, string? OtherLabelKey = null);

/// <summary>绑定的默认值 / 覆盖 / 冲突判据。</summary>
public static class ShortcutBindings
{
    /// <summary>默认绑定（取自 <see cref="ShortcutCatalog"/>，只含本应用注册的那些）。</summary>
    public static IReadOnlyList<ShortcutBinding> Defaults { get; } =
        ShortcutCatalog.AppShortcuts.Select(entry => new ShortcutBinding(entry.Action, entry.Gesture)).ToList();

    /// <summary>当前生效的绑定（默认值 + 用户覆盖）。</summary>
    /// <param name="overrides">动作名 → 键位串（来自 `config.json`）；null / 非法值一律回落默认。</param>
    public static IReadOnlyList<ShortcutBinding> Resolve(IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return Defaults;
        }

        var resolved = new List<ShortcutBinding>(Defaults.Count);

        foreach (var binding in Defaults)
        {
            var gesture = binding.Gesture;

            if (overrides.TryGetValue(binding.Action.ToString(), out var text)
                && ShortcutGesture.Parse(text) is { } parsed)
            {
                gesture = parsed;
            }

            resolved.Add(new ShortcutBinding(binding.Action, gesture));
        }

        return resolved;
    }

    /// <summary>
    /// 把当前绑定压成**覆盖表**（只留与默认不同的那些）。
    /// <para>⚠️ 差分存储的好处：将来我们改了某个默认键位，没动过那条的用户会自动跟上；
    /// 若全量存储，改默认值对老用户就永远无效了。</para>
    /// </summary>
    public static Dictionary<string, string> ToOverrides(IEnumerable<ShortcutBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var byAction = bindings.ToDictionary(binding => binding.Action, binding => binding.Gesture);
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var fallback in Defaults)
        {
            if (byAction.TryGetValue(fallback.Action, out var gesture)
                && !string.Equals(gesture.Display, fallback.Gesture.Display, StringComparison.OrdinalIgnoreCase))
            {
                overrides[fallback.Action.ToString()] = gesture.Display;
            }
        }

        return overrides;
    }

    /// <summary>某一个动作的默认键位。</summary>
    public static ShortcutGesture DefaultGesture(ShortcutAction action)
        => Defaults.FirstOrDefault(binding => binding.Action == action)?.Gesture
           ?? ShortcutCatalog.Find(action)?.Gesture
           ?? new ShortcutGesture("None");

    /// <summary>把绑定还原成某个动作的默认键位（找不到该动作时原样返回）。</summary>
    public static IReadOnlyList<ShortcutBinding> RestoreDefault(
        IReadOnlyList<ShortcutBinding> bindings, ShortcutAction action)
        => bindings.Select(binding => binding.Action == action
            ? binding with { Gesture = DefaultGesture(action) }
            : binding).ToList();

    /// <summary>
    /// 逐条判定"静态可判"的问题（重复 / 编辑键 / 系统保留 / 缺修饰键）。
    ///
    /// <para>⚠️ **只判本应用注册的那些**（`Kind == App`）：`Alt+F4` 这种系统级条目不是我们的快捷键，
    /// 把它标成"系统保留"会让界面一打开就一片黄。</para>
    /// </summary>
    public static IReadOnlyDictionary<ShortcutAction, ShortcutIssueInfo> FindIssues(
        IReadOnlyList<ShortcutBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var issues = new Dictionary<ShortcutAction, ShortcutIssueInfo>(bindings.Count);

        // 同一个组合被几条功能占了（用 Display 做键：大小写/顺序由 Display 统一）
        var groups = bindings
            .GroupBy(binding => binding.Gesture.Display, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var binding in bindings)
        {
            if (ValidateGesture(binding.Gesture) is { } invalid && invalid != ShortcutIssue.None)
            {
                issues[binding.Action] = new ShortcutIssueInfo(invalid);
                continue;
            }

            if (groups.TryGetValue(binding.Gesture.Display, out var same)
                && same.Count > 1
                && same.FirstOrDefault(other => other.Action != binding.Action) is { } conflict)
            {
                issues[binding.Action] = new ShortcutIssueInfo(
                    ShortcutIssue.Duplicate, ShortcutCatalog.Find(conflict.Action)?.LabelKey);
                continue;
            }

            if (IsReserved(binding.Gesture))
            {
                issues[binding.Action] = new ShortcutIssueInfo(ShortcutIssue.Reserved);
                continue;
            }

            if (IsTextEditingKey(binding.Gesture))
            {
                issues[binding.Action] = new ShortcutIssueInfo(ShortcutIssue.TextEditing);
            }
        }

        return issues;
    }

    /// <summary>
    /// 一个用户按下的组合能不能当快捷键（**改键时的校验**）。
    /// <para>顺序有讲究：先判"输入框编辑键"（它是**允许但警告**的，见用户 2026-09-19 的决定），
    /// 再判"修饰键够不够" —— 否则 `Shift+Delete`（默认值之一）会被误报成"缺修饰键"。</para>
    /// </summary>
    public static ShortcutIssue ValidateGesture(ShortcutGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        if (string.IsNullOrWhiteSpace(gesture.Key) || string.Equals(gesture.Key, "None", StringComparison.OrdinalIgnoreCase))
        {
            return ShortcutIssue.NoModifier;
        }

        if (IsTextEditingKey(gesture))
        {
            return ShortcutIssue.TextEditing;   // 允许保存，但界面要标黄警告
        }

        return HasEnoughModifier(gesture) ? ShortcutIssue.None : ShortcutIssue.NoModifier;
    }

    /// <summary>
    /// 修饰键够不够：必须有 Ctrl 或 Alt；例外是功能键（F1–F24，`F5` / `Shift+F5` 这类很常见）。
    ///
    /// <para>⚠️ 刻意**不**允许"Shift + 字母/数字"：那等于输入大写字母 ⇒ 会与打字冲突，判它缺修饰键。
    /// 而 `Shift+Delete` 那种"非文本键 + Shift"由 <see cref="IsTextEditingKey"/> 先接走
    /// （它是**允许但警告**的那一类），不会落到这里。</para>
    /// </summary>
    private static bool HasEnoughModifier(ShortcutGesture gesture)
        => gesture.Ctrl || gesture.Alt || IsFunctionKey(gesture.Key);

    /// <summary>
    /// 文本框里会被**输入框自己**先接管的组合（我们的守卫在这种情况下放行给文本框）。
    ///
    /// <para>⚠️ 只有**真正的编辑键**才算：功能键（F5 之类）在文本框里不产生编辑行为，
    /// 所以裸 `F5` 是合法快捷键（早先误把它算进"非文本键"⇒ 裸 F5 被误报，已修）。</para>
    /// </summary>
    private static bool IsTextEditingKey(ShortcutGesture gesture)
    {
        // 不带 Ctrl/Alt 的编辑键（带不带 Shift 都算）：Delete / Backspace / Home / End / 方向键 / PageUp·Down
        if (!gesture.Ctrl && !gesture.Alt && IsEditingKey(gesture.Key))
        {
            return true;
        }

        // Ctrl + 剪贴板 / 撤销类
        if (gesture.Ctrl && !gesture.Alt)
        {
            return gesture.Key.ToUpperInvariant() switch
            {
                "C" or "V" or "X" or "A" or "Z" or "Y" => true,
                _ => false,
            };
        }

        return false;
    }

    /// <summary>Windows 保留的组合（设了也不会生效）。</summary>
    private static bool IsReserved(ShortcutGesture gesture)
        => gesture.Alt && !gesture.Ctrl && string.Equals(gesture.Key, "F4", StringComparison.OrdinalIgnoreCase);

    private static bool IsFunctionKey(string key)
    {
        if (key.Length < 2 || (key[0] != 'F' && key[0] != 'f'))
        {
            return false;
        }

        return int.TryParse(key[1..], out var number) && number is >= 1 and <= 24;
    }

    /// <summary>
    /// 真正的"编辑键"：在文本框里按它们会被**文本编辑**接管
    /// （注意**不含**功能键 —— 裸 `F5` 在文本框里什么都不做，所以可以当快捷键）。
    /// </summary>
    private static bool IsEditingKey(string key)
        => key.ToUpperInvariant() switch
        {
            "DELETE" or "INSERT" or "BACK" or "BACKSPACE" or "HOME" or "END"
                or "PAGEUP" or "PAGEDOWN" or "UP" or "DOWN" or "LEFT" or "RIGHT" => true,
            _ => false,
        };
}
