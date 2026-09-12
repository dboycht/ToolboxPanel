// ListItemModel.cs —— ToolboxPanel v2 数据层（C# 重写，W1）
//
// 逐字段对应 Python 的 src/toolbox/models/list_item_model.py::ListItemModel。

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolboxPanel.Core.Models;

/// <summary>列表式标签页里的一行：第 1 列说明（可编辑），第 2 列路径（点击打开）。</summary>
public sealed class ListItemModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }

    /// <summary>未知字段原样保留（见 <see cref="IconModel.ExtraFields"/> 的说明）。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}
