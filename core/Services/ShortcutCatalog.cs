// ShortcutCatalog.cs —— 快捷键**目录**（纯数据 + 纯函数，可单测）
//
// 为什么把"有哪些快捷键"搬进 Core：
//   ① 它天生是**数据**（功能 + 按键），可以单测（按键不重复、文案 key 都在表里）；
//   ② 更关键：界面**照着这份目录注册加速器**（不是手写一份 XAML 列表）——
//      这样"加了功能忘了接线""改了键位只改一处"这类漂移**在结构上就不可能发生**。
//      本项目已经被这一类问题咬过（标签栏右键菜单、`AddTab` 长期没有入口）。
//
// 基准 = 原版 v1.11.6 的 `src/toolbox/shortcut_dialog.py`（那张"功能 × 按键"表）
//        + `app_window.py` 的菜单栏快捷键。**功能名沿用原版 i18n 的 `shortcut.*` 文案**；
//        键位串按本实现的实际情况写（见下面"原版有、我们有意不列"一节的说明）。
//
// ────────────────────────── ⚠️ 原版有、我们**有意不列**的三条 ──────────────────────────
//   · `shortcut.open_icon`（原版 Enter / 双击）
//   · `shortcut.rename_icon`（原版 F2）
//   · `shortcut.delete_icon`（原版 Delete）
//   ⇒ 这三条都是"对**当前选中的图标**"动作，而本实现的网格页/列表页是
//     `SelectionMode="None"`（**没有选中项这个模型**：单击/双击即打开，选择只有批量模式的勾选框）。
//     硬塞进去只会做出"按了没反应"的哑快捷键 —— 所以**不列**。
//     "删除勾选项"这条路径由 `shortcut.batch_delete`（Shift+Delete）覆盖。
//     将来若真加了选中模型，再把这三行补回来（补的时候记得同时接线，否则又变成"列了但按不动"）。

namespace ToolboxPanel.Core.Services;

/// <summary>快捷键对应的动作（界面按它分派到具体处理函数）。</summary>
public enum ShortcutAction
{
    NewTab,
    NewListTab,
    CloseTab,
    RenameTab,
    PrevTab,
    NextTab,
    Find,
    BatchMode,
    BatchDelete,
    NewFile,
    NewFolder,
    NewShortcut,
    NewUrl,
    NewCommand,
    ResetData,
    Export,
    Import,
    ExitApp,
}

/// <summary>这条快捷键由谁负责。</summary>
public enum ShortcutKind
{
    /// <summary>本应用自己注册并处理。</summary>
    App,

    /// <summary>系统级（Windows 自己处理，我们只把它列在参考里，例如 Alt+F4）。</summary>
    System,
}

/// <summary>
/// 一个键位组合。<see cref="Key"/> 用 W3C 风格的键名（<c>"T"</c> / <c>"Tab"</c> / <c>"Delete"</c>），
/// 界面侧再映射成 <c>Windows.System.VirtualKey</c>。
/// </summary>
public sealed record ShortcutGesture(string Key, bool Ctrl = false, bool Shift = false, bool Alt = false)
{
    /// <summary>给人看的写法（快捷键参考窗口用）。</summary>
    public string Display
    {
        get
        {
            var parts = new List<string>(4);
            if (Ctrl) parts.Add("Ctrl");
            if (Shift) parts.Add("Shift");
            if (Alt) parts.Add("Alt");
            parts.Add(Key);
            return string.Join("+", parts);
        }
    }

    /// <summary>把键位串还原回来（诊断/日志用）。</summary>
    public static ShortcutGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        bool ctrl = false, shift = false, alt = false;
        string? key = null;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    ctrl = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                default:
                    key = raw;
                    break;
            }
        }

        return key is null ? null : new ShortcutGesture(key, ctrl, shift, alt);
    }
}

/// <summary>一行：动作 + 功能名（i18n key）+ 键位 + 谁来处理 + 打字时是否放行。</summary>
/// <param name="Action">动作。</param>
/// <param name="LabelKey">功能名的文案 key（原版 <c>shortcut.*</c>）。</param>
/// <param name="Gesture">键位。</param>
/// <param name="Kind">谁负责（本应用 / 系统）。</param>
/// <param name="SafeWhileTyping">
/// **焦点在文本框里（用户正在输入）时要不要照样接管**。
///
/// <para>⚠️ 默认 <c>false</c>：应用级加速器是"窗口级"的，不问青红皂白就会抢键 ——
/// 最典型的是 `Shift+Delete`：在文本框里那是**剪切**，被抢走就变成删图标。
/// 只有**在文本框里也语义明确、且不会造成破坏**的才标 <c>true</c>
/// （例：`Ctrl+F` 开关搜索栏 —— 用户正在搜索框里打字时按它，期望就是"关掉搜索"，
/// 而 Esc 虽然也能关，但没理由让原来能用的快捷键退化）。</para>
/// </param>
public sealed record ShortcutEntry(
    ShortcutAction Action,
    string LabelKey,
    ShortcutGesture Gesture,
    ShortcutKind Kind,
    bool SafeWhileTyping = false);

