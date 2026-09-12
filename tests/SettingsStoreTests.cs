// SettingsStoreTests.cs —— 设置层（W3）：默认值、容错、归一化、与原版 config.json 的兼容

using System.Text;
using System.Text.Json;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class SettingsStoreTests
{
    /// <summary>开发副本里真实存在的 config.json 内容（QML 线写的，含 theme_overrides）。</summary>
    private const string RealisticConfig = """
    {
      "language": "zh",
      "icon_size": "medium",
      "theme": "matcha",
      "theme_overrides": {
        "param:window_opacity": 0.34
      }
    }
    """;

    [Fact]
    public void 没有配置文件时用默认值且不报错()
    {
        using var temp = new TempDataDirectory();
        var settings = new SettingsStore(temp.Path).Load();

        Assert.Equal("zh", settings.Language);
        Assert.Equal("medium", settings.IconSize);
        Assert.Equal("dark", settings.Theme);
        Assert.Empty(settings.ThemeOverrides!);
        Assert.Equal(TabIconMode.Hover, settings.TabIconMode);
        Assert.True(settings.AnimationsEnabled);
        Assert.False(File.Exists(Path.Combine(temp.Path, "config.json")));   // 只读不写
    }

    [Fact]
    public void 配置文件损坏时回落默认值_不抛异常也不备份()
    {
        using var temp = new TempDataDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "config.json"), "{ \"language\": ", new UTF8Encoding(false));

        var settings = new SettingsStore(temp.Path).Load();

        Assert.Equal("zh", settings.Language);
        Assert.False(File.Exists(Path.Combine(temp.Path, "config.json.bak")));   // 原版策略：不备份
    }

    [Theory]
    [InlineData("fr", "zh")]
    [InlineData("EN", "en")]
    [InlineData("", "zh")]
    public void 非法语言归一化(string input, string expected)
    {
        using var temp = new TempDataDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "config.json"),
            $"{{ \"language\": \"{input}\" }}",
            new UTF8Encoding(false));

        Assert.Equal(expected, new SettingsStore(temp.Path).Load().Language);
    }

    [Theory]
    [InlineData("giant", "medium")]
    [InlineData("small", "small")]
    [InlineData("LARGE", "large")]
    public void 非法图标尺寸归一化(string input, string expected)
    {
        using var temp = new TempDataDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "config.json"),
            $"{{ \"icon_size\": \"{input}\" }}",
            new UTF8Encoding(false));

        Assert.Equal(expected, new SettingsStore(temp.Path).Load().IconSize);
    }

    [Fact]
    public void 空主题名归一化为默认主题()
    {
        using var temp = new TempDataDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "config.json"), "{ \"theme\": \"  \" }", new UTF8Encoding(false));

        Assert.Equal("dark", new SettingsStore(temp.Path).Load().Theme);
    }

    [Theory]
    [InlineData("text", TabIconMode.Text)]
    [InlineData("always", TabIconMode.Always)]
    [InlineData("hover", TabIconMode.Hover)]
    [InlineData("bogus", TabIconMode.Hover)]
    [InlineData(null, TabIconMode.Hover)]
    public void 标签图标形态解析与容错(string? wire, TabIconMode expected)
    {
        Assert.Equal(expected, AppSettings.ParseTabIconMode(wire));
    }

    [Fact]
    public void 真实config往返后逐字节不变_不丢用户的主题细调()
    {
        using var temp = new TempDataDirectory();
        var configPath = Path.Combine(temp.Path, "config.json");

        // 原版 Python 在 Windows 上写出的是 CRLF（与 tabs.json 同一套格式），这里照样写
        File.WriteAllText(configPath, RealisticConfig.Replace("\n", "\r\n"), new UTF8Encoding(false));
        var before = File.ReadAllBytes(configPath);

        var store = new SettingsStore(temp.Path);
        var settings = store.Load();
        store.Save(settings);

        // 字段一个不少（含 theme_overrides 的数值型令牌覆盖）
        Assert.Equal("matcha", settings.Theme);
        Assert.NotNull(settings.ThemeOverrides);
        Assert.Equal(0.34, settings.ThemeOverrides!["param:window_opacity"].GetDouble(), 3);

        // 没被改过的配置写回必须逐字节不变（新字段 null → 不写进文件）
        Assert.Equal(
            Encoding.UTF8.GetString(before),
            File.ReadAllText(configPath, Encoding.UTF8));
    }

    [Fact]
    public void 保存后能被原版Python的语义读回_字段名一致()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        var settings = store.Load();

        store.Update(s =>
        {
            s.TabIconMode = TabIconMode.Always;
            s.AnimationsEnabled = false;
        });

        using var document = JsonDocument.Parse(File.ReadAllText(store.SettingsFile, Encoding.UTF8));
        var root = document.RootElement;

        // 原版认识的字段必须还在、类型不变
        Assert.Equal("zh", root.GetProperty("language").GetString());
        Assert.Equal("medium", root.GetProperty("icon_size").GetString());
        Assert.Equal("dark", root.GetProperty("theme").GetString());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("theme_overrides").ValueKind);

        // C# 线新增的字段
        Assert.Equal("always", root.GetProperty("tab_icon_mode").GetString());
        Assert.False(root.GetProperty("animations").GetBoolean());
    }

    [Fact]
    public void 写出格式与tabs同一套_无BOM且CRLF且两空格缩进()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        store.Load();
        store.Save(store.Current);

        var bytes = File.ReadAllBytes(store.SettingsFile);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty));
        Assert.Contains("\r\n  \"language\": \"zh\",", text);
        Assert.False(File.Exists(store.SettingsTempFile));      // 原子写不留临时文件
    }

    [Fact]
    public void 未知字段被保留()
    {
        using var temp = new TempDataDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "config.json"),
            """{ "language": "zh", "future_key": { "a": 1 } }""",
            new UTF8Encoding(false));

        var store = new SettingsStore(temp.Path);
        store.Load();
        store.Save(store.Current);

        var text = File.ReadAllText(store.SettingsFile, Encoding.UTF8);
        Assert.Contains("future_key", text);
    }

    [Fact]
    public void 新增字段缺省时不影响旧配置读取()
    {
        using var temp = new TempDataDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "config.json"), RealisticConfig, new UTF8Encoding(false));

        var settings = new SettingsStore(temp.Path).Load();

        // 旧配置没有 tab_icon_mode / animations 等 → 用新增字段的默认值
        Assert.Equal(TabIconMode.Hover, settings.TabIconMode);
        Assert.True(settings.AnimationsEnabled);
        Assert.Equal("mica", settings.Backdrop);
        Assert.True(settings.ShowTabCounts);
        Assert.Equal(220, settings.AnimationDurationMs);
        Assert.Equal(24, settings.AnimationStaggerMs);
        Assert.Equal(AnimationEasing.Standard, settings.AnimationEasing);
    }

    [Theory]
    [InlineData("soft", AnimationEasing.Soft)]
    [InlineData("SNAPPY", AnimationEasing.Snappy)]
    [InlineData("standard", AnimationEasing.Standard)]
    [InlineData("bogus", AnimationEasing.Standard)]
    [InlineData(null, AnimationEasing.Standard)]
    public void 动效曲线解析与容错(string? wire, AnimationEasing expected)
    {
        Assert.Equal(expected, AppSettings.ParseEasing(wire));
    }

    [Theory]
    [InlineData("mica", "mica")]
    [InlineData("ACRYLICTHIN", "acrylicThin")]
    [InlineData("none", "none")]
    [InlineData("bogus", "mica")]
    [InlineData(null, "mica")]
    public void 材质名归一化(string? input, string expected)
    {
        Assert.Equal(expected, ToolboxPanel.Core.Storage.BackdropKinds.Normalize(input));
    }

    [Fact]
    public void 动效参数换算与夹取()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        var settings = store.Load();

        // 关掉动效 → Disabled
        settings.AnimationsEnabled = false;
        Assert.False(settings.ToAnimationSpec().Enabled);

        // 打开并给超范围的值 → 夹到合法区间
        settings.AnimationsEnabled = true;
        settings.AnimationDurationMs = 99999;
        settings.AnimationStaggerMs = -5;
        settings.AnimationEasing = AnimationEasing.Snappy;

        var spec = settings.ToAnimationSpec();
        Assert.True(spec.Enabled);
        Assert.Equal(1200, spec.DurationMs);
        Assert.Equal(0, spec.StaggerMs);
        Assert.Equal(AnimationEasing.Snappy, spec.Easing);
        Assert.InRange(spec.FromOffset, 4, 24);
    }

    [Fact]
    public void 新增设置项往返不变()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        var settings = store.Load();

        store.Update(s =>
        {
            s.Backdrop = "acrylicThin";
            s.TabIconMode = TabIconMode.Always;
            s.ShowTabCounts = false;
            s.AnimationsEnabled = true;
            s.AnimationDurationMs = 320;
            s.AnimationStaggerMs = 40;
            s.AnimationEasing = AnimationEasing.Soft;
            s.WindowSize = (1024, 768);
        });

        var reloaded = new SettingsStore(temp.Path).Load();

        Assert.Equal("acrylicThin", reloaded.Backdrop);
        Assert.Equal(TabIconMode.Always, reloaded.TabIconMode);
        Assert.False(reloaded.ShowTabCounts);
        Assert.Equal(320, reloaded.AnimationDurationMs);
        Assert.Equal(40, reloaded.AnimationStaggerMs);
        Assert.Equal(AnimationEasing.Soft, reloaded.AnimationEasing);
        Assert.Equal((1024, 768), reloaded.WindowSize);
    }

    [Fact]
    public void 窗口尺寸_没记录过时返回null()
    {
        using var temp = new TempDataDirectory();
        var settings = new SettingsStore(temp.Path).Load();

        Assert.Null(settings.WindowSize);
    }

    [Theory]
    [InlineData(0, 0)]          // 手改成 0：写入时夹到下限
    [InlineData(100, 80)]       // 太小：夹到下限
    [InlineData(-5, 600)]       // 负数：夹到下限
    public void 窗口尺寸_写入时夹取到合法区间(int width, int height)
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        var settings = store.Load();

        settings.WindowSize = (width, height);
        store.Save(settings);                    // 落盘后再读回来核对

        var size = new SettingsStore(temp.Path).Load().WindowSize!.Value;
        Assert.InRange(size.Width, AppSettings.MinWindowWidth, AppSettings.MaxWindowWidth);
        Assert.InRange(size.Height, AppSettings.MinWindowHeight, AppSettings.MaxWindowHeight);
    }

    [Fact]
    public void 窗口尺寸_清空后不再写进文件()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        var settings = store.Load();

        settings.WindowSize = (1000, 700);
        store.Save(settings);
        Assert.NotNull(new SettingsStore(temp.Path).Load().WindowSize);

        settings.WindowSize = null;
        store.Save(settings);

        Assert.Null(new SettingsStore(temp.Path).Load().WindowSize);
        Assert.DoesNotContain("window_width", File.ReadAllText(store.SettingsFile, Encoding.UTF8));
    }

    [Fact]
    public void 窗口尺寸_手改配置成非法值时不崩且回落默认()
    {
        using var temp = new TempDataDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "config.json"),
            """{ "language": "zh", "window_width": 10, "window_height": 10 }""",
            new UTF8Encoding(false));

        var settings = new SettingsStore(temp.Path).Load();

        Assert.Null(settings.WindowSize);   // 调用方据此用默认尺寸
    }
}
