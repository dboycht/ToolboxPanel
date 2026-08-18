"""列表式标签页 — 两列列表：说明 + 文件/文件夹地址。"""
import os
from pathlib import Path

from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QTreeWidget, QTreeWidgetItem,
                              QMenu, QMessageBox, QDialog, QFormLayout,
                              QLineEdit, QHBoxLayout, QPushButton,
                              QDialogButtonBox, QFileDialog, QLabel,
                              QHeaderView, QAbstractItemView,
                              QApplication)
from PyQt6.QtCore import Qt, pyqtSignal, QEvent, QPoint, QMimeData
from PyQt6.QtGui import QKeyEvent, QDrag, QPainter, QPixmap

from .models.data_store import DataStore
from .models.tab_model import TabModel
from .models.list_item_model import ListItemModel
from .i18n import tr

# 列表行内部拖拽的 MIME 类型（内容为 item_id 的 UTF-8 文本）
_LIST_DRAG_MIME = "application/x-toolbox-list-item"


class _ListTree(QTreeWidget):
    """列表页专用 QTreeWidget：重写拖放虚函数，把内部行排序交给宿主处理。

    为什么不用 QTreeWidget 内置 InternalMove：drop 位置计算在顶层列表场景
    会失败，此时 Qt 的 startDrag 补偿逻辑会删除源行（行消失）且顺序不变；
    且 QTreeModel::moveRows 在 Qt 6 被禁用。
    为什么不用 viewport 事件过滤器收 Drop：PyQt6 中 QDropEvent 由
    QDragManager 特殊投递，不经过事件过滤器（实测 sendEvent 只触发
    User/Mouse 事件的过滤器）。拖放必须走 QTreeWidget 的虚函数重写。
    """

    def __init__(self, host: "ListTabPage", parent=None):
        super().__init__(parent)
        self._host = host

    def dragEnterEvent(self, event):
        if event.mimeData().hasFormat(_LIST_DRAG_MIME):
            event.acceptProposedAction()
        else:
            super().dragEnterEvent(event)

    def dragMoveEvent(self, event):
        if event.mimeData().hasFormat(_LIST_DRAG_MIME):
            self._host._show_drop_feedback(event.position().toPoint())
            event.acceptProposedAction()
        else:
            super().dragMoveEvent(event)

    def dragLeaveEvent(self, event):
        self._host._clear_drop_feedback()
        super().dragLeaveEvent(event)

    def dropEvent(self, event):
        if event.mimeData().hasFormat(_LIST_DRAG_MIME):
            self._host._handle_drop(event)
            event.acceptProposedAction()
            self._host._clear_drop_feedback()
        else:
            super().dropEvent(event)


