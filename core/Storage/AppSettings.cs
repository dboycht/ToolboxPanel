// AppSettings.cs —— ToolboxPanel v2 · 设置层（W3 起：UI 偏好）
//
// 对应原 Python 的 src/toolbox/ui/settings.py（读写 data/config.json）。
//
// **必须与原版保持兼容的地方**（改坏会让旧版读不了 / 丢用户的主题细调）：
//   · 文件名与位置不变：`data/config.json`
//   · 既有字段语义不变：`language`(zh|en) / `icon_size`(small|medium|large) / `theme`(非空字符串)
//   · `theme_overrides` 的值可能是**字符串或数字**，必须原样保留（QML 线的主题细调存在这里）
//   · **原子写**（config.tmp → replace）；写出格式与 tabs.json 同一套
//     （无 BOM / CRLF / 2 空格缩进 / 中文不转义）
//   · 读取失败一律回落默认值，**绝不因为配置损坏而启动失败**（原版还会归一化非法取值）
//
// v2.0.1（C# 线）新增两个字段：
//   · `tab_icon_mode`：标签栏图标形态 —— "text"（只保留文字）/"always"（常驻）/"hover"（悬停浮现并拉伸）
//   · `animations`：界面动效总开关

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolboxPanel.Core.Storage;

/// <summary>标签栏图标的三种形态。</summary>
public enum TabIconMode
{
    /// <summary>只保留文字（与原版 v1.11.6 / QML 一致）。</summary>
    Text,

    /// <summary>常驻显示图标。</summary>
    Always,

    /// <summary>悬停时在文字旁浮现图标，标签整体拉伸。</summary>
    Hover,
}

/// <summary>动效曲线（只暴露三种观感，避免把缓动函数名暴露成设置项）。</summary>
public enum AnimationEasing
{
    /// <summary>柔和：SineEase / EaseOut。</summary>
    Soft,

    /// <summary>标准：CubicEase / EaseOut（默认）。</summary>
    Standard,

    /// <summary>干脆：QuadraticEase / EaseOut（起步快、收尾快）。</summary>
    Snappy,
}

/// <summary>交给界面用的动效参数（由设置换算而来，界面不再关心取值合法性）。</summary>
public readonly record struct AnimationSpec(
    bool Enabled,
    int DurationMs,
    int StaggerMs,
    AnimationEasing Easing)
{
    /// <summary>入场位移的下落距离（DIP）：跟时长挂钩，慢动画配大位移，观感更一致。</summary>
    public double FromOffset => Math.Clamp(DurationMs / 14.0, 4, 24);

    /// <summary>关掉动效时的"直给"参数。</summary>
    public static AnimationSpec Disabled => new(false, 0, 0, AnimationEasing.Standard);
}

/// <summary>可选的窗口材质名（与启动参数 <c>--backdrop=</c> 同一套取值）。</summary>
public static class BackdropKinds
{
    public const string Mica = "mica";
    public const string MicaAlt = "micaAlt";
    public const string Acrylic = "acrylic";
    public const string AcrylicThin = "acrylicThin";
    public const string None = "none";

    public static readonly string[] All = { Mica, MicaAlt, Acrylic, AcrylicThin, None };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Mica;
        }

        var match = All.FirstOrDefault(
            kind => string.Equals(kind, value.Trim(), StringComparison.OrdinalIgnoreCase));

        return match ?? Mica;
    }
}

/// <summary>config.json 的线上表示（字段名与原版一致）。</summary>
public sealed class AppSettings
{
    public const string DefaultLanguage = "zh";
    public const string DefaultIconSize = "medium";
    public const string DefaultTheme = "dark";

    public static readonly string[] Languages = { "zh", "en" };

    public static readonly string[] IconSizes = { "small", "medium", "large" };

    [JsonPropertyName("language")]
    public string Language { get; set; } = DefaultLanguage;

    [JsonPropertyName("icon_size")]
    public string IconSize { get; set; } = DefaultIconSize;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = DefaultTheme;

    /// <summary>主题令牌细调（键→字符串/数字）。原版与 QML 线都在用，**不要动它的内容**。</summary>
    [JsonPropertyName("theme_overrides")]
    public Dictionary<string, JsonElement>? ThemeOverrides { get; set; }

