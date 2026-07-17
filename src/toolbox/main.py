"""工具箱入口。"""
import sys

from PyQt6.QtWidgets import QApplication
from PyQt6.QtCore import Qt
from PyQt6.QtGui import QFont, QPalette, QColor

from .app_window import AppWindow


def main():
    app = QApplication(sys.argv)
    app.setApplicationName("工具箱")
    app.setOrganizationName("Toolbox")

    # ---------- Win11 系统字体 ----------
    font = QFont("Microsoft YaHei UI", 9)
    app.setFont(font)

    # ---------- Win11 浅色调色板 ----------
    app.setStyle("Fusion")
    palette = QPalette()
    palette.setColor(QPalette.ColorRole.Window, QColor(243, 243, 243))
    palette.setColor(QPalette.ColorRole.WindowText, QColor(30, 30, 30))
    palette.setColor(QPalette.ColorRole.Base, QColor(255, 255, 255))
    palette.setColor(QPalette.ColorRole.AlternateBase, QColor(249, 249, 249))
    palette.setColor(QPalette.ColorRole.ToolTipBase, QColor(30, 30, 30))
    palette.setColor(QPalette.ColorRole.ToolTipText, QColor(255, 255, 255))
    palette.setColor(QPalette.ColorRole.Text, QColor(30, 30, 30))
    palette.setColor(QPalette.ColorRole.Button, QColor(249, 249, 249))
    palette.setColor(QPalette.ColorRole.ButtonText, QColor(30, 30, 30))
    palette.setColor(QPalette.ColorRole.Highlight, QColor(0, 103, 192))
    palette.setColor(QPalette.ColorRole.HighlightedText, QColor(255, 255, 255))
    app.setPalette(palette)

    # ---------- Win11 样式表 ----------
    app.setStyleSheet("""
        /* ── 主窗口 ── */
        QMainWindow {
            background-color: #f3f3f3;
        }

        /* ── 菜单栏 ── */
        QMenuBar {
            background-color: #f3f3f3;
            color: #1e1e1e;
            padding: 2px 8px;
            border-bottom: 1px solid #e0e0e0;
            font-size: 9pt;
        }
        QMenuBar::item {
            background-color: transparent;
            padding: 6px 10px;
            border-radius: 6px;
            margin: 2px 1px;
        }
        QMenuBar::item:selected {
            background-color: #e0e0e0;
        }
        QMenu {
            background-color: #ffffff;
            color: #1e1e1e;
            border: 1px solid #d0d0d0;
            border-radius: 8px;
            padding: 6px 4px;
        }
        QMenu::item {
            padding: 7px 32px 7px 16px;
            border-radius: 4px;
            margin: 1px 4px;
        }
        QMenu::item:selected {
            background-color: #0067c0;
            color: #ffffff;
        }
        QMenu::separator {
            height: 1px;
            background-color: #e8e8e8;
            margin: 4px 12px;
        }

        /* ── 标签页 ── */
        QTabWidget::pane {
            border: none;
            background-color: #f3f3f3;
        }
        QTabBar::tab {
            background-color: transparent;
            color: #5a5a5a;
            border: none;
            padding: 7px 18px;
            margin: 4px 2px 0 2px;
            border-radius: 8px 8px 0 0;
            font-size: 9pt;
        }
        QTabBar::tab:selected {
            background-color: #ffffff;
            color: #1e1e1e;
            font-weight: 600;
        }
        QTabBar::tab:hover:!selected {
            background-color: rgba(0, 0, 0, 0.04);
            color: #333333;
        }

        /* ── 滚动区域 ── */
        QScrollArea {
            background-color: #ffffff;
            border: none;
            border-radius: 8px;
        }
        QScrollBar:vertical {
            background-color: #f3f3f3;
            width: 8px;
            border-radius: 4px;
        }
        QScrollBar::handle:vertical {
            background-color: #c0c0c0;
            border-radius: 4px;
            min-height: 30px;
        }
        QScrollBar::handle:vertical:hover {
            background-color: #a0a0a0;
        }
        QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {
            height: 0px;
        }
        QScrollBar::add-page:vertical, QScrollBar::sub-page:vertical {
            background: none;
        }

        /* ── 状态栏 ── */
        QStatusBar {
            background-color: #f3f3f3;
            color: #666666;
            border-top: 1px solid #e0e0e0;
            font-size: 8pt;
            padding: 2px 10px;
        }

        /* ── 按钮 ── */
        QPushButton {
            background-color: #0067c0;
            color: #ffffff;
            border: none;
            border-radius: 6px;
            padding: 6px 16px;
            font-size: 9pt;
        }
        QPushButton:hover {
            background-color: #1979ca;
        }
        QPushButton:pressed {
            background-color: #005499;
        }

        /* ── 输入框 ── */
        QLineEdit {
            background-color: #ffffff;
            color: #1e1e1e;
            border: 1px solid #d0d0d0;
            border-radius: 6px;
            padding: 5px 10px;
            font-size: 9pt;
        }
        QLineEdit:focus {
            border-color: #0067c0;
        }

        /* ── 消息框 / 对话框 ── */
        QDialog {
            background-color: #ffffff;
        }
        QMessageBox {
            background-color: #ffffff;
        }
    """)

    window = AppWindow()
    window.show()

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
