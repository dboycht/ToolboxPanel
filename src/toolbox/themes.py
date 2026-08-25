"""主题管理 —— 浅色 / 深色双主题的调色板、全局样式表与组件颜色令牌。

设计：
- 所有语义化颜色集中在 LIGHT / DARK 两个令牌表里，新增主题只需加一张表；
- main.py 启动时 apply_theme()；AppWindow 的「视图 → 主题」菜单可即时切换；
- 组件内不要硬编码颜色：构建/刷新时用 token("xxx") 取色，并调用
  on_theme_changed(self._refresh_style) 注册重刷回调；
- 回调用 WeakMethod 弱引用持有，部件销毁后自动清理，不阻塞 GC。
"""
import json
import sys
from pathlib import Path
from string import Template
from weakref import WeakMethod

from PyQt6.QtGui import QPalette, QColor

# ── 颜色令牌 ──────────────────────────────────────────────

LIGHT: dict[str, str] = {
    # 基底
    "window": "#f3f3f3",
    "base": "#ffffff",
    "alt_base": "#f9f9f9",
    "text": "#1e1e1e",
    "border": "#d0d0d0",
    "border_soft": "#e0e0e0",
    # 强调色（按钮 / 焦点 / 勾选）
    "accent": "#0067c0",
    "accent_hover": "#1979ca",
    "accent_pressed": "#005499",
    "on_accent": "#ffffff",
    # 列表 / 表格选中（QPalette.Highlight）
    "highlight": "#0067c0",
    "highlighted_text": "#ffffff",
    # 提示浮层
    "tooltip_base": "#1e1e1e",
    "tooltip_text": "#ffffff",
    # 菜单
    "menu_bg": "#ffffff",
    "menu_separator": "#e8e8e8",
    "menubar_item_hover": "#e0e0e0",
    # 标签页
    "tab_inactive_text": "#5a5a5a",
    "tab_hover_bg": "rgba(0, 0, 0, 0.04)",
    "tab_hover_text": "#333333",
    # 滚动条（背景用 window）
    "scrollbar_handle": "#c0c0c0",
    "scrollbar_handle_hover": "#a0a0a0",
    # 状态栏文字
    "status_text": "#666666",
    # 树 / 表格
    "tree_alt_row": "#f7f9fb",
    "header_bg": "#f0f0f0",
    "gridline": "#e0e0e0",
    "tree_hover_bg": "rgba(0, 103, 192, 0.06)",
    "tree_sel_bg": "rgba(0, 103, 192, 0.12)",
    "tree_sel_text": "#1e1e1e",
    # 弱化文字
    "hint_text": "#999999",
    "muted_text": "#666666",
    # 次级按钮（浏览…等）
    "btn_secondary_bg": "#e0e0e0",
    "btn_secondary_hover": "#d0d0d0",
    "btn_secondary_border": "#c0c0c0",
    "btn_secondary_text": "#1e1e1e",
    # 图标悬停淡色高亮
    "hover_tint_bg": "rgba(0, 103, 192, 0.08)",
    "hover_tint_border": "rgba(0, 103, 192, 0.25)",
    # 拖放悬停提示区
    "drop_zone_bg": "#f0f6ff",
    # 批量复选框
    "checkbox_bg": "#ffffff",
}

