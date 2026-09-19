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

    public SettingsPanel()
    {
        InitializeComponent();
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

    /// <summary>绑定设置与存储（面板只读这两者的引用，不接管生命周期）。</summary>
    public void Bind(SettingsStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        SyncUiFromModel();
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
            UiThemeChoices.SelectedIndex = _settings.UiTheme switch
            {
                ThemeMode.Light => 1,
                ThemeMode.Dark => 2,
                _ => 0,
            };

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
    }

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

    private void OnUiThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UiThemeChoices.SelectedItem is RadioButton { Tag: string wire })
        {
            Apply(s => s.UiTheme = ThemeTokens.ParseMode(wire));
        }
    }

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
