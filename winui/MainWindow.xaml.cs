// MainWindow.xaml.cs —— ToolboxPanel v2 · WinUI 3 最小验证样板（W0）
//
// 职责（只做验证，不做正式功能）：
//   1) 窗口级材质：Mica / MicaAlt / DesktopAcrylic(Base/Thin) / 纯色回退，按钮实时切换；
//   2) 自绘标题栏 + ExtendsContentIntoTitleBar，让系统材质能透到标题栏区域；
//   3) 读取**现有** data/tabs.json（v1 旧格式）并列出标签页 —— 走 W1 的 C# 数据层
//      （ToolboxPanel.Core 的 DataStore），不再是样板自己手写的 JSON 解析；
//   4) 自检结果同时写进窗口状态栏和 %TEMP%\toolboxpanel-w0-verify.txt，
//      便于无法看图时也能确认「材质是否受支持 / 数据读到几条」。
//
// ⚠️ 纪律（ERROR.md E5）：本样板**不注入任何合成鼠标/键盘输入**；
//    按钮类交互由用户手动点击确认。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Storage;
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

    /// <summary>样板用到的数据层（只读；不做任何写操作）。</summary>
    private DataStore? _store;

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

    // ────────────────────────────── 读旧数据（走真正的数据层）──────────────────────────────

    /// <summary>
    /// 用 W1 的 C# 数据层（<see cref="DataStore"/>，ToolboxPanel.Core）读取现有 data/tabs.json。
    ///
    /// ⚠️ 样板**只读不写**：不调用任何增删改，避免验证阶段动到用户的真实数据。
    /// （<see cref="DataStore.Load"/> 本身在「文件不存在 / 损坏」时会落一份兜底数据，这是原版行为。）
    /// </summary>
    private void LoadTabsFromLegacyJson()
    {
        try
        {
            _store = DataStore.CreateDefault();
            var tabs = _store.Load();

            var rows = new List<TabRow>(tabs.Count);
            int gridCount = 0, listCount = 0;

            foreach (var tab in tabs)
            {
                if (tab.IsListTab)
                {
                    listCount++;
                }
                else
                {
                    gridCount++;
                }

                rows.Add(new TabRow
                {
                    Name = string.IsNullOrEmpty(tab.Name) ? "(未命名标签页)" : tab.Name,
                    Kind = tab.TabType,
                    Accent = tab.IsListTab
                        ? new SolidColorBrush(Color.FromArgb(255, 63, 167, 214))
                        : new SolidColorBrush(Color.FromArgb(255, 118, 200, 147)),
                    Summary = $"id={ShortId(tab.Id)} · order={tab.Order} · "
                              + $"图标 {tab.Icons.Count} 个 · 列表项 {tab.ListItems.Count} 个"
                              + DescribeListItems(tab),
                });
            }

            TabList.ItemsSource = rows;

            _log.AppendLine($"数据目录 = {_store.DataDirectory}");
            _log.AppendLine($"读到 {rows.Count} 个标签页（grid {gridCount} / list {listCount}）");
            foreach (var row in rows)
            {
                _log.AppendLine($"  - [{row.Kind}] {row.Name} :: {row.Summary}");
            }
        }
        catch (Exception ex)
        {
            _log.AppendLine("数据层读取失败：" + ex.Message);
            App.WriteCrash("LoadTabsFromLegacyJson", ex);
        }

        UpdateStatus();
    }

    /// <summary>把列表页条目也写出来 —— 这样「数据真的读到了」一眼可验。</summary>
    private static string DescribeListItems(TabModel tab)
        => tab.ListItems.Count > 0
            ? $" → [{string.Join(", ", tab.ListItems.Select(item => item.Description))}]"
            : string.Empty;

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

    private static string ShortId(string id) =>
        string.IsNullOrEmpty(id) ? "?" : (id.Length > 8 ? id[..8] : id);
}

/// <summary>
/// 标签页的**展示行**（视图模型，只服务于样板的 ListView 模板）。
/// 真正的数据模型是 <see cref="TabModel"/> / <see cref="IconModel"/> / <see cref="ListItemModel"/>
/// （ToolboxPanel.Core，字段与 tabs.json 一一对应）。
/// </summary>
public sealed class TabRow
{
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public SolidColorBrush Accent { get; init; } = new(Colors.Gray);
}
