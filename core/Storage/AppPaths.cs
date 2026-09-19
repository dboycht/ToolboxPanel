// AppPaths.cs —— ToolboxPanel v2 数据层（C# 重写，W1）
//
// 数据目录定位。对应原 Python 的 get_data_dir()（打包后取 exe 同级 data/，开发时取项目根 data/）。
//
// 查找顺序：
//   1) 环境变量 TOOLBOXPANEL_DATA_DIR（绝对路径，或相对当前工作目录）—— 测试/多档案用；
//   2) 从起始目录（默认 exe 所在目录）逐级向上，找到「项目根」→ 用它的 data/   —— 开发期用；
//   3) 兜底：<起始目录>/data                                      —— 发布后的 exe 同级 data/。
//
// 「项目根」判据（两者之一）：
//   · 该目录下有 data/tabs.json（开发副本已有数据时）
//   · 该目录下同时有 winui/ToolboxPanel.WinUI.csproj 与 src/toolbox/（首次运行、还没有数据文件时）

namespace ToolboxPanel.Core.Storage;

public static class AppPaths
{
    /// <summary>覆盖数据目录的环境变量名。</summary>
    public const string DataDirEnvironmentVariable = "TOOLBOXPANEL_DATA_DIR";

    /// <summary>向上查找的最大层数（exe 在 bin\x64\Debug\net9.0-...\win-x64\ 下约 6 层）。</summary>
    private const int MaxUpwardLevels = 12;

    /// <summary>
    /// 解析数据目录（不保证已存在；由 <see cref="DataStore"/> 负责创建）。
    ///
    /// <para>⚠️ 环境变量 <c>TOOLBOXPANEL_DATA_DIR</c> 的值是**外部输入**，可能含非法字符
    /// （<c>&lt; &gt; | " :</c> 或老式 <c>C:xxx</c> 形式）。这类值必须**明确报错**而不是静默忽略：
    /// 静默落下会变成"用户以为指定了数据目录、程序却写到了别处"，那是最难排查的一类问题。
    /// 这里把底层异常换成一句能直接照做的提示（带上那个坏值），再由启动路径记进崩溃日志。</para>
    /// </summary>
    public static string ResolveDataDirectory(string? startDirectory = null)
    {
        var env = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(env))
        {
            try
            {
                return Path.GetFullPath(env.Trim());
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ArgumentException(
                    $"{DataDirEnvironmentVariable} 的值不是合法路径：'{env.Trim()}'（{ex.Message}）", nameof(env), ex);
            }
        }

        var start = string.IsNullOrWhiteSpace(startDirectory) ? AppContext.BaseDirectory : startDirectory!;
        var repoRoot = FindRepositoryRoot(start);
        if (repoRoot is not null)
        {
            return Path.Combine(repoRoot, "data");
        }

        return Path.Combine(Path.GetFullPath(start), "data");
    }

    /// <summary>从 <paramref name="startDirectory"/> 向上找项目根；找不到返回 null。</summary>
    public static string? FindRepositoryRoot(string? startDirectory = null)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(
            string.IsNullOrWhiteSpace(startDirectory) ? AppContext.BaseDirectory : startDirectory!));

        for (int level = 0; level < MaxUpwardLevels && dir is not null; level++, dir = dir.Parent)
        {
            if (LooksLikeRepositoryRoot(dir.FullName))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    private static bool LooksLikeRepositoryRoot(string directory) =>
        File.Exists(Path.Combine(directory, "data", "tabs.json"))
        || (File.Exists(Path.Combine(directory, "winui", "ToolboxPanel.WinUI.csproj"))
            && Directory.Exists(Path.Combine(directory, "src", "toolbox")));
}
