// ShortcutCatalogTests.cs —— 批次 2：快捷键目录的**一致性**（纯数据）
//
// 这一族测试的价值不在"跑通"，而在**钉住不变量**：
//   ① 每个功能名 key 都在文案表里（否则参考窗口会显示 ??key??）；
//   ② 动作不重复、**键位不重复**（两条功能绑同一个组合 = 用户按下去只有一条生效，另一条形同虚设）；
//   ③ 界面是**照目录注册**加速器的（不是手写 XAML），所以"目录里有的按键一定接得上"。
//      ⚠️ 原版那张参考表曾经漏掉「新建列表标签页」的键位（菜单里有 `Ctrl+Shift+T`，表里没列）——
//      ①②两条测试就是防这类漏项的。

using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class ShortcutCatalogTests
{
    [Fact]
    public void 目录非空且第一个是新建标签页()
    {
        Assert.NotEmpty(ShortcutCatalog.All);
        Assert.Equal(ShortcutAction.NewTab, ShortcutCatalog.All[0].Action);
    }

    [Fact]
    public void 每个功能名的文案key都在文案表里()
    {
        var missing = ShortcutCatalog.All
            .Where(entry => !I18n.Has(entry.LabelKey))
            .Select(entry => entry.LabelKey)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void 动作不重复()
    {
        var duplicated = ShortcutCatalog.All
            .GroupBy(entry => entry.Action)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.ToString())
            .ToList();

        Assert.Empty(duplicated);
    }

    [Fact]
    public void 键位不重复_否则按下去只有一条生效()
    {
        // ⚠️ 只比对本应用自己注册的那些：Alt+F4 是系统级的，不参与我们的注册
        var duplicated = ShortcutCatalog.AppShortcuts
            .GroupBy(entry => entry.Gesture.Display, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} -> {string.Join(", ", group.Select(e => e.Action))}")
            .ToList();

        Assert.Empty(duplicated);
    }

    [Fact]
    public void 键位描述可读且非空()
    {
        Assert.All(ShortcutCatalog.All, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Gesture.Key));
            Assert.False(string.IsNullOrWhiteSpace(entry.Gesture.Display));
        });
    }

    [Fact]
    public void 退出程序是系统级的_不由我们注册()
    {
        var exit = ShortcutCatalog.Find(ShortcutAction.ExitApp);

        Assert.NotNull(exit);
        Assert.Equal(ShortcutKind.System, exit!.Kind);
        Assert.DoesNotContain(ShortcutCatalog.AppShortcuts, e => e.Action == ShortcutAction.ExitApp);
    }

    [Fact]
    public void 新增的五类图标键位与原版菜单一致()
    {
        // 原版 app_window.py 的菜单栏键位，逐条对照（别"顺手"改成别的组合）
        Assert.Equal("Ctrl+Shift+F", ShortcutCatalog.Find(ShortcutAction.NewFile)!.Gesture.Display);
        Assert.Equal("Ctrl+Shift+O", ShortcutCatalog.Find(ShortcutAction.NewFolder)!.Gesture.Display);
        Assert.Equal("Ctrl+Shift+L", ShortcutCatalog.Find(ShortcutAction.NewShortcut)!.Gesture.Display);
        Assert.Equal("Ctrl+Shift+U", ShortcutCatalog.Find(ShortcutAction.NewUrl)!.Gesture.Display);
        Assert.Equal("Ctrl+Shift+P", ShortcutCatalog.Find(ShortcutAction.NewCommand)!.Gesture.Display);
    }

    [Fact]
    public void 数据类键位与原版一致()
    {
        Assert.Equal("Ctrl+Shift+R", ShortcutCatalog.Find(ShortcutAction.ResetData)!.Gesture.Display);
        Assert.Equal("Ctrl+Shift+E", ShortcutCatalog.Find(ShortcutAction.Export)!.Gesture.Display);
        Assert.Equal("Ctrl+Shift+I", ShortcutCatalog.Find(ShortcutAction.Import)!.Gesture.Display);
    }

    [Theory]
    [InlineData("T", true, false, false, "Ctrl+T")]
    [InlineData("T", true, true, false, "Ctrl+Shift+T")]
    [InlineData("Delete", false, true, false, "Shift+Delete")]
    [InlineData("Tab", true, false, false, "Ctrl+Tab")]
    [InlineData("F4", false, false, true, "Alt+F4")]
    public void 键位描述格式(string key, bool ctrl, bool shift, bool alt, string expected)
        => Assert.Equal(expected, new ShortcutGesture(key, ctrl, shift, alt).Display);

    [Theory]
    [InlineData("Ctrl+Shift+T", "T", true, true, false)]
    [InlineData("Ctrl+F", "F", true, false, false)]
    [InlineData("Shift+Delete", "Delete", false, true, false)]
    [InlineData("Alt+F4", "F4", false, false, true)]
    public void 键位串可以解析回来(string text, string key, bool ctrl, bool shift, bool alt)
    {
        var gesture = ShortcutGesture.Parse(text);

        Assert.NotNull(gesture);
        Assert.Equal(key, gesture!.Key);
        Assert.Equal(ctrl, gesture.Ctrl);
        Assert.Equal(shift, gesture.Shift);
        Assert.Equal(alt, gesture.Alt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Shift")]   // 只有修饰键、没有主键 ⇒ 不算一个组合
    public void 非法键位串解析成null(string? text)
    {
        Assert.Null(ShortcutGesture.Parse(text));
    }

    [Fact]
    public void 解析与描述可以往返()
    {
        Assert.All(ShortcutCatalog.All, entry =>
            Assert.Equal(entry.Gesture.Display, ShortcutGesture.Parse(entry.Gesture.Display)!.Display));
    }

    [Fact]
    public void 功能名跟随语言()
    {
        var original = I18n.Current;
        try
        {
            I18n.SetLanguage("zh");
            var zh = ShortcutCatalog.Label(ShortcutCatalog.All[0]);
            I18n.SetLanguage("en");
            var en = ShortcutCatalog.Label(ShortcutCatalog.All[0]);

            Assert.NotEqual(zh, en);
            Assert.Equal(I18n.T("shortcut.new_tab"), en);
        }
        finally
        {
            I18n.SetLanguage(original);
        }
    }

    [Fact]
    public void 参考窗口的标题与表头都来自文案表()
    {
        Assert.Equal(I18n.T("shortcut.dialog.title"), ShortcutCatalog.DialogTitle);
        Assert.Equal(I18n.T("shortcut.col.action"), ShortcutCatalog.ColumnAction);
        Assert.Equal(I18n.T("shortcut.col.key"), ShortcutCatalog.ColumnKey);
    }

    [Fact]
    public void 标签页那六条都覆盖到了()
    {
        // 批次 1 做的标签页操作，每一条都该有快捷键（这是本批次的重点）
        var actions = ShortcutCatalog.AppShortcuts.Select(e => e.Action).ToHashSet();

        Assert.Contains(ShortcutAction.NewTab, actions);
        Assert.Contains(ShortcutAction.NewListTab, actions);
        Assert.Contains(ShortcutAction.RenameTab, actions);
        Assert.Contains(ShortcutAction.CloseTab, actions);
        Assert.Contains(ShortcutAction.PrevTab, actions);
        Assert.Contains(ShortcutAction.NextTab, actions);
    }
}
