"""Custom QLayout that arranges icons in a phone-home-screen style grid."""
from PyQt6.QtWidgets import QLayout, QWidgetItem
from PyQt6.QtCore import Qt, QRect, QSize


class FlowLayout(QLayout):
    """A grid layout that auto-calculates columns based on available width.

    Items are placed left-to-right, top-to-bottom, with a fixed cell size.
    Excess items scroll vertically (parent must be in a QScrollArea)."""

    def __init__(self, cell_width: int = 68, cell_height: int = 104,
                 h_spacing: int = 10, v_spacing: int = 14, margin: int = 8,
                 parent=None):
        super().__init__(parent)
        self.cell_width = cell_width
        self.cell_height = cell_height
        self.h_spacing = h_spacing
        self.v_spacing = v_spacing
        self.margin = margin
        self._items: list[QWidgetItem] = []
        self._variable_width = (cell_width <= 0)  # 自适应宽度模式

    def addItem(self, item):
        self._items.append(item)

    def count(self) -> int:
        return len(self._items)

    def itemAt(self, index: int):
        if 0 <= index < len(self._items):
            return self._items[index]
        return None

    def takeAt(self, index: int):
        if 0 <= index < len(self._items):
            return self._items.pop(index)
        return None

    def expandingDirections(self):
        return Qt.Orientation(0)  # No expanding

    def hasHeightForWidth(self) -> bool:
        return True

    def heightForWidth(self, width: int) -> int:
        return self._do_layout(QRect(0, 0, width, 0), apply_geometry=False)

    def setGeometry(self, rect: QRect):
        super().setGeometry(rect)
        self._do_layout(rect, apply_geometry=True)

    def minimumSize(self) -> QSize:
        return QSize(self.cell_width + 2 * self.margin,
                     self.cell_height + 2 * self.margin)

    def sizeHint(self) -> QSize:
        # Calculate the actual size needed to fit all items.
        # Use the parent widget's width if available, otherwise a sensible default.
        parent = self.parent()
        if parent:
            width = parent.width()
        else:
            width = 400
        height = self.heightForWidth(width)
        return QSize(width, height)

    def columns_for_width(self, width: int) -> int:
        """Calculate number of columns that fit in the given width."""
        available = width - 2 * self.margin + self.h_spacing
        cols = available // (self.cell_width + self.h_spacing)
        return max(1, cols)

    def cell_index_at_pos(self, x: int, y: int, container_width: int) -> int:
        """Return the insertion index for a drop at (x, y).

        Returns -1 if the position is beyond all cells (append)."""
        cols = self.columns_for_width(container_width)
        if cols == 0:
            return 0

        # Determine row and column from coordinates
        adj_x = x - self.margin
        adj_y = y - self.margin
        col = max(0, adj_x // (self.cell_width + self.h_spacing))
        row = max(0, adj_y // (self.cell_height + self.v_spacing))

        # Clamp column
        col = min(col, cols - 1)

        index = row * cols + col
        if index >= len(self._items):
            return len(self._items)
        return max(0, index)

    def _do_layout(self, rect: QRect, apply_geometry: bool) -> int:
        """Calculate (and optionally apply) widget geometry.

        Returns the total height needed."""
        container_width = rect.width()
        if container_width <= 0:
            return 0

        x = self.margin
        y = self.margin
        row_h = self.cell_height

        for item in self._items:
            wid = item.widget()
            if not wid:
                continue

            if self._variable_width:
                w = max(10, wid.sizeHint().width())
            else:
                w = self.cell_width

            # 换行判断
            if x + w > container_width - self.margin and x > self.margin:
                x = self.margin
                y += row_h + self.v_spacing

            if apply_geometry and not wid.isHidden():
                gw = w
                gh = self.cell_height
                wid.setGeometry(QRect(x, y, gw, gh))

            x += w + self.h_spacing

        total_height = y + self.cell_height + self.margin
        return total_height

    def insert_widget_at(self, index: int, widget):
        """Insert a widget at a specific position in the layout.

        Uses addWidget() to ensure Qt properly adopts the widget
        (reparents it to the layout's parent, makes it visible, etc.),
        then reorders the internal item list to the desired position.
        """
        self.addWidget(widget)                     # Qt 标准 API：自动 reparent + show
        item = self._items.pop()                   # addWidget 把 item 加到了末尾
        if index >= len(self._items):
            self._items.append(item)               # 放回末尾
        else:
            self._items.insert(index, item)        # 插入正确位置
        self.invalidate()
        if self.parent():
            self.parent().updateGeometry()

    def move_widget(self, from_index: int, to_index: int):
        """Move a widget from one position to another."""
        if 0 <= from_index < len(self._items) and 0 <= to_index < len(self._items):
            item = self._items.pop(from_index)
            self._items.insert(to_index, item)
            self.invalidate()
            if self.parent():
                self.parent().updateGeometry()

    def remove_widget(self, widget):
        """Remove a specific widget from the layout.

        Properly hides the widget and clears its parent so it can be
        reparented or deleted without leaving stale references.
        """
        for i, item in enumerate(self._items):
            if item.widget() is widget:
                self._items.pop(i)
                widget.hide()
                widget.setParent(None)
                self.invalidate()
                if self.parent():
                    self.parent().updateGeometry()
                return True
        return False

    def index_of(self, widget) -> int:
        """Return the index of a widget, or -1 if not found."""
        for i, item in enumerate(self._items):
            if item.widget() is widget:
                return i
        return -1
