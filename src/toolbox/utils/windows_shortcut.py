"""Windows .lnk shortcut resolution using pywin32."""
import sys
from pathlib import Path
from typing import Optional


def resolve_shortcut(lnk_path: str) -> Optional[dict]:
    """Resolve a .lnk file and return its properties.

    Returns dict with: target_path, arguments, working_dir, icon_location, description.
    Returns None on failure or non-Windows platforms.
    """
    if sys.platform != "win32":
        return None

    try:
        import pythoncom
        pythoncom.CoInitialize()
        try:
            import win32com.client
            shell = win32com.client.Dispatch("WScript.Shell")
            shortcut = shell.CreateShortcut(str(lnk_path))
            result = {
                "target_path": shortcut.TargetPath or "",
                "arguments": shortcut.Arguments or "",
                "working_dir": shortcut.WorkingDirectory or "",
                "icon_location": shortcut.IconLocation or "",
                "description": shortcut.Description or "",
            }
            return result
        finally:
            pythoncom.CoUninitialize()
    except ImportError:
        return None
    except Exception:
        return None
