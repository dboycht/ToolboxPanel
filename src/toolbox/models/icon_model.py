"""Icon data model and type enumeration."""
from dataclasses import dataclass, field
from enum import StrEnum
from uuid import uuid4


class IconType(StrEnum):
    """Types of icons the toolbox supports."""
    FILE = "file"
    FOLDER = "folder"
    SHORTCUT = "shortcut"
    URL = "url"
    COMMAND = "command"


@dataclass
class IconModel:
    """Represents a single icon on a tab page."""
    id: str = field(default_factory=lambda: str(uuid4()))
    type: IconType = IconType.FILE
    display_name: str = ""
    source_path: str = ""       # file path, .lnk path, URL, or command string
    target_path: str = ""       # resolved real path (for shortcut), or executable
    arguments: str = ""         # command-line arguments
    working_dir: str = ""       # working directory for execution
    icon_cache_file: str = ""   # filename inside data/icons/
    sort_order: int = 0

    def to_dict(self) -> dict:
        return {
            "id": self.id,
            "type": str(self.type),
            "display_name": self.display_name,
            "source_path": self.source_path,
            "target_path": self.target_path,
            "arguments": self.arguments,
            "working_dir": self.working_dir,
            "icon_cache_file": self.icon_cache_file,
            "sort_order": self.sort_order,
        }

    @classmethod
    def from_dict(cls, d: dict) -> "IconModel":
        return cls(
            id=d.get("id", str(uuid4())),
            type=IconType(d.get("type", "file")),
            display_name=d.get("display_name", ""),
            source_path=d.get("source_path", ""),
            target_path=d.get("target_path", ""),
            arguments=d.get("arguments", ""),
            working_dir=d.get("working_dir", ""),
            icon_cache_file=d.get("icon_cache_file", ""),
            sort_order=d.get("sort_order", 0),
        )
