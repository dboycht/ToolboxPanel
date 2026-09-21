// SettingsPanel.xaml.cs —— 设置面板
//
// 职责边界：
//   · 面板负责「控件 ↔ AppSettings」的双向同步 + 立刻落盘；
//   · 主窗口负责「把设置套用到界面」（材质、标签栏、页面动效）——通过 SettingApplied 事件。
// 这样设置项增多时只改这一个文件，主窗口不必知道每个控件的存在。

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using ToolboxPanel.Core;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Views;

public sealed partial class SettingsPanel : UserControl
{
    private SettingsStore? _store;
    private AppSettings? _settings;
    private bool _syncingUi;
    private string? _dataDirectory;

    /// <summary>当前生效的主题解析结果（由宿主下发）—— 滑杆显示的是**生效值**，不是"文件里写过什么"。</summary>
    private ThemeResolution? _theme;

    /// <summary>「外观微调」的滑杆/标签/数值文字，按参数 key 索引（只建一次）。</summary>
    private readonly Dictionary<string, Slider> _tuningSliders = new(StringComparer.Ordinal);

    private readonly Dictionary<string, TextBlock> _tuningLabels = new(StringComparer.Ordinal);

    private readonly Dictionary<string, TextBlock> _tuningValues = new(StringComparer.Ordinal);

    private bool _tuningBuilt;

    public SettingsPanel()
    {
        InitializeComponent();
        BuildTuningPanel();
    }

    /// <summary>某项设置已改并落盘 —— 宿主据此重新套用界面。</summary>
    public event EventHandler? SettingApplied;

    /// <summary>点「预览动效」—— 宿主重放当前页的入场动效。</summary>
    public event EventHandler? PreviewRequested;

    /// <summary>点关闭 —— 宿主收起面板。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>点「导出备份…」—— 宿主负责选目录、跑进度、反馈（面板不碰文件 IO）。</summary>
    public event EventHandler? ExportBackupRequested;

    /// <summary>点「导入备份…」—— 宿主负责选文件、二次确认、跑进度、重载。</summary>
    public event EventHandler? ImportBackupRequested;

    /// <summary>
    /// 点「重置数据」—— 宿主负责二次确认、清空数据、重建界面。
    /// <para>⚠️ 与 <see cref="ResetToDefaultsRequested"/>（恢复默认**设置**）是两件事：
    /// 这个删的是**内容**（所有标签页与图标缓存）。</para>
    /// </summary>
    public event EventHandler? ResetDataRequested;

    /// <summary>
    /// 点「快捷键设置」那一节的「编辑」—— 宿主负责弹「快捷键设置」窗口（面板不碰窗口与持久化）。
    /// <para>⚠️ 入口只在这里（2026-09-19 用户决定从标题栏搬进来）：用户找设置的第一反应是开这个面板，
    /// 摆在标题栏的 ❓ 图标上根本找不到（用户实际反馈："快捷键在哪里设置？"）。</para>
    /// </summary>
    public event EventHandler? ShortcutSettingsRequested;

    /// <summary>绑定设置与存储（面板只读这两者的引用，不接管生命周期）。</summary>
    public void Bind(SettingsStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        SyncUiFromModel();
    }

    /// <summary>
    /// 宿主下发"当前生效的主题"（生效预置 + 解析后的 8 个参数）。
    /// <para>⚠️ 为什么不由面板自己算：生效预置要看"系统是不是深色"（跟随系统那一档），
    /// 那是窗口才知道的事；面板只该显示结果（与"界面主题"一节同一条纪律：
    /// 同一份值只有一个来源，别两处各算一遍）。</para>
    /// </summary>
    public void ApplyThemeResolution(ThemeResolution resolution)
    {
        _theme = resolution;
        SyncTuningFromModel();
    }

    /// <summary>显示数据目录（"数据"一节里的那行小字）—— 由宿主传入，面板不去定位路径。</summary>
    public void SetDataDirectory(string? path)
    {
        _dataDirectory = path;
        RefreshDataDirectoryText();
    }

