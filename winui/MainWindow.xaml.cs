// MainWindow.xaml.cs —— 主窗口（编排层）
//
// 职责（保持薄）：
//   1) 窗口 chrome：自绘标题栏（拖动区）+ 系统标题栏按钮；
//   2) 玻璃材质：按设置套用 Mica / Acrylic / 纯色（材质不生效时补纯色兜底）；
//   3) 装配：数据（MainViewModel）→ 标签栏（TabStripView）→ 内容页（GridPage / ListViewPage）；
//   4) 设置：读 config.json、把设置套用到界面、处理设置面板事件；
//   5) 自检产物：%TEMP%\toolboxpanel-verify.txt（便于"看不了图"时确认程序读到了什么）。
//
// 具体的标签栏交互、设置面板控件、页面动效都在 Views/ 下的各自文件里 —— 本文件不重复实现。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Storage;
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
    private SettingsStore? _settings;
    private AppSettings? _settingsData;

    private string? _backdropOverride;      // --backdrop=
    private TabIconMode? _tabIconOverride;  // --tab-icons=
    private int _startupTabIndex;           // --tab=
    private bool _isDemo;                   // --demo
    private bool _openSettingsAtStartup;    // --open-settings
    private string _backdropLine = "窗口材质：未初始化";
    private string? _transientStatus;

    public MainWindow()
    {
        InitializeComponent();

        Title = "ToolboxPanel";
        AppWindow.Resize(new SizeInt32(1200, 800));

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        CustomizeCaptionButtons();

        ApplyStartupArguments();
        LoadSettings();
        LoadData();
        ApplyAllSettings();

        if (_openSettingsAtStartup)
        {
            ShowSettings(true);
        }
    }

    // ────────────────────────────── 启动参数（开发/验证用）──────────────────────────────

    /// <summary>
    ///   --demo                                纯 UI 演示（假数据、不碰文件、点击不启动）
    ///   --backdrop=mica|micaAlt|acrylic|acrylicThin|none   临时覆盖窗口材质（不落盘）
    ///   --tab-icons=text|always|hover         临时覆盖标签图标形态（不落盘）
    ///   --pos=x,y / --size=WxH / --tab=N      窗口位置、尺寸、初始标签页
    ///   --open-settings                       启动即打开设置面板
    /// </summary>
    private void ApplyStartupArguments()
    {
        SizeInt32? size = null;

        foreach (var argument in Environment.GetCommandLineArgs())
        {
            if (argument.Equals("--demo", StringComparison.OrdinalIgnoreCase))
            {
                _isDemo = true;
            }
            else if (argument.Equals("--open-settings", StringComparison.OrdinalIgnoreCase))
            {
                _openSettingsAtStartup = true;
            }
            else if (argument.StartsWith("--backdrop=", StringComparison.OrdinalIgnoreCase))
            {
                _backdropOverride = argument["--backdrop=".Length..];
            }
            else if (argument.StartsWith("--tab-icons=", StringComparison.OrdinalIgnoreCase))
            {
                _tabIconOverride = AppSettings.ParseTabIconMode(argument["--tab-icons=".Length..]);
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
                    TryMoveWindow(x, y);
                }
            }
        }

        TryResizeWindow(size ?? (_isDemo ? new SizeInt32(880, 560) : new SizeInt32(1200, 800)));
    }

    private void TryMoveWindow(int x, int y)
    {
        try
        {
            AppWindow.Move(new PointInt32(x, y));
        }
        catch (Exception ex)
        {
            App.WriteCrash("TryMoveWindow", ex);
        }
    }

    private void TryResizeWindow(SizeInt32 size)
    {
        try
        {
            AppWindow.Resize(size);
        }
        catch (Exception ex)
        {
            App.WriteCrash("TryResizeWindow", ex);
        }
    }

    // ────────────────────────────── 设置 ──────────────────────────────

    /// <summary>读设置；演示模式把 config.json 写到临时目录，**绝不碰用户真实数据目录**。</summary>
    private void LoadSettings()
    {
        try
        {
            _settings = _isDemo
                ? new SettingsStore(Path.Combine(Path.GetTempPath(), "toolboxpanel-ui-demo"))
                : SettingsStore.CreateDefault();

            _settingsData = _settings.Load();

            // 命令行临时覆盖（**不落盘**，只在本次运行生效）
            if (_tabIconOverride is { } iconMode)
            {
                _settingsData.TabIconMode = iconMode;
            }

            if (_backdropOverride is not null)
            {
                _settingsData.Backdrop = _backdropOverride;
            }

            Settings.Bind(_settings, _settingsData);
            Settings.SettingApplied += (_, _) => ApplyAllSettings();
            Settings.PreviewRequested += (_, _) => (ContentHost.Content as IAnimatedPage)?.PlayEntrance();
            Settings.CloseRequested += (_, _) => ShowSettings(false);

            _log.AppendLine($"设置文件 = {_settings.SettingsFile}");
            _log.AppendLine($"设置 = {DescribeSettings(_settingsData)}");
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.LoadSettings", ex);
            _settingsData = new AppSettings();
        }
    }

    private static string DescribeSettings(AppSettings settings)
        => $"材质={settings.Backdrop} 标签图标={settings.TabIconModeRaw ?? "默认hover"} "
           + $"显示数量={settings.ShowTabCounts} 动效={settings.AnimationsEnabled} "
           + $"{settings.AnimationDurationMs}ms/{settings.AnimationStaggerMs}ms/{AppSettings.ToWire(settings.AnimationEasing)}";

    /// <summary>把当前设置整体套用到界面（幂等：设置一变就整份重套，省掉"改一处忘一处"）。</summary>
    private void ApplyAllSettings()
    {
        var settings = _settingsData ?? new AppSettings();

        ApplyBackdrop(settings.Backdrop);
        TabStrip.ApplySettings(settings.TabIconMode, settings.ShowTabCounts, settings.ToAnimationSpec());

        foreach (var page in _pages.Values.OfType<IAnimatedPage>())
        {
            page.ApplyAnimationSpec(settings.ToAnimationSpec());
        }

        Settings.Refresh();
    }

    private void OnSettingsButtonClick(object sender, RoutedEventArgs e) => ShowSettings(SettingsOverlay.Visibility != Visibility.Visible);

    private void ShowSettings(bool open)
    {
        SettingsOverlay.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Background = open
            ? new SolidColorBrush(Color.FromArgb(30, 255, 255, 255))
            : new SolidColorBrush(Colors.Transparent);
    }

    // ────────────────────────────── 材质 ──────────────────────────────

    private void ApplyBackdrop(string kind)
    {
        SystemBackdrop? backdrop;
        bool supported;
        string label;

        try
        {
            switch (kind)
            {
                case BackdropKinds.None:
                    backdrop = null;
                    supported = true;
                    label = "纯色回退";
                    break;
                case BackdropKinds.MicaAlt:
                    backdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
                    supported = MicaController.IsSupported();
                    label = "Mica (BaseAlt)";
                    break;
                case BackdropKinds.Acrylic:
                    backdrop = new DesktopAcrylicBackdrop();
                    supported = DesktopAcrylicController.IsSupported();
                    label = "Desktop Acrylic (Base)";
                    break;
                case BackdropKinds.AcrylicThin:
                    backdrop = new AcrylicThinBackdrop();
                    supported = DesktopAcrylicController.IsSupported();
                    label = "Desktop Acrylic (Thin)";
                    break;
                default:
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
        BackdropLabel.Text = label;
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
            TabStrip.TabSelected += OnTabSelected;

            _log.AppendLine($"数据目录 = {_viewModel.DataDirectory}");
            _log.AppendLine(_viewModel.StatusText);
            foreach (var tab in _viewModel.Tabs)
            {
                _log.AppendLine($"  - [{tab.Kind}] {tab.Name} :: {tab.CountLabel}");
            }

            if (TabStrip.ItemCount > 0)
            {
                TabStrip.SelectedIndex = Math.Clamp(_startupTabIndex, 0, TabStrip.ItemCount - 1);
                TabStrip.SyncSelection();
                ShowTab(TabStrip.SelectedTab);   // SelectedIndex 未变化时不会触发 SelectionChanged，这里兜底
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

    private void OnTabSelected(object? sender, TabItemViewModel tab) => ShowTab(tab);

    /// <summary>切换内容区；页面只建一次，但**每次切页都重放一次入场动效**。</summary>
    private void ShowTab(TabItemViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        if (!_pages.TryGetValue(tab.Id, out var page))
        {
            page = CreatePage(tab);
            _pages[tab.Id] = page;
            _log.AppendLine($"首次创建页面 = [{tab.Kind}] {tab.Name}（{page.GetType().Name}）");
        }

        ContentHost.Content = page;
        _transientStatus = null;

        if (page is IAnimatedPage animated)
        {
            animated.ApplyAnimationSpec((_settingsData ?? new AppSettings()).ToAnimationSpec());
            animated.PlayEntrance();
        }

        UpdateStatusBar();
    }

    private UIElement CreatePage(TabItemViewModel tab)
    {
        if (tab.IsList)
        {
            var listPage = new ListViewPage(tab);
            listPage.ItemActivated += OnListItemActivated;
            return listPage;
        }

        var gridPage = new GridPage(tab);
        gridPage.IconActivated += OnIconActivated;
        return gridPage;
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
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ToolboxPanel 自检（pid={Environment.ProcessId}）{Environment.NewLine}"
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
