// CacheFileName.cs —— 图标缓存文件名的约束（纯逻辑，可单测）
//
// 「缓存文件名」这个契约原本只写在注释里（`IconModel.IconCacheFile`：data/icons/ 下的文件名，
// **不是**完整路径），代码里没有任何一处真的校验过它。而它来自**磁盘上的 JSON 字符串** ——
// 来源有三条，每条都不要求攻击者拿到本机写权限之外的东西：
//   ① 用户手工编辑过 tabs.json；
//   ② 导入一个别人给的备份 ZIP（里面的 data/tabs.json 会被原样落盘）；
//   ③ 程序自己的历史版本写坏过。
//
// 危险点很具体：`Path.Combine(IconsDirectory, "..\\config.json")` 在 Windows 上**不会报错**，
// 它会老老实实拼出 `<data>\icons\..\config.json` = 数据目录里的设置文件，
// 于是一个"清理图标缓存"的动作就能删到 icons/ 之外（`..\..\..\Windows\...` 更是完全离开数据目录）。
//
// 所以：删文件/查孤儿缓存之前，一律先过 <see cref="IsPlain"/>。

namespace ToolboxPanel.Core.Storage;

/// <summary>缓存文件名的合法性判据与安全拼接。</summary>
internal static class CacheFileName
{
    /// <summary>
    /// 是不是一个**裸文件名**（可以安全地拼进 icons/ 目录）。
    ///
    /// <para>拒绝：空值；含目录分隔符（<c>\</c> / <c>/</c>）或卷分隔符（<c>:</c>）；
    /// <c>.</c> 与 <c>..</c> 这两个特殊名。</para>
    /// </summary>
    public static bool IsPlain(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return name.IndexOfAny(InvalidChars) < 0
               && name != "."
               && name != "..";
    }

    /// <summary>
    /// 把裸文件名拼进 <paramref name="directory"/>；名字不合法时返回 null。
    ///
    /// <para>拼完再做一次 <see cref="Path.GetFullPath(string)"/> 复核（双保险）：
    /// 万一将来 <see cref="IsPlain"/> 漏了某个新花样，这一步仍能挡住越界。</para>
    /// </summary>
    public static string? Combine(string directory, string? name)
    {
        if (!IsPlain(name))
        {
            return null;
        }

        var fullDirectory = Path.GetFullPath(directory);
        var combined = Path.GetFullPath(Path.Combine(fullDirectory, name!));

        var prefix = fullDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? fullDirectory
            : fullDirectory + Path.DirectorySeparatorChar;

        return combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? combined : null;
    }

    private static readonly char[] InvalidChars = { '\\', '/', ':' };
}
