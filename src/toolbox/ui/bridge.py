"""QML ↔ Python 数据桥接。

分层约定（与原版一致的「逻辑在 Python」）：
- Python 侧持有 `DataStore`（原版数据层，未改动）与 `IconResolver`；
- QML 只做视图，通过**模型 + 槽函数**读写，不直接碰 JSON；
- 模型用 `QAbstractListModel`（官方推荐做法，**不要把 Python list 直接灌给 QML**）。

本文件只做「把现有数据暴露出去 + 把 QML 的意图转成 DataStore 调用」，
不含任何业务算法 —— 业务仍全部在 `models/` 与 `services/` 里。
"""
from __future__ import annotations

from pathlib import Path

from PyQt6.QtCore import (
    QAbstractListModel, QModelIndex, QObject, Qt, pyqtProperty, pyqtSignal, pyqtSlot,
)

from ..models.data_store import DataStore
from ..models.icon_model import IconModel, IconType
from ..models.list_item_model import ListItemModel
from ..models.tab_model import TabModel
from ..services.icon_resolver import IconResolver
from ..services.launcher import Launcher
from ..i18n import tr
from .settings import Settings


class TabListModel(QAbstractListModel):
    """标签页列表模型（供顶部标签栏使用）。"""

    IdRole = Qt.ItemDataRole.UserRole + 1
    NameRole = Qt.ItemDataRole.UserRole + 2
    TypeRole = Qt.ItemDataRole.UserRole + 3
    CountRole = Qt.ItemDataRole.UserRole + 4
    CurrentRole = Qt.ItemDataRole.UserRole + 5

    def __init__(self, parent=None):
        super().__init__(parent)
        self._tabs: list[TabModel] = []
        self._current = 0

    def set_tabs(self, tabs: list[TabModel], current: int = 0):
        self.beginResetModel()
        self._tabs = list(tabs)
        self._current = max(0, min(current, max(0, len(self._tabs) - 1)))
        self.endResetModel()

    def refresh(self):
        """数据在 Python 侧被改动后，通知视图重读全部行。"""
        if not self._tabs:
            return
        self.dataChanged.emit(self.index(0, 0),
                              self.index(len(self._tabs) - 1, 0))

    def rowCount(self, parent=QModelIndex()):
        return 0 if parent.isValid() else len(self._tabs)

    def data(self, index: QModelIndex, role=Qt.ItemDataRole.DisplayRole):
        if not index.isValid() or not (0 <= index.row() < len(self._tabs)):
            return None
        tab = self._tabs[index.row()]
        if role == self.IdRole:
            return tab.id
        if role == self.NameRole:
            return tab.name
        if role == self.TypeRole:
            return tab.tab_type
        if role == self.CountRole:
            return len(tab.icons) if tab.tab_type == "grid" else len(tab.list_items)
        if role == self.CurrentRole:
            return index.row() == self._current
        return None

    def roleNames(self):
        return {
            self.IdRole: b"tabId",
            self.NameRole: b"tabName",
            self.TypeRole: b"tabType",
            self.CountRole: b"itemCount",
            self.CurrentRole: b"isCurrent",
        }

    def set_current(self, row: int):
        old, new = self._current, max(0, row)
        if old == new or new >= len(self._tabs):
            return
        self._current = new
        for r in (old, new):
            idx = self.index(r, 0)
            self.dataChanged.emit(idx, idx)


