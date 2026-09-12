// TempDataDirectory.cs —— 单测辅助：每个测试一个独立临时数据目录

using ToolboxPanel.Core.Storage;

namespace ToolboxPanel.Core.Tests;

/// <summary>用完即删的临时数据目录（避免测试之间互相污染）。</summary>
public sealed class TempDataDirectory : IDisposable
{
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
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录删不掉不影响测试结论
        }
    }
}
