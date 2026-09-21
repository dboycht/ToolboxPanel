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
using ToolboxPanel.Core.Storage;
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
        return SaveIconToCache(ExtractHandleFor(icon, shortcut), icon.Type);
    }

    /// <summary>某个类型的兜底图标缓存文件名（不提取任何真实图标）。</summary>
    public string CreateFallbackCache(IconType type) => SaveIconToCache(IntPtr.Zero, type);

    // ────────────────────────────── 缓存格式版本（自动迁移）──────────────────────────────

    /// <summary>
    /// 当前**图标缓存格式**版本。提取方式一变就 +1 ⇒ 下次启动自动把已有缓存原地重提一遍。
    ///
    /// <para>⚠️ 为什么需要它（2026-09-21 的"图标黑底"修复）：`LoadOrExtractIcon` 只在
    /// **缓存文件不存在**时才提取 ⇒ 改了提取代码之后，用户磁盘上已有的坏 PNG 不会自己变好，
    /// 界面上还是老样子（"我修了、用户看不见修"）。版本号把"升级后重提一次"变成显式的、可测的行为。</para>
    ///
    /// <para>版本历史：1 = 最初的 `Bitmap.FromHicon`（透明区会变黑底）；**2 = 保住 alpha 的转换**（本版）。</para>
    /// </summary>
    public const int CacheFormat = 2;

    /// <summary>格式标记文件名（放在 <c>icons/</c> 里；以 `.` 开头 ⇒ 不被当作孤儿缓存删掉）。</summary>
    public const string CacheFormatFileName = ".cache-format";

    /// <summary>格式标记的完整路径。</summary>
    public string CacheFormatPath => Path.Combine(CacheDirectory, CacheFormatFileName);

    /// <summary>磁盘上的缓存是不是当前格式（文件缺失 / 读不动 / 版本旧 ⇒ false）。</summary>
    public bool IsCacheFormatCurrent
    {
        get
        {
            try
            {
                if (!File.Exists(CacheFormatPath))
                {
                    return false;
                }

                var text = File.ReadAllText(CacheFormatPath).Trim();
                return int.TryParse(text, out var version) && version >= CacheFormat;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 读不动就当作"不是当前格式"：最坏结果是多提取一遍（幂等、且都是本机系统图标）
                return false;
            }
        }
    }

    /// <summary>写下格式标记（失败不影响使用：下次启动再提一遍而已）。</summary>
    public void MarkCacheFormatCurrent()
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            File.WriteAllText(
                CacheFormatPath, CacheFormat.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不下就算了
        }
    }

    /// <summary>
    /// 把某个图标的图标**原地重提**到它已有的缓存文件名上（用于缓存格式升级）。
    ///
    /// <para>⚠️ 为什么坚持"用原来的文件名"而不是新生成一个：这样 `tabs.json` **一个字节都不用改**，
    /// 也不会产生孤儿缓存 —— 迁移只碰 `icons/` 里的 PNG 内容，数据文件完全不动。</para>
    /// </summary>
    /// <returns>提取并写盘成功返回 true。</returns>
    public bool RefreshCache(IconModel icon, string? cacheFileName, ShortcutInfo? shortcut = null)
    {
        ArgumentNullException.ThrowIfNull(icon);

        // 缓存文件名来自磁盘上的 JSON ⇒ 当不可信输入（只接受裸文件名；`..\` 之类一律拒绝）
        if (CacheFileName.Combine(CacheDirectory, cacheFileName) is not { } cachePath)
        {
            return false;
        }

        return SaveIconToPath(ExtractHandleFor(icon, shortcut), icon.Type, cachePath);
    }

    // ────────────────────────────── 提取 ──────────────────────────────

    /// <summary>
    /// 按图标模型挑出该用哪个 HICON（URL/COMMAND → 0，表示"用该类型的标准图标"）。
    /// <para><see cref="ExtractAndCacheForIcon"/> 与 <see cref="RefreshCache"/> 共用这一处判定，
    /// 免得两条路各自写一遍"什么时候用自定义图标、什么时候按路径提"。</para>
    /// </summary>
    private IntPtr ExtractHandleFor(IconModel icon, ShortcutInfo? shortcut)
    {
        // ⚠️ URL / COMMAND **从不按路径提取**：它们的 source_path 是网址/命令行，
        //    拿去当文件名查图标只会得到毫无意义的图（原版就是 `get_fallback(URL/COMMAND)`）
        if (icon.Type is IconType.Url or IconType.Command)
        {
            return IntPtr.Zero;
        }

        if (shortcut is not null && !string.IsNullOrWhiteSpace(shortcut.IconPath))
        {
            // ⚠️ 与原实现**完全同序**：先「图标文件 + 索引」，取不到再退回**那个图标文件本身**的默认图标
            //    （不是退回 source_path —— 用户既然指定了自定义图标，就不该悄悄换回目标程序的图标）
            var custom = ExtractIconHandleFromFile(shortcut.IconPath, shortcut.IconIndex);
            return custom != IntPtr.Zero ? custom : ExtractIconHandle(shortcut.IconPath);
        }

        var path = !string.IsNullOrWhiteSpace(icon.SourcePath) ? icon.SourcePath : icon.TargetPath;
        return ExtractIconHandle(path);
    }

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

    /// <summary>
    /// 从 exe/dll/ico 的指定索引取图标；取不到返回 <see cref="IntPtr.Zero"/>。
    ///
    /// <para>先请求**大图标**，拿不到再请求**小图标** —— 只试大图标时，
    /// 那些"只有 16×16 资源"的 .ico / dll 会直接失败，调用方随即退化成
    /// "按文件类型给的通用图标"，用户会以为自定义图标生效了，其实没有（静默错图）。</para>
    /// </summary>
    private static IntPtr ExtractIconHandleFromFile(string? filePath, int index)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return IntPtr.Zero;
        }

        // 两条路各拿一次；**拿到的句柄不用的那些必须销毁**，否则每次提取漏一个 GDI 对象。
        var large = ExtractOne(filePath, index, wantLarge: true);
        if (large != IntPtr.Zero)
        {
            return large;
        }

        return ExtractOne(filePath, index, wantLarge: false);
    }

    /// <summary>取「大图标」或「小图标」其中一种；取不到返回 <see cref="IntPtr.Zero"/>。</summary>
    private static IntPtr ExtractOne(string filePath, int index, bool wantLarge)
    {
        var buffer = new IntPtr[1];

        try
        {
            // ⚠️ 判据必须是"恰好取到 1 个 **且** 句柄非空"：
            //    `ExtractIconEx` 失败时可能返回 0，也可能返回 `UINT_MAX`（索引超范围），
            //    写成 `count > 0` 会把 UINT_MAX 当成功。
            uint count = wantLarge
                ? ExtractIconEx(filePath, index, buffer, null, 1)
                : ExtractIconEx(filePath, index, null, buffer, 1);

            if (count == 1 && buffer[0] != IntPtr.Zero)
            {
                return buffer[0];
            }

            if (buffer[0] != IntPtr.Zero)
            {
                DestroyIcon(buffer[0]);   // 拿到了但整体判定失败 ⇒ 就地销毁，不漏句柄
            }

            return IntPtr.Zero;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return IntPtr.Zero;
        }
    }

    private static bool LooksLikeDirectory(string path)
        => path.EndsWith('\\') || path.EndsWith('/');

    // ────────────────────────────── 保存 ──────────────────────────────

    /// <summary>
    /// 把 HICON 写进缓存目录（hIcon 为 0 时写该类型的兜底图标）；返回缓存文件名。
    ///
    /// <para>⚠️ 本方法**接管 <paramref name="hIcon"/> 的所有权**：无论成功、失败、还是中途抛异常，
    /// 它都保证把句柄销毁掉（见下面的 finally）。</para>
    /// </summary>
    private string SaveIconToCache(IntPtr hIcon, IconType type)
    {
        var cacheName = $"{Guid.NewGuid()}.png";
        return SaveIconToPath(hIcon, type, Path.Combine(CacheDirectory, cacheName)) ? cacheName : string.Empty;
    }

    /// <summary>
    /// 把 HICON（为 0 时用该类型的兜底图标）写进**指定路径**；成功返回 true。
    ///
    /// <para>⚠️ 本方法**接管 <paramref name="hIcon"/> 的所有权**：无论成功、失败、还是中途抛异常，
    /// 它都保证把句柄销毁掉。</para>
    /// </summary>
    private bool SaveIconToPath(IntPtr hIcon, IconType type, string cachePath)
    {
        // ── 第一步：把原生 HICON 换成托管 Bitmap，**换完立刻销毁句柄** ──
        //
        // ⚠️ 为什么销毁要贴着转换写、而不是放在方法末尾的 finally：
        //    转换会**复制像素**，托管 Bitmap 一旦拿到手，HICON 就可以立刻释放；
        //    而"把 DestroyIcon 留到最后"的写法太脆 —— 中间任何一条 `return`（例如
        //    "写不出缓存就返回 false"那条）都会把句柄漏掉，正是本文件开头警告的那种
        //    "每次提取都漏一个 GDI 对象"。现在顺序写死：**拿到托管对象 → 立刻销毁原生句柄**。
        Bitmap? source = null;
        if (hIcon != IntPtr.Zero)
        {
            try
            {
                source = ToBitmapPreservingAlpha(hIcon);
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        try
        {
            if (source is not null)
            {
                using (source)
                {
                    SaveScaled(source, cachePath);
                }

                return true;
            }

            // ── 兜底图 ──
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

            return true;
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or IOException or OutOfMemoryException)
        {
            // 写不出缓存：返回 false（调用方据此知道"这张图没成"），与原版一致
            return false;
        }
    }

    /// <summary>
    /// HICON → 托管 Bitmap，**保住透明通道**。
    ///
    /// <para>⚠️⚠️ 这里绝对不要用 `Bitmap.FromHicon` —— 那是 2026-09-21 用户反馈
    /// "有的图标背景是半透明的、有的却是黑的"的真根因（`ERROR.md` **E42**）：
    /// 对**用 AND 掩码表示透明**的图标（`SHGetFileInfo` 给的就是这一类：系统图库里的
    /// 文件 / 快捷方式图标都算），`FromHicon` 会认为"这张图根本没有 alpha"，
    /// 于是把**所有像素的 alpha 都填成 255** ⇒ 透明区变成不透明黑 ⇒ 屏幕上是"图标背后一块黑方块"。
    /// 深色主题下看不出来（黑底黑背景），浅色主题下一眼就是黑方块 —— 用户就是切到浅色后发现的。</para>
    ///
    /// <para>本机实测（.NET 9 / Win11 26200，notepad.exe 的 32×32 图标，放大到 64 后取样）：</para>
    /// <list type="bullet">
    ///   <item>`Bitmap.FromHicon`：1024 个像素**全部 alpha=255**，角上 `A255 #000000`（黑底）；</item>
    ///   <item>`Icon.FromHandle(...).ToBitmap()`：角上 `A0`、边缘 `A19`（正常的抗锯齿过渡），
    ///     而图标主体颜色**逐像素相同**（`#8AC5D2`）⇒ 只修好了透明，没有动内容。</item>
    /// </list>
    ///
    /// <para>`Icon.ToBitmap()` 走的是 `DrawIconEx` 语义，会同时尊重 alpha 通道与 AND 掩码，
    /// 所以两种图标（老的掩码型 / 新的 alpha 型）都对。回归测试见
    /// <c>IconExtractorTests.提取的图标必须保留透明背景_不能是黑底</c>。</para>
    /// </summary>
    private static Bitmap? ToBitmapPreservingAlpha(IntPtr hIcon)
    {
        try
        {
            // ⚠️ `Icon.FromHandle` **不接管句柄所有权** ⇒ 调用方仍然负责 DestroyIcon（见上）。
            //    所以这里的 using 只释放托管包装，不会把 HICON 销毁两次。
            using var icon = Icon.FromHandle(hIcon);
            return icon.ToBitmap();
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or OutOfMemoryException
                                      or InvalidOperationException or NotSupportedException)
        {
            return null;   // 换不出托管图 ⇒ 由调用方走兜底图标
        }
    }

    /// <summary>
    /// 统一到 <see cref="IconSize"/> 见方并写成 PNG（保留 alpha）。
    ///
    /// <para>⚠️ 先写 `*.tmp` 再原子替换：`RefreshCache` 会**覆盖**已有缓存，
    /// 中途失败留下半张 PNG 的话，界面上就是一个坏图标（而且下次启动"文件还在"⇒ 不会重提）。</para>
    /// </summary>
    private void SaveScaled(Bitmap source, string cachePath)
    {
        var tempPath = cachePath + ".tmp";

        try
        {
            using (var target = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(target))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(source, 0, 0, IconSize, IconSize);
                }

                target.Save(tempPath, ImageFormat.Png);
            }

            File.Move(tempPath, cachePath, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);   // 成功时它已经不在了；失败时别把 .tmp 留在 icons/ 里
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
            // 删不掉不影响结论（孤儿清理下次会收）
        }
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
            // ToBitmapPreservingAlpha 复制像素，之后就能安全销毁原生 HICON
            return ToBitmapPreservingAlpha(info.hIcon);
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
