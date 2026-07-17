"""JSON persistence layer for tabs and icons."""
import json
import shutil
from pathlib import Path
from typing import Optional

from .tab_model import TabModel
from .icon_model import IconModel


class DataStore:
    """Manages loading and saving of tab/icon state to tabs.json."""

    def __init__(self, data_dir: Path):
        self.data_dir = data_dir
        self.icons_dir = data_dir / "icons"
        self.tabs_file = data_dir / "tabs.json"
        self.tabs: list[TabModel] = []

        # Ensure directories exist
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.icons_dir.mkdir(parents=True, exist_ok=True)

    def load(self) -> list[TabModel]:
        """Load tabs from JSON. Returns default tab on any failure."""
        if not self.tabs_file.exists():
            self.tabs = [TabModel(name="主页", order=0)]
            self.save()
            return self.tabs

        try:
            with open(self.tabs_file, "r", encoding="utf-8") as f:
                data = json.load(f)
        except (json.JSONDecodeError, OSError):
            # Corrupted file — back it up and start fresh
            backup = self.tabs_file.with_suffix(".json.bak")
            shutil.copy2(self.tabs_file, backup)
            self.tabs = [TabModel(name="主页", order=0)]
            self.save()
            return self.tabs

        version = data.get("version", 1)
        tabs_data = data.get("tabs", [])

        if not tabs_data:
            self.tabs = [TabModel(name="主页", order=0)]
            self.save()
            return self.tabs

        self.tabs = [TabModel.from_dict(t) for t in tabs_data]
        self.tabs.sort(key=lambda t: t.order)
        return self.tabs

    def save(self):
        """Save current state to JSON."""
        data = {
            "version": 1,
            "tabs": [t.to_dict() for t in self.tabs],
        }
        with open(self.tabs_file, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2, ensure_ascii=False)

    def find_tab(self, tab_id: str) -> Optional[TabModel]:
        """Find a tab by its ID."""
        for tab in self.tabs:
            if tab.id == tab_id:
                return tab
        return None

    def find_icon(self, icon_id: str) -> Optional[tuple[TabModel, IconModel]]:
        """Find an icon and its parent tab by icon ID."""
        for tab in self.tabs:
            for icon in tab.icons:
                if icon.id == icon_id:
                    return tab, icon
        return None

    def add_tab(self, name: str = "New Tab") -> TabModel:
        """Add a new tab and return it."""
        tab = TabModel(name=name, order=len(self.tabs))
        self.tabs.append(tab)
        self.save()
        return tab

    def remove_tab(self, tab_id: str):
        """Remove a tab and all its icons."""
        self.tabs = [t for t in self.tabs if t.id != tab_id]
        # Renumber order
        for i, t in enumerate(self.tabs):
            t.order = i
        self.save()

    def add_icon(self, tab_id: str, icon: IconModel):
        """Add an icon to the specified tab."""
        tab = self.find_tab(tab_id)
        if tab:
            icon.sort_order = len(tab.icons)
            tab.icons.append(icon)
            self.save()

    def remove_icon(self, icon_id: str):
        """Remove an icon and its cache file."""
        result = self.find_icon(icon_id)
        if result:
            tab, icon = result
            tab.icons.remove(icon)
            # Delete cached icon file
            if icon.icon_cache_file:
                cache_path = self.icons_dir / icon.icon_cache_file
                if cache_path.exists():
                    cache_path.unlink()
            self.save()

    def reorder_tabs(self, from_index: int, to_index: int):
        """Update tab order after a drag-reorder."""
        if 0 <= from_index < len(self.tabs) and 0 <= to_index < len(self.tabs):
            tab = self.tabs.pop(from_index)
            self.tabs.insert(to_index, tab)
            for i, t in enumerate(self.tabs):
                t.order = i
            self.save()

    def move_icon(self, icon_id: str, target_tab_id: str, new_sort_order: int):
        """Move an icon to a different tab and/or position."""
        result = self.find_icon(icon_id)
        if not result:
            return
        source_tab, icon = result
        source_tab.icons.remove(icon)

        target_tab = self.find_tab(target_tab_id)
        if target_tab is None:
            # Put it back
            source_tab.icons.insert(min(new_sort_order, len(source_tab.icons)), icon)
            return

        target_tab.icons.insert(min(new_sort_order, len(target_tab.icons)), icon)

        # Renumber both tabs
        for tab in (source_tab, target_tab):
            for i, ic in enumerate(tab.icons):
                ic.sort_order = i
        self.save()

    def rename_icon(self, icon_id: str, new_name: str):
        """Rename an icon's display name."""
        result = self.find_icon(icon_id)
        if result:
            _, icon = result
            icon.display_name = new_name
            self.save()

    def rename_tab(self, tab_id: str, new_name: str):
        """Rename a tab."""
        tab = self.find_tab(tab_id)
        if tab:
            tab.name = new_name
            self.save()

    def reorder_icon(self, tab_id: str, from_index: int, to_index: int):
        """Reorder an icon within the same tab by moving the list element."""
        tab = self.find_tab(tab_id)
        if not tab or not (0 <= from_index < len(tab.icons)):
            return
        if from_index == to_index:
            return
        icon = tab.icons.pop(from_index)
        tab.icons.insert(to_index, icon)
        for i, ic in enumerate(tab.icons):
            ic.sort_order = i
        self.save()

    def orphan_cache_files(self) -> set[str]:
        """Return set of cache filenames not referenced by any icon."""
        referenced = set()
        for tab in self.tabs:
            for icon in tab.icons:
                if icon.icon_cache_file:
                    referenced.add(icon.icon_cache_file)
        on_disk = set()
        if self.icons_dir.exists():
            on_disk = {f.name for f in self.icons_dir.iterdir() if f.is_file()}
        return on_disk - referenced

    def clean_orphan_cache(self):
        """Delete unreferenced icon cache files."""
        for orphan in self.orphan_cache_files():
            (self.icons_dir / orphan).unlink()
