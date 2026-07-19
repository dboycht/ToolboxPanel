"""快捷键参考对话框 · Shortcut Reference Dialog."""
from PyQt6.QtWidgets import (QDialog, QVBoxLayout, QTableWidget, QTableWidgetItem,
                              QDialogButtonBox, QHeaderView, QAbstractItemView)
from PyQt6.QtCore import Qt
from .i18n import tr


SHORTCUTS = [
    # (action_key, default_shortcut)
    ("shortcut.new_tab",       "Ctrl+T"),
    ("shortcut.close_tab",     "Ctrl+W"),
    ("shortcut.rename_tab",    "Ctrl+R / F2"),
    ("shortcut.prev_tab",      "←"),
    ("shortcut.next_tab",      "→"),
    ("shortcut.batch_mode",    "Ctrl+B"),
    ("shortcut.batch_delete",  "Shift+Delete"),
    ("shortcut.new_file",      "Ctrl+Shift+F"),
    ("shortcut.new_folder",    "Ctrl+Shift+O"),
    ("shortcut.new_url",       "Ctrl+Shift+U"),
    ("shortcut.new_command",   "Ctrl+Shift+P"),
    ("shortcut.open_icon",     "Enter / DoubleClick"),
    ("shortcut.rename_icon",   "F2"),
    ("shortcut.delete_icon",   "Delete"),
    ("shortcut.reset_data",    "Ctrl+Shift+R"),
    ("shortcut.export_data",   "Ctrl+Shift+E"),
    ("shortcut.import_data",   "Ctrl+Shift+I"),
    ("shortcut.exit_app",      "Alt+F4"),
]


def show_shortcut_dialog(parent=None):
    dlg = QDialog(parent)
    dlg.setWindowTitle(tr("shortcut.dialog.title"))
    dlg.setMinimumSize(420, 440)

    layout = QVBoxLayout(dlg)

    table = QTableWidget(len(SHORTCUTS), 2)
    table.setHorizontalHeaderLabels([tr("shortcut.col.action"), tr("shortcut.col.key")])
    table.horizontalHeader().setStretchLastSection(True)
    table.horizontalHeader().setSectionResizeMode(0, QHeaderView.ResizeMode.Stretch)
    table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
    table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
    table.verticalHeader().setVisible(False)
    table.setAlternatingRowColors(True)
    table.setStyleSheet("""
        QTableWidget {
            gridline-color: #e0e0e0;
            font-size: 10pt;
        }
        QHeaderView::section {
            background-color: #f0f0f0;
            padding: 6px;
            font-weight: bold;
        }
    """)

    for i, (action_key, shortcut) in enumerate(SHORTCUTS):
        action_item = QTableWidgetItem(tr(action_key))
        shortcut_item = QTableWidgetItem(shortcut)
        shortcut_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
        table.setItem(i, 0, action_item)
        table.setItem(i, 1, shortcut_item)

    table.resizeRowsToContents()
    layout.addWidget(table)

    btns = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok)
    btns.button(QDialogButtonBox.StandardButton.Ok).setText(tr("btn.ok"))
    btns.accepted.connect(dlg.accept)
    layout.addWidget(btns)

    dlg.exec()