class IconListModel(QAbstractListModel):
    """当前标签页的图标模型（供网格/列表视图使用）。"""

    countChanged = pyqtSignal()

    IdRole = Qt.ItemDataRole.UserRole + 1
    NameRole = Qt.ItemDataRole.UserRole + 2
    IconPathRole = Qt.ItemDataRole.UserRole + 3
    TypeRole = Qt.ItemDataRole.UserRole + 4
    SortRole = Qt.ItemDataRole.UserRole + 5
    DescRole = Qt.ItemDataRole.UserRole + 6
    PathRole = Qt.ItemDataRole.UserRole + 7
    MissingRole = Qt.ItemDataRole.UserRole + 8

    def __init__(self, icons_dir: Path, parent=None):
        super().__init__(parent)
        self._icons_dir = icons_dir
        self._items: list[dict] = []

    def set_items(self, items: list[dict]):
        self.beginResetModel()
        self._items = list(items)
        self.endResetModel()
        self.countChanged.emit()

    def rowCount(self, parent=QModelIndex()):
        return 0 if parent.isValid() else len(self._items)

    def data(self, index: QModelIndex, role=Qt.ItemDataRole.DisplayRole):
        if not index.isValid() or not (0 <= index.row() < len(self._items)):
            return None
        it = self._items[index.row()]
        if role == self.IdRole:
            return it.get("id", "")
        if role == self.NameRole:
            return it.get("name", "")
        if role == self.IconPathRole:
            return it.get("iconPath", "")
        if role == self.TypeRole:
            return it.get("type", "")
        if role == self.SortRole:
            return index.row()
        if role == self.DescRole:
            return it.get("desc", "")
        if role == self.PathRole:
            return it.get("path", "")
        if role == self.MissingRole:
            return it.get("missing", False)
        return None

    def roleNames(self):
        return {
            self.IdRole: b"iconId",
            self.NameRole: b"iconName",
            self.IconPathRole: b"iconPath",
            self.TypeRole: b"iconType",
            self.SortRole: b"sortOrder",
            self.DescRole: b"iconDesc",
            self.PathRole: b"iconTarget",
            self.MissingRole: b"iconMissing",
        }

    # ⚠️ QML 侧 `model.count` 需要显式 property：QAbstractListModel 的 rowCount()
    # 在 QML 里**不自动**映射成 `count`（`count` 是视图自己的属性）。
    # 少了它，`app.icons.count === 0` 会报 "Cannot read property 'count' of undefined"。
    @pyqtProperty(int, notify=countChanged)
    def count(self) -> int:
        return len(self._items)


