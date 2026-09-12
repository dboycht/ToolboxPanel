// MainWindow.xaml.cs —— ToolboxPanel v2 · WinUI 3 最小验证样板（W0）
//
// 职责（只做验证，不做正式功能）：
//   1) 窗口级材质：Mica / MicaAlt / DesktopAcrylic(Base/Thin) / 纯色回退，按钮实时切换；
//   2) 自绘标题栏 + ExtendsContentIntoTitleBar，让系统材质能透到标题栏区域；
//   3) 读取**现有** data/tabs.json（v1 旧格式）并列出标签页 —— 字段一个都不改；
//   4) 自检结果同时写进窗口状态栏和 %TEMP%\toolboxpanel-w0-verify.txt，
//      便于无法看图时也能确认「材质是否受支持 / 数据读到几条」。
//
// ⚠️ 纪律（ERROR.md E5）：本样板**不注入任何合成鼠标/键盘输入**；
//    按钮类交互由用户手动点击确认。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace ToolboxPanel;

public sealed partial class MainWindow : Window
{
    /// <summary>自检产物路径：材质支持情况 + tabs.json 读取结果。</summary>
    private static readonly string VerifyLogPath =
        Path.Combine(Path.GetTempPath(), "toolboxpanel-w0-verify.txt");

    private readonly StringBuilder _log = new();
    private string _backdropLine = "窗口材质：未初始化";

    public MainWindow()
    {
        InitializeComponent();

        Title = "ToolboxPanel · WinUI 3 样板（W0）";
        AppWindow.Resize(new SizeInt32(1180, 720));

        // 自绘标题栏：材质才能透到标题栏区域
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        CustomizeCaptionButtons();

        ApplyStartupArgs();      // --backdrop=<kind> / --pos=x,y（给 A/B 自检用）
        LoadTabsFromLegacyJson();
        FlushLog();
    }

