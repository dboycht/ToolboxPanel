"""可滚动图标网格 — 容纳 FlowLayout 并处理拖放操作。"""
import json
import os
import subprocess
from pathlib import Path

from PyQt6.QtWidgets import (QScrollArea, QWidget, QMenu, QMessageBox,
                              QVBoxLayout, QApplication)
from PyQt6.QtCore import Qt, pyqtSignal, QUrl, QTimer
from PyQt6.QtGui import QDragEnterEvent, QDragMoveEvent, QDropEvent

from .flow_layout import FlowLayout
from .icon_widget import IconWidget
from .models.data_store import DataStore
from .models.tab_model import TabModel
from .models.icon_model import IconModel, IconType
from .i18n import tr


class _DropContainer(QWidget):
    """内部容器 — 处理拖放事件并转发给 IconGrid。"""

    external_dropped = pyqtSignal(list)
    internal_dropped = pyqtSignal(dict, int, int)  # info dict, x, y
    status_message = pyqtSignal(str)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setAcceptDrops(True)
        self.setStyleSheet("_DropContainer { background-color: #ffffff; }")

    def dragEnterEvent(self, event: QDragEnterEvent):
        if event.mimeData().hasUrls() or event.mimeData().hasFormat("application/x-toolbox-icon"):
            event.acceptProposedAction()
            self.setStyleSheet(
                "_DropContainer {"
                "  background-color: #f0f6ff;"
                "  border: 2px dashed #0067c0;"
                "  border-radius: 8px;"
                "}"
            )
        else:
            event.ignore()

    def dragMoveEvent(self, event: QDragMoveEvent):
        if event.mimeData().hasUrls() or event.mimeData().hasFormat("application/x-toolbox-icon"):
            event.acceptProposedAction()
        else:
            event.ignore()

    def dragLeaveEvent(self, event):
        self.setStyleSheet("_DropContainer { background-color: #ffffff; }")

    def dropEvent(self, event: QDropEvent):
        self.setStyleSheet("_DropContainer { background-color: #ffffff; }")

        if event.mimeData().hasUrls():
            urls = event.mimeData().urls()
            paths = [QUrl(url).toLocalFile() for url in urls if QUrl(url).isLocalFile()]
            if paths:
                self.external_dropped.emit(paths)
            else:
                self.status_message.emit(tr("status.no_files"))
            event.acceptProposedAction()
        elif event.mimeData().hasFormat("application/x-toolbox-icon"):
            data = event.mimeData().data("application/x-toolbox-icon")
            if data:
                try:
                    info = json.loads(bytes(data).decode("utf-8"))
                except (json.JSONDecodeError, UnicodeDecodeError):
                    info = {}
                self.internal_dropped.emit(info,
                    int(event.position().x()), int(event.position().y()))
            event.acceptProposedAction()
        else:
            event.ignore()


