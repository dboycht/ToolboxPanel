// IconExtractorTests.cs —— W2：图标提取与 PNG 缓存
//
// 全部是**只读/离线**验证：只读系统文件、只在自己临时目录里写 PNG，
// 不会打开任何窗口、不会起任何进程（ERROR.md E5 的纪律）。

using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using ToolboxPanel.Core.Models;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

public class IconExtractorTests
{
    private static string WindowsDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private static string NotepadPath => Path.Combine(WindowsDir, "System32", "notepad.exe");

    private static string Shell32Path => Path.Combine(WindowsDir, "System32", "shell32.dll");

    [Fact]
    public void 提取exe图标_得到指定尺寸且非空白的PNG()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        var cacheName = extractor.ExtractAndCache(NotepadPath, IconType.File);

        Assert.False(string.IsNullOrEmpty(cacheName));
        Assert.EndsWith(".png", cacheName);
        Assert.True(Guid.TryParse(Path.GetFileNameWithoutExtension(cacheName), out _));   // uuid.png

        var cachePath = Path.Combine(temp.IconsDirectory, cacheName);
        Assert.True(File.Exists(cachePath));
        AssertImage(cachePath, size: IconExtractor.DefaultIconSize, minOpaqueRatio: 0.02);
    }

    [Fact]
    public void 提取文件夹图标_成功()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        var cacheName = extractor.ExtractAndCache(temp.Path, IconType.Folder);

        Assert.False(string.IsNullOrEmpty(cacheName));
        AssertImage(Path.Combine(temp.IconsDirectory, cacheName), IconExtractor.DefaultIconSize, 0.02);
    }

    [Fact]
    public void 路径不存在_按后缀给图标而不是空白()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        // 不存在的 .txt：原版 Qt 走 QFileIconProvider 也会按扩展名给图标
        var cacheName = extractor.ExtractAndCache(Path.Combine(temp.Path, "还不存在.txt"), IconType.File);

        Assert.False(string.IsNullOrEmpty(cacheName));
        AssertImage(Path.Combine(temp.IconsDirectory, cacheName), IconExtractor.DefaultIconSize, 0.01);
    }

    [Fact]
    public void 五种类型都能拿到兜底图标()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        foreach (var type in new[] { IconType.File, IconType.Folder, IconType.Shortcut, IconType.Url, IconType.Command })
        {
            var cacheName = extractor.CreateFallbackCache(type);

            Assert.False(string.IsNullOrEmpty(cacheName), $"{type} 没拿到兜底图标");
            AssertImage(Path.Combine(temp.IconsDirectory, cacheName), IconExtractor.DefaultIconSize, 0.01);
        }
    }

    [Fact]
    public void 按索引提取_不同索引得到不同图标()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        var index3 = extractor.ExtractAndCacheFromIconFile(Shell32Path, 3, IconType.File);
        var index13 = extractor.ExtractAndCacheFromIconFile(Shell32Path, 13, IconType.File);

        Assert.False(string.IsNullOrEmpty(index3));
        Assert.False(string.IsNullOrEmpty(index13));

        var path3 = Path.Combine(temp.IconsDirectory, index3);
        var path13 = Path.Combine(temp.IconsDirectory, index13);
        AssertImage(path3, IconExtractor.DefaultIconSize, 0.01);
        AssertImage(path13, IconExtractor.DefaultIconSize, 0.01);
        Assert.NotEqual(File.ReadAllBytes(path3), File.ReadAllBytes(path13));   // 索引确实被用上了
    }

    [Fact]
    public void 按索引提取_文件不存在时退回兜底不抛异常()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        var cacheName = extractor.ExtractAndCacheFromIconFile(Path.Combine(temp.Path, "没有.dll"), 7, IconType.Command);

        Assert.False(string.IsNullOrEmpty(cacheName));
        AssertImage(Path.Combine(temp.IconsDirectory, cacheName), IconExtractor.DefaultIconSize, 0.01);
    }

    [Fact]
    public void 快捷方式的自定义图标_优先于目标程序图标()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);
        var lnkPath = Path.Combine(temp.Path, "带自定义图标.lnk");

        TestShortcutFactory.Create(lnkPath, NotepadPath, iconPath: Shell32Path, iconIndex: 5);
        var shortcut = WindowsShortcut.Resolve(lnkPath);
        Assert.NotNull(shortcut);

        var icon = new IconModel { Type = IconType.Shortcut, SourcePath = lnkPath, TargetPath = NotepadPath };

        var viaShortcut = extractor.ExtractAndCacheForIcon(icon, shortcut);
        var expected = extractor.ExtractAndCacheFromIconFile(Shell32Path, 5, IconType.Shortcut);
        var targetIcon = extractor.ExtractAndCache(NotepadPath, IconType.File);

        // 用的是「自定义图标文件 + 索引」，不是目标程序自己的图标
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(temp.IconsDirectory, expected)),
            File.ReadAllBytes(Path.Combine(temp.IconsDirectory, viaShortcut)));
        Assert.NotEqual(
            File.ReadAllBytes(Path.Combine(temp.IconsDirectory, targetIcon)),
            File.ReadAllBytes(Path.Combine(temp.IconsDirectory, viaShortcut)));
    }

    [Fact]
    public void 没有自定义图标时_用快捷方式目标自身的图标()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);
        var lnkPath = Path.Combine(temp.Path, "普通.lnk");
        TestShortcutFactory.Create(lnkPath, NotepadPath);

        var info = WindowsShortcut.Resolve(lnkPath);
        var icon = new IconModel { Type = IconType.Shortcut, SourcePath = lnkPath, TargetPath = NotepadPath };

        var viaShortcut = extractor.ExtractAndCacheForIcon(icon, info);
        var direct = extractor.ExtractAndCache(lnkPath, IconType.Shortcut);

        // 两次都读 .lnk 的状态栏图标，结果应一致（同一来源）
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(temp.IconsDirectory, direct)),
            File.ReadAllBytes(Path.Combine(temp.IconsDirectory, viaShortcut)));
    }

    [Fact]
    public void 缓存文件名唯一_每次都是新文件()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        var first = extractor.ExtractAndCache(NotepadPath);
        var second = extractor.ExtractAndCache(NotepadPath);

        Assert.NotEqual(first, second);   // 原版也是每次新 uuid.png（旧的由孤儿清理负责删）
        Assert.Equal(2, Directory.GetFiles(temp.IconsDirectory, "*.png").Length);
    }

    [Fact]
    public void 图标尺寸可配置()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory, iconSize: 32);

        var cacheName = extractor.ExtractAndCache(NotepadPath);

        AssertImage(Path.Combine(temp.IconsDirectory, cacheName), 32, 0.02);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 空路径_不抛异常_产出兜底图(string? path)
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        var cacheName = extractor.ExtractAndCache(path, IconType.Url);

        Assert.False(string.IsNullOrEmpty(cacheName));
        AssertImage(Path.Combine(temp.IconsDirectory, cacheName), IconExtractor.DefaultIconSize, 0.01);
    }

    [Fact]
    public void 兜底图标映射与原版语义一致()
    {
        // 原版 FALLBACK_ICON_MAP 用的是 Qt 标准图标，这里逐个对应到 Windows stock icon
        Assert.Equal(ShellStockIcon.DocumentNoAssoc, IconExtractor.FallbackStockIcon(IconType.File));
        Assert.Equal(ShellStockIcon.Folder, IconExtractor.FallbackStockIcon(IconType.Folder));
        Assert.Equal(ShellStockIcon.Link, IconExtractor.FallbackStockIcon(IconType.Shortcut));
        Assert.Equal(ShellStockIcon.DesktopPc, IconExtractor.FallbackStockIcon(IconType.Url));
        Assert.Equal(ShellStockIcon.Application, IconExtractor.FallbackStockIcon(IconType.Command));
    }

    [Fact]
    public void 生成的PNG能被再次读入_尺寸与格式正确()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);
        var cacheName = extractor.ExtractAndCache(NotepadPath);
        var cachePath = Path.Combine(temp.IconsDirectory, cacheName);

        using var image = new Bitmap(cachePath);

        Assert.Equal(IconExtractor.DefaultIconSize, image.Width);
        Assert.Equal(IconExtractor.DefaultIconSize, image.Height);
        Assert.Equal(ImageFormat.Png.Guid, image.RawFormat.Guid);

        // PNG magic number：确认不是别的东西被写成了 .png 后缀
        var bytes = File.ReadAllBytes(cachePath);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take(4).ToArray());
    }

    // ────────────────────────────── 断言辅助 ──────────────────────────────

    /// <summary>断言是一张指定尺寸、可解码、且"有内容"（不透明像素占比达标）的 PNG。</summary>
    private static void AssertImage(string pngPath, int size, double minOpaqueRatio)
    {
        Assert.True(File.Exists(pngPath), $"没有生成 PNG：{pngPath}");

        using var image = new Bitmap(pngPath);
        Assert.Equal(size, image.Width);
        Assert.Equal(size, image.Height);

        int opaque = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y).A > 16)
                {
                    opaque++;
                }
            }
        }

        double ratio = (double)opaque / (image.Width * image.Height);
        Assert.True(ratio >= minOpaqueRatio, $"{Path.GetFileName(pngPath)} 几乎是空白（不透明占比 {ratio:P1}）");
    }
}
