// TempDataDirectory.cs —— 单测辅助：每个测试一个独立临时数据目录

using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

/// <summary>用完即删的临时数据目录（避免测试之间互相污染）。</summary>
public sealed class TempDataDirectory : IDisposable
{
    /// <summary>
    /// 测试自己造的、**不在本目录里**的文件（例如备份 ZIP 必须落在数据目录之外）。
    /// 由 <see cref="NewSiblingPath"/> 登记，<see cref="Dispose"/> 统一删掉。
    /// </summary>
    private readonly List<string> _registeredFiles = new();

    public TempDataDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "toolboxpanel-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string TabsFile => System.IO.Path.Combine(Path, "tabs.json");

    /// <summary>原子写的临时文件（原版命名是 tabs.tmp）。</summary>
    public string TabsTempFile => System.IO.Path.Combine(Path, "tabs.tmp");

    /// <summary>损坏文件的备份（原版命名是 tabs.json.bak）。</summary>
    public string TabsBackupFile => System.IO.Path.Combine(Path, "tabs.json.bak");

    public string IconsDirectory => System.IO.Path.Combine(Path, "icons");

    public DataStore NewStore() => new(Path);

    /// <summary>
    /// 造一个**与本目录同级**的新路径（备份 ZIP 就属于这一类：它不能放进数据目录里，
    /// 否则"导出会连 zip 自己一起打包"这类判定就失真了）。
    ///
    /// <para>⚠️ 登记在册、由 <see cref="Dispose"/> 统一清理 —— 这是为了避免
    /// `%TEMP%\toolboxpanel-tests\` 里越攒越多的残留 zip（历史上真有 9 个留着没删）。</para>
    /// </summary>
    public string NewSiblingPath(string extension = ".zip")
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Path)!,
            $"{Guid.NewGuid():N}{extension}");
        _registeredFiles.Add(path);
        return path;
    }

    /// <summary>
    /// 造一个**与本目录同级**的具名路径（用于"某个越界文件不该被写出来"这类断言：
    /// 名字必须固定，才能去查它到底有没有出现）。
    /// </summary>
    public string NamedSiblingPath(string fileName)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, fileName);
        _registeredFiles.Add(path);
        return path;
    }

    /// <summary>直接落一份原始文本（含故意写坏的 JSON），模拟"用户手上的旧文件"。</summary>
    public void WriteRawTabsJson(string text)
        => File.WriteAllText(TabsFile, text, new System.Text.UTF8Encoding(false));

    public void TouchIconCache(string fileName)
    {
        Directory.CreateDirectory(IconsDirectory);
        File.WriteAllBytes(System.IO.Path.Combine(IconsDirectory, fileName), new byte[] { 1, 2, 3 });
    }

    public void Dispose()
    {
        // 先删登记在册的外部文件（放在 try 里各自独立，一个删不掉不影响其它）
        foreach (var file in _registeredFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 删不掉不影响测试结论
            }
        }

        _registeredFiles.Clear();

        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时目录删不掉不影响测试结论。
            // ⚠️ 必须把 UnauthorizedAccessException 一起接住：只接 IOException 时，
            //    目录被占用/只读会让 Dispose 抛异常 —— 那会盖掉真正的断言失败，
            //    在报告里表现为"假红"（看不出是哪条断言的锅）。
        }
    }
}
