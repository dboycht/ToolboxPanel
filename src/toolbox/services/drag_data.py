"""MIME data parsing helpers for drag-and-drop."""
import json
from typing import Optional

from PyQt6.QtCore import QMimeData, QUrl


def parse_external_drop(mime_data: QMimeData) -> list[str]:
    """Parse file paths from a Windows Explorer drag-and-drop.

    Returns a list of absolute file paths.
    """
    urls = mime_data.urls()
    paths = []
    for url in urls:
        local = QUrl(url).toLocalFile()
        if local:
            paths.append(local)
    return paths


def parse_internal_drag(mime_data: QMimeData) -> Optional[dict]:
    """Parse toolbox-internal icon drag data.

    Returns dict with icon_id and source_tab_id, or None.
    """
    data = mime_data.data("application/x-toolbox-icon")
    if not data:
        return None
    try:
        return json.loads(bytes(data).decode("utf-8"))
    except (json.JSONDecodeError, UnicodeDecodeError):
        return None


def make_drag_data(icon_id: str, tab_id: str) -> QMimeData:
    """Create MIME data for an internal icon drag."""
    mime = QMimeData()
    mime.setData(
        "application/x-toolbox-icon",
        json.dumps({"icon_id": icon_id, "source_tab_id": tab_id}).encode("utf-8"),
    )
    mime.setText(icon_id)
    return mime
