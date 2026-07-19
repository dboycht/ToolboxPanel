"""进度对话框 — QProgressBar + 日志输出。"""
from PyQt6.QtWidgets import (QDialog, QVBoxLayout, QProgressBar, QTextEdit,
                              QDialogButtonBox, QLabel, QPushButton)
from PyQt6.QtCore import Qt
from .i18n import tr


class ProgressDialog(QDialog):
    """显示进度条、当前操作和日志列表的对话框。"""

    def __init__(self, title: str, parent=None):
        super().__init__(parent)
        self.setWindowTitle(title)
        self.setMinimumSize(480, 360)
        self.setModal(True)

        layout = QVBoxLayout(self)

        # 当前操作
        self._status_label = QLabel(tr("progress.preparing"))
        self._status_label.setStyleSheet("font-weight: bold; font-size: 10pt;")
        layout.addWidget(self._status_label)

        # 进度条
        self._progress = QProgressBar()
        self._progress.setMinimum(0)
        self._progress.setMaximum(100)
        layout.addWidget(self._progress)

        # 日志区域
        self._log = QTextEdit()
        self._log.setReadOnly(True)
        self._log.setStyleSheet("font-size: 9pt; font-family: Consolas, Microsoft YaHei UI;")
        layout.addWidget(self._log)

        # 关闭按钮（操作完成后启用）
        self._btn_box = QDialogButtonBox()
        self._close_btn = QPushButton(tr("btn.ok"))
        self._close_btn.setEnabled(False)
        self._close_btn.clicked.connect(self.accept)
        self._btn_box.addButton(self._close_btn, QDialogButtonBox.ButtonRole.AcceptRole)
        layout.addWidget(self._btn_box)

        self._max_log_lines = 200
        self._closed = False

    def set_status(self, text: str):
        self._status_label.setText(text)

    def set_progress(self, current: int, total: int):
        if total > 0:
            pct = int(current / total * 100)
            self._progress.setValue(pct)

    def append_log(self, text: str):
        self._log.append(text)
        # 限制日志行数
        if self._log.document().blockCount() > self._max_log_lines:
            cursor = self._log.textCursor()
            cursor.movePosition(cursor.MoveOperation.Start)
            cursor.select(cursor.SelectionType.BlockUnderCursor)
            cursor.removeSelectedText()
            cursor.deleteChar()
        # 滚动到底部
        scrollbar = self._log.verticalScrollBar()
        scrollbar.setValue(scrollbar.maximum())

    def mark_done(self, success: bool, message: str):
        self._close_btn.setEnabled(True)
        if success:
            self.set_status(tr("progress.done"))
        else:
            self.set_status(tr("progress.failed", err=message))
        self.append_log(message)

    def closeEvent(self, event):
        # 操作中不允许关闭
        if not self._close_btn.isEnabled():
            event.ignore()
        else:
            super().closeEvent(event)
