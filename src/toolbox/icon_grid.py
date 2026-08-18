"""可滚动图标网格 — 容纳 FlowLayout 并处理拖放操作。"""
import json
import os
import subprocess
import uuid
from pathlib import Path

from PyQt6.QtWidgets import (QScrollArea, QWidget, QMenu, QMessageBox,
                              QVBoxLayout, QLineEdit)
from PyQt6.QtCore import Qt, pyqtSignal, QUrl, QEvent
from PyQt6.QtGui import QDragEnterEvent, QDragMoveEvent, QDropEvent

from .flow_layout import FlowLayout
from .icon_widget import IconWidget, SIZE_PRESETS
from .models.data_store import DataStore
from .models.tab_model import TabModel
from .models.icon_model import IconModel, IconType
from .i18n import tr


class _DropContainer(QWidget):
    """内部容器 — 处理拖放事件并转发给 IconGrid。"""

    external_dropped = pyqtSignal(list)
    internal_dropped = pyqtSignal(dict, int, int)
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

    icon_removed = pyqtSignal(str)
    icon_moved = pyqtSignal(str, str, int)
    icon_double_clicked = pyqtSignal(str)
    files_dropped = pyqtSignal(list)
    status_message = pyqtSignal(str)
    search_closed = pyqtSignal()

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

        self._container = _DropContainer()
        self._layout = FlowLayout()
        self._container.setLayout(self._layout)

        # Outer widget: search bar + container
        self._outer = QWidget()
        outer_layout = QVBoxLayout(self._outer)
        outer_layout.setContentsMargins(0, 0, 0, 0)
        outer_layout.setSpacing(4)

        # Search bar (hidden by default) — Esc 可关闭
        self._search_bar = QLineEdit()
        self._search_bar.setPlaceholderText(tr("search.placeholder"))
        self._search_bar.setClearButtonEnabled(True)
        self._search_bar.hide()
        self._search_bar.textChanged.connect(self._apply_filter)
        self._search_bar.installEventFilter(self)
        outer_layout.addWidget(self._search_bar)
        outer_layout.addWidget(self._container)

        self.setWidget(self._outer)

        self._container.external_dropped.connect(self.files_dropped.emit)
        self._container.internal_dropped.connect(self._on_internal_drop)
        self._container.status_message.connect(self.status_message.emit)

        self._icon_widgets: dict[str, IconWidget] = {}
        self._batch_mode = False

        # 样式表必须放在 __init__ 末尾设置：
        # 在子部件构建前调用 setStyleSheet 会触发 Qt 原生崩溃
        # （Qt 6.x 在 QScrollArea 上 polish 时访问未初始化结构）
        self.setStyleSheet("QScrollArea { background-color: transparent; border: none; }")

    # ── 批量管理 ──

    def set_batch_mode(self, on: bool):
        """切换批量管理模式：显示/隐藏所有图标的复选框。"""
        self._batch_mode = on
        for w in self._icon_widgets.values():
            w.set_batch_mode(on)

    def batch_delete(self):
        """删除所有已勾选的图标。"""
        checked = [id_ for id_, w in self._icon_widgets.items() if w.is_checked()]
        count = len(checked)
        if count == 0:
            self.status_message.emit(tr("batch.none_checked"))
            return
        confirm = QMessageBox.question(
            self,
            tr("bulk_delete.title"),
            tr("bulk_delete.confirm", count=count),
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
        )
        if confirm == QMessageBox.StandardButton.Yes:
            for icon_id in checked:
                self.remove_icon(icon_id)
                self.data_store.remove_icon(icon_id)
                self.icon_removed.emit(icon_id)
            self.status_message.emit(tr("bulk_delete.done", count=count))

    # ── 图标管理 ──

    def add_icon(self, icon: IconModel) -> IconWidget:
        widget = IconWidget(icon, self.icon_cache_dir)
        widget.icon_double_clicked.connect(self.icon_double_clicked.emit)
        widget.rename_requested.connect(self._on_rename_requested)
        if self._batch_mode:
            widget.set_batch_mode(True)

        self._layout.insert_widget_at(icon.sort_order, widget)
        self._icon_widgets[icon.id] = widget

        widget.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        widget.customContextMenuRequested.connect(
            lambda pos, w=widget: self._show_icon_context_menu(pos, w)
        )
        return widget

    def remove_icon(self, icon_id: str):
        widget = self._icon_widgets.pop(icon_id, None)
        if widget:
            self._layout.remove_widget(widget)
            widget.deleteLater()

    def rebuild_from_model(self):
        tab_icons = {i.id: i for i in self.tab.icons}
        for icon_id in list(self._icon_widgets.keys()):
            if icon_id not in tab_icons:
                self.remove_icon(icon_id)
        items = list(self._layout._items)
        sorted_widgets = sorted(
            items,
            key=lambda item: tab_icons.get(
                item.widget().icon_model.id if item.widget() else "",
                IconModel()
            ).sort_order
        )
        self._layout.set_items(sorted_widgets)
        self._container.updateGeometry()

    # ── 搜索 ──

    def set_search_visible(self, visible: bool):
        """Show or hide the icon search/filter bar."""
        if visible and not self._search_bar.isHidden():
            return  # 已在显示状态，无需重复处理
        self._search_bar.setVisible(visible)
        if visible:
            self._search_bar.setFocus()
        else:
            self._search_bar.clear()
            self.search_closed.emit()

    def is_search_visible(self) -> bool:
        """Whether the search/filter bar is currently shown.

        Uses isHidden() (explicit state) rather than isVisible(),
        which would require the whole ancestor chain to be shown.
        """
        return not self._search_bar.isHidden()

    def eventFilter(self, obj, event):
        """Esc 键关闭搜索栏（事件过滤器安装在搜索框上）。"""
        if obj is self._search_bar and event.type() == QEvent.Type.KeyPress:
            if event.key() == Qt.Key.Key_Escape:
                self.set_search_visible(False)
                return True
        return super().eventFilter(obj, event)

    def _apply_filter(self, text: str):
        """Filter visible icons by display name (case-insensitive substring)."""
        query = text.strip().lower()
        for icon_id, widget in self._icon_widgets.items():
            name = widget.icon_model.display_name.lower()
            widget.setVisible(query == "" or query in name)
        self._layout.invalidate()
        self._container.updateGeometry()

    # ── 图标大小 ──

    def update_cell_size(self, cell_w: int, cell_h: int):
        """Update FlowLayout cell dimensions and refresh all widgets."""
        self._layout.cell_width = cell_w
        self._layout.cell_height = cell_h
        for widget in self._icon_widgets.values():
            widget.setFixedSize(cell_w, cell_h)
            widget.refresh_icon()
            # Recalculate name label layout
            widget.name_label.set_text(widget.icon_model.display_name)
        self._layout.invalidate()
        self._container.updateGeometry()

    # ── 拖放 ──

    def _on_internal_drop(self, info: dict, x: int, y: int):
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

    # ── 右键菜单 ──

    def _show_icon_context_menu(self, pos, widget: IconWidget):
        """图标右键菜单。

        菜单项说明（措辞需让用户一目了然）：
        - 打开           → 用默认程序打开（同双击）
        - 用其他应用打开… → 调出 Windows「打开方式」对话框，仅文件/文件夹/快捷方式可用
        - 打开文件位置   → 在资源管理器中定位该文件
        - 编辑属性…      → 修改名称/路径/参数等属性（不是替换图标图片！）
        - 重命名         → 直接改名
        - 删除           → 移除该图标
        """
        menu = QMenu(self)
        icon = widget.icon_model

        # 打开（同双击行为）
        open_action = menu.addAction(tr("icon.menu.open"))
        open_action.triggered.connect(lambda: self.icon_double_clicked.emit(icon.id))

        # 用其他应用打开 — 仅对真实文件/文件夹/快捷方式有意义
        if icon.type in (IconType.FILE, IconType.FOLDER, IconType.SHORTCUT):
            open_with_action = menu.addAction(tr("icon.menu.open_with"))
            open_with_action.triggered.connect(lambda: self._open_with(icon))

        # 在资源管理器中定位文件
        open_loc_action = menu.addAction(tr("icon.menu.open_location"))
        open_loc_action.triggered.connect(lambda: self._open_file_location(icon))

        menu.addSeparator()

        # 编辑属性（名称/路径/参数），保持与创建对话框一致
        edit_action = menu.addAction(tr("icon.menu.edit"))
        edit_action.triggered.connect(lambda: self._edit_icon(icon))

        rename_action = menu.addAction(tr("icon.menu.rename"))
        rename_action.triggered.connect(lambda: widget.name_label._start_edit())

        remove_action = menu.addAction(tr("icon.menu.remove"))
        remove_action.triggered.connect(lambda: self._remove_icon(widget.icon_model.id))

        menu.exec(widget.mapToGlobal(pos))

    def _edit_icon(self, icon: IconModel):
        """打开编辑对话框，保存后刷新图标。"""
        from PyQt6.QtWidgets import QDialog
        from .icon_edit_dialog import IconEditDialog
        from .services.icon_resolver import IconResolver

        old_path = icon.target_path or icon.source_path
        dlg = IconEditDialog(icon, self)
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        ok, err_key = dlg.apply()
        if not ok:
            self.status_message.emit(tr(err_key or "status.edit_invalid"))
            return

        resolver = IconResolver(self.icon_cache_dir)
        reextract = False

        # 1) 快捷方式：自定义图标优先；未设置时回退到目标路径默认图标
        if icon.type == IconType.SHORTCUT:
            spec = dlg.get_custom_icon_spec()
            if spec:
                pixmap = resolver.extract_icon_from_file(spec[0], spec[1])
                if pixmap and not pixmap.isNull():
                    self._delete_cache(icon)
                    new_name = f"{uuid.uuid4()}.png"
                    pixmap.save(str(resolver.cache_dir / new_name), "PNG")
                    icon.icon_cache_file = new_name
                # 无论自定义图标是否提取成功，都不再走默认重提取
            else:
                reextract = True

        # 2) 文件/文件夹路径变化 → 重新提取
        if icon.type in (IconType.FILE, IconType.FOLDER):
            new_path = icon.target_path or icon.source_path
            if new_path and new_path != old_path:
                reextract = True

        if reextract:
            new_path = icon.target_path or icon.source_path
            if new_path:
                new_cache = resolver.extract_and_cache(new_path)
                if new_cache:
                    self._delete_cache(icon)
                    icon.icon_cache_file = new_cache

        self.data_store.save()
        # 刷新显示
        w = self._icon_widgets.get(icon.id)
        if w:
            w.name_label.set_text(icon.display_name)
            w.refresh_icon()
        self.status_message.emit(tr("status.edited", name=icon.display_name))

    def _delete_cache(self, icon: IconModel):
        if icon.icon_cache_file:
            old = self.icon_cache_dir / icon.icon_cache_file
            if old.exists():
                try:
                    old.unlink()
                except (OSError, PermissionError):
                    pass

    def _on_rename_requested(self, icon_id: str, new_name: str):
        self.data_store.rename_icon(icon_id, new_name)
        self.status_message.emit(tr("status.renamed", name=new_name))

    def _remove_icon(self, icon_id: str):
        widget = self._icon_widgets.get(icon_id)
        name = widget.icon_model.display_name if widget else tr("icon.remove.unknown")
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
        path = icon.target_path or icon.source_path
        if not path:
            return
        if os.path.isfile(path):
            subprocess.Popen(['explorer', '/select,', path])
        elif os.path.isdir(path):
            os.startfile(path)
        else:
            self.status_message.emit(tr("status.path_missing", path=path))

    def _open_with(self, icon: IconModel):
        """Show Windows 'Open with...' dialog for a file/folder/shortcut."""
        path = icon.target_path or icon.source_path
        if not path or not os.path.exists(path):
            self.status_message.emit(tr("status.path_missing", path=path))
            return
        try:
            subprocess.Popen(['rundll32.exe', 'shell32.dll,OpenAs_RunDLL', path])
        except Exception as e:
            self.status_message.emit(tr("status.open_with_failed", err=str(e)))