    /// <summary>
    /// 语言切换后由宿主调用：重写"由代码设置的"那几处文字。
    /// <para>⚠️ XAML 里标了 <c>ui:Tr.Key</c> 的静态文案由 `Tr.RefreshAll()` 统一重刷，
    /// 不在这里重复处理；这里只管 XAML 表达不了的（ToggleSwitch 的开/关文字、带占位符的提示行）。</para>
    /// </summary>
    public void ApplyLanguage()
    {
        ShowCountsSwitch.OnContent = I18n.T("settings.on");
        ShowCountsSwitch.OffContent = I18n.T("settings.off");
        AnimationSwitch.OnContent = I18n.T("settings.animations.on");
        AnimationSwitch.OffContent = I18n.T("settings.animations.off");
        RefreshDataDirectoryText();
        RefreshTuningLabels();   // 参数名来自 Core 的规格表（按语言取），不在 XAML 里
    }

    private void RefreshDataDirectoryText()
        => DataDirText.Text = I18n.T("settings.data_dir",
            ("path", _dataDirectory ?? I18n.T("about.unlocated")));

    private void OnExportBackupClick(object sender, RoutedEventArgs e)
        => ExportBackupRequested?.Invoke(this, EventArgs.Empty);

    private void OnImportBackupClick(object sender, RoutedEventArgs e)
        => ImportBackupRequested?.Invoke(this, EventArgs.Empty);

    private void OnResetDataClick(object sender, RoutedEventArgs e)
        => ResetDataRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>点「快捷键设置」→ 请宿主弹窗。
    /// <para>`internal`：自检探针走同一条链路（不必模拟鼠标点击）。</para></summary>
    internal void OnShortcutSettingsClick(object sender, RoutedEventArgs e)
        => ShortcutSettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>外部（如命令行的临时覆盖）改了模型后，让面板重新显示一次。</summary>
    public void Refresh() => SyncUiFromModel();


    // ────────────────────────────── 模型 → 控件 ──────────────────────────────

    private void SyncUiFromModel()
    {
        if (_settings is null)
        {
            return;
        }

        _syncingUi = true;
        try
        {
            // 「跟随系统」+ 5 个预置（顺序必须与 XAML 里那 6 个 RadioButton 一致 —— 见 ThemeChoiceOrder）
            ThemeChoices.SelectedIndex = Math.Max(0, Array.IndexOf(ThemeChoiceOrder, _settings.ThemeChoice));

            BackdropBox.SelectedIndex = Array.IndexOf(
                BackdropKinds.All, _settings.Backdrop) is var backdropIndex && backdropIndex >= 0
                ? backdropIndex
                : 0;

            TabIconModeChoices.SelectedIndex = _settings.TabIconMode switch
            {
                TabIconMode.Text => 0,
                TabIconMode.Always => 1,
                _ => 2,
            };

            ShowCountsSwitch.IsOn = _settings.ShowTabCounts;

            // 图标大小：只认三档（Core 的 IconSizeMetrics 归一化，未知一律 medium）
            IconSizeChoices.SelectedIndex = IconSizeMetrics.For(_settings.IconSize).Name switch
            {
                "small" => 0,
                "large" => 2,
                _ => 1,
            };

            AnimationSwitch.IsOn = _settings.AnimationsEnabled;
            DurationSlider.Value = _settings.AnimationDurationMs;
            EasingBox.SelectedIndex = _settings.AnimationEasing switch
            {
                AnimationEasing.Soft => 0,
                AnimationEasing.Snappy => 2,
                _ => 1,
            };

            LanguageChoices.SelectedIndex = I18n.Normalize(_settings.Language) == "en" ? 1 : 0;
        }
        finally
        {
            _syncingUi = false;
        }

        // StackPanel 不是 Control，没有 IsEnabled —— 用"不吃命中 + 变淡"表达禁用
        AnimationDetails.IsHitTestVisible = _settings.AnimationsEnabled;
        AnimationDetails.Opacity = _settings.AnimationsEnabled ? 1 : 0.45;

        SyncTuningFromModel();
    }

