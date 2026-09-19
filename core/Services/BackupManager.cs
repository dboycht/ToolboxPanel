// BackupManager.cs —— 备份 ZIP 的导入 / 导出（纯逻辑，可单测）
//
// 移植自原版 v1.11.6 的 `services/backup_manager.py`（`unique_filename` / `build_metadata` /
// `BackupWorker._do_export` / `_do_import`）。**包格式必须与旧版双向兼容**，这是本文件的头号约束：
//
//   ┌ 压缩包
//   ├── metadata.json                 ← 根目录，字段与原版逐字一致
//   ├── data/tabs.json                ← 数据文件统一放在 data/ 前缀下
//   ├── data/config.json
//   └── data/icons/<uuid>.png
//
// ⚠️⚠️ **兼容性三连（都是实测过的坑，别改回去）**
//   ① **写出用正斜杠** `data/tabs.json`。原版在 Windows 上写的是 `data\tabs.json`（Python `Path`
//      拼接的产物），而它自己的导入只认 `startswith("data/")` ⇒ **原版导出 → 原版导入会把文件写到
//      `<数据目录>/data/tabs.json`（多一层），等于白导**。我们写正斜杠，原版导入就能正确识别；
//   ② **读入两种都认**：`data/`、`data\`、以及**没有前缀的裸文件名**（原版两条分支都支持）；
//   ③ **metadata.json 缺字段只当"未知"**，不报错（原版是 `.get(key, "?")`）。
//
// 另加一条原版没有的**安全线**：解压时拒绝写到数据目录之外的条目（zip-slip），对合法备份零影响。
//
// ⚠️ 这里**不碰任何运行中的界面状态** —— 导入完成后由调用方（WinUI）重新读盘并重建界面。

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolboxPanel.Core.Services;

/// <summary>备份元数据（字段与原版 <c>build_metadata</c> 的 json **逐字一致**）。</summary>
public sealed class BackupMetadata
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>ISO 8601 时间串（原版 <c>datetime.now().isoformat()</c>，不带时区）。</summary>
    [JsonPropertyName("exported_at")]
    public string ExportedAt { get; set; } = string.Empty;

    [JsonPropertyName("tab_count")]
    public int TabCount { get; set; }

    [JsonPropertyName("icon_count")]
    public int IconCount { get; set; }

    /// <summary>写进压缩包的 JSON：2 空格缩进 + 中文不转义（与原版 <c>json.dumps(indent=2, ensure_ascii=False)</c> 同义）。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>解析；坏 JSON 返回 null（调用方当"无效备份"处理）。缺字段保持默认值（= 未知）。</summary>
    public static BackupMetadata? TryParse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BackupMetadata>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>进度（与原版 <c>progress = pyqtSignal(int, int)</c> 同义：当前 / 总数）。</summary>
public readonly record struct BackupProgress(int Current, int Total);

/// <summary>一次导入/导出的结果。<see cref="Metadata"/> 仅在导入成功时给出。</summary>
public sealed class BackupResult
{
    private BackupResult(bool success, string message, BackupMetadata? metadata)
    {
        Success = success;
        Message = message;
        Metadata = metadata;
    }

    public bool Success { get; }

    /// <summary>成功时是 zip 路径（原版 <c>finished.emit(True, str(zip_path))</c>）；失败时是原因。</summary>
    public string Message { get; }

    public BackupMetadata? Metadata { get; }

    public static BackupResult Ok(string zipPath, BackupMetadata? metadata = null) => new(true, zipPath, metadata);

    public static BackupResult Fail(string reason) => new(false, reason, null);
}

/// <summary>ZIP 备份的导入与导出（纯逻辑：不依赖 UI、不依赖 WinUI）。</summary>
public static class BackupManager
{
    /// <summary>元数据在压缩包里的固定名字（原版硬编码同名）。</summary>
    public const string MetadataEntryName = "metadata.json";

    /// <summary>数据文件的统一前缀（**写出时用正斜杠**，见文件头兼容性说明）。</summary>
    public const string DataEntryPrefix = "data/";

