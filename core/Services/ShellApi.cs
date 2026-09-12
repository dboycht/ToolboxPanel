// ShellApi.cs —— ToolboxPanel v2 · W2 系统能力
//
// 图标提取要用的 Win32 声明（shell32/user32）。
// 数值全部对着本机 SDK 头文件核过：`D:\Windows Kits\10\Include\10.0.26100.0\um\shellapi.h`
// 与 `shlobj_core.h`（SIID_* / SHGFI_* / SHGSI_*）。

using System.Runtime.InteropServices;

namespace ToolboxPanel.Core.Services;

/// <summary>W2 用到的 shell32 / user32 入口。</summary>
internal static class ShellApi
{
    // ── SHGetFileInfo ──
    internal const uint SHGFI_ICON = 0x000000100;
    internal const uint SHGFI_LARGEICON = 0x000000000;
    internal const uint SHGFI_SMALLICON = 0x000000001;
    internal const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    internal const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    // ── SHGetStockIconInfo ──
    internal const uint SHGSI_ICON = 0x000000100;
    internal const uint SHGSI_LARGEICON = 0x000000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int SHGetStockIconInfo(int siid, uint uFlags, ref SHSTOCKICONINFO psii);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint ExtractIconEx(
        string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    // ── 窗口（W2 单实例守卫：把已有实例的窗口拉到前台）──
    internal const int SW_RESTORE = 9;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hWnd);
}

/// <summary>
/// <c>SHSTOCKICONID</c>（数值取自本机 SDK 的 <c>shellapi.h</c>）。
/// 只列出本项目兜底图标要用的几个。
/// </summary>
internal enum ShellStockIcon
{
    /// <summary>空白文档（原版 SP_FileIcon）。</summary>
    DocumentNoAssoc = 0,

    /// <summary>通用应用程序（原版 SP_CommandLink 的近似替代）。</summary>
    Application = 2,

    /// <summary>文件夹（原版 SP_DirIcon）。</summary>
    Folder = 3,

    /// <summary>快捷方式叠加图标（原版 SP_FileLinkIcon）。</summary>
    Link = 29,

    /// <summary>计算机（原版 SP_ComputerIcon，用于 URL）。</summary>
    DesktopPc = 94,
}
