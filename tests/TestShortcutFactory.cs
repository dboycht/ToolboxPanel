// TestShortcutFactory.cs —— 单测辅助：用真 COM 造出真实的 .lnk
//
// 用 core 里那份 IShellLinkW/IPersistFile 声明来**写**快捷方式（生产代码只读），
// 这样「造 .lnk」和「读 .lnk」走的是同一套互操作声明，能顺带验证 vtable 顺序没写错。

using System.Runtime.InteropServices;
using ToolboxPanel.Core.Services;

namespace ToolboxPanel.Core.Tests;

internal static class TestShortcutFactory
{
    public static void Create(
        string lnkPath,
        string targetPath,
        string? arguments = null,
        string? workingDirectory = null,
        string? description = null,
        string? iconPath = null,
        int iconIndex = 0)
    {
        object? comObject = null;
        try
        {
            comObject = new ShellLinkCoClass();
            var link = (IShellLinkW)comObject;

            link.SetPath(targetPath);
            if (arguments is not null)
            {
                link.SetArguments(arguments);
            }

            if (workingDirectory is not null)
            {
                link.SetWorkingDirectory(workingDirectory);
            }

            if (description is not null)
            {
                link.SetDescription(description);
            }

            if (iconPath is not null)
            {
                link.SetIconLocation(iconPath, iconIndex);
            }

            ((IPersistFile)comObject).Save(lnkPath, true);
        }
        finally
        {
            if (comObject is not null && Marshal.IsComObject(comObject))
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
    }
}
