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

            var files = Directory.Exists(dataDirectory)
                ? Directory.GetFiles(dataDirectory, "*", SearchOption.AllDirectories)
                : Array.Empty<string>();

            var total = files.Length + 1;   // 元数据占一项（与原版 total = len(files) + 1 一致）

            log?.Invoke(I18n.T("backup.log.create_zip", ("name", Path.GetFileName(zipPath))));

            var directory = Path.GetDirectoryName(Path.GetFullPath(zipPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // ⚠️ 用 FileStream(FileMode.Create) 而不是 `ZipFile.Open(path, Create)`：
            //    后者内部是 **FileMode.CreateNew**，目标文件已存在会抛
            //    "The file '...' already exists."（实测踩到）；原版 Python 的
            //    `zipfile.ZipFile(path, "w")` 是**覆盖**语义，这里必须对齐。
            using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
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

    /// <summary>
    /// 导入：**先清空当前数据**（icons 目录内容 + tabs.json + config.json），再按包内容落盘。
    /// ⚠️ 与导入前一致的地方：原版也是"先删这三个、再解压"，所以**包里没有的文件就是没有**。
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

            // ② 清空当前数据（与原版同一套：icons 目录内容 + tabs.json + config.json）
            log?.Invoke(I18n.T("backup.log.clear"));
            ClearCurrentData(dataDirectory);

            // ③ 解压（跳过 metadata.json）
            var total = archive.Entries.Count;
            var root = Path.GetFullPath(dataDirectory);
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

                var destination = Path.GetFullPath(Path.Combine(root, relative));

                // ⚠️ 安全线（原版没有）：拒绝写到数据目录之外（zip-slip）
                if (!IsInside(root, destination))
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

            log?.Invoke(I18n.T("backup.log.imported"));
            return BackupResult.Ok(Path.GetFullPath(zipPath), metadata);
        }
        catch (Exception ex)
        {
            return BackupResult.Fail(ex.Message);
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
