// ThemeTokensTests.cs —— 主题令牌（W4 第一步的"最小全局主题"）
//
// 这里守的是**契约**，不是观感：
//   · 浅/深两张令牌表必须**同构**（少一个键就是一类控件在某主题下没颜色）；
//   · 模式的解析 / 归一化 / 回落；
//   · `config.json` 的新字段 `ui_theme` 的兼容语义（null = 文件里没有这个键 ⇒ 不写回，
//     保住"没被改过的配置逐字节不变"）。
// 观感（浅色下看不看得清、玻璃够不够透）只能真机目检，不在这里。

using System.Text.Json;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class ThemeTokensTests
{
    /// <summary>把一个令牌表的所有颜色取出来（名 → 值），用于"两张表同构"的断言。</summary>
    private static Dictionary<string, object?> Snapshot(ThemePalette palette)
    {
        var result = new Dictionary<string, object?>();
        foreach (var property in typeof(ThemePalette).GetProperties())
        {
            result[property.Name] = property.GetValue(palette);
        }

        return result;
    }

    [Fact]
    public void 浅色与深色两张令牌表必须同构()
    {
        var dark = Snapshot(ThemeTokens.Dark);
        var light = Snapshot(ThemeTokens.Light);

        Assert.Equal(dark.Keys.OrderBy(k => k), light.Keys.OrderBy(k => k));

        // 每个令牌都必须是"四元组颜色"或 bool，不能是 null / 默认值漏填
        foreach (var (name, value) in dark)
        {
            Assert.True(value is not null, $"深色令牌 {name} 为空");
            Assert.True(light[name] is not null, $"浅色令牌 {name} 为空");
        }
    }

    [Fact]
    public void 两张表的深浅标记必须正确()
    {
        Assert.True(ThemeTokens.Dark.IsDark);
        Assert.False(ThemeTokens.Light.IsDark);
    }

    [Fact]
    public void 浅色主题的关键表面亮度必须高于深色()
    {
        // 用"兜底底色"做代表：浅色的亮度必须明显高于深色（防止把两张表写反）
        static int Luma((byte A, byte R, byte G, byte B) c) => c.R + c.G + c.B;

        Assert.True(Luma(ThemeTokens.Light.WindowFallback) > Luma(ThemeTokens.Dark.WindowFallback));
        Assert.True(Luma(ThemeTokens.Light.PanelSurface) > Luma(ThemeTokens.Dark.PanelSurface));
    }

    [Fact]
    public void 飘在玻璃上的表面必须带透明度()
    {
        // 面板/标签栏/遮罩这些"浮层"一旦完全不透明，就会把窗口材质盖住（ERROR.md E9）
        Assert.True(ThemeTokens.Dark.PanelSurface.A < 255);
        Assert.True(ThemeTokens.Light.PanelSurface.A < 255);
        Assert.True(ThemeTokens.Dark.TabStripSurface.A < 255);
        Assert.True(ThemeTokens.Light.TabStripSurface.A < 255);
        Assert.True(ThemeTokens.Dark.Overlay.A < 255);
    }

    [Theory]
    [InlineData("system", ThemeMode.System)]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("dark", ThemeMode.Dark)]
    [InlineData("DARK", ThemeMode.Dark)]
    [InlineData(" light ", ThemeMode.Light)]
    [InlineData("", ThemeMode.System)]
    [InlineData(null, ThemeMode.System)]
    [InlineData("乱写的值", ThemeMode.System)]   // 非法值回落"跟随系统"，与原版归一化口径一致
    public void 主题模式解析_大小写与空白与非法值(string? wire, ThemeMode expected)
    {
        Assert.Equal(expected, ThemeTokens.ParseMode(wire));
    }

    [Fact]
    public void 主题模式往返转换稳定()
    {
        foreach (var mode in new[] { ThemeMode.System, ThemeMode.Light, ThemeMode.Dark })
        {
            Assert.Equal(mode, ThemeTokens.ParseMode(ThemeTokens.ToWire(mode)));
        }
    }

    [Theory]
    [InlineData(ThemeMode.Light, true, false)]    // 强制浅色：系统再深也是浅色
    [InlineData(ThemeMode.Dark, false, true)]     // 强制深色：系统再浅也是深色
    [InlineData(ThemeMode.System, true, true)]    // 跟随系统：系统深则深
    [InlineData(ThemeMode.System, false, false)]
    public void 解析令牌_强制模式不受系统影响_跟随系统才看系统(ThemeMode mode, bool systemIsDark, bool expectDark)
    {
        Assert.Equal(expectDark, ThemeTokens.Resolve(mode, systemIsDark).IsDark);
    }

    // ────────────────────────────── config.json 契约 ──────────────────────────────

    [Fact]
    public void 界面主题_默认跟随系统且不写进文件()
    {
        var settings = new AppSettings();

        Assert.Equal(ThemeMode.System, settings.UiTheme);
        Assert.Null(settings.UiThemeRaw);   // null = 文件里没有这个键 ⇒ 不写回（保住逐字节不变）

        var json = JsonSerializer.Serialize(settings, TabsJson.Options);
        Assert.DoesNotContain("ui_theme", json);
    }

    [Fact]
    public void 界面主题_设置后能往返且与老线的theme字段互不干扰()
    {
        var settings = new AppSettings
        {
            Theme = "dark",                     // 老线/QML 线的主题名
            ThemeOverrides = new Dictionary<string, JsonElement>(),
            UiTheme = ThemeMode.Light,          // 新增的界面主题
        };

        var json = JsonSerializer.Serialize(settings, TabsJson.Options);
        Assert.Contains("ui_theme", json);
        Assert.Contains("\"theme\": \"dark\"", json);   // 老字段原样保留

        var reloaded = JsonSerializer.Deserialize<AppSettings>(json, TabsJson.Options)!;
        Assert.Equal(ThemeMode.Light, reloaded.UiTheme);
        Assert.Equal("dark", reloaded.Theme);
    }

    [Fact]
    public void 界面主题_非法值在载入时被归一化()
    {
        using var temp = new TempDataDirectory();
        temp.WriteRawTabsJson("{}");
        File.WriteAllText(
            Path.Combine(temp.Path, "config.json"),
            "{\"ui_theme\":\"  DAZZLE  \"}",
            new System.Text.UTF8Encoding(false));

        var store = new SettingsStore(temp.Path);
        var settings = store.Load();

        Assert.Equal(ThemeMode.System, settings.UiTheme);   // 未知值 → 跟随系统
        Assert.Equal("system", settings.UiThemeRaw);
    }
}
