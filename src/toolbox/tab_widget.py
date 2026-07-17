"""标签页组件 — 管理标签页和图标网格。"""
import os
import uuid
from pathlib import Path

from PyQt6.QtWidgets import (QTabWidget, QWidget, QVBoxLayout, QMenu,
                              QInputDialog, QMessageBox, QPushButton, QHBoxLayout,
                              QDialog, QFormLayout, QLabel, QLineEdit,
                              QDialogButtonBox, QFileDialog)
from PyQt6.QtCore import pyqtSignal, Qt, QEvent

from .models.data_store import DataStore
from .models.tab_model import TabModel
from .models.icon_model import IconModel, IconType
from .icon_grid import IconGrid
from .services.icon_resolver import IconResolver
from .services.launcher import Launcher
from .i18n import tr


class TabWidget(QTabWidget):
    """管理多个标签页，每个标签页包含一个图标网格。"""

    new_tab_requested = pyqtSignal()
    status_message = pyqtSignal(str)

    def __init__(self, data_store: DataStore, parent=None):
        super().__init__(parent)
        self.data_store = data_store
        self.icon_resolver = IconResolver(data_store.icons_dir)
        self.launcher = Launcher()

        self.setDocumentMode(True)
        self.setTabsClosable(False)
        self.setMovable(True)

        # 标签页右键菜单
        tab_bar = self.tabBar()
        tab_bar.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        tab_bar.customContextMenuRequested.connect(self._show_tab_context_menu)

        # 标签页拖拽排序
        tab_bar.tabMoved.connect(self._on_tab_moved)

        # 双击标签页名称 → 重命名
        tab_bar.installEventFilter(self)

        # tab_id -> IconGrid 映射
        self._icon_grids: dict[str, IconGrid] = {}

        self.currentChanged.connect(self._on_current_changed)

    def restore_tabs(self, tabs: list[TabModel]):
        """从已保存状态重建标签页。"""
        self.blockSignals(True)
        while self.count() > 0:
            self.removeTab(0)
        self._icon_grids.clear()

        for tab in tabs:
            self.add_tab_page(tab)
        self.blockSignals(False)

    def add_tab_page(self, tab: TabModel):
        """添加一个带图标网格的标签页。"""
        grid = IconGrid(tab, self.data_store, self.data_store.icons_dir)
        grid.icon_added.connect(lambda tab_id, icon: self.data_store.add_icon(tab_id, icon))
        grid.icon_removed.connect(self._on_icon_removed)
        grid.icon_moved.connect(self._on_icon_moved_between_tabs)
        grid.icon_double_clicked.connect(self._on_icon_open)
        grid.files_dropped.connect(lambda paths: self._add_dropped_paths(paths, grid))
        grid.status_message.connect(self.status_message.emit)

        # 空白区域右键菜单 — 连接在容器上（而非 QScrollArea）
        grid._container.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        grid._container.customContextMenuRequested.connect(
            lambda pos: self._show_grid_context_menu(pos, grid)
        )

        # 恢复已有图标
        for icon in sorted(tab.icons, key=lambda i: i.sort_order):
            grid.add_icon(icon)

        self._icon_grids[tab.id] = grid
        index = self.addTab(grid, tab.name)
        return index

    def _get_current_grid(self) -> IconGrid | None:
        current = self.currentWidget()
        if isinstance(current, IconGrid):
            return current
        return None

    def _get_grid_for_tab(self, tab_id: str) -> IconGrid | None:
        return self._icon_grids.get(tab_id)

    # ---- 外部拖放处理 ----

    def _add_dropped_paths(self, paths: list[str], target_grid: IconGrid):
        """处理从资源管理器拖入的文件/文件夹。"""
        for path_str in paths:
            path = Path(path_str)
            if not path.exists():
                self.status_message.emit(tr("status.path_not_found", path=path_str))
                continue

            # 检查当前标签页是否已有相同路径
            source_path = str(path.resolve())
            existing = any(
                i.source_path == source_path for i in target_grid.tab.icons
            )
            if existing:
                self.status_message.emit(tr("status.already_exists", name=path.name))
                continue

            # 判断类型
            if path_str.lower().endswith(".lnk"):
                icon_type = IconType.SHORTCUT
                target_info = self.icon_resolver.resolve_shortcut(str(path))
                target_path = target_info.get("target_path", str(path))
                arguments = target_info.get("arguments", "")
                working_dir = target_info.get("working_dir", "")
                icon_cache_file = self.icon_resolver.extract_and_cache(str(path))
            elif path.is_dir():
                icon_type = IconType.FOLDER
                target_path = source_path
                arguments = ""
                working_dir = ""
                icon_cache_file = self.icon_resolver.extract_and_cache(str(path))
            else:
                icon_type = IconType.FILE
                target_path = source_path
                arguments = ""
                working_dir = ""
                icon_cache_file = self.icon_resolver.extract_and_cache(str(path))

            display_name = path.stem if icon_type != IconType.FOLDER else path.name

            icon = IconModel(
                type=icon_type,
                display_name=display_name,
                source_path=source_path,
                target_path=target_path,
                arguments=arguments,
                working_dir=working_dir,
                icon_cache_file=icon_cache_file or "",
            )

            self.data_store.add_icon(target_grid.tab.id, icon)
            target_grid.add_icon(icon)
            self.status_message.emit(tr("status.added", name=display_name))

    # ---- 图标移动 ----

    def _on_icon_moved_between_tabs(self, icon_id: str, target_tab_id: str, new_position: int):
        """处理跨标签页的图标拖动。"""
        self.data_store.move_icon(icon_id, target_tab_id, new_position)

        # 从源网格移除
        for grid in self._icon_grids.values():
            if icon_id in grid._icon_widgets:
                grid.remove_icon(icon_id)
                break

        # 添加到目标网格
        target_grid = self._get_grid_for_tab(target_tab_id)
        if target_grid:
            result = self.data_store.find_icon(icon_id)
            if result:
                _, icon = result
                target_grid.add_icon(icon)
                target_grid.rebuild_from_model()

        self.status_message.emit(tr("status.moved_tab"))

    def _on_icon_removed(self, icon_id: str):
        self.data_store.remove_icon(icon_id)

    def _on_icon_open(self, icon_id: str):
        """打开/执行图标。"""
        result = self.data_store.find_icon(icon_id)
        if result:
            _, icon = result
            try:
                self.launcher.open(icon)
                self.status_message.emit(tr("status.opened", name=icon.display_name))
            except Exception as e:
                self.status_message.emit(tr("status.open_failed", err=str(e)))

    # ---- 标签页右键菜单 ----

    def _show_tab_context_menu(self, pos):
        """标签页右键菜单。"""
        tab_idx = self.tabBar().tabAt(pos)
        if tab_idx < 0:
            return

        menu = QMenu(self)

        new_tab_action = menu.addAction(tr("tab.menu.new"))
        rename_action = menu.addAction(tr("tab.menu.rename"))
        menu.addSeparator()
        delete_action = menu.addAction(tr("tab.menu.delete"))

        chosen = menu.exec(self.tabBar().mapToGlobal(pos))

        if chosen == new_tab_action:
            self.new_tab_requested.emit()
        elif chosen == rename_action:
            self._rename_tab(tab_idx)
        elif chosen == delete_action:
            self._delete_tab(tab_idx)

    def _rename_tab(self, tab_index: int):
        """重命名标签页（使用本地化按钮的对话框）。"""
        current_name = self.tabText(tab_index)
        new_name = self._prompt_text(
            tr("tab.rename.title"), tr("tab.rename.prompt"), current_name
        )
        if new_name is not None and new_name != current_name:
            self.setTabText(tab_index, new_name)
            grid = self.widget(tab_index)
            if isinstance(grid, IconGrid):
                self.data_store.rename_tab(grid.tab.id, new_name)
                self.status_message.emit(tr("tab.renamed", name=new_name))

    @staticmethod
    def _prompt_text(title: str, label: str, default: str = "") -> str | None:
        """弹出输入对话框。确定 → 返回文本；取消 → 返回 None。"""
        dlg = QDialog()
        dlg.setWindowTitle(title)
        dlg.setMinimumWidth(320)
        layout = QVBoxLayout(dlg)
        layout.addWidget(QLabel(label))
        edit = QLineEdit(default)
        edit.selectAll()
        layout.addWidget(edit)
        btns = QDialogButtonBox()
        btns.addButton(tr("btn.ok"), QDialogButtonBox.ButtonRole.AcceptRole)
        btns.addButton(tr("btn.cancel"), QDialogButtonBox.ButtonRole.RejectRole)
        btns.accepted.connect(dlg.accept)
        btns.rejected.connect(dlg.reject)
        layout.addWidget(btns)
        if dlg.exec() == QDialog.DialogCode.Accepted:
            return edit.text().strip() or default
        return None

    def _delete_tab(self, tab_index: int):
        """删除标签页。"""
        if self.count() <= 1:
            QMessageBox.warning(self, tr("tab.delete.blocked_title"), tr("tab.delete.blocked"))
            return

        grid = self.widget(tab_index)
        if not isinstance(grid, IconGrid):
            return

        name = self.tabText(tab_index)
        confirm = QMessageBox.question(
            self, tr("tab.delete.title"),
            tr("tab.delete.confirm", name=name),
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
        )
        if confirm == QMessageBox.StandardButton.Yes:
            self.data_store.remove_tab(grid.tab.id)
            self._icon_grids.pop(grid.tab.id, None)
            self.removeTab(tab_index)
            self.status_message.emit(tr("tab.deleted", name=name))

    def _on_tab_moved(self, from_index: int, to_index: int):
        self.data_store.reorder_tabs(from_index, to_index)

    def _on_current_changed(self, index: int):
        pass

    # ---- 键盘 & 鼠标快捷操作 ----

    def eventFilter(self, obj, event):
        """双击标签页名称 → 弹出重命名对话框。"""
        if obj is self.tabBar() and event.type() == QEvent.Type.MouseButtonDblClick:
            idx = self.tabBar().tabAt(event.position().toPoint())
            if idx >= 0:
                self._rename_tab(idx)
                return True
        return super().eventFilter(obj, event)

    def keyPressEvent(self, event):
        """F2 = 重命名当前标签页 | ← → = 切换标签页。"""
        key = event.key()
        if key == Qt.Key.Key_F2:
            idx = self.currentIndex()
            if idx >= 0:
                self._rename_tab(idx)
            return
        if key in (Qt.Key.Key_Left, Qt.Key.Key_Right):
            # 仅在焦点不在文本编辑框时切换标签页
            from PyQt6.QtWidgets import QLineEdit, QAbstractSpinBox
            focus = self.window().focusWidget()
            if isinstance(focus, (QLineEdit, QAbstractSpinBox)):
                super().keyPressEvent(event)
                return
            count = self.count()
            if count > 1:
                cur = self.currentIndex()
                if key == Qt.Key.Key_Left:
                    nxt = cur - 1 if cur > 0 else count - 1
                else:
                    nxt = cur + 1 if cur < count - 1 else 0
                self.setCurrentIndex(nxt)
            return
        super().keyPressEvent(event)

    # ---- 空白区域右键菜单 ----

    def _show_grid_context_menu(self, pos, grid: IconGrid):
        """网格空白区域右键菜单。"""
        menu = QMenu(self)

        new_file_action = menu.addAction(tr("grid.menu.file"))
        new_folder_action = menu.addAction(tr("grid.menu.folder"))
        menu.addSeparator()
        new_url_action = menu.addAction(tr("grid.menu.url"))
        new_cmd_action = menu.addAction(tr("grid.menu.command"))

        chosen = menu.exec(grid._container.mapToGlobal(pos))

        if chosen == new_file_action:
            self._create_file_icon(grid)
        elif chosen == new_folder_action:
            self._create_folder_icon(grid)
        elif chosen == new_url_action:
            self._create_url_icon(grid)
        elif chosen == new_cmd_action:
            self._create_command_icon(grid)

    def _create_file_icon(self, grid: IconGrid):
        file_path, _ = QFileDialog.getOpenFileName(self, tr("grid.dialog.select_file"))
        if file_path:
            self._add_dropped_paths([file_path], grid)

    def _create_folder_icon(self, grid: IconGrid):
        folder_path = QFileDialog.getExistingDirectory(self, tr("grid.dialog.select_folder"))
        if folder_path:
            self._add_dropped_paths([folder_path], grid)

    def _create_url_icon(self, grid: IconGrid):
        """新建网址图标对话框。"""
        dialog = QDialog(self)
        dialog.setWindowTitle(tr("url.dialog.title"))
        dialog.setMinimumWidth(420)

        layout = QVBoxLayout(dialog)

        form = QFormLayout()
        name_edit = QLineEdit()
        name_edit.setPlaceholderText(tr("url.placeholder.name"))
        url_edit = QLineEdit()
        url_edit.setPlaceholderText(tr("url.placeholder.url"))

        form.addRow(tr("url.label.name"), name_edit)
        form.addRow(tr("url.label.url"), url_edit)
        layout.addLayout(form)

        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        buttons.button(QDialogButtonBox.StandardButton.Ok).setText(tr("btn.ok"))
        buttons.button(QDialogButtonBox.StandardButton.Cancel).setText(tr("btn.cancel"))
        buttons.accepted.connect(dialog.accept)
        buttons.rejected.connect(dialog.reject)
        layout.addWidget(buttons)

        if dialog.exec() == QDialog.DialogCode.Accepted:
            name = name_edit.text().strip()
            url = url_edit.text().strip()
            if name and url:
                if not url.startswith(("http://", "https://", "ftp://")):
                    url = "https://" + url
                cache_file = f"{uuid.uuid4()}.png"
                pixmap = self.icon_resolver._get_fallback(IconType.URL)
                cache_path = self.data_store.icons_dir / cache_file
                pixmap.save(str(cache_path), "PNG")

                icon = IconModel(
                    type=IconType.URL,
                    display_name=name,
                    source_path=url,
                    target_path=url,
                    icon_cache_file=cache_file,
                )
                self.data_store.add_icon(grid.tab.id, icon)
                grid.add_icon(icon)
                self.status_message.emit(tr("url.created", name=name))

    def _create_command_icon(self, grid: IconGrid):
        """新建命令图标对话框。"""
        dialog = QDialog(self)
        dialog.setWindowTitle(tr("cmd.dialog.title"))
        dialog.setMinimumWidth(480)

        layout = QVBoxLayout(dialog)

        form = QFormLayout()
        name_edit = QLineEdit()
        name_edit.setPlaceholderText(tr("cmd.placeholder.name"))

        # 命令 + 浏览按钮
        cmd_widget = QWidget()
        cmd_layout = QHBoxLayout(cmd_widget)
        cmd_layout.setContentsMargins(0, 0, 0, 0)
        cmd_edit = QLineEdit()
        cmd_edit.setPlaceholderText(tr("cmd.placeholder.command"))
        browse_btn = QPushButton("…")
        browse_btn.setFixedWidth(36)
        browse_btn.clicked.connect(
            lambda: cmd_edit.setText(
                QFileDialog.getOpenFileName(dialog, tr("cmd.dialog.select_exe"))[0] or cmd_edit.text()
            )
        )
        cmd_layout.addWidget(cmd_edit)
        cmd_layout.addWidget(browse_btn)

        args_edit = QLineEdit()
        args_edit.setPlaceholderText(tr("cmd.placeholder.args"))

        wd_widget = QWidget()
        wd_layout = QHBoxLayout(wd_widget)
        wd_layout.setContentsMargins(0, 0, 0, 0)
        wd_edit = QLineEdit()
        wd_edit.setPlaceholderText(tr("cmd.placeholder.wd"))
        wd_browse_btn = QPushButton("…")
        wd_browse_btn.setFixedWidth(36)
        wd_browse_btn.clicked.connect(
            lambda: wd_edit.setText(
                QFileDialog.getExistingDirectory(dialog, tr("cmd.dialog.select_wd")) or wd_edit.text()
            )
        )
        wd_layout.addWidget(wd_edit)
        wd_layout.addWidget(wd_browse_btn)

        form.addRow(tr("cmd.label.name"), name_edit)
        form.addRow(tr("cmd.label.command"), cmd_widget)
        form.addRow(tr("cmd.label.args"), args_edit)
        form.addRow(tr("cmd.label.wd"), wd_widget)
        layout.addLayout(form)

        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        buttons.button(QDialogButtonBox.StandardButton.Ok).setText(tr("btn.ok"))
        buttons.button(QDialogButtonBox.StandardButton.Cancel).setText(tr("btn.cancel"))
        buttons.accepted.connect(dialog.accept)
        buttons.rejected.connect(dialog.reject)
        layout.addWidget(buttons)

        if dialog.exec() == QDialog.DialogCode.Accepted:
            name = name_edit.text().strip()
            command = cmd_edit.text().strip()
            args = args_edit.text().strip()
            wd = wd_edit.text().strip()
            if name and command:
                cache_file = f"{uuid.uuid4()}.png"
                pixmap = self.icon_resolver._get_fallback(IconType.COMMAND)
                cache_path = self.data_store.icons_dir / cache_file
                pixmap.save(str(cache_path), "PNG")

                icon = IconModel(
                    type=IconType.COMMAND,
                    display_name=name,
                    source_path=f"{command} {args}".strip(),
                    target_path=command,
                    arguments=args,
                    working_dir=wd,
                    icon_cache_file=cache_file,
                )
                self.data_store.add_icon(grid.tab.id, icon)
                grid.add_icon(icon)
                self.status_message.emit(tr("cmd.created", name=name))
