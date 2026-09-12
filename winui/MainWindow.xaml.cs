// MainWindow.xaml.cs —— W3 主界面（真实窗口）
//
// 职责：
//   1) 玻璃：Mica（只支持 Win11，用户已定）＋ 不生效时纯色兜底；
//   2) 自绘标题栏（拖动区）＋ 系统最小化/最大化/关闭按钮；
//   3) 标签栏切页：网格页（grid）/ 列表页（list）；
//   4) 点击图标/列表项 → 用 Core 的 Launcher 真正打开，并把结果显示在状态栏；
//   5) 自检产物：%TEMP%\toolboxpanel-verify.txt（载入结果 + 材质 + 窗口矩形），
//      便于「不开窗口/看不了图」时也能确认程序读到了什么。
//
// ⚠️ 纪律（ERROR.md E5）：本程序不会注入任何合成输入；验证交互一律交用户手点。
// ⚠️ 开发期验证请用环境变量 TOOLBOXPANEL_DATA_DIR 指向**临时数据目录**，
//    避免动到你的真实 data/tabs.json（AppPaths 支持这个变量）。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ToolboxPanel.Core.Models;
using ToolboxPanel.ViewModels;
using ToolboxPanel.Views;
using Windows.Graphics;
using Windows.UI;

namespace ToolboxPanel;

public sealed partial class MainWindow : Window
{
    private static readonly string VerifyLogPath =
        Path.Combine(Path.GetTempPath(), "toolboxpanel-verify.txt");

    private readonly StringBuilder _log = new();
    private readonly Dictionary<string, UIElement> _pages = new(StringComparer.Ordinal);

    private MainViewModel? _viewModel;
    private string _backdropLine = "窗口材质：未初始化";
    private string? _transientStatus;

    /// <summary>启动时选中的标签页下标（--tab=N；验证用，默认 0）。</summary>
    private int _startupTabIndex;

    /// <summary>纯 UI 演示模式（--demo）：假数据、不读写任何数据文件、点击不启动程序。</summary>
    private bool _isDemo;

    public MainWindow()
    {
        InitializeComponent();

        Title = "ToolboxPanel";

        // 先按默认尺寸开，启动参数里给了 --size 再覆盖（演示模式默认更小更紧凑）
        AppWindow.Resize(new SizeInt32(1200, 800));

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        CustomizeCaptionButtons();

        ApplyStartupArguments();   // --demo / --backdrop= / --pos= / --size= / --tab=
        LoadData();
    }

    // ────────────────────────────── 材质 / 启动参数 ──────────────────────────────

