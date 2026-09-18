// TabModel.cs —— ToolboxPanel v2 数据层（C# 重写，W1）
//
// 逐字段对应 Python 的 src/toolbox/models/tab_model.py::TabModel。

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolboxPanel.Core.Models;

/// <summary>
/// 一个标签页。
///
/// <para><see cref="TabType"/> 为 <c>"grid"</c>（默认，图标网格）或 <c>"list"</c>（两列列表）。
/// 旧 JSON 没有 tab_type 字段 → 按 grid 加载（向后兼容）。</para>
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

    /// <summary>未知字段原样保留（见 <see cref="IconModel.ExtraFields"/> 的说明）。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }

    /// <summary>该页是否为列表式（大小写不敏感；未知值一律按网格处理，与 Python 的字符串比较一致但更宽容）。</summary>
    [JsonIgnore]
    public bool IsListTab => string.Equals(TabType, TypeList, StringComparison.OrdinalIgnoreCase);
}
