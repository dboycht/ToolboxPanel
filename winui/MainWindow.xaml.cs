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
using System.Linq;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Storage;
using ToolboxPanel.ViewModels;
using ToolboxPanel.Views;
using Windows.Graphics;
using Windows.UI;

namespace ToolboxPanel;

public sealed partial class MainWindow : Window
{
    /// <summary>标签图标槽展开后的宽度（DIP）。"整体拉伸"就是标签宽度跟着这个值变化。</summary>
    private const double TabGlyphWidth = 16;

    private static readonly string VerifyLogPath =
        Path.Combine(Path.GetTempPath(), "toolboxpanel-verify.txt");

    private readonly StringBuilder _log = new();
    private readonly Dictionary<string, UIElement> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<TabItemViewModel, Border> _tabGlyphHosts = new();
    private readonly Dictionary<TabItemViewModel, Storyboard> _tabGlyphAnimations = new();

    private MainViewModel? _viewModel;
    private SettingsStore? _settings;
    private AppSettings? _settingsData;
    private bool _syncingSettingsUi;
    private string _backdropLine = "窗口材质：未初始化";
    private string? _transientStatus;

    /// <summary>启动时选中的标签页下标（--tab=N；验证用，默认 0）。</summary>
    private int _startupTabIndex;

    /// <summary>纯 UI 演示模式（--demo）：假数据、不读写任何数据文件、点击不启动程序。</summary>
    private bool _isDemo;

