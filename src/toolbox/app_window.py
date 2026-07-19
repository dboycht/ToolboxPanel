"""主应用程序窗口 · Main application window."""
import json
from pathlib import Path

from PyQt6.QtWidgets import (
    QMainWindow, QStatusBar, QMessageBox,
)
from PyQt6.QtGui import QAction, QActionGroup

from .models.data_store import DataStore
from .tab_widget import TabWidget
from .i18n import tr, current_lang, set_language, on_language_changed


def get_data_dir() -> Path:
    """获取数据目录。开发时放在项目根目录；打包后放在 .exe 旁边。"""
    import sys
    if getattr(sys, 'frozen', False):
        # PyInstaller 打包后：data/ 在 .exe 同级目录
        return Path(sys.executable).resolve().parent / "data"
    else:
        # 开发环境：data/ 在项目根目录
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

        # ── 批量管理 ──
        self._batch_action = QAction(tr("app.menu.batch") + "\tCtrl+B", self)
        self._batch_action.setCheckable(True)
        self._batch_action.setChecked(False)
        self._batch_action.triggered.connect(self._toggle_batch_mode)
        file_menu.addAction(self._batch_action)

        self._batch_delete_action = QAction(tr("app.menu.batch_delete") + "\tShift+Del", self)
        self._batch_delete_action.triggered.connect(self._batch_delete)
        self._batch_delete_action.setEnabled(False)
        file_menu.addAction(self._batch_delete_action)

        file_menu.addSeparator()

        # 数据管理
        reset_action = QAction(tr("app.menu.reset") + "\tCtrl+Shift+R", self)
        reset_action.triggered.connect(self._reset_data)
        file_menu.addAction(reset_action)

        export_action = QAction(tr("app.menu.export") + "\tCtrl+Shift+E", self)
        export_action.triggered.connect(self._export_data)
        file_menu.addAction(export_action)

        import_action = QAction(tr("app.menu.import") + "\tCtrl+Shift+I", self)
        import_action.triggered.connect(self._import_data)
        file_menu.addAction(import_action)

        file_menu.addSeparator()

        exit_action = QAction(tr("app.menu.exit"), self)
        exit_action.setShortcut("Alt+F4")
        exit_action.triggered.connect(self.close)
        file_menu.addAction(exit_action)

        # ── 帮助菜单 · Help ──
        help_menu = menu_bar.addMenu(tr("app.menu.help"))
        shortcuts_action = QAction(tr("app.menu.shortcuts"), self)
        shortcuts_action.triggered.connect(self._show_shortcuts)
        help_menu.addAction(shortcuts_action)
        help_menu.addSeparator()
        about_action = QAction(tr("app.menu.about"), self)
        about_action.triggered.connect(self._on_about)
        help_menu.addAction(about_action)

    # ── 全局快捷键 ──

    def keyPressEvent(self, event):
        """拦截全局快捷键。"""
        from PyQt6.QtCore import Qt as QtCore
        modifiers = event.modifiers()
        key = event.key()
        ctrl = bool(modifiers & QtCore.KeyboardModifier.ControlModifier)
        shift = bool(modifiers & QtCore.KeyboardModifier.ShiftModifier)

        if ctrl and shift:
            grid = self.tab_widget._get_current_grid()
            if key == QtCore.Key.Key_F and grid:
                self.tab_widget._create_file_icon(grid)
                return
            if key == QtCore.Key.Key_O and grid:
                self.tab_widget._create_folder_icon(grid)
                return
            if key == QtCore.Key.Key_U and grid:
                self.tab_widget._create_url_icon(grid)
                return
            if key == QtCore.Key.Key_P and grid:
                self.tab_widget._create_command_icon(grid)
                return
        if ctrl and key == QtCore.Key.Key_B:
            self._batch_action.setChecked(not self._batch_action.isChecked())
            self._toggle_batch_mode(self._batch_action.isChecked())
            return
        if shift and key == QtCore.Key.Key_Delete and self._batch_action.isChecked():
            self._batch_delete()
            return
        if ctrl and shift and key == QtCore.Key.Key_R:
            self._reset_data()
            return
        if ctrl and shift and key == QtCore.Key.Key_E:
            self._export_data()
            return
        if ctrl and shift and key == QtCore.Key.Key_I:
            self._import_data()
            return

        super().keyPressEvent(event)

    def _show_shortcuts(self):
        from .shortcut_dialog import show_shortcut_dialog
        show_shortcut_dialog(self)

    # ── 状态恢复 · State restore ──

    def _restore_state(self):
        set_language(self._lang)
        tabs = self.data_store.load()
        self.tab_widget.restore_tabs(tabs)
        self.status_bar.showMessage(tr("app.status.loaded", n=len(tabs)))

    def _on_new_tab(self):
        default_name = tr("tab.default_name")
        tab_name = self._prompt_text(tr("tab.rename.title"), tr("tab.rename.prompt"), default_name)
        if tab_name is None:
            return  # 用户取消
        tab = self.data_store.add_tab(tab_name)
        self.tab_widget.add_tab_page(tab)
        self.status_bar.showMessage(tr("app.status.created_tab", name=tab.name))

    @staticmethod
    def _prompt_text(title: str, label: str, default: str = "") -> str | None:
        """弹出输入对话框，确定返回文本，取消返回 None。按钮文字跟随语言。"""
        from PyQt6.QtWidgets import (QDialog, QVBoxLayout, QLabel,
                                      QLineEdit, QDialogButtonBox)
        dlg = QDialog()
        dlg.setWindowTitle(title)
        dlg.setMinimumWidth(320)
        layout = QVBoxLayout(dlg)
        layout.addWidget(QLabel(label))
        edit = QLineEdit(default)
        edit.selectAll()
        layout.addWidget(edit)
        btns = QDialogButtonBox()
        btn_ok = btns.addButton(tr("btn.ok"), QDialogButtonBox.ButtonRole.AcceptRole)
        btn_cancel = btns.addButton(tr("btn.cancel"), QDialogButtonBox.ButtonRole.RejectRole)
        btns.accepted.connect(dlg.accept)
        btns.rejected.connect(dlg.reject)
        layout.addWidget(btns)
        if dlg.exec() == QDialog.DialogCode.Accepted:
            return edit.text().strip() or default
        return None

    def _toggle_batch_mode(self, checked: bool):
        """切换批量管理模式。"""
        self._batch_delete_action.setEnabled(checked)
        for i in range(self.tab_widget.count()):
            grid = self.tab_widget.widget(i)
            from .icon_grid import IconGrid
            if isinstance(grid, IconGrid):
                grid.set_batch_mode(checked)
        if checked:
            self.status_bar.showMessage("批量管理模式：勾选图标后点击「批量删除勾选图标」")
        else:
            self.status_bar.showMessage(tr("app.status.ready"))

    def _batch_delete(self):
        """执行批量删除（当前标签页）。"""
        grid = self.tab_widget._get_current_grid()
        if grid:
            grid.batch_delete()

    def _reset_data(self):
        """重置所有数据。"""
        confirm = QMessageBox.warning(
            self, tr("reset.title"),
            tr("reset.confirm"),
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
        )
        if confirm != QMessageBox.StandardButton.Yes:
            return
        # 清空所有标签页
        tw = self.tab_widget
        while tw._stack.count() > 0:
            w = tw._stack.widget(0)
            tw._stack.removeWidget(w)
            if hasattr(w, 'deleteLater'):
                w.deleteLater()
        tw._icon_grids.clear()
        tw._tab_records.clear()
        while tw._tab_bar.count() > 0:
            tw._tab_bar.remove_tab(0)
        # 清除数据文件
        import shutil
        icons_dir = self.data_store.icons_dir
        if icons_dir.exists():
            shutil.rmtree(icons_dir)
            icons_dir.mkdir()
        self.data_store.tabs = []
        self.data_store.save()
        # 重建默认标签页
        default_name = tr("tab.default_name")
        tab = self.data_store.add_tab(default_name)
        tw.add_tab_page(tab)
        self.status_bar.showMessage(tr("reset.done"))

    def _export_data(self):
        """导出数据为 ZIP 压缩包（带进度条）。"""
        from PyQt6.QtWidgets import QFileDialog
        from .services.backup_manager import BackupWorker, unique_filename
        from .progress_dialog import ProgressDialog

        folder = QFileDialog.getExistingDirectory(self, tr("export.select_folder"))
        if not folder:
            return
        zip_path = str(Path(folder) / unique_filename())

        dlg = ProgressDialog(tr("export.title"), self)
        worker = BackupWorker("export", self.data_store.data_dir, zip_path)

        worker.log.connect(dlg.append_log)
        worker.progress.connect(dlg.set_progress)
        worker.finished.connect(lambda ok, msg: dlg.mark_done(ok, msg))

        # 线程结束后清理
        worker.finished.connect(lambda ok, msg: worker.deleteLater())
        worker.start()
        dlg.exec()

    def _import_data(self):
        """从 ZIP 压缩包导入数据（带进度条 + 重载）。"""
        from PyQt6.QtWidgets import QFileDialog
        from .services.backup_manager import BackupWorker, unique_filename
        from .progress_dialog import ProgressDialog

        zip_path, _ = QFileDialog.getOpenFileName(
            self, tr("import.select_file"), "", "ZIP (*.zip)"
        )
        if not zip_path:
            return

        dlg = ProgressDialog(tr("import.title"), self)
        worker = BackupWorker("import", self.data_store.data_dir, zip_path)

        worker.log.connect(dlg.append_log)
        worker.progress.connect(dlg.set_progress)
        # 导入完成后重新加载
        worker.finished.connect(lambda ok, msg: self._on_import_done(ok, msg, dlg))
        worker.finished.connect(lambda ok, msg: worker.deleteLater())
        worker.start()
        dlg.exec()

    def _on_import_done(self, ok: bool, msg: str, dlg):
        """导入完成后刷新 UI。"""
        dlg.mark_done(ok, msg)
        if ok:
            self.data_store.load()
            self.tab_widget.restore_tabs(self.data_store.tabs)
            self.status_bar.showMessage(tr("import.done"))

    def _on_about(self):
        QMessageBox.about(self, tr("app.about.title"), tr("app.about.text"))

    def closeEvent(self, event):
        self.data_store.save()
        self.data_store.clean_orphan_cache()
        super().closeEvent(event)
