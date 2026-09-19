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
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ToolboxPanel.Core;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
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
    private bool _probeThemeSwitch;         // --probe-theme-switch（常驻诊断：切主题漏网元素自检）

    /// <summary>窗口尺寸就绪之前不把 Changed 事件当"用户改尺寸"（启动时我们自己会 Resize 一次）。</summary>
    private bool _windowSizeReady;

    private DispatcherQueueTimer? _sizeSaveTimer;
    private Storyboard? _settingsAnimation;
    private bool _settingsPanelOpen;
    private string _backdropLine = I18n.T("window.backdrop.initializing");
    private string? _transientStatus;

    public MainWindow()
    {
        // ⚠️ 必须在 InitializeComponent() **之前**把主题令牌灌进资源字典：
        //    XAML 里的 {ThemeResource 令牌名} 是在加载那一刻解析的，晚一步就会找不到键。
        EnsureThemeResources();

        InitializeComponent();

        Title = "ToolboxPanel";

        // ⚠️ 搜索框的占位文字不在这里设：XAML 上标了 `ui:Tr.Key="search.placeholder"`，
        //    文案（含语言切换后的重刷）统一由 Tr / ApplyLanguage 负责 —— 一处来源。

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

        // ⚠️ 这里必须再写一次自检日志：`LoadData()` 内部会 `FlushLog()`，而那一刻
        //    `_backdropLine` 还是初值「未初始化」（材质要到 `ApplyAllSettings()` 里才真正挂上）
        //    ⇒ 只写那一次的话，`%TEMP%\toolboxpanel-verify.txt` 会永远停在"未初始化"这一行，
        //    明明同一份日志里已经写着"材质=mica"，读起来像材质没生效。
        FlushLog();

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

        if (_probeThemeSwitch)
        {
            DispatcherQueue.TryEnqueue(async () => await RunThemeSwitchProbeAsync());
        }

    }

    /// <summary>
    /// 开发/验证开关：`--probe-theme-switch`（**常驻**，与 `--diag` / `--probe-switch` 同类）。
    ///
    /// <para>自驱动复现用户路径「打开 → 点设置 → 切换主题」（深色 → 浅色），把**会随主题变化的各元素
    /// 当前颜色**写进自检日志（`%TEMP%\toolboxpanel-verify.txt`），并在屏幕上各停留一会儿
    /// 供外部 `PrintWindow` 抓图核对；同时写 `%TEMP%\toolboxpanel-switch-probe.txt`
    /// （内容 `before` / `after`）作为"现在是哪一帧"的标记。跑完自动退出。</para>
    ///
    /// <para>为什么值得常驻：**"切主题后有些元素没换"是热切换特有的 bug** ——
    /// 冷启动两种主题都是对的（初值来自 XAML），所以"用 `--theme=` 启动两次"根本验证不出来。
    /// 这个开关让"漏网元素"以后随时可复测（本次它一眼抓出 6 处，见 ERROR.md E19）。</para>
    /// </summary>
    private async Task RunThemeSwitchProbeAsync()
    {
        var markerPath = Path.Combine(Path.GetTempPath(), "toolboxpanel-switch-probe.txt");

        try
        {
            _log.AppendLine("===== 切主题：漏网元素自检（--probe-theme-switch）=====");

            _settingsData!.UiTheme = ThemeMode.Dark;
            ApplyAllSettings();
            ShowSettings(true);                 // 复现用户路径：先打开设置面板
            await Task.Delay(700);

            LogThemeState("切换前(深色)");
            File.WriteAllText(markerPath, "before");
            await Task.Delay(2600);

            _settingsData.UiTheme = ThemeMode.Light;
            ApplyAllSettings();                 // 设置面板改主题走的就是这条链
            await Task.Delay(900);

            LogThemeState("切换后(浅色)");
            File.WriteAllText(markerPath, "after");
            await Task.Delay(2600);

            _log.AppendLine("===== 自检结束 =====");
        }
        catch (Exception ex)
        {
            _log.AppendLine("[自检] 失败：" + ex);
        }
        finally
        {
            FlushLog();
            Environment.Exit(0);
        }
    }

    /// <summary>自检：把"会随主题变"的各元素当前颜色打出来（漏网元素一眼可见）。</summary>
    private void LogThemeState(string tag)
    {
        static string C(Brush? brush)
            => brush is SolidColorBrush solid
                ? $"#{solid.Color.A:X2}{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}"
                : "(非纯色/未设)";

        var bar = AppWindow.TitleBar;
        _log.AppendLine($"[{tag}] 根元素ActualTheme={RootGrid.ActualTheme} 令牌IsDark={_themePalette.IsDark}");
        _log.AppendLine($"[{tag}] 系统窗按钮：前景={C(new SolidColorBrush(bar.ButtonForegroundColor ?? Microsoft.UI.Colors.Transparent))} "
                        + $"非活动={C(new SolidColorBrush(bar.ButtonInactiveForegroundColor ?? Microsoft.UI.Colors.Transparent))} "
                        + $"悬停底={C(new SolidColorBrush(bar.ButtonHoverBackgroundColor ?? Microsoft.UI.Colors.Transparent))}");
        _log.AppendLine($"[{tag}] 标题文字={C(TitleText.Foreground)} 材质标签={C(BackdropLabel.Foreground)}");
        _log.AppendLine($"[{tag}] 状态栏底={C(StatusBar.Background)}");
        _log.AppendLine($"[{tag}] 设置面板：描边={C(SettingsPanelHost.BorderBrush)} 遮罩={C(SettingsBackdrop.Background)}");
        _log.AppendLine($"[{tag}] 标签选中条={C(FindSelectionBarBrush())}");
        FlushLog();
    }

    /// <summary>自检：在标签栏可视树里找"选中强调条"（模板里的 <c>Border x:Name=SelectionBar</c>）。</summary>
    private Brush? FindSelectionBarBrush()
    {
        Brush? found = null;
        Walk(TabStrip);
        return found;

        void Walk(DependencyObject node)
        {
            if (found is not null)
            {
                return;
            }

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is FrameworkElement { Name: "SelectionBar" } bar)
                {
                    found = bar is Border border ? border.Background : null;
                    return;
                }

                Walk(child);
            }
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

    /// <summary>
    /// 当前生效的深浅，供对话框设 <c>RequestedTheme</c> 用。
    ///
    /// <para>⚠️ **ContentDialog 不会跟随窗口根元素的 `RequestedTheme`**：它住在 XamlRoot 的
    /// popup 层，不在 `RootGrid` 的子树里，于是永远按系统主题解析 —— 用户实测反馈的
    /// "关于窗口不随深浅色切换、里面有的字颜色不对"就是这个根因（ERROR.md E18）。
    /// 每个对话框在 `ShowAsync` 之前都要显式设一遍。</para>
    /// </summary>
    private ElementTheme CurrentElementTheme => _themePalette.IsDark ? ElementTheme.Dark : ElementTheme.Light;

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
                // ⚠️⚠️ 改 `RequestedTheme` 之前**必须先摘掉 SystemBackdrop**（用户实测的崩溃，见 ERROR.md E16）：
                //    主题一变，WinUI 会把"默认背景配置变了"通知给当前挂着的 backdrop
                //    （`SystemBackdrop.OnDefaultSystemBackdropConfigurationChanged`），
                //    这一步会抛 `System.ArgumentException: 参数错误`（XamlUnhandledException ⇒ 直接崩）。
                //    摘掉之后再改，就没有 backdrop 可通知了。
                bool detached = SystemBackdrop is not null;
                if (detached)
                {
                    SystemBackdrop = null;
                }

                try
                {
                    RootGrid.RequestedTheme = requested;
                }
                catch (Exception ex)
                {
                    // 换主题失败不该把窗口搞挂：落盘 + 继续（保持原主题）
                    App.WriteCrash("ApplyTheme/RequestedTheme", ex);
                }

                // 自己挂回来：`ApplyAllSettings` 随后也会再挂一次（那一步是幂等的），
                // 但如果本次是从别处（例如只改主题）调用，没有第二次机会 —— 所以这里必须兜住，
                // 否则窗口会永久失去材质（表现就是"玻璃没了"）。
                if (detached)
                {
                    ApplyBackdrop(_settingsData?.Backdrop ?? BackdropKinds.Mica);
                }
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

        // ── 下面是**切主题时不会自己变的"漏网元素"**（用户实测反馈"有些图标与文字没换"）──
        //    它们要么引用本项目注入的固定资源键（`XxxDark`，值是死的），
        //    要么是系统绘制的窗口按钮 ⇒ 必须由代码按当前令牌直接赋值（ERROR.md E19、memory/07 §10）。
        //
        // ⚠️ 今后新增"会随主题变化的颜色"时，**同一步把它接进这个清单**，否则下次热切换必复现。

        // ① 状态栏底（文字色走元素主题，这里只管底）
        StatusBar.Background = new SolidColorBrush(ToColor(palette.StatusSurface));

        // ② 设置面板的左描边（过去只换了底、没换描边：浅色下是一条白线压在浅色面板上）
        SettingsPanelHost.BorderBrush = new SolidColorBrush(ToColor(palette.PanelBorder));

        // ③ 设置面板的模态遮罩
        SettingsBackdrop.Background = new SolidColorBrush(ToColor(palette.Overlay));

        // ④ 系统绘制的窗口按钮（— □ ✕）：颜色是我们通过 AppWindow.TitleBar 设的，
        //    **不会**随 RequestedTheme 自动变 —— 不在切主题时重设，浅色主题下就是白字白底（看不见）
        CustomizeCaptionButtons();
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
            else if (argument.Equals("--probe-theme-switch", StringComparison.OrdinalIgnoreCase))
            {
                // 常驻自检：自驱动走"开设置 → 切主题"，打印各元素颜色并停留供抓图（见 RunThemeSwitchProbeAsync）
                _probeThemeSwitch = true;
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

    /// <summary>设置相关事件是否已订阅（导入备份后会重新读盘，避免重复订阅）。</summary>
    private bool _settingsHooked;

    /// <summary>读设置；演示模式把 config.json 写到临时目录，**绝不碰用户真实数据目录**。</summary>
    private void LoadSettings()
    {
        try
        {
            _settings = _isDemo
                ? new SettingsStore(Path.Combine(Path.GetTempPath(), "toolboxpanel-ui-demo"))
                : SettingsStore.CreateDefault();

            _settingsData = _settings.Load();

            // 语言要尽早生效：后面所有文案（含 XAML 上标了 ui:Tr.Key 的静态文字）都按它取
            I18n.SetLanguage(_settingsData.Language);

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
            Settings.SetDataDirectory(_viewModel?.DataDirectory);

            // ⚠️ 这里一定要 try/catch：设置面板的每一项改动都会走到这条链，
            //    链上任何一处抛异常（例如切主题时 WinUI 的背景配置回调抛 ArgumentException，
            //    见 ERROR.md E16）都会变成"未处理异常 → 应用直接崩"。
            //    外观类失败必须是**软**的：落盘 + 保持原样；用户顶多看到"没变色"，不该丢掉整个应用。
            //
            // ⚠️ 事件**只订阅一次**：导入备份之后会再次调用本方法（重新读盘），
            //    不加这道闸门就会重复订阅 ⇒ 一次改动套用两遍（见 _settingsHooked）。
            if (!_settingsHooked)
            {
                _settingsHooked = true;

                Settings.SettingApplied += (_, _) =>
                {
                    try
                    {
                        ApplyAllSettings();
                    }
                    catch (Exception ex)
                    {
                        App.WriteCrash("MainWindow.SettingApplied/ApplyAllSettings", ex);
                    }
                };

                Settings.PreviewRequested += (_, _) => CurrentPage()?.RevealWhenReady();
                Settings.CloseRequested += (_, _) => ShowSettings(false);
                Settings.ExportBackupRequested += async (_, _) => await ExportBackupAsync();
                Settings.ImportBackupRequested += async (_, _) => await ImportBackupAsync();
            }

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
            page.ApplyTheme(_themePalette);   // 页面里的固定资源键（落点指示线等）随主题重绘
        }

        // 图标大小三档：只对网格页有意义（列表页没有图块，不实现这个契约）
        var iconSize = IconSizeMetrics.For(settings.IconSize);
        foreach (var page in _pages.Values.OfType<IIconSizedPage>())
        {
            page.ApplyIconSize(iconSize);
        }

        // 语言放在最后：它要重刷"其它几项套用过程中可能刚被写上的"文字（状态栏、空页提示、计数）
        ApplyLanguage();

        Settings.Refresh();
    }

    // ────────────────────────────── 语言 / 文案（W5 · v2.0.3 i18n）──────────────────────────────
    //
    // 三件事分工清楚，别互相重复：
    //   ① **XAML 里的静态文案** —— 标 `ui:Tr.Key="<key>"`，由 `Tr.RefreshAll()` 统一重刷；
    //   ② **代码里设的文案** —— 各页面 / 面板自己的 `ApplyLanguage()`（本方法逐个下发）；
    //   ③ **数据型文案**（状态栏摘要、标签栏计数）—— 让 ViewModel 重算。
    // 语言本身来自 `config.json` 的 `language`（设置面板「语言」一节改它 → SettingApplied → 这里）。

    /// <summary>把当前语言套用到整个界面（幂等：同一语言重复调用只是重写一遍同样的字）。</summary>
    private void ApplyLanguage()
    {
        I18n.SetLanguage(_settingsData?.Language);

        Tr.RefreshAll();          // ① XAML 上标了 ui:Tr.Key / ui:Tr.Tip 的元素
        Settings.ApplyLanguage(); // ② 设置面板里由代码设置的文字（开关的开/关、数据目录行）

        foreach (var page in _pages.Values.OfType<IAnimatedPage>())
        {
            page.ApplyLanguage();
        }

        if (_viewModel is not null)
        {
            _viewModel.RebuildStatusText();          // ③ 状态栏摘要
            foreach (var tab in _viewModel.Tabs)
            {
                tab.NotifyCountLabel();              // ③ 标签栏上的「N 个图标 / N 项」
            }
        }

        UpdateSearchUi();     // 搜索栏上的「匹配 N / M」
        UpdateStatusBar();
    }

    private void OnSettingsButtonClick(object sender, RoutedEventArgs e) => ShowSettings(!_settingsPanelOpen);

    // ────────────────────────────── 搜索过滤（W5）──────────────────────────────
    //
    // 形态照原版 v1.11.6 的**隐藏式搜索栏**（用户 2026-09-16 选择题确认）：
    //   · 标题栏 🔍 按钮 / Ctrl+F 切换显示；Esc 关闭；**关闭即清空查询**（原版 set_search_visible(False) 同义）；
    //   · 查询是**全局一个**：过滤"当前显示的那一页"，切页后继续生效；
    //   · 命中判定、文案、"可见位 → Core 下标"的换算全在 Core 的 `SearchFilter`（有单测）；
    //     这里只负责把查询下发下去 + 显示匹配计数。
    // ⚠️ 页面是**懒创建**的（切到才建），所以查询也要发给"标签页"本身（它自己维护可见集合），
    //    这样后建的页面一绑上就已经是过滤后的样子。

    /// <summary>当前查询（与搜索框内容一致；空 = 不过滤）。</summary>
    private string _searchQuery = string.Empty;

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ToggleSearch();
    }

    private void OnSearchButtonClick(object sender, RoutedEventArgs e) => ToggleSearch();

    private void OnSearchCloseClick(object sender, RoutedEventArgs e) => CloseSearch();

    private void ToggleSearch()
    {
        if (SearchBar.Visibility == Visibility.Visible)
        {
            CloseSearch();
        }
        else
        {
            OpenSearch();
        }
    }

    private void OpenSearch()
    {
        SearchBar.Visibility = Visibility.Visible;
        SearchBox.Focus(FocusState.Programmatic);   // 打开就能直接打字（原版也是 setFocus）
        UpdateSearchUi();
    }

    /// <summary>
    /// 关闭搜索栏。⚠️ 必须**同时清空查询**：否则会出现"看不见的过滤" ——
    /// 搜索栏没了、页面却还是少一半图标，用户只会觉得程序坏了。
    /// </summary>
    private void CloseSearch()
    {
        SearchBar.Visibility = Visibility.Collapsed;
        SearchBox.Text = string.Empty;   // 触发 OnSearchTextChanged → 自动清掉过滤
        ApplySearch(string.Empty);

        _log.AppendLine("搜索：已关闭（查询清空）");
        FlushLog();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplySearch(SearchBox.Text);

    /// <summary>Esc 关闭搜索栏（原版把事件过滤器装在搜索框上，行为一致）。</summary>
    private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            CloseSearch();
        }
    }

    /// <summary>
    /// 把查询下发给**所有**标签页与已建页面（页面懒创建 ⇒ 两条路都要走）。
    /// 每一页都下发（而不是只发当前页）的理由：切页时就不需要再重算，各页自己已经是过滤后的状态。
    /// </summary>
    private void ApplySearch(string? query)
    {
        _searchQuery = query ?? string.Empty;

        if (_viewModel is not null)
        {
            foreach (var tab in _viewModel.Tabs)
            {
                if (_pages.TryGetValue(tab.Id, out var page) && page is ISearchablePage searchable)
                {
                    searchable.ApplySearch(_searchQuery);   // 页面顺带把空页提示 / 批量条摆对
                }
                else
                {
                    tab.SetFilter(_searchQuery);            // 页面还没建：先记在标签页上
                }
            }
        }

        UpdateSearchUi();
    }

    /// <summary>搜索栏上的匹配计数（「匹配 3 / 19」）—— 一屏只剩几个时，一眼能看出是被过滤了。</summary>
    private void UpdateSearchUi()
    {
        var tab = TabStrip.SelectedTab;
        SearchCount.Text = tab is not null && SearchFilter.IsActive(_searchQuery)
            ? SearchFilter.CountText(tab.VisibleCount, tab.TotalCount)
            : string.Empty;
    }

    // ────────────────────────────── 备份：导出 / 导入 ZIP（W5）──────────────────────────────    //
    // 逻辑全在 Core 的 `BackupManager`（14 项单测，含与**原版 Python 的双向兼容**）；
    // 这里只负责：选路径 → 二次确认 → 起进度对话框 → 后台线程跑 → 收尾反馈。
    // ⚠️ 文件 IO 一律放后台线程；进度与日志用 DispatcherQueue 切回 UI 线程（UI 线程纪律）。

    private async void OnExportBackupAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ExportBackupAsync();
    }

    private async void OnImportBackupAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ImportBackupAsync();
    }

    /// <summary>导出备份：选目录 → 写 ZIP（metadata.json 在根 + 数据在 <c>data/</c> 前缀下）→ 进度反馈。</summary>
    private async Task ExportBackupAsync()
    {
        try
        {
            if (_viewModel is null)
            {
                return;
            }

            if (_isDemo)
            {
                ReportTransient(I18n.T("demo.no_export"));
                return;
            }

            var folder = await FilePickers.PickFolderAsync(AppWindow.Id);
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            var zipPath = Path.Combine(folder, BackupManager.UniqueFileName());
            var dataDirectory = _viewModel.DataDirectory;
            var version = AppInfo.Version;   // 版本号只读程序集（csproj 是单一来源）

            ShowSettings(false);
            var dialog = ShowBackupDialog(I18n.T("export.title"));

            var result = await Task.Run(() => BackupManager.Export(
                dataDirectory,
                zipPath,
                version,
                progress => DispatcherQueue.TryEnqueue(() => dialog.Report(progress)),
                line => DispatcherQueue.TryEnqueue(() => dialog.AppendLog(line))));

            ReportBackupResult(dialog, I18n.T("action.export"), result);

            if (result.Success)
            {
                ReportTransient(I18n.T("export.done", ("path", result.Message)));
            }
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.ExportBackupAsync", ex);
            ReportTransient(I18n.T("export.failed", ("err", ex.Message)));
        }
    }

    /// <summary>
    /// 导入备份：选 ZIP → **二次确认**（会清空当前数据）→ 落盘 → 重载界面。
    ///
    /// <para>⚠️ 失败时 Core 保证**一个字节都不改** —— 而且这句话对**所有**失败路径都成立：
    /// 包内容先解到暂存区，确认可用之后才清空当前数据并搬进来（见 <see cref="BackupManager.Import"/>）。
    /// 早期版本只做到"metadata 校验在清理之前"，解压中途失败仍会把数据删干净，别退回去。</para>
    /// </summary>
    private async Task ImportBackupAsync()
    {
        try
        {
            if (_viewModel is null)
            {
                return;
            }

            if (_isDemo)
            {
                ReportTransient(I18n.T("demo.no_import"));
                return;
            }

            var zipPath = await FilePickers.PickFileAsync(AppWindow.Id, new[] { ".zip" });
            if (string.IsNullOrEmpty(zipPath))
            {
                return;
            }

            var confirmed = await ConfirmAsync(
                I18n.T("import.confirm.title"),
                I18n.T("import.confirm.text"),
                I18n.T("import.button"));
            if (!confirmed)
            {
                return;
            }

            var dataDirectory = _viewModel.DataDirectory;

            ShowSettings(false);
            var dialog = ShowBackupDialog(I18n.T("import.title"));

            var result = await Task.Run(() => BackupManager.Import(
                zipPath,
                dataDirectory,
                progress => DispatcherQueue.TryEnqueue(() => dialog.Report(progress)),
                line => DispatcherQueue.TryEnqueue(() => dialog.AppendLog(line))));

            ReportBackupResult(dialog, I18n.T("action.import"), result);

            if (result.Success)
            {
                ReloadAfterImport();
                ReportTransient(I18n.T("import.done"));
            }
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.ImportBackupAsync", ex);
            ReportTransient(I18n.T("import.failed", ("err", ex.Message)));
        }
    }

    /// <summary>起一个进度对话框（**非阻塞**显示；结束时由调用方 MarkDone，用户再关掉）。</summary>
    private BackupProgressDialog ShowBackupDialog(string title)
    {
        var dialog = new BackupProgressDialog(title)
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = CurrentElementTheme,   // E18：ContentDialog 不继承根元素主题
        };

        _ = dialog.ShowAsync().AsTask();
        return dialog;
    }

    private void ReportBackupResult(BackupProgressDialog dialog, string action, BackupResult result)
    {
        if (result.Success)
        {
            dialog.MarkDone(true, I18n.T("progress.action_done", ("action", action), ("message", result.Message)));
            _log.AppendLine($"{action}备份成功 = {result.Message}");
        }
        else
        {
            var failed = I18n.T("progress.action_failed", ("action", action), ("message", result.Message));
            dialog.MarkDone(false, failed);
            _log.AppendLine($"{action}备份失败 = {result.Message}");
            ReportTransient(failed);
        }

        FlushLog();
    }

    /// <summary>
    /// 导入成功后重载 —— **tabs.json 与 config.json 都可能被整包替换**，所以三步都要做：
    /// ① 重新读设置并整份套用（主题 / 材质 / 动效都可能变了）；
    /// ② 重建数据模型（<see cref="MainViewModel.Load"/> 重建 Tabs 与图标缓存引用）；
    /// ③ **丢弃页面缓存**（页面持有旧的 tab 视图模型实例，不丢就会显示旧内容），再显示第一页。
    /// </summary>
    private void ReloadAfterImport()
    {
        try
        {
            LoadSettings();
            ApplyAllSettings();

            _viewModel?.Load();

            PageHost.Children.Clear();
            _pages.Clear();
            _pageWrappers.Clear();
            _currentPageId = null;

            TabStrip.ItemsSource = _viewModel?.Tabs;
            Settings.SetDataDirectory(_viewModel?.DataDirectory);

            _log.AppendLine("导入后重载 = 设置 + 数据 + 页面缓存全部重建");

            if (_viewModel is { Tabs.Count: > 0 })
            {
                TabStrip.SelectedTab = _viewModel.Tabs[0];
                ShowTab(_viewModel.Tabs[0]);
            }
            else
            {
                UpdateStatusBar();
            }

            // 导入后标签页与页面都是新建的（过滤状态是空的）⇒ 把当前查询重新下发一遍
            ApplySearch(_searchQuery);
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.ReloadAfterImport", ex);
        }
    }

    /// <summary>
    /// 标题栏 ⓘ —— 弹「关于」对话框。
    /// 内容由 Core 的 <see cref="AboutInfo"/> 组装（有单测）；环境事实（版本/运行时/SDK/系统/程序路径）
    /// 由 <see cref="AppInfo"/> 采集后传进去 —— 版本号**只读程序集**（csproj 的 &lt;Version&gt; 是单一来源）。
    /// </summary>
    private async void OnAboutButtonClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var info = AboutInfo.Create(
                version: AppInfo.Version,
                dataDirectory: _isDemo ? null : _viewModel?.DataDirectory,
                isDemo: _isDemo,
                extraDiagnostics: AppInfo.Diagnostics());

            var dialog = AboutDialog.Create(info);
            dialog.XamlRoot = RootGrid.XamlRoot;
            dialog.RequestedTheme = CurrentElementTheme;   // ⚠️ 不设就永远用系统主题（E18）
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.OnAboutButtonClick", ex);
        }
    }

    /// <summary>点面板外的空白处关闭（这一层在面板"下面"，点面板本身不会触发）。</summary>
    private void OnSettingsBackdropTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        => ShowSettings(false);

    /// <summary>
    /// 打开 / 收起设置面板。
    ///
    /// <para><b>打开</b>：面板从右侧滑入（`TranslateX: 宽 → 0`）+ 遮罩淡入。</para>
    /// <para><b>收起</b>：反向滑出（`TranslateX: 0 → 宽`）+ 遮罩淡出，
    /// **动画结束后**才把遮罩层折叠起来。</para>
    /// <para>关掉"界面动效"时两向都直接切换。</para>
    ///
    /// <para>⚠️ 三条纪律（收起动画"看起来没生效"就是这几条漏了）：</para>
    /// <list type="number">
    /// <item><b>起动画前必须 `Stop()` 上一轮</b>：Storyboard 默认 `HoldEnd`，
    /// 活动动画值优先于本地赋值 —— 不停掉旧动画，我们写的起始值会被压住，
    /// 表现就是"点了关闭但没有滑出过程"。</item>
    /// <item><b>显式声明 `FillBehavior = HoldEnd`</b>：动画结束/被中断时保持的是**终值**
    /// （收起是"已滑出"、打开是"已滑入"），不会回落到本地值把面板弹回来。</item>
    /// <item><b>收起期间遮罩层不吃点击</b>：动画那几百毫秒里点到的应该是下面主界面，
    /// 而不是一个正在滑走的面板；动画结束后整层折叠、恢复可点。</item>
    /// </list>
    /// </summary>
    private void ShowSettings(bool open)
    {
        _settingsPanelOpen = open;

        // 起新动画前先停掉上一轮（否则 HoldEnd 的旧值会压住这次的起始值）
        StopSettingsAnimation();

        bool animate = _settingsData?.AnimationsEnabled ?? true;
        var duration = new Duration(TimeSpan.FromMilliseconds(
            Math.Clamp((_settingsData?.AnimationDurationMs ?? 220) * 1.2, 120, 420)));

        // 设置按钮的"打开中"底色：随主题令牌走（深色用白、浅色用黑）
        RefreshSettingsButtonBackground();

        if (open)
        {
            SettingsOverlay.Visibility = Visibility.Visible;
            SettingsOverlay.IsHitTestVisible = true;

            if (!animate)
            {
                ApplySettingsPanelClosedState(closed: false);
                Settings.Focus(FocusState.Programmatic);
                _log.AppendLine("设置面板：打开（动效关闭 → 直接显示）");
                FlushLog();
                return;
            }

            // 起始态：贴在右侧边缘外 + 遮罩全透明（同步写，同一帧内完成 ⇒ 不闪最终态）
            SettingsBackdrop.Opacity = 0;
            SettingsPanelTransform.TranslateX = PanelSlideDistance();

            _settingsAnimation = BuildSettingsAnimation(
                toTranslateX: 0, toBackdropOpacity: 1, duration,
                onCompleted: () => Settings.Focus(FocusState.Programmatic));
            _settingsAnimation.Begin();
            _log.AppendLine($"设置面板：滑入动画开始（{duration.TimeSpan.TotalMilliseconds:F0}ms）");
            FlushLog();
            return;
        }

        // ── 收起 ──
        if (SettingsOverlay.Visibility != Visibility.Visible)
        {
            // 本来就没显示：直接归位即可（不必动画）
            ApplySettingsPanelClosedState(closed: true);
            return;
        }

        if (!animate)
        {
            ApplySettingsPanelClosedState(closed: true);
            _log.AppendLine("设置面板：收起（动效关闭 → 直接隐藏）");
            FlushLog();
            return;
        }

        // 收起期间不吃点击（点到的是下面的主界面），动画结束后整层折叠
        SettingsOverlay.IsHitTestVisible = false;

        // ⚠️⚠️ 关键一步（"关闭没有动画"的最后真根因）：
        //    Storyboard 用 HoldEnd 停在 0，一旦 `Stop()` 掉它，位移会**回落到本地值**
        //    —— 而本地值正是打开时写进去的 392（面板宽度）⇒ 关闭动画还没跑，面板就已经被弹到屏幕外了。
        //    （探针实测：`滑出 Begin：From=392.0 To=392.0`，动画等于从终点到终点。）
        //    做法：起动画前把**本地值钉在当前实测位置**（收起时是 0，即"已展开"的位置），
        //    再由动画把它滑出去，过程就真实可见了。
        //    遮罩同理：HoldEnd 停在 1，本地值却在打开时被写成 0 ⇒ 也一并钉回 1，
        //    否则淡出动画会"从 0 到 0"（探针实测过：遮罩From=0.00，看起来是瞬暗）。
        SettingsPanelTransform.TranslateX = 0;
        SettingsBackdrop.Opacity = 1;

        _settingsAnimation = BuildSettingsAnimation(
            toTranslateX: PanelSlideDistance(), toBackdropOpacity: 0, duration,
            onCompleted: () =>
            {
                SettingsOverlay.Visibility = Visibility.Collapsed;
                SettingsOverlay.IsHitTestVisible = true;
                ApplySettingsPanelClosedState(closed: true);
            });
        _settingsAnimation.Begin();
        _log.AppendLine($"设置面板：滑出动画开始（{duration.TimeSpan.TotalMilliseconds:F0}ms，"
                        + $"从 X={SettingsPanelTransform.TranslateX:0.0} 到 X={PanelSlideDistance():0.0}）");
        FlushLog();
    }

    //    /// <summary>面板滑出多远才算"完全在窗口外"（面板宽度；拿不到时退回设计宽度）。</summary>
    private double PanelSlideDistance()
    {
        double width = SettingsPanelHost.ActualWidth > 0
            ? SettingsPanelHost.ActualWidth
            : SettingsPanelHost.Width;

        return width > 0 ? width : 392;
    }

    //
    /// <summary>把面板直接摆成"已收起 / 已展开"的终态（不做动画，用于动效关闭或动画结束后的归位）。</summary>
    private void ApplySettingsPanelClosedState(bool closed)
    {
        SettingsPanelTransform.TranslateX = closed ? PanelSlideDistance() : 0;
        SettingsBackdrop.Opacity = closed ? 0 : 1;

        if (closed)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
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

            // ⚠️ 显式 HoldEnd：动画结束/被中断时保持**终值**，不会回落到本地值把面板弹回去
            FillBehavior = FillBehavior.HoldEnd,
        };
        Storyboard.SetTarget(slide, SettingsPanelTransform);
        Storyboard.SetTargetProperty(slide, "TranslateX");

        var fade = new DoubleAnimation
        {
            From = SettingsBackdrop.Opacity,
            To = toBackdropOpacity,
            Duration = duration,
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd,
        };
        Storyboard.SetTarget(fade, SettingsBackdrop);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(slide);
        storyboard.Children.Add(fade);

        // ⚠️⚠️ 这里有个坑（"关闭没有动画"的真根因）：
        //    `StopSettingsAnimation()` 停止 storyboard 时，它挂的 `Completed` 回调**也会被执行**
        //    （表现为：上一轮"打开"的回调被收尾触发 → 先把遮罩整层折叠掉 → 这次"关闭"的动画
        //     还没跑就被藏起来了，用户看到的就是"关闭没有动画"）。
        //    所以回调必须**只允许第一次完成生效**。
        bool completed = false;
        storyboard.Completed += (_, _) =>
        {
            if (completed)
            {
                return;   // 被 Stop() 触发的"收尾式完成"：忽略，别动界面
            }

            completed = true;

            try
            {
                onCompleted();
            }
            catch (Exception ex)
            {
                // 外观类失败必须是"软"的；由它把面板留在屏幕上最糟，所以兜底强制归位
                App.WriteCrash("ShowSettings.onCompleted", ex);
                ApplySettingsPanelClosedState(closed: !_settingsPanelOpen);
            }
        };

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
                    label = I18n.T("window.backdrop.solid");
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
            (backdrop, supported, label) = (null, false, I18n.T("window.backdrop.failed") + ": " + ex.Message);
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

        _backdropLine = I18n.T("window.backdrop.line",
            ("label", label), ("supported", supported), ("glass", glass));
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
                Title = I18n.T("demo.title");
            }

            TabStrip.ItemsSource = _viewModel.Tabs;

            // ⚠️ 事件只订阅一次：`LoadData` 只在构造函数里调一次，
            //    但**别在这里重复写 `+=`** —— 曾经把 `TabDraggedOver` 写了两遍，
            //    跨页拖动时 `OnTabDraggedOver` 会被调用两次（多出来的那次是空转，但语义已经错了）。
            TabStrip.TabSelected += OnTabSelected;
            TabStrip.TabDraggedOver += OnTabDraggedOver;

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

        ApplySearch(_searchQuery);   // 数据重建后重新下发查询（导入/重载后页面与标签页都是新的）
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

        // 切回一个仍处在"批量管理模式"的网格页时，把那条提示重新显示出来
        // （否则提示没了、但页面上的批量条还在，看着像两套状态）
        if (page is GridPage { IsBulkMode: true })
        {
            _transientStatus = BulkDelete.StatusOnText;
        }

        UpdateSearchUi();   // 搜索栏上的「匹配 N / M」跟着当前页走
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
        // 页面创建时就要套一次主题与图标大小：
        // 页里的固定资源键（落点指示线）不会自己变，图块尺寸也得按当前档起始。
        void ApplyThemeIfAnimated(UIElement page)
        {
            if (page is IAnimatedPage animated)
            {
                animated.ApplyTheme(_themePalette);
            }

            if (page is IIconSizedPage sized)
            {
                sized.ApplyIconSize(IconSizeMetrics.For((_settingsData ?? new AppSettings()).IconSize));
            }
        }

        if (tab.IsList)
        {
            var listPage = new ListViewPage(tab) { DragDropEnabled = !_isDemo };
            listPage.ItemActivated += OnListItemActivated;
            listPage.ItemDropped += OnItemDropped;
            listPage.ItemMenuActionRequested += OnListItemMenuActionRequested;
            listPage.NewListItemRequested += async (_, _) => await ShowCreateListItemAsync(listPage);
            ApplyThemeIfAnimated(listPage);
            return listPage;
        }

        var gridPage = new GridPage(tab) { DragDropEnabled = !_isDemo, IconEditingEnabled = !_isDemo };
        gridPage.IconActivated += OnIconActivated;
        gridPage.ItemDropped += OnItemDropped;
        gridPage.NewIconRequested += OnNewIconRequested;
        gridPage.IconMenuActionRequested += OnIconMenuActionRequested;
        gridPage.FilesDropped += OnFilesDropped;
        gridPage.BulkDeleteRequested += OnBulkDeleteRequested;
        gridPage.BulkModeChanged += OnBulkModeChanged;
        ApplyThemeIfAnimated(gridPage);
        return gridPage;
    }

    // ────────────────────────────── 列表页行菜单（W5）──────────────────────────────
    //
    // 菜单规格与字段规则全在 Core（`ListItemContextMenu` / `ListItemEditor`，都有单测）；
    // 这里只管"每个动作具体怎么做"—— 与图块那一套完全对称。

    private async void OnListItemMenuActionRequested(object? sender, ListItemMenuRequest request)
    {
        if (_viewModel is null || sender is not ListViewPage page)
        {
            return;
        }

        switch (request.Action)
        {
            case ListItemMenuAction.EditProperties:
                await ShowEditListItemAsync(page, request.Item);
                return;

            case ListItemMenuAction.Rename:
                await ShowRenameListItemAsync(page, request.Item);
                return;

            case ListItemMenuAction.Remove:
                await ConfirmRemoveListItemAsync(page, request.Item);
                return;
        }
    }

    /// <summary>空白处右键 → 新建列表项（原版语义：直接弹对话框）。</summary>
    private async Task ShowCreateListItemAsync(ListViewPage page)
    {
        if (_isDemo)
        {
            ReportTransient(I18n.T("demo.no_save"));
            return;
        }

        try
        {
            var dialog = ListItemEditDialog.CreateForNew(AppWindow.Id);
            dialog.XamlRoot = RootGrid.XamlRoot;
            dialog.RequestedTheme = CurrentElementTheme;

            if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is not { } values)
            {
                return;
            }

            var item = ListItemEditor.Create(values.Description, values.Path);
            if (item is null || _viewModel?.AddListItem(page.Tab.Id, item) != true)
            {
                ReportTransient(ListItemEditor.ErrorBothEmpty);
                return;
            }

            _log.AppendLine($"新建列表项：{ListItemEditor.DisplayName(item.Description)} → {item.Path}");
            ReportTransient(ListItemEditor.ItemAddedText(ListItemEditor.DisplayName(item.Description)));
            FlushLog();
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.ShowCreateListItemAsync", ex);
        }
    }

    /// <summary>编辑属性…：改说明与路径。</summary>
    private async Task ShowEditListItemAsync(ListViewPage page, ListItemModel item)
    {
        if (_isDemo)
        {
            ReportTransient(I18n.T("demo.no_save"));
            return;
        }

        try
        {
            var dialog = ListItemEditDialog.CreateForEdit(AppWindow.Id, item.Description, item.Path);
            dialog.XamlRoot = RootGrid.XamlRoot;
            dialog.RequestedTheme = CurrentElementTheme;

            if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Result is not { } values)
            {
                return;
            }

            // 两个字段都为空 ⇒ 不改（Core 里判，规则只一份）
            if (!ListItemEditor.TryApplyEdit(item, values.Description, values.Path))
            {
                ReportTransient(ListItemEditor.ErrorBothEmpty);
                return;
            }

            _viewModel?.UpdateListItem(item);
            page.Tab.ListItems.FirstOrDefault(row => row.Model.Id == item.Id)?.Refresh();

            _log.AppendLine($"编辑列表项：{ListItemEditor.DisplayName(item.Description)} → {item.Path}");
            ReportTransient(ListItemEditor.DescriptionUpdatedText);
            FlushLog();
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.ShowEditListItemAsync", ex);
        }
    }

    /// <summary>重命名：只改说明（路径不动）。</summary>
    private async Task ShowRenameListItemAsync(ListViewPage page, ListItemModel item)
    {
        if (_isDemo)
        {
            ReportTransient(I18n.T("demo.no_save"));
            return;
        }

        try
        {
            var dialog = RenameListItemDialog.Create(item.Description);
            dialog.XamlRoot = RootGrid.XamlRoot;
            dialog.RequestedTheme = CurrentElementTheme;

            if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.NewDescription is not { } description)
            {
                return;
            }

            if (!ListItemEditor.TryRename(item, description))
            {
                ReportTransient(ListItemEditor.ErrorNameRequired);
                return;
            }

            _viewModel?.RenameListItem(item);
            page.Tab.ListItems.FirstOrDefault(row => row.Model.Id == item.Id)?.Refresh();

            _log.AppendLine($"重命名列表项：{description}");
            ReportTransient(ListItemEditor.DescriptionUpdatedText);
            FlushLog();
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.ShowRenameListItemAsync", ex);
        }
    }

    /// <summary>删除：二次确认（说明为空时文案用「此行」兜底，照原版）。</summary>
    private async Task ConfirmRemoveListItemAsync(ListViewPage page, ListItemModel item)
    {
        if (_isDemo)
        {
            ReportTransient(I18n.T("demo.no_save"));
            return;
        }

        try
        {
            var confirmed = await ConfirmAsync(
                ListItemEditor.DeleteTitle,
                ListItemEditor.DeleteConfirmText(item.Description),
                ListItemContextMenu.LabelRemove);
            if (!confirmed)
            {
                return;
            }

            var name = ListItemEditor.DisplayName(item.Description);
            if (_viewModel?.RemoveListItem(item) == true)
            {
                _log.AppendLine($"删除列表项：{name}");
                ReportTransient(ListItemEditor.RemovedText(name));
                FlushLog();
            }
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.ConfirmRemoveListItemAsync", ex);
        }
    }

    // ────────────────────────────── 批量管理（勾选多项删除，W5）──────────────────────────────    //
    // 判定与文案全在 Core 的 `BulkDelete`（有单测）；批量落库在 `DataStore.RemoveIcons`（一次落盘）。
    // 这里只负责：二次确认 → 执行 → 反馈，以及状态栏提示。

    private void OnBulkModeChanged(object? sender, bool on)
    {
        if (on)
        {
            ReportTransient(BulkDelete.StatusOnText);
            return;
        }

        // 退出批量模式：清掉这条临时提示，状态栏回到常规文案（原版是 showMessage(ready) 同义）
        _transientStatus = null;
        UpdateStatusBar();
    }

    private async void OnBulkDeleteRequested(object? sender, IReadOnlyList<string> checkedIds)
    {
        try
        {
            if (sender is not GridPage page || _viewModel is null)
            {
                return;
            }

            // ⚠️ 确认框里的数量必须是"真要删的数量"：把界面勾选**按这一页收敛**一遍
            //    （界面可能残留已不在本页的 id，直接数会多算，见 Core 的 BulkDelete.ResolveSelection）
            var effective = BulkDelete.ResolveSelection(page.Tab.Model, checkedIds);
            if (effective.Count == 0)
            {
                ReportTransient(BulkDelete.NoneCheckedText);
                return;
            }

            var confirmed = await ConfirmAsync(
                BulkDelete.ConfirmTitle,
                BulkDelete.ConfirmText(effective.Count),
                IconContextMenu.LabelRemove);
            if (!confirmed)
            {
                return;
            }

            var removed = _viewModel.RemoveIcons(page.Tab.Id, effective);

            if (removed > 0)
            {
                page.LeaveBulkModeAfterDelete();
                page.Tab.NotifyCountLabel();
                _log.AppendLine($"批量删除落库：{page.Tab.Name} 删除 {removed} 个图标");
                ReportTransient(BulkDelete.DoneText(removed));
            }
            else
            {
                ReportTransient(_isDemo ? I18n.T("demo.no_save") : BulkDelete.NoneCheckedText);
            }

            FlushLog();
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.OnBulkDeleteRequested", ex);
        }
    }

    // ────────────────────────────── 拖入添加图标（W5）──────────────────────────────

    /// <summary>
    /// 从资源管理器拖入文件/文件夹/快捷方式 → 建图标（原版 <c>tab_widget._add_dropped_paths</c>）。
    /// 判定在 Core（<see cref="DropImporter"/>，有单测）；这里只负责执行与状态栏反馈。
    /// </summary>
    private void OnFilesDropped(object? sender, IReadOnlyList<string> paths)
    {
        if (_viewModel is null || sender is not GridPage page)
        {
            return;
        }

        if (_isDemo)
        {
            ReportTransient(I18n.T("demo.no_save"));
            return;
        }

        try
        {
            var result = _viewModel.AddDroppedPaths(page.Tab.Id, paths);
            if (result.Error is not null)
            {
                ReportTransient(result.Error);
                return;
            }

            _log.AppendLine($"拖入添加：{result.Added}/{paths.Count} 个（{page.Tab.Name}）");
            foreach (var message in result.Messages)
            {
                _log.AppendLine("    " + message);
            }

            // 原版是"每处理一个就发一条状态消息"；状态栏单行，这里用 · 串起来（过长自动省略号）
            ReportTransient(result.Messages.Count == 0
                ? I18n.T("status.nothing_to_add")
                : string.Join("   ·   ", result.Messages));
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.OnFilesDropped", ex);
            ReportTransient(I18n.T("status.drop_failed", ("err", ex.Message)));
        }
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
            _transientStatus = I18n.T("status.tab_rejects", ("name", target.Name));
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
            ? I18n.T("status.moved_to", ("item", DescribeDroppedItem(request.Payload)), ("tab", name))
            : I18n.T("status.action_failed_detail", ("action", I18n.T("action.move")), ("err", result.Reason));

        if (result.Success)
        {
            // ⚠️ 用落库后**解析过的**位置：过滤态下界面给的是"可见位"，Core 收到的是换算后的下标
            _log.AppendLine($"拖放落库：{request.Payload} → 页={name} 位置={result.Request.TargetIndex}");
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

    // ────────────────────────────── 新建 / 编辑图标（W5）──────────────────────────────
    //
    // 交互照原版 v1.11.6（icon_grid._show_icon_context_menu / tab_widget._show_grid_context_menu）：
    //   ① 空白处右键 → 选类型；
    //   ② 文件 / 文件夹 / 快捷方式**先弹系统选择框**，选完把名称与路径预填进字段对话框
    //      （网址 / 命令没有对应的选择框，直接进对话框）；
    //   ③ 「确定」→ Core（IconEditor）校验 → 落库 → 按 Core 给的刷新计划重取图标。
    //
    // ⚠️ 校验规则的唯一来源是 Core；对话框里校验不过**不关窗**，所以这里拿到的 Draft 一定是合法的。

    /// <summary>空白处菜单选了「新建 XX 图标…」。</summary>
    private async void OnNewIconRequested(object? sender, IconType type)
    {
        if (sender is not GridPage page || _viewModel is null)
        {
            return;
        }

        if (_isDemo)
        {
            ReportTransient(I18n.T("demo.no_save"));
            return;
        }

        try
        {
            var prefill = await BuildCreatePrefillAsync(type);
            if (prefill is null)
            {
                return;   // 用户在系统选择框里取消了
            }

            var dialog = IconEditDialog.ForCreate(type, prefill, AppWindow.Id);
            dialog.XamlRoot = RootGrid.XamlRoot;
            dialog.RequestedTheme = CurrentElementTheme;   // ⚠️ 不设就永远用系统主题（E18，与 About 同病）

            if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Draft is not { } draft)
            {
                return;
            }

            var result = _viewModel.CreateIcon(page.Tab.Id, draft);
            ReportIconEdit(result, draft.DisplayName, created: true);
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.OnNewIconRequested", ex);
            ReportTransient(I18n.T("status.action_failed_detail",
                ("action", I18n.T("action.create")), ("err", ex.Message)));
        }
    }

    /// <summary>
    /// 图块右键菜单的**统一入口**：按 Core 给的动作分发。
    ///
    /// <para>菜单项的顺序、按类型的门控、文案都在 Core（<see cref="IconContextMenu"/>，有单测），
    /// 这里只管"每个动作具体怎么做"。</para>
    /// </summary>
    private async void OnIconMenuActionRequested(object? sender, IconMenuRequest request)
    {
        if (_viewModel is null)
        {
            return;
        }

        switch (request.Action)
        {
            case IconMenuAction.Open:
                OnIconActivated(this, request.Icon);      // 与"点一下图块"完全同一条路
                return;

            case IconMenuAction.OpenWith:
                ReportMenuLaunch(_viewModel.OpenWith(request.Icon), request.Icon, IconContextMenu.LabelOpenWith);
                return;

            case IconMenuAction.OpenLocation:
                ReportMenuLaunch(_viewModel.OpenFileLocation(request.Icon), request.Icon, IconContextMenu.LabelOpenLocation);
                return;

            case IconMenuAction.EditProperties:
                await ShowEditIconAsync(request.Icon);
                return;

            case IconMenuAction.Rename:
                await ShowRenameIconAsync(request.Icon);
                return;

            case IconMenuAction.Remove:
                await ConfirmRemoveIconAsync(request.Icon);
                return;
        }
    }

    /// <summary>
    /// 打开类动作的反馈：**成功不打扰**（资源管理器/「打开方式」窗口本身就是反馈，与原版一致 ——
    /// 原版这两个动作成功时不发状态消息），失败才写状态栏，文案来自 Core（与原版 i18n 一致）。
    /// </summary>
    private void ReportMenuLaunch(LaunchResult result, IconModel icon, string actionLabel)
    {
        var name = string.IsNullOrWhiteSpace(icon.DisplayName) ? icon.SourcePath : icon.DisplayName;

        if (result.Success)
        {
            _log.AppendLine($"{actionLabel}：{name}");
            FlushLog();
            return;
        }

        ReportTransient(result.Error ?? I18n.T("status.action_failed", ("action", actionLabel)));
        _log.AppendLine($"{actionLabel}失败：{name} → {result.Error}");
        FlushLog();
    }

    /// <summary>图块菜单选了「编辑属性…」。</summary>
    private async Task ShowEditIconAsync(IconModel icon)
    {
        if (_isDemo)
        {
            ReportTransient(I18n.T("demo.no_save"));
            return;
        }

        try
        {
            var dialog = IconEditDialog.ForEdit(icon, AppWindow.Id);
            dialog.XamlRoot = RootGrid.XamlRoot;
            dialog.RequestedTheme = CurrentElementTheme;   // ⚠️ 不设就永远用系统主题（E18，与 About 同病）
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Draft is not { } draft)
            {
                return;
            }

            var result = _viewModel!.UpdateIcon(icon, draft);
            ReportIconEdit(result, draft.DisplayName, created: false);
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.ShowEditIconAsync", ex);
            ReportTransient(I18n.T("status.action_failed_detail",
                ("action", I18n.T("action.edit")), ("err", ex.Message)));
        }
    }

    /// <summary>图块菜单选了「重命名」（原版是标签内联编辑，这里用迷你对话框）。</summary>
    private async Task ShowRenameIconAsync(IconModel icon)
    {
        if (_isDemo)
        {
            ReportTransient(I18n.T("demo.no_save"));
            return;
        }

        try
        {
            var dialog = RenameIconDialog.Create(icon.DisplayName);
            dialog.XamlRoot = RootGrid.XamlRoot;
            dialog.RequestedTheme = CurrentElementTheme;   // ⚠️ 同 E18
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.NewName is not { } newName)
            {
                return;
            }

            var result = _viewModel!.RenameIcon(icon, newName);
            if (!result.Success)
            {
                ReportTransient(result.ErrorMessage ?? I18n.T("status.rename_failed"));
                return;
            }

            _log.AppendLine($"重命名图标：{newName}");
            ReportTransient(IconContextMenu.RenamedStatus(newName));
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.ShowRenameIconAsync", ex);
            ReportTransient(I18n.T("status.action_failed_detail",
                ("action", I18n.T("action.rename")), ("err", ex.Message)));
        }
    }

    /// <summary>图块菜单选了「删除」：**先确认再删**（原版是 QMessageBox 的 Yes/No，文案照抄）。</summary>
    private async Task ConfirmRemoveIconAsync(IconModel icon)
    {
        if (_isDemo)
        {
            ReportTransient(I18n.T("demo.no_save"));
            return;
        }

        try
        {
            var confirmed = await ConfirmAsync(
                IconContextMenu.RemoveTitle,
                IconContextMenu.ConfirmRemoveText(icon.DisplayName),
                IconContextMenu.LabelRemove);

            if (!confirmed || !_viewModel!.RemoveIcon(icon))
            {
                return;
            }

            _log.AppendLine($"删除图标：{icon.DisplayName}");
            ReportTransient(IconContextMenu.RemovedStatus(icon.DisplayName));
        }
        catch (Exception ex)
        {
            App.WriteCrash("MainWindow.ConfirmRemoveIconAsync", ex);
            ReportTransient(I18n.T("status.action_failed_detail",
                ("action", I18n.T("action.delete")), ("err", ex.Message)));
        }
    }

    /// <summary>
    /// 通用确认框（是/否）。
    /// ⚠️ 同样是 ContentDialog ⇒ 必须显式设 `XamlRoot` 与 `RequestedTheme`（否则不跟主题，ERROR.md E18）；
    /// 默认按钮设成"取消"，避免回车误删。
    /// </summary>
    private async Task<bool> ConfirmAsync(string title, string message, string primaryText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primaryText,
            CloseButtonText = I18n.T("btn.cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = CurrentElementTheme,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>
    /// 新建时先选目标（原版也是这样：选择框在对话框**之前**），返回预填好的草稿。
    /// 返回 null = 用户取消，什么都不做。
    /// </summary>
    private async Task<IconEditDraft?> BuildCreatePrefillAsync(IconType type)
    {
        switch (type)
        {
            case IconType.File:
            {
                var path = await FilePickers.PickFileAsync(AppWindow.Id, null);
                return path is null
                    ? null
                    : new IconEditDraft { Type = IconType.File, DisplayName = StemOf(path), Path = path };
            }

            case IconType.Folder:
            {
                var path = await FilePickers.PickFolderAsync(AppWindow.Id);
                return path is null
                    ? null
                    : new IconEditDraft { Type = IconType.Folder, DisplayName = FolderNameOf(path), Path = path };
            }

            case IconType.Shortcut:
            {
                var lnk = await FilePickers.PickFileAsync(AppWindow.Id, new[] { ".lnk" });
                if (lnk is null)
                {
                    return null;
                }

                // 解析 .lnk 拿目标/参数/工作目录做预填；解析失败也不拦着用户（目标退回 .lnk 自己）
                var info = WindowsShortcut.Resolve(lnk);
                return new IconEditDraft
                {
                    Type = IconType.Shortcut,
                    DisplayName = StemOf(lnk),
                    Path = string.IsNullOrWhiteSpace(info?.TargetPath) ? lnk : info!.TargetPath,
                    ShortcutSourcePath = lnk,
                    Arguments = info?.Arguments ?? string.Empty,
                    WorkingDir = info?.WorkingDirectory ?? string.Empty,
                };
            }

            default:
                // 网址 / 命令：没有对应的系统选择框，直接进对话框（原版一致）
                return new IconEditDraft { Type = type };
        }
    }

    private string StemOf(string path)
    {
        try
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrEmpty(stem) ? path : stem;
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private string FolderNameOf(string path)
    {
        try
        {
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? path : name;
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    /// <summary>把一次新建/编辑的结果写进状态栏与自检日志（成功/失败都写）。</summary>
    private void ReportIconEdit(IconEditResult result, string name, bool created)
    {
        if (!result.Success)
        {
            ReportTransient(result.ErrorMessage ?? I18n.T("progress.failed_short"));
            _log.AppendLine($"{(created ? "新建" : "编辑")}图标失败：{result.ErrorMessage}");
            FlushLog();
            return;
        }

        _log.AppendLine($"{(created ? "新建" : "编辑")}图标：{name}（图标刷新={result.Refresh.Kind}）");
        ReportTransient(created
            ? I18n.T("status.added", ("name", name))
            : I18n.T("status.edited", ("name", name)));
    }

    private void ReportTransient(string message)
    {
        _transientStatus = message;
        UpdateStatusBar();
        FlushLog();
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
        _transientStatus = result.Success
            ? I18n.T("status.open_short", ("name", name))
            : I18n.T("status.open_failed_short", ("name", name), ("err", result.Error));
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
            ? I18n.T("status.open_short", ("name", item.Path))
            : I18n.T("status.open_failed_short", ("name", item.Path), ("err", result.Error));
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
