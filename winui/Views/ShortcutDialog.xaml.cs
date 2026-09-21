// ShortcutDialog.xaml.cs —— 「快捷键设置」窗口（v2.0.6：从只读参考升级为可改键 + 冲突管理）
//
// 分工：
//   · **规则**全在 Core：默认键位/功能名（`ShortcutCatalog`）、覆盖解析与四类判据（`ShortcutBindings`）、
//     跨应用占用探测（`HotkeyProbe`）—— 都有单测；
//   · 这里只做三件事：铺行、**采集按键**、把改好的绑定回给宿主窗口（由宿主落盘 + 重新注册加速器）。
//
// ⚠️ 三个实现要点（都是踩过才知道的）：
//   ① **按键采集必须用 `AddHandler(..., handledEventsToo: true)`**：对话框里按钮/输入框会先把键
//      `Handled` 掉，普通 `KeyDown` 订阅收不到 —— 而"按下新快捷键"这件事恰恰要抢在它们前面看到。
//   ② **改键后整体重建列表**（`RowsHost.ItemsSource = BuildRows()`），而不是给每行做 INPC：
//      17 行、重建极便宜，能避开 `x:Bind` 默认 OneTime 的坑（本项目被咬过多次）。
//   ③ 只在**真正改动**时才回调宿主持久化 —— 打开看一圈不该去动用户的 `config.json`。

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using ToolboxPanel.Core;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Views;

/// <summary>列表里的一行（只给 XAML 的 `x:Bind` 用，所以属性名要短；改键后整体重建）。</summary>
public sealed record ShortcutRow(
    ShortcutAction Action,
    string Label,
    string Keys,
    string StatusText,
    Visibility WarningVisibility,
    Visibility ResetVisibility,
    string ResetTip);

public sealed partial class ShortcutDialog : ContentDialog
{
    private IReadOnlyList<ShortcutBinding> _bindings;
    private readonly Dictionary<ShortcutAction, ShortcutIssueInfo> _staticIssues;

    /// <summary>跨应用占用探测的结果（按了「检测冲突」才有；null = 还没测过）。</summary>
    private HashSet<ShortcutAction>? _takenByOtherApp;

    /// <summary>正在录制按键的那一条（null = 没在录制）。</summary>
    private ShortcutAction? _recording;

    private ShortcutDialog(IReadOnlyList<ShortcutBinding> bindings)
    {
        InitializeComponent();

        _bindings = bindings;
        _staticIssues = ShortcutBindings.FindIssues(_bindings).ToDictionary(pair => pair.Key, pair => pair.Value);

        // 标题与按钮文字（ContentDialog 的 Title/按钮不是可视元素，Tr 管不到）
        Title = I18n.T("shortcut.settings.title");
        CloseButtonText = I18n.T("btn.ok");
        CheckButton.Content = I18n.T("shortcut.check");
        ResetAllButton.Content = I18n.T("shortcut.reset_all");
        HintText.Text = I18n.T("shortcut.hint");
        ActionHeader.Text = ShortcutCatalog.ColumnAction;
        KeyHeader.Text = ShortcutCatalog.ColumnKey;
        StatusHeader.Text = I18n.T("shortcut.col.status");

        // ⚠️ 必须 handledEventsToo: true —— 否则按钮/输入框把键 Handled 掉之后我们就看不到了
        AddHandler(KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler(OnAnyKeyDown), handledEventsToo: true);

        RefreshRows();
    }

    /// <summary>绑定被用户改动时通知宿主（宿主负责落盘 + 重新注册加速器）。</summary>
    public event EventHandler<IReadOnlyList<ShortcutBinding>>? BindingsChanged;

    /// <summary>由 MainWindow 调用（它会先把 RequestedTheme 设好再 ShowAsync）。</summary>
    public static ShortcutDialog Create(IEnumerable<ShortcutBinding>? bindings = null)
        => new(bindings?.ToList() ?? ShortcutBindings.Resolve(null));

    // ────────────────────────────── 列表 ──────────────────────────────

