// HotkeyProbe.cs —— 「这个组合是不是已经被**别的程序**的全局热键占用了？」（实测，不猜）
//
// 为什么需要它（用户 2026-09-19 选定的冲突检测口径）：
//   本项目的快捷键是**窗口级**的，不去别的软件里抢键；但反过来说 ——
//   若别的软件用 `RegisterHotKey` 注册了同一个组合，**系统会把键截走给那个软件**，
//   我们的窗口根本收不到 ⇒ 我们的快捷键就"按不出来"。这是真实会发生、也真实需要提示的一类冲突。
//
// 探测方法（**不改我们自己的机制**）：
//   试着 `RegisterHotKey` 一下 —— 成功 ⇒ 没人占（立刻 `UnregisterHotKey` 还回去）；
//   失败且错误码是 `ERROR_HOTKEY_ALREADY_REGISTERED(1409)` ⇒ 已被别的程序占用。
//
// ⚠️ 三条纪律：
//   ① **一定**要立刻注销：探测本身不能真的占住全局热键（不然我们会把用户的键抢走）；
//   ② 用**独立的探测 id**，与任何真实注册都不共用；
//   ③ 传 `hWnd = IntPtr.Zero` ⇒ 注册到**当前线程**上，不需要窗口句柄，Core 层就能独立完成。

using System.Runtime.InteropServices;

namespace ToolboxPanel.Core.Services;

/// <summary>一次探测的结论。</summary>
public enum HotkeyAvailability
{
    /// <summary>没人占（探测用的注册已经注销还回去了）。</summary>
    Available,

    /// <summary>已被其他程序的全局热键占用 —— 本应用里这个组合很可能按不出来。</summary>
    TakenByOtherApp,

    /// <summary>探测不了（键名不是合法的虚拟键，或系统拒绝），**不当成冲突**。</summary>
    Unsupported,
}

/// <summary>全局热键占用探测（纯 Win32，无 UI 依赖）。</summary>
public static class HotkeyProbe
{
    // RegisterHotKey 的修饰键位
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    /// <summary>探测专用的 id（与任何真实注册都不共用）。</summary>
    private const int ProbeId = 0x7B0B;   // 随便挑的、不与常规用途冲突的值

    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    /// <summary>
    /// 探测 <paramref name="gesture"/> 是否已被别的程序占用。
    /// <para>⚠️ 内部"注册 → 立刻注销"，**不会**真的占住这个组合。</para>
    /// </summary>
    public static HotkeyAvailability Check(ShortcutGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        if (!TryToVirtualKey(gesture.Key, out var key))
        {
            return HotkeyAvailability.Unsupported;
        }

        var modifiers = Modifiers(gesture);
        if (modifiers == 0)
        {
            // 没有修饰键的组合**不是**全局热键（RegisterHotKey 会拒绝），
            // 这种"没被占用"的结论没有意义 ⇒ 明确返回探测不了，别让界面画一个假的绿勾。
            return HotkeyAvailability.Unsupported;
        }

        try
        {
            if (RegisterHotKey(IntPtr.Zero, ProbeId, modifiers, key))
            {
                // ★ 立刻还回去 —— 探测绝不能真的占住用户的键
                UnregisterHotKey(IntPtr.Zero, ProbeId);
                return HotkeyAvailability.Available;
            }

            var error = Marshal.GetLastWin32Error();

            // 失败时也尝试注销一次（万一注册其实成功了、只是返回 false 的极端情况）
            UnregisterHotKey(IntPtr.Zero, ProbeId);

            return error == ERROR_HOTKEY_ALREADY_REGISTERED
                ? HotkeyAvailability.TakenByOtherApp
                : HotkeyAvailability.Unsupported;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return HotkeyAvailability.Unsupported;
        }
    }

    /// <summary>把键位描述转成 Win32 虚拟键码（认不出返回 false）。</summary>
    internal static bool TryToVirtualKey(string? key, out uint virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var name = key.Trim().ToUpperInvariant();

        // F1–F24
        if (name.Length >= 2 && name[0] == 'F' && int.TryParse(name[1..], out var fn) && fn is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + fn - 1);   // VK_F1 = 0x70
            return true;
        }

        switch (name)
        {
            case "TAB": virtualKey = 0x09; return true;
            case "ESCAPE" or "ESC": virtualKey = 0x1B; return true;
            case "SPACE": virtualKey = 0x20; return true;
            case "PAGEUP": virtualKey = 0x21; return true;
            case "PAGEDOWN": virtualKey = 0x22; return true;
            case "END": virtualKey = 0x23; return true;
            case "HOME": virtualKey = 0x24; return true;
            case "LEFT": virtualKey = 0x25; return true;
            case "UP": virtualKey = 0x26; return true;
            case "RIGHT": virtualKey = 0x27; return true;
            case "DOWN": virtualKey = 0x28; return true;
            case "INSERT": virtualKey = 0x2D; return true;
            case "DELETE" or "DEL": virtualKey = 0x2E; return true;
            case "BACK" or "BACKSPACE": virtualKey = 0x08; return true;
        }

        // 单个字母 / 数字（`ShortcutGesture.Key` 用的是 W3C 风格的大写单字符）
        if (name.Length == 1)
        {
            var ch = name[0];
            if (ch is >= 'A' and <= 'Z')
            {
                virtualKey = ch;                 // VK_A..VK_Z == 'A'..'Z'
                return true;
            }

            if (ch is >= '0' and <= '9')
            {
                virtualKey = ch;                 // VK_0..VK_9 == '0'..'9'
                return true;
            }
        }

        return false;
    }

    private static uint Modifiers(ShortcutGesture gesture)
    {
        uint modifiers = MOD_NOREPEAT;

        if (gesture.Ctrl)
        {
            modifiers |= MOD_CONTROL;
        }

        if (gesture.Shift)
        {
            modifiers |= MOD_SHIFT;
        }

        if (gesture.Alt)
        {
            modifiers |= MOD_ALT;
        }

        return modifiers == MOD_NOREPEAT ? 0 : modifiers;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
