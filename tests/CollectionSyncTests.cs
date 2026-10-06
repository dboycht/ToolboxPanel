// CollectionSyncTests.cs —— 「界面集合跟上 Core 集合」的判据（Core 侧）
//
// 基准来自一个真 bug（2026-10-06 用户要的「图标跨页面拖动」）：
//   跨页移动之后 `tabs.json` 是对的，但**源页的界面集合没删掉那一项** ⇒ 图标同时留在两页上，
//   直到重启。根因是旧的 `ReorderObservable` **只 insert/move、从不移除**。
//   所以这里最要紧的一条就是「**目标里没有的项必须被删掉**」。
//
// ⚠️ 这些判据以前没有，是因为同一页内的重排"集合成员不变"⇒ 漏掉删除也看不出来。

using System.Collections.ObjectModel;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class CollectionSyncTests
{
    /// <summary>测试用的最小项：一个 id + 一个名字（key 取 id）。</summary>
    private sealed record Item(string Id, string Name);

    private static Item[] Items(params string[] ids) => ids.Select(id => new Item(id, id)).ToArray();

    private static string[] IdsOf(IEnumerable<Item> items) => items.Select(i => i.Id).ToArray();

    private static string KeyOf(Item item) => item.Id;

    // ────────────────────────────── ★ 回归点：该删的必须删 ──────────────────────────────

    [Fact]
    public void 目标里没有的项必须从界面集合里删掉_跨页移动的回归点()
    {
        // 源页：搬走一个之后 Core 只剩 Calc；界面集合还留着 Notepad（旧 bug 的形状）
        var notepad = new Item("n", "Notepad");
        var calc = new Item("c", "Calc");

        var target = new ObservableCollection<Item> { notepad, calc };

        CollectionSync.Sync(target, new[] { calc }, KeyOf);

        Assert.Equal(new[] { "c" }, IdsOf(target));
        Assert.DoesNotContain(notepad, target);
    }

    [Fact]
    public void 目标为空时把界面集合清空()
    {
        var target = new ObservableCollection<Item>(Items("a", "b", "c"));

        CollectionSync.Sync(target, Array.Empty<Item>(), KeyOf);

        Assert.Empty(target);
    }

    [Fact]
    public void 同id的重复项收敛成一条_留第一个()
    {
        // tabs.json 里出现两条同 id 的脏数据时：界面上只留第一个（其余删掉）
        var first = new Item("x", "第一个");
        var second = new Item("x", "第二个");
        var target = new ObservableCollection<Item> { first, second };

        CollectionSync.Sync(target, new[] { first }, KeyOf);

        Assert.Single(target);
        Assert.Same(first, target[0]);
    }

    // ────────────────────────────── 重排 / 插入：最小改动 ──────────────────────────────

    [Fact]
    public void 只换顺序时给出Move而不是删了重插()
    {
        var a = new Item("a", "A");
        var b = new Item("b", "B");
        var c = new Item("c", "C");
        var current = new[] { a, b, c };

        var ops = CollectionSync.Plan(current, new[] { c, a, b }, KeyOf);

        Assert.DoesNotContain(ops, op => op.Kind == CollectionOpKind.RemoveAt);
        Assert.DoesNotContain(ops, op => op.Kind == CollectionOpKind.Insert);
        Assert.Contains(ops, op => op.Kind == CollectionOpKind.Move);

        // 施加之后就是目标顺序（顺带证明 ops 的下标口径是自洽的）
        var target = new ObservableCollection<Item>(current);
        CollectionSync.Apply(target, ops);
        Assert.Equal(new[] { "c", "a", "b" }, IdsOf(target));
    }

    [Fact]
    public void 缺的项插到正确位置()
    {
        var a = new Item("a", "A");
        var b = new Item("b", "B");
        var c = new Item("c", "C");
        var current = new[] { a, c };

        var target = new ObservableCollection<Item>(current);
        CollectionSync.Sync(target, new[] { a, b, c }, KeyOf);

        Assert.Equal(new[] { "a", "b", "c" }, IdsOf(target));
        Assert.Same(b, target[1]);
    }

    [Fact]
    public void 增删改混在一起也能一步到位()
    {
        var a = new Item("a", "A");
        var b = new Item("b", "B");
        var c = new Item("c", "C");
        var d = new Item("d", "D");
        var newOne = new Item("e", "E");

        var target = new ObservableCollection<Item> { b, a, d };   // 少 c、没有 e、顺序也不对

        CollectionSync.Sync(target, new[] { c, a, b, newOne }, KeyOf);

        Assert.Equal(new[] { "c", "a", "b", "e" }, IdsOf(target));
    }

    // ────────────────────────────── 边界 ──────────────────────────────

    [Fact]
    public void 已经一致时一个操作都不产生()
    {
        var items = Items("a", "b", "c");

        Assert.Empty(CollectionSync.Plan(items, items, KeyOf));
    }

    [Fact]
    public void 两边都空时不崩()
    {
        var target = new ObservableCollection<Item>();

        CollectionSync.Sync(target, Array.Empty<Item>(), KeyOf);

        Assert.Empty(target);
    }

    [Fact]
    public void 目标里同key出现两次时按一次算_不会产生越界Move()
    {
        // 防御式：调用方万一给了重复项（例如过滤态下的可见集合里混着同 id），
        // 也不能生成 `Move(0, 1)` 这种对单元素集合越界的操作（ObservableCollection.Move 会抛）
        var a1 = new Item("a", "A1");
        var a2 = new Item("a", "A2");
        var target = new ObservableCollection<Item> { a1 };

        CollectionSync.Sync(target, new[] { a1, a2 }, KeyOf);

        Assert.Single(target);
        Assert.Same(a1, target[0]);
    }
}
