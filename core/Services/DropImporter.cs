// DropImporter.cs —— 「从资源管理器拖入文件 / 文件夹 / 快捷方式 → 自动创建图标」的判定逻辑
//
// 基准 = 原版 v1.11.6 的 src/toolbox/tab_widget.py::_add_dropped_paths：
//
//   · 路径不存在（空路径也算）→ status.path_not_found（"路径不存在: {path}"）并跳过；
//   · 该页已有 source_path 相同的图标 → status.already_exists（"已存在: {name}"）并跳过；
//     （原版是随加随查，所以**同一次拖入里出现两次的同一路径，第二次也算重复**）
//   · `.lnk`（不分大小写）→ SHORTCUT：解析出目标 / 参数 / 工作目录，
//     目标解析不到就退回 .lnk 自己；名字取文件主干（不含 .lnk）
//   · 目录 → FOLDER：名字取目录名本身；source_path = target_path = 解析后的绝对路径
//   · 其余文件 → FILE：名字取文件主干（不含扩展名）；source_path = target_path = 绝对路径
//
// 为什么放 Core：分类 / 命名 / 去重判断 / 状态文案全是**只读磁盘的纯判定**，天生可单测。
// 落库与图标提取由调用方按结果执行（本项目里是 MainViewModel.AddDroppedPaths）。
//
// ⚠️ 与原版的一处**有意差异**：去重比较用**忽略大小写**（Windows 路径不区分大小写），
//    原版是 Python 的字符串 `==`（区分大小写）。宁可不重复添加，也不要"同一文件在大小写不同时重复建图标"。

using ToolboxPanel.Core.Models;

namespace ToolboxPanel.Core.Services;

/// <summary>一个拖入路径的处理结论。</summary>
public enum DropDecisionKind
{
    /// <summary>可以新建图标。</summary>
    Add,

    /// <summary>该标签页里已经有同一个源路径的图标。</summary>
    Duplicate,

    /// <summary>路径不存在（或空路径）。</summary>
    Missing,
}

/// <summary>一个拖入路径的完整判定结果（含给状态栏的文案，与原版 i18n 一致）。</summary>
public sealed record DropDecision(
    DropDecisionKind Kind,
    string SourcePath,
    IconType Type,
    string DisplayName,
    string TargetPath,
    string Arguments,
    string WorkingDir,
    string Message);

/// <summary>拖入路径的判定与成图（不落库、不提图标 —— 那是调用方的事）。</summary>
public static class DropImporter
{
    /// <summary>添加成功的状态文案（原版 <c>status.added</c>）。</summary>
    public static string AddedMessage(string name) => I18n.T("status.added", ("name", name));

    /// <summary>已存在的状态文案（原版 <c>status.already_exists</c>）。</summary>
    public static string AlreadyExistsMessage(string name) => I18n.T("status.already_exists", ("name", name));

    /// <summary>路径不存在的状态文案（原版 <c>status.path_not_found</c>，与右键菜单那条同一个键）。</summary>
    public static string PathNotFoundMessage(string? path) => I18n.T("status.path_not_found", ("path", path));

    /// <summary>
    /// 批量判定（原版 <c>_add_dropped_paths</c> 的循环）：
    /// 已存在的源路径集合会**随判定结果增长** —— 同一次拖入里重复的路径，第二个也算重复。
    /// </summary>
    public static IReadOnlyList<DropDecision> Plan(
        IEnumerable<string> paths,
        IEnumerable<string> existingSourcePaths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existingSourcePaths is not null)
        {
            foreach (var path in existingSourcePaths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    existing.Add(path.Trim());
                }
            }
        }

        var decisions = new List<DropDecision>();

        foreach (var path in paths)
        {
            var decision = Classify(path, existing);
            decisions.Add(decision);

            if (decision.Kind == DropDecisionKind.Add)
            {
                existing.Add(decision.SourcePath);
            }
        }

        return decisions;
    }

    /// <summary>判定单个路径（只读磁盘，无副作用）。</summary>
    public static DropDecision Classify(string? rawPath, IReadOnlySet<string>? existingSourcePaths = null)
    {
        var raw = (rawPath ?? string.Empty).Trim();

        if (raw.Length == 0)
        {
            return Missing(raw);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(raw);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Missing(raw);
        }

        bool isDirectory = Directory.Exists(fullPath);
        if (!isDirectory && !File.Exists(fullPath))
        {
            // 原版用"用户给的原始字符串"报错（不是解析后的），这里保持一致
            return Missing(raw);
        }

        // 重复判定与报错都用"文件名（含扩展名）" —— 原版也是 path.name
        var fileName = SafeFileName(fullPath);

        if (existingSourcePaths is not null && existingSourcePaths.Contains(fullPath))
        {
            return new DropDecision(
                DropDecisionKind.Duplicate, fullPath, IconType.File, fileName,
                fullPath, string.Empty, string.Empty, AlreadyExistsMessage(fileName));
        }

        if (!isDirectory && fullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            // 快捷方式：解析出目标 / 参数 / 工作目录（解析失败就退回 .lnk 自己，与原版一致）
            var shortcut = WindowsShortcut.Resolve(fullPath);
            var target = string.IsNullOrWhiteSpace(shortcut?.TargetPath) ? fullPath : shortcut!.TargetPath;
            var name = Path.GetFileNameWithoutExtension(fullPath);

            return new DropDecision(
                DropDecisionKind.Add, fullPath, IconType.Shortcut, name,
                target, shortcut?.Arguments ?? string.Empty, shortcut?.WorkingDirectory ?? string.Empty,
                AddedMessage(name));
        }

        if (isDirectory)
        {
            // 目录：名字就是目录名（不取主干 —— 目录名里的点号是名字的一部分）
            return new DropDecision(
                DropDecisionKind.Add, fullPath, IconType.Folder, fileName,
                fullPath, string.Empty, string.Empty, AddedMessage(fileName));
        }

        var fileStem = Path.GetFileNameWithoutExtension(fullPath);
        var displayName = string.IsNullOrEmpty(fileStem) ? fileName : fileStem;

        return new DropDecision(
            DropDecisionKind.Add, fullPath, IconType.File, displayName,
            fullPath, string.Empty, string.Empty, AddedMessage(displayName));
    }

    /// <summary>
    /// 按判定结果造出要落库的图标模型（<see cref="DropDecisionKind.Add"/> 之外一律返回 null）。
    /// ⚠️ 不设 <see cref="IconModel.IconCacheFile"/>：缓存由调用方按"按路径提取"再填（与原版同序）。
    /// </summary>
    public static IconModel? CreateIcon(DropDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.Kind != DropDecisionKind.Add)
        {
            return null;
        }

        return new IconModel
        {
            Type = decision.Type,
            DisplayName = decision.DisplayName,
            SourcePath = decision.SourcePath,
            TargetPath = decision.TargetPath,
            Arguments = decision.Arguments,
            WorkingDir = decision.WorkingDir,
        };
    }

    private static DropDecision Missing(string path)
        => new(DropDecisionKind.Missing, path, IconType.File, string.Empty,
               string.Empty, string.Empty, string.Empty, PathNotFoundMessage(path));

    private static string SafeFileName(string fullPath)
    {
        try
        {
            var name = Path.GetFileName(fullPath.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? fullPath : name;
        }
        catch (ArgumentException)
        {
            return fullPath;
        }
    }
}