/// <summary>快捷键目录。<b>界面只读它，不另外维护一份列表。</b></summary>
public static class ShortcutCatalog
{
    /// <summary>对话框标题与两列表头（原版 <c>shortcut.dialog.title</c> / <c>shortcut.col.*</c>）。</summary>
    public static string DialogTitle => I18n.T("shortcut.dialog.title");

    public static string ColumnAction => I18n.T("shortcut.col.action");

    public static string ColumnKey => I18n.T("shortcut.col.key");

    /// <summary>
    /// 全部快捷键（顺序 = 参考窗口里的显示顺序）。
    /// </summary>
    public static IReadOnlyList<ShortcutEntry> All { get; } = new List<ShortcutEntry>
    {
        // ── 标签页 ──（Ctrl+Shift+T 是原版菜单里的键位；顺带补进了原版那张参考表漏掉的一行）
        new(ShortcutAction.NewTab, "shortcut.new_tab", new ShortcutGesture("T", Ctrl: true), ShortcutKind.App),
        new(ShortcutAction.NewListTab, "list.new_tab", new ShortcutGesture("T", Ctrl: true, Shift: true), ShortcutKind.App),
        new(ShortcutAction.RenameTab, "shortcut.rename_tab", new ShortcutGesture("R", Ctrl: true), ShortcutKind.App),
        new(ShortcutAction.CloseTab, "shortcut.close_tab", new ShortcutGesture("W", Ctrl: true), ShortcutKind.App),
        new(ShortcutAction.PrevTab, "shortcut.prev_tab", new ShortcutGesture("Tab", Ctrl: true, Shift: true), ShortcutKind.App),
        new(ShortcutAction.NextTab, "shortcut.next_tab", new ShortcutGesture("Tab", Ctrl: true), ShortcutKind.App),

        // ── 查找 / 批量 ──
        // ⚠️ 只有 `Ctrl+F` 标了 SafeWhileTyping：它是搜索栏自己的开关，
        //    用户在搜索框里打字时按它期望就是"关掉搜索"（原版行为），不该因为输入焦点而失效。
        //    其余一律**不标** —— 正在输入时按 Ctrl+W/Ctrl+T/Ctrl+B 之类，语义可疑还可能误删。
        new(ShortcutAction.Find, "shortcut.find", new ShortcutGesture("F", Ctrl: true), ShortcutKind.App,
            SafeWhileTyping: true),
        new(ShortcutAction.BatchMode, "shortcut.batch_mode", new ShortcutGesture("B", Ctrl: true), ShortcutKind.App),
        new(ShortcutAction.BatchDelete, "shortcut.batch_delete", new ShortcutGesture("Delete", Shift: true), ShortcutKind.App),

        // ── 新建五类图标（原版菜单里的键位，照抄）──
        new(ShortcutAction.NewFile, "shortcut.new_file", new ShortcutGesture("F", Ctrl: true, Shift: true), ShortcutKind.App),
        new(ShortcutAction.NewFolder, "shortcut.new_folder", new ShortcutGesture("O", Ctrl: true, Shift: true), ShortcutKind.App),
        new(ShortcutAction.NewShortcut, "shortcut.new_shortcut", new ShortcutGesture("L", Ctrl: true, Shift: true), ShortcutKind.App),
        new(ShortcutAction.NewUrl, "shortcut.new_url", new ShortcutGesture("U", Ctrl: true, Shift: true), ShortcutKind.App),
        new(ShortcutAction.NewCommand, "shortcut.new_command", new ShortcutGesture("P", Ctrl: true, Shift: true), ShortcutKind.App),

        // ── 数据 / 退出 ──
        new(ShortcutAction.ResetData, "shortcut.reset_data", new ShortcutGesture("R", Ctrl: true, Shift: true), ShortcutKind.App),
        new(ShortcutAction.Export, "shortcut.export_data", new ShortcutGesture("E", Ctrl: true, Shift: true), ShortcutKind.App),
        new(ShortcutAction.Import, "shortcut.import_data", new ShortcutGesture("I", Ctrl: true, Shift: true), ShortcutKind.App),
        new(ShortcutAction.ExitApp, "shortcut.exit_app", new ShortcutGesture("F4", Alt: true), ShortcutKind.System),
    };

    /// <summary>需要本应用注册并处理的那些（界面照着它建加速器）。</summary>
    public static IEnumerable<ShortcutEntry> AppShortcuts
        => All.Where(entry => entry.Kind == ShortcutKind.App);

    /// <summary>打字时也放行的那些（见 <see cref="ShortcutEntry.SafeWhileTyping"/>）。</summary>
    public static IEnumerable<ShortcutEntry> ShortcutsSafeWhileTyping
        => AppShortcuts.Where(entry => entry.SafeWhileTyping);

    /// <summary>功能名（走文案表 ⇒ 跟随语言）。</summary>
    public static string Label(ShortcutEntry entry) => I18n.T(entry.LabelKey);

    /// <summary>按动作取一行（找不到返回 null）。</summary>
    public static ShortcutEntry? Find(ShortcutAction action)
        => All.FirstOrDefault(entry => entry.Action == action);
}
