"""可编辑图标标签 — 双击可改名。

规则：
- 文字超宽时自动换行
- 长拉丁单词在行尾用「-」连字符断开
- 超过三行才在末尾显示 "…" 省略
- 文字居中，背景 pill 比文字略宽即可
"""
import re
from PyQt6.QtWidgets import QFrame, QLabel, QLineEdit, QVBoxLayout, QSizePolicy
from PyQt6.QtCore import Qt, pyqtSignal, QRect
from PyQt6.QtGui import QFontMetrics

PILL_PAD_H = 3   # 背景 pill 水平内边距（紧凑）
PILL_PAD_V = 1   # 背景 pill 垂直内边距


class IconLabel(QFrame):
    """显示图标名称。"""

    MAX_LINES = 3

    editing_finished = pyqtSignal(str)
    clicked = pyqtSignal()

    def __init__(self, text: str = "", parent=None):
        super().__init__(parent)
        self.setFrameShape(QFrame.Shape.NoFrame)
        self._full_text = text

        self.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Preferred)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        # 名称标签
        self._label = QLabel(text, self)
        self._label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._label.setWordWrap(True)
        self._label.setStyleSheet(f"""
            QLabel {{
                color: #ffffff;
                font-size: 10px;
                background-color: rgba(30, 30, 30, 0.55);
                border-radius: 4px;
                padding: {PILL_PAD_V}px {PILL_PAD_H}px;
            }}
        """)

        # 编辑框
        self._editor = QLineEdit(self)
        self._editor.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._editor.setStyleSheet("""
            QLineEdit {
                font-size: 10px;
                color: #ffffff;
                background-color: rgba(30, 30, 30, 0.75);
                border: 1px solid #60cdff;
                border-radius: 4px;
                padding: 1px 4px;
            }
        """)
        self._editor.hide()

        layout.addWidget(self._label)
        layout.addWidget(self._editor)

        self._editor.editingFinished.connect(self._finish_edit)

        # 初始布局
        self._update_label()

    # ── 核心：文本适配 ──

    def _text_width(self) -> int:
        """可用于文字的实际宽度。"""
        fw = self.width()
        if fw < 30:
            fw = 68  # 布局前的默认估算
        # QLabel 宽度 = fw - Frame 的 margins(0) = fw
        # 文字可用的宽度 = QLabel 宽度 - 左右 padding × 2
        return max(20, fw - PILL_PAD_H * 2)

    def _update_label(self):
        """重新计算显示文本和高度。"""
        tw = self._text_width()

        fm = QFontMetrics(self._label.font())
        line_h = fm.lineSpacing()
        max_h = self.MAX_LINES * line_h

        # 1) 连字符处理
        hyphenated = self._hyphenate(self._full_text, fm, tw)

        # 2) 超出三行则省略
        display = self._elide_to_height(hyphenated, fm, tw, max_h)

        self._label.setText(display)
        self._label.setToolTip(self._full_text)

        # 3) 测量实际行数，计算 QLabel 需要的高度
        bound = fm.boundingRect(QRect(0, 0, tw, 9999), Qt.TextFlag.TextWordWrap, display)
        actual_lines = max(1, (bound.height() + line_h - 1) // line_h)
        lines = min(actual_lines, self.MAX_LINES)
        # QLabel 高度 = 文字高度 + 垂直 padding
        qlabel_h = lines * line_h + PILL_PAD_V * 2

        self._label.setMinimumHeight(qlabel_h)
        self._label.setMaximumHeight(qlabel_h)

        # QFrame 高度 = QLabel 高度（无额外 margin）
        self.setMinimumHeight(qlabel_h)
        self.setMaximumHeight(qlabel_h)

    def _hyphenate(self, text: str, fm: QFontMetrics, max_width: int) -> str:
        """对于超出 max_width 的纯拉丁单词，在行尾插入 「-」 连字符。"""
        if not text or max_width <= 0:
            return text

        parts = re.split(r'(\s+|(?<=[^\w])|(?=[^\w]))', text)
        result = []

        for part in parts:
            if not part or part.isspace() or len(part) <= 1:
                result.append(part)
                continue

            is_latin = all(c.isascii() and c.isalpha() for c in part)
            if not is_latin or fm.horizontalAdvance(part) <= max_width:
                result.append(part)
                continue

            hyphened = ""
            chunk = ""
            for ch in part:
                test = chunk + ch
                if fm.horizontalAdvance(test) > max_width and chunk:
                    hyphened += chunk + "-"
                    chunk = ch
                else:
                    chunk = test
            if chunk:
                hyphened += chunk
            result.append(hyphened)

        return "".join(result)

    def _elide_to_height(self, text: str, fm: QFontMetrics, width: int, max_h: int) -> str:
        """超过三行则在末尾加 「…」。"""
        if not text:
            return ""

        bound = fm.boundingRect(QRect(0, 0, width, 9999), Qt.TextFlag.TextWordWrap, text)
        if bound.height() <= max_h:
            return text

        lo, hi = 1, len(text)
        best = ""
        while lo <= hi:
            mid = (lo + hi) // 2
            candidate = text[:mid] + "…"
            b = fm.boundingRect(QRect(0, 0, width, 9999), Qt.TextFlag.TextWordWrap, candidate)
            if b.height() <= max_h:
                best = candidate
                lo = mid + 1
            else:
                hi = mid - 1
        return best or (text[:1] + "…")

    # ── 公共接口 ──

    def set_text(self, text: str):
        self._full_text = text
        self._update_label()

    def text(self) -> str:
        return self._full_text

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self._update_label()

    def mouseDoubleClickEvent(self, event):
        self._start_edit()

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self.clicked.emit()
        super().mousePressEvent(event)

    def _start_edit(self):
        self._editor.setText(self._full_text)
        self._label.hide()
        self._editor.show()
        self._editor.setFocus()
        self._editor.selectAll()

    def _finish_edit(self):
        new_text = self._editor.text().strip()
        if new_text and new_text != self._full_text:
            self._full_text = new_text
            self._update_label()
            self.editing_finished.emit(new_text)
        self._editor.hide()
        self._label.show()
        self.clearFocus()
