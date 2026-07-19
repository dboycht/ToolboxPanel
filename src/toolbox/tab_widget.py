"""标签页组件 — 管理标签页和图标网格。使用可换行的 WrapTabBar。"""
import uuid
from pathlib import Path

from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QStackedWidget, QMenu,
                              QMessageBox, QPushButton, QHBoxLayout,
                              QDialog, QFormLayout, QLabel, QLineEdit,
                              QDialogButtonBox, QFileDialog)
from PyQt6.QtCore import pyqtSignal, Qt, QEvent

from .models.data_store import DataStore
from .models.tab_model import TabModel
from .models.icon_model import IconModel, IconType
from .icon_grid import IconGrid
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
    status_message = pyqtSignal(str)

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

    def restore_tabs(self, tabs: list[TabModel]):
        """从已保存状态重建标签页。"""
        while self._stack.count() > 0:
            self._stack.removeWidget(self._stack.widget(0))
        self._icon_grids.clear()
        self._tab_records.clear()

        for i in range(self._tab_bar.count()):
            self._tab_bar.remove_tab(0)

        for tab in sorted(tabs, key=lambda t: t.order):
            self.add_tab_page(tab)

    def add_tab_page(self, tab: TabModel):
        """添加一个带图标网格的标签页。"""
        idx = self._tab_bar.add_tab(tab.name)
        self._tab_records.insert(idx, {"id": tab.id, "name": tab.name, "order": idx})

        grid = IconGrid(tab, self.data_store, self.data_store.icons_dir)
        grid.icon_removed.connect(self._on_icon_removed)
        grid.icon_moved.connect(self._on_icon_moved_between_tabs)
        grid.icon_double_clicked.connect(self._on_icon_open)
        grid.files_dropped.connect(lambda paths: self._add_dropped_paths(paths, grid))
        grid.status_message.connect(self.status_message.emit)

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
        if self._stack.count() == 1:
            self._stack.setCurrentIndex(0)
        return idx

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
                tp = ti.get("target_path", str(path))
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
        rename_action = menu.addAction(tr("tab.menu.rename"))
        menu.addSeparator()
        delete_action = menu.addAction(tr("tab.menu.delete"))
        chosen = menu.exec(self._tab_bar.mapToGlobal(self._tab_bar.pos()))
        if chosen == new_tab_action:
            self.new_tab_requested.emit()
        elif chosen == rename_action:
            self._rename_tab(tab_idx)
        elif chosen == delete_action:
            self._delete_tab(tab_idx)

    def _rename_tab(self, tab_index: int):
        current_name = self._tab_bar.tab_text(tab_index)
        new_name = self._prompt_text(tr("tab.rename.title"), tr("tab.rename.prompt"), current_name)
        if new_name is not None and new_name != current_name:
            self._tab_bar.set_tab_text(tab_index, new_name)
            # 找到对应 grid 更新模型
            for grid in self._icon_grids.values():
                if self._stack.indexOf(grid) == tab_index:
                    self.data_store.rename_tab(grid.tab.id, new_name)
                    grid.tab.name = new_name
                    break
            self.status_message.emit(tr("tab.renamed", name=new_name))

    def _delete_tab(self, tab_index: int):
        if self._stack.count() <= 1:
            QMessageBox.warning(self, tr("tab.delete.blocked_title"), tr("tab.delete.blocked"))
            return
        grid = self._stack.widget(tab_index)
        if not isinstance(grid, IconGrid):
            return
        name = self._tab_bar.tab_text(tab_index)
        confirm = QMessageBox.question(
            self, tr("tab.delete.title"),
            tr("tab.delete.confirm", name=name),
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
        )
        if confirm == QMessageBox.StandardButton.Yes:
            self.data_store.remove_tab(grid.tab.id)
            self._icon_grids.pop(grid.tab.id, None)
            self._stack.removeWidget(grid)
            self._tab_bar.remove_tab(tab_index)
            grid.deleteLater()
            self.status_message.emit(tr("tab.deleted", name=name))

    # ── 键盘快捷操作 ──

    def eventFilter(self, obj, event):
        if obj is self._tab_bar and event.type() == QEvent.Type.MouseButtonDblClick:
            # 从 tab_bar 自身找到被双击的按钮索引
            pos = event.position().toPoint()
            child = self._tab_bar.childAt(pos)
            if child and hasattr(child, '_index'):
                self._rename_tab(child._index)
                return True
        return super().eventFilter(obj, event)

    def keyPressEvent(self, event):
        key = event.key()
        ctrl = bool(event.modifiers() & Qt.KeyboardModifier.ControlModifier)
        shift = bool(event.modifiers() & Qt.KeyboardModifier.ShiftModifier)

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
            from PyQt6.QtWidgets import QLineEdit, QAbstractSpinBox
            focus = self.window().focusWidget()
            if isinstance(focus, (QLineEdit, QAbstractSpinBox)):
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
        menu.addSeparator()
        a3 = menu.addAction(tr("grid.menu.url"))
        a4 = menu.addAction(tr("grid.menu.command"))
        chosen = menu.exec(grid._container.mapToGlobal(pos))
        if chosen == a1: self._create_file_icon(grid)
        elif chosen == a2: self._create_folder_icon(grid)
        elif chosen == a3: self._create_url_icon(grid)
        elif chosen == a4: self._create_command_icon(grid)

    def _create_file_icon(self, grid: IconGrid):
        p, _ = QFileDialog.getOpenFileName(self, tr("grid.dialog.select_file"))
        if p: self._add_dropped_paths([p], grid)

    def _create_folder_icon(self, grid: IconGrid):
        p = QFileDialog.getExistingDirectory(self, tr("grid.dialog.select_folder"))
        if p: self._add_dropped_paths([p], grid)

    def _create_url_icon(self, grid: IconGrid):
        dlg = QDialog(self)
        dlg.setWindowTitle(tr("url.dialog.title"))
        dlg.setMinimumWidth(420)
        lo = QVBoxLayout(dlg)
        form = QFormLayout()
        ne = QLineEdit(); ne.setPlaceholderText(tr("url.placeholder.name"))
        ue = QLineEdit(); ue.setPlaceholderText(tr("url.placeholder.url"))
        form.addRow(tr("url.label.name"), ne)
        form.addRow(tr("url.label.url"), ue)
        lo.addLayout(form)
        btns = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        btns.button(QDialogButtonBox.StandardButton.Ok).setText(tr("btn.ok"))
        btns.button(QDialogButtonBox.StandardButton.Cancel).setText(tr("btn.cancel"))
        btns.accepted.connect(dlg.accept); btns.rejected.connect(dlg.reject)
        lo.addWidget(btns)
        if dlg.exec() == QDialog.DialogCode.Accepted:
            name = ne.text().strip(); url = ue.text().strip()
            if name and url:
                if not url.startswith(("http://", "https://", "ftp://")): url = "https://" + url
                c = f"{uuid.uuid4()}.png"
                self.icon_resolver._get_fallback(IconType.URL).save(str(self.data_store.icons_dir / c), "PNG")
                icon = IconModel(type=IconType.URL, display_name=name, source_path=url, target_path=url, icon_cache_file=c)
                self.data_store.add_icon(grid.tab.id, icon); grid.add_icon(icon)
                self.status_message.emit(tr("url.created", name=name))

    def _create_command_icon(self, grid: IconGrid):
        dlg = QDialog(self)
        dlg.setWindowTitle(tr("cmd.dialog.title"))
        dlg.setMinimumWidth(480)
        lo = QVBoxLayout(dlg)
        form = QFormLayout()
        ne = QLineEdit(); ne.setPlaceholderText(tr("cmd.placeholder.name"))
        cw = QWidget(); cl = QHBoxLayout(cw); cl.setContentsMargins(0,0,0,0)
        ce = QLineEdit(); ce.setPlaceholderText(tr("cmd.placeholder.command"))
        bb = QPushButton("…"); bb.setFixedWidth(36)
        bb.clicked.connect(lambda: ce.setText(QFileDialog.getOpenFileName(dlg, tr("cmd.dialog.select_exe"))[0] or ce.text()))
        cl.addWidget(ce); cl.addWidget(bb)
        ae = QLineEdit(); ae.setPlaceholderText(tr("cmd.placeholder.args"))
        ww = QWidget(); wl = QHBoxLayout(ww); wl.setContentsMargins(0,0,0,0)
        we = QLineEdit(); we.setPlaceholderText(tr("cmd.placeholder.wd"))
        wb = QPushButton("…"); wb.setFixedWidth(36)
        wb.clicked.connect(lambda: we.setText(QFileDialog.getExistingDirectory(dlg, tr("cmd.dialog.select_wd")) or we.text()))
        wl.addWidget(we); wl.addWidget(wb)
        form.addRow(tr("cmd.label.name"), ne)
        form.addRow(tr("cmd.label.command"), cw)
        form.addRow(tr("cmd.label.args"), ae)
        form.addRow(tr("cmd.label.wd"), ww)
        lo.addLayout(form)
        btns = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        btns.button(QDialogButtonBox.StandardButton.Ok).setText(tr("btn.ok"))
        btns.button(QDialogButtonBox.StandardButton.Cancel).setText(tr("btn.cancel"))
        btns.accepted.connect(dlg.accept); btns.rejected.connect(dlg.reject)
        lo.addWidget(btns)
        if dlg.exec() == QDialog.DialogCode.Accepted:
            name = ne.text().strip(); cmd = ce.text().strip()
            args = ae.text().strip(); wd = we.text().strip()
            if name and cmd:
                c = f"{uuid.uuid4()}.png"
                self.icon_resolver._get_fallback(IconType.COMMAND).save(str(self.data_store.icons_dir / c), "PNG")
                icon = IconModel(type=IconType.COMMAND, display_name=name,
                                 source_path=f"{cmd} {args}".strip(), target_path=cmd,
                                 arguments=args, working_dir=wd, icon_cache_file=c)
                self.data_store.add_icon(grid.tab.id, icon); grid.add_icon(icon)
                self.status_message.emit(tr("cmd.created", name=name))

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