    /// <summary>
    /// 用户改过的快捷键（**差分存储**：动作名 → 键位串，只存与默认不同的那些）。
    ///
    /// <para>⚠️ 差分的好处：将来我们改了某个默认键位，**没动过那条的用户会自动跟上**；
    /// 若是全量存储，改默认值对老用户就永远无效了（见 <see cref="Services.ShortcutBindings.ToOverrides"/>）。</para>
    ///
    /// <para>⚠️ 旧版（v2.0.5 及更早）读到这个键会**原样保留**、不影响使用；
    /// 反过来本版读到没有这个键的旧配置也一切照旧（null = 全用默认）。</para>
    /// </summary>
    [JsonPropertyName("shortcut_bindings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? ShortcutBindings { get; set; }

    /// <summary>
    /// 标签栏图标形态的**线上值**：text | always | hover。
    /// ⚠️ 新字段刻意用「null = 文件里没有这个键」：这样**没被改过的配置写回时逐字节不变**
    /// （不会因为我们加了字段就去动用户的 config.json）。
    /// </summary>
    [JsonPropertyName("tab_icon_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TabIconModeRaw { get; set; }

    /// <summary>界面动效总开关的线上值（null = 文件里没有）。</summary>
    [JsonPropertyName("animations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AnimationsEnabledRaw { get; set; }

    /// <summary>窗口材质（mica | micaAlt | acrylic | acrylicThin | none；null = 默认 mica）。</summary>
    [JsonPropertyName("backdrop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackdropRaw { get; set; }

    /// <summary>是否显示标签栏上的数量（如「20 个图标」；null = 默认显示）。</summary>
    [JsonPropertyName("show_tab_counts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ShowTabCountsRaw { get; set; }

    /// <summary>动效时长（毫秒；null = 默认 220）。</summary>
    [JsonPropertyName("animation_duration_ms")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AnimationDurationMsRaw { get; set; }

    /// <summary>动效交错间隔（毫秒；null = 默认 24）。</summary>
    [JsonPropertyName("animation_stagger_ms")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AnimationStaggerMsRaw { get; set; }

    /// <summary>动效曲线（soft | standard | snappy；null = 默认 standard）。</summary>
    [JsonPropertyName("animation_easing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AnimationEasingRaw { get; set; }

    /// <summary>上次退出时的窗口宽/高（**物理像素**；null = 还没记录过）。</summary>
    [JsonPropertyName("window_width")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WindowWidthRaw { get; set; }

    [JsonPropertyName("window_height")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WindowHeightRaw { get; set; }

    /// <summary>
    /// **界面**主题（system | light | dark；null = 默认跟随系统）。
    ///
    /// <para>⚠️ 为什么另起一个字段而不复用既有的 <see cref="Theme"/>：
    /// 那个 <c>theme</c> 是老线/QML 线的**主题名**（非空字符串、配合 <c>theme_overrides</c> 做细调），
    /// 动它会直接破坏旧版兼容。这里新增 <c>ui_theme</c>，
    /// 旧版读得懂多余键、会原样保留，所以**降级回去也不丢**。</para>
    /// </summary>
    [JsonPropertyName("ui_theme")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UiThemeRaw { get; set; }

    /// <summary>未知字段原样保留（旧版/未来版本的键都不该被 C# 线吃掉）。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }

    public const int DefaultAnimationDurationMs = 220;

    public const int DefaultAnimationStaggerMs = 24;

    /// <summary>标签栏图标形态（未设置时默认 <see cref="TabIconMode.Hover"/>）。</summary>
    [JsonIgnore]
    public TabIconMode TabIconMode
    {
        get => ParseTabIconMode(TabIconModeRaw);
        set => TabIconModeRaw = ToWire(value);
    }

    /// <summary>界面动效总开关（未设置时默认开）。</summary>
    [JsonIgnore]
    public bool AnimationsEnabled
    {
        get => AnimationsEnabledRaw ?? true;
        set => AnimationsEnabledRaw = value;
    }

    /// <summary>窗口材质（未设置时默认 Mica）。</summary>
    [JsonIgnore]
    public string Backdrop
    {
        get => BackdropKinds.Normalize(BackdropRaw);
        set => BackdropRaw = BackdropKinds.Normalize(value);
    }

    /// <summary>界面主题（未设置时默认跟随系统）。</summary>
    [JsonIgnore]
    public ThemeMode UiTheme
    {
        get => ThemeTokens.ParseMode(UiThemeRaw);
        set => UiThemeRaw = ThemeTokens.ToWire(value);
    }

    /// <summary>
    /// 主题预置 id（`theme` 字段；认不出的值一律归一化到默认预置）。
    ///
    /// <para>⚠️ 这个字段是**老线/QML 线也在用的**（原版 `theme.py` 的 `PRESETS` 就存这儿），
    /// 所以取值集合必须与原版一致：dark / light / midnight / grape / matcha。</para>
    /// </summary>
    [JsonIgnore]
    public string ThemePresetId
    {
        get => ThemePresets.Normalize(Theme);
        set => Theme = ThemePresets.Normalize(value);
    }

    /// <summary>
    /// 界面上「预置主题」一节的选择：`system`（跟随系统）或某个预置 id。
    /// <para>它就是"当前选中项"，不是新的存储字段 —— 跟随系统看 <c>ui_theme</c>，预置看 <c>theme</c>。</para>
    /// <para>⚠️ 锁定明暗时返回的是**真正生效**的预置（预置与明暗冲突会退回中性预置，见
    /// <see cref="ThemeResolver.ResolvePreset(ThemeMode, string?, bool)"/>）——
    /// 面板上高亮的那一项必须与用户看到的一致，不能"选着葡萄紫、渲染的是浅色"。</para>
    /// </summary>
    [JsonIgnore]
    public string ThemeChoice => UiTheme == ThemeMode.System
        ? ThemePresets.SystemId
        : ThemeResolver.ResolvePreset(UiTheme, Theme, systemIsDark: false).Id;

    /// <summary>
    /// 选中一个预置：**同时**把明暗锁定成该预置的归属（深色系预置 ⇒ 锁定深色）。
    ///
    /// <para>为什么要一起写 `ui_theme`：预置已经决定了明暗，再留着"跟随系统"就会出现
    /// "系统是浅色、用户却选了午夜蓝"这种自相矛盾（`ThemeResolver.ResolvePreset` 里那条
    /// "明暗由预置决定"的规则也是为此）。选「跟随系统」请直接写 <see cref="UiTheme"/> = System。</para>
    /// </summary>
    public void SelectPreset(string? presetId)
    {
        var preset = ThemePresets.Find(presetId) ?? ThemePresets.Dark;
        Theme = preset.Id;
        UiTheme = preset.IsDark ? ThemeMode.Dark : ThemeMode.Light;
    }

    /// <summary>
    /// 逐令牌**颜色**覆盖（`theme_overrides` 里的颜色键；只认 21 个已知令牌）。
    ///
    /// <para>本项目界面不提供选色（与原版 QML 线一致：原版也只暴露了 `setColor` 接口），
    /// 但**旧版/手改配置文件写的覆盖照样生效** —— 这正是"逐令牌覆盖"这一层存在的意义。</para>
    /// </summary>
    public IReadOnlyDictionary<string, string> ThemeColorOverrides()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (ThemeOverrides is null)
        {
            return result;
        }

        foreach (var (key, element) in ThemeOverrides)
        {
            if (!ThemePresets.IsColorKey(key) || element.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = element.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                result[key] = text.Trim();
            }
        }

        return result;
    }

    /// <summary>
    /// **数字参数**覆盖（`theme_overrides` 里的 `param:&lt;key&gt;`；只认 8 个已知参数）。
    ///
    /// <para>键格式与原版 `theme.py::setParam` 完全一致（`param:radius` 这样），
    /// 所以原版/QML 版写下的细调，本版读得到；反之亦然。</para>
    /// </summary>
    public IReadOnlyDictionary<string, double> ThemeParamOverrides()
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);

        if (ThemeOverrides is null)
        {
            return result;
        }

        foreach (var (key, element) in ThemeOverrides)
        {
            if (!key.StartsWith(ThemeParamSpecs.OverridePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var spec = ThemeParamSpecs.Find(key[ThemeParamSpecs.OverridePrefix.Length..]);
            if (spec is null || element.ValueKind != JsonValueKind.Number
                || !element.TryGetDouble(out var value) || !double.IsFinite(value))
            {
                continue;
            }

            result[spec.Key] = spec.Clamp(value);
        }

        return result;
    }

    /// <summary>写一个参数覆盖（立刻反映到 `theme_overrides`；落盘由 <c>SettingsStore.Save</c> 负责）。</summary>
    public void SetThemeParamOverride(string? key, double value)
    {
        var spec = ThemeParamSpecs.Find(key);
        if (spec is null)
        {
            return;
        }

        ThemeOverrides ??= new Dictionary<string, JsonElement>();
        ThemeOverrides[ThemeParamSpecs.OverridePrefix + spec.Key] =
            JsonSerializer.SerializeToElement(spec.Clamp(value));
    }

    /// <summary>删掉一个参数覆盖（"恢复该参数为预置默认"）。返回是否真的删掉了。</summary>
    public bool RemoveThemeParamOverride(string? key)
    {
        var spec = ThemeParamSpecs.Find(key);
        if (spec is null || ThemeOverrides is null)
        {
            return false;
        }

        return ThemeOverrides.Remove(ThemeParamSpecs.OverridePrefix + spec.Key);
    }

    /// <summary>清空全部主题细调（颜色 + 参数）—— 界面上的「恢复默认外观」。</summary>
    public void ClearThemeOverrides() => ThemeOverrides = new Dictionary<string, JsonElement>();

    /// <summary>
    /// 只清掉**颜色**覆盖，保留参数细调（原版切预置时就是这个行为，这里单独留一个入口备用）。
    /// </summary>
    public void ClearThemeColorOverrides()
    {
        if (ThemeOverrides is null)
        {
            return;
        }

        foreach (var key in ThemeOverrides.Keys.Where(ThemePresets.IsColorKey).ToList())
        {
            ThemeOverrides.Remove(key);
        }
    }

    /// <summary>是否显示标签栏数量（未设置时默认显示）。</summary>
    [JsonIgnore]
    public bool ShowTabCounts
    {
        get => ShowTabCountsRaw ?? true;
        set => ShowTabCountsRaw = value;
    }

    /// <summary>动效时长（毫秒，已夹到 60~1200）。</summary>
    [JsonIgnore]
    public int AnimationDurationMs
    {
        get => Math.Clamp(AnimationDurationMsRaw ?? DefaultAnimationDurationMs, 60, 1200);
        set => AnimationDurationMsRaw = Math.Clamp(value, 60, 1200);
    }

    /// <summary>交错间隔（毫秒，已夹到 0~120）。</summary>
    [JsonIgnore]
    public int AnimationStaggerMs
    {
        get => Math.Clamp(AnimationStaggerMsRaw ?? DefaultAnimationStaggerMs, 0, 120);
        set => AnimationStaggerMsRaw = Math.Clamp(value, 0, 120);
    }

    /// <summary>动效曲线（无法识别时回落 Standard）。</summary>
    [JsonIgnore]
    public AnimationEasing AnimationEasing
    {
        get => ParseEasing(AnimationEasingRaw);
        set => AnimationEasingRaw = ToWire(value);
    }

    /// <summary>窗口尺寸的合法区间（物理像素）—— 防止手改配置把窗口设成 0 或大到点不到。</summary>
    public const int MinWindowWidth = 400;

    public const int MinWindowHeight = 300;

    public const int MaxWindowWidth = 10000;

    public const int MaxWindowHeight = 10000;

    /// <summary>上次的窗口尺寸；没记录过 / 不合法时返回 null（调用方用默认值）。</summary>
    [JsonIgnore]
    public (int Width, int Height)? WindowSize
    {
        get
        {
            if (WindowWidthRaw is not { } width || WindowHeightRaw is not { } height)
            {
                return null;
            }

            if (width < MinWindowWidth || height < MinWindowHeight)
            {
                return null;
            }

            return (Math.Min(width, MaxWindowWidth), Math.Min(height, MaxWindowHeight));
        }
        set
        {
            if (value is { } size)
            {
                WindowWidthRaw = Math.Clamp(size.Width, MinWindowWidth, MaxWindowWidth);
                WindowHeightRaw = Math.Clamp(size.Height, MinWindowHeight, MaxWindowHeight);
            }
            else
            {
                WindowWidthRaw = null;
                WindowHeightRaw = null;
            }
        }
    }

    /// <summary>换算成界面直接可用的动效参数。</summary>
    public AnimationSpec ToAnimationSpec() => AnimationsEnabled
        ? new AnimationSpec(true, AnimationDurationMs, AnimationStaggerMs, AnimationEasing)
        : AnimationSpec.Disabled;

    /// <summary>
    /// 就地恢复默认值（**不新建实例**：调用方持有的引用必须继续有效，
    /// 否则"面板换了个新对象、主窗口还拿着旧对象"就会两边不一致）。
    /// </summary>
    public void ResetToDefaults()
    {
        Language = DefaultLanguage;
        IconSize = DefaultIconSize;
        Theme = DefaultTheme;
        ThemeOverrides = new Dictionary<string, JsonElement>();
        TabIconModeRaw = null;
        AnimationsEnabledRaw = null;
        BackdropRaw = null;
        ShowTabCountsRaw = null;
        AnimationDurationMsRaw = null;
        AnimationStaggerMsRaw = null;
        AnimationEasingRaw = null;
        WindowWidthRaw = null;
        WindowHeightRaw = null;
        UiThemeRaw = null;
        ExtraFields = null;
    }

    public static AnimationEasing ParseEasing(string? wire) => wire?.Trim().ToLowerInvariant() switch
    {
        "soft" or "sine" => AnimationEasing.Soft,
        "snappy" or "fast" or "quad" => AnimationEasing.Snappy,
        _ => AnimationEasing.Standard,
    };

    public static string ToWire(AnimationEasing easing) => easing switch
    {
        AnimationEasing.Soft => "soft",
        AnimationEasing.Snappy => "snappy",
        _ => "standard",
    };

    public static string ToWire(TabIconMode mode) => mode switch
    {
        TabIconMode.Text => "text",
        TabIconMode.Always => "always",
        _ => "hover",
    };

    public static TabIconMode ParseTabIconMode(string? wire) => wire?.Trim().ToLowerInvariant() switch
    {
        "text" or "none" or "never" => TabIconMode.Text,
        "always" or "on" => TabIconMode.Always,
        _ => TabIconMode.Hover,
    };
}

/// <summary>config.json 的读写（原子写 + 容错 + 取值归一化）。</summary>
public sealed class SettingsStore
{
    public SettingsStore(string dataDirectory)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        SettingsFile = Path.Combine(DataDirectory, "config.json");
        SettingsTempFile = Path.Combine(DataDirectory, "config.tmp");
    }

    /// <summary>用 <see cref="AppPaths.ResolveDataDirectory"/> 定位数据目录。</summary>
    public static SettingsStore CreateDefault(string? startDirectory = null)
        => new(AppPaths.ResolveDataDirectory(startDirectory));

    public string DataDirectory { get; }

    public string SettingsFile { get; }

    /// <summary>原子写的临时文件（与原版一样是 <c>config.tmp</c>）。</summary>
    public string SettingsTempFile { get; }

    /// <summary>最近一次载入/保存的设置。</summary>
    public AppSettings Current { get; private set; } = new();

    /// <summary>读配置；文件不存在 / 损坏 / 非法取值都归一化到可用的默认值（绝不抛异常）。</summary>
    public AppSettings Load()
    {
        var settings = new AppSettings();

        try
        {
            if (File.Exists(SettingsFile))
            {
                var text = File.ReadAllText(SettingsFile, Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<AppSettings>(text, TabsJson.Options);
                if (loaded is not null)
                {
                    settings = loaded;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 配置损坏：直接用默认值（原版也是这个策略：不备份、不报错，保证能启动）
            settings = new AppSettings();
        }

        Normalize(settings);
        Current = settings;
        return settings;
    }

    /// <summary>原子写回 config.json（串行化 + 失败不留临时文件，见 <see cref="AtomicFile"/>）。</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Normalize(settings);
        Directory.CreateDirectory(DataDirectory);

        var json = JsonSerializer.Serialize(settings, TabsJson.Options);

        AtomicFile.WriteAllText(SettingsFile, SettingsTempFile, json);

        Current = settings;
    }

    /// <summary>改一个字段并立刻落盘（<paramref name="save"/> 为 false 时只改内存）。</summary>
    public void Update(Action<AppSettings> change, bool save = true)
    {
        ArgumentNullException.ThrowIfNull(change);

        change(Current);
        if (save)
        {
            Save(Current);
        }
    }

    /// <summary>非法取值归一化（与原版 <c>Settings.load()</c> 的校验一致）。</summary>
    private static void Normalize(AppSettings settings)
    {
        if (!AppSettings.Languages.Contains(settings.Language, StringComparer.OrdinalIgnoreCase))
        {
            settings.Language = AppSettings.DefaultLanguage;
        }
        else
        {
            settings.Language = settings.Language.ToLowerInvariant();
        }

        if (!AppSettings.IconSizes.Contains(settings.IconSize, StringComparer.OrdinalIgnoreCase))
        {
            settings.IconSize = AppSettings.DefaultIconSize;
        }
        else
        {
            settings.IconSize = settings.IconSize.ToLowerInvariant();
        }

        if (string.IsNullOrWhiteSpace(settings.Theme))
        {
            settings.Theme = AppSettings.DefaultTheme;
        }
        else
        {
            // 预置名归一化：认不出的值回落默认预置（照原版 `theme.py::load_from_settings`
            // "name if name in PRESETS else DEFAULT_PRESET"）—— 5 个预置名与原版完全同一套。
            settings.Theme = ThemePresets.Normalize(settings.Theme);
        }

        // 主题细调必须是对象；是别的类型就当空（原版同样处理）
        if (settings.ThemeOverrides is null)
        {
            settings.ThemeOverrides = new Dictionary<string, JsonElement>();
        }

        // 新字段：只在文件里已经写了值时才归一化 —— null 保持 null（避免无谓改动用户的文件）
        if (settings.TabIconModeRaw is not null)
        {
            settings.TabIconModeRaw = AppSettings.ToWire(AppSettings.ParseTabIconMode(settings.TabIconModeRaw));
        }

        if (settings.BackdropRaw is not null)
        {
            settings.BackdropRaw = BackdropKinds.Normalize(settings.BackdropRaw);
        }

        if (settings.AnimationEasingRaw is not null)
        {
            settings.AnimationEasingRaw = AppSettings.ToWire(AppSettings.ParseEasing(settings.AnimationEasingRaw));
        }

        if (settings.AnimationDurationMsRaw is not null)
        {
            settings.AnimationDurationMsRaw = settings.AnimationDurationMs;   // 夹到合法区间
        }

        if (settings.AnimationStaggerMsRaw is not null)
        {
            settings.AnimationStaggerMsRaw = settings.AnimationStaggerMs;
        }

        // 界面主题：只在文件里已经写了值时才归一化（null 保持 null，避免无谓改动用户的文件）
        if (settings.UiThemeRaw is not null)
        {
            settings.UiThemeRaw = ThemeTokens.ToWire(ThemeTokens.ParseMode(settings.UiThemeRaw));
        }

        // 快捷键覆盖：清掉**认不出**的条目（动作名不认得 / 键位串解析不出来 / 键位不合法），
        // 空表归一成 null（= 全用默认），免得把一个空对象一直写在用户的 config.json 里。
        if (settings.ShortcutBindings is { } bindings)
        {
            var cleaned = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (action, keys) in bindings)
            {
                if (!Enum.TryParse<Services.ShortcutAction>(action, ignoreCase: false, out var parsedAction)
                    || Services.ShortcutGesture.Parse(keys) is not { } gesture)
                {
                    continue;
                }

                // ⚠️ 这里**不**因为"缺修饰键 / 与编辑键冲突"就丢掉 ——
                //    用户的决定是"允许改，但给出明确警告"（2026-09-19），
                //    校验只负责"能不能解析"，"好不好"由界面标黄提示。
                cleaned[parsedAction.ToString()] = gesture.Display;
            }

            settings.ShortcutBindings = cleaned.Count == 0 ? null : cleaned;
        }

        // 窗口尺寸：只在记录过时才夹取（不合法 → 当作没记录，回落默认尺寸）
        if (settings.WindowSize is { } windowSize)
        {
            settings.WindowSize = windowSize;
        }
    }
}
