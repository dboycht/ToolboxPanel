"""工具箱入口。"""
import sys

from PyQt6.QtWidgets import QApplication
from PyQt6.QtGui import QFont

from . import themes
from .app_window import AppWindow
from .single_instance import SingleInstanceGuard


def main():
    app = QApplication(sys.argv)
    app.setApplicationName("工具箱")
    app.setOrganizationName("Toolbox")

    # ---------- 单实例守护 ----------
    # data/tabs.json 是单文件即时保存，多实例并发会互相覆盖，
    # 因此只允许一个实例；检测到已有实例时拉取其窗口并退出。
    guard = SingleInstanceGuard()
    if not guard.acquire():
        guard.activate_existing()
        sys.exit(0)

    # ---------- Win11 系统字体 ----------
    font = QFont("Microsoft YaHei UI", 9)
    app.setFont(font)

    # ---------- 主题（浅色/深色，读 config.json 偏好）----------
    # Fusion 风格 + 调色板 + 全局样式表都由 themes 统一管理
    themes.apply_theme(app, themes.saved_theme())

    window = AppWindow()
    window.show()
    # 窗口显示后把 HWND 写入共享内存，供后续实例拉取窗口
    guard.register_window(window)

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
