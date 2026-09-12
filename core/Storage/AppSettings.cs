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

    /// <summary>未知字段原样保留（旧版/未来版本的键都不该被 C# 线吃掉）。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }

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
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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

    /// <summary>原子写回 config.json。</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Normalize(settings);
        Directory.CreateDirectory(DataDirectory);

        var json = JsonSerializer.Serialize(settings, TabsJson.Options);
        File.WriteAllText(SettingsTempFile, json, Utf8NoBom);
        File.Move(SettingsTempFile, SettingsFile, overwrite: true);

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
    }
}
