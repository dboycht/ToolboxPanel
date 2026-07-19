"""多行换行标签栏 — 窗口缩小时自动换行而非隐藏。"""
from PyQt6.QtWidgets import (QWidget, QPushButton, QApplication, QSizePolicy)
from PyQt6.QtCore import Qt, pyqtSignal, QPoint, QMimeData
from PyQt6.QtGui import QDrag, QPainter, QPixmap

from .flow_layout import FlowLayout


class _TabButton(QPushButton):
    """单个标签按钮，支持拖拽排序。"""

    dragged = pyqtSignal(int, int)       # from_index, to_index
    double_clicked = pyqtSignal()

    def __init__(self, text: str, index: int, parent=None):
        super().__init__(text, parent)
        self._index = index
        self._drag_start: QPoint | None = None
        self.setCheckable(True)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Fixed)
        self.setStyleSheet(_tab_style(False))
        # 根据文字长度自动调整按钮宽度
        fm = self.fontMetrics()
        text_w = fm.horizontalAdvance(text) + 32  # padding
        self.setFixedWidth(max(40, min(200, text_w)))

    def set_selected(self, sel: bool):
        self.setChecked(sel)
        self.setStyleSheet(_tab_style(sel))

    def mouseDoubleClickEvent(self, event):
        self.double_clicked.emit()
        super().mouseDoubleClickEvent(event)

    # ── 拖拽排序 ──
    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self._drag_start = event.position().toPoint()
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event):
        if self._drag_start is None:
            return
        dist = (event.position().toPoint() - self._drag_start).manhattanLength()
        if dist < QApplication.startDragDistance():
            return
        drag = QDrag(self)
        mime = QMimeData()
        mime.setData("application/x-toolbox-tab", str(self._index).encode("utf-8"))
        drag.setMimeData(mime)
        pix = self.grab()
        ghost = QPixmap(pix.size())
        ghost.fill(Qt.GlobalColor.transparent)
        p = QPainter(ghost)
        p.setOpacity(0.7)
        p.drawPixmap(0, 0, pix)
        p.end()
        drag.setPixmap(ghost)
        drag.setHotSpot(QPoint(ghost.width() // 2, ghost.height() // 2))
        self._drag_start = None
        drag.exec(Qt.DropAction.MoveAction)

    def dragEnterEvent(self, event):
        if event.mimeData().hasFormat("application/x-toolbox-tab"):
            event.acceptProposedAction()

    def dragMoveEvent(self, event):
        if event.mimeData().hasFormat("application/x-toolbox-tab"):
            event.acceptProposedAction()

    def dropEvent(self, event):
        data = event.mimeData().data("application/x-toolbox-tab")
        if data:
            try:
                from_idx = int(bytes(data).decode("utf-8"))
                if from_idx != self._index:
                    self.dragged.emit(from_idx, self._index)
            except (ValueError, UnicodeDecodeError):
                pass
        event.acceptProposedAction()


def _tab_style(selected: bool) -> str:
    if selected:
        return """
            QPushButton {
                background-color: #ffffff;
                color: #1e1e1e;
                border: none;
                border-radius: 8px;
                padding: 5px 16px;
                font-size: 9pt;
                font-weight: 600;
            }
        """
    return """
        QPushButton {
            background-color: transparent;
            color: #5a5a5a;
            border: none;
            border-radius: 8px;
            padding: 5px 16px;
            font-size: 9pt;
        }
        QPushButton:hover {
            background-color: rgba(0, 0, 0, 0.04);
            color: #333333;
        }
    """


class WrapTabBar(QWidget):
    """多行标签栏。

    使用 FlowLayout，窗口宽度不足时自动换行，不会隐藏标签。
    支持双击重命名、右键菜单、拖拽排序。
    """

    current_changed = pyqtSignal(int)
    tab_moved = pyqtSignal(int, int)             # from_index, to_index
    rename_requested = pyqtSignal(int)
    context_menu_requested = pyqtSignal(int)     # index

    def __init__(self, parent=None):
        super().__init__(parent)
        self._layout = FlowLayout(cell_width=0, cell_height=30,
                                  h_spacing=4, v_spacing=4, margin=4)
        self.setLayout(self._layout)
        self.setAcceptDrops(True)
        self._buttons: list[_TabButton] = []
        self._current_index = -1

    # ── 公开 API ──

    def add_tab(self, name: str) -> int:
        idx = len(self._buttons)
        btn = _TabButton(name, idx, self)
        btn.dragged.connect(self._on_drag_reorder)
        btn.double_clicked.connect(lambda: self.rename_requested.emit(idx))
        btn.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        btn.customContextMenuRequested.connect(lambda pos: self.context_menu_requested.emit(idx))
        btn.clicked.connect(lambda checked, i=idx: self.set_current(i))
        self._layout.insert_widget_at(idx, btn)
        self._buttons.append(btn)
        if self._current_index < 0:
            self.set_current(0)
        return idx

    def remove_tab(self, index: int):
        if 0 <= index < len(self._buttons):
            btn = self._buttons.pop(index)
            self._layout.remove_widget(btn)
            btn.deleteLater()
            # 重新编号
            for i, b in enumerate(self._buttons):
                b._index = i
            if self._current_index >= len(self._buttons):
                self.set_current(len(self._buttons) - 1)

    def set_tab_text(self, index: int, text: str):
        if 0 <= index < len(self._buttons):
            self._buttons[index].setText(text)

    def tab_text(self, index: int) -> str:
        if 0 <= index < len(self._buttons):
            return self._buttons[index].text()
        return ""

    def count(self) -> int:
        return len(self._buttons)

    def set_current(self, index: int):
        if 0 <= index < len(self._buttons) and index != self._current_index:
            if self._current_index >= 0 and self._current_index < len(self._buttons):
                self._buttons[self._current_index].set_selected(False)
            self._current_index = index
            if index < len(self._buttons):
                self._buttons[index].set_selected(True)
            self.current_changed.emit(index)

    def current_index(self) -> int:
        return self._current_index

    def _on_drag_reorder(self, from_idx: int, to_idx: int):
        if 0 <= from_idx < len(self._buttons) and 0 <= to_idx < len(self._buttons):
            btn = self._buttons.pop(from_idx)
            self._buttons.insert(to_idx, btn)
            # 重新插入到 layout 的正确位置
            self._layout.remove_widget(btn)
            self._layout.insert_widget_at(to_idx, btn)
            # 重新编号
            for i, b in enumerate(self._buttons):
                b._index = i
            self._current_index = to_idx if self._current_index == from_idx else self._current_index
            self.tab_moved.emit(from_idx, to_idx)

    def update_all_texts(self, texts: list[str]):
        """批量更新所有标签文字（用于语言切换）。"""
        for i, txt in enumerate(texts):
            if i < len(self._buttons):
                self._buttons[i].setText(txt)
