// ShortcutBindingsTests.cs —— 2.0.6：快捷键绑定的默认值 / 覆盖 / 冲突判据
//
// 这一族测的是"用户改键"这件事的**规则**（界面只负责显示与采集按键）：
//   · 覆盖只影响改过的那条，其余回落默认；
//   · 覆盖表是**差分**存储（等于默认的不写）；
//   · 四类"静态可判"的冲突：重复 / 输入框编辑键 / 系统保留 / 缺修饰键；
//   · 非法覆盖（认不出的动作名或键位）被忽略而不是让界面炸掉。

using ToolboxPanel.Core.Services;
using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

public class ShortcutBindingsTests
{
    // ────────────────────────────── 默认值 ──────────────────────────────

    [Fact]
    public void 默认绑定与目录一致()
    {
        Assert.Equal(ShortcutCatalog.AppShortcuts.Count(), ShortcutBindings.Defaults.Count);
        Assert.All(ShortcutBindings.Defaults, binding =>
            Assert.Equal(ShortcutCatalog.Find(binding.Action)!.Gesture.Display, binding.Gesture.Display));
    }

    [Fact]
    public void 没有覆盖时解析结果就是默认()
    {
        var resolved = ShortcutBindings.Resolve(null);

        Assert.Equal(ShortcutBindings.Defaults.Count, resolved.Count);
        Assert.All(resolved, binding =>
            Assert.Equal(ShortcutBindings.DefaultGesture(binding.Action).Display, binding.Gesture.Display));
    }

    // ────────────────────────────── 覆盖 ──────────────────────────────

    [Fact]
    public void 覆盖只改指定的那条_其余回落默认()
    {
        var overrides = new Dictionary<string, string> { ["NewTab"] = "Ctrl+Alt+N" };

        var resolved = ShortcutBindings.Resolve(overrides);

        Assert.Equal("Ctrl+Alt+N", resolved.Single(b => b.Action == ShortcutAction.NewTab).Gesture.Display);
        Assert.Equal("Ctrl+W", resolved.Single(b => b.Action == ShortcutAction.CloseTab).Gesture.Display);
    }

    [Theory]
    [InlineData("NotAnAction", "Ctrl+Q")]      // 动作名认不出
    [InlineData("NewTab", "Ctrl+")]            // 键位串解析不出来（只有修饰键）
    [InlineData("NewTab", "   ")]              // 空串
    public void 非法覆盖被忽略而不是抛异常(string action, string keys)
    {
        var resolved = ShortcutBindings.Resolve(new Dictionary<string, string> { [action] = keys });

        Assert.Equal(ShortcutBindings.Defaults.Count, resolved.Count);
        Assert.Equal("Ctrl+T", resolved.Single(b => b.Action == ShortcutAction.NewTab).Gesture.Display);
    }

    [Fact]
    public void 覆盖表是差分存储_等于默认的不写进去()
    {
        var bindings = ShortcutBindings.Resolve(new Dictionary<string, string> { ["NewTab"] = "Ctrl+Alt+N" });

        var overrides = ShortcutBindings.ToOverrides(bindings);

        Assert.Single(overrides);
        Assert.Equal("Ctrl+Alt+N", overrides["NewTab"]);
    }

    [Fact]
    public void 改回默认后_覆盖表里就没有这一条了()
    {
        // 先改一条，再"恢复默认" ⇒ 差分要点：恢复默认 = **从覆盖里删掉**，而不是写一条等于默认的值
        var changed = ShortcutBindings.Resolve(new Dictionary<string, string> { ["NewTab"] = "Ctrl+Alt+N" });
        var restored = ShortcutBindings.RestoreDefault(changed, ShortcutAction.NewTab);

        Assert.Empty(ShortcutBindings.ToOverrides(restored));
        Assert.Equal("Ctrl+T", restored.Single(b => b.Action == ShortcutAction.NewTab).Gesture.Display);
    }