    /// <summary>--tab-icons=text|always|hover：本次运行临时覆盖标签图标形态（不写配置文件，供验证用）。</summary>
    private TabIconMode? _tabIconModeOverride;

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
        LoadSettings();
        LoadData();
        ApplyAnimationSetting();
    }

    // ────────────────────────────── 设置（config.json）──────────────────────────────

    /// <summary>
    /// 读设置。演示模式把 config.json 写到临时目录，**绝不碰用户真实数据目录**。
    /// </summary>
    private void LoadSettings()
    {
        try
        {
            _settings = _isDemo
                ? new SettingsStore(Path.Combine(Path.GetTempPath(), "toolboxpanel-ui-demo"))
                : SettingsStore.CreateDefault();

            _settingsData = _settings.Load();

            // 验证用：本次运行临时覆盖（**不落盘**，配置文件的真实值不受影响）
            if (_tabIconModeOverride is { } overridden)
            {
                _settingsData.TabIconMode = overridden;
            }

            _log.AppendLine($"设置文件 = {_settings.SettingsFile}");
            _log.AppendLine($"标签图标形态 = {_settingsData.TabIconModeRaw ?? "(未设置→默认 hover)"}"
                            + $"；动效 = {_settingsData.AnimationsEnabled}");
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.LoadSettings", ex);
            _settingsData = new AppSettings();
        }

        SyncSettingsUi();
    }

    /// <summary>把设置值刷到设置面板控件上（加锁标志避免触发变更回调）。</summary>
    private void SyncSettingsUi()
    {
        if (TabIconModeChoices is null || AnimationSwitch is null)
        {
            return;
        }

        _syncingSettingsUi = true;
        try
        {
            TabIconModeChoices.SelectedIndex = (_settingsData?.TabIconMode ?? TabIconMode.Hover) switch
            {
                TabIconMode.Text => 0,
                TabIconMode.Always => 1,
                _ => 2,
            };

            AnimationSwitch.IsOn = _settingsData?.AnimationsEnabled ?? true;
        }
        finally
        {
            _syncingSettingsUi = false;
        }
    }

    private void OnTabIconModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSettingsUi || _settings is null || _settingsData is null)
        {
            return;
        }

        if (TabIconModeChoices.SelectedItem is not RadioButton { Tag: string wire })
        {
            return;
        }

        try
        {
            _settingsData.TabIconMode = AppSettings.ParseTabIconMode(wire);
            _settings.Save(_settingsData);          // 立刻落盘（与原版"改完即存"一致）
            ApplyTabIconMode();
            _log.AppendLine($"设置变更：标签图标形态 = {_settingsData.TabIconModeRaw}");
            FlushLog();
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.OnTabIconModeChanged", ex);
        }
    }

    private void OnAnimationsToggled(object sender, RoutedEventArgs e)
    {
        if (_syncingSettingsUi || _settings is null || _settingsData is null)
        {
            return;
        }

        try
        {
            _settingsData.AnimationsEnabled = AnimationSwitch.IsOn;
            _settings.Save(_settingsData);
            ApplyAnimationSetting();
            _log.AppendLine($"设置变更：动效 = {_settingsData.AnimationsEnabled}");
            FlushLog();
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.OnAnimationsToggled", ex);
        }
    }

    // ────────────────────────────── 标签图标三种形态 ──────────────────────────────

    /// <summary>标签图标槽被加载出来时记下它（模式变化时要直接改这些实例）。</summary>
    private void OnTabGlyphHostLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border host && host.DataContext is TabItemViewModel tab)
        {
            _tabGlyphHosts[tab] = host;
            ApplyTabIconModeToHost(host, expand: (_settingsData?.TabIconMode ?? TabIconMode.Hover) == TabIconMode.Always);
        }
    }

    private void OnTabPointerEntered(object sender, PointerRoutedEventArgs e)
        => AnimateTabGlyph(sender, show: true);

    private void OnTabPointerExited(object sender, PointerRoutedEventArgs e)
        => AnimateTabGlyph(sender, show: false);

    /// <summary>按当前设置重新套用三种形态（设置变化 / 载入完成时调用）。</summary>
    private void ApplyTabIconMode()
    {
        bool expand = (_settingsData?.TabIconMode ?? TabIconMode.Hover) == TabIconMode.Always;

        foreach (var (tab, host) in _tabGlyphHosts)
        {
            StopTabGlyphAnimation(tab);
            ApplyTabIconModeToHost(host, expand);
        }
    }

    private static void ApplyTabIconModeToHost(Border host, bool expand)
    {
        host.Width = expand ? TabGlyphWidth : 0;
        host.Opacity = expand ? 1 : 0;
    }

    /// <summary>
    /// 悬停动效：图标槽 0↔16 宽度 + 透明度渐变，标签随之"整体拉伸"。
    /// 只有 <see cref="TabIconMode.Hover"/> 模式才响应；动效总开关关闭时直接落值不动画。
    /// </summary>
    private void AnimateTabGlyph(object sender, bool show)
    {
        if (_settingsData?.TabIconMode != TabIconMode.Hover)
        {
            return;
        }

        if (sender is not FrameworkElement root || root.DataContext is not TabItemViewModel tab)
        {
            return;
        }

        if (!_tabGlyphHosts.TryGetValue(tab, out var host))
        {
            return;
        }

        StopTabGlyphAnimation(tab);

        var targetWidth = show ? TabGlyphWidth : 0d;
        var targetOpacity = show ? 1d : 0d;

        if (!(_settingsData?.AnimationsEnabled ?? true))
        {
            ApplyTabIconModeToHost(host, show);
            return;
        }

        var widthAnimation = new DoubleAnimation
        {
            To = targetWidth,
            Duration = new Duration(TimeSpan.FromMilliseconds(170)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },

            // 动画宽度会触发布局（"拉伸"就是要它发生），必须显式允许
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(widthAnimation, host);
        Storyboard.SetTargetProperty(widthAnimation, "Width");

        var opacityAnimation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(140)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(opacityAnimation, host);
        Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(widthAnimation);
        storyboard.Children.Add(opacityAnimation);
        _tabGlyphAnimations[tab] = storyboard;
        storyboard.Begin();
    }

    private void StopTabGlyphAnimation(TabItemViewModel tab)
    {
        if (_tabGlyphAnimations.Remove(tab, out var running))
        {
            running.Stop();
        }
    }

    /// <summary>动效总开关：控制标签栏与各页面的入场/重排过渡。</summary>
    private void ApplyAnimationSetting()
    {
        bool enabled = _settingsData?.AnimationsEnabled ?? true;

        if (TabStrip is not null)
        {
            TabStrip.ItemContainerTransitions.Clear();
            if (enabled)
            {
                TabStrip.ItemContainerTransitions.Add(new EntranceThemeTransition
                {
                    FromVerticalOffset = 8,
                    IsStaggeringEnabled = true,
                });
            }
        }

        foreach (var page in _pages.Values)
        {
            if (page is IAnimationHost host)
            {
                host.SetAnimationsEnabled(enabled);
            }
        }
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
            else if (argument.StartsWith("--tab-icons=", StringComparison.OrdinalIgnoreCase))
            {
                _tabIconModeOverride = AppSettings.ParseTabIconMode(argument["--tab-icons=".Length..]);
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

                // 兜底：SelectedIndex 本来就是 0 时不会触发 SelectionChanged，这里主动同步一次
                SyncSelection();
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

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e) => SyncSelection();

    /// <summary>同步"哪个标签被选中"：更新强调条标记 + 切换内容区。</summary>
    private void SyncSelection()
    {
        if (_viewModel is not null)
        {
            foreach (var tab in _viewModel.Tabs)
            {
                tab.IsSelected = ReferenceEquals(tab, TabStrip.SelectedItem);
            }
        }

        if (TabStrip.SelectedItem is TabItemViewModel selected)
        {
            ShowTab(selected);
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
            _log.AppendLine($"首次创建页面 = [{tab.Kind}] {tab.Name}（{page.GetType().Name}）");

            // 新页面要立刻服从"动效总开关"（默认已在 XAML 里声明，这里按设置覆盖）
            if (page is IAnimationHost animationHost)
            {
                animationHost.SetAnimationsEnabled(_settingsData?.AnimationsEnabled ?? true);
            }
        }

        ContentHost.Content = page;
        _transientStatus = null;
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