class IconGrid(QScrollArea):
    """单个标签页的可滚动图标网格。"""

    icon_added = pyqtSignal(str, IconModel)
    icon_removed = pyqtSignal(str)
    icon_moved = pyqtSignal(str, str, int)
    icon_double_clicked = pyqtSignal(str)
    files_dropped = pyqtSignal(list)
    status_message = pyqtSignal(str)

    def __init__(self, tab: TabModel, data_store: DataStore,
                 icon_cache_dir: Path, parent=None):
        super().__init__(parent)
        self.tab = tab
        self.data_store = data_store
        self.icon_cache_dir = icon_cache_dir

        self.setWidgetResizable(True)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        self.setFrameShape(QScrollArea.Shape.NoFrame)
        self.setStyleSheet("QScrollArea { background-color: transparent; border: none; }")

        # 使用自定义 DropContainer 代替普通 QWidget
        self._container = _DropContainer()
        self._layout = FlowLayout()
        self._container.setLayout(self._layout)
        self.setWidget(self._container)

        # 连接 DropContainer 的信号
        self._container.external_dropped.connect(self.files_dropped.emit)
        self._container.internal_dropped.connect(self._on_internal_drop)
        self._container.status_message.connect(self.status_message.emit)

        self._icon_widgets: dict[str, IconWidget] = {}

    # ---- 公开 API ----

    def add_icon(self, icon: IconModel) -> IconWidget:
        """创建并添加一个图标组件。"""
        widget = IconWidget(icon, self.icon_cache_dir)
        widget.icon_double_clicked.connect(self.icon_double_clicked.emit)
        widget.rename_requested.connect(self._on_rename_requested)

        self._layout.insert_widget_at(icon.sort_order, widget)
        self._icon_widgets[icon.id] = widget

        # 图标右键菜单
        widget.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        widget.customContextMenuRequested.connect(
            lambda pos, w=widget: self._show_icon_context_menu(pos, w)
        )
        return widget

    def remove_icon(self, icon_id: str):
        """从网格中移除图标组件。"""
        widget = self._icon_widgets.pop(icon_id, None)
        if widget:
            self._layout.remove_widget(widget)
            widget.deleteLater()

    def refresh_all(self):
        """从模型完全重建网格。"""
        for wid in list(self._icon_widgets.values()):
            self._layout.remove_widget(wid)
            wid.deleteLater()
        self._icon_widgets.clear()

        for icon in sorted(self.tab.icons, key=lambda i: i.sort_order):
            self.add_icon(icon)

    def rebuild_from_model(self):
        """根据 sort_order 同步组件位置。"""
        tab_icons = {i.id: i for i in self.tab.icons}
        for icon_id in list(self._icon_widgets.keys()):
            if icon_id not in tab_icons:
                self.remove_icon(icon_id)

        items = list(self._layout._items)
        self._layout._items.clear()
        sorted_widgets = sorted(
            items,
            key=lambda item: tab_icons.get(
                item.widget().icon_model.id if item.widget() else "",
                IconModel()
            ).sort_order
        )
        self._layout._items = sorted_widgets
        self._layout.invalidate()
        self._container.updateGeometry()

    # ---- 内部拖放处理 ----

    def _on_internal_drop(self, info: dict, x: int, y: int):
        """处理内部图标拖放。"""
        icon_id = info.get("icon_id", "")
        if not icon_id:
            return

        drop_index = self._layout.cell_index_at_pos(x, y, self._container.width())

        if icon_id in self._icon_widgets:
            source_widget = self._icon_widgets[icon_id]
            source_index = self._layout.index_of(source_widget)
            if source_index >= 0 and drop_index != source_index:
                target_index = drop_index
                if target_index > source_index:
                    target_index -= 1
                self._layout.move_widget(source_index, target_index)
                self.data_store.reorder_icon(self.tab.id, source_index, target_index)
                self.status_message.emit(tr("status.moved"))
        else:
            self.icon_moved.emit(icon_id, self.tab.id, drop_index)

    # ---- 右键菜单 ----

    def _show_icon_context_menu(self, pos, widget: IconWidget):
        """图标右键菜单。"""
        menu = QMenu(self)

        open_action = menu.addAction(tr("icon.menu.open"))
        open_action.triggered.connect(
            lambda: self.icon_double_clicked.emit(widget.icon_model.id)
        )

        open_loc_action = menu.addAction(tr("icon.menu.open_location"))
        open_loc_action.triggered.connect(
            lambda: self._open_file_location(widget.icon_model)
        )

        menu.addSeparator()

        rename_action = menu.addAction(tr("icon.menu.rename"))
        rename_action.triggered.connect(lambda: widget.name_label._start_edit())

        remove_action = menu.addAction(tr("icon.menu.remove"))
        remove_action.triggered.connect(
            lambda: self._remove_icon(widget.icon_model.id)
        )

        menu.exec(widget.mapToGlobal(pos))

    def _on_rename_requested(self, icon_id: str, new_name: str):
        """图标被重命名。"""
        self.data_store.rename_icon(icon_id, new_name)
        self.status_message.emit(tr("status.renamed", name=new_name))

    def _remove_icon(self, icon_id: str):
        """删除图标（带确认）。"""
        widget = self._icon_widgets.get(icon_id)
        name = widget.icon_model.display_name if widget else "此图标"

        confirm = QMessageBox.question(
            self, tr("icon.remove.title"),
            tr("icon.remove.confirm", name=name),
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
        )
        if confirm == QMessageBox.StandardButton.Yes:
            self.remove_icon(icon_id)
            self.data_store.remove_icon(icon_id)
            self.icon_removed.emit(icon_id)
            self.status_message.emit(tr("status.removed", name=name))

    def _open_file_location(self, icon: IconModel):
        """在资源管理器中定位文件。"""
        path = icon.target_path or icon.source_path
        if not path:
            return
        if os.path.isfile(path):
            subprocess.Popen(['explorer', '/select,', path])
        elif os.path.isdir(path):
            os.startfile(path)
        else:
            self.status_message.emit(tr("status.path_missing", path=path))
