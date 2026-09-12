// ShellLinkInterop.cs —— ToolboxPanel v2 · W2 系统能力
//
// .lnk 解析所需的 COM 互操作声明（IShellLinkW / IPersistFile）。
//
// 为什么用 COM 而不是 `WScript.Shell`：
//   · 原版 Python 走的是 pywin32 的 `WScript.Shell.CreateShortcut()`（即 COM 晚绑定）；
//     这里用同样的底层 COM 对象（ShellLink CoClass），但不依赖 WSH 脚本宿主，
//     少一层间接、字段拿得更细（图标是「文件 + 索引」两段，原版只能拿到拼好的字符串）。
//
// ⚠️ IShellLinkW 的方法**顺序即 vtable 顺序**，少写/调换一个方法就会拿到垃圾数据或崩溃；
//    不要凭记忆改这个接口，改之前对着 Windows SDK 的 shobjidl_core.h 核一遍。

using System.Runtime.InteropServices;
using System.Text;

namespace ToolboxPanel.Core.Services;

/// <summary>IShellLinkW 的 GetPath/GetIconLocation 等缓冲区长度的约定值。</summary>
internal static class ShellLinkLimits
{
    public const int MaxPath = 260;
    public const int MaxArgs = 1024;
    public const int MaxDescription = 1024;
}

/// <summary>ShellLink COM 对象的 CLSID 包装（`new ShellLinkCoClass()` 即 CoCreateInstance）。</summary>
[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal sealed class ShellLinkCoClass
{
}

/// <summary>`IShellLinkW`（只声明本项目用得到的成员，顺序必须与原生 vtable 一致）。</summary>
[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

    void GetIDList(out IntPtr ppidl);

    void SetIDList(IntPtr pidl);

    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

    void GetHotkey(out ushort pwHotkey);

    void SetHotkey(ushort wHotkey);

    void GetShowCmd(out int piShowCmd);

    void SetShowCmd(int iShowCmd);

    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

    void Resolve(IntPtr hwnd, uint fFlags);

    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

/// <summary>`IPersistFile`（只声明 Load/Save，够用）。</summary>
[ComImport]
[Guid("0000010B-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);

    [PreserveSig]
    int IsDirty();

    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

    void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, bool fRemember);

    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}
