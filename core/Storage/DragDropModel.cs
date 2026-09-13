// DragDropModel.cs —— W3 拖拽排序：**与 UI 框架无关**的那一半
//
// 为什么把这类"拖放逻辑"放在 core 而不是 winui/：
//   1) WinUI 3 的 XAML 交互**无法自动化验证**（不能注入鼠标，见 ERROR.md E5），
//      但拖放里真正容易算错的是「落在第几个位置」和「落库后顺序对不对」——
//      这两件事都不依赖 WinUI，放在这里就能被 `dotnet test` 覆盖；
//   2) winui/ 那侧只剩下"读指针坐标 → 喂给这里 → 把结果交给 DataStore"的转发，
//      出错面被压到最小。
//
// ⚠️ 持久化语义只有一条：**视图（ObservableCollection）的顺序必须与 Core 列表顺序一致** ——
//    因为下次启动时界面是照 Core 的顺序重建的。所以每次拖放都要走 DataStore 落库，
//    而不是只改界面集合（见 MainViewModel.ReorderIconsInMemory 的注释）。

using ToolboxPanel.Core.Models;

namespace ToolboxPanel.Core.Storage;

/// <summary>拖动的是哪一类东西（与页面的 <c>TabModel.TabType</c> 对应）。</summary>
public enum DragItemKind
{
    /// <summary>网格页的图标。</summary>
    Icon,

    /// <summary>列表页的一行。</summary>
    ListItem,
}

/// <summary>
/// 拖拽载荷：一次拖放开始时确定下来的四件事（**不会中途变**）。
/// 用 <c>"|"</c> 拼成一行字符串跨拖放传输（WinUI 的 <c>DataPackage</c> 里存文本），
/// 刻意不用 JSON：字段只有两个 id 和一个枚举，拼串更好读、也更容易在日志里核对。
/// </summary>
public sealed class DragPayload
{
    private const char Separator = '|';

    public DragPayload(DragItemKind kind, string sourceTabId, string itemId)
    {
        Kind = kind;
        SourceTabId = sourceTabId;
        ItemId = itemId;
    }

    public DragItemKind Kind { get; }

    /// <summary>拖动开始时该项**所在**的标签页（放置目标可能与它不同 = 跨页移动）。</summary>
    public string SourceTabId { get; }

    /// <summary>图标 id 或列表项 id。</summary>
    public string ItemId { get; }

    public override string ToString()
        => $"{(Kind == DragItemKind.Icon ? "icon" : "list")}{Separator}{SourceTabId}{Separator}{ItemId}";

    /// <summary>解析失败（格式不对 / 空串）返回 null —— 调用方一律当作"不接受这次拖放"。</summary>
    public static DragPayload? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(Separator);
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
        {
            return null;
        }

        DragItemKind kind;
        if (parts[0] == "icon")
        {
            kind = DragItemKind.Icon;
        }
        else if (parts[0] == "list")
        {
            kind = DragItemKind.ListItem;
        }
        else
        {
            return null;
        }

        return new DragPayload(kind, parts[1], parts[2]);
    }
}

/// <summary>一次拖放请求：拖的是谁 + 放到哪一页的第几个位置。</summary>
public sealed class DragDropRequest
{
    public DragDropRequest(DragPayload payload, string targetTabId, int targetIndex)
    {
        Payload = payload;
        TargetTabId = targetTabId;
        TargetIndex = targetIndex;
    }

    public DragPayload Payload { get; }

    public string TargetTabId { get; }

    /// <summary>目标页里的插入位置（0..Count，Count 表示放到最后）。</summary>
    public int TargetIndex { get; }
}

/// <summary>拖放落库的结果。<see cref="Success"/> 为 false 时**数据一个字节都没动**。</summary>
public sealed class DragDropResult
{
    private DragDropResult(bool success, string reason, DragDropRequest request)
    {
        Success = success;
        Reason = reason;
        Request = request;
    }

    public bool Success { get; }

    /// <summary>失败原因（中文，可直接进日志/状态栏）。失败时 <see cref="Request"/> 保持原样。</summary>
    public string Reason { get; }

    /// <summary>失败时是**原请求**；成功时是**实际落库后的索引**（夹取 + 跨页移动修正之后）。</summary>
    public DragDropRequest Request { get; }

    public static DragDropResult Fail(string reason, DragDropRequest request) => new(false, reason, request);

    public static DragDropResult Ok(DragDropRequest resolved) => new(true, string.Empty, resolved);
}

