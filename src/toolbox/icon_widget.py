"""A single icon widget: icon image + editable label, with drag support."""
from pathlib import Path

from PyQt6.QtWidgets import QWidget, QVBoxLayout, QLabel, QApplication, QStyle
from PyQt6.QtCore import Qt, pyqtSignal, QPoint, QMimeData
from PyQt6.QtGui import QPixmap, QDrag, QPainter, QColor, QMouseEvent

from .models.icon_model import IconModel, IconType
from .icon_label import IconLabel


class IconWidget(QWidget):
    """Displays an icon with its name. Supports drag-to-rearrange."""

    icon_clicked = pyqtSignal(str)           # icon_id
    icon_double_clicked = pyqtSignal(str)    # icon_id -> open
    drag_started = pyqtSignal(str)           # icon_id
    rename_requested = pyqtSignal(str, str)  # icon_id, new_name

    # Fixed dimensions (height accommodates up to 3 lines of label text)
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

        # Layout
        layout = QVBoxLayout(self)
        layout.setContentsMargins(3, 4, 3, 2)
        layout.setSpacing(2)
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)

        # Icon image
        self.icon_label = QLabel(self)
        self.icon_label.setFixedSize(self.ICON_SIZE, self.ICON_SIZE)
        self.icon_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.icon_label.setStyleSheet("background: transparent; border: none;")
        layout.addWidget(self.icon_label, alignment=Qt.AlignmentFlag.AlignHCenter)

        # Editable name
        self.name_label = IconLabel(icon_model.display_name, self)
        layout.addWidget(self.name_label, alignment=Qt.AlignmentFlag.AlignHCenter)

        # Load the icon pixmap
        self._load_icon()

        # Connect signals
        self.name_label.clicked.connect(lambda: self.icon_clicked.emit(self.icon_model.id))
        self.name_label.editing_finished.connect(
            lambda new_name: self.rename_requested.emit(self.icon_model.id, new_name)
        )

        # Default style
        self.setStyleSheet(self._base_style())

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

    def _load_icon(self):
        """Load the cached icon or use a fallback."""
        pixmap = None

        if self.icon_model.icon_cache_file:
            cache_path = self.icon_cache_dir / self.icon_model.icon_cache_file
            if cache_path.exists():
                pixmap = QPixmap(str(cache_path))

        if pixmap is None or pixmap.isNull():
            pixmap = self._get_fallback_icon()

        scaled = pixmap.scaled(
            self.ICON_SIZE, self.ICON_SIZE,
            Qt.AspectRatioMode.KeepAspectRatio,
            Qt.TransformationMode.SmoothTransformation
        )
        self.icon_label.setPixmap(scaled)

    def _get_fallback_icon(self) -> QPixmap:
        """Return a generic fallback icon based on the icon type."""
        style = QApplication.style()
        if not style:
            return self._empty_pixmap()

        mapping = {
            IconType.FILE: QStyle.StandardPixmap.SP_FileIcon,
            IconType.FOLDER: QStyle.StandardPixmap.SP_DirIcon,
            IconType.SHORTCUT: QStyle.StandardPixmap.SP_FileLinkIcon,
            IconType.URL: QStyle.StandardPixmap.SP_ComputerIcon,
            IconType.COMMAND: QStyle.StandardPixmap.SP_CommandLink,
        }
        std_icon = mapping.get(self.icon_model.type, QStyle.StandardPixmap.SP_FileIcon)
        return style.standardIcon(std_icon).pixmap(self.ICON_SIZE, self.ICON_SIZE)

    def _empty_pixmap(self) -> QPixmap:
        pixmap = QPixmap(self.ICON_SIZE, self.ICON_SIZE)
        pixmap.fill(Qt.GlobalColor.transparent)
        return pixmap

    def refresh_icon(self):
        """Reload the icon from cache."""
        self._load_icon()

    # ---- Drag support ----

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
        """Initiate a drag operation for rearranging."""
        drag = QDrag(self)
        mime_data = QMimeData()
        mime_data.setText(self.icon_model.id)
        mime_data.setData("application/x-toolbox-icon",
                          f'{{"icon_id":"{self.icon_model.id}"}}'.encode("utf-8"))
        drag.setMimeData(mime_data)

        # Create a semi-transparent drag pixmap
        original = self.grab()
        ghost = QPixmap(original.size())
        ghost.fill(Qt.GlobalColor.transparent)
        painter = QPainter(ghost)
        painter.setOpacity(0.65)
        painter.drawPixmap(0, 0, original)
        painter.end()

        drag.setPixmap(ghost)
        drag.setHotSpot(QPoint(self.WIDGET_WIDTH // 2, self.WIDGET_HEIGHT // 2))

        self.drag_started.emit(self.icon_model.id)
        drag.exec(Qt.DropAction.MoveAction)
        self._drag_start_pos = None
