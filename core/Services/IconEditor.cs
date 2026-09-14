// IconEditor.cs —— ToolboxPanel v2 · W5 逻辑层：新建 / 编辑图标的字段语义（纯逻辑、可单测）
//
// 对应原 Python（v1.11.6）的两处，行为逐条对齐：
//   · src/toolbox/icon_edit_dialog.py::apply()
//       —— 按类型做「必填校验 + 字段归一化」（名称必填；创建模式还要路径/网址/命令必填）
//   · src/toolbox/tab_widget.py::_create_*_icon / src/toolbox/icon_grid.py::_edit_icon
//       —— 保存之后「要不要重新提取图标、从哪里提取」
//
// 为什么把这两件事搬进 Core：
//   1. 它们全是**纯逻辑**（字符串归一化、必填校验、按类型决定字段组合），天生可单测；
//   2. 界面上「确定」按下的那一刻就要知道"能不能存、存完图标怎么刷新"。
//      判断留在 Core，UI 只负责读字段 + 执行 <see cref="IconRefreshPlan"/>。
//
// ⚠️ 与原版的三处**有意差异**（都有注释与单测）：
//   1. URL 协议判断**忽略大小写**：Python 的 startswith 区分大小写，
//      `HTTPS://x` 会被补成 `https://HTTPS://x`（原版的小毛病）；这里刻意更宽容。
//   2. 错误消息用**中文常量**（原版是 i18n key）。WinUI 线的 i18n 还没搬，
//      等 i18n 到位后把常量值换成 key 即可 —— UI 读的是同一个字段，不用改结构。
//   3. 图标刷新是**显式返回的计划**（原版是散在 UI 里的 if）：行为一致，只是收口。

using ToolboxPanel.Core.Models;

namespace ToolboxPanel.Core.Services;

/// <summary>图标保存之后，界面该怎么刷新它的图标缓存（对应原版那些 <c>reextract</c> 判断）。</summary>
public enum IconRefreshKind
{
    /// <summary>不用动（编辑 URL / COMMAND；或文件路径没变）。</summary>
    None,

    /// <summary>按 <see cref="IconRefreshPlan.SourcePath"/> 重新提取系统图标（文件/文件夹/快捷方式）。</summary>
    ReextractFromSource,

    /// <summary>从「文件 + 索引」取自定义图标（快捷方式的自定义图标）。</summary>
    CustomIndex,

    /// <summary>用该类型的标准兜底图标（新建 URL / COMMAND 时原版就是这么做的）。</summary>
    StandardFallback,
}

/// <summary>「这个图标该怎么刷新」的完整描述；界面照着它调 <see cref="IconExtractor"/> 即可。</summary>
public sealed class IconRefreshPlan
{
    /// <summary>什么都不用做。</summary>
    public static readonly IconRefreshPlan None =
        new(IconRefreshKind.None, string.Empty, string.Empty, 0);

    /// <summary>用该类型的标准图标（不按路径提取）。</summary>
    public static readonly IconRefreshPlan Standard =
        new(IconRefreshKind.StandardFallback, string.Empty, string.Empty, 0);

    private IconRefreshPlan(IconRefreshKind kind, string sourcePath, string iconFile, int iconIndex)
    {
        Kind = kind;
        SourcePath = sourcePath;
        IconFile = iconFile;
        IconIndex = iconIndex;
    }

    public IconRefreshKind Kind { get; }

    /// <summary><see cref="IconRefreshKind.ReextractFromSource"/> 时要提取的路径。</summary>
    public string SourcePath { get; }

    /// <summary><see cref="IconRefreshKind.CustomIndex"/> 时的图标文件（exe/dll/ico）。</summary>
    public string IconFile { get; }

    /// <summary><see cref="IconRefreshKind.CustomIndex"/> 时的图标索引。</summary>
    public int IconIndex { get; }

    public static IconRefreshPlan FromSource(string path)
        => new(IconRefreshKind.ReextractFromSource, path ?? string.Empty, string.Empty, 0);

    public static IconRefreshPlan CustomIcon(string file, int index)
        => new(IconRefreshKind.CustomIndex, string.Empty, file ?? string.Empty, index);
}

