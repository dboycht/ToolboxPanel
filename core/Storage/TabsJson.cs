// TabsJson.cs —— ToolboxPanel v2 数据层（C# 重写，W1）
//
// tabs.json 的顶层结构与序列化设置。
//
// ⚠️ 输出格式必须与「原 Python 版在 Windows 上写出来的文件」**逐字节一致**，
//    实测基线（data/tabs.json）：**无 BOM、CRLF 换行、无尾换行、2 空格缩进、
//    `": "` 与 `", "` 分隔、中文不转义（不是 \uXXXX）**。
//    这就是为什么这里显式设置 NewLine / IndentSize 并关掉默认的转义器。

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ToolboxPanel.Core.Models;

namespace ToolboxPanel.Core.Storage;

/// <summary>tabs.json 顶层：<c>{"version": 1, "tabs": [...]}</c>。</summary>
public sealed class TabsDocument
{
    /// <summary>当前数据结构版本（原版固定写 1）。</summary>
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("tabs")]
    public List<TabModel> Tabs { get; set; } = new();

    /// <summary>未知的顶层字段原样保留（见 <see cref="IconModel.ExtraFields"/>）。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

/// <summary>tabs.json 的序列化器设置与便捷方法。</summary>
public static class TabsJson
{
    /// <summary>读写共用的设置。</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions() => new()
    {
        // ── 与 Python json.dump(indent=2, ensure_ascii=False) 对齐的写出格式 ──
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\r\n",                                        // Python 文本模式在 Windows 上写 CRLF
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 中文原样写出，不转 \uXXXX
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,      // 空字符串等照写，键一个不少

        // ── 读出保持严格（与 Python json.load 的接受面一致，不做额外宽容）──
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    public static string Serialize(TabsDocument document) => JsonSerializer.Serialize(document, Options);

    public static TabsDocument? Deserialize(string json) => JsonSerializer.Deserialize<TabsDocument>(json, Options);
}
