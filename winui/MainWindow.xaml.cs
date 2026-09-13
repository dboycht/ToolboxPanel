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
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private static readonly string VerifyLogPath =
        Path.Combine(Path.GetTempPath(), "toolboxpanel-verify.txt");

    private readonly StringBuilder _log = new();
    private readonly Dictionary<string, UIElement> _pages = new(StringComparer.Ordinal);

    private MainViewModel? _viewModel;
    private SettingsStore? _settings;
    private AppSettings? _settingsData;

    private string? _backdropOverride;      // --backdrop=
    private TabIconMode? _tabIconOverride;  // --tab-icons=
    private SizeInt32? _sizeOverride;       // --size=
    private int _startupTabIndex;           // --tab=
    private bool _isDemo;                   // --demo
    private bool _openSettingsAtStartup;    // --open-settings
    private int _forceHoverTab = -1;        // --hover-tab=N（开发/验证：强制某个标签展开）
    private bool _diagSwitch;               // ⚠️ 临时诊断（定位完删）

    /// <summary>窗口尺寸就绪之前不把 Changed 事件当"用户改尺寸"（启动时我们自己会 Resize 一次）。</summary>
    private bool _windowSizeReady;

    private DispatcherQueueTimer? _sizeSaveTimer;
    private Storyboard? _settingsAnimation;
    private bool _settingsPanelOpen;
    private string _backdropLine = "窗口材质：未初始化";
    private string? _transientStatus;

    public MainWindow()
    {
        // ⚠️ 必须在 InitializeComponent() **之前**把主题令牌灌进资源字典：
        //    XAML 里的 {ThemeResource 令牌名} 是在加载那一刻解析的，晚一步就会找不到键。
        EnsureThemeResources();

        InitializeComponent();

        Title = "ToolboxPanel";

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ApplyStartupArguments();
        LoadSettings();

        // 主题要早于其它界面套用：它决定"深色下的前景色"等基础观感，
        // 也让下面的 CustomizeCaptionButtons 能取到正确的标题栏按钮颜色。
        ApplyTheme();

        CustomizeCaptionButtons();
        ApplyInitialWindowSize();
        LoadData();
        ApplyAllSettings();

        // 记住窗口大小：用户拖完尺寸后（去抖）写进 config.json
        AppWindow.Changed += OnAppWindowChanged;

        if (_openSettingsAtStartup)
        {
            ShowSettings(true);
        }

        if (_diagSwitch)
        {
            StartDiagSwitch();   // ⚠️ 临时诊断：程序自己切页（不注入任何输入）
        }

        if (_forceHoverTab >= 0)
        {
            // 等一次布局：标签项的模板要先实现出来，才能取到隐藏项宿主（`--hover-tab=N`）
            DispatcherQueue.TryEnqueue(() => TabStrip.ForceHoverTab(_forceHoverTab));
        }
    }

    /// <summary>
    /// ⚠️ 临时诊断：用程序自己的切页把"主页 → 常用 → 主页 → 工具 → 主页"跑一遍，
    /// 好在探针日志里看到"切回已打开过的页面"时的真实时间线。定位完删掉。
    /// </summary>
    private void StartDiagSwitch()
    {
        var plan = new (int DelayMs, int TabIndex)[] { (2500, 1), (3000, 0), (3000, 2), (3000, 0), (3000, 1) };
        int cumulative = 0;

        foreach (var (delayMs, tabIndex) in plan)
        {
            cumulative += delayMs;
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(cumulative);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                if (_viewModel is not null && tabIndex < _viewModel.Tabs.Count)
                {
                    TabStrip.SelectedTab = _viewModel.Tabs[tabIndex];
                    ShowTab(_viewModel.Tabs[tabIndex]);
                }
            };

            timer.Start();
        }
    }

    // ────────────────────────────── 主题（最小全局主题，W4 第一步）──────────────────────────────
    //
    // 目标："主题是全局的" —— 主窗口、标签栏、状态栏、**设置面板**都用同一套令牌。
    // 做法分三件：
    //   ① 令牌表在 Core（`core/Storage/ThemeTokens.cs`，浅/深两张、可单测）；
    //   ② 这里把令牌灌成**应用级资源**（Brush），XAML 用 {StaticResource 名字} 引用 ——
    //      设置面板是独立 UserControl，只能用应用级资源才拿得到同一套颜色；
    //   ③ 根 Grid 的 RequestedTheme 跟着设置走，让 WinUI 原生控件的默认配色也一起变
    //      （否则浅色主题下 ComboBox/Slider 还是深色的）。
    //
    // ⚠️ 资源名就是 XAML 里用的键，改名字要两边一起改。

    /// <summary>当前生效的主题令牌（换主题时整体替换）。</summary>
    private ThemePalette _themePalette = ThemeTokens.Dark;

    /// <summary>把令牌灌进应用级资源（**幂等**：XAML 每次加载都会重新解析资源引用，字典只需建一次）。</summary>
    private static void EnsureThemeResources()
    {
        var resources = Application.Current.Resources;
        if (resources.ContainsKey("PanelSurfaceBrush"))
        {
            return;
        }

        void Add(string key, ThemePalette palette, Func<ThemePalette, (byte A, byte R, byte G, byte B)> pick)
        {
            var (a, r, g, b) = pick(palette);
            resources[key] = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }

        foreach (var palette in new[] { ThemeTokens.Dark, ThemeTokens.Light })
        {
            var suffix = palette.IsDark ? "Dark" : "Light";

            // 令牌名 → Brush。键名与 XAML 里的 {StaticResource} 一一对应。
            Add($"PanelSurface{suffix}", palette, p => p.PanelSurface);
            Add($"PanelBorder{suffix}", palette, p => p.PanelBorder);
            Add($"OverlaySurface{suffix}", palette, p => p.Overlay);
            Add($"StatusSurface{suffix}", palette, p => p.StatusSurface);
            Add($"TabStripSurface{suffix}", palette, p => p.TabStripSurface);
            Add($"TabStripBorder{suffix}", palette, p => p.TabStripBorder);
            Add($"CardSurface{suffix}", palette, p => p.CardSurface);
            Add($"HoverSurface{suffix}", palette, p => p.HoverSurface);
            Add($"PressedSurface{suffix}", palette, p => p.PressedSurface);
            Add($"SelectedSurface{suffix}", palette, p => p.SelectedSurface);
            Add($"AccentBrush{suffix}", palette, p => p.Accent);
            Add($"DividerBrush{suffix}", palette, p => p.Divider);
            Add($"TitleBarButtonForeground{suffix}", palette, p => p.TitleBarButtonForeground);
            Add($"TitleBarButtonInactiveForeground{suffix}", palette, p => p.TitleBarButtonInactiveForeground);
            Add($"TitleBarButtonHover{suffix}", palette, p => p.TitleBarButtonHoverBackground);
            Add($"TitleBarButtonPressed{suffix}", palette, p => p.TitleBarButtonPressedBackground);
        }
    }

    /// <summary>按 `--theme=` 临时覆盖（开发/验证用，**不落盘**）。</summary>
    private string? _themeOverride;

    /// <summary>套用界面主题：换令牌 + 跟根 Grid 的 RequestedTheme，并刷新标题栏按钮与手写的画刷。</summary>
    private void ApplyTheme()
    {
        var mode = _themeOverride is null
            ? (_settingsData?.UiTheme ?? ThemeMode.System)
            : ThemeTokens.ParseMode(_themeOverride);

        // "跟随系统"要让根元素回到 Default（= 跟随系统），否则一旦被强制过就再也回不去系统了。
        // 具体生效的深浅由 ApplyThemeToHandWrittenBrushes 里读 RootGrid.ActualTheme 决定 ——
        // ⚠️ ElementTheme 与 ApplicationTheme 是两个不同的类型，不能直接比较（会 CS0019）。
        if (RootGrid is not null)
        {
            var requested = mode switch
            {
                ThemeMode.Light => ElementTheme.Light,
                ThemeMode.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            if (RootGrid.RequestedTheme != requested)
            {
                RootGrid.RequestedTheme = requested;
            }
        }

        // 真正的深浅：强制模式直接定；"跟随系统"读根元素的生效主题
        // （刚把 RequestedTheme 设成 Default 后，ActualTheme 就是系统的深浅）。
        bool isDark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => RootGrid is null || RootGrid.ActualTheme == ElementTheme.Dark,
        };

        var palette = ThemeTokens.Resolve(mode, systemIsDark: isDark);
        _themePalette = palette;

        ApplyThemeToHandWrittenBrushes();
        TabStrip.ApplyTheme(palette);
        Settings.Refresh();
        RefreshSettingsButtonBackground();
        _log.AppendLine($"主题 = {ThemeTokens.ToWire(mode)}（生效：{(palette.IsDark ? "深色" : "浅色")}）");
    }

    /// <summary>当前是否真的用上了系统材质（由 <see cref="ApplyBackdrop"/> 更新）。</summary>
    private bool _glassActive;

    /// <summary>
    /// 代码里手写的画刷（XAML 里那几处资源键换不动的）跟着主题走。
    /// 之所以有"手写的"，是因为它们要么是**运行时切换**（设置按钮的打开态底色），
    /// 要么是系统 API（标题栏按钮）、要么依赖"材质是否生效"—— 这几类用资源键表达不了。
    /// </summary>
    private void ApplyThemeToHandWrittenBrushes()
    {
        var palette = _themePalette;

        // 材质不生效时的兜底底色（材质生效时必须透明，否则盖住玻璃 —— ERROR.md E9）
        RootGrid.Background = _glassActive
            ? new SolidColorBrush(Colors.Transparent)
            : new SolidColorBrush(ToColor(palette.WindowFallback));

        // 设置按钮的"打开中"底色：深色下用白、浅色下用黑
        RefreshSettingsButtonBackground();

        // 设置面板的玻璃表面：元素级 Acrylic，让面板后面的应用内内容也糊开
        SettingsPanelHost.Background = CreatePanelGlass(palette);
    }

    /// <summary>
    /// 设置面板的表面：**低不透明度令牌 + 元素级 AcrylicBrush**。
    ///
    /// <para>⚠️ `TintLuminosityOpacity` 必须显式设 0：它默认 0.8，会在模糊之上再叠一层很亮的
    /// 亮度层，把模糊"洗"成实心感（ERROR.md E11 的实测结论）。</para>
    /// <para>FallbackColor 用不透明令牌色：材质被系统回退时也不至于变成透明玻璃片看不清字。</para>
    /// </summary>
    private static AcrylicBrush CreatePanelGlass(ThemePalette palette)
    {
        var tint = ToColor(palette.PanelSurface);
        var fallback = Color.FromArgb(
            255, tint.R, tint.G, tint.B);

        return new AcrylicBrush
        {
            TintColor = tint,
            TintOpacity = 0.0,        // 不额外加色调层，浓度完全交给 tint 的 alpha
            TintLuminosityOpacity = 0.0,
            FallbackColor = fallback,
        };
    }

    /// <summary>设置按钮"打开中"的底色（选中态）—— 深色用白、浅色用黑。</summary>
    private void RefreshSettingsButtonBackground()
    {
        if (SettingsButton is null)
        {
            return;
        }

        if (!_settingsPanelOpen)
        {
            SettingsButton.Background = new SolidColorBrush(Colors.Transparent);
            return;
        }

        var (a, r, g, b) = _themePalette.SelectedSurface;
        SettingsButton.Background = new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }

    private static Color ToColor((byte A, byte R, byte G, byte B) value)
        => Color.FromArgb(value.A, value.R, value.G, value.B);

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
            else if (argument.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase))
            {
                // 开发/验证用：临时覆盖界面主题（**不落盘**）
                _themeOverride = argument["--theme=".Length..];
            }
            else if (argument.StartsWith("--hover-tab=", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(argument["--hover-tab=".Length..], out int hoverIndex))
            {
                // 开发/验证用：把某个标签强制置为"展开"（悬停态没法稳定合成，只能这样核对布局）
                _forceHoverTab = hoverIndex;
            }
            else if (argument.Equals("--diag", StringComparison.OrdinalIgnoreCase)
                     || argument.Equals("--probe-switch", StringComparison.OrdinalIgnoreCase))
            {
                // ⚠️ 临时诊断：逐毫秒记录入场动效的容器状态（定位"切页先亮一下"）
                EntranceAnimator.DiagnosticsEnabled = true;
                _diagSwitch = true;
            }
            else if (argument.StartsWith("--size=", StringComparison.OrdinalIgnoreCase))
            {
                var parts = argument["--size=".Length..].Split('x', 'X');
                if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                {
                    _sizeOverride = new SizeInt32(Math.Max(360, w), Math.Max(320, h));
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
    }

    /// <summary>窗口尺寸优先级：--size= &gt; 上次记录的尺寸 &gt; 默认（演示模式更小）。</summary>
    private void ApplyInitialWindowSize()
    {
        SizeInt32 size;

        if (_sizeOverride is { } overridden)
        {
            size = overridden;
        }
        else if (_settingsData?.WindowSize is { } saved)
        {
            size = new SizeInt32(saved.Width, saved.Height);
        }
        else
        {
            size = _isDemo ? new SizeInt32(880, 560) : new SizeInt32(1200, 800);
        }

        TryResizeWindow(ClampToWorkArea(size));
        _windowSizeReady = true;
    }

    /// <summary>夹到当前显示器的可用区域，避免换了显示器/手改配置后窗口大到点不到。</summary>
    private SizeInt32 ClampToWorkArea(SizeInt32 size)
    {
        try
        {
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            var work = area.WorkArea;

            return new SizeInt32(
                Math.Clamp(size.Width, AppSettings.MinWindowWidth, Math.Max(AppSettings.MinWindowWidth, work.Width)),
                Math.Clamp(size.Height, AppSettings.MinWindowHeight, Math.Max(AppSettings.MinWindowHeight, work.Height)));
        }
        catch (Exception ex)
        {
            App.WriteCrash("ClampToWorkArea", ex);
            return size;
        }
    }

    /// <summary>窗口尺寸变化 → 去抖 400ms 后写进设置（拖拽过程中不刷盘）。</summary>
    private void OnAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (!_windowSizeReady || !args.DidSizeChange || _settings is null || _settingsData is null)
        {
            return;
        }

        var size = sender.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        _sizeSaveTimer ??= CreateSizeSaveTimer();
        _sizeSaveTimer.Stop();
        _sizeSaveTimer.Start();
    }

    private DispatcherQueueTimer CreateSizeSaveTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(400);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => SaveWindowSize();
        return timer;
    }

    private void SaveWindowSize()
    {
        if (_settings is null || _settingsData is null)
        {
            return;
        }

        try
        {
            var size = AppWindow.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            if (_settingsData.WindowSize == (size.Width, size.Height))
            {
                return;   // 没变化就不写文件
            }

            _settingsData.WindowSize = (size.Width, size.Height);
            _settings.Save(_settingsData);
            _log.AppendLine($"记住窗口尺寸 = {size.Width}x{size.Height}");
            FlushLog();
        }
        catch (Exception ex)
        {
            App.WriteCrash("SaveWindowSize", ex);
        }
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
            Settings.PreviewRequested += (_, _) => CurrentPage()?.RevealWhenReady();
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

        // 主题放在最前：它决定兜底底色/面板玻璃等基础观感，后面几项都在它之上叠加。
        ApplyTheme();
        ApplyBackdrop(settings.Backdrop);
        TabStrip.ApplySettings(settings.TabIconMode, settings.ShowTabCounts, settings.ToAnimationSpec());

        foreach (var page in _pages.Values.OfType<IAnimatedPage>())
        {
            page.ApplyAnimationSpec(settings.ToAnimationSpec());
        }

        Settings.Refresh();
    }

    private void OnSettingsButtonClick(object sender, RoutedEventArgs e) => ShowSettings(!_settingsPanelOpen);

    /// <summary>点面板外的空白处关闭（这一层在面板"下面"，点面板本身不会触发）。</summary>
    private void OnSettingsBackdropTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        => ShowSettings(false);

    /// <summary>
    /// 打开/收起设置面板。**带滑入/滑出动画**（面板从右侧滑进来 + 遮罩淡入）；
    /// 关掉"界面动效"时直接切换，不做动画。
    /// </summary>
    private void ShowSettings(bool open)
    {
        _settingsPanelOpen = open;
        StopSettingsAnimation();

        bool animate = _settingsData?.AnimationsEnabled ?? true;
        var duration = new Duration(TimeSpan.FromMilliseconds(
            Math.Clamp((_settingsData?.AnimationDurationMs ?? 220) * 1.2, 120, 420)));

        // 设置按钮的"打开中"底色：随主题令牌走（深色用白、浅色用黑）
        RefreshSettingsButtonBackground();

        if (open)
        {
            SettingsOverlay.Visibility = Visibility.Visible;

            if (!animate)
            {
                SettingsPanelTransform.TranslateX = 0;
                SettingsBackdrop.Opacity = 1;
                Settings.Focus(FocusState.Programmatic);
                _log.AppendLine("设置面板：打开（动效关闭 → 直接显示）");
                FlushLog();
                return;
            }

            SettingsBackdrop.Opacity = 0;
            SettingsPanelTransform.TranslateX = SettingsPanelHost.Width;
            _settingsAnimation = BuildSettingsAnimation(
                toTranslateX: 0, toBackdropOpacity: 1, duration, onCompleted: () => Settings.Focus(FocusState.Programmatic));
            _settingsAnimation.Begin();
            _log.AppendLine($"设置面板：滑入动画开始（{duration.TimeSpan.TotalMilliseconds:F0}ms）");
            FlushLog();
        }
        else
        {
            if (!animate || SettingsOverlay.Visibility != Visibility.Visible)
            {
                SettingsOverlay.Visibility = Visibility.Collapsed;
                SettingsPanelTransform.TranslateX = SettingsPanelHost.Width;
                SettingsBackdrop.Opacity = 1;
                return;
            }

            _settingsAnimation = BuildSettingsAnimation(
                toTranslateX: SettingsPanelHost.Width, toBackdropOpacity: 0, duration,
                onCompleted: () => SettingsOverlay.Visibility = Visibility.Collapsed);
            _settingsAnimation.Begin();
            _log.AppendLine($"设置面板：滑出动画开始（{duration.TimeSpan.TotalMilliseconds:F0}ms）");
            FlushLog();
        }
    }

    private Storyboard BuildSettingsAnimation(
        double toTranslateX, double toBackdropOpacity, Duration duration, Action onCompleted)
    {
        var easing = EntranceAnimator.CreateEasing(_settingsData?.AnimationEasing ?? AnimationEasing.Standard);

        var slide = new DoubleAnimation
        {
            From = SettingsPanelTransform.TranslateX,
            To = toTranslateX,
            Duration = duration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(slide, SettingsPanelTransform);
        Storyboard.SetTargetProperty(slide, "TranslateX");

        var fade = new DoubleAnimation
        {
            From = SettingsBackdrop.Opacity,
            To = toBackdropOpacity,
            Duration = duration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(fade, SettingsBackdrop);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(slide);
        storyboard.Children.Add(fade);
        storyboard.Completed += (_, _) => onCompleted();
        return storyboard;
    }

    private void StopSettingsAnimation()
    {
        _settingsAnimation?.Stop();
        _settingsAnimation = null;
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

        // 材质生效时根容器必须完全透明；否则补一层主题兜底底色（ERROR.md E9）
        // ⚠️ 这一行**交给主题统一处理**（ApplyThemeToHandWrittenBrushes 会按"材质是否生效 + 当前主题"决定），
        //    避免"改材质把主题底色冲掉"这类两边打架的问题。
        bool glass = backdrop is not null && supported;
        _glassActive = glass;
        ApplyThemeToHandWrittenBrushes();

        _backdropLine = $"窗口材质 = {label}；本机支持 = {supported}；启用 = {glass}";
        BackdropLabel.Text = label;
        UpdateStatusBar();
    }

    /// <summary>标题栏按钮也透出材质（否则右上角是一块实心主题色）；颜色随主题走。</summary>
    private void CustomizeCaptionButtons()
    {
        try
        {
            if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            var palette = _themePalette;
            var bar = AppWindow.TitleBar;
            bar.ButtonBackgroundColor = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
            bar.ButtonForegroundColor = ToColor(palette.TitleBarButtonForeground);
            bar.ButtonInactiveForegroundColor = ToColor(palette.TitleBarButtonInactiveForeground);
            bar.ButtonHoverBackgroundColor = ToColor(palette.TitleBarButtonHoverBackground);
            bar.ButtonPressedBackgroundColor = ToColor(palette.TitleBarButtonPressedBackground);
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

            // 内容为空时补一批示例图标（系统自带程序），否则用户面对的是空页面 ——
            // "新建图标"的界面属于 W5，这一版先用示例让他有东西可看、可点。
            // ⚠️ 判定很克制：**所有内容页一个图标都没有**才动手，且只新增一个「示例」页，不动已有页。
            if (!_isDemo && !_viewModel.HasAnyIcon && _viewModel.Store is { } store)
            {
                if (SampleIcons.EnsureSampleIcons(store))
                {
                    _log.AppendLine($"内容为空 → 已补充示例图标（新建「{SampleIcons.SampleTabName}」页）");
                    _viewModel.Load();   // 重新装配：示例图标也要走"提取并缓存"的既有流程
                }
            }

            if (_isDemo)
            {
                Title = "ToolboxPanel · 纯 UI 演示";
            }

            TabStrip.ItemsSource = _viewModel.Tabs;
            TabStrip.TabSelected += OnTabSelected;
            TabStrip.TabDraggedOver += OnTabDraggedOver;
            TabStrip.ItemDroppedOnTab += OnItemDroppedOnTab;

            _log.AppendLine($"数据目录 = {_viewModel.DataDirectory}");
            _log.AppendLine(_viewModel.StatusText);
            foreach (var tab in _viewModel.Tabs)
            {
                _log.AppendLine($"  - [{tab.DraggableKind}] {tab.Name} :: {tab.CountLabel}");
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

    /// <summary>
    /// 切换内容区：**页面只建一次并常驻**（切页只切 <c>Visibility</c>），每次切页重放一次入场动效。
    ///
    /// <para>⚠️ 为什么"常驻"是必须的（这是用户反复反馈"不像从无到有"的最终根因）：
    /// 原来用 `ContentHost.Content = page` 换页 —— 被换出的页面会被从内容宿主里摘掉，
    /// 它的 item 容器随即被**回收销毁**（探针实测：切回来时"已实现容器=0"）。
    /// 再切回来时容器要**重新实现**，而"重新实现"发生在渲染之后 ⇒
    /// 那一帧会以最终态被画出来（`Prepare 进入：范围=[1.00..1.00]`，窗口 1.1ms），
    /// 用户看到的就是"先亮一下、再重播"。
    /// 现在页面常驻，容器不再被销毁，配合页面级不透明度兜住**首次**展示，入场就永远是"从无到有"。</para>
    /// </summary>
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

            // 常驻：页面建好就放进 PageHost，并且**始终保持可见**
            // （靠页面级不透明度区分谁在前台 —— 用 Visibility=Collapsed 会让 GridView 不布局、
            //   容器一直不被实现，动画就没对象可播；探针实测过这个坑）。
            var pageWrapper = new Grid { Visibility = Visibility.Collapsed };
            pageWrapper.Children.Add(page);
            _pageWrappers[tab.Id] = pageWrapper;
            PageHost.Children.Add(pageWrapper);
            _log.AppendLine($"首次创建页面 = [{tab.DraggableKind}] {tab.Name}（{page.GetType().Name}）");
        }

        if (_currentPageId != tab.Id)
        {
            CollapsePage(_currentPageId);
            _currentPageId = tab.Id;
        }

        if (page is IAnimatedPage animated)
        {
            animated.ApplyAnimationSpec((_settingsData ?? new AppSettings()).ToAnimationSpec());

            if (EntranceAnimator.DiagnosticsEnabled)
            {
                EntranceAnimator.BeginDiagnostics();
                App.ProbeLog($"===== 切到「{tab.Name}」 =====");
            }

            // ⚠️ 入场动效的时序（`--diag` 可核对）：
            //   ① 目标页此刻还是**折叠**的；
            //   ② PrepareEntrance()：把列表整体不透明度置 0（入场起始态）；
            //   ③ ShowPage()：让页面可见 —— 它此刻是全 0，显示出来也什么都没有，
            //      而且**可见才会布局**（折叠状态下 GridView 不布局、容器不会被实现）；
            //   ④ RevealWhenReady()：容器就位/布局跑过之后再起"整片淡入"。
            //      ⚠️ 不要简单"延迟一帧"就放行：调度器转一圈不保证布局跑过，
            //         布局没跑就没有可播的对象（动画建不出来）。
            animated.PrepareEntrance();
            ShowPage(tab.Id);
            animated.RevealWhenReady();
        }

        _transientStatus = null;
        UpdateStatusBar();
    }

    /// <summary>页面已建一次后常驻：每个页面外面套一个 Grid，切页只改它的 Visibility。</summary>
    private readonly Dictionary<string, Grid> _pageWrappers = new(StringComparer.Ordinal);

    private string? _currentPageId;

    private void ShowPage(string tabId)
    {
        if (_pageWrappers.TryGetValue(tabId, out var wrapper))
        {
            wrapper.Visibility = Visibility.Visible;
        }
        // ⚠️ 这里**故意不把页面不透明度改回 1**：起始态（0）已由 PrepareEntrance 设好，
        //    放行整页是 PlayEntrance 的职责（延迟一帧、等容器实现之后再放）。
    }

    private void CollapsePage(string? tabId)
    {
        if (tabId is not null && _pageWrappers.TryGetValue(tabId, out var wrapper))
        {
            wrapper.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>当前显示的内容页（"预览动效"要用）。</summary>
    private IAnimatedPage? CurrentPage()
        => _currentPageId is not null && _pages.TryGetValue(_currentPageId, out var page)
            ? page as IAnimatedPage
            : null;

    private UIElement CreatePage(TabItemViewModel tab)
    {
        if (tab.IsList)
        {
            var listPage = new ListViewPage(tab) { DragDropEnabled = !_isDemo };
            listPage.ItemActivated += OnListItemActivated;
            listPage.ItemDropped += OnItemDropped;
            return listPage;
        }

        var gridPage = new GridPage(tab) { DragDropEnabled = !_isDemo };
        gridPage.IconActivated += OnIconActivated;
        gridPage.ItemDropped += OnItemDropped;
        return gridPage;
    }

    // ────────────────────────────── 拖拽排序（W3）──────────────────────────────

    /// <summary>
    /// 拖拽悬停在某个标签上 → 切到那一页（跨页移动的"先切页再选位置"路径）。
    /// 只有停留超过 <see cref="TabStripView"/> 里的阈值才会到这里，拖过标签栏不会乱切。
    /// </summary>
    private void OnTabDraggedOver(object? sender, TabItemViewModel tab)
    {
        if (!ReferenceEquals(tab, TabStrip.SelectedTab))
        {
            TabStrip.SelectedTab = tab;
            ShowTab(tab);
            _log.AppendLine($"拖拽悬停 → 切到标签页「{tab.Name}」");
        }
    }

    /// <summary>直接把图标/列表项丢在标签上 —— 追加到那一页末尾。</summary>
    private void OnItemDroppedOnTab(object? sender, (DragPayload Payload, TabItemViewModel Tab) e)
    {
        var target = e.Tab;
        if (!KindMatches(e.Payload, target))
        {
            _transientStatus = $"「{target.Name}」不收这一类项目";
            UpdateStatusBar();
            return;
        }

        int targetIndex = e.Payload.Kind == DragItemKind.Icon ? target.Icons.Count : target.ListItems.Count;
        ApplyDrop(new DragDropRequest(e.Payload, target.Id, targetIndex));
    }

    /// <summary>页面里拖放落下 —— 交给 ViewModel 落库（Core 是唯一事实源）。</summary>
    private void OnItemDropped(object? sender, DragDropRequest request)
    {
        var target = _viewModel?.Tabs.FirstOrDefault(t => t.Id == request.TargetTabId);
        if (target is null)
        {
            return;
        }

        if (!KindMatches(request.Payload, target))
        {
            _transientStatus = $"「{target.Name}」不收这一类项目";
            UpdateStatusBar();
            return;
        }

        ApplyDrop(request);
    }

    /// <summary>同一类才能往同一页放（图标 ↔ 列表项不通用；网格页/列表页的语义不同）。</summary>
    private static bool KindMatches(DragPayload payload, TabItemViewModel tab)
        => payload.Kind == tab.DraggableKind;

    private void ApplyDrop(DragDropRequest request)
    {
        if (_viewModel is null)
        {
            return;
        }

        var result = _viewModel.ApplyDrop(request);
        var target = _viewModel.Tabs.FirstOrDefault(t => t.Id == request.TargetTabId);
        var name = target?.Name ?? "?";

        _transientStatus = result.Success
            ? $"已移动：{DescribeDroppedItem(request.Payload)} → 「{name}」"
            : $"移动失败：{result.Reason}";

        if (result.Success)
        {
            _log.AppendLine($"拖放落库：{request.Payload} → 页={name} 位置={request.TargetIndex}");
        }

        UpdateStatusBar();
        FlushLog();
    }

    private string DescribeDroppedItem(DragPayload payload)
    {
        if (_viewModel is null)
        {
            return payload.ItemId;
        }

        if (payload.Kind == DragItemKind.Icon)
        {
            var tile = _viewModel.Tabs
                .SelectMany(t => t.Icons)
                .FirstOrDefault(i => i.Model.Id == payload.ItemId);
            return tile?.DisplayName ?? payload.ItemId;
        }

        var row = _viewModel.Tabs
            .SelectMany(t => t.ListItems)
            .FirstOrDefault(i => i.Model.Id == payload.ItemId);
        return row?.Description ?? payload.ItemId;
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