    [Fact]
    public void 恢复默认只动指定的那一条()
    {
        var bindings = ShortcutBindings.Resolve(new Dictionary<string, string>
        {
            ["NewTab"] = "Ctrl+Alt+N",
            ["CloseTab"] = "Ctrl+Alt+W",
        });

        var restored = ShortcutBindings.RestoreDefault(bindings, ShortcutAction.NewTab);

        Assert.Equal("Ctrl+T", restored.Single(b => b.Action == ShortcutAction.NewTab).Gesture.Display);
        Assert.Equal("Ctrl+Alt+W", restored.Single(b => b.Action == ShortcutAction.CloseTab).Gesture.Display);
    }

    // ────────────────────────────── 冲突判据 ──────────────────────────────

    [Fact]
    public void 默认绑定没有重复_但Shift加Delete会被标成输入框编辑键()
    {
        var issues = ShortcutBindings.FindIssues(ShortcutBindings.Defaults);

        // 默认值之间不该有重复
        Assert.DoesNotContain(issues.Values, info => info.Issue == ShortcutIssue.Duplicate);

        // `Shift+Delete`（批量删除）是**唯一**一条在输入框里会被抢走的默认值 —— 这是事实，界面该提示
        Assert.Equal(ShortcutIssue.TextEditing, issues[ShortcutAction.BatchDelete].Issue);
    }

    [Fact]
    public void 重复绑定被检出_并带上对方的功能名()
    {
        var bindings = ShortcutBindings.Defaults
            .Select(b => b.Action == ShortcutAction.CloseTab ? b with { Gesture = new ShortcutGesture("T", Ctrl: true) } : b)
            .ToList();

        var issues = ShortcutBindings.FindIssues(bindings);

        Assert.Equal(ShortcutIssue.Duplicate, issues[ShortcutAction.CloseTab].Issue);
        Assert.Equal(ShortcutIssue.Duplicate, issues[ShortcutAction.NewTab].Issue);
        // 消息里要能指出"跟谁重复了"
        Assert.Equal("shortcut.new_tab", issues[ShortcutAction.CloseTab].OtherLabelKey);
    }

    [Fact]
    public void 系统保留组合被检出()
    {
        var bindings = ShortcutBindings.Defaults
            .Select(b => b.Action == ShortcutAction.NewTab
                ? b with { Gesture = new ShortcutGesture("F4", Alt: true) }
                : b)
            .ToList();

        Assert.Equal(ShortcutIssue.Reserved, ShortcutBindings.FindIssues(bindings)[ShortcutAction.NewTab].Issue);
    }

    [Theory]
    [InlineData("T", false, false, false)]      // 裸字母：会与打字冲突
    [InlineData("A", false, true, false)]       // Shift+字母 = 输入大写字母
    [InlineData("1", false, false, false)]      // 裸数字
    public void 缺修饰键被检出(string key, bool ctrl, bool shift, bool alt)
        => Assert.Equal(ShortcutIssue.NoModifier,
            ShortcutBindings.ValidateGesture(new ShortcutGesture(key, ctrl, shift, alt)));

    [Theory]
    [InlineData("T", true, false, false)]       // Ctrl+T
    [InlineData("F5", false, false, false)]     // 功能键允许不带修饰键（裸 F5 在文本框里不产生编辑行为）
    [InlineData("F5", false, true, false)]      // Shift+F5
    [InlineData("N", false, false, true)]       // Alt+N
    public void 合法组合通过(string key, bool ctrl, bool shift, bool alt)
        => Assert.Equal(ShortcutIssue.None,
            ShortcutBindings.ValidateGesture(new ShortcutGesture(key, ctrl, shift, alt)));

