// CollectionSync.cs —— 「让界面集合跟上 Core 集合」的**纯逻辑**（可单测）
//
// 为什么要有它（2026-10-06，用户要的「图标跨页面拖动」直接踩中）：
//   `TabItemViewModel` 里那份"照 Core 重排界面集合"的代码（旧名 `ReorderObservable`）**只 insert / move，
//   从不移除**已经不在 Core 里的项。于是**跨页移动**（图标从 A 页搬到 B 页）之后：
//     · `tabs.json` 正确（源页少一个、目标页多一个）；
//     · 但**源页的界面集合仍然留着那一个图块** ⇒ 图标同时出现在两个页面上，直到重启。
//   同一页内的重排看不出问题（集合成员没变），所以这个错**藏了很久**（跨页移动一直没被手验过）。
//
// 所以把"该删谁 / 该插谁 / 该挪到哪"这三件判断从 UI 里搬进 Core：
//   · `Plan`  —— 纯函数：比较"当前集合"与"想要的集合"，产出一串**最小改动**操作；
//   · `Apply` —— 按顺序施加（`ObservableCollection` 走 `Move`，免得 Remove+Insert 让
//                GridView/ListView 丢掉容器、把入场动画与滚动位置一起重置）；
//   · `Sync`  —— 上面两步的合并入口（UI 只需要调它）。
//   判据有单测钉住（见 `tests/CollectionSyncTests.cs`）。
//
// ⚠️ 匹配口径 = **调用方给的 key**（本项目一律用 `Model.Id`）：
//   · `current` 里同 key 的重复项**会被删掉**（留第一个）—— 顺带把"tabs.json 里两条同 id"
//     那种脏数据的显示收敛掉（以前它会一直挂在界面上）；
//   · `desired` 里的重复项按同口径收敛（第二条起忽略），所以调用方不用自己去重。

using System.Collections.ObjectModel;

namespace ToolboxPanel.Core.Services;

/// <summary>一次集合改动的最小操作。</summary>
public enum CollectionOpKind
{
    /// <summary>删掉当前下标处的项。</summary>
    RemoveAt,

    /// <summary>把 <see cref="CollectionOp{T}.Item"/> 插到当前下标处。</summary>
    Insert,

    /// <summary>把当前下标处的项挪到 <see cref="CollectionOp{T}.Target"/>。</summary>
    Move,
}

/// <summary>
/// 一次集合改动。<see cref="Index"/> / <see cref="Target"/> 都是**施加那一刻**的下标，
/// 所以操作必须**按顺序**施加（<see cref="CollectionSync.Apply"/>）。
/// </summary>
public readonly record struct CollectionOp<T>(CollectionOpKind Kind, int Index, int Target = -1, T? Item = default)
{
    public static CollectionOp<T> RemoveAt(int index) => new(CollectionOpKind.RemoveAt, index);

    public static CollectionOp<T> Insert(int index, T item) => new(CollectionOpKind.Insert, index, -1, item);

    public static CollectionOp<T> Move(int from, int to) => new(CollectionOpKind.Move, from, to);
}

/// <summary>把"界面集合"调成"Core 集合"的样子：先删多余的（含同 key 的重复项），再逐个归位。</summary>
public static class CollectionSync
{
    /// <summary>
    /// 产出"把 <paramref name="current"/> 变成 <paramref name="desired"/>"的最小操作序列。
    ///
    /// <para>顺序固定为：**先删 → 再逐个归位**（先删是为了让后面的下标一眼可算；
    /// 删的时候"删一个、下标不动"，因为原来的下一项会补上来）。</para>
    /// </summary>
    /// <param name="current">界面当前持有的集合（顺序 = 现在显示的顺序）。</param>
    /// <param name="desired">Core 给出的目标顺序（同一批对象的引用；要新增的项由调用方补好）。</param>
    /// <param name="keyOf">取 key 的函数（本项目用 <c>Model.Id</c>）。</param>
    public static IReadOnlyList<CollectionOp<T>> Plan<T>(
        IList<T> current, IReadOnlyList<T> desired, Func<T, string> keyOf)
    {
        var ops = new List<CollectionOp<T>>();

        // 目标集合按 key 去重后的顺序（同 key 只认第一个）—— 调用方不必自己去重
        var wanted = new List<T>(desired.Count);
        var wantedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in desired)
        {
            if (wantedKeys.Add(keyOf(item)))
            {
                wanted.Add(item);
            }
        }

        // 模拟一份"正在被改动"的集合，这样每条 op 的下标都是施加那一刻的真实下标
        var work = new List<T>(current);

        // ① 从前往后扫：**key 不在目标里** ⇒ 删；**同 key 已经留过一个** ⇒ 也删
        var kept = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < work.Count;)
        {
            var key = keyOf(work[i]);
            if (!wantedKeys.Contains(key) || !kept.Add(key))
            {
                ops.Add(CollectionOp<T>.RemoveAt(i));
                work.RemoveAt(i);
                continue;
            }

            i++;
        }

        // ② 逐个归位（此时 work 与目标**一一对应**，只剩顺序问题）
        for (int i = 0; i < wanted.Count; i++)
        {
            int at = IndexOfKey(work, keyOf, keyOf(wanted[i]));

            if (at < 0)
            {
                ops.Add(CollectionOp<T>.Insert(i, wanted[i]));
                work.Insert(i, wanted[i]);
            }
            else if (at != i)
            {
                ops.Add(CollectionOp<T>.Move(at, i));
                var item = work[at];
                work.RemoveAt(at);
                work.Insert(i, item);
            }
        }

        return ops;
    }

    /// <summary>
    /// 按顺序施加操作。<paramref name="target"/> 是界面集合
    /// （`ObservableCollection` 时走它的 <c>Move</c>，保住容器与滚动位置）。
    /// </summary>
    public static void Apply<T>(IList<T> target, IReadOnlyList<CollectionOp<T>> ops)
    {
        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case CollectionOpKind.RemoveAt:
                    target.RemoveAt(op.Index);
                    break;

                case CollectionOpKind.Insert:
                    target.Insert(op.Index, op.Item!);
                    break;

                case CollectionOpKind.Move:
                    if (target is ObservableCollection<T> observable)
                    {
                        // ⚠️ 一定要走 ObservableCollection.Move：Remove+Insert 会让 GridView/ListView
                        //    丢掉容器、把入场动画与滚动位置一起重置（观感上是"整页闪一下"）。
                        observable.Move(op.Index, op.Target);
                    }
                    else
                    {
                        var moved = target[op.Index];
                        target.RemoveAt(op.Index);
                        target.Insert(op.Target, moved);
                    }

                    break;
            }
        }
    }

    /// <summary>一步到位：把 <paramref name="target"/> 调成 <paramref name="desired"/> 的样子。</summary>
    public static void Sync<T>(IList<T> target, IReadOnlyList<T> desired, Func<T, string> keyOf)
        => Apply(target, Plan(target, desired, keyOf));

    private static int IndexOfKey<T>(List<T> items, Func<T, string> keyOf, string key)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (string.Equals(keyOf(items[i]), key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
