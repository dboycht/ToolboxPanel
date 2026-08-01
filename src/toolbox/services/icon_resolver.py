"""System icon extraction and caching."""
import uuid
from pathlib import Path

from PyQt6.QtWidgets import QFileIconProvider, QApplication, QStyle
from PyQt6.QtCore import QFileInfo, Qt
from PyQt6.QtGui import QPixmap

from ..models.icon_model import IconType
from ..utils.windows_shortcut import resolve_shortcut

# Single source of truth for fallback icon type → standard pixmap mapping
FALLBACK_ICON_MAP: dict = {
    IconType.FILE: QStyle.StandardPixmap.SP_FileIcon,
    IconType.FOLDER: QStyle.StandardPixmap.SP_DirIcon,
    IconType.SHORTCUT: QStyle.StandardPixmap.SP_FileLinkIcon,
    IconType.URL: QStyle.StandardPixmap.SP_ComputerIcon,
    IconType.COMMAND: QStyle.StandardPixmap.SP_CommandLink,
}


class IconResolver:
    """Extracts system icons for files/folders and caches them as PNG."""

    def __init__(self, cache_dir: Path, icon_size: int = 64):
        self.cache_dir = cache_dir
        self.icon_size = icon_size
        self._provider = QFileIconProvider()
        self.cache_dir.mkdir(parents=True, exist_ok=True)

    def extract_and_cache(self, file_path: str) -> str:
        """Extract the system icon for a path and cache it.

        Returns the cache filename (uuid.png), or empty string on failure.
        """
        try:
            pixmap = self._extract_icon(file_path)
        except Exception:
            pixmap = None

        if pixmap is None or pixmap.isNull():
            pixmap = self.get_fallback(IconType.FILE)

        cache_name = f"{uuid.uuid4()}.png"
        cache_path = self.cache_dir / cache_name
        pixmap.save(str(cache_path), "PNG")
        return cache_name

    def get_cached_path(self, cache_file: str) -> Path:
        """Get the full path to a cached icon file."""
        return self.cache_dir / cache_file

    def _extract_icon(self, file_path: str) -> QPixmap:
        """Extract icon for a given file/folder path.

        For .lnk files, QFileIconProvider automatically resolves to the target's icon on Windows.
        """
        file_info = QFileInfo(file_path)
        icon = self._provider.icon(file_info)
        if icon.isNull():
            return self.get_fallback(IconType.FILE)

        pixmap = icon.pixmap(self.icon_size, self.icon_size)
        if pixmap.isNull():
            return self.get_fallback(IconType.FILE)
        return pixmap

    def resolve_shortcut(self, lnk_path: str) -> dict:
        """Resolve a .lnk shortcut. Returns dict with target info."""
        result = resolve_shortcut(lnk_path)
        if result is None:
            # Fallback: treat .lnk as a regular file
            return {
                "target_path": lnk_path,
                "arguments": "",
                "working_dir": "",
                "icon_location": "",
                "description": "",
            }
        return result

    def extract_icon_from_file(self, file_path: str, index: int = 0,
                               size: int | None = None) -> QPixmap | None:
        """Extract the icon at a given index from an exe/dll/ico file.

        Uses Windows ExtractIconEx via pywin32; falls back to the file's
        default icon if index extraction is unavailable. Returns None on failure.
        """
        if not file_path or not Path(file_path).exists():
            return None
        size = size or self.icon_size
        try:
            import win32gui
            large, _ = win32gui.ExtractIconEx(file_path, index, 1, 0)
            if large:
                from PyQt6.QtGui import QIcon
                pixmap = QIcon.fromWinHICON(large[0]).pixmap(size, size)
                win32gui.DestroyIcon(large[0])
                if pixmap and not pixmap.isNull():
                    return pixmap
        except Exception:
            pass
        # Fallback: default icon of the file
        try:
            return self._extract_icon(file_path)
        except Exception:
            return None

    def get_fallback(self, icon_type: IconType) -> QPixmap:
        """Return a generic fallback icon for the given icon type."""
        style = QApplication.style()
        if not style:
            pixmap = QPixmap(self.icon_size, self.icon_size)
            pixmap.fill(Qt.GlobalColor.lightGray)
            return pixmap

        std_icon = FALLBACK_ICON_MAP.get(icon_type, QStyle.StandardPixmap.SP_FileIcon)
        return style.standardIcon(std_icon).pixmap(self.icon_size, self.icon_size)

    # Backward-compatible alias
    _get_fallback = get_fallback