    private void RefreshRows() => RowsHost.ItemsSource = BuildRows();

    private List<ShortcutRow> BuildRows()
        => _bindings.Select(binding =>
        {
            var entry = ShortcutCatalog.Find(binding.Action);
            var label = entry is null ? binding.Action.ToString() : ShortcutCatalog.Label(entry);

            // 录制中：那一行的键位按钮显示"请按下新的组合键…"
            var keys = _recording == binding.Action
                ? I18n.T("shortcut.recording")
                : binding.Gesture.Display;

            var (issue, otherKey) = ResolveIssue(binding);
            var isProblem = issue is not (ShortcutIssue.None or ShortcutIssue.TextEditing);

            return new ShortcutRow(
                Action: binding.Action,
                Label: label,
                Keys: keys,
                StatusText: DescribeIssue(issue, otherKey),
                WarningVisibility: isProblem ? Visibility.Visible : Visibility.Collapsed,
                ResetVisibility: IsDefault(binding)
                    ? Visibility.Collapsed
                    : Visibility.Visible,
                ResetTip: I18n.T("shortcut.reset_one"));
        }).ToList();

    /// <summary>取一条绑定的最终问题：跨应用占用（若测过）优先于"输入框编辑键"这类轻微提示。</summary>
    private (ShortcutIssue Issue, string? OtherLabelKey) ResolveIssue(ShortcutBinding binding)
    {
        if (_takenByOtherApp is not null && _takenByOtherApp.Contains(binding.Action))
        {
            return (ShortcutIssue.TakenByOtherApp, null);
        }

        return _staticIssues.TryGetValue(binding.Action, out var info)
            ? (info.Issue, info.OtherLabelKey)
            : (ShortcutIssue.None, null);
    }

    private static bool IsDefault(ShortcutBinding binding)
        => string.Equals(binding.Gesture.Display,
            ShortcutBindings.DefaultGesture(binding.Action).Display, StringComparison.OrdinalIgnoreCase);

    private static string DescribeIssue(ShortcutIssue issue, string? otherLabelKey)
        => issue switch
        {
            ShortcutIssue.Duplicate => I18n.T("shortcut.status.duplicate",
                ("name", otherLabelKey is null ? "?" : I18n.T(otherLabelKey))),
            ShortcutIssue.TextEditing => I18n.T("shortcut.status.text_editing"),
            ShortcutIssue.Reserved => I18n.T("shortcut.status.reserved"),
            ShortcutIssue.TakenByOtherApp => I18n.T("shortcut.status.taken"),
            ShortcutIssue.NoModifier => I18n.T("shortcut.status.no_modifier"),
            _ => I18n.T("shortcut.status.ok"),
        };

    // ────────────────────────────── 改键（采集按键） ──────────────────────────────