    [Theory]
    [InlineData("Delete", false, false, false)]  // 裸 Delete：文本框会拿去删字符
    [InlineData("Left", false, false, false)]    // 裸方向键：文本框会拿去移动插入符
    [InlineData("Space", false, false, false)]   // 裸空格：会去激活当前按钮
    [InlineData("Tab", false, false, false)]     // 裸 Tab：框架的焦点导航
    [InlineData("Space", false, true, false)]    // Shift+空格也不行（不是功能键、没有 Ctrl/Alt）
    public void 编辑键与非功能键但没修饰键_都被判成问题(string key, bool ctrl, bool shift, bool alt)
    {
        var issue = ShortcutBindings.ValidateGesture(new ShortcutGesture(key, ctrl, shift, alt));

        Assert.True(
            issue is ShortcutIssue.TextEditing or ShortcutIssue.NoModifier,
            $"{key} 应当是「编辑键」或「缺修饰键」，实际是 {issue}");
    }

    [Fact]
    public void Shift加Delete被判成编辑键_而不是缺修饰键()
    {
        // ⚠️ 顺序判据：它是**允许但警告**的那一类（用户 2026-09-19 的决定），
        //    不该被"缺修饰键"覆盖掉 —— 否则默认值一打开界面就被说成"不合法"。
        Assert.Equal(ShortcutIssue.TextEditing,
            ShortcutBindings.ValidateGesture(new ShortcutGesture("Delete", Shift: true)));
    }

    [Fact]
    public void 粘贴类组合被检出为编辑键()
    {
        foreach (var key in new[] { "C", "V", "X", "A", "Z", "Y" })
        {
            Assert.Equal(ShortcutIssue.TextEditing,
                ShortcutBindings.ValidateGesture(new ShortcutGesture(key, Ctrl: true)));
        }
    }

    [Fact]
    public void 绑定被改成None时算缺修饰键()
        => Assert.Equal(ShortcutIssue.NoModifier,
            ShortcutBindings.ValidateGesture(new ShortcutGesture("None")));

    // ────────────────────────────── 与 config.json 的往返 ──────────────────────────────

    [Fact]
    public void 覆盖表能写进配置并读回来()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        var settings = store.Load();

        settings.ShortcutBindings = ShortcutBindings.ToOverrides(
            ShortcutBindings.Resolve(new Dictionary<string, string> { ["NewTab"] = "Ctrl+Alt+N" }));
        store.Save(settings);

        var reloaded = new SettingsStore(temp.Path).Load();

        Assert.NotNull(reloaded.ShortcutBindings);
        Assert.Equal("Ctrl+Alt+N", reloaded.ShortcutBindings!["NewTab"]);
    }

    [Fact]
    public void 配置里的非法条目在归一化时被清掉()
    {
        using var temp = new TempDataDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "config.json"),
            """{"shortcut_bindings":{"NewTab":"Ctrl+Alt+N","Bogus":"Ctrl+Q","CloseTab":"Ctrl+"}}""",
            new System.Text.UTF8Encoding(false));

        var settings = new SettingsStore(temp.Path).Load();

        Assert.NotNull(settings.ShortcutBindings);
        Assert.Single(settings.ShortcutBindings!);
        Assert.Equal("Ctrl+Alt+N", settings.ShortcutBindings!["NewTab"]);
    }

    [Fact]
    public void 空覆盖表归一成null_不往用户的配置里写空对象()
    {
        using var temp = new TempDataDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "config.json"),
            """{"shortcut_bindings":{}}""",
            new System.Text.UTF8Encoding(false));

        Assert.Null(new SettingsStore(temp.Path).Load().ShortcutBindings);
    }

    [Fact]
    public void 旧版配置没有这个键时_也是null且不影响其它设置()
    {
        using var temp = new TempDataDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "config.json"),
            """{"language":"en","icon_size":"large"}""",
            new System.Text.UTF8Encoding(false));

        var settings = new SettingsStore(temp.Path).Load();

        Assert.Null(settings.ShortcutBindings);
        Assert.Equal("en", settings.Language);
        Assert.Equal("large", settings.IconSize);
    }
}
