// I18nTests.cs —— 双语文案（W5 · v2.0.3）
//
// 两类测试：
//   ① ★ **保真测试**：用**真实 Python** 解析原版 `src/toolbox/i18n.py`，与 C# 的 `I18n.Table`
//      **逐 key、逐语言逐字比对**（193 条 × 2 种语言，含多行文案）。这是"key 命名与文案照原版"
//      这条验收条款的唯一硬证据；开发副本 + PATH 有 python 时才跑，否则由 [PythonFact] **显式跳过**
//      （报告里显示"已跳过"，不是静默 passed）；开发副本里探测不到 python / 缺 i18n.py 则**显式失败**。
//   ② 运行时行为：未知 key 回落 `??key??`、命名占位符替换、**缺参数保留占位符不抛异常**、
//      语言切换幂等 + 事件只在真的变化时触发、白名单与 `AppSettings.Languages` 同源。

using System.Text;
using System.Text.Json;
using ToolboxPanel.Core;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class I18nTests
{
    /// <summary>把原版 i18n.py 当独立模块 import，把 TEXTS 以 JSON 打出来（顺序保留）。</summary>
    private const string DumpScript = """
        import importlib.util, json, sys
        spec = importlib.util.spec_from_file_location("toolbox_i18n_probe", sys.argv[1])
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        print(json.dumps(module.TEXTS, ensure_ascii=False))
        """;

    // ────────────────────────────── ★ 保真：与原版 i18n.py 逐条一致 ──────────────────────────────

    [PythonFact]
    public void 文案表与原版i18n_py逐条一致()
    {
        // 开发副本 + python 齐备才会走到这里（否则 [PythonFact] 已经 Skip / 开发副本里缺 python 直接抛异常）
        var i18nPy = PythonRunner.RequireOriginalFile("src", "toolbox", "i18n.py");

        var scriptPath = Path.Combine(Path.GetTempPath(), $"tb-i18n-dump-{Guid.NewGuid():N}.py");
        File.WriteAllText(scriptPath, DumpScript, new UTF8Encoding(false));

        try
        {
            var (exitCode, stdout, stderr) = PythonRunner.RunScript(scriptPath, new[] { i18nPy });
            Assert.True(exitCode == 0, $"python 退出码 {exitCode}：{stderr}");

            using var document = JsonDocument.Parse(stdout);
            var original = document.RootElement;

            // ① key 集合必须完全一致（C# 不能少、也不该多出原版没有的 key —— 多出来的要显式说明用途）
            var originalKeys = original.EnumerateObject().Select(p => p.Name).ToList();
            Assert.Equal(originalKeys.Count, I18n.Keys.Count);
            Assert.Equal(
                originalKeys.OrderBy(k => k, StringComparer.Ordinal),
                I18n.Keys.OrderBy(k => k, StringComparer.Ordinal));

            // ② 每条：中英两种语言都逐字一致
            foreach (var entry in original.EnumerateObject())
            {
                var key = entry.Name;
                Assert.True(I18n.Has(key), $"C# 文案表缺 key：{key}");
                Assert.Equal(entry.Value.GetProperty("zh").GetString(), I18n.Raw(key, "zh"));
                Assert.Equal(entry.Value.GetProperty("en").GetString(), I18n.Raw(key, "en"));
            }
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public void 每条文案的中英文都非空()
    {
        Assert.NotEmpty(I18n.Keys);
        foreach (var key in I18n.Keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(I18n.Raw(key, "zh")), $"中文为空：{key}");
            Assert.False(string.IsNullOrWhiteSpace(I18n.Raw(key, "en")), $"英文为空：{key}");
        }
    }

    // ────────────────────────────── 运行时行为 ──────────────────────────────

    [Fact]
    public void 未知key回落为双问号包裹()
    {
        Assert.Equal("??no.such.key??", I18n.T("no.such.key"));
        Assert.Equal("??no.such.key??", I18n.Raw("no.such.key", "zh"));
        Assert.False(I18n.Has("no.such.key"));
    }

    [Fact]
    public void 命名占位符按参数替换()
    {
        Assert.Equal("已添加: 记事本", I18n.T("status.added", ("name", "记事本")));
        Assert.Equal("已删除 3 个图标", I18n.T("bulk_delete.done", ("count", 3)));
    }

    [Fact]
    public void 缺参数时保留占位符且不抛异常()
    {
        // 原版 `text.format(**kwargs)` 遇 KeyError 返回原文 —— 我们连"一个参数都没传"也照这个语义
        Assert.Equal("已添加: {name}", I18n.T("status.added"));
        Assert.Equal("已删除 {count} 个图标", I18n.T("bulk_delete.done"));
    }

    [Fact]
    public void 多余的参数被忽略_名字对不上的占位符原样保留()
    {
        // 参数比占位符多：多出来的参数不影响结果
        Assert.Equal("已添加: 记事本", I18n.T("status.added", ("name", "记事本"), ("path", @"C:\x")));

        // 参数名与占位符名对不上：占位符原样留着（便于一眼看出漏传参）
        Assert.Equal("已添加: {name}", I18n.T("status.added", ("nome", "记事本")));
    }

    [Fact]
    public void 只替换配得上名字的占位符_其余原样()
    {
        // 造一条含两个占位符的真实文案场景（app.about.text 里有 {version}）
        var text = I18n.T("app.about.text", ("version", "2.0.3"));

        Assert.Contains("工具箱 v2.0.3", text);
        Assert.DoesNotContain("{version}", text);
    }

    [Fact]
    public void 切换语言后取对应语言的文案()
    {
        I18n.SetLanguage("zh");
        Assert.Equal("工具箱", I18n.T("app.title"));
        Assert.True(I18n.IsChinese);

        I18n.SetLanguage("en");
        Assert.Equal("Toolbox", I18n.T("app.title"));
        Assert.False(I18n.IsChinese);

        I18n.SetLanguage("zh");   // 收尾：别把语言状态留给其它测试
        Assert.Equal("工具箱", I18n.T("app.title"));
    }

    [Fact]
    public void 语言切换事件只在真的变化时触发()
    {
        I18n.SetLanguage("zh");

        int fired = 0;
        void Handler() => fired++;
        I18n.LanguageChanged += Handler;

        try
        {
            I18n.SetLanguage("zh");        // 同一个 ⇒ 不触发
            Assert.Equal(0, fired);

            I18n.SetLanguage("en");        // 变了 ⇒ 触发一次
            Assert.Equal(1, fired);

            I18n.SetLanguage("EN");        // 同一个（大小写不同）⇒ 不触发
            Assert.Equal(1, fired);

            I18n.SetLanguage("fr");        // 未知 ⇒ 收敛成默认语言 zh ⇒ 触发一次（en → zh）
            Assert.Equal(2, fired);
            Assert.Equal("zh", I18n.Current);
        }
        finally
        {
            I18n.LanguageChanged -= Handler;
            I18n.SetLanguage("zh");
        }
    }

    [Fact]
    public void 语言白名单与设置层同源()
    {
        Assert.Equal(AppSettings.Languages, I18n.Languages);
        Assert.Equal(AppSettings.DefaultLanguage, I18n.DefaultLanguage);
        Assert.Equal("zh", I18n.Normalize(null));
        Assert.Equal("zh", I18n.Normalize("   "));
        Assert.Equal("zh", I18n.Normalize("de"));
        Assert.Equal("en", I18n.Normalize("EN"));
        Assert.Equal("en", I18n.Normalize(" en "));
    }

    // ────────────────────────────── 各 Core 服务的文案真的跟着语言走 ──────────────────────────────

    [Fact]
    public void 核心服务文案跟着语言切换()
    {
        try
        {
            I18n.SetLanguage("zh");
            Assert.Equal("已添加: 记事本", DropImporter.AddedMessage("记事本"));
            Assert.Equal("路径不存在: C:\\x", IconContextMenu.PathMissingMessage(@"C:\x"));
            Assert.Equal("批量管理", BulkDelete.MenuLabel);
            Assert.Equal("已删除 3 个图标", BulkDelete.DoneText(3));
            Assert.Equal("没有匹配的图标", SearchFilter.NoResultIconText);
            Assert.Equal("匹配 3 / 19", SearchFilter.CountText(3, 19));
            Assert.Equal("名称不能为空", IconEditor.ErrorNameRequired);
            Assert.Equal("新建列表项", ListItemEditor.NewItemTitle);
            Assert.Equal("新建标签页", TabModel.DefaultName);
            Assert.Equal("打开", IconContextMenu.LabelOpen);
            Assert.Equal("示例", SampleIcons.SampleTabName);

            I18n.SetLanguage("en");
            Assert.Equal("Added: 记事本", DropImporter.AddedMessage("记事本"));
            Assert.Equal(@"Path not found: C:\x", IconContextMenu.PathMissingMessage(@"C:\x"));
            Assert.Equal("Batch Manage", BulkDelete.MenuLabel);
            Assert.Equal("Deleted 3 icon(s)", BulkDelete.DoneText(3));
            Assert.Equal("No matching icons", SearchFilter.NoResultIconText);
            Assert.Equal("3 / 19 matched", SearchFilter.CountText(3, 19));
            Assert.Equal("Name cannot be empty", IconEditor.ErrorNameRequired);
            Assert.Equal("New List Item", ListItemEditor.NewItemTitle);
            Assert.Equal("New Tab", TabModel.DefaultName);
            Assert.Equal("Open", IconContextMenu.LabelOpen);
            Assert.Equal("Sample", SampleIcons.SampleTabName);

            // 拖放失败原因（数据层）
            Assert.Equal("Source tab no longer exists", I18n.T("drag.error.source_missing"));
            // 备份进度日志（服务层）
            Assert.Equal("Collecting data files...", I18n.T("backup.log.collect"));
        }
        finally
        {
            I18n.SetLanguage("zh");
        }
    }

    [Fact]
    public void 关于的六条提示就是原版关于文本里的那六行()
    {
        // 原版把 6 条提示塞在一整段 `app.about.text` 里；WinUI 线拆成了 about.feature.1..6。
        // 这条测试保证"拆出来的 6 条"与"原版那段文本里的 6 行"逐条一致（两种语言都查）。
        try
        {
            foreach (var language in I18n.Languages)
            {
                I18n.SetLanguage(language);
                var aboutText = I18n.T("app.about.text", ("version", "x"));
                var features = AboutInfo.DefaultFeatures;

                Assert.Equal(6, features.Count);
                foreach (var feature in features)
                {
                    Assert.Contains($"• {feature}", aboutText);
                }
            }
        }
        finally
        {
            I18n.SetLanguage("zh");
        }
    }

    [Fact]
    public void 关于对话框的字段与诊断项也跟着语言()
    {
        try
        {
            I18n.SetLanguage("en");
            var info = AboutInfo.Create("2.0.3", dataDirectory: null, isDemo: true);

            Assert.Equal("Phone-home-screen style launcher", info.Subtitle);
            Assert.Equal("2.0.3", info.Version);
            Assert.Equal("Data directory", info.Diagnostics[0].Label);
            Assert.Equal("(not located)", info.Diagnostics[0].Value);
            Assert.Equal("Demo mode (no files read or written)", info.Diagnostics[1].Value);
            Assert.Contains("Author: dboycht", info.ToPlainText());

            // 版本为空 ⇒ 未知（英文语境下也应当是英文）
            Assert.Equal("Unknown", AboutInfo.Create("", null, false).Version);
        }
        finally
        {
            I18n.SetLanguage("zh");
        }
    }

    [Fact]
    public void 示例图标的名字跟着创建时的语言()
    {
        try
        {
            var chinese = SampleIcons.BuildForExisting().Select(i => i.DisplayName).ToList();

            I18n.SetLanguage("en");
            var english = SampleIcons.BuildForExisting().Select(i => i.DisplayName).ToList();

            // 同一台机器上两次装配出来的条目数必须一致（只是名字换了语言）
            Assert.Equal(chinese.Count, english.Count);

            // 英文下不该再出现中文名（清单里的名字都来自 sample.* key）
            foreach (var name in english)
            {
                Assert.DoesNotMatch("[\u4e00-\u9fff]", name);
            }

            if (chinese.Contains("记事本"))
            {
                Assert.Contains("Notepad", english);
            }
        }
        finally
        {
            I18n.SetLanguage("zh");
        }
    }
}