class ListTabPage(QWidget):
    """两列列表页：

    - 列 0：文本说明（双击内联编辑，悬停显示完整文本）
    - 列 1：文件/文件夹地址（单击打开，双击弹选择对话框）
    - 右键行 → 删除
    - 列分割线可拖拽（QHeaderView Interactive）
    """

    status_message = pyqtSignal(str)

    def __init__(self, tab: TabModel, data_store: DataStore, parent=None):
        super().__init__(parent)
        self.tab = tab
        self.data_store = data_store

        layout = QVBoxLayout(self)
        layout.setContentsMargins(4, 4, 4, 4)
        layout.setSpacing(0)

        self._tree = _ListTree(self)
        self._tree.setColumnCount(2)
        self._tree.setHeaderLabels([tr("list.col.desc"), tr("list.col.path")])
        header = self._tree.header()
        header.setSectionResizeMode(0, QHeaderView.ResizeMode.Interactive)
        header.setSectionResizeMode(1, QHeaderView.ResizeMode.Interactive)
        header.setStretchLastSection(True)
        self._tree.setRootIsDecorated(False)
        self._tree.setAlternatingRowColors(True)
        self._tree.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self._tree.setStyleSheet("""
            QTreeWidget {
                background-color: #ffffff;
                alternate-background-color: #f7f9fb;
                border: 1px solid #e0e0e0;
                border-radius: 6px;
                font-size: 10pt;
            }
            QTreeWidget::item {
                padding: 4px 2px;
            }
            QTreeWidget::item:hover {
                background-color: rgba(0, 103, 192, 0.06);
            }
            QTreeWidget::item:selected {
                background-color: rgba(0, 103, 192, 0.12);
                color: #1e1e1e;
            }
            QHeaderView::section {
                background-color: #f0f0f0;
                padding: 5px 8px;
                font-weight: 600;
                border: none;
                border-right: 1px solid #e0e0e0;
                border-bottom: 1px solid #e0e0e0;
            }
        """)

        self._tree.itemChanged.connect(self._on_item_changed)
        self._tree.itemClicked.connect(self._on_item_clicked)
        self._tree.itemDoubleClicked.connect(self._on_item_double_clicked)
        self._tree.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self._tree.customContextMenuRequested.connect(self._on_context_menu)

        # 左键上下拖拽调整行顺序 — 完全自定义拖拽（不用 QTreeWidget 内置
        # InternalMove：drop 位置计算失败时 Qt 会删除源行导致行消失且顺序
        # 不变；QTreeModel::moveRows 在 Qt 6 被禁用）。
        # 鼠标事件（启动 QDrag）走 viewport 过滤器；拖放事件（DragEnter/
        # DragMove/Drop）由 _ListTree 虚函数处理（QDropEvent 不经过过滤器）。
        self._tree.setAcceptDrops(True)
        self._tree.viewport().setAcceptDrops(True)
        self._tree.viewport().installEventFilter(self)
        self._drag_start_pos: QPoint | None = None
        self._drag_triggered = False
        self._suppress_click = False

        layout.addWidget(self._tree)

        # 空列表提示（有行时隐藏）
        self._empty_hint = QLabel(tr("list.empty_hint"))
        self._empty_hint.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._empty_hint.setStyleSheet("color: #999999; font-size: 11pt; padding: 24px;")
        layout.addWidget(self._empty_hint)
        layout.addStretch()

        # 列宽默认值
        self._tree.setColumnWidth(0, 260)
        self._tree.setColumnWidth(1, 320)

        self._suppress_changed = False

        # 恢复已有条目
        for item in sorted(tab.list_items, key=lambda i: i.sort_order):
            self.add_item(item)
        self._update_empty_hint()

    # ── 行管理 ──

    def add_item(self, item: ListItemModel) -> QTreeWidgetItem:
        row = QTreeWidgetItem()
        row.setData(0, Qt.ItemDataRole.UserRole, item.id)
        row.setText(0, item.description)
        row.setText(1, item.path)
        # 仅第一列可编辑（双击内联编辑）
        row.setFlags(row.flags() | Qt.ItemFlag.ItemIsEditable)
        row.setToolTip(0, item.description)
        row.setToolTip(1, item.path)
        self._tree.addTopLevelItem(row)
        return row

    def _row_for_id(self, item_id: str) -> QTreeWidgetItem | None:
        for i in range(self._tree.topLevelItemCount()):
            row = self._tree.topLevelItem(i)
            if row.data(0, Qt.ItemDataRole.UserRole) == item_id:
                return row
        return None

    def eventFilter(self, obj, event):
        """viewport 鼠标事件驱动：左键按住拖动行时启动 QDrag。

        拖放事件（DragEnter/DragMove/Drop）不经过事件过滤器
        （QDropEvent 由 QDragManager 特殊投递），由 _ListTree 虚函数处理。
        """
        if obj is self._tree.viewport():
            etype = event.type()
            if etype == QEvent.Type.MouseButtonPress:
                if event.button() == Qt.MouseButton.LeftButton:
                    self._drag_start_pos = event.position().toPoint()
                    self._drag_triggered = False
            elif etype == QEvent.Type.MouseMove:
                if self._drag_start_pos is not None and not self._drag_triggered:
                    dist = (event.position().toPoint() - self._drag_start_pos).manhattanLength()
                    if dist >= QApplication.startDragDistance():
                        self._drag_triggered = True
                        self._start_drag(event.position().toPoint())
                        return True
            elif etype == QEvent.Type.MouseButtonRelease:
                self._drag_start_pos = None
        return super().eventFilter(obj, event)

    # ── 自定义拖拽 ──

    def _start_drag(self, pos: QPoint):
        """启动行拖拽（半透明 ghost 跟随鼠标）。"""
        index = self._tree.indexAt(pos)
        if not index.isValid():
            self._drag_start_pos = None
            return
        item = self._tree.itemFromIndex(index)
        item_id = item.data(0, Qt.ItemDataRole.UserRole)
        if not item_id:
            self._drag_start_pos = None
            return

        drag = QDrag(self._tree)
        mime = QMimeData()
        mime.setData(_LIST_DRAG_MIME, item_id.encode("utf-8"))
        drag.setMimeData(mime)

        # 半透明 ghost：抓取整行作为拖拽图像
        rect = self._tree.visualItemRect(item)
        pix = self._tree.viewport().grab(rect)
        # 高分屏下 grab 返回的 pixmap 带 devicePixelRatio>1，拖拽显示会被放大，
        # 导致幽灵窗口与鼠标错位（偏左/漂移）；统一按 1:1 处理
        pix.setDevicePixelRatio(1.0)
        ghost = QPixmap(pix.size())
        ghost.fill(Qt.GlobalColor.transparent)
        painter = QPainter(ghost)
        painter.setOpacity(0.6)
        painter.drawPixmap(0, 0, pix)
        painter.end()
        drag.setPixmap(ghost)
        # 以鼠标按下位置为抓取点（相对行左上角），使幽灵窗口紧贴鼠标跟随，
        # 而不是固定在 ghost 中心导致视觉偏移
        hot_x = max(0, min(pix.width() - 1, self._drag_start_pos.x() - rect.x()))
        hot_y = max(0, min(pix.height() - 1, self._drag_start_pos.y() - rect.y()))
        drag.setHotSpot(QPoint(hot_x, hot_y))

        # 拖拽后释放左键可能误触发 itemClicked（打开路径），抑制一次
        self._suppress_click = True
        drag.exec(Qt.DropAction.MoveAction)
        self._drag_start_pos = None
        self._drag_triggered = False

    def _drop_target_row(self, pos: QPoint) -> int:
        """根据 drop 位置返回「插入到该行之前」的行号。"""
        index = self._tree.indexAt(pos)
        if index.isValid():
            item = self._tree.itemFromIndex(index)
            row = self._tree.indexOfTopLevelItem(item)
            rect = self._tree.visualItemRect(item)
            if pos.y() > rect.center().y():
                return row + 1
            return row
        count = self._tree.topLevelItemCount()
        if count == 0:
            return 0
        last = self._tree.topLevelItem(count - 1)
        if pos.y() > self._tree.visualItemRect(last).bottom():
            return count
        return 0

    def _move_row(self, item_id: str, target_row: int) -> bool:
        """把指定 item 的行移动到 target_row（插入到该位置前）。

        返回 True 表示顺序发生了变化。
        """
        from_row = -1
        for i in range(self._tree.topLevelItemCount()):
            if self._tree.topLevelItem(i).data(0, Qt.ItemDataRole.UserRole) == item_id:
                from_row = i
                break
        if from_row < 0:
            return False
        count = self._tree.topLevelItemCount()
        target_row = max(0, min(target_row, count))
        # 移动后位置不变的两种情况：目标即自身 / 目标在自身正下方
        if target_row == from_row or target_row == from_row + 1:
            return False
        row_item = self._tree.takeTopLevelItem(from_row)
        if target_row > from_row:
            target_row -= 1
        self._tree.insertTopLevelItem(target_row, row_item)
        return True

    def _show_drop_feedback(self, pos: QPoint):
        """拖拽悬停时高亮目标行作为插入反馈。"""
        row = self._drop_target_row(pos)
        count = self._tree.topLevelItemCount()
        if 0 <= row < count:
            self._tree.setCurrentItem(self._tree.topLevelItem(row))
        else:
            self._tree.setCurrentItem(None)

    def _clear_drop_feedback(self):
        self._tree.setCurrentItem(None)

    def _handle_drop(self, event):
        """内部拖拽 drop：移动行并同步模型 + 持久化。"""
        data = bytes(event.mimeData().data(_LIST_DRAG_MIME)).decode("utf-8", "replace")
        if not data:
            return
        target_row = self._drop_target_row(event.position().toPoint())
        if self._move_row(data, target_row):
            self._sync_order_after_drop()

    def _sync_order_after_drop(self):
        """读取当前 UI 行顺序，同步到模型并持久化（拖拽 drop 后调用）。"""
        new_ids = []
        for i in range(self._tree.topLevelItemCount()):
            item_id = self._tree.topLevelItem(i).data(0, Qt.ItemDataRole.UserRole)
            if item_id:
                new_ids.append(item_id)
        old_ids = [it.id for it in self.tab.list_items]
        if new_ids and new_ids != old_ids:
            self.data_store.reorder_list_items(self.tab.id, new_ids)
            self.status_message.emit(tr("list.reordered"))

    def _update_empty_hint(self):
        self._empty_hint.setVisible(self._tree.topLevelItemCount() == 0)

    # ── 事件处理 ──

    def _on_item_changed(self, row: QTreeWidgetItem, column: int):
        """内联编辑列 0 完成后持久化。"""
        if self._suppress_changed or column != 0:
            return
        item_id = row.data(0, Qt.ItemDataRole.UserRole)
        text = row.text(0).strip()
        if item_id and text:
            self.data_store.update_list_item(item_id, description=text)
            row.setToolTip(0, text)
            self.status_message.emit(tr("list.desc_updated"))

    def _on_item_clicked(self, row: QTreeWidgetItem, column: int):
        """单击列 1 → 打开文件/文件夹。"""
        if self._suppress_click:
            # 刚结束一次拖拽，抑制由此触发的误点击（防止意外打开路径）
            self._suppress_click = False
            return
        if column != 1:
            return
        path = row.text(1).strip()
        if not path:
            return
        if os.path.exists(path):
            try:
                os.startfile(path)
            except OSError as e:
                self.status_message.emit(tr("status.open_failed", err=str(e)))
        else:
            self.status_message.emit(tr("status.path_missing", path=path))

    def _on_item_double_clicked(self, row: QTreeWidgetItem, column: int):
        """双击列 1 → 弹出文件/文件夹选择对话框。列 0 由内联编辑处理。"""
        if column != 1:
            return
        item_id = row.data(0, Qt.ItemDataRole.UserRole)
        path = self._pick_path(self.tab.name)
        if path:
            self._suppress_changed = True
            row.setText(1, path)
            row.setToolTip(1, path)
            self._suppress_changed = False
            self.data_store.update_list_item(item_id, path=path)
            self.status_message.emit(tr("list.path_updated"))

    def _on_context_menu(self, pos):
        row = self._tree.itemAt(pos)
        if row is None:
            # 空白区域 → 新建列表项
            self._create_item()
            return
        menu = QMenu(self)
        edit_action = menu.addAction(tr("icon.menu.edit"))
        rename_action = menu.addAction(tr("icon.menu.rename"))
        menu.addSeparator()
        delete_action = menu.addAction(tr("icon.menu.remove"))
        chosen = menu.exec(self._tree.viewport().mapToGlobal(pos))
        if chosen == edit_action:
            self._edit_row(row)
        elif chosen == rename_action:
            self._rename_row(row)
        elif chosen == delete_action:
            self._delete_row(row)

    def _edit_row(self, row: QTreeWidgetItem):
        """右键 → 编辑属性…：弹对话框修改说明与路径。"""
        item_id = row.data(0, Qt.ItemDataRole.UserRole)
        result = self.data_store.find_list_item(item_id)
        if not result:
            return
        _, item = result
        values = self._list_item_dialog(tr("list.edit_item"), item.description, item.path)
        if values is None:
            return
        desc, path = values
        if not desc and not path:
            return
        self.data_store.update_list_item(item_id, description=desc, path=path)
        row.setText(0, desc)
        row.setText(1, path)
        row.setToolTip(0, desc)
        row.setToolTip(1, path)
        self.status_message.emit(tr("status.edited", name=desc or path))

    def _rename_row(self, row: QTreeWidgetItem):
        """右键 → 重命名：进入列 0 内联编辑（完成时由 itemChanged 持久化）。"""
        self._tree.editItem(row, 0)

    def _delete_row(self, row: QTreeWidgetItem):
        item_id = row.data(0, Qt.ItemDataRole.UserRole)
        desc = row.text(0) or tr("list.this_row")
        confirm = QMessageBox.question(
            self, tr("list.delete_title"),
            tr("list.confirm_delete", desc=desc),
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
        )
        if confirm != QMessageBox.StandardButton.Yes:
            return
        self.data_store.remove_list_item(item_id)
        idx = self._tree.indexOfTopLevelItem(row)
        self._tree.takeTopLevelItem(idx)
        self._update_empty_hint()
        self.status_message.emit(tr("status.removed", name=desc))

    def _list_item_dialog(self, title: str, desc: str = "", path: str = "") -> tuple[str, str] | None:
        """新建/编辑列表项的通用对话框，返回 (desc, path)；取消返回 None。"""
        dlg = QDialog(self)
        dlg.setWindowTitle(title)
        dlg.setMinimumWidth(420)
        layout = QVBoxLayout(dlg)

        form = QFormLayout()
        desc_edit = QLineEdit(desc)
        desc_edit.setPlaceholderText(tr("list.desc_ph"))
        form.addRow(tr("list.col.desc"), desc_edit)

        # 路径 + 选择文件/文件夹按钮
        path_row = QWidget()
        h = QHBoxLayout(path_row)
        h.setContentsMargins(0, 0, 0, 0)
        h.setSpacing(4)
        path_edit = QLineEdit(path)
        file_btn = QPushButton(tr("list.select_file"))
        folder_btn = QPushButton(tr("list.select_folder"))
        file_btn.clicked.connect(
            lambda: path_edit.setText(
                QFileDialog.getOpenFileName(dlg, tr("list.select_file"))[0] or path_edit.text()
            )
        )
        folder_btn.clicked.connect(
            lambda: path_edit.setText(
                QFileDialog.getExistingDirectory(dlg, tr("list.select_folder")) or path_edit.text()
            )
        )
        h.addWidget(path_edit)
        h.addWidget(file_btn)
        h.addWidget(folder_btn)
        form.addRow(tr("list.col.path"), path_row)
        layout.addLayout(form)

        btns = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        btns.button(QDialogButtonBox.StandardButton.Ok).setText(tr("btn.ok"))
        btns.button(QDialogButtonBox.StandardButton.Cancel).setText(tr("btn.cancel"))
        btns.accepted.connect(dlg.accept)
        btns.rejected.connect(dlg.reject)
        layout.addWidget(btns)

        if dlg.exec() != QDialog.DialogCode.Accepted:
            return None
        return desc_edit.text().strip(), path_edit.text().strip()

    def _create_item(self):
        """空白右键 → 新建列表项对话框。"""
        values = self._list_item_dialog(tr("list.new_item"))
        if values is None:
            return
        desc, path = values
        if not desc and not path:
            return
        item = ListItemModel(description=desc, path=path)
        # add_list_item 内部已把 item 追加到 tab.list_items 并 save()，不要重复 append
        self.data_store.add_list_item(self.tab.id, item)
        self.add_item(item)
        self._update_empty_hint()
        self.status_message.emit(tr("list.item_added", desc=desc or path))

    def _pick_path(self, title: str) -> str | None:
        """弹出一个「选文件或选文件夹」的小对话框。"""
        dlg = QDialog(self)
        dlg.setWindowTitle(tr("list.pick_title", title=title))
        layout = QVBoxLayout(dlg)
        hint = QLabel(tr("list.pick_hint"))
        hint.setStyleSheet("color: #666666;")
        layout.addWidget(hint)
        btns = QHBoxLayout()
        file_btn = QPushButton(tr("list.select_file"))
        folder_btn = QPushButton(tr("list.select_folder"))
        cancel_btn = QPushButton(tr("btn.cancel"))
        btns.addWidget(file_btn)
        btns.addWidget(folder_btn)
        btns.addWidget(cancel_btn)
        layout.addLayout(btns)

        result = {"path": None}

        def pick_file():
            p, _ = QFileDialog.getOpenFileName(dlg, tr("list.select_file"))
            if p:
                result["path"] = p
                dlg.accept()

        def pick_folder():
            p = QFileDialog.getExistingDirectory(dlg, tr("list.select_folder"))
            if p:
                result["path"] = p
                dlg.accept()

        file_btn.clicked.connect(pick_file)
        folder_btn.clicked.connect(pick_folder)
        cancel_btn.clicked.connect(dlg.reject)

        dlg.exec()
        return result["path"]

    # ── 键盘 ──

    def keyPressEvent(self, event):
        """Delete 键删除选中行。"""
        if event.key() == Qt.Key.Key_Delete:
            rows = self._tree.selectedItems()
            if rows:
                self._delete_row(rows[0])
            return
        super().keyPressEvent(event)