DARK: dict[str, str] = {
    # 基底
    "window": "#202020",
    "base": "#2b2b2b",
    "alt_base": "#303030",
    "text": "#f3f3f3",
    "border": "#555555",
    "border_soft": "#3f3f3f",
    # 强调色（Win11 深色模式亮蓝）
    "accent": "#4cc2ff",
    "accent_hover": "#6fd3ff",
    "accent_pressed": "#33a3e4",
    "on_accent": "#101010",
    # 列表 / 表格选中
    "highlight": "#4cc2ff",
    "highlighted_text": "#101010",
    # 提示浮层
    "tooltip_base": "#333333",
    "tooltip_text": "#f3f3f3",
    # 菜单
    "menu_bg": "#2b2b2b",
    "menu_separator": "#454545",
    "menubar_item_hover": "#383838",
    # 标签页
    "tab_inactive_text": "#a6a6a6",
    "tab_hover_bg": "rgba(255, 255, 255, 0.06)",
    "tab_hover_text": "#e0e0e0",
    # 滚动条
    "scrollbar_handle": "#5a5a5a",
    "scrollbar_handle_hover": "#707070",
    # 状态栏文字
    "status_text": "#9a9a9a",
    # 树 / 表格
    "tree_alt_row": "#272727",
    "header_bg": "#333333",
    "gridline": "#3f3f3f",
    "tree_hover_bg": "rgba(76, 194, 255, 0.08)",
    "tree_sel_bg": "rgba(76, 194, 255, 0.16)",
    "tree_sel_text": "#ffffff",
    # 弱化文字
    "hint_text": "#8a8a8a",
    "muted_text": "#9a9a9a",
    # 次级按钮
    "btn_secondary_bg": "#373737",
    "btn_secondary_hover": "#414141",
    "btn_secondary_border": "#4a4a4a",
    "btn_secondary_text": "#f3f3f3",
    # 图标悬停淡色高亮
    "hover_tint_bg": "rgba(76, 194, 255, 0.10)",
    "hover_tint_border": "rgba(76, 194, 255, 0.35)",
    # 拖放悬停提示区
    "drop_zone_bg": "#1e3a52",
    # 批量复选框
    "checkbox_bg": "#333333",
}

THEMES: dict[str, dict[str, str]] = {"light": LIGHT, "dark": DARK}
DEFAULT_THEME = "light"

# 当前主题名（模块级状态）
_name: str = DEFAULT_THEME

# 主题切换回调（WeakMethod：部件销毁后自动失效）
_receivers: list[WeakMethod] = []

# ── 对外 API ──────────────────────────────────────────────


def current_theme() -> str:
    """当前主题名。"""
    return _name


def available_themes() -> list[str]:
    """可用主题名列表（按菜单展示顺序）。"""
    return list(THEMES)


def token(key: str) -> str:
    """取当前主题的颜色令牌（QSS 可直接拼入）。"""
    return THEMES[_name][key]


def on_theme_changed(callback) -> None:
    """注册主题切换回调（传绑定方法，内部用 WeakMethod 持有）。"""
    _receivers.append(WeakMethod(callback))


def saved_theme() -> str:
    """从 config.json 读用户保存的主题；无或非法则返回默认。

    数据目录定位与 app_window.get_data_dir 一致；此处延迟导入避免循环引用。
    """
    try:
        from .app_window import get_data_dir
        data_dir = get_data_dir()
    except Exception:
        if getattr(sys, "frozen", False):
            data_dir = Path(sys.executable).resolve().parent / "data"
        else:
            data_dir = Path(__file__).resolve().parent.parent.parent / "data"
    try:
        cfg_file = Path(data_dir) / "config.json"
        if cfg_file.exists():
            v = json.loads(cfg_file.read_text(encoding="utf-8")).get(
                "theme", DEFAULT_THEME)
            return v if v in THEMES else DEFAULT_THEME
    except Exception:
        pass
    return DEFAULT_THEME


# ── 应用主题 ──────────────────────────────────────────────

