// IconModel.cs —— ToolboxPanel v2 数据层（C# 重写，W1）
//
// 逐字段对应 Python 的 src/toolbox/models/icon_model.py::IconModel。
// ⚠️ 字段名（JSON key）、顺序、默认值都**不能改**：tabs.json 是 5 类图标共用一套字段，
//    不同 type 用不同字段组合（见原 Python 文件注释）。

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolboxPanel.Core.Models;

/// <summary>标签页里的一个图标。5 种 type 共用以下字段。</summary>
public sealed class IconModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("type")]
    [JsonConverter(typeof(IconTypeJsonConverter))]
    public IconType Type { get; set; } = IconType.File;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>文件路径 / .lnk 路径 / URL / 命令行（含义随 <see cref="Type"/> 变化）。</summary>
    [JsonPropertyName("source_path")]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>快捷方式的真实目标路径（或可执行文件路径）。</summary>
    [JsonPropertyName("target_path")]
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>命令行参数。</summary>
    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;

    /// <summary>工作目录。</summary>
    [JsonPropertyName("working_dir")]
    public string WorkingDir { get; set; } = string.Empty;

    /// <summary>快捷方式描述（仅 SHORTCUT 使用）。</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>data/icons/ 下的缓存文件名（不是完整路径）。</summary>
    [JsonPropertyName("icon_cache_file")]
    public string IconCacheFile { get; set; } = string.Empty;

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }

    /// <summary>
    /// 未知字段（未来版本新增的键）原样保留并在写回时输出。
    /// 原 Python 版用的是「重建 dict」写法，未知键会被**丢弃**；这里刻意做得更安全，
    /// 代价为零（Python 读得懂多余键），收益是**降级使用旧版不会丢数据**。
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}
