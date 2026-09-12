// IconExtractor.cs —— ToolboxPanel v2 · W2 系统能力
//
// 对应原 Python 的 src/toolbox/services/icon_resolver.py（系统图标提取 + PNG 缓存）。
//
// 与原版的对应关系：
//   QFileIconProvider.icon(path).pixmap(64,64)  →  SHGetFileInfo(SHGFI_ICON|SHGFI_LARGEICON)
//   win32gui.ExtractIconEx(file, index)         →  ExtractIconEx
//   QStyle.standardIcon(SP_*)（兜底）           →  SHGetStockIconInfo(SIID_*)
//   QPixmap.save(png)                           →  System.Drawing → PNG
//
// ⚠️ 与原版的两处差异：
//   1. 原版把图标**放大**到 icon_size（QPixmap 缩放 32→64 会糊）；这里同样缩放到统一尺寸
//      （SHGetFileInfo 给的是 32×32），但用高质量插值 —— 观感略好，语义一致。
//      想要真正的 256px 大图需要 `IShellItemImageFactory`，留作后续优化（见 DEVELOPMENT.md）。
//   2. 兜底图标用的是 Windows 自己的 stock icon（SIID_*），不是 Qt 的标准图标；
//      两者的语义映射写在 <see cref="FallbackStockIcon"/> 里。
//
// 所有 HICON 都必须 DestroyIcon，否则每次提取都会漏一个 GDI 对象（原版也显式销毁了）。

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ToolboxPanel.Core.Models;
using static ToolboxPanel.Core.Services.ShellApi;

namespace ToolboxPanel.Core.Services;

/// <summary>系统图标提取与 PNG 缓存。</summary>
public sealed class IconExtractor
{
    /// <summary>原版 <c>IconResolver(cache_dir, icon_size=64)</c> 的默认尺寸。</summary>
    public const int DefaultIconSize = 64;

    public IconExtractor(string cacheDirectory, int iconSize = DefaultIconSize)
    {
        CacheDirectory = Path.GetFullPath(cacheDirectory);
        IconSize = iconSize > 0 ? iconSize : DefaultIconSize;
        Directory.CreateDirectory(CacheDirectory);
    }

    public string CacheDirectory { get; }

    public int IconSize { get; }

    /// <summary>缓存文件的完整路径。</summary>
    public string CachePath(string cacheFileName) => Path.Combine(CacheDirectory, cacheFileName);

    // ────────────────────────────── 主入口 ──────────────────────────────

    /// <summary>
    /// 提取某个路径的系统图标并缓存，返回缓存文件名（<c>uuid.png</c>）。
    /// 提取失败**也**会写一张该类型的兜底图标（与原版 <c>extract_and_cache</c> 一致），
    /// 所以正常情况不会返回空串；只有连兜底都画不出来时才返回空串。
    /// </summary>
    public string ExtractAndCache(string? path, IconType type = IconType.File)
    {
        IntPtr hIcon = ExtractIconHandle(path);
        return SaveIconToCache(hIcon, type);
    }

    /// <summary>
    /// 从 exe/dll/ico 里按索引取图标（原版 <c>extract_icon_from_file</c>）；
    /// 取不到时退回该文件自身的默认图标，最后才退回兜底图标。
    /// </summary>
    public string ExtractAndCacheFromIconFile(string? filePath, int index = 0, IconType type = IconType.File)
    {
        IntPtr hIcon = ExtractIconHandleFromFile(filePath, index);
        if (hIcon == IntPtr.Zero)
        {
            hIcon = ExtractIconHandle(filePath);
        }

        return SaveIconToCache(hIcon, type);
    }

    /// <summary>
    /// 给一个图标模型提取图标：
    ///   · **URL / COMMAND → 直接用该类型的标准图标**（与原版一致：原版是
    ///     <c>get_fallback(IconType.URL/COMMAND)</c>，**从不按路径提取** —— 这两类的
    ///     source_path 是网址/命令行，拿去当文件名查图标只会得到毫无意义的图）；
    ///   · 快捷方式带自定义图标 → 用「文件 + 索引」；
    ///   · 其余（文件/文件夹/快捷方式） → 按路径取系统图标。
    /// </summary>
    /// <param name="icon">目标图标。</param>
    /// <param name="shortcut">该快捷方式的解析结果（可为 null；有自定义图标时会被优先使用）。</param>
    public string ExtractAndCacheForIcon(IconModel icon, ShortcutInfo? shortcut = null)
    {
        ArgumentNullException.ThrowIfNull(icon);

        if (icon.Type is IconType.Url or IconType.Command)
        {
            return CreateFallbackCache(icon.Type);
        }

        if (shortcut is not null && !string.IsNullOrWhiteSpace(shortcut.IconPath))
        {
            return ExtractAndCacheFromIconFile(shortcut.IconPath, shortcut.IconIndex, icon.Type);
        }

        var path = !string.IsNullOrWhiteSpace(icon.SourcePath) ? icon.SourcePath : icon.TargetPath;
        return ExtractAndCache(path, icon.Type);
    }

    /// <summary>某个类型的兜底图标缓存文件名（不提取任何真实图标）。</summary>
    public string CreateFallbackCache(IconType type) => SaveIconToCache(IntPtr.Zero, type);

