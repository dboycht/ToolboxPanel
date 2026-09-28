// TextItemModel.cs —— 文本页（粘贴板式表格页）里的一行
//
// 与 `ListItemModel` 同一套写法与同一套持久化纪律：
//   · JSON 字段名（note / text / sort_order）一旦发布就**不能改**；
//   · 未知字段原样保留（降级用旧版不会丢数据）；
//   · **不做**与 Python 原版的字段保真 —— 原版没有这种页（见 TabModel.TypeText 的说明）。
//
// 一行 = 左列「备注」+ 右列「文本」；点这一行就把 Text 复制进剪贴板。

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolboxPanel.Core.Models;

/// <summary>文本页里的一行：第 1 列备注（给人看），第 2 列文本（点击复制）。</summary>
public sealed class TextItemModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>备注（左列）—— 这行文本是干什么用的。</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    /// <summary>文本内容（右列）—— 点击这一行时复制进剪贴板的就是它。</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }

    /// <summary>未知字段原样保留（见 <see cref="IconModel.ExtraFields"/> 的说明）。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}
