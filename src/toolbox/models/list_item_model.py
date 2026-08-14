"""List item data model for list-style tabs."""
from dataclasses import dataclass, field
from uuid import uuid4


@dataclass
class ListItemModel:
    """Represents a single row in a list tab.

    Column 1: description (text label, editable)
    Column 2: path (file or folder, click opens / double-click edits)
    """
    id: str = field(default_factory=lambda: str(uuid4()))
    description: str = ""
    path: str = ""
    sort_order: int = 0

    def to_dict(self) -> dict:
        return {
            "id": self.id,
            "description": self.description,
            "path": self.path,
            "sort_order": self.sort_order,
        }

    @classmethod
    def from_dict(cls, d: dict) -> "ListItemModel":
        return cls(
            id=d.get("id", str(uuid4())),
            description=d.get("description", ""),
            path=d.get("path", ""),
            sort_order=d.get("sort_order", 0),
        )
