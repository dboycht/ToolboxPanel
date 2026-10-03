// UiSourceGuardTests.cs —— 源码级守门员：**容器不是数据项**（`ERROR.md` E47 / E52）
//
// 背景（2026-10-03 用户实测："刷新图标怎么没有啊"）：
//   四个右键菜单（图标 / 列表行 / 文本行 / 标签）各自写了一段**同形**的"往上找数据项"：
//       if (current is GridViewItem { DataContext: IconTileViewModel tile }) return tile;
//   它**永远不成立** —— ① 模板元素（真正带 `DataContext` 的那个）不是容器类型，这个模式压根不看它；
//   ② 容器的 `DataContext` 是 **null**（E47 实测）。后果是"右键永远判成点在空白处"：
//   图标菜单里的「刷新图标」看不到、标签菜单里的「重命名 / 删除」看不到。
//
// 为什么要有这个守门员：这段错**在同一批代码里出现了 4 次**（四个页面各一份）。
// 靠"下次注意"是拦不住的（本项目 `AGENTS.md` 规则 14 的教训：**发现"承诺 > 机制"时改机制**），
// 所以这里把它变成一条**扫源码**的机械判据：`is (GridViewItem|ListViewItem) { … DataContext … }` 出现即红。
//
// ⚠️ 本测试**只读源码**，不碰任何用户数据；找不到 `winui/` 源码时由 `[UiSourceFact]` **显式跳过**。

using System.Text.RegularExpressions;

namespace ToolboxPanel.Core.Tests;

public class UiSourceGuardTests
{
    /// <summary>被禁的写法：在**容器**类型上绑 `DataContext` 取数据项。</summary>
    private static readonly Regex ForbiddenContainerDataContext = new(
        @"\bis\s+(?:GridViewItem|ListViewItem)\s*\{[^}]*\bDataContext\b",
        RegexOptions.Compiled);

    // ────────────────────────────── 守门员自身也要被验（免得它"扫了个空"还全绿）──────────────────────────────

    [Fact]
    public void 剥注释_注释里的违禁写法被丢掉_代码里的保留()
    {
        const string sample = """
            var a = 1;   // 以前写的是 is GridViewItem { DataContext: object x }
            /* is ListViewItem { DataContext: object y } */
            if (current is GridViewItem { DataContext: object z }) { }
            """;

        var stripped = UiSourceScan.StripCommentsAndStrings(sample);

        Assert.DoesNotContain("object x", stripped);
        Assert.DoesNotContain("object y", stripped);
        Assert.Contains("object z", stripped);

        // 代码里那条必须被扫出来（否则守门员是假绿）
        Assert.Matches(ForbiddenContainerDataContext, stripped);
    }

    [Fact]
    public void 剥注释_字符串字面量里的内容不算代码()
    {
        // 反面对照：违禁写法**出现在字符串里**（例如日志文案）不该判红；
        // 真正的代码写法必须仍被判红 —— 证明"剥字符串"没有把整行吃掉。
        const string sample = """
            var message = "is GridViewItem { DataContext: bogus }";
            if (item is ListViewItem { DataContext: RowViewModel row }) { }
            """;

        var stripped = UiSourceScan.StripCommentsAndStrings(sample);

        Assert.DoesNotContain("bogus", stripped);
        Assert.Contains("RowViewModel", stripped);
        Assert.Matches(ForbiddenContainerDataContext, stripped);
    }

    // ────────────────────────────── ★ 真判据：winui 源码里不许再出现 ──────────────────────────────

    [UiSourceFact]
    public void winui源码里不许读容器的DataContext来取数据项()
    {
        // ① 先证明"扫描面有效"：文件数够多、且确实读到了 DataContext 这种字样
        //    （否则"没扫到违禁写法"可能只是因为它扫了个空目录 —— 那是假绿）
        Assert.True(UiSourceScan.Files.Count >= 10,
            $"winui 源码只扫到 {UiSourceScan.Files.Count} 个 .cs —— 扫描面不对，守门员不可信");

        var codes = UiSourceScan.Files.ToDictionary(path => path, UiSourceScan.ReadCode);
        int dataContextMentions = codes.Values.Sum(code => Regex.Matches(code, @"\bDataContext\b").Count);
        Assert.True(dataContextMentions >= 5,
            $"扫到的源码里只有 {dataContextMentions} 处 DataContext —— 剥注释/剥字符串可能把代码也吃掉了");

        // ② 反面对照：正确写法（模板元素上的 DataContext）必须**不被**这条正则命中
        //    —— 证明正则不是在"什么都能匹配"，而是精确地盯着容器那两种类型。
        Assert.DoesNotMatch(ForbiddenContainerDataContext,
            "if (current is FrameworkElement { DataContext: TView v }) return v;");

        // ③ 真判据
        var offenders = new List<string>();

        foreach (var (path, code) in codes)
        {
            foreach (Match match in ForbiddenContainerDataContext.Matches(code))
            {
                int line = code.Take(match.Index).Count(ch => ch == '\n') + 1;
                offenders.Add($"{UiSourceScan.Relative(path)}:{line}  {match.Value.Replace('\n', ' ')}");
            }
        }

        Assert.True(offenders.Count == 0,
            "winui 源码里出现了「在容器（GridViewItem / ListViewItem）上绑 DataContext 取数据项」的写法 —— "
            + "它**永远不成立**（容器的 DataContext 是 null，数据项在模板元素上，见 ERROR.md E47/E52）。\n"
            + "请改用 Views/ContextHitTest.Resolve（它按 E47 的两条路走：模板元素读 DataContext、"
            + "容器用 IndexFromContainer 成对换算）：\n  "
            + string.Join("\n  ", offenders));
    }

    [UiSourceFact]
    public void 四个右键菜单都走统一命中判据()
    {
        // 命中判据只有一份实现 ⇒ 四个入口都应当调用它（而不是各自再写一段往上找的循环）。
        var callers = UiSourceScan.Files
            .Where(path => UiSourceScan.ReadCode(path).Contains("ContextHitTest.Resolve"))
            .Select(UiSourceScan.Relative)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Contains("Views\\GridPage.xaml.cs", callers);
        Assert.Contains("Views\\ListViewPage.xaml.cs", callers);
        Assert.Contains("Views\\TextPage.xaml.cs", callers);
        Assert.Contains("Views\\TabStripView.xaml.cs", callers);
    }
}
