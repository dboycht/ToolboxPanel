// ThemeEngineTests.cs —— 主题引擎（v2.0.6「5 预置 + 8 参数 + 逐令牌覆盖」）
//
// 这里守两类东西：
//   ① **契约**：5 个预置的令牌集合必须同构、8 个参数的区间/步长必须合法、
//      三层叠加（预置 → 参数 → 逐令牌）的优先级不能变；
//   ② ★ **不许改变现有观感**：默认参数下投影出来的调色板必须与 W4 定标的
//      `ThemeTokens.Dark/Light` **逐字段一致** —— 这条是"换主题引擎不顺手改外观"的唯一硬证据。
//
// 与原版 `theme.py` 的**逐字段保真**在 `ThemeFidelityTests.cs`（那边要跑真实 Python）。

using System.Text.Json;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class ThemeEngineTests
{
    /// <summary>把调色板摊平成名 → 值（用于"逐字段一致"的断言）。</summary>
    private static Dictionary<string, object?> Snapshot(ThemePalette palette)
    {
        var result = new Dictionary<string, object?>();
        foreach (var property in typeof(ThemePalette).GetProperties())
        {
            result[property.Name] = property.GetValue(palette);
        }

        return result;
    }

    private static void AssertPaletteEqual(ThemePalette expected, ThemePalette actual)
    {
        var left = Snapshot(expected);
        var right = Snapshot(actual);

        foreach (var (name, value) in left)
        {
            Assert.Equal(value, right[name]);
        }
    }

    // ────────────────────────────── 预置表 ──────────────────────────────

    [Fact]
    public void 五个预置的id与顺序与原版一致()
    {
        Assert.Equal(new[] { "dark", "light", "midnight", "grape", "matcha" }, ThemePresets.Ids);
        Assert.Equal("dark", ThemePresets.DefaultId);
        Assert.Equal("system", ThemePresets.SystemId);
    }

    [Fact]
    public void 每个预置的颜色令牌集合完全相同()
    {
        // 21 个令牌（原版 DARK 与 LIGHT 的并集），每个预置都必须齐全 ——
        // 少一个就是"某个预置下有一类控件没颜色"。
        Assert.Equal(21, ThemePresets.ColorKeys.Count);

        foreach (var preset in ThemePresets.All)
        {
            Assert.Equal(
                ThemePresets.ColorKeys.OrderBy(k => k, StringComparer.Ordinal),
                preset.Colors.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void 每个预置都带齐八个参数()
    {
        foreach (var preset in ThemePresets.All)
        {
            Assert.Equal(
                ThemeParamSpecs.Keys.OrderBy(k => k, StringComparer.Ordinal),
                preset.Params.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void 预置的明暗归属与原版一致()
    {
        Assert.True(ThemePresets.Dark.IsDark);
        Assert.False(ThemePresets.Light.IsDark);

        // 三个彩色预置在原版里都是 `{**DARK, ...}` ⇒ 都是深色系
        Assert.True(ThemePresets.Midnight.IsDark);
        Assert.True(ThemePresets.Grape.IsDark);
        Assert.True(ThemePresets.Matcha.IsDark);
    }

    [Theory]
    [InlineData("DARK", "dark")]
    [InlineData("  Grape ", "grape")]
    [InlineData("matcha", "matcha")]
    [InlineData("苹果绿", "dark")]      // 未知 ⇒ 默认预置（照原版归一化）
    [InlineData("", "dark")]
    [InlineData(null, "dark")]
    public void 预置名解析_大小写与未知值(string? raw, string expected)
    {
        Assert.Equal(expected, ThemePresets.Normalize(raw));
    }

    // ⚠️ 「预置标签按语言取」这条测试随 `ThemePresets.Label` 一起删了（2026-09-21 精简）：
    //    界面上的预置名走的是**原版那 6 个 i18n key**（`theme.dark` 等，XAML 里 `ui:Tr.Key` 直接用），
    //    Core 里再存一份中英标签属于第二份来源。表里的中英字段本身仍被
    //    `ThemeFidelityTests` 拿真实 Python 逐字比对钉住。

    // ────────────────────────────── 参数规格 ──────────────────────────────

    [Fact]
    public void 八个参数的key与顺序与原版一致()
    {
        Assert.Equal(
            new[]
            {
                "window_opacity", "card_opacity", "card_hover_opacity", "blur_radius",
                "radius", "anim_ms", "hover_ms", "sheet_opacity",
            },
            ThemeParamSpecs.Keys);
    }

    [Fact]
    public void 参数的范围与步长必须自洽()
    {
        foreach (var spec in ThemeParamSpecs.All)
        {
            Assert.True(spec.Min <= spec.Default, $"{spec.Key} 的默认值低于下界");
            Assert.True(spec.Default <= spec.Max, $"{spec.Key} 的默认值高于上界");
            Assert.True(spec.Step > 0, $"{spec.Key} 的步长必须为正");
            Assert.True(spec.UiMin >= spec.Min && spec.UiMax <= spec.Max, $"{spec.Key} 的界面区间越界");
            Assert.False(string.IsNullOrWhiteSpace(spec.Zh));
            Assert.False(string.IsNullOrWhiteSpace(spec.En));
        }
    }

    [Fact]
    public void 界面微调只暴露可落地的六个参数()
    {
        // blur_radius（系统材质不可调）与 anim_ms（与「动效」节的时长合并）都不进界面
        Assert.Equal(
            new[] { "window_opacity", "card_opacity", "card_hover_opacity", "radius", "hover_ms", "sheet_opacity" },
            ThemeParamSpecs.Adjustable.Select(s => s.Key));

        Assert.False(ThemeParamSpecs.Find("blur_radius")!.Adjustable);
        Assert.False(ThemeParamSpecs.Find("anim_ms")!.Adjustable);
    }

    [Fact]
    public void 参数夹取_含非法浮点()
    {
        var radius = ThemeParamSpecs.Find("radius")!;
        Assert.Equal(0, radius.Clamp(-5));
        Assert.Equal(32, radius.Clamp(999));
        Assert.Equal(16, radius.Clamp(double.NaN));            // NaN ⇒ 回落默认（配置文件可能是手改的）
        Assert.Equal(16, radius.Clamp(double.PositiveInfinity));
        Assert.Equal(16, ThemeParamSpecs.Clamp("no_such_param", 16));
    }

    // ────────────────────────────── 解析：预置 ──────────────────────────────

    [Theory]
    [InlineData(true, "dark")]
    [InlineData(false, "light")]
    public void 跟随系统按系统明暗挑预置(bool systemIsDark, string expected)
    {
        // 即使用户选的是葡萄紫，"跟随系统"也不该拿它去配浅色底
        var resolution = ThemeResolver.Resolve(
            new ThemeRequest(ThemeMode.System, "grape"), systemIsDark);

        Assert.Equal(expected, resolution.PresetId);
        Assert.Equal(systemIsDark, resolution.IsDark);
    }

    [Fact]
    public void 锁定明暗时用用户选的预置_但预置必须与明暗一致()
    {
        var resolution = ThemeResolver.Resolve(new ThemeRequest(ThemeMode.Dark, "grape"), systemIsDark: false);

        Assert.Equal("grape", resolution.PresetId);
        Assert.True(resolution.IsDark);
        Assert.True(resolution.Palette.IsDark);
    }

    [Fact]
    public void 预置与锁定的明暗冲突时以明暗为准()
    {
        // ⚠️ 这是**兼容性**要求：`theme` 以前是老线/QML 线的主题名，
        //    老配置里完全可能是 "ui_theme=light + theme=dark"（那时以 ui_theme 为准）。
        //    升级到本版后必须还是浅色，不能被预置翻掉 —— 2.0.6 的常驻探针抓出来的真问题。
        var light = ThemeResolver.Resolve(new ThemeRequest(ThemeMode.Light, "dark"), systemIsDark: false);
        Assert.Equal("light", light.PresetId);
        Assert.False(light.IsDark);

        var lightWithColored = ThemeResolver.Resolve(new ThemeRequest(ThemeMode.Light, "grape"), systemIsDark: true);
        Assert.Equal("light", lightWithColored.PresetId);
        Assert.False(lightWithColored.IsDark);

        var darkWithLightPreset = ThemeResolver.Resolve(new ThemeRequest(ThemeMode.Dark, "light"), systemIsDark: false);
        Assert.Equal("dark", darkWithLightPreset.PresetId);
        Assert.True(darkWithLightPreset.IsDark);
    }

    [Fact]
    public void 面板上的选择项与真正生效的预置一致()
    {
        var settings = new AppSettings();

        // 手改出来的冲突组合：面板要显示"深色"（真正生效的那个），而不是"浅色"
        settings.UiTheme = ThemeMode.Light;
        settings.Theme = "dark";
        Assert.Equal("light", settings.ThemeChoice);

        settings.SelectPreset("grape");
        Assert.Equal("grape", settings.ThemeChoice);
    }

    [Fact]
    public void 未知预置回落该明暗的中性预置()
    {
        Assert.Equal("light", ThemeResolver.Resolve(new ThemeRequest(ThemeMode.Light, "乱写"), true).PresetId);
        Assert.Equal("dark", ThemeResolver.Resolve(new ThemeRequest(ThemeMode.Dark, null), false).PresetId);
    }

    // ────────────────────────────── 解析：三层叠加 ──────────────────────────────

    [Fact]
    public void 逐令牌覆盖只认已知令牌且忽略空值()
    {
        var colors = ThemeResolver.ResolveColors(ThemePresets.Dark, new Dictionary<string, string>
        {
            ["accent"] = "#ff0000",
            ["no_such_token"] = "#00ff00",
            ["text"] = "   ",
        });

        Assert.Equal("#ff0000", colors["accent"]);
        Assert.Equal(ThemePresets.Dark.Colors["text"], colors["text"]);
        Assert.DoesNotContain("no_such_token", colors.Keys);
    }

    [Fact]
    public void 参数覆盖生效并夹到合法区间()
    {
        var parameters = ThemeResolver.ResolveParams(ThemePresets.Dark, new Dictionary<string, double>
        {
            ["radius"] = 999,
            ["no_such_param"] = 5,
        });

        Assert.Equal(32, parameters.Radius);
        Assert.Equal(ThemePresets.Dark.ParamOrDefault("card_opacity"), parameters.CardOpacity);
    }

    [Fact]
    public void 动效时长以动效设置为准_其后才是旧配置与预置默认()
    {
        var spec = ThemeResolver.ResolveParams(ThemePresets.Dark, null, animationDurationMs: null);
        Assert.Equal(260, spec.AnimMs);                       // 预置默认（原版 dark 的 anim_ms）

        var legacy = ThemeResolver.ResolveParams(
            ThemePresets.Dark, new Dictionary<string, double> { ["anim_ms"] = 300 }, null);
        Assert.Equal(300, legacy.AnimMs);                     // 旧版/QML 写下的 param:anim_ms 仍然认

        var withSettings = ThemeResolver.ResolveParams(
            ThemePresets.Dark, new Dictionary<string, double> { ["anim_ms"] = 300 }, 220);
        Assert.Equal(220, withSettings.AnimMs);               // 「动效」节的时长压过一切
    }

    // ────────────────────────────── ★ 不许改变现有观感 ──────────────────────────────

    [Theory]
    [InlineData("dark", true)]
    [InlineData("light", false)]
    public void 默认参数下投影与既有调色板逐字段一致(string presetId, bool systemIsDark)
    {
        var resolution = ThemeResolver.Resolve(
            new ThemeRequest(ThemeMode.System, presetId), systemIsDark);

        AssertPaletteEqual(systemIsDark ? ThemeTokens.Dark : ThemeTokens.Light, resolution.Palette);
    }

    [Fact]
    public void 彩色预置只换色相不换不透明度()
    {
        var dark = ThemeResolver.Resolve(new ThemeRequest(ThemeMode.Dark, "dark"), true).Palette;
        var midnight = ThemeResolver.Resolve(new ThemeRequest(ThemeMode.Dark, "midnight"), true).Palette;

        // 色相：窗口兜底 / 面板表面 / 强调色 取自预置
        Assert.Equal((byte)11, midnight.WindowFallback.R);      // #0b1020
        Assert.Equal((byte)16, midnight.WindowFallback.G);
        Assert.Equal((byte)32, midnight.WindowFallback.B);
        Assert.Equal((byte)17, midnight.PanelSurface.R);        // #111830
        Assert.Equal((byte)91, midnight.Accent.R);              // #5b8def
        Assert.Equal((byte)141, midnight.Accent.G);
        Assert.Equal((byte)239, midnight.Accent.B);

        // alpha：一律沿用设计值（透明度只由参数那一层管）
        Assert.Equal(dark.WindowFallback.A, midnight.WindowFallback.A);
        Assert.Equal(dark.PanelSurface.A, midnight.PanelSurface.A);
        Assert.Equal((byte)255, midnight.Accent.A);

        // 其余淡层保持中性（彩色预置不该把每一层都染上颜色）
        Assert.Equal(dark.CardSurface, midnight.CardSurface);
        Assert.Equal(dark.HoverSurface, midnight.HoverSurface);
        Assert.Equal(dark.PressedSurface, midnight.PressedSurface);
        Assert.Equal(dark.Overlay, midnight.Overlay);
        Assert.Equal(dark.Divider, midnight.Divider);
        Assert.Equal(dark.StatusSurface, midnight.StatusSurface);
        Assert.Equal(dark.TabStripSurface, midnight.TabStripSurface);
        Assert.Equal(dark.TabStripBorder, midnight.TabStripBorder);
    }

    // ────────────────────────────── 参数怎么调制外观 ──────────────────────────────

    [Fact]
    public void 圆角参数按倍率缩放设计圆角()
    {
        static double Corner(string presetId, double radius)
        {
            var resolution = ThemeResolver.Resolve(
                new ThemeRequest(ThemeMode.Dark, presetId, ParamOverrides: new Dictionary<string, double>
                {
                    ["radius"] = radius,
                }),
                systemIsDark: true);
            return resolution.ScaleCornerRadius(6.0);   // 6 = 界面里的设计圆角（图块/标签项）
        }

        Assert.Equal(6.0, Corner("dark", 16));    // 默认 ⇒ 一丝不变
        Assert.Equal(0.0, Corner("dark", 0));     // 直角
        Assert.Equal(12.0, Corner("dark", 32));   // 最大 ⇒ 设计圆角的一倍
        Assert.Equal(7.0, Corner("midnight", 18)); // 预置自带的 radius=18 ⇒ 6×18/16 = 6.75 ⇒ 7
    }

    [Fact]
    public void 卡片不透明度按倍率调制surface的alpha()
    {
        var doubled = ThemeResolver.Resolve(
            new ThemeRequest(ThemeMode.Dark, "dark", ParamOverrides: new Dictionary<string, double>
            {
                ["card_opacity"] = 0.32,          // 预置默认 0.16 的两倍
                ["card_hover_opacity"] = 0.80,    // 预置默认 0.26 的上界
            }),
            systemIsDark: true).Palette;

        Assert.Equal((byte)28, doubled.CardSurface.A);       // 14 × 2
        Assert.Equal((byte)40, doubled.TabStripSurface.A);   // 20 × 2
        Assert.Equal((byte)62, doubled.HoverSurface.A);      // 20 × 0.80/0.26
        Assert.Equal((byte)105, doubled.PressedSurface.A);   // 34 × 0.80/0.26

        var transparent = ThemeResolver.Resolve(
            new ThemeRequest(ThemeMode.Dark, "dark", ParamOverrides: new Dictionary<string, double>
            {
                ["card_opacity"] = 0,
            }),
            systemIsDark: true).Palette;
        Assert.Equal((byte)0, transparent.CardSurface.A);
        Assert.Equal((byte)0, transparent.TabStripSurface.A);
    }

    [Fact]
    public void 抽屉不透明度按倍率调制面板底色的alpha()
    {
        static byte PanelAlpha(double sheetOpacity) => ThemeResolver.Resolve(
            new ThemeRequest(ThemeMode.Dark, "dark", ParamOverrides: new Dictionary<string, double>
            {
                ["sheet_opacity"] = sheetOpacity,
            }),
            systemIsDark: true).Palette.PanelSurface.A;

        // ⚠️ 2026-09-21 起设计值就是**不透明**（用户："这个默认主题你可以不用半透明的"）：
        //    默认（0.62）⇒ 255（实心面板），往下拖才逐渐变透。
        Assert.Equal((byte)255, PanelAlpha(0.62));   // 预置默认 ⇒ 实心面板
        Assert.Equal((byte)255, PanelAlpha(1.00));
        Assert.Equal((byte)82, PanelAlpha(0.20));    // 255 × 0.20/0.62 = 82.2 ⇒ 82
    }

    [Fact]
    public void 窗口兜底底色默认不透明_调低也不低于可读下限()
    {
        static byte FallbackAlpha(double windowOpacity) => ThemeResolver.Resolve(
            new ThemeRequest(ThemeMode.Dark, "dark", ParamOverrides: new Dictionary<string, double>
            {
                ["window_opacity"] = windowOpacity,
            }),
            systemIsDark: true).Palette.WindowFallback.A;

        Assert.Equal((byte)255, FallbackAlpha(0.42));   // 预置默认值 ⇒ 与现在完全一样（不透明兜底）
        Assert.Equal((byte)255, FallbackAlpha(1.00));

        var lowest = FallbackAlpha(0.20);         // 参数能取到的最小值
        Assert.True(lowest >= (byte)204, $"兜底底色低于可读下限：{lowest}");
        Assert.True(lowest < 255);

        // 单调：越往下越透（但不越过下限）
        Assert.True(FallbackAlpha(0.30) >= lowest);
    }

    [Fact]
    public void 颜色串解析_六位与八位()
    {
        Assert.True(ThemeResolver.TryParseColor("#14161f", out var six));
        Assert.Equal(((byte)255, (byte)20, (byte)22, (byte)31), six);

        Assert.True(ThemeResolver.TryParseColor("#33ffffff", out var eight));
        Assert.Equal(((byte)51, (byte)255, (byte)255, (byte)255), eight);

        Assert.False(ThemeResolver.TryParseColor("红色", out _));
        Assert.False(ThemeResolver.TryParseColor(null, out _));
        Assert.False(ThemeResolver.TryParseColor("#12345", out _));
    }

    // ────────────────────────────── 设置层：读写与兼容 ──────────────────────────────

    [Fact]
    public void 参数覆盖写进config并读回_键名与原版一致()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);

        var settings = store.Load();
        settings.SetThemeParamOverride("radius", 24);
        settings.SelectPreset("grape");
        store.Save(settings);

        // 线上键名必须是 `param:<key>`（原版 `theme_overrides` 就是这么存的 ⇒ 双向兼容）
        var raw = File.ReadAllText(store.SettingsFile);
        Assert.Contains("\"param:radius\"", raw);
        Assert.Contains("\"theme\": \"grape\"", raw);
        Assert.Contains("\"ui_theme\": \"dark\"", raw);   // 选了深色系预置 ⇒ 明暗一起锁定

        var reloaded = new SettingsStore(temp.Path).Load();
        Assert.Equal(24, reloaded.ThemeParamOverrides()["radius"]);
        Assert.Equal("grape", reloaded.ThemePresetId);
        Assert.Equal(ThemeMode.Dark, reloaded.UiTheme);
    }

    [Fact]
    public void 逐令牌颜色覆盖读得回_恢复默认外观只清细调()
    {
        using var temp = new TempDataDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "config.json"),
            """
            {
              "theme": "midnight",
              "theme_overrides": {
                "accent": "#ff0000",
                "param:radius": 30,
                "param:no_such": 3,
                "no_such_token": "#00ff00",
                "param:hover_ms": "不是数字"
              }
            }
            """,
            new System.Text.UTF8Encoding(false));

        var settings = new SettingsStore(temp.Path).Load();

        Assert.Equal("#ff0000", settings.ThemeColorOverrides()["accent"]);
        Assert.Equal(30, settings.ThemeParamOverrides()["radius"]);
        Assert.DoesNotContain("no_such", settings.ThemeParamOverrides().Keys);       // 未知参数被忽略
        Assert.DoesNotContain("no_such_token", settings.ThemeColorOverrides().Keys);  // 未知令牌被忽略
        Assert.DoesNotContain("hover_ms", settings.ThemeParamOverrides().Keys);       // 类型不对被忽略

        // 只清颜色：参数细调要留着
        settings.ClearThemeColorOverrides();
        Assert.Empty(settings.ThemeColorOverrides());
        Assert.Equal(30, settings.ThemeParamOverrides()["radius"]);

        // 全清 + 删单条
        Assert.True(settings.RemoveThemeParamOverride("radius"));
        Assert.False(settings.RemoveThemeParamOverride("radius"));
        Assert.Empty(settings.ThemeParamOverrides());
    }

    [Fact]
    public void 恢复默认设置会把主题细调一并清掉()
    {
        var settings = new AppSettings();
        settings.SelectPreset("matcha");
        settings.SetThemeParamOverride("radius", 30);

        settings.ResetToDefaults();

        Assert.Equal(ThemePresets.DefaultId, settings.ThemePresetId);
        Assert.Equal(ThemeMode.System, settings.UiTheme);
        Assert.Empty(settings.ThemeParamOverrides());
        Assert.Empty(settings.ThemeColorOverrides());
    }

    [Fact]
    public void 界面上跟随系统与选具体预置是互斥的两种选择()
    {
        var settings = new AppSettings();

        // 默认：跟随系统（`ui_theme` 没写过 ⇒ System），此时选择项显示"跟随系统"
        Assert.Equal(ThemePresets.SystemId, settings.ThemeChoice);
        Assert.Null(settings.UiThemeRaw);

        settings.SelectPreset("midnight");
        Assert.Equal("midnight", settings.ThemeChoice);
        Assert.Equal(ThemeMode.Dark, settings.UiTheme);

        settings.UiTheme = ThemeMode.System;   // 切回"跟随系统"
        Assert.Equal(ThemePresets.SystemId, settings.ThemeChoice);
        Assert.Equal("midnight", settings.ThemePresetId);   // 用户的预置选择不丢
    }

    [Fact]
    public void 没被改过的配置写回时不含theme_overrides以外的多余键()
    {
        var settings = new AppSettings();
        var json = JsonSerializer.Serialize(settings, TabsJson.Options);

        Assert.Contains("\"theme\": \"dark\"", json);     // 老字段照原样写着
        Assert.DoesNotContain("ui_theme", json);          // 没选过 ⇒ 不写（保住逐字节不变）
        Assert.DoesNotContain("param:", json);            // 没细调过 ⇒ 不写
    }
}