    /// <summary>导出文件名：时间戳 + 随机码（原版 <c>Toolbox_Backup_%Y%m%d_%H%M%S_<6位hex>.zip</c>）。</summary>
    public static string UniqueFileName()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        return $"Toolbox_Backup_{stamp}_{code}.zip";
    }

    /// <summary>
    /// 从数据目录算元数据（原版语义：读 <c>tabs.json</c> 数**标签页数**与**图标数**；
    /// ⚠️ 原版只数 <c>icons</c>，**不含 <c>list_items</c>**，这里保持一致）。
    /// 读不到 / 解析失败一律按 0 计（原版是 try/except pass）。
    /// </summary>
    public static BackupMetadata BuildMetadata(string dataDirectory, string version)
    {
        int tabCount = 0;
        int iconCount = 0;

        try
        {
            var tabsFile = Path.Combine(dataDirectory, "tabs.json");
            if (File.Exists(tabsFile))
            {
                using var stream = File.OpenRead(tabsFile);
                using var document = JsonDocument.Parse(stream);

                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("tabs", out var tabs)
                    && tabs.ValueKind == JsonValueKind.Array)
                {
                    tabCount = tabs.GetArrayLength();

                    foreach (var tab in tabs.EnumerateArray())
                    {
                        if (tab.ValueKind == JsonValueKind.Object
                            && tab.TryGetProperty("icons", out var icons)
                            && icons.ValueKind == JsonValueKind.Array)
                        {
                            iconCount += icons.GetArrayLength();
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // 元数据只是"描述"，坏了不影响备份本体（与原版 try/except pass 一致）
        }

        return new BackupMetadata
        {
            Version = version,
            ExportedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.ffffff"),
            TabCount = tabCount,
            IconCount = iconCount,
        };
    }

    /// <summary>
    /// 导出：把 <paramref name="dataDirectory"/> 下**所有文件**打进 zip（元数据 + <c>data/</c> 前缀）。
    /// </summary>
    /// <param name="version">写进元数据的版本号（由调用方给，通常是程序集版本 —— Core 里不写死版本）。</param>
    public static BackupResult Export(
        string dataDirectory,
        string zipPath,
        string version,
        Action<BackupProgress>? progress = null,
        Action<string>? log = null)
    {
        try
        {
            log?.Invoke(I18n.T("backup.log.collect"));

            // ⚠️ 排除原子写盘用的临时文件（`tabs.tmp` / `config.tmp`）：
            //    它们只是保存过程中的中转产物，打进包里既是垃圾、又会让"包里没有的文件就是没有"
            //    这条导入语义变得含糊。
            var files = Directory.Exists(dataDirectory)
                ? Directory.GetFiles(dataDirectory, "*", SearchOption.AllDirectories)
                    .Where(file => !file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : Array.Empty<string>();

            var total = files.Length + 1;   // 元数据占一项（与原版 total = len(files) + 1 一致）

            log?.Invoke(I18n.T("backup.log.create_zip", ("name", Path.GetFileName(zipPath))));

            var directory = Path.GetDirectoryName(Path.GetFullPath(zipPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // ★ 先写 `<zip>.part`、全部成功后再改名成最终 zip：
            //   否则中途失败会**留下一个半截 zip**，而用户会以为"我导出过了" ——
            //   那个半截包正是导入时最危险的输入（解压到一半就坏）。
            var partPath = zipPath + ".part";
            TryDeleteFile(partPath);

            try
            {
                // ⚠️ 用 FileStream(FileMode.Create) 而不是 `ZipFile.Open(path, Create)`：
                //    后者内部是 **FileMode.CreateNew**，目标文件已存在会抛
                //    "The file '...' already exists."（实测踩到）；原版 Python 的
                //    `zipfile.ZipFile(path, "w")` 是**覆盖**语义，这里必须对齐。
                using (var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
                {
                    // ① 元数据
                    log?.Invoke(I18n.T("backup.log.write_metadata"));
                    var metadataEntry = archive.CreateEntry(MetadataEntryName, CompressionLevel.Optimal);
                    using (var stream = metadataEntry.Open())
                    using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                    {
                        writer.Write(BuildMetadata(dataDirectory, version).ToJson());
                    }

                    progress?.Invoke(new BackupProgress(1, total));

                    // ② 数据文件（**正斜杠**前缀，见文件头兼容性 ①）
                    var index = 1;
                    foreach (var file in files)
                    {
                        var relative = Path.GetRelativePath(dataDirectory, file).Replace('\\', '/');
                        log?.Invoke(I18n.T("backup.log.compress", ("name", relative)));

                        archive.CreateEntryFromFile(file, DataEntryPrefix + relative, CompressionLevel.Optimal);

                        index++;
                        progress?.Invoke(new BackupProgress(index, total));
                    }
                }

                File.Move(partPath, zipPath, overwrite: true);
            }
            catch
            {
                TryDeleteFile(partPath);   // 失败不留半截包
                throw;
            }

            var sizeKb = new FileInfo(zipPath).Length / 1024.0;
            log?.Invoke(I18n.T("backup.log.exported",
                ("size", sizeKb.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))));

            return BackupResult.Ok(Path.GetFullPath(zipPath));
        }
        catch (Exception ex)
        {
            return BackupResult.Fail(ex.Message);
        }
    }

    private static void TryDeleteFile(string path)
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
            // 删不掉不影响导出结论
        }
    }

    /// <summary>
    /// 导入：把包内容**先解到暂存区**，全部落定、确认可用之后，才清空当前数据并搬进来。
    ///
    /// <para>⚠️ 为什么不是"先清空再解压"（原版 Python 的顺序）：那样只要解压中途出一点问题
    /// （坏包 / 某条目标文件被占用 / 磁盘满 / 路径过长），异常就被下面的 catch 吞成一句
    /// `Fail(ex.Message)` —— 而此刻 `icons/` 已经空了、`tabs.json` 也不在了，**没有任何回滚**，
    /// 用户看到的是"导入失败"外加"图标全没了"。`MainWindow` 那边的注释把
    /// "失败时 Core 保证一个字节都不改"写成了承诺，那就得真的做到。</para>
    ///
    /// <para>与原版一致的语义**没有变**：包里没有的文件，导入后就是没有（既不保留旧的，
    /// 也不会凭空补出来）—— 变的只是"什么时候动手删"，而不是删什么。</para>
    /// </summary>
    public static BackupResult Import(
        string zipPath,
        string dataDirectory,
        Action<BackupProgress>? progress = null,
        Action<string>? log = null)
    {
        if (!File.Exists(zipPath))
        {
            return BackupResult.Fail(I18n.T("backup.error.not_found"));   // 原版文案：File not found
        }

        try
        {
            log?.Invoke(I18n.T("backup.log.open_zip", ("name", Path.GetFileName(zipPath))));

            using var archive = ZipFile.OpenRead(zipPath);

            // ① 必须有根目录的 metadata.json（原版硬要求）
            var hasMetadata = archive.Entries.Any(e =>
                string.Equals(e.FullName, MetadataEntryName, StringComparison.Ordinal));

            if (!hasMetadata)
            {
                return BackupResult.Fail(I18n.T("backup.error.no_metadata"));
            }

            log?.Invoke(I18n.T("backup.log.read_metadata"));
            BackupMetadata? metadata;
            using (var stream = archive.GetEntry(MetadataEntryName)!.Open())
            using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
            {
                metadata = BackupMetadata.TryParse(reader.ReadToEnd());
            }

            if (metadata is null)
            {
                return BackupResult.Fail(I18n.T("backup.error.bad_metadata"));
            }

            log?.Invoke("  " + I18n.T("backup.log.meta_version",
                ("version", string.IsNullOrWhiteSpace(metadata.Version) ? "?" : metadata.Version)));
            log?.Invoke("  " + I18n.T("backup.log.meta_exported",
                ("time", string.IsNullOrWhiteSpace(metadata.ExportedAt) ? "?" : metadata.ExportedAt)));
            log?.Invoke("  " + I18n.T("backup.log.meta_counts",
                ("tabs", metadata.TabCount), ("icons", metadata.IconCount)));

            // ② 解压到**暂存目录**（★ 关键：这一步在清空数据**之前**，见方法头注释）
            var staging = CreateStagingDirectory();
            try
            {
                var total = archive.Entries.Count;
                var index = 0;

                foreach (var entry in archive.Entries)
                {
                    index++;

                    if (string.Equals(entry.FullName, MetadataEntryName, StringComparison.Ordinal))
                    {
                        progress?.Invoke(new BackupProgress(index, total));
                        continue;
                    }

                    var relative = NormalizeEntryName(entry.FullName);
                    if (relative.Length == 0)
                    {
                        progress?.Invoke(new BackupProgress(index, total));
                        continue;
                    }

                    var destination = Path.GetFullPath(Path.Combine(staging, relative));

                    // ⚠️ 安全线（原版没有）：拒绝写到暂存目录之外（zip-slip）
                    if (!IsInside(staging, destination))
                    {
                        log?.Invoke(I18n.T("backup.log.skip_unsafe", ("name", entry.FullName)));
                        progress?.Invoke(new BackupProgress(index, total));
                        continue;
                    }

                    var parent = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    log?.Invoke(I18n.T("backup.log.extract", ("name", relative)));
                    entry.ExtractToFile(destination, overwrite: true);

                    progress?.Invoke(new BackupProgress(index, total));
                }

                // ③ 暂存区落定之后，才动用户的数据：清空 → 搬进来
                log?.Invoke(I18n.T("backup.log.clear"));
                ClearCurrentData(dataDirectory);

                Directory.CreateDirectory(Path.GetFullPath(dataDirectory));
                MoveDirectoryContents(staging, Path.GetFullPath(dataDirectory));

                log?.Invoke(I18n.T("backup.log.imported"));
                return BackupResult.Ok(Path.GetFullPath(zipPath), metadata);
            }
            finally
            {
                // 暂存目录一定要清掉（成功时它已经被搬空，失败时里面是半成品）
                DeleteDirectoryQuietly(staging);
            }
        }
        catch (Exception ex)
        {
            return BackupResult.Fail(ex.Message);
        }
    }

    // ────────────────────────────── 导入的暂存区 ──────────────────────────────

    /// <summary>
    /// 造一个全新的暂存目录。
    ///
    /// <para>放在 `%TEMP%` 下而不是数据目录里：数据目录里多一个子目录会被
    /// <see cref="Export"/> 的"整目录打包"顺手收进去（变成包里的垃圾条目）。
    /// 跨卷时 <see cref="File.Move(string, string, bool)"/> 会自己退化成"复制 + 删除"，
    /// 所以放哪里都正确，只是同卷更快。</para>
    /// </summary>
    private static string CreateStagingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "toolboxpanel-import", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>把 <paramref name="source"/> 下的内容逐个搬进 <paramref name="destination"/>（保留子目录结构）。</summary>
    private static void MoveDirectoryContents(string source, string destination)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            // 同卷时这是改名（原子）；跨卷时 .NET 会自己退化成"复制 + 删除"
            File.Move(file, target, overwrite: true);
        }
    }

    private static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删不掉不影响导入结论
        }
    }

    /// <summary>
    /// 把包内条目名规范化成"相对数据目录的路径"：
    /// 去掉 <c>data/</c> 或 <c>data\</c> 前缀（兼容**原版在 Windows 上写出的反斜杠包**），
    /// 其余情况按裸文件名处理（原版两条分支都支持）。
    /// </summary>
    internal static string NormalizeEntryName(string entryName)
    {
        var name = entryName.Replace('\\', '/').TrimStart('/');

        if (name.StartsWith(DataEntryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[DataEntryPrefix.Length..];
        }

        // 目录条目（以 / 结尾）与空名直接跳过
        return name.EndsWith('/') ? string.Empty : name;
    }

    /// <summary>目标路径是否落在数据目录内（zip-slip 判据）。</summary>
    private static bool IsInside(string root, string candidate)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void ClearCurrentData(string dataDirectory)
    {
        var iconsDirectory = Path.Combine(dataDirectory, "icons");
        if (Directory.Exists(iconsDirectory))
        {
            foreach (var file in Directory.GetFiles(iconsDirectory))
            {
                TryDelete(file);
            }
        }

        TryDelete(Path.Combine(dataDirectory, "tabs.json"));
        TryDelete(Path.Combine(dataDirectory, "config.json"));
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
        catch (Exception)
        {
            // 原版也是 `except (OSError, PermissionError): pass` —— 删不掉就交给后面的覆盖写
        }
    }
}
