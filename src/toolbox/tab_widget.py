"""标签页组件 — 管理标签页和图标网格。使用可换行的 WrapTabBar。"""
import uuid
from pathlib import Path

from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QStackedWidget, QMenu,
                              QMessageBox,
                              QDialog, QLabel, QLineEdit,
                              QDialogButtonBox, QFileDialog)
from PyQt6.QtCore import pyqtSignal, Qt

from .models.data_store import DataStore
from .models.tab_model import TabModel
from .models.icon_model import IconModel, IconType
from .icon_grid import IconGrid
from .list_tab_page import ListTabPage
from .wrap_tab_bar import WrapTabBar
from .services.icon_resolver import IconResolver
from .services.launcher import Launcher
from .i18n import tr


class TabWidget(QWidget):
    """管理多个标签页，每个标签页包含一个图标网格。

    用 QStackedWidget + WrapTabBar 替代 QTabWidget，
    使标签栏在窗口缩小时自动多行换行。
    """

    new_tab_requested = pyqtSignal()
    new_list_tab_requested = pyqtSignal()
    status_message = pyqtSignal(str)
    search_closed = pyqtSignal()

    def __init__(self, data_store: DataStore, parent=None):
        super().__init__(parent)
        self.data_store = data_store
        self.icon_resolver = IconResolver(data_store.icons_dir)
        self.launcher = Launcher()

        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        # 多行标签栏
        self._tab_bar = WrapTabBar()
        self._tab_bar.current_changed.connect(self._on_tab_switched)
        self._tab_bar.tab_moved.connect(self._on_tab_moved)
        self._tab_bar.rename_requested.connect(self._rename_tab)
        self._tab_bar.context_menu_requested.connect(self._show_tab_context_menu)

        # 标签页内容区域
        self._stack = QStackedWidget()

        layout.addWidget(self._tab_bar)
        layout.addWidget(self._stack)

        # tab_id → (page_index, IconGrid)
        self._icon_grids: dict[str, IconGrid] = {}
        self._list_pages: dict[str, ListTabPage] = {}
        self._tab_records: list[dict] = []  # [{id, name, order}, ...]

    # ── 公开 API (兼容旧 QTabWidget 接口) ──

    def count(self) -> int:
        return self._stack.count()

    def currentIndex(self) -> int:
        return self._stack.currentIndex()

    def setCurrentIndex(self, index: int):
        self._tab_bar.set_current(index)

    def currentWidget(self):
        return self._stack.currentWidget()

    def widget(self, index: int):
        return self._stack.widget(index)

    def tabText(self, index: int) -> str:
        return self._tab_bar.tab_text(index)

    def setTabText(self, index: int, text: str):
        self._tab_bar.set_tab_text(index, text)

    def tabBar(self):
        return self._tab_bar  # 兼容 eventFilter（双击标签重命名）

    # ── 标签页管理 ──

    def clear_all(self):
        """Remove all tabs, grids, and widgets. Used for data reset."""
        while self._stack.count() > 0:
            w = self._stack.widget(0)
            self._stack.removeWidget(w)
            w.deleteLater()
        self._icon_grids.clear()
        self._list_pages.clear()
        self._tab_records.clear()
        while self._tab_bar.count() > 0:
            self._tab_bar.remove_tab(0)

    def restore_tabs(self, tabs: list[TabModel]):
        """从已保存状态重建标签页。"""
        while self._stack.count() > 0:
            w = self._stack.widget(0)
            self._stack.removeWidget(w)
            w.deleteLater()
        self._icon_grids.clear()
        self._list_pages.clear()
        self._tab_records.clear()

        for i in range(self._tab_bar.count()):
            self._tab_bar.remove_tab(0)

        for tab in sorted(tabs, key=lambda t: t.order):
            self.add_tab_page(tab)

    def add_tab_page(self, tab: TabModel):
        """按 tab_type 添加图标网格或列表页。"""
        idx = self._tab_bar.add_tab(tab.name)
        self._tab_records.insert(idx, {"id": tab.id, "name": tab.name, "order": idx})

        if tab.tab_type == "list":
            self._add_list_page(tab, idx)
        else:
            self._add_grid_page(tab, idx)

        if self._stack.count() == 1:
            self._stack.setCurrentIndex(0)
        return idx

    def _add_grid_page(self, tab: TabModel, idx: int):
        grid = IconGrid(tab, self.data_store, self.data_store.icons_dir)
        grid.icon_removed.connect(self._on_icon_removed)
        grid.icon_moved.connect(self._on_icon_moved_between_tabs)
        grid.icon_double_clicked.connect(self._on_icon_open)
        grid.files_dropped.connect(lambda paths: self._add_dropped_paths(paths, grid))
        grid.status_message.connect(self.status_message.emit)
        grid.search_closed.connect(self.search_closed.emit)

        # 空白区域右键菜单
        grid._container.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        grid._container.customContextMenuRequested.connect(
            lambda pos: self._show_grid_context_menu(pos, grid)
        )

        # 恢复已有图标
        for icon in sorted(tab.icons, key=lambda i: i.sort_order):
            grid.add_icon(icon)

        self._icon_grids[tab.id] = grid
        self._stack.insertWidget(idx, grid)

    def _add_list_page(self, tab: TabModel, idx: int):
        page = ListTabPage(tab, self.data_store)
        page.status_message.connect(self.status_message.emit)
        self._list_pages[tab.id] = page
        self._stack.insertWidget(idx, page)

    def _get_current_grid(self) -> IconGrid | None:
        w = self._stack.currentWidget()
        if isinstance(w, IconGrid):
            return w
        return None

    def _get_grid_for_tab(self, tab_id: str) -> IconGrid | None:
        return self._icon_grids.get(tab_id)

    # ── 外部拖放 ──

    def _add_dropped_paths(self, paths: list[str], target_grid: IconGrid):
        for path_str in paths:
            path = Path(path_str)
            if not path.exists():
                self.status_message.emit(tr("status.path_not_found", path=path_str))
                continue
            source_path = str(path.resolve())
            existing = any(i.source_path == source_path for i in target_grid.tab.icons)
            if existing:
                self.status_message.emit(tr("status.already_exists", name=path.name))
                continue
            if path_str.lower().endswith(".lnk"):
                icon_type = IconType.SHORTCUT
                ti = self.icon_resolver.resolve_shortcut(str(path))
                tp = ti.get("target_path") or str(path)
                args = ti.get("arguments", "")
                wd = ti.get("working_dir", "")
                cache = self.icon_resolver.extract_and_cache(str(path))
            elif path.is_dir():
                icon_type = IconType.FOLDER
                tp, args, wd = source_path, "", ""
                cache = self.icon_resolver.extract_and_cache(str(path))
            else:
                icon_type = IconType.FILE
                tp, args, wd = source_path, "", ""
                cache = self.icon_resolver.extract_and_cache(str(path))
            name = path.stem if icon_type != IconType.FOLDER else path.name
            icon = IconModel(type=icon_type, display_name=name, source_path=source_path,
                             target_path=tp, arguments=args, working_dir=wd, icon_cache_file=cache or "")
            self.data_store.add_icon(target_grid.tab.id, icon)
            target_grid.add_icon(icon)
            self.status_message.emit(tr("status.added", name=name))

    # ── 图标移动 ──

    def _on_icon_moved_between_tabs(self, icon_id: str, target_tab_id: str, new_position: int):
        self.data_store.move_icon(icon_id, target_tab_id, new_position)
        for grid in self._icon_grids.values():
            if icon_id in grid._icon_widgets:
                grid.remove_icon(icon_id)
                break
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
        result = self.data_store.find_icon(icon_id)
        if result:
            _, icon = result
            try:
                self.launcher.open(icon)
                self.status_message.emit(tr("status.opened", name=icon.display_name))
            except Exception as e:
                self.status_message.emit(tr("status.open_failed", err=str(e)))

    # ── 标签页切换 ──

    def _on_tab_switched(self, index: int):
        self._stack.setCurrentIndex(index)

    def _on_tab_moved(self, from_idx: int, to_idx: int):
        self.data_store.reorder_tabs(from_idx, to_idx)
        # 同步 stack 中的 widget 顺序
        w = self._stack.widget(from_idx)
        self._stack.removeWidget(w)
        self._stack.insertWidget(to_idx, w)

    # ── 标签页右键菜单 ──

    def _show_tab_context_menu(self, tab_idx: int):
        menu = QMenu(self)
        new_tab_action = menu.addAction(tr("tab.menu.new"))
        new_list_tab_action = menu.addAction(tr("list.new_tab"))
        rename_action = menu.addAction(tr("tab.menu.rename"))
        menu.addSeparator()
        delete_action = menu.addAction(tr("tab.menu.delete"))
        chosen = menu.exec(self._tab_bar.mapToGlobal(self._tab_bar.pos()))
        if chosen == new_tab_action:
            self.new_tab_requested.emit()
        elif chosen == new_list_tab_action:
            self.new_list_tab_requested.emit()
        elif chosen == rename_action:
            self._rename_tab(tab_idx)
        elif chosen == delete_action:
            self._delete_tab(tab_idx)

    def _rename_tab(self, tab_index: int):
        current_name = self._tab_bar.tab_text(tab_index)
        new_name = self._prompt_text(tr("tab.rename.title"), tr("tab.rename.prompt"), current_name)
        if new_name is not None and new_name != current_name:
            self._tab_bar.set_tab_text(tab_index, new_name)
            # 无论网格页还是列表页，都同步模型并持久化
            page = self._stack.widget(tab_index)
            if page is not None and getattr(page, "tab", None) is not None:
                self.data_store.rename_tab(page.tab.id, new_name)
                page.tab.name = new_name
            self.status_message.emit(tr("tab.renamed", name=new_name))

    def _delete_tab(self, tab_index: int):
        if self._stack.count() <= 1:
            QMessageBox.warning(self, tr("tab.delete.blocked_title"), tr("tab.delete.blocked"))
            return
        page = self._stack.widget(tab_index)
        if page is None:
            return
        name = self._tab_bar.tab_text(tab_index)
        confirm = QMessageBox.question(
            self, tr("tab.delete.title"),
            tr("tab.delete.confirm", name=name),
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
        )
        if confirm == QMessageBox.StandardButton.Yes:
            # 找到对应 tab_id 并清理（grid 或 list 两种类型）
            if isinstance(page, IconGrid):
                self.data_store.remove_tab(page.tab.id)
                self._icon_grids.pop(page.tab.id, None)
            elif isinstance(page, ListTabPage):
                self.data_store.remove_tab(page.tab.id)
                self._list_pages.pop(page.tab.id, None)
            self._stack.removeWidget(page)
            self._tab_bar.remove_tab(tab_index)
            page.deleteLater()
            self.status_message.emit(tr("tab.deleted", name=name))

    # ── 键盘快捷操作 ──

    def set_icon_size(self, preset_name: str):
        """Apply an icon size preset to all grids and their widgets."""
        from .icon_widget import IconWidget, SIZE_PRESETS
        preset = SIZE_PRESETS.get(preset_name, SIZE_PRESETS["medium"])
        IconWidget.apply_size_preset(preset_name)
        for grid in self._icon_grids.values():
            grid.update_cell_size(preset["widget_w"], preset["widget_h"])

    def keyPressEvent(self, event):
        key = event.key()
        ctrl = bool(event.modifiers() & Qt.KeyboardModifier.ControlModifier)

        # Ctrl+W — 关闭当前标签页
        if ctrl and key == Qt.Key.Key_W:
            idx = self._stack.currentIndex()
            if idx >= 0:
                self._delete_tab(idx)
            return
        # Ctrl+R — 重命名当前标签页
        if ctrl and key == Qt.Key.Key_R:
            idx = self._stack.currentIndex()
            if idx >= 0:
                self._rename_tab(idx)
            return
        # F2 — 重命名当前标签页
        if key == Qt.Key.Key_F2:
            idx = self._stack.currentIndex()
            if idx >= 0:
                self._rename_tab(idx)
            return
        # Left/Right — 切换标签页
        if key in (Qt.Key.Key_Left, Qt.Key.Key_Right):
            from PyQt6.QtWidgets import (QLineEdit, QAbstractSpinBox,
                                          QTextEdit, QPlainTextEdit)
            focus = self.window().focusWidget()
            if isinstance(focus, (QLineEdit, QAbstractSpinBox, QTextEdit, QPlainTextEdit)):
                super().keyPressEvent(event)
                return
            cnt = self._stack.count()
            if cnt > 1:
                cur = self._stack.currentIndex()
                nxt = cur - 1 if key == Qt.Key.Key_Left and cur > 0 else \
                      cur + 1 if key == Qt.Key.Key_Right and cur < cnt - 1 else \
                      cnt - 1 if key == Qt.Key.Key_Left else 0
                self._tab_bar.set_current(nxt)
            return
        super().keyPressEvent(event)

    # ── 空白区域右键菜单 ──

    def _show_grid_context_menu(self, pos, grid: IconGrid):
        menu = QMenu(self)
        a1 = menu.addAction(tr("grid.menu.file"))
        a2 = menu.addAction(tr("grid.menu.folder"))
        a5 = menu.addAction(tr("grid.menu.shortcut"))
        menu.addSeparator()
        a3 = menu.addAction(tr("grid.menu.url"))
        a4 = menu.addAction(tr("grid.menu.command"))
        chosen = menu.exec(grid._container.mapToGlobal(pos))
        if chosen == a1: self._create_file_icon(grid)
        elif chosen == a2: self._create_folder_icon(grid)
        elif chosen == a5: self._create_shortcut_icon(grid)
        elif chosen == a3: self._create_url_icon(grid)
        elif chosen == a4: self._create_command_icon(grid)

    def _create_file_icon(self, grid: IconGrid):
        from .icon_edit_dialog import IconEditDialog
        p, _ = QFileDialog.getOpenFileName(self, tr("grid.dialog.select_file"))
        if not p:
            return
        path = Path(p)
        dlg = IconEditDialog.create_for_type(
            IconType.FILE,
            parent=self,
            display_name=path.stem,
            source_path=str(path.resolve()),
            target_path=str(path.resolve()),
        )
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        ok, err_key = dlg.apply()
        if not ok:
            self.status_message.emit(tr(err_key or "validate.name_required"))
            return
        icon = dlg.get_created_icon()
        cache = self.icon_resolver.extract_and_cache(icon.source_path)
        icon.icon_cache_file = cache or ""
        self.data_store.add_icon(grid.tab.id, icon)
        grid.add_icon(icon)
        self.status_message.emit(tr("status.added", name=icon.display_name))

    def _create_folder_icon(self, grid: IconGrid):
        from .icon_edit_dialog import IconEditDialog
        p = QFileDialog.getExistingDirectory(self, tr("grid.dialog.select_folder"))
        if not p:
            return
        path = Path(p)
        dlg = IconEditDialog.create_for_type(
            IconType.FOLDER,
            parent=self,
            display_name=path.name,
            source_path=str(path.resolve()),
            target_path=str(path.resolve()),
        )
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        ok, err_key = dlg.apply()
        if not ok:
            self.status_message.emit(tr(err_key or "validate.name_required"))
            return
        icon = dlg.get_created_icon()
        cache = self.icon_resolver.extract_and_cache(icon.source_path)
        icon.icon_cache_file = cache or ""
        self.data_store.add_icon(grid.tab.id, icon)
        grid.add_icon(icon)
        self.status_message.emit(tr("status.added", name=icon.display_name))

    def _create_shortcut_icon(self, grid: IconGrid):
        from .icon_edit_dialog import IconEditDialog
        p, _ = QFileDialog.getOpenFileName(
            self, tr("grid.dialog.select_shortcut"), "",
            "Shortcuts (*.lnk);;All Files (*)"
        )
        if not p:
            return
        path = Path(p)
        ti = self.icon_resolver.resolve_shortcut(str(path))
        tp = ti.get("target_path") or str(path)
        args = ti.get("arguments", "")
        wd = ti.get("working_dir", "")
        dlg = IconEditDialog.create_for_type(
            IconType.SHORTCUT,
            parent=self,
            display_name=path.stem,
            source_path=str(path.resolve()),
            target_path=tp,
            arguments=args,
            working_dir=wd,
        )
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        ok, err_key = dlg.apply()
        if not ok:
            self.status_message.emit(tr(err_key or "validate.name_required"))
            return
        icon = dlg.get_created_icon()
        # 自定义图标优先，否则使用 .lnk 的默认图标
        cache = ""
        spec = dlg.get_custom_icon_spec()
        if spec:
            pixmap = self.icon_resolver.extract_icon_from_file(spec[0], spec[1])
            if pixmap and not pixmap.isNull():
                cache_name = f"{uuid.uuid4()}.png"
                pixmap.save(str(self.data_store.icons_dir / cache_name), "PNG")
                cache = cache_name
        if not cache:
            cache = self.icon_resolver.extract_and_cache(icon.source_path) or ""
        icon.icon_cache_file = cache
        self.data_store.add_icon(grid.tab.id, icon)
        grid.add_icon(icon)
        self.status_message.emit(tr("status.added", name=icon.display_name))

    def _create_url_icon(self, grid: IconGrid):
        from .icon_edit_dialog import IconEditDialog
        dlg = IconEditDialog.create_for_type(IconType.URL, parent=self)
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        ok, err_key = dlg.apply()
        if not ok:
            self.status_message.emit(tr(err_key or "validate.name_required"))
            return
        icon = dlg.get_created_icon()
        cache_name = f"{uuid.uuid4()}.png"
        self.icon_resolver.get_fallback(IconType.URL).save(
            str(self.data_store.icons_dir / cache_name), "PNG")
        icon.icon_cache_file = cache_name
        self.data_store.add_icon(grid.tab.id, icon)
        grid.add_icon(icon)
        self.status_message.emit(tr("url.created", name=icon.display_name))

    def _create_command_icon(self, grid: IconGrid):
        from .icon_edit_dialog import IconEditDialog
        dlg = IconEditDialog.create_for_type(IconType.COMMAND, parent=self)
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        ok, err_key = dlg.apply()
        if not ok:
            self.status_message.emit(tr(err_key or "validate.name_required"))
            return
        icon = dlg.get_created_icon()
        cache_name = f"{uuid.uuid4()}.png"
        self.icon_resolver.get_fallback(IconType.COMMAND).save(
            str(self.data_store.icons_dir / cache_name), "PNG")
        icon.icon_cache_file = cache_name
        self.data_store.add_icon(grid.tab.id, icon)
        grid.add_icon(icon)
        self.status_message.emit(tr("cmd.created", name=icon.display_name))

    @staticmethod
    def _prompt_text(title: str, label: str, default: str = "") -> str | None:
        dlg = QDialog()
        dlg.setWindowTitle(title); dlg.setMinimumWidth(320)
        lo = QVBoxLayout(dlg)
        lo.addWidget(QLabel(label))
        edit = QLineEdit(default); edit.selectAll(); lo.addWidget(edit)
        btns = QDialogButtonBox()
        btns.addButton(tr("btn.ok"), QDialogButtonBox.ButtonRole.AcceptRole)
        btns.addButton(tr("btn.cancel"), QDialogButtonBox.ButtonRole.RejectRole)
        btns.accepted.connect(dlg.accept); btns.rejected.connect(dlg.reject)
        lo.addWidget(btns)
        if dlg.exec() == QDialog.DialogCode.Accepted:
            return edit.text().strip() or default
        return None
