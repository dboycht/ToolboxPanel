// IconType.cs —— ToolboxPanel v2 数据层（C# 重写，W1）
//
// 从原版 Python 逐字搬过来：src/toolbox/models/icon_model.py 的 IconType。
//
// ⚠️ **线上表示（wire value）必须与 Python 完全一致**：file / folder / shortcut / url / command。
//    这 5 个字符串是 tabs.json 的持久化契约，改一个字符旧数据就读不出来 / 新数据旧版读不了。

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolboxPanel.Core.Models;

/// <summary>图标类型（对应 Python 的 <c>IconType(StrEnum)</c>）。</summary>
public enum IconType
{
    File,
    Folder,
    Shortcut,
    Url,
    Command,
}

/// <summary><see cref="IconType"/> 与 tabs.json 字符串的互转。</summary>
public static class IconTypes
{
    public const string File = "file";
    public const string Folder = "folder";
    public const string Shortcut = "shortcut";
    public const string Url = "url";
    public const string Command = "command";

    /// <summary>枚举 → 线上字符串（未知枚举值一律写 "file"，与 Python 的兜底一致）。</summary>
    public static string ToWire(IconType type) => type switch
    {
        IconType.Folder => Folder,
        IconType.Shortcut => Shortcut,
        IconType.Url => Url,
        IconType.Command => Command,
        _ => File,
    };

    /// <summary>
    /// 线上字符串 → 枚举。**任何无法识别的值都回落到 File**，
    /// 与 Python 的 <c>try: IconType(...) except ValueError: IconType.FILE</c> 等价
    /// （比 Python 略宽松：这里忽略大小写与首尾空白，Python 是严格区分大小写的）。
    /// </summary>
    public static IconType Parse(string? wire) => wire?.Trim().ToLowerInvariant() switch
    {
        Folder => IconType.Folder,
        Shortcut => IconType.Shortcut,
        Url => IconType.Url,
        Command => IconType.Command,
        _ => IconType.File,
    };
}

/// <summary>让 <see cref="IconType"/> 在 JSON 里就是那 5 个小写字符串，且读到脏值不抛异常。</summary>
public sealed class IconTypeJsonConverter : JsonConverter<IconType>
{
    public override IconType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return IconTypes.Parse(reader.GetString());
        }

        // 容器（对象/数组）当 type 用时必须跳过整个值，否则读流会错位
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            reader.Skip();
        }

        // 数字 / true / false / null：单 token，不消费也算读完；Python 会抛 ValueError 后回落 File，这里等价
        return IconType.File;
    }

    public override void Write(Utf8JsonWriter writer, IconType value, JsonSerializerOptions options)
        => writer.WriteStringValue(IconTypes.ToWire(value));
}
