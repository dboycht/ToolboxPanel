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

    [Theory]
    [InlineData(IconType.Url)]
    [InlineData(IconType.Command)]
    public void URL与命令用类型标准图标_不按路径提取(IconType type)
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        // 原版语义：这两类走 get_fallback(type)，source_path 是网址/命令行，不参与图标提取
        var icon = type == IconType.Url
            ? new IconModel { Type = type, SourcePath = "https://example.com/path?q=1" }
            : new IconModel { Type = type, SourcePath = "cmd.exe /c echo hi", TargetPath = "cmd.exe" };

        var viaModel = extractor.ExtractAndCacheForIcon(icon);
        var directFallback = extractor.CreateFallbackCache(type);

        Assert.Equal(
            File.ReadAllBytes(Path.Combine(temp.IconsDirectory, directFallback)),
            File.ReadAllBytes(Path.Combine(temp.IconsDirectory, viaModel)));
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

    // ────────────────────────────── ★ 透明背景（2026-09-21 用户反馈的真 bug）──────────────────────────────
    //
    // 现象：用户（切到浅色主题后）看到"有的图标背景是半透明的、有的却是黑的"。
    // 根因：`Bitmap.FromHicon` 对**用 AND 掩码表示透明**的图标（SHGetFileInfo 给的就是这一类）
    //       会把所有像素的 alpha 填成 255 ⇒ 透明区变成不透明黑 ⇒ 图标背后一块黑方块。
    // 详见 ERROR.md E42。

    [Fact]
    public void 提取的图标必须保留透明背景_不能是黑底()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        // ⚠️ 必须用"从 shell 图库拿图标"的那条路（SHGetFileInfo）：它给的正是掩码型图标。
        //    只测兜底/stock 图标是测不出这个 bug 的（那些本来就带 alpha）。
        var cacheName = extractor.ExtractAndCache(NotepadPath, IconType.File);
        var cachePath = Path.Combine(temp.IconsDirectory, cacheName);

        using var image = new Bitmap(cachePath);

        // ① 角上必须是透明的（黑底 bug 下这里是 A255）
        var corner = image.GetPixel(1, 1);
        Assert.True(corner.A < 8, $"左上角不是透明的：A={corner.A}（图标被画在了不透明底上，见 ERROR.md E42）");

        // ② "不透明纯黑"的像素占比必须很小：那正是黑方块的指纹。
        //    图标本身可以有黑色描边/黑字，所以给 10% 的余量；黑底 bug 下这个比例是 20%~40%。
        int opaqueBlack = 0;
        int total = image.Width * image.Height;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.A > 240 && pixel.R < 12 && pixel.G < 12 && pixel.B < 12)
                {
                    opaqueBlack++;
                }
            }
        }

        double ratio = (double)opaqueBlack / total;
        Assert.True(ratio < 0.10, $"不透明黑占了 {ratio:P1} —— 透明区被填成了黑底（ERROR.md E42）");

        // ③ 反向对照：图标本身还得有内容（别用"整张全透明"来骗过上面两条）
        AssertImage(cachePath, IconExtractor.DefaultIconSize, minOpaqueRatio: 0.02);
    }

    [Fact]
    public void 快捷方式图标同样保留透明背景()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        // .lnk 的图标同样来自 shell 图库（掩码型）
        var lnkPath = Path.Combine(temp.Path, "probe.lnk");
        TestShortcutFactory.Create(lnkPath, NotepadPath);

        var cacheName = extractor.ExtractAndCache(lnkPath, IconType.Shortcut);
        var cachePath = Path.Combine(temp.IconsDirectory, cacheName);

        using var image = new Bitmap(cachePath);
        var corner = image.GetPixel(1, 1);
        Assert.True(corner.A < 8, $"快捷方式图标左上角不是透明的：A={corner.A}（ERROR.md E42）");
    }

    // ────────────────────────────── 缓存格式版本 + 原地重提 ──────────────────────────────

    [Fact]
    public void 缓存格式标记_写入后才算当前格式()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        Assert.False(extractor.IsCacheFormatCurrent);   // 新目录里没有标记

        extractor.MarkCacheFormatCurrent();
        Assert.True(extractor.IsCacheFormatCurrent);

        // 旧版本的标记 ⇒ 视为"需要重提"
        File.WriteAllText(extractor.CacheFormatPath, "1");
        Assert.False(extractor.IsCacheFormatCurrent);

        // 比当前更高的版本（降级运行）⇒ 不要重提（别人的新格式别乱覆盖）
        File.WriteAllText(extractor.CacheFormatPath, (IconExtractor.CacheFormat + 1).ToString());
        Assert.True(extractor.IsCacheFormatCurrent);

        // 垃圾内容 ⇒ 当作需要重提（最坏只是多提一次）
        File.WriteAllText(extractor.CacheFormatPath, "not-a-number");
        Assert.False(extractor.IsCacheFormatCurrent);
    }

    [Fact]
    public void 原地重提会用同一个文件名覆盖_且内容被修好()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        // 先造一个"旧格式"的坏缓存：把真实图标写进一个已知文件名，再把它弄成黑底
        var fileName = "11111111-2222-3333-4444-555555555555.png";
        var cachePath = Path.Combine(temp.IconsDirectory, fileName);
        using (var black = new Bitmap(IconExtractor.DefaultIconSize, IconExtractor.DefaultIconSize,
                   System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using var graphics = Graphics.FromImage(black);
            graphics.Clear(Color.Black);            // 全不透明黑 = 旧格式的坏图
            black.Save(cachePath, ImageFormat.Png);
        }

        var icon = new IconModel { Type = IconType.File, SourcePath = NotepadPath, IconCacheFile = fileName };

        Assert.True(extractor.RefreshCache(icon, fileName));

        // 文件名没变（tabs.json 不用改），内容已经是透明背景的正确图标
        Assert.True(File.Exists(cachePath));
        using var refreshed = new Bitmap(cachePath);
        Assert.True(refreshed.GetPixel(1, 1).A < 8, "重提之后角上仍不是透明（没有真的覆盖成功）");
        Assert.Equal(IconExtractor.DefaultIconSize, refreshed.Width);
    }

    [Fact]
    public void 原地重提拒绝越界的缓存文件名()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);
        var icon = new IconModel { Type = IconType.File, SourcePath = NotepadPath };

        // 缓存文件名来自磁盘上的 JSON ⇒ 当不可信输入：`..\` 一律拒绝（同 CacheFileName 的判据）
        Assert.False(extractor.RefreshCache(icon, @"..\..\tabs.json"));
        Assert.False(extractor.RefreshCache(icon, ""));
        Assert.False(extractor.RefreshCache(icon, null));
    }

    [Fact]
    public void 重提中途失败不会留下临时文件()
    {
        using var temp = new TempDataDirectory();
        var extractor = new IconExtractor(temp.IconsDirectory);

        var cacheName = extractor.ExtractAndCache(NotepadPath);
        Assert.False(string.IsNullOrEmpty(cacheName));
        Assert.True(extractor.RefreshCache(
            new IconModel { Type = IconType.File, SourcePath = NotepadPath }, cacheName));

        // 写 PNG 走的是 `*.tmp` → 原子替换，跑完之后不该有 .tmp 残留
        Assert.Empty(Directory.GetFiles(temp.IconsDirectory, "*.tmp"));
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