    /// <summary>
    /// 启动参数（仅样板自检用）：
    ///   --backdrop=none|mica|micaAlt|acrylic|acrylicThin   指定初始材质
    ///   --pos=x,y                                          指定窗口位置（像素）
    /// 用途：同一位置连续跑两次（一次纯色回退、一次材质），**不用移动窗口**即可做
    /// 「透明区 vs 不透明面板」的对照实验（ERROR.md E8/E9 的判据）。
    /// </summary>
    private void ApplyStartupArgs()
    {
        string backdrop = "mica";

        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg.StartsWith("--backdrop=", StringComparison.OrdinalIgnoreCase))
            {
                backdrop = arg["--backdrop=".Length..];
            }
            else if (arg.StartsWith("--pos=", StringComparison.OrdinalIgnoreCase))
            {
                var parts = arg["--pos=".Length..].Split(',');
                if (parts.Length == 2
                    && int.TryParse(parts[0], out int x)
                    && int.TryParse(parts[1], out int y))
                {
                    try
                    {
                        AppWindow.Move(new PointInt32(x, y));
                    }
                    catch (Exception ex)
                    {
                        App.WriteCrash("ApplyStartupArgs/pos", ex);
                    }
                }
            }
        }

        ApplyBackdrop(backdrop);
    }

    // ────────────────────────────── 窗口级材质 ──────────────────────────────

    /// <summary>按名称切换窗口材质（kind: none / mica / micaAlt / acrylic / acrylicThin）。</summary>
    private void ApplyBackdrop(string kind)
    {
        SystemBackdrop? backdrop;
        bool supported;
        string label;

        try
        {
            // ⚠️ 用 switch 语句而非 switch 表达式：三个分支类型没有共同「最佳类型」
            //    （C# 不会自动去找公共基类），用 switch 表达式会报 CS8506/CS8131。
            switch (kind)
            {
                case "mica":
                    backdrop = new MicaBackdrop { Kind = MicaKind.Base };
                    supported = MicaController.IsSupported();
                    label = "Mica (Base)";
                    break;
                case "micaAlt":
                    backdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
                    supported = MicaController.IsSupported();
                    label = "Mica (BaseAlt)";
                    break;
                case "acrylic":
                    backdrop = new DesktopAcrylicBackdrop();
                    supported = DesktopAcrylicController.IsSupported();
                    label = "Desktop Acrylic (Base)";
                    break;
                case "acrylicThin":
                    backdrop = new AcrylicThinBackdrop();
                    supported = DesktopAcrylicController.IsSupported();
                    label = "Desktop Acrylic (Thin)";
                    break;
                default:
                    backdrop = null;
                    supported = true;
                    label = "纯色回退（无材质）";
                    break;
            }
        }
        catch (Exception ex)
        {
            (backdrop, supported, label) = (null, false, "构造失败：" + ex.Message);
            App.WriteCrash("ApplyBackdrop/" + kind, ex);
        }

        try
        {
            SystemBackdrop = backdrop;
        }
        catch (Exception ex)
        {
            supported = false;
            App.WriteCrash("SetSystemBackdrop/" + kind, ex);
        }

        // 材质生效时根容器必须完全透明（否则把材质盖住 → ERROR.md E9）；
        // 不支持/纯色模式才补一层不透明兜底底色，保证文字可读。
        bool glass = backdrop is not null && supported;
        RootGrid.Background = glass
            ? new SolidColorBrush(Colors.Transparent)
            : new SolidColorBrush(Color.FromArgb(255, 32, 32, 37));

        _backdropLine = $"窗口材质 = {label}；本机支持 = {supported}；启用 = {glass}";
        UpdateStatus();
    }

    private void OnBackdropClick(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Button { Tag: string tag })
        {
            ApplyBackdrop(tag);
            FlushLog();
        }
    }

    /// <summary>让标题栏的最小化/最大化/关闭按钮也透出材质（否则那一块是实心主题色）。</summary>
    private void CustomizeCaptionButtons()
    {
        try
        {
            if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            var bar = AppWindow.TitleBar;
            bar.ButtonBackgroundColor = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
            bar.ButtonForegroundColor = Colors.White;
            bar.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
            bar.ButtonHoverBackgroundColor = Color.FromArgb(40, 255, 255, 255);
            bar.ButtonPressedBackgroundColor = Color.FromArgb(60, 255, 255, 255);
        }
        catch (Exception ex)
        {
            App.WriteCrash("CustomizeCaptionButtons", ex);
        }
    }

    // ────────────────────────────── 读旧数据 ──────────────────────────────

    /// <summary>
    /// 读取 v1 版 data/tabs.json。为了「字段一字不改」，这里只做**只读**解析，
    /// 不写回、不迁移。真实数据层在 W1 用 C# 重写（原子写 + 损坏容错 + 增删改查）。
    /// </summary>
    private void LoadTabsFromLegacyJson()
    {
        var path = FindLegacyTabsJson();
        if (path is null)
        {
            _log.AppendLine("tabs.json = 未找到（从 exe 目录向上 10 层内查找 data/tabs.json）");
            UpdateStatus();
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            int version = root.TryGetProperty("version", out var v) && v.TryGetInt32(out var vi) ? vi : 0;
            var rows = new List<TabRow>();
            int gridCount = 0, listCount = 0;

            if (root.TryGetProperty("tabs", out var tabs) && tabs.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tabs.EnumerateArray())
                {
                    string name = GetString(t, "name");
                    string tabType = GetString(t, "tab_type");
                    if (string.IsNullOrEmpty(tabType))
                    {
                        tabType = "grid";   // 旧 JSON 无 tab_type → 按 grid（向后兼容）
                    }

                    int iconCount = ArrayLength(t, "icons");
                    int itemCount = ArrayLength(t, "list_items");
                    if (tabType == "list") { listCount++; } else { gridCount++; }

                    rows.Add(new TabRow
                    {
                        Name = string.IsNullOrEmpty(name) ? "(未命名标签页)" : name,
                        Kind = tabType,
                        Accent = tabType == "list"
                            ? new SolidColorBrush(Color.FromArgb(255, 63, 167, 214))
                            : new SolidColorBrush(Color.FromArgb(255, 118, 200, 147)),
                        Summary = $"id={ShortId(GetString(t, "id"))} · order={GetInt(t, "order")} · "
                                  + $"图标 {iconCount} 个 · 列表项 {itemCount} 个"
                                  + DescribeListItems(t),
                    });
                }
            }

            TabList.ItemsSource = rows;

            _log.AppendLine($"tabs.json 路径 = {path}");
            _log.AppendLine($"tabs.json version = {version}；读到 {rows.Count} 个标签页（grid {gridCount} / list {listCount}）");
            foreach (var r in rows)
            {
                _log.AppendLine($"  - [{r.Kind}] {r.Name} :: {r.Summary}");
            }
        }
        catch (Exception ex)
        {
            _log.AppendLine("tabs.json 解析失败：" + ex.Message);
            App.WriteCrash("LoadTabsFromLegacyJson", ex);
        }

        UpdateStatus();
    }

    /// <summary>把列表页条目也写出来 —— 这样「数据真的读到了」一眼可验。</summary>
    private static string DescribeListItems(JsonElement tab)
    {
        if (!tab.TryGetProperty("list_items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var it in items.EnumerateArray())
        {
            parts.Add(GetString(it, "description"));
        }

        return parts.Count > 0 ? $" → [{string.Join(", ", parts)}]" : string.Empty;
    }

    private static string? FindLegacyTabsJson()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data", "tabs.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // ────────────────────────────── 小工具 ──────────────────────────────

    private void UpdateStatus()
    {
        StatusText.Text =
            "自检：" + _backdropLine + Environment.NewLine
            + "数据：" + (_log.Length > 0
                ? _log.ToString().Replace("\r\n", " ").Replace("\n", " ").Trim()
                : "正在读取…")
            + Environment.NewLine
            + $"（完整结果见 {VerifyLogPath}）";
    }

    private void FlushLog()
    {
        try
        {
            string rect = "?";
            try
            {
                var pos = AppWindow.Position;
                var size = AppWindow.Size;
                rect = $"{pos.X},{pos.Y} {size.Width}x{size.Height}";
            }
            catch
            {
                // 位置拿不到不影响日志
            }

            File.WriteAllText(
                VerifyLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ToolboxPanel W0 自检（pid={Environment.ProcessId}）{Environment.NewLine}"
                + $"窗口矩形 = {rect}{Environment.NewLine}"
                + _backdropLine + Environment.NewLine
                + _log,
                Encoding.UTF8);
        }
        catch
        {
            // 写不了日志不影响样板运行
        }
    }

    private static string GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static int GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : -1;

    private static int ArrayLength(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.GetArrayLength()
            : 0;

    private static string ShortId(string id) =>
        string.IsNullOrEmpty(id) ? "?" : (id.Length > 8 ? id[..8] : id);
}

/// <summary>
/// 标签页行（仅样板用；字段名与 tabs.json 对齐，方便对照）。
/// 正式模型在 W1：移植 models/tab_model.py 等，字段一个都不改。
/// </summary>
public sealed class TabRow
{
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public SolidColorBrush Accent { get; init; } = new(Colors.Gray);
}
