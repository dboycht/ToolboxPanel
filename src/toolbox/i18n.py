"""双语翻译模块 · Bilingual i18n module.

Usage:
    from .i18n import tr, set_language, current_lang, on_language_changed
    label.setText(tr("key", name="World"))
    on_language_changed(lambda: refresh_ui())
"""
from __future__ import annotations
from pathlib import Path
from typing import Callable

# ── 当前语言 · Current language ──
_current_lang: str = "zh"

# ── 语言变更回调 · Language change callbacks ──
_lang_callbacks: list[Callable[[], None]] = []


def current_lang() -> str:
    return _current_lang


_LANGS = ("zh", "en")


def set_language(lang: str):
    global _current_lang
    if lang not in _LANGS:
        return
    _current_lang = lang
    for cb in _lang_callbacks:
        try:
            cb()
        except Exception:
            pass


def on_language_changed(cb: Callable[[], None]):
    """Register a callback to be called when the language switches."""
    _lang_callbacks.append(cb)


def tr(key: str, **kwargs) -> str:
    """Return the translated string for `key` in the current language.

    Supports Python format-string placeholders: tr("loaded", n=5)
    """
    entry = TEXTS.get(key)
    if entry is None:
        return f"??{key}??"
    text = entry.get(_current_lang) or entry.get("en", key)
    if kwargs:
        try:
            return text.format(**kwargs)
        except KeyError:
            return text
    return text


