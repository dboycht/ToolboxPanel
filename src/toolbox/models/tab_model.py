"""Tab data model."""
from dataclasses import dataclass, field
from uuid import uuid4

from .icon_model import IconModel


@dataclass
class TabModel:
    """Represents a single tab page with its icons."""
    id: str = field(default_factory=lambda: str(uuid4()))
    name: str = "新建标签页"
    order: int = 0
    icons: list[IconModel] = field(default_factory=list)

    def to_dict(self) -> dict:
        return {
            "id": self.id,
            "name": self.name,
            "order": self.order,
            "icons": [icon.to_dict() for icon in self.icons],
        }

    @classmethod
    def from_dict(cls, d: dict) -> "TabModel":
        icons = [IconModel.from_dict(i) for i in d.get("icons", [])]
        return cls(
            id=d.get("id", str(uuid4())),
            name=d.get("name", "新建标签页"),
            order=d.get("order", 0),
            icons=icons,
        )