class Bridge(QAbstractListModel):
    """QML 的 `app` 上下文属性：暴露标签页/图标/状态与操作槽。"""

    statusChanged = pyqtSignal()
    currentTabChanged = pyqtSignal()
    languageChanged = pyqtSignal()
    searchChanged = pyqtSignal()
    tabsChanged = pyqtSignal()
    iconSizeChanged = pyqtSignal()

    def __init__(self, settings: Settings, data_store: DataStore, parent=None):
        super().__init__(parent)
        self.settings = settings
        self.store = data_store
        self.launcher = Launcher()
        self.resolver = IconResolver(data_store.icons_dir)
        self._status = ""
        self._current = 0
        self._search = ""
        self._batch = False
        self._lang = settings.get("language", "zh")

        self._tabs = TabListModel(self)
        self._icons = IconListModel(data_store.icons_dir, self)

        self.load()

    # ── 子模型（必须是 pyqtProperty，QML 看不见裸 Python 属性）─────────
    # ⚠️ 与 theme.c 同一个坑：`self.icons = ...` 这种普通属性在 QML 里读出来是
    # `undefined`，报错形如 "Cannot read property 'count' of undefined"。
    # QML 只能看见 QObject 的 pyqtProperty / 槽 / 信号。

    @pyqtProperty(QObject, constant=True)
    def tabs(self):
        return self._tabs

    @pyqtProperty(QObject, constant=True)
    def icons(self):
        return self._icons

    # ── 启动载入 ───────────────────────────────────────────────────

    def load(self):
        """读 tabs.json（原版 DataStore.load，含损坏容错）并重建模型。"""
        tabs = self.store.load()
        self._tabs.set_tabs(tabs, self._current)
        self._rebuild_icons()
        self.tabsChanged.emit()
        self.setStatus(tr("app.status.loaded", n=len(tabs)))

    # ── 属性 ───────────────────────────────────────────────────────

    @pyqtProperty(str, notify=statusChanged)
    def status(self) -> str:
        return self._status

    @pyqtProperty(int, notify=currentTabChanged)
    def currentIndex(self) -> int:
        return self._current

    @pyqtProperty(str, notify=currentTabChanged)
    def currentTabName(self) -> str:
        t = self.current_tab()
        return t.name if t else ""

    @pyqtProperty(str, notify=currentTabChanged)
    def currentTabType(self) -> str:
        t = self.current_tab()
        return t.tab_type if t else "grid"

    @pyqtProperty(str, notify=searchChanged)
    def searchQuery(self) -> str:
        return self._search

    @pyqtProperty(str, notify=languageChanged)
    def language(self) -> str:
        return self._lang

    @pyqtProperty(str, notify=languageChanged)
    def appTitle(self) -> str:
        return tr("app.title")

    @pyqtProperty(bool, notify=currentTabChanged)
    def canRemoveTab(self) -> bool:
        """至少保留一个标签页（与原版 tab.delete.blocked 规则一致）。"""
        return len(self.store.tabs) > 1

    @pyqtProperty(bool, notify=currentTabChanged)
    def isBatchMode(self) -> bool:
        return self._batch

    @pyqtProperty(str, notify=iconSizeChanged)
    def iconSize(self) -> str:
        return self.settings.get("icon_size", "medium")

    @pyqtSlot(str)
    def setIconSize(self, preset: str):
        """图标大小（small/medium/large），写回 config.json（与原版一致）。"""
        if preset not in ("small", "medium", "large"):
            return
        if preset == self.settings.get("icon_size"):
            return
        self.settings.set("icon_size", preset)
        self.iconSizeChanged.emit()

    # ── 槽：标签页 ─────────────────────────────────────────────────

    @pyqtSlot(int)
    def selectTab(self, index: int):
        if not (0 <= index < len(self.store.tabs)):
            return
        self._current = index
        self._tabs.set_current(index)
        self._rebuild_icons()
        self.currentTabChanged.emit()

    def current_tab(self) -> TabModel | None:
        if 0 <= self._current < len(self.store.tabs):
            return self.store.tabs[self._current]
        return None

    def _rebuild_icons(self):
        """按当前标签页重建图标模型；同时应用搜索过滤。"""
        tab = self.current_tab()
        if tab is None:
            self._icons.set_items([])
            return
        query = self._search.strip().lower()

        if tab.tab_type == "list":
            items = []
            for li in sorted(tab.list_items, key=lambda x: x.sort_order):
                if query and query not in (li.description or "").lower() \
                        and query not in (li.path or "").lower():
                    continue
                items.append({
                    "id": li.id, "name": li.description, "desc": li.description,
                    "path": li.path, "type": "listitem", "iconPath": "",
                    "missing": bool(li.path) and not Path(li.path).exists(),
                })
            self._icons.set_items(items)
            return

        items = []
        for ic in sorted(tab.icons, key=lambda x: x.sort_order):
            if query and query not in (ic.display_name or "").lower():
                continue
            icon_path = ""
            if ic.icon_cache_file:
                p = self.store.icons_dir / ic.icon_cache_file
                if p.exists():
                    # QML 侧用 file:// URL 加载
                    icon_path = p.as_uri()
            target = ic.target_path or ic.source_path
            items.append({
                "id": ic.id,
                "name": ic.display_name,
                "iconPath": icon_path,
                "type": str(ic.type),
                "desc": ic.description,
                "path": target,
                "missing": bool(target) and not target.startswith(("http://", "https://"))
                           and not Path(target).exists(),
            })
        self._icons.set_items(items)

    @pyqtSlot()
    def addTab(self):
        name = tr("tab.default_name")
        tab = self.store.add_tab(name)
        self._tabs.set_tabs(self.store.tabs, len(self.store.tabs) - 1)
        self._current = len(self.store.tabs) - 1
        self._rebuild_icons()
        self.tabsChanged.emit()
        self.currentTabChanged.emit()
        self.setStatus(tr("app.status.created_tab", name=tab.name))

    @pyqtSlot(str)
    def addTabNamed(self, name: str):
        name = (name or "").strip() or tr("tab.default_name")
        tab = self.store.add_tab(name)
        self._tabs.set_tabs(self.store.tabs, len(self.store.tabs) - 1)
        self._current = len(self.store.tabs) - 1
        self._rebuild_icons()
        self.tabsChanged.emit()
        self.currentTabChanged.emit()
        self.setStatus(tr("app.status.created_tab", name=tab.name))

    @pyqtSlot()
    def addListTab(self):
        name = tr("list.default_name")
        tab = self.store.add_tab(name, tab_type="list")
        self._tabs.set_tabs(self.store.tabs, len(self.store.tabs) - 1)
        self._current = len(self.store.tabs) - 1
        self._rebuild_icons()
        self.tabsChanged.emit()
        self.currentTabChanged.emit()
        self.setStatus(tr("app.status.created_tab", name=tab.name))

    @pyqtSlot(int)
    def removeTab(self, index: int):
        # 至少保留一个标签页（与原版 tab.delete.blocked 一致）
        if len(self.store.tabs) <= 1:
            self.setStatus(tr("tab.delete.blocked"))
            return
        if not (0 <= index < len(self.store.tabs)):
            return
        tab = self.store.tabs[index]
        name = tab.name
        self.store.remove_tab(tab.id)
        self._current = max(0, min(self._current, len(self.store.tabs) - 1))
        self._tabs.set_tabs(self.store.tabs, self._current)
        self._rebuild_icons()
        self.tabsChanged.emit()
        self.currentTabChanged.emit()
        self.setStatus(tr("tab.deleted", name=name))

    @pyqtSlot(int, str)
    def renameTab(self, index: int, name: str):
        if not (0 <= index < len(self.store.tabs)):
            return
        name = (name or "").strip()
        if not name:
            return
        tab = self.store.tabs[index]
        self.store.rename_tab(tab.id, name)
        tab.name = name
        self._tabs.refresh()
        self.tabsChanged.emit()
        if index == self._current:
            self.currentTabChanged.emit()
        self.setStatus(tr("tab.renamed", name=name))

    # ── 槽：搜索 ───────────────────────────────────────────────────

    @pyqtSlot(str)
    def setSearch(self, text: str):
        if text == self._search:
            return
        self._search = text or ""
        self._rebuild_icons()
        self.searchChanged.emit()

    @pyqtSlot()
    def clearSearch(self):
        self.setSearch("")

    @pyqtSlot()
    def toggleSearch(self):
        """Ctrl+F / 标题栏搜索按钮：切换搜索可见性（与原版 Toggle 行为一致）。"""
        self._search_open = not getattr(self, "_search_open", False)
        self.searchChanged.emit()
        if not self._search_open:
            self.setSearch("")

    @pyqtProperty(bool, notify=searchChanged)
    def searchOpen(self) -> bool:
        return bool(getattr(self, "_search_open", False))

    # ── 槽：语言 ───────────────────────────────────────────────────

    @pyqtSlot(str)
    def setLanguage(self, lang: str):
        from ..i18n import set_language
        if lang not in ("zh", "en") or lang == self._lang:
            return
        self._lang = lang
        set_language(lang)
        self.settings.set("language", lang)
        self.languageChanged.emit()
        self.statusChanged.emit()
        self.currentTabChanged.emit()

    # ── 槽：图标操作（与 v1.11.6 行为一致）─────────────────────────

    @pyqtSlot(str)
    def openIcon(self, icon_id: str):
        """打开图标（原版双击行为）。"""
        result = self.store.find_icon(icon_id)
        if not result:
            self._open_list_item(icon_id)
            return
        _, icon = result
        try:
            self.launcher.open(icon)
            self.setStatus(tr("status.opened", name=icon.display_name))
        except Exception as e:  # noqa: BLE001
            self.setStatus(tr("status.open_failed", err=str(e)))

    def _open_list_item(self, item_id: str):
        res = self.store.find_list_item(item_id)
        if not res:
            return
        _, li = res
        try:
            if li.path:
                self.launcher._open_file(li.path)
                self.setStatus(tr("status.opened", name=li.description or li.path))
        except Exception as e:  # noqa: BLE001
            self.setStatus(tr("status.open_failed", err=str(e)))

    @pyqtSlot(str, str)
    def renameIcon(self, icon_id: str, name: str):
        name = (name or "").strip()
        if not name:
            self.setStatus(tr("validate.name_required"))
            return
        self.store.rename_icon(icon_id, name)
        self._rebuild_icons()
        self.setStatus(tr("status.renamed", name=name))

    @pyqtSlot(str)
    def removeIcon(self, icon_id: str):
        """删除图标（原版：从当前标签页移除并清理缓存文件）。"""
        result = self.store.find_icon(icon_id)
        if result:
            name = result[1].display_name
            self.store.remove_icon(icon_id)
            self._rebuild_icons()
            self._tabs.refresh()
            self.setStatus(tr("status.removed", name=name))
            return
        res2 = self.store.find_list_item(icon_id)
        if res2:
            name = res2[1].description
            self.store.remove_list_item(icon_id)
            self._rebuild_icons()
            self._tabs.refresh()
            self.setStatus(tr("status.removed", name=name))

    @pyqtSlot(str, str)
    def renameListItem(self, item_id: str, desc: str):
        self.store.update_list_item(item_id, description=desc)
        self._rebuild_icons()

    # ── 槽：批量管理（与原版行为一致）──────────────────────────────

    @pyqtSlot()
    def toggleBatchMode(self):
        self._batch = not self._batch
        self.currentTabChanged.emit()
        self.setStatus(tr("status.batch_mode_on") if self._batch
                       else tr("app.status.ready"))

    @pyqtSlot("QVariantList")
    def batchRemove(self, ids):
        """按 id 列表删除（QML 侧收集勾选项后调用）。"""
        count = 0
        for icon_id in (ids or []):
            if self.store.find_icon(icon_id):
                self.store.remove_icon(icon_id)
                count += 1
            elif self.store.find_list_item(icon_id):
                self.store.remove_list_item(icon_id)
                count += 1
        if count:
            self._rebuild_icons()
            self._tabs.refresh()
        self.setStatus(tr("bulk_delete.done", count=count) if count
                       else tr("batch.none_checked"))

    # ── 状态 ───────────────────────────────────────────────────────

    def setStatus(self, text: str):
        self._status = text or ""
        self.statusChanged.emit()

    @pyqtSlot(str)
    def showStatus(self, text: str):
        self.setStatus(text)