/// <summary>
/// 「落点在容器的第几个位置」——纯几何计算，**不碰 WinUI**，所以可以单测。
///
/// <para><b>两条判据（都是被单测抓出来的，别凭直觉简化）</b>：</para>
/// <list type="number">
/// <item>
///   <b>先用"离哪一项中心最近"定位锚点</b>，不能只比 Y 中点：网格页是**换行**布局，
///   只比 Y 会把第二行左侧的落点误判到第一行右侧（第一行三项的 Y 中点"都还没越过"）。
/// </item>
/// <item>
///   <b>再看落点相对锚点中心的方向：X 或 Y 任一方向越过中心 → 插到锚点后面</b>。
///   ⚠️ 这里**不能"有列时只看 X"**（第一版就是那样，被单测抓出来）：图块是 68 宽 × 72 高，
///   指针落在**左半格的下半部分**（X 没过中心、Y 过了中心）时，用户的心理预期是"插到它后面"，
///   只看 X 会算成"插到它前面"，误差一格。
/// </item>
/// </list>
///
/// <para>调用方（WinUI 那侧）只负责把每个已实现 item 的矩形**换算到同一个坐标系**再喂进来，
/// 顺序必须是"视觉顺序"（网格 = 行主序，从 <c>ContainerFromIndex(i)</c> 依 i 递增取即可）。</para>
/// </summary>
public static class DropIndexCalculator
{
    /// <summary>
    /// 计算插入索引（0..Count）。
    /// </summary>
    public static int Compute(IReadOnlyList<ItemBounds> itemBounds, double pointerX, double pointerY)
    {
        if (itemBounds.Count == 0)
        {
            return 0;
        }

        int anchor = FindNearestIndex(itemBounds, pointerX, pointerY);
        var bound = itemBounds[anchor];

        // 越过锚点的水平或垂直中心 → 插到它后面（见类注释第 2 条）
        // ⚠️ 用 ≥ 而不是 >：指针正好落在项的下边界/右边界时（WinUI 给的坐标经常正好压在边界上），
        //    必须算"越过"，否则"拖到列表末尾"会永远差一格（单测抓出来的）。
        bool beyondCenterY = Math.Abs(pointerY - (bound.Y + bound.Height / 2)) >= bound.Height / 2;

        // 单列布局（列表页）里 X 没有意义：此时所有项 X 相同，
        //   若还算水平方向，"指针在左侧空白处"会被误判成"越过水平中心"（单测抓出来的）。
        bool beyondCenterX =
            !IsSingleColumn(itemBounds) &&
            Math.Abs(pointerX - (bound.X + bound.Width / 2)) >= bound.Width / 2;

        bool after = beyondCenterX || beyondCenterY;

        return Math.Clamp(after ? anchor + 1 : anchor, 0, itemBounds.Count);
    }

    /// <summary>所有已实现项的左边界都相同 ⇒ 单列（列表页），此时只按 Y 判定。</summary>
    private static bool IsSingleColumn(IReadOnlyList<ItemBounds> itemBounds)
    {
        for (int i = 1; i < itemBounds.Count; i++)
        {
            if (Math.Abs(itemBounds[i].X - itemBounds[0].X) > 0.5)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 只给 Y 的简化入口（列表页）。保留它是为了调用方读起来更直白：
    /// 单列布局里 X 没有意义，传 0 即可。
    /// </summary>
    public static int Compute(IReadOnlyList<ItemBounds> itemBounds, double pointerY)
        => Compute(itemBounds, pointerX: 0, pointerY);

    /// <summary>离落点最近的一项（距离按中心点算；相等时取靠前的那一项，保证结果稳定）。</summary>
    private static int FindNearestIndex(IReadOnlyList<ItemBounds> itemBounds, double pointerX, double pointerY)
    {
        int nearest = 0;
        double nearestDistance = double.MaxValue;

        for (int i = 0; i < itemBounds.Count; i++)
        {
            var bound = itemBounds[i];
            double dx = pointerX - (bound.X + bound.Width / 2);
            double dy = pointerY - (bound.Y + bound.Height / 2);
            double distance = dx * dx + dy * dy;

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = i;
            }
        }

        return nearest;
    }

    /// <summary>插入位置在容器里的绘制坐标（给"一条插入指示线"定位用）。</summary>
    public static (double X, double Y, double Height) IndicatorAt(
        IReadOnlyList<ItemBounds> itemBounds, int insertIndex)
    {
        if (itemBounds.Count == 0)
        {
            return (0, 0, 0);
        }

        if (insertIndex < itemBounds.Count)
        {
            var target = itemBounds[insertIndex];
            return (target.X, target.Y, target.Height);
        }

        var last = itemBounds[^1];
        return (last.X + last.Width + 2, last.Y, last.Height);
    }
}

/// <summary>一个已实现 item 的矩形（坐标系由调用方统一，WinUI 里是"相对于页面"的 DIP）。</summary>
public readonly record struct ItemBounds(double X, double Y, double Width, double Height);
