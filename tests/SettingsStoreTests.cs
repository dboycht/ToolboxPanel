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

        // 旧配置没有 tab_icon_mode / animations → 用新增字段的默认值
        Assert.Equal(TabIconMode.Hover, settings.TabIconMode);
        Assert.True(settings.AnimationsEnabled);
    }
}
