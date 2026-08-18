"""A single icon widget: icon image + editable label, with drag support."""
from pathlib import Path

from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QLabel,
                              QApplication, QStyle, QCheckBox)
from PyQt6.QtCore import Qt, pyqtSignal, QPoint, QMimeData
from PyQt6.QtGui import QPixmap, QDrag, QPainter, QMouseEvent

from .models.icon_model import IconModel
from .icon_label import IconLabel

# Icon size presets — used by View > Icon Size menu
SIZE_PRESETS = {
    "small":  {"icon": 32, "widget_w": 52, "widget_h": 82},
    "medium": {"icon": 48, "widget_w": 68, "widget_h": 104},
    "large":  {"icon": 64, "widget_w": 84, "widget_h": 126},
}


class IconWidget(QWidget):
    """Displays an icon with its name. Supports drag-to-rearrange."""

    icon_double_clicked = pyqtSignal(str)          # icon_id -> open
    rename_requested = pyqtSignal(str, str)        # icon_id, new_name

    ICON_SIZE = 48
    WIDGET_WIDTH = 68
    WIDGET_HEIGHT = 104

    def __init__(self, icon_model: IconModel, icon_cache_dir: Path, parent=None):
        super().__init__(parent)
        self.icon_model = icon_model
        self.icon_cache_dir = icon_cache_dir
        self._drag_start_pos: QPoint | None = None
        self._hovered = False

        self.setFixedSize(self.WIDGET_WIDTH, self.WIDGET_HEIGHT)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setMouseTracking(True)

        # Main vertical layout
        layout = QVBoxLayout(self)
        layout.setContentsMargins(3, 1, 3, 2)
        layout.setSpacing(2)
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)

        # Top row: spacer + checkbox (for batch mode, normally hidden)
        self._check = QCheckBox(self)
        self._check.setFixedSize(18, 18)
        self._check.setStyleSheet("""
            QCheckBox {
                background-color: #ffffff;
                border: 2px solid #0067c0;
                border-radius: 3px;
                spacing: 0px;
            }
            QCheckBox:checked {
                background-color: #0067c0;
            }
        """)
        self._check.hide()

        top_row = QHBoxLayout()
        top_row.setContentsMargins(0, 0, 0, 0)
        top_row.setSpacing(0)
        top_row.addStretch()
        top_row.addWidget(self._check)
        layout.addLayout(top_row)

        # Icon image
        self.icon_label = QLabel(self)
        self.icon_label.setFixedSize(self.ICON_SIZE, self.ICON_SIZE)
        self.icon_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.icon_label.setStyleSheet("background: transparent; border: none;")
        layout.addWidget(self.icon_label, alignment=Qt.AlignmentFlag.AlignHCenter)

        # Editable name
        self.name_label = IconLabel(icon_model.display_name, self)
        layout.addWidget(self.name_label, alignment=Qt.AlignmentFlag.AlignHCenter)

        # Bottom stretch
        layout.addStretch()

        self._load_icon()

        self.name_label.editing_finished.connect(
            lambda new_name: self.rename_requested.emit(self.icon_model.id, new_name)
        )

        self.setStyleSheet(self._base_style())

    # ── 样式 ──

    def _base_style(self) -> str:
        return """
            IconWidget {
                background-color: transparent;
                border: 1px solid transparent;
                border-radius: 8px;
            }
        """

    def _hover_style(self) -> str:
        return """
            IconWidget {
                background-color: rgba(0, 103, 192, 0.08);
                border: 1px solid rgba(0, 103, 192, 0.25);
                border-radius: 8px;
            }
        """

    def enterEvent(self, event):
        self._hovered = True
        self.setStyleSheet(self._hover_style())
        super().enterEvent(event)

    def leaveEvent(self, event):
        self._hovered = False
        self.setStyleSheet(self._base_style())
        super().leaveEvent(event)

    # ── 批量管理 ──

    def set_batch_mode(self, on: bool):
        self._check.setVisible(on)
        if not on:
            self._check.setChecked(False)

    def is_checked(self) -> bool:
        return self._check.isChecked()

    # ── 图标 ──

    def _load_icon(self):
        pixmap = None
        if self.icon_model.icon_cache_file:
            cache_path = self.icon_cache_dir / self.icon_model.icon_cache_file
            if cache_path.exists():
                pixmap = QPixmap(str(cache_path))
        if pixmap is None or pixmap.isNull():
            pixmap = self._get_fallback_icon()
        scaled = pixmap.scaled(self.ICON_SIZE, self.ICON_SIZE,
                               Qt.AspectRatioMode.KeepAspectRatio,
                               Qt.TransformationMode.SmoothTransformation)
        self.icon_label.setPixmap(scaled)

    def _get_fallback_icon(self) -> QPixmap:
        style = QApplication.style()
        if not style:
            p = QPixmap(self.ICON_SIZE, self.ICON_SIZE)
            p.fill(Qt.GlobalColor.transparent)
            return p
        from .services.icon_resolver import FALLBACK_ICON_MAP
        std = FALLBACK_ICON_MAP.get(self.icon_model.type, QStyle.StandardPixmap.SP_FileIcon)
        return style.standardIcon(std).pixmap(self.ICON_SIZE, self.ICON_SIZE)

    def refresh_icon(self):
        """Reload icon pixmap (e.g., after path change or size change)."""
        self.icon_label.setFixedSize(self.ICON_SIZE, self.ICON_SIZE)
        self._load_icon()

    @classmethod
    def apply_size_preset(cls, preset_name: str):
        """Apply a size preset to the entire class (affects future widgets)."""
        preset = SIZE_PRESETS.get(preset_name, SIZE_PRESETS["medium"])
        cls.ICON_SIZE = preset["icon"]
        cls.WIDGET_WIDTH = preset["widget_w"]
        cls.WIDGET_HEIGHT = preset["widget_h"]

    # ── 拖拽 ──

    def mousePressEvent(self, event: QMouseEvent):
        if event.button() == Qt.MouseButton.LeftButton:
            self._drag_start_pos = event.position().toPoint()
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event: QMouseEvent):
        if self._drag_start_pos is None:
            return
        dist = (event.position().toPoint() - self._drag_start_pos).manhattanLength()
        if dist < QApplication.startDragDistance():
            return
        self._start_drag()

    def mouseDoubleClickEvent(self, event: QMouseEvent):
        if event.button() == Qt.MouseButton.LeftButton:
            self.icon_double_clicked.emit(self.icon_model.id)

    def mouseReleaseEvent(self, event: QMouseEvent):
        self._drag_start_pos = None
        super().mouseReleaseEvent(event)

    def _start_drag(self):
        drag = QDrag(self)
        mime_data = QMimeData()
        mime_data.setText(self.icon_model.id)
        mime_data.setData("application/x-toolbox-icon",
                          f'{{"icon_id":"{self.icon_model.id}"}}'.encode("utf-8"))
        drag.setMimeData(mime_data)
        original = self.grab()
        ghost = QPixmap(original.size())
        ghost.fill(Qt.GlobalColor.transparent)
        painter = QPainter(ghost)
        painter.setOpacity(0.65)
        painter.drawPixmap(0, 0, original)
        painter.end()
        drag.setPixmap(ghost)
        drag.setHotSpot(QPoint(self.WIDGET_WIDTH // 2, self.WIDGET_HEIGHT // 2))
        drag.exec(Qt.DropAction.MoveAction)
        self._drag_start_pos = None
