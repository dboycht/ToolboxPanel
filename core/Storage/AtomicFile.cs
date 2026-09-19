// AtomicFile.cs —— 原子写盘：先写临时文件，再整体改名覆盖
//
// 这个形状不是随便定的，它是**格式兼容契约**的一部分：原版 Python 的
// `data_store.save()` 用的是 `with_suffix(".tmp")` + `Path.replace()`，
// 也就是 `<data>/tabs.tmp` → `<data>/tabs.json`。所以临时文件名必须保持 `tabs.tmp` / `config.tmp`
// （不能图省事换成 `Path.GetTempFileName()`），否则同目录下会多出别的残留文件、
// 也和"两边写出来的目录内容要能对上"的验收口径不一致。
//
// 本文件存在的意义是把两处**逐字重复**的实现（DataStore 与 SettingsStore 各一份）收口成一份，
// 并顺手补上原来两份都没有的两件事：
//   ① **同一目标串行化**（`File.Move` 会把临时文件移走，两次并发保存时后一次会因为
//      "临时文件已不存在"抛 FileNotFoundException —— 见 ERROR.md 里"多开写坏 tabs.json"的背景）；
//   ② **失败不留垃圾**：任何一步抛异常都要把临时文件删掉。
//
// ⚠️ 锁是"按目标全路径"分的（<see cref="Locks"/>），不是全局一把锁：
//   DataStore 与 SettingsStore 各写各的文件，不该互相等。

using System.Collections.Concurrent;
using System.Text;

namespace ToolboxPanel.Core.Storage;

/// <summary>原子写盘工具（进程内串行化 + 失败清理）。</summary>
internal static class AtomicFile
{
    /// <summary>UTF-8 且**不带 BOM**（Python 侧写出的文件就是无 BOM 的）。</summary>
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>按目标文件路径分的锁（同一路径的两次写盘必须排队）。</summary>
    private static readonly ConcurrentDictionary<string, object> Locks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 用 <paramref name="tempPath"/> 当中转，把 <paramref name="content"/> 原子地写到
    /// <paramref name="targetPath"/>：先写临时文件 → 再改名覆盖。
    /// 失败时保证**目标文件保持原样**、且临时文件不残留。
    /// </summary>
    public static void WriteAllText(string targetPath, string tempPath, string content)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetPath);
        ArgumentException.ThrowIfNullOrEmpty(tempPath);

        var gate = Locks.GetOrAdd(Path.GetFullPath(targetPath), static _ => new object());

        lock (gate)
        {
            try
            {
                File.WriteAllText(tempPath, content, Utf8NoBom);
                File.Move(tempPath, targetPath, overwrite: true);
            }
            catch
            {
                // 失败不留垃圾：临时文件在就删掉（删不掉也无所谓，绝不能因此盖掉真正的异常）
                TryDelete(tempPath);
                throw;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删不掉不影响"目标文件没被写坏"这个结论
        }
    }
}