    // ────────────────────────────── 提取 ──────────────────────────────

    /// <summary>按路径取 HICON；取不到返回 <see cref="IntPtr.Zero"/>。调用方负责销毁。</summary>
    private IntPtr ExtractIconHandle(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return IntPtr.Zero;
        }

        try
        {
            // .lnk 交给 shell 自己解析（原版 QFileIconProvider 也是这个行为）
            if (File.Exists(path) || Directory.Exists(path))
            {
                var info = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_LARGEICON;
                IntPtr result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
                if (result != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                {
                    return info.hIcon;
                }
            }

            // 路径不存在：用「文件名后缀 + USEFILEATTRIBUTES」让 shell 按类型给图标
            // （例如 .txt → 文本图标），这样新建的图标还没落地也能有个像样的图
            var attrs = LooksLikeDirectory(path) ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            var fallbackInfo = new SHFILEINFO();
            IntPtr fallbackResult = SHGetFileInfo(
                Path.GetFileName(path),
                attrs,
                ref fallbackInfo,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);

            return fallbackResult != IntPtr.Zero ? fallbackInfo.hIcon : IntPtr.Zero;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or IOException)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>从 exe/dll/ico 的指定索引取大图标；取不到返回 <see cref="IntPtr.Zero"/>。</summary>
    private static IntPtr ExtractIconHandleFromFile(string? filePath, int index)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return IntPtr.Zero;
        }

        var large = new IntPtr[1];
        try
        {
            uint extracted = ExtractIconEx(filePath, index, large, null, 1);
            return extracted > 0 ? large[0] : IntPtr.Zero;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return IntPtr.Zero;
        }
    }

    private static bool LooksLikeDirectory(string path)
        => path.EndsWith('\\') || path.EndsWith('/');

    // ────────────────────────────── 保存 ──────────────────────────────

    /// <summary>把 HICON 写进缓存目录（hIcon 为 0 时写该类型的兜底图标）；返回缓存文件名。</summary>
    private string SaveIconToCache(IntPtr hIcon, IconType type)
    {
        var cacheName = $"{Guid.NewGuid()}.png";
        var cachePath = Path.Combine(CacheDirectory, cacheName);

        try
        {
            if (hIcon != IntPtr.Zero)
            {
                using var iconBitmap = Bitmap.FromHicon(hIcon);
                SaveScaled(iconBitmap, cachePath);
            }
            else
            {
                using var stock = CreateStockIcon(type);
                if (stock is not null)
                {
                    SaveScaled(stock, cachePath);
                }
                else
                {
                    using var placeholder = CreatePlaceholder(type);
                    SaveScaled(placeholder, cachePath);
                }
            }
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or IOException or OutOfMemoryException)
        {
            return string.Empty;
        }
        finally
        {
            if (hIcon != IntPtr.Zero)
            {
                DestroyIcon(hIcon);
            }
        }

        return cacheName;
    }

    /// <summary>统一到 <see cref="IconSize"/> 见方并写成 PNG（保留 alpha）。</summary>
    private void SaveScaled(Bitmap source, string cachePath)
    {
        using var target = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, IconSize, IconSize);
        }

        target.Save(cachePath, ImageFormat.Png);
    }

    /// <summary>取 Windows stock icon（对应原版的 QStyle 标准图标）；失败返回 null。</summary>
    private static Bitmap? CreateStockIcon(IconType type)
    {
        var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };

        int hr = SHGetStockIconInfo((int)FallbackStockIcon(type), SHGSI_ICON | SHGSI_LARGEICON, ref info);
        if (hr != 0 || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // FromHicon 复制像素，之后就能安全销毁原生 HICON
            return Bitmap.FromHicon(info.hIcon);
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException)
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    /// <summary>原版 <c>FALLBACK_ICON_MAP</c>（Qt 标准图标）→ Windows stock icon 的语义映射。</summary>
    internal static ShellStockIcon FallbackStockIcon(IconType type) => type switch
    {
        IconType.Folder => ShellStockIcon.Folder,                 // SP_DirIcon
        IconType.Shortcut => ShellStockIcon.Link,                 // SP_FileLinkIcon
        IconType.Url => ShellStockIcon.DesktopPc,                 // SP_ComputerIcon
        IconType.Command => ShellStockIcon.Application,           // SP_CommandLink（近似）
        _ => ShellStockIcon.DocumentNoAssoc,                      // SP_FileIcon
    };

    /// <summary>连 stock icon 都拿不到时，画一个最简占位图（保证永远有图可显示）。</summary>
    private Bitmap CreatePlaceholder(IconType type)
    {
        var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(type switch
        {
            IconType.Folder => Color.FromArgb(255, 214, 178, 92),
            IconType.Url => Color.FromArgb(255, 96, 165, 250),
            IconType.Command => Color.FromArgb(255, 129, 140, 248),
            IconType.Shortcut => Color.FromArgb(255, 148, 163, 184),
            _ => Color.FromArgb(255, 148, 163, 184),
        });

        int inset = IconSize / 8;
        graphics.FillRectangle(brush, inset, inset, IconSize - inset * 2, IconSize - inset * 2);
        return bitmap;
    }
}
