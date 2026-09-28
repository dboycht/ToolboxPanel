// TabModel.cs —— ToolboxPanel v2 数据层（C# 重写，W1）
//
// 逐字段对应 Python 的 src/toolbox/models/tab_model.py::TabModel。

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolboxPanel.Core.Models;

/// <summary>
/// 一个标签页。
///
/// <para><see cref="TabType"/> 为 <c>"grid"</c>（默认，图标网格）、<c>"list"</c>（两列列表）
/// 或 <c>"text"</c>（文本页：左列备注 + 右列文本，点击即复制）。
/// 旧 JSON 没有 tab_type 字段 → 按 grid 加载（向后兼容）。</para>
///
/// <para>⚠️ <c>"text"</c> 是 **WinUI 线新增的第三种页**（原版 Python 只有 grid / list）：
/// 旧版读到不认识的类型一律按网格页处理（<see cref="IsListTab"/> / <see cref="IsTextTab"/>
/// 都只认自己的字符串），所以"新版本写的 tabs.json 拿给旧版读"只会看到**一个空网格页**，
/// 而**不会**崩、也不会丢数据（TextItems 仍原样躺在 JSON 里，旧版 Save 时也带得回去 ——
/// 不过旧版只认识 grid/list，真让它回写就会把这个字段丢掉，属已知取舍）。</para>
/// </summary>
public sealed class TabModel
{
    /// <summary>
    /// 默认页名（新建标签页用，原版 i18n <c>tab.default_name</c>）——
    /// **跟着当前语言走**（原版的默认页名也是按语言取的），
    /// 注意 <c>DataStore.Load()</c> 的兜底默认页名仍是英文 "Home"（与原版一致）。
    /// </summary>
    public static string DefaultName => I18n.T("tab.default_name");

    public const string TypeGrid = "grid";
    public const string TypeList = "list";

    /// <summary>文本页（WinUI 线新增；见类注释里"第三种页"的说明）。</summary>
    public const string TypeText = "text";

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = DefaultName;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("tab_type")]
    public string TabType { get; set; } = TypeGrid;

    [JsonPropertyName("icons")]
    public List<IconModel> Icons { get; set; } = new();

    [JsonPropertyName("list_items")]
    public List<ListItemModel> ListItems { get; set; } = new();

    /// <summary>
    /// 文本页的条目（JSON 键 <c>text_items</c>）。其他类型的页恒为空。
    ///
    /// <para>⚠️ <c>WhenWritingDefault</c> + **可空且默认 null** 是**必须的**，不是顺手加的：
    /// `text_items` 是原版 Python 完全没有的字段，而本项目有一条**逐字节一致性**要求
    /// （`TabsJson` 的注释：输出必须与原版 Python 写出的文件逐字节相同）。空集合被写出来会变成
    /// <c>"text_items": []</c> —— 网格页 / 列表页的 tabs.json 就与原版不一致了
    /// （三条跨实现测试立刻变红，2026-09-28 实测：期望以 `"list_items": []` 结尾，实际后面还跟了一行）。</para>
    ///
    /// <para>⚠️ 还要注意：JSON 的"默认值"对**引用类型是 <c>null</c>**，不是"空集合" ——
    /// 所以光加 <c>WhenWritingDefault</c> 而让属性默认 <c>new()</c> 是**没用的**（空 List 不等于 null）。
    /// 这里的做法是"**没人碰过它就是 null**"：读盘 / 新建页时为 null，
    /// 只有真的加过一条文本项才会变成非 null ⇒ 序列化器自己就把它省掉了。</para>
    ///
    /// <para>写入的时机（都靠 <c>??=</c> 自愈）：<see cref="Storage.DataStore.AddTextItem"/>
    /// （唯一会把它变成非 null 的生产路径；<c>DataStore.Load</c> **刻意不补空集合**，理由见那里的注释）。</para>
    /// </summary>
    [JsonPropertyName("text_items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<TextItemModel>? TextItems { get; set; }

    /// <summary>未知字段原样保留（见 <see cref="IconModel.ExtraFields"/> 的说明）。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }

    /// <summary>该页是否为列表式（大小写不敏感；未知值一律按网格处理，与 Python 的字符串比较一致但更宽容）。</summary>
    [JsonIgnore]
    public bool IsListTab => string.Equals(TabType, TypeList, StringComparison.OrdinalIgnoreCase);

    /// <summary>该页是否为文本页（判据同 <see cref="IsListTab"/>；未知值一律按网格处理）。</summary>
    [JsonIgnore]
    public bool IsTextTab => string.Equals(TabType, TypeText, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 界面**该用的**那一份文本项集合：<see cref="TextItems"/> 为 null（这一页没人碰过）时给一个空集合。
    ///
    /// <para>⚠️ 界面请一律走这个属性，别直接读 <see cref="TextItems"/> —— 那个字段为了"写回时不多出
    /// <c>"text_items": []</c>"刻意是"可空 + 默认 null"的（见它的长注释），直接解引用是一颗会炸的雷。
    /// 这里是**只读**视图：要加/删/改请走 <c>DataStore.AddTextItem</c> 等（它们自己会 <c>??=</c> 建集合）。</para>
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<TextItemModel> EffectiveTextItems
        => TextItems ?? (IReadOnlyList<TextItemModel>)Array.Empty<TextItemModel>();
}
