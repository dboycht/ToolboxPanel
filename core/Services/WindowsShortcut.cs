// WindowsShortcut.cs —— ToolboxPanel v2 · W2 系统能力
//
// 对应原 Python 的 src/toolbox/utils/windows_shortcut.py::resolve_shortcut()。
// 返回字段与它保持一致（target_path / arguments / working_dir / icon_location / description），
// 另外**多给**了 IconPath 与 IconIndex 两段（W2 的图标提取要用「文件 + 索引」去 ExtractIconEx）。

using System.Runtime.InteropServices;
using System.Text;

namespace ToolboxPanel.Core.Services;

/// <summary>一个 .lnk 的解析结果。</summary>
/// <param name="TargetPath">快捷方式的目标（原样返回 .lnk 里存的值，不做环境变量展开）。</param>
/// <param name="Arguments">命令行参数。</param>
/// <param name="WorkingDirectory">工作目录。</param>
/// <param name="IconPath">自定义图标所在的文件（exe/dll/ico），没有则为空串。</param>
/// <param name="IconIndex">图标在上述文件中的索引。</param>
/// <param name="IconLocation">兼容原版 pywin32 的写法：<c>"文件,索引"</c>（无自定义图标时为空串）。</param>
/// <param name="Description">描述文本。</param>
public sealed record ShortcutInfo(
    string TargetPath,
    string Arguments,
    string WorkingDirectory,
    string IconPath,
    int IconIndex,
    string IconLocation,
    string Description);

/// <summary>.lnk 解析（只读）。</summary>
public static class WindowsShortcut
{
    /// <summary>
    /// 解析 .lnk；任何失败（文件不存在、不是快捷方式、COM 出错）都返回 null，
    /// 与原版 <c>resolve_shortcut()</c> 的「出错就返回 None」一致。
    /// </summary>
    public static ShortcutInfo? Resolve(string? lnkPath)
        => TryResolve(lnkPath, out _);

    /// <summary>解析 .lnk，并把失败原因带出来（便于日志/诊断）。</summary>
    public static ShortcutInfo? TryResolve(string? lnkPath, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(lnkPath))
        {
            error = I18n.T("status.path_empty");
            return null;
        }

        if (!File.Exists(lnkPath))
        {
            error = I18n.T("status.file_not_found");
            return null;
        }

        object? comObject = null;
        try
        {
            comObject = new ShellLinkCoClass();
            var shellLink = (IShellLinkW)comObject;
            var persistFile = (IPersistFile)comObject;

            // STGM_READ = 0
            persistFile.Load(lnkPath, 0);

            // 原生接口要求调用方给缓冲区，字符串由 COM 填进来
            var targetBuffer = new StringBuilder(ShellLinkLimits.MaxPath);
            shellLink.GetPath(targetBuffer, targetBuffer.Capacity, IntPtr.Zero, 0);

            var argumentsBuffer = new StringBuilder(ShellLinkLimits.MaxArgs);
            shellLink.GetArguments(argumentsBuffer, argumentsBuffer.Capacity);

            var workingDirBuffer = new StringBuilder(ShellLinkLimits.MaxPath);
            shellLink.GetWorkingDirectory(workingDirBuffer, workingDirBuffer.Capacity);

            var descriptionBuffer = new StringBuilder(ShellLinkLimits.MaxDescription);
            shellLink.GetDescription(descriptionBuffer, descriptionBuffer.Capacity);

            var iconBuffer = new StringBuilder(ShellLinkLimits.MaxPath);
            shellLink.GetIconLocation(iconBuffer, iconBuffer.Capacity, out int iconIndex);

            var iconPath = iconBuffer.ToString();
            var iconLocation = string.IsNullOrEmpty(iconPath) ? string.Empty : $"{iconPath},{iconIndex}";

            return new ShortcutInfo(
                TargetPath: targetBuffer.ToString(),
                Arguments: argumentsBuffer.ToString(),
                WorkingDirectory: workingDirBuffer.ToString(),
                IconPath: iconPath,
                IconIndex: iconIndex,
                IconLocation: iconLocation,
                Description: descriptionBuffer.ToString());
        }
        catch (Exception ex)
        {
            // COMException / FileNotFoundException 等一律按「解析失败」处理
            error = ex.Message;
            return null;
        }
        finally
        {
            ReleaseComObjectQuietly(comObject);
        }
    }

    private static void ReleaseComObjectQuietly(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(comObject))
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException)
        {
            // RCW 已释放 / 未启用内置 COM 互操作：忽略，交给 GC
        }
    }
}
