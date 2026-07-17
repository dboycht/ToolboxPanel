"""主应用程序窗口 · Main application window."""
import json
from pathlib import Path

from PyQt6.QtWidgets import (
    QMainWindow, QStatusBar, QMenu, QMessageBox,
)
from PyQt6.QtCore import Qt
from PyQt6.QtGui import QAction, QActionGroup

from .models.data_store import DataStore
from .tab_widget import TabWidget
from .i18n import tr, current_lang, set_language, on_language_changed


def get_data_dir() -> Path:
    return Path(__file__).resolve().parent.parent.parent / "data"


class AppWindow(QMainWindow):
    """工具箱主窗口 · Toolbox main window."""

    def __init__(self):
        super().__init__()
        self._lang = self._load_language()
        set_language(self._lang)  # 必须在所有 tr() 调用之前

        self.setWindowTitle(tr("app.title"))
        self.setMinimumSize(600, 400)
        self.resize(960, 680)

        # 数据存储
        self.data_store = DataStore(get_data_dir())

        # 中央组件
        self.tab_widget = TabWidget(self.data_store)
        self.setCentralWidget(self.tab_widget)

        # 状态栏
        self.status_bar = QStatusBar()
        self.setStatusBar(self.status_bar)
        self.status_bar.showMessage(tr("app.status.ready"))

        # 菜单栏
        self._setup_menus()

        # 连接信号
        self.tab_widget.new_tab_requested.connect(self._on_new_tab)
        self.tab_widget.status_message.connect(self.status_bar.showMessage)

        # 注册语言切换回调（必须在 _restore_state 之前，否则首次加载不触发）
        on_language_changed(self._refresh_ui)

        # 恢复已保存状态
        self._restore_state()

    # ── 语言管理 · Language ──

    @staticmethod
    def _load_language() -> str:
        config_file = get_data_dir() / "config.json"
        try:
            if config_file.exists():
                cfg = json.loads(config_file.read_text(encoding="utf-8"))
                lang = cfg.get("language", "zh")
                if lang in ("zh", "en"):
                    return lang
        except Exception:
            pass
        return "zh"

    @staticmethod
    def _save_language(lang: str):
        config_file = get_data_dir() / "config.json"
        try:
            config_file.parent.mkdir(parents=True, exist_ok=True)
            cfg = {}
            if config_file.exists():
                cfg = json.loads(config_file.read_text(encoding="utf-8"))
            cfg["language"] = lang
            config_file.write_text(json.dumps(cfg, indent=2, ensure_ascii=False), encoding="utf-8")
        except Exception:
            pass

    def _switch_language(self, lang: str):
        if lang == current_lang():
            return
        set_language(lang)
        self._save_language(lang)

    def _refresh_ui(self):
        """Called whenever the language changes — update all visible strings."""
        self.setWindowTitle(tr("app.title"))
        self.status_bar.showMessage(tr("app.status.ready"))
        # Rebuild menus
        self._setup_menus()
        # Refresh tab texts
        for i in range(self.tab_widget.count()):
            grid = self.tab_widget.widget(i)
            from .icon_grid import IconGrid
            if isinstance(grid, IconGrid):
                tab = grid.tab
                # Use stored name or translate the default
                name = tab.name
                # If name is the old-language default, translate it
                if name in ("新建标签页", "New Tab", "主页", "Home"):
                    name = tr("data.default_tab") if i == 0 and name in ("主页", "Home") else tr("tab.default_name")
                    tab.name = name
                self.tab_widget.setTabText(i, name)

    # ── 菜单栏 · Menu bar ──

    def _setup_menus(self):
        menu_bar = self.menuBar()
        menu_bar.clear()

        # ── 文件菜单 · File ──
        file_menu = menu_bar.addMenu(tr("app.menu.file"))

        new_tab_action = QAction(tr("app.menu.new_tab"), self)
        new_tab_action.setShortcut("Ctrl+T")
        new_tab_action.triggered.connect(self._on_new_tab)
        file_menu.addAction(new_tab_action)

        file_menu.addSeparator()

        # ── 语言子菜单 · Language submenu ──
        lang_menu = file_menu.addMenu(tr("app.menu.language"))

        lang_group = QActionGroup(self)
        lang_group.setExclusive(True)

        zh_action = QAction(tr("app.menu.chinese"), self)
        zh_action.setCheckable(True)
        zh_action.setChecked(current_lang() == "zh")
        zh_action.triggered.connect(lambda: self._switch_language("zh"))
        lang_group.addAction(zh_action)
        lang_menu.addAction(zh_action)

        en_action = QAction(tr("app.menu.english"), self)
        en_action.setCheckable(True)
        en_action.setChecked(current_lang() == "en")
        en_action.triggered.connect(lambda: self._switch_language("en"))
        lang_group.addAction(en_action)
        lang_menu.addAction(en_action)

        file_menu.addSeparator()

        exit_action = QAction(tr("app.menu.exit"), self)
        exit_action.setShortcut("Alt+F4")
        exit_action.triggered.connect(self.close)
        file_menu.addAction(exit_action)

        # ── 帮助菜单 · Help ──
        help_menu = menu_bar.addMenu(tr("app.menu.help"))
        about_action = QAction(tr("app.menu.about"), self)
        about_action.triggered.connect(self._on_about)
        help_menu.addAction(about_action)

    # ── 状态恢复 · State restore ──

    def _restore_state(self):
        set_language(self._lang)
        tabs = self.data_store.load()
        self.tab_widget.restore_tabs(tabs)
        self.status_bar.showMessage(tr("app.status.loaded", n=len(tabs)))

    def _on_new_tab(self):
        tab_name = tr("tab.default_name")
        tab = self.data_store.add_tab(tab_name)
        self.tab_widget.add_tab_page(tab)
        self.status_bar.showMessage(tr("app.status.created_tab", name=tab.name))

    def _on_about(self):
        QMessageBox.about(self, tr("app.about.title"), tr("app.about.text"))

    def closeEvent(self, event):
        self.data_store.save()
        self.data_store.clean_orphan_cache()
        super().closeEvent(event)