/// <summary>
/// 对话框里那份字段草稿（UI 无关的纯数据）。
/// 「新建」与「编辑」共用：界面上显示哪些字段由 <see cref="Type"/> 决定。
/// </summary>
public sealed class IconEditDraft
{
    /// <summary>图标类型（编辑模式下以模型上的类型为准，草稿里的会被忽略）。</summary>
    public IconType Type { get; set; } = IconType.File;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 主路径输入框的值，含义随类型变化：
    /// 文件/文件夹 = 路径；快捷方式 = .lnk 的目标路径；网址 = 网址；命令 = 可执行文件。
    /// </summary>
    public string Path { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public string WorkingDir { get; set; } = string.Empty;

    /// <summary>快捷方式描述。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>快捷方式的自定义图标文件（可选）。</summary>
    public string CustomIconPath { get; set; } = string.Empty;

    /// <summary>快捷方式自定义图标的索引。</summary>
    public int CustomIconIndex { get; set; }

    /// <summary>
    /// 新建快捷方式时，用户在文件选择框里选中的那个 **.lnk 本身的路径**。
    ///
    /// <para>对应原版 <c>create_for_type(..., source_path=...)</c> 的 prefill：
    /// 对话框里那个「路径」框显示/编辑的是**目标**（target_path），而 .lnk 自己记在 source_path
    /// —— 原版启动 .lnk 时优先用它（见 <c>Launcher.OpenShortcut</c>）。</para>
    /// </summary>
    public string ShortcutSourcePath { get; set; } = string.Empty;

    /// <summary>
    /// 是否填了自定义图标文件。
    /// ⚠️ 这里**只判"填没填"**，是否真的用它由 <see cref="IconEditor"/> 按**生效的类型**决定
    /// （编辑模式下类型以模型为准，草稿里的 <see cref="Type"/> 会被忽略 —— 早先按草稿类型判断，
    /// 结果是"编辑一个快捷方式、明明填了自定义图标却被当成没填"，被单测抓出来过）。
    /// </summary>
    public bool HasCustomIcon => !string.IsNullOrWhiteSpace(CustomIconPath);
}

/// <summary>一次「新建 / 编辑」的结论：成功则带上模型与刷新计划，失败带上给用户看的消息。</summary>
public sealed class IconEditResult
{
    private IconEditResult(bool success, string? errorMessage, IconModel? icon, IconRefreshPlan refresh)
    {
        Success = success;
        ErrorMessage = errorMessage;
        Icon = icon;
        Refresh = refresh;
    }

    public bool Success { get; }

    /// <summary>失败原因（中文；原版这里是 i18n key）。</summary>
    public string? ErrorMessage { get; }

    /// <summary>成功时的图标（新建 = 新对象；编辑 = 被就地改过的那个模型）。</summary>
    public IconModel? Icon { get; }

    /// <summary>成功时界面该执行的图标刷新动作。</summary>
    public IconRefreshPlan Refresh { get; } = IconRefreshPlan.None;

    public static IconEditResult Fail(string errorMessage)
        => new(false, errorMessage, null, IconRefreshPlan.None);

    public static IconEditResult Ok(IconModel icon, IconRefreshPlan refresh)
        => new(true, null, icon, refresh);
}

/// <summary>新建 / 编辑图标的字段语义。<b>只做校验与字段写入，不碰磁盘</b>（落库由调用方做）。</summary>
public static class IconEditor
{
    // 错误消息（对应原版 i18n 的 validate.* 四条）
    public const string ErrorNameRequired = "名称不能为空";
    public const string ErrorPathRequired = "路径不能为空";
    public const string ErrorUrlRequired = "网址不能为空";
    public const string ErrorCommandRequired = "命令不能为空";

    /// <summary>
    /// 新建（原版 <c>IconEditDialog.create_for_type</c> + <c>apply(creation_mode=True)</c>）：
    /// **名称 + 对应类型的主字段**都必填。
    /// </summary>
    public static IconEditResult Create(IconEditDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var name = Trim(draft.DisplayName);
        if (name.Length == 0)
        {
            return IconEditResult.Fail(ErrorNameRequired);
        }

        var icon = new IconModel { Type = draft.Type, DisplayName = name };
        var raw = Trim(draft.Path);

        switch (draft.Type)
        {
            case IconType.File:
            case IconType.Folder:
                if (raw.Length == 0)
                {
                    return IconEditResult.Fail(ErrorPathRequired);
                }

                icon.SourcePath = raw;
                icon.TargetPath = raw;
                return IconEditResult.Ok(icon, IconRefreshPlan.FromSource(raw));

            case IconType.Shortcut:
                if (raw.Length == 0)
                {
                    return IconEditResult.Fail(ErrorPathRequired);
                }

                // ⚠️ 与原版一致：这个框里填的是**目标**；.lnk 自己的路径来自文件选择框的 prefill
                icon.TargetPath = raw;
                icon.SourcePath = Trim(draft.ShortcutSourcePath);
                icon.Arguments = Trim(draft.Arguments);
                icon.WorkingDir = Trim(draft.WorkingDir);
                icon.Description = Trim(draft.Description);

                // 原版：给了自定义图标就用它；没给就按 .lnk 路径提取（shell 自己会解析 .lnk）
                return IconEditResult.Ok(icon, draft.HasCustomIcon
                    ? IconRefreshPlan.CustomIcon(Trim(draft.CustomIconPath), draft.CustomIconIndex)
                    : IconRefreshPlan.FromSource(icon.SourcePath));
            case IconType.Url:
                if (raw.Length == 0)
                {
                    return IconEditResult.Fail(ErrorUrlRequired);
                }

                var url = NormalizeUrl(raw);
                icon.SourcePath = url;
                icon.TargetPath = url;
                return IconEditResult.Ok(icon, IconRefreshPlan.Standard);

            case IconType.Command:
                if (raw.Length == 0)
                {
                    return IconEditResult.Fail(ErrorCommandRequired);
                }

                icon.TargetPath = raw;
                icon.Arguments = Trim(draft.Arguments);
                icon.WorkingDir = Trim(draft.WorkingDir);

                // 原版：命令类型的 source_path 存「命令 + 参数」整串（启动时仍用 target/arguments）
                icon.SourcePath = JoinCommand(raw, icon.Arguments);
                return IconEditResult.Ok(icon, IconRefreshPlan.Standard);

            default:
                return IconEditResult.Fail($"未知的图标类型: {draft.Type}");
        }
    }

