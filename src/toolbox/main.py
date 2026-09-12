"""工具箱入口（v2.0.1 起为 QML 版）。

⚠️ v2.0.1 大重构：UI 已由 PyQt6 Widget 全线切换为 **Qt Quick / QML**。
旧的 Widget 实现（app_window.py / tab_widget.py / icon_grid.py 等）仍保留在
`src/toolbox/` 下作为参考，但**不再由本入口加载**。
详见开发副本 DEVELOPMENT.md 第 0 节。
"""
import sys


def main():
    from toolbox.ui.app import main as qml_main
    sys.exit(qml_main())


if __name__ == "__main__":
    main()