    /// <summary>「外观微调」6 个选项的顺序（`system` + 5 个预置）—— 必须与 XAML 里那 6 个 RadioButton 一致。</summary>
    private static readonly string[] ThemeChoiceOrder =
        new[] { ThemePresets.SystemId }.Concat(ThemePresets.Ids).ToArray();

    // ────────────────────────────── 控件 → 模型 ──────────────────────────────

    private void Apply(Action<AppSettings> change)
    {
        if (_syncingUi || _store is null || _settings is null)
        {
            return;
        }

        change(_settings);
        _store.Save(_settings);          // 改完即存（与原版"设置即时持久化"一致）
        // StackPanel 不是 Control，没有 IsEnabled —— 用"不吃命中 + 变淡"表达禁用
        AnimationDetails.IsHitTestVisible = _settings.AnimationsEnabled;
        AnimationDetails.Opacity = _settings.AnimationsEnabled ? 1 : 0.45;
        SettingApplied?.Invoke(this, EventArgs.Empty);
    }

    private void OnBackdropChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackdropBox.SelectedItem is ComboBoxItem { Tag: string kind })
        {
            Apply(s => s.Backdrop = kind);
        }
    }

    // ────────────────────────────── 主题 / 外观微调（v2.0.6）──────────────────────────────
    //
    // 两层结构，别混：
    //   · 「预置主题」= 5 个预置 + 跟随系统（内存里就是 `theme` 与 `ui_theme` 两个字段，见 AppSettings）；
    //   · 「外观微调」= 8 个参数里的 6 个，滑杆**按 Core 的规格表生成**（ThemeParamSpecs.Adjustable），
    //     值存在 `theme_overrides` 的 `param:<key>`（与原版/QML 线完全同一套键名 ⇒ 双向兼容）。
    //
    // ⚠️ 滑杆的值一律来自宿主下发的**生效值**（`_theme.Params`）：这样"切预置之后滑杆跟着走到新预置的
    //    默认位置"，用户看到的就是当前真实生效的参数，而不是"文件里写过什么"。

    /// <summary>点某个主题选项：跟随系统 ⇒ 写 `ui_theme=system`；具体预置 ⇒ 同时锁定明暗。</summary>
    private void OnThemeChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeChoices.SelectedItem is not RadioButton { Tag: string choice })
        {
            return;
        }

        Apply(s =>
        {
            if (choice == ThemePresets.SystemId)
            {
                s.UiTheme = ThemeMode.System;
            }
            else
            {
                s.SelectPreset(choice);
            }
        });
    }

    /// <summary>按 Core 的规格表把「外观微调」的控件树建起来（只建一次）。</summary>
    private void BuildTuningPanel()
    {
        if (_tuningBuilt)
        {
            return;
        }

        _tuningBuilt = true;

        foreach (var spec in ThemeParamSpecs.Adjustable)
        {
            var label = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var valueText = new TextBlock
            {
                FontSize = 11,
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var header = new Grid();
            header.Children.Add(label);
            header.Children.Add(valueText);

            var slider = new Slider
            {
                Minimum = spec.UiMin,
                Maximum = spec.UiMax,
                StepFrequency = spec.Step,
                SmallChange = spec.Step,
                LargeChange = spec.Step * 5,
            };
            slider.ValueChanged += (_, args) => OnTuningChanged(spec, args.NewValue);

            var block = new StackPanel { Spacing = 2 };
            block.Children.Add(header);
            block.Children.Add(slider);

            TuningPanel.Children.Add(block);

            _tuningSliders[spec.Key] = slider;
            _tuningLabels[spec.Key] = label;
            _tuningValues[spec.Key] = valueText;
        }

        RefreshTuningLabels();
    }

    /// <summary>参数名按当前语言写一遍（名字来自 Core 的规格表，与 XAML 无关）。</summary>
    private void RefreshTuningLabels()
    {
        foreach (var spec in ThemeParamSpecs.Adjustable)
        {
            if (_tuningLabels.TryGetValue(spec.Key, out var label))
            {
                label.Text = spec.Label(I18n.Current);
            }
        }
    }

    /// <summary>把"生效值"写到滑杆与数值文字上（内部会打开 `_syncingUi` 闸门，不会反向落盘）。</summary>
    private void SyncTuningFromModel()
    {
        if (!_tuningBuilt || _theme is null)
        {
            return;
        }

        bool previous = _syncingUi;
        _syncingUi = true;
        try
        {
            foreach (var spec in ThemeParamSpecs.Adjustable)
            {
                var value = _theme.Params.Get(spec.Key);

                if (_tuningSliders.TryGetValue(spec.Key, out var slider)
                    && Math.Abs(slider.Value - value) > spec.Step / 2)
                {
                    slider.Value = value;   // 只在"真的不一样"时写：免得拖到一半被自己弹回去
                }

                if (_tuningValues.TryGetValue(spec.Key, out var text))
                {
                    text.Text = FormatParamValue(spec, value);
                }
            }
        }
        finally
        {
            _syncingUi = previous;
        }
    }

    /// <summary>参数值的显示格式：步长小于 1 的按两位小数（不透明度），其余取整（圆角 / 毫秒）。</summary>
    private static string FormatParamValue(ThemeParamSpec spec, double value)
        => spec.Step < 1 ? value.ToString("0.00") : value.ToString("0");

    /// <summary>拖动某个滑杆 —— 与预置默认值相同的就**不写覆盖**（保持 config.json 干净）。</summary>
    private void OnTuningChanged(ThemeParamSpec spec, double value)
    {
        if (_syncingUi || _settings is null)
        {
            return;
        }

        var presetDefault = _theme?.Preset.ParamOrDefault(spec.Key) ?? spec.Default;

        Apply(s =>
        {
            if (Math.Abs(value - presetDefault) <= spec.Step / 2)
            {
                s.RemoveThemeParamOverride(spec.Key);
            }
            else
            {
                s.SetThemeParamOverride(spec.Key, value);
            }
        });
    }

    /// <summary>「恢复默认外观」= 清掉全部主题细调（颜色 + 参数），回到当前预置的原样。</summary>
    private void OnResetTuningClick(object sender, RoutedEventArgs e)
        => Apply(s => s.ClearThemeOverrides());


    private void OnTabIconModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabIconModeChoices.SelectedItem is RadioButton { Tag: string wire })
        {
            Apply(s => s.TabIconMode = AppSettings.ParseTabIconMode(wire));
        }
    }

    private void OnShowCountsToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.ShowTabCounts = ShowCountsSwitch.IsOn);

    /// <summary>语言：只写线名（zh / en），由 Core 的 `I18n` 归一化（未知一律回落到默认语言）。</summary>
    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageChoices.SelectedItem is RadioButton { Tag: string wire })
        {
            Apply(s => s.Language = wire);
        }
    }

    /// <summary>图标大小：只写线名（small/medium/large），由 Core 的尺寸表解释。</summary>
    private void OnIconSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IconSizeChoices.SelectedItem is RadioButton { Tag: string wire })
        {
            Apply(s => s.IconSize = IconSizeMetrics.For(wire).WireName);
        }
    }

    private void OnAnimationsToggled(object sender, RoutedEventArgs e)
        => Apply(s => s.AnimationsEnabled = AnimationSwitch.IsOn);

    private void OnDurationChanged(object sender, RangeBaseValueChangedEventArgs e)
        => Apply(s => s.AnimationDurationMs = (int)Math.Round(e.NewValue));

    private void OnEasingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EasingBox.SelectedItem is ComboBoxItem { Tag: string wire })
        {
            Apply(s => s.AnimationEasing = AppSettings.ParseEasing(wire));
        }
    }

    private void OnPreviewClick(object sender, RoutedEventArgs e) => PreviewRequested?.Invoke(this, EventArgs.Empty);

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>恢复默认：就地重置当前设置对象（保持引用有效），并立刻落盘。</summary>
    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (_store is null || _settings is null)
        {
            return;
        }

        _settings.ResetToDefaults();
        _store.Save(_settings);
        SyncUiFromModel();
        SettingApplied?.Invoke(this, EventArgs.Empty);
    }
}
