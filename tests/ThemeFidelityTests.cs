// ThemeFidelityTests.cs —— ★ 主题保真测试（与原版 `theme.py` 逐字段一致）
//
// 这是"5 预置 + 8 参数照抄原版"这条验收条款的**唯一硬证据**：
// 用**真实 Python** 读原版 `src/toolbox/ui/theme.py` 的**数据段**，与 C# 侧逐字段比对
// （21 个颜色令牌 × 5 个预置、8 个参数的默认/范围/步长/中英标签）。
//
// ⚠️ 为什么只 exec 数据段、不 import 整个模块：`theme.py` 顶部 `from PyQt6... import ...`，
//    整模块 import 会把 PyQt6 拉起来（没装的机器直接失败）。数据段（DARK / LIGHT /
//    PARAM_SPECS / PRESETS）在文件前半部分，与 Qt 无关 —— 截取到 `def preset_names` 之前、
//    去掉 import 行即可，**读到的仍然是原文件里的真实字面量**。
//
// 与 `I18nTests` 同一套纪律：开发副本 + PATH 有 python 时才跑，否则由 [PythonFact] **显式跳过**
// （报告里显示"已跳过"，不是静默 passed）；开发副本里探测不到 python / 缺 theme.py 则**显式失败**。

using System.Text;
using System.Text.Json;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class ThemeFidelityTests
{
    /// <summary>截取原版 theme.py 的数据段并打印成 JSON（保留 dict 的插入顺序）。</summary>
    private const string DumpScript = """
        import json, sys
        src = open(sys.argv[1], encoding="utf-8").read()
        cut = src.index("def preset_names")
        prefix = src[:cut]
        keep = [line for line in prefix.splitlines() if not line.startswith(("import ", "from "))]
        namespace = {}
        exec(compile("\n".join(keep), sys.argv[1], "exec"), namespace)
        print(json.dumps({
            "param_specs": namespace["PARAM_SPECS"],
            "presets": namespace["PRESETS"],
            "dark": namespace["DARK"],
            "light": namespace["LIGHT"],
            "default": namespace["DEFAULT_PRESET"],
        }, ensure_ascii=False))
        """;

    /// <summary>读原版 theme.py 的数据段；开发副本里缺文件 ⇒ 显式失败（不许静默跳过）。</summary>
    private static JsonElement LoadOriginalTheme()
    {
        var themePy = PythonRunner.RequireOriginalFile("src", "toolbox", "ui", "theme.py");

        var scriptPath = Path.Combine(Path.GetTempPath(), $"tb-theme-dump-{Guid.NewGuid():N}.py");
        File.WriteAllText(scriptPath, DumpScript, new UTF8Encoding(false));

        try
        {
            var (exitCode, stdout, stderr) = PythonRunner.RunScript(scriptPath, new[] { themePy });
            Assert.True(exitCode == 0, $"python 退出码 {exitCode}：{stderr}");

            using var document = JsonDocument.Parse(stdout);
            return document.RootElement.Clone();
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    private static IReadOnlyDictionary<string, string> ToColorMap(JsonElement element)
        => element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);

    [PythonFact]
    public void 五个预置的颜色与参数与原版theme_py逐字段一致()
    {
        var original = LoadOriginalTheme();

        Assert.Equal(ThemePresets.DefaultId, original.GetProperty("default").GetString());

        var presets = original.GetProperty("presets");
        var originalIds = presets.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(originalIds, ThemePresets.Ids);

        foreach (var entry in presets.EnumerateObject())
        {
            var preset = ThemePresets.Find(entry.Name);
            Assert.NotNull(preset);

            // ①② 中英标签
            Assert.Equal(entry.Value.GetProperty("zh").GetString(), preset!.Zh);
            Assert.Equal(entry.Value.GetProperty("en").GetString(), preset.En);

            // ③ 21 个颜色令牌：逐条逐字
            var expectedColors = ToColorMap(entry.Value.GetProperty("colors"));
            Assert.Equal(expectedColors.Keys.OrderBy(k => k, StringComparer.Ordinal),
                preset.Colors.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var (token, color) in expectedColors)
            {
                Assert.Equal(color, preset.Colors[token]);
            }

            // ④ 8 个参数：逐条逐字
            var expectedParams = entry.Value.GetProperty("params")
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetDouble());
            Assert.Equal(expectedParams.Keys.OrderBy(k => k, StringComparer.Ordinal),
                preset.Params.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var (key, value) in expectedParams)
            {
                Assert.Equal(value, preset.ParamOrDefault(key));
            }
        }
    }

    [PythonFact]
    public void 深色与浅色基线令牌与原版完全一致()
    {
        var original = LoadOriginalTheme();

        var dark = ToColorMap(original.GetProperty("dark"));
        var light = ToColorMap(original.GetProperty("light"));

        Assert.Equal(dark.Keys.OrderBy(k => k, StringComparer.Ordinal),
            ThemePresets.DarkColors.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(light.Keys.OrderBy(k => k, StringComparer.Ordinal),
            ThemePresets.LightColors.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var (token, color) in dark)
        {
            Assert.Equal(color, ThemePresets.DarkColors[token]);
        }

        foreach (var (token, color) in light)
        {
            Assert.Equal(color, ThemePresets.LightColors[token]);
        }
    }

    [PythonFact]
    public void 八个参数的默认范围步长与中英标签与原版一致()
    {
        var original = LoadOriginalTheme();

        var specs = original.GetProperty("param_specs");
        var originalKeys = specs.EnumerateObject().Select(p => p.Name).ToList();

        // 顺序也要一致：界面（以及 `paramSpecs` 那套自动生成滑杆）都照这个顺序铺
        Assert.Equal(originalKeys, ThemeParamSpecs.Keys);

        foreach (var entry in specs.EnumerateObject())
        {
            var spec = ThemeParamSpecs.Find(entry.Name);
            Assert.NotNull(spec);

            // 元组 = (默认值, 最小, 最大, 步长, 中文名, 英文名) —— 后两项是字符串，不能一律当数字读
            var tuple = entry.Value.EnumerateArray().ToList();
            Assert.Equal(6, tuple.Count);

            Assert.Equal(tuple[0].GetDouble(), spec!.Default);
            Assert.Equal(tuple[1].GetDouble(), spec.Min);
            Assert.Equal(tuple[2].GetDouble(), spec.Max);
            Assert.Equal(tuple[3].GetDouble(), spec.Step);
            Assert.Equal(tuple[4].GetString(), spec.Zh);
            Assert.Equal(tuple[5].GetString(), spec.En);
        }
    }
}
