"""Tab data model."""
from dataclasses import dataclass, field
from uuid import uuid4

from .icon_model import IconModel
from .list_item_model import ListItemModel


@dataclass
class TabModel:
    """Represents a single tab page.

    tab_type: "grid" (icon grid, default) or "list" (two-column list).
    Old JSON files without tab_type load as "grid" (backward compatible).
    """
    id: str = field(default_factory=lambda: str(uuid4()))
    name: str = "新建标签页"
    order: int = 0
    tab_type: str = "grid"
    icons: list[IconModel] = field(default_factory=list)
    list_items: list[ListItemModel] = field(default_factory=list)

    def to_dict(self) -> dict:
        return {
            "id": self.id,
            "name": self.name,
            "order": self.order,
            "tab_type": self.tab_type,
            "icons": [icon.to_dict() for icon in self.icons],
            "list_items": [item.to_dict() for item in self.list_items],
        }

    @classmethod
    def from_dict(cls, d: dict) -> "TabModel":
        icons = [IconModel.from_dict(i) for i in d.get("icons", [])]
        list_items = [ListItemModel.from_dict(i) for i in d.get("list_items", [])]
        return cls(
            id=d.get("id", str(uuid4())),
            name=d.get("name", "新建标签页"),
            order=d.get("order", 0),
            tab_type=d.get("tab_type", "grid"),
            icons=icons,
            list_items=list_items,
        )