    /// <summary>
    /// 编辑（原版 <c>IconEditDialog.apply()</c>，非创建模式）：<b>只有名称必填</b>；
    /// 主字段留空 = 保持原样（但快捷方式的参数/工作目录/描述、命令的参数/工作目录照常写入 —— 与原版一致）。
    ///
    /// <para>⚠️ **类型不可改**：编辑对话框不给改类型（原版也一样），这里以 <paramref name="icon"/> 上的类型为准，
    /// 草稿里的 <see cref="IconEditDraft.Type"/> 被忽略。</para>
    /// </summary>
    public static IconEditResult Edit(IconModel icon, IconEditDraft draft)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(draft);

        var name = Trim(draft.DisplayName);
        if (name.Length == 0)
        {
            return IconEditResult.Fail(ErrorNameRequired);
        }

        // 原版在弹对话框**之前**记下 old_path，改完再比 —— 这里同样在改动前取
        var oldPath = PreferTarget(icon);
        var raw = Trim(draft.Path);

        icon.DisplayName = name;

        switch (icon.Type)
        {
            case IconType.File:
            case IconType.Folder:
                if (raw.Length > 0)
                {
                    icon.SourcePath = raw;
                    icon.TargetPath = raw;
                }

                var newPath = PreferTarget(icon);
                return IconEditResult.Ok(
                    icon,
                    raw.Length > 0 && newPath != oldPath
                        ? IconRefreshPlan.FromSource(newPath)
                        : IconRefreshPlan.None);

            case IconType.Shortcut:
                if (raw.Length > 0)
                {
                    icon.TargetPath = raw;
                }

                icon.Arguments = Trim(draft.Arguments);
                icon.WorkingDir = Trim(draft.WorkingDir);
                icon.Description = Trim(draft.Description);

                // 原版：编辑快捷方式时，只要有自定义图标就用它；否则**总是**重新提取一次
                return IconEditResult.Ok(icon, draft.HasCustomIcon
                    ? IconRefreshPlan.CustomIcon(Trim(draft.CustomIconPath), draft.CustomIconIndex)
                    : IconRefreshPlan.FromSource(PreferTarget(icon)));

            case IconType.Url:
                if (raw.Length > 0)
                {
                    var url = NormalizeUrl(raw);
                    icon.SourcePath = url;
                    icon.TargetPath = url;
                }

                // 原版编辑 URL 时不重新提取（图标是类型标准图）
                return IconEditResult.Ok(icon, IconRefreshPlan.None);

            case IconType.Command:
                if (raw.Length > 0)
                {
                    icon.TargetPath = raw;
                    icon.SourcePath = JoinCommand(raw, Trim(draft.Arguments));
                }

                icon.Arguments = Trim(draft.Arguments);
                icon.WorkingDir = Trim(draft.WorkingDir);
                return IconEditResult.Ok(icon, IconRefreshPlan.None);

            default:
                return IconEditResult.Fail($"未知的图标类型: {icon.Type}");
        }
    }

    /// <summary>
    /// 网址归一化：没写协议就补 <c>https://</c>（原版 <c>icon_edit_dialog.apply()</c> 的 URL 分支）。
    /// ⚠️ 与 <see cref="Launcher.OpenUrl"/> 的判断保持同一口径（忽略大小写），两处别改歪。
    /// </summary>
    public static string NormalizeUrl(string? raw)
    {
        var url = Trim(raw);
        if (url.Length == 0)
        {
            return url;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return "https://" + url;
    }

    /// <summary>「命令 + 参数」拼成一串（原版就是这个写法，空参数时不留尾空格）。</summary>
    private static string JoinCommand(string command, string arguments)
        => arguments.Length == 0 ? command : $"{command} {arguments}";

    /// <summary>文件/文件夹/快捷方式取「目标优先」的那条路径（原版 <c>target_path or source_path</c>）。</summary>
    private static string PreferTarget(IconModel icon)
        => string.IsNullOrWhiteSpace(icon.TargetPath) ? icon.SourcePath : icon.TargetPath;

    private static string Trim(string? value) => (value ?? string.Empty).Trim();
}