    /// <summary>
    /// 启动参数（验证/开发用）：
    ///   --demo                                             纯 UI 演示（假数据、不碰文件、点击不启动）
    ///   --backdrop=none|mica|micaAlt|acrylic|acrylicThin   指定初始材质
    ///   --pos=x,y                                          指定窗口位置
    ///   --size=WxH                                         指定窗口尺寸
    ///   --tab=N                                            指定初始选中的标签页下标
    /// </summary>
    private void ApplyStartupArguments()
    {
        string backdrop = "mica";
        SizeInt32? size = null;

        foreach (var argument in Environment.GetCommandLineArgs())
        {
            if (argument.Equals("--demo", StringComparison.OrdinalIgnoreCase))
            {
                _isDemo = true;
            }
            else if (argument.StartsWith("--backdrop=", StringComparison.OrdinalIgnoreCase))
            {
                backdrop = argument["--backdrop=".Length..];
            }
            else if (argument.StartsWith("--size=", StringComparison.OrdinalIgnoreCase))
            {
                var parts = argument["--size=".Length..].Split('x', 'X');
                if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                {
                    size = new SizeInt32(Math.Max(360, w), Math.Max(320, h));
                }
            }
            else if (argument.StartsWith("--tab=", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(argument["--tab=".Length..], out int tabIndex))
            {
                _startupTabIndex = Math.Max(0, tabIndex);
            }
            else if (argument.StartsWith("--pos=", StringComparison.OrdinalIgnoreCase))
            {
                var parts = argument["--pos=".Length..].Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                {
                    try
                    {
                        AppWindow.Move(new PointInt32(x, y));
                    }
                    catch (Exception ex)
                    {
                        App.WriteCrash("ApplyStartupArguments/pos", ex);
                    }
                }
            }
        }

        // 尺寸：给了 --size 用它；演示模式给一个更紧凑、贴合内容的默认值；否则 1200x800
        var targetSize = size ?? (_isDemo ? new SizeInt32(880, 560) : new SizeInt32(1200, 800));
        try
        {
            AppWindow.Resize(targetSize);
        }
        catch (Exception ex)
        {
            App.WriteCrash("ApplyStartupArguments/size", ex);
        }

        ApplyBackdrop(backdrop);
    }

    private void ApplyBackdrop(string kind)
    {
        SystemBackdrop? backdrop;
        bool supported;
        string label;

        try
        {
            switch (kind)
            {
                case "none":
                    backdrop = null;
                    supported = true;
                    label = "纯色回退";
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
                default:   // mica —— 用户已定「只支持 Win11，用 Mica」
                    backdrop = new MicaBackdrop { Kind = MicaKind.Base };
                    supported = MicaController.IsSupported();
                    label = "Mica (Base)";
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

        // 材质生效时根容器必须完全透明；否则补一层不透明兜底底色（ERROR.md E9）
        bool glass = backdrop is not null && supported;
        RootGrid.Background = glass
            ? new SolidColorBrush(Colors.Transparent)
            : new SolidColorBrush(Color.FromArgb(255, 32, 32, 37));

        _backdropLine = $"窗口材质 = {label}；本机支持 = {supported}；启用 = {glass}";
        if (BackdropLabel is not null)
        {
            BackdropLabel.Text = label;
        }

        UpdateStatusBar();
    }

    /// <summary>标题栏按钮也透出材质（否则右上角是一块实心主题色）。</summary>
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

    // ────────────────────────────── 数据与页面 ──────────────────────────────

    private void LoadData()
    {
        try
        {
            _viewModel = _isDemo ? MainViewModel.CreateDemo() : new MainViewModel();
            _viewModel.Load();

            if (_isDemo)
            {
                Title = "ToolboxPanel · 纯 UI 演示";
            }

            TabStrip.ItemsSource = _viewModel.Tabs;

            _log.AppendLine($"数据目录 = {_viewModel.DataDirectory}");
            _log.AppendLine(_viewModel.StatusText);

            foreach (var tab in _viewModel.Tabs)
            {
                _log.AppendLine($"  - [{tab.Kind}] {tab.Name} :: {tab.Summary}");
            }

            if (TabStrip.Items.Count > 0)
            {
                // 触发 OnTabSelectionChanged → 显示对应页
                TabStrip.SelectedIndex = Math.Clamp(_startupTabIndex, 0, TabStrip.Items.Count - 1);
            }
            else
            {
                ContentHost.Content = null;
            }
        }
        catch (Exception ex)
        {
            _log.AppendLine("数据载入失败：" + ex.Message);
            App.WriteCrash("MainWindow.LoadData", ex);
        }

        UpdateStatusBar();
        FlushLog();
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabStrip.SelectedItem is TabItemViewModel tab)
        {
            ShowTab(tab);
        }
    }

    /// <summary>切换内容区；每个标签页的页面只建一次（保留滚动位置等状态）。</summary>
    private void ShowTab(TabItemViewModel tab)
    {
        if (!_pages.TryGetValue(tab.Id, out var page))
        {
            if (tab.IsList)
            {
                var listPage = new ListViewPage(tab);
                listPage.ItemActivated += OnListItemActivated;
                page = listPage;
            }
            else
            {
                var gridPage = new GridPage(tab);
                gridPage.IconActivated += OnIconActivated;
                page = gridPage;
            }

            _pages[tab.Id] = page;
        }

        ContentHost.Content = page;
        _transientStatus = null;
        _log.AppendLine($"显示标签页 = [{tab.Kind}] {tab.Name}（selectedIndex={TabStrip.SelectedIndex}，页面={page.GetType().Name}）");
        UpdateStatusBar();
    }

    // ────────────────────────────── 打开动作 ──────────────────────────────

    private void OnIconActivated(object? sender, IconModel icon)
    {
        if (_viewModel is null)
        {
            return;
        }

        var result = _viewModel.Launch(icon);
        var name = string.IsNullOrWhiteSpace(icon.DisplayName) ? icon.SourcePath : icon.DisplayName;
        _transientStatus = result.Success ? $"已打开：{name}" : $"打开失败（{name}）：{result.Error}";
        UpdateStatusBar();
        FlushLog();
    }

    private void OnListItemActivated(object? sender, ListItemModel item)
    {
        if (_viewModel is null)
        {
            return;
        }

        var result = _viewModel.LaunchListItem(item);
        _transientStatus = result.Success
            ? $"已打开：{item.Path}"
            : $"打开失败（{item.Path}）：{result.Error}";
        UpdateStatusBar();
        FlushLog();
    }

    // ────────────────────────────── 状态栏 / 自检 ──────────────────────────────

    private void UpdateStatusBar()
    {
        if (StatusText is null)
        {
            return;
        }

        // 状态栏单行：材质已经显示在标题栏上，这里不再重复（省出横向空间）
        var parts = new List<string>();
        if (_viewModel is not null)
        {
            parts.Add(_viewModel.StatusText);
        }

        if (!string.IsNullOrEmpty(_transientStatus))
        {
            parts.Add(_transientStatus);
        }

        StatusText.Text = string.Join("   ·   ", parts);
    }

    private void FlushLog()
    {
        try
        {
            string rect = "?";
            try
            {
                var position = AppWindow.Position;
                var size = AppWindow.Size;
                rect = $"{position.X},{position.Y} {size.Width}x{size.Height}";
            }
            catch
            {
                // 位置拿不到不影响日志
            }

            File.WriteAllText(
                VerifyLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ToolboxPanel W3 自检（pid={Environment.ProcessId}）{Environment.NewLine}"
                + $"窗口矩形 = {rect}{Environment.NewLine}"
                + _backdropLine + Environment.NewLine
                + _log
                + (string.IsNullOrEmpty(_transientStatus) ? string.Empty : _transientStatus + Environment.NewLine),
                Encoding.UTF8);
        }
        catch
        {
            // 写不了日志不影响界面
        }
    }
}