# ── 翻译表 · Translation table ──
# fmt: off
TEXTS: dict[str, dict[str, str]] = {
    # ── App window ──
    "app.title":                   {"zh": "工具箱",                         "en": "Toolbox"},
    "app.status.ready":            {"zh": "就绪 — 拖入文件或右键空白区域创建图标",
                                                                           "en": "Ready — Drag files or right‑click empty area"},
    "app.status.loaded":           {"zh": "已加载 {n} 个标签页",             "en": "Loaded {n} tab(s)"},
    "app.status.created_tab":      {"zh": "已创建标签页「{name}」",          "en": "Created tab '{name}'"},
    "app.menu.file":               {"zh": "文件(&F)",                       "en": "&File"},
    "app.menu.language":           {"zh": "语言(&L)",                       "en": "&Language"},
    "app.menu.chinese":            {"zh": "中文",                           "en": "中文 (Chinese)"},
    "app.menu.english":            {"zh": "English",                        "en": "English"},
    "app.menu.new_tab":            {"zh": "新建标签页(&N)",                 "en": "&New Tab"},
    "app.menu.exit":               {"zh": "退出(&X)",                       "en": "E&xit"},
    "app.menu.help":               {"zh": "帮助(&H)",                       "en": "&Help"},
    "app.menu.about":              {"zh": "关于(&A)",                       "en": "&About"},
    "app.about.title":             {"zh": "关于 工具箱",                    "en": "About Toolbox"},
    "app.about.text":              {"zh":
        "工具箱 v1.10.1 — 手机桌面风格的启动器\n"
        "作者: dboycht\n"
        "项目地址: https://github.com/dboycht/ToolboxPanel\n\n"
        "• 从资源管理器拖入文件/文件夹/快捷方式即可创建图标\n"
        "• 双击图标打开，右键查看更多选项\n"
        "• 右键空白区域创建 URL / 命令图标\n"
        "• 图标和标签页均可拖动排序\n"
        "• 数据自动保存到 data/ 文件夹",
                                    "en":
        "Toolbox v1.10.1 — Phone‑home‑screen style launcher\n"
        "Author: dboycht\n"
        "Project: https://github.com/dboycht/ToolboxPanel\n\n"
        "• Drag files / folders / shortcuts from Explorer to create icons\n"
        "• Double‑click to open, right‑click for more options\n"
        "• Right‑click empty area to create URL / Command icons\n"
        "• Drag icons and tabs to reorder\n"
        "• Data auto‑saved to data/ folder"},

    # ── Tab widget ──
    "tab.default_name":            {"zh": "新建标签页",                     "en": "New Tab"},
    "tab.menu.new":                {"zh": "新建标签页",                     "en": "New Tab"},
    "tab.menu.rename":             {"zh": "重命名",                         "en": "Rename"},
    "tab.menu.delete":             {"zh": "删除",                           "en": "Delete"},
    "tab.delete.title":            {"zh": "删除标签页",                     "en": "Delete Tab"},
    "tab.delete.confirm":          {"zh": "确定要删除标签页「{name}」及其所有图标吗？",
                                                                           "en": "Delete tab '{name}' and all its icons?"},
    "tab.delete.blocked":          {"zh": "至少需要保留一个标签页。",       "en": "You must keep at least one tab."},
    "tab.delete.blocked_title":    {"zh": "无法删除",                       "en": "Cannot Delete"},
    "tab.rename.title":            {"zh": "重命名标签页",                   "en": "Rename Tab"},
    "tab.rename.prompt":           {"zh": "标签页名称:",                    "en": "Tab name:"},
    "tab.renamed":                 {"zh": "标签页已重命名为「{name}」",     "en": "Tab renamed to '{name}'"},
    "tab.deleted":                 {"zh": "已删除标签页: {name}",           "en": "Deleted tab: {name}"},

    # ── Grid context menu ──
    "grid.menu.file":              {"zh": "新建文件图标…",                  "en": "New File Icon…"},
    "grid.menu.folder":            {"zh": "新建文件夹图标…",                "en": "New Folder Icon…"},
    "grid.menu.url":               {"zh": "新建网址图标…",                  "en": "New URL Icon…"},
    "grid.menu.command":           {"zh": "新建命令图标…",                  "en": "New Command Icon…"},
    "grid.dialog.select_file":     {"zh": "选择文件",                       "en": "Select File"},
    "grid.dialog.select_folder":   {"zh": "选择文件夹",                     "en": "Select Folder"},

    # ── Icon context menu ──
    "icon.menu.open":              {"zh": "打开",                           "en": "Open"},
    "icon.menu.open_location":     {"zh": "打开文件位置",                   "en": "Open File Location"},
    "icon.menu.rename":            {"zh": "重命名",                         "en": "Rename"},
    "icon.menu.remove":            {"zh": "删除",                           "en": "Remove"},
    "icon.remove.title":           {"zh": "删除图标",                       "en": "Remove Icon"},
    "icon.remove.confirm":         {"zh": "确定要从当前标签页中删除「{name}」吗？",
                                                                           "en": "Remove '{name}' from this tab?"},

    # ── URL dialog ──
    "url.dialog.title":            {"zh": "新建网址图标",                   "en": "New URL Icon"},
    "url.label.name":              {"zh": "名称:",                          "en": "Name:"},
    "url.label.url":               {"zh": "网址:",                          "en": "URL:"},
    "url.placeholder.name":        {"zh": "我的网站",                       "en": "My Website"},
    "url.placeholder.url":         {"zh": "https://example.com",            "en": "https://example.com"},
    "url.created":                 {"zh": "已创建网址图标: {name}",         "en": "Created URL icon: {name}"},

    # ── Command dialog ──
    "cmd.dialog.title":            {"zh": "新建命令图标",                   "en": "New Command Icon"},
    "cmd.label.name":              {"zh": "名称:",                          "en": "Name:"},
    "cmd.label.command":           {"zh": "命令:",                          "en": "Command:"},
    "cmd.label.args":              {"zh": "参数:",                          "en": "Arguments:"},
    "cmd.label.wd":                {"zh": "工作目录:",                      "en": "Working Dir:"},
    "cmd.placeholder.name":        {"zh": "备份脚本",                       "en": "Backup Script"},
    "cmd.placeholder.command":     {"zh": "python",                         "en": "python"},
    "cmd.placeholder.args":        {"zh": "--verbose backup.py",            "en": "--verbose backup.py"},
    "cmd.placeholder.wd":          {"zh": "C:\\Scripts",                    "en": "C:\\Scripts"},
    "cmd.created":                 {"zh": "已创建命令图标: {name}",         "en": "Created command icon: {name}"},
    "cmd.dialog.select_exe":       {"zh": "选择可执行文件",                 "en": "Select Executable"},
    "cmd.dialog.select_wd":        {"zh": "选择工作目录",                   "en": "Select Working Directory"},

    # ── Status messages ──
    "status.added":                {"zh": "已添加: {name}",                 "en": "Added: {name}"},
    "status.removed":              {"zh": "已删除: {name}",                 "en": "Removed: {name}"},
    "status.renamed":              {"zh": "已重命名为「{name}」",           "en": "Renamed to '{name}'"},
    "status.moved":                {"zh": "图标已移动",                     "en": "Icon moved"},
    "status.moved_tab":            {"zh": "图标已移动到目标标签页",         "en": "Icon moved to tab"},
    "status.opened":               {"zh": "已打开: {name}",                 "en": "Opened: {name}"},
    "status.open_failed":          {"zh": "打开失败: {err}",                "en": "Open failed: {err}"},
    "status.already_exists":       {"zh": "已存在: {name}",                 "en": "Already exists: {name}"},
    "status.path_not_found":       {"zh": "路径不存在: {path}",             "en": "Path not found: {path}"},
    "status.no_files":             {"zh": "未检测到有效文件",               "en": "No valid files detected"},
    "status.path_missing":         {"zh": "路径不存在: {path}",             "en": "Path not found: {path}"},

    # ── General ──
    "btn.ok":                      {"zh": "确定",                           "en": "OK"},
    "btn.cancel":                  {"zh": "取消",                           "en": "Cancel"},
    "data.default_tab":            {"zh": "主页",                           "en": "Home"},
}
# fmt: on