_QSS_TEMPLATE = Template("""
    /* ── 主窗口 ── */
    QMainWindow {
        background-color: $window;
    }

    /* ── 菜单栏 ── */
    QMenuBar {
        background-color: $window;
        color: $text;
        padding: 2px 8px;
        border-bottom: 1px solid $border_soft;
        font-size: 9pt;
    }
    QMenuBar::item {
        background-color: transparent;
        padding: 6px 10px;
        border-radius: 6px;
        margin: 2px 1px;
    }
    QMenuBar::item:selected {
        background-color: $menubar_item_hover;
    }
    QMenu {
        background-color: $menu_bg;
        color: $text;
        border: 1px solid $border;
        border-radius: 8px;
        padding: 6px 4px;
    }
    QMenu::item {
        padding: 7px 32px 7px 16px;
        border-radius: 4px;
        margin: 1px 4px;
    }
    QMenu::item:selected {
        background-color: $accent;
        color: $on_accent;
    }
    QMenu::separator {
        height: 1px;
        background-color: $menu_separator;
        margin: 4px 12px;
    }

    /* ── 标签页 ── */
    QTabWidget::pane {
        border: none;
        background-color: $window;
    }
    QTabBar::tab {
        background-color: transparent;
        color: $tab_inactive_text;
        border: none;
        padding: 7px 18px;
        margin: 4px 2px 0 2px;
        border-radius: 8px 8px 0 0;
        font-size: 9pt;
    }
    QTabBar::tab:selected {
        background-color: $base;
        color: $text;
        font-weight: 600;
    }
    QTabBar::tab:hover:!selected {
        background-color: $tab_hover_bg;
        color: $tab_hover_text;
    }

    /* ── 滚动区域 ── */
    QScrollArea {
        background-color: $base;
        border: none;
        border-radius: 8px;
    }
    QScrollBar:vertical {
        background-color: $window;
        width: 8px;
        border-radius: 4px;
    }
    QScrollBar::handle:vertical {
        background-color: $scrollbar_handle;
        border-radius: 4px;
        min-height: 30px;
    }
    QScrollBar::handle:vertical:hover {
        background-color: $scrollbar_handle_hover;
    }
    QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {
        height: 0px;
    }
    QScrollBar::add-page:vertical, QScrollBar::sub-page:vertical {
        background: none;
    }

    /* ── 状态栏 ── */
    QStatusBar {
        background-color: $window;
        color: $status_text;
        border-top: 1px solid $border_soft;
        font-size: 8pt;
        padding: 2px 10px;
    }

    /* ── 按钮 ── */
    QPushButton {
        background-color: $accent;
        color: $on_accent;
        border: none;
        border-radius: 6px;
        padding: 6px 16px;
        font-size: 9pt;
    }
    QPushButton:hover {
        background-color: $accent_hover;
    }
    QPushButton:pressed {
        background-color: $accent_pressed;
    }

    /* ── 输入框 ── */
    QLineEdit {
        background-color: $base;
        color: $text;
        border: 1px solid $border;
        border-radius: 6px;
        padding: 5px 10px;
        font-size: 9pt;
    }
    QLineEdit:focus {
        border-color: $accent;
    }

    /* ── 消息框 / 对话框 ── */
    QDialog {
        background-color: $base;
    }
    QMessageBox {
        background-color: $base;
    }
""")


def build_palette(name: str) -> QPalette:
    """由令牌表构造 QPalette。"""
    t = THEMES[name]

    def c(key: str) -> QColor:
        return QColor(t[key])

    pal = QPalette()
    pal.setColor(QPalette.ColorRole.Window, c("window"))
    pal.setColor(QPalette.ColorRole.WindowText, c("text"))
    pal.setColor(QPalette.ColorRole.Base, c("base"))
    pal.setColor(QPalette.ColorRole.AlternateBase, c("alt_base"))
    pal.setColor(QPalette.ColorRole.ToolTipBase, c("tooltip_base"))
    pal.setColor(QPalette.ColorRole.ToolTipText, c("tooltip_text"))
    pal.setColor(QPalette.ColorRole.Text, c("text"))
    pal.setColor(QPalette.ColorRole.Button, c("alt_base"))
    pal.setColor(QPalette.ColorRole.ButtonText, c("text"))
    pal.setColor(QPalette.ColorRole.Highlight, c("highlight"))
    pal.setColor(QPalette.ColorRole.HighlightedText, c("highlighted_text"))
    return pal


def apply_theme(app, name: str) -> None:
    """把指定主题应用到 QApplication（调色板 + 全局样式表），并通知组件重刷。

    未知主题名回落到默认主题。
    """
    global _name
    _name = name if name in THEMES else DEFAULT_THEME

    app.setStyle("Fusion")
    app.setPalette(build_palette(_name))
    app.setStyleSheet(_QSS_TEMPLATE.substitute(THEMES[_name]))
    _notify()


def _notify() -> None:
    """触发所有存活的组件重刷回调，并清掉已销毁部件的弱引用。"""
    alive: list[WeakMethod] = []
    for wm in _receivers:
        cb = wm()
        if cb is None:
            continue  # 部件已销毁，丢弃
        alive.append(wm)
        cb()
    _receivers[:] = alive