    private void OnKeyButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ShortcutAction action })
        {
            return;
        }

        // 再点一次同一条 = 取消录制
        _recording = _recording == action ? null : action;
        MessageText.Text = _recording is null ? string.Empty : I18n.T("shortcut.recording");
        RefreshRows();
    }

    private void OnAnyKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (_recording is not { } action)
        {
            return;
        }

        e.Handled = true;

        if (e.Key == VirtualKey.Escape)
        {
            _recording = null;
            MessageText.Text = string.Empty;
            RefreshRows();
            return;
        }

        // 只按修饰键时继续等真正的主键（用户按住 Ctrl 再按 T，先到的是 Ctrl）
        if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
            or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            return;
        }

        var gesture = new ShortcutGesture(
            e.Key.ToString(),
            Ctrl: IsDown(VirtualKey.Control),
            Shift: IsDown(VirtualKey.Shift),
            Alt: IsDown(VirtualKey.Menu));

        ApplyGesture(action, gesture);
    }

    private static bool IsDown(VirtualKey key)
        => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(
            Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>
    /// 把某个动作的键位改成 <paramref name="gesture"/>（**允许**非法组合，只提示 —— 用户 2026-09-19 的决定）。
    /// <para>`internal`：自检探针走的就是这条真实链路（等价于"用户按下了这个组合"），不必注入键盘。</para>
    /// </summary>
    internal void ApplyGesture(ShortcutAction action, ShortcutGesture gesture)
    {
        _bindings = _bindings
            .Select(binding => binding.Action == action ? binding with { Gesture = gesture } : binding)
            .ToList();

        _recording = null;

        // 改完键位，"跨应用占用"的旧结论就作废了（那个结论是针对旧组合的）
        _takenByOtherApp = null;

        RecomputeStaticIssues();
        RefreshRows();

        var entry = ShortcutCatalog.Find(action);
        MessageText.Text = I18n.T("shortcut.changed",
            ("name", entry is null ? action.ToString() : ShortcutCatalog.Label(entry)),
            ("keys", gesture.Display));

        BindingsChanged?.Invoke(this, _bindings);
    }

    private void RecomputeStaticIssues()
    {
        _staticIssues.Clear();
        foreach (var (action, info) in ShortcutBindings.FindIssues(_bindings))
        {
            _staticIssues[action] = info;
        }
    }

    // ────────────────────────────── 恢复默认 ──────────────────────────────

    private void OnResetOneClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ShortcutAction action })
        {
            return;
        }

        _bindings = ShortcutBindings.RestoreDefault(_bindings, action);
        _takenByOtherApp = null;
        RecomputeStaticIssues();
        RefreshRows();

        MessageText.Text = I18n.T("shortcut.changed",
            ("name", NameOf(action)),
            ("keys", ShortcutBindings.DefaultGesture(action).Display));

        BindingsChanged?.Invoke(this, _bindings);
    }

    /// <summary>动作名（文案来自 Core 的目录；目录里查不到就退回枚举名，绝不 NRE）。</summary>
    private static string NameOf(ShortcutAction action)
        => ShortcutCatalog.Find(action) is { } entry ? ShortcutCatalog.Label(entry) : action.ToString();

    private void OnResetAllClick(object sender, RoutedEventArgs e) => ResetAllBindings();

    /// <summary>全部恢复默认（`internal`：自检探针共用同一条链路）。</summary>
    internal void ResetAllBindings()
    {
        // 回到"全默认"= 空覆盖表（差分存储：恢复默认就是从覆盖里删掉，而不是写一条等于默认的值）
        _bindings = ShortcutBindings.Resolve(null);
        _takenByOtherApp = null;
        RecomputeStaticIssues();
        RefreshRows();

        MessageText.Text = I18n.T("shortcut.reset_done");
        BindingsChanged?.Invoke(this, _bindings);
    }

    // ────────────────────────────── 冲突检测（跨应用） ──────────────────────────────

    private void OnCheckClick(object sender, RoutedEventArgs e) => RunConflictCheck();

    /// <summary>
    /// 逐条探测"是否已被其他程序的全局热键占用"（`internal`：自检探针共用同一条链路，
    /// 顺带在真实 UI 进程里跑一遍 `RegisterHotKey` 那段 P/Invoke）。
    /// </summary>
    internal void RunConflictCheck()
    {
        var taken = new HashSet<ShortcutAction>();
        var probed = 0;

        foreach (var binding in _bindings)
        {
            // ⚠️ 2026-09-21 修：原来这里把 `HotkeyProbe.Check` 调了两次（每次都是一次 `RegisterHotKey` P/Invoke），
            //    `probed` 与 `taken` 可能基于两次不同的结果。现在**一次取值再判**。
            var state = HotkeyProbe.Check(binding.Gesture);
            if (state == HotkeyAvailability.Available)
            {
                probed++;
            }
            else if (state == HotkeyAvailability.TakenByOtherApp)
            {
                probed++;
                taken.Add(binding.Action);
            }
        }

        _takenByOtherApp = taken;
        RefreshRows();

        MessageText.Text = taken.Count == 0
            ? I18n.T("shortcut.check_clean")
            : I18n.T("shortcut.check_done", ("total", probed), ("issues", taken.Count));
    }
}
