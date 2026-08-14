"""列表式标签页 — 两列列表：说明 + 文件/文件夹地址。"""
import os
from pathlib import Path

from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QTreeWidget, QTreeWidgetItem,
                              QMenu, QMessageBox, QDialog, QFormLayout,
                              QLineEdit, QHBoxLayout, QPushButton,
                              QDialogButtonBox, QFileDialog, QLabel,
                              QHeaderView, QAbstractItemView, QSizePolicy)
from PyQt6.QtCore import Qt, pyqtSignal
from PyQt6.QtGui import QKeyEvent

from .models.data_store import DataStore
from .models.tab_model import TabModel
from .models.list_item_model import ListItemModel
from .i18n import tr


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

        self._tree = QTreeWidget()
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
        delete_action = menu.addAction(tr("icon.menu.remove"))
        chosen = menu.exec(self._tree.viewport().mapToGlobal(pos))
        if chosen == delete_action:
            self._delete_row(row)

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

    def _create_item(self):
        """空白右键 → 新建列表项对话框。"""
        dlg = QDialog(self)
        dlg.setWindowTitle(tr("list.new_item"))
        dlg.setMinimumWidth(420)
        layout = QVBoxLayout(dlg)

        form = QFormLayout()
        desc_edit = QLineEdit()
        desc_edit.setPlaceholderText(tr("list.desc_ph"))
        form.addRow(tr("list.col.desc"), desc_edit)

        # 路径 + 选择文件/文件夹按钮
        path_row = QWidget()
        h = QHBoxLayout(path_row)
        h.setContentsMargins(0, 0, 0, 0)
        h.setSpacing(4)
        path_edit = QLineEdit()
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
            return
        desc = desc_edit.text().strip()
        path = path_edit.text().strip()
        if not desc and not path:
            return
        item = ListItemModel(description=desc, path=path)
        self.data_store.add_list_item(self.tab.id, item)
        self.tab.list_items.append(item)
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
