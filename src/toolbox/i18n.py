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


def remove_language_callback(cb: Callable[[], None]):
    """Remove a previously registered language-change callback."""
    if cb in _lang_callbacks:
        _lang_callbacks.remove(cb)


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
    "app.menu.view":               {"zh": "视图(&V)",                       "en": "&View"},
    "app.menu.icon_size":          {"zh": "图标大小",                       "en": "Icon Size"},
    "app.menu.size_small":         {"zh": "小",                             "en": "Small"},
    "app.menu.size_medium":        {"zh": "中",                             "en": "Medium"},
    "app.menu.size_large":         {"zh": "大",                             "en": "Large"},
    "app.menu.find":               {"zh": "查找(&F)",                       "en": "&Find"},
    "app.menu.language":           {"zh": "语言(&L)",                       "en": "&Language"},
    "app.menu.chinese":            {"zh": "中文",                           "en": "中文 (Chinese)"},
    "app.menu.english":            {"zh": "English",                        "en": "English"},
    "app.menu.new_tab":            {"zh": "新建标签页(&N)",                 "en": "&New Tab"},
    "app.menu.exit":               {"zh": "退出(&X)",                       "en": "E&xit"},
    "app.menu.help":               {"zh": "帮助(&H)",                       "en": "&Help"},
    "app.menu.about":              {"zh": "关于(&A)",                       "en": "&About"},
    "app.about.title":             {"zh": "关于 工具箱",                    "en": "About Toolbox"},
    "app.about.text":              {"zh":
        "工具箱 v1.10.4 — 手机桌面风格的启动器\n"
        "作者: dboycht\n"
        "项目地址: https://github.com/dboycht/ToolboxPanel\n\n"
        "• 从资源管理器拖入文件/文件夹/快捷方式即可创建图标\n"
        "• 双击图标打开，右键查看更多选项\n"
        "• 右键空白区域创建 URL / 命令图标\n"
        "• 图标和标签页均可拖动排序\n"
        "• 数据自动保存到 data/ 文件夹\n"
        "• 支持搜索过滤、图标大小切换、打开方式",
                                    "en":
        "Toolbox v1.10.4 — Phone‑home‑screen style launcher\n"
        "Author: dboycht\n"
        "Project: https://github.com/dboycht/ToolboxPanel\n\n"
        "• Drag files / folders / shortcuts from Explorer to create icons\n"
        "• Double‑click to open, right‑click for more options\n"
        "• Right‑click empty area to create URL / Command icons\n"
        "• Drag icons and tabs to reorder\n"
        "• Data auto‑saved to data/ folder\n"
        "• Search filter, icon size options, Open With support"},

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
    "grid.menu.shortcut":          {"zh": "新建快捷方式图标…",              "en": "New Shortcut Icon…"},
    "grid.menu.url":               {"zh": "新建网址图标…",                  "en": "New URL Icon…"},
    "grid.menu.command":           {"zh": "新建命令图标…",                  "en": "New Command Icon…"},
    "grid.dialog.select_file":     {"zh": "选择文件",                       "en": "Select File"},
    "grid.dialog.select_folder":   {"zh": "选择文件夹",                     "en": "Select Folder"},
    "grid.dialog.select_shortcut": {"zh": "选择快捷方式",                   "en": "Select Shortcut"},

    # ── Icon context menu ──
    "icon.menu.open":              {"zh": "打开",                           "en": "Open"},
    "icon.menu.open_with":         {"zh": "用其他应用打开…",                "en": "Open With Another App…"},
    "icon.menu.edit":              {"zh": "编辑属性…",                      "en": "Edit Properties…"},
    "edit.title":                  {"zh": "编辑图标属性",                   "en": "Edit Icon Properties"},
    "edit.field.name":             {"zh": "名称:",                          "en": "Name:"},
    "edit.field.path":             {"zh": "路径:",                          "en": "Path:"},
    "edit.field.name":             {"zh": "名称:",                          "en": "Name:"},
    "edit.field.desc":             {"zh": "描述:",                          "en": "Description:"},
    "edit.field.desc_ph":          {"zh": "快捷方式说明（可选）",           "en": "Shortcut description (optional)"},
    "edit.field.icon":             {"zh": "图标:",                          "en": "Icon:"},
    "edit.field.icon_ph":          {"zh": "自定义图标文件（可选）",         "en": "Custom icon file (optional)"},
    "edit.field.icon_browse":      {"zh": "选择图标文件",                   "en": "Select Icon File"},
    "edit.field.icon_index":       {"zh": "索引:",                          "en": "Index:"},
    "edit.btn.reset_icon":         {"zh": "使用默认",                       "en": "Default"},
    "edit.btn.reset_icon_tip":     {"zh": "恢复使用目标程序的默认图标",     "en": "Restore the target's default icon"},
    "edit.type.file":              {"zh": "文件图标",                       "en": "File Icon"},
    "edit.type.folder":            {"zh": "文件夹图标",                     "en": "Folder Icon"},
    "edit.type.shortcut":          {"zh": "快捷方式图标",                   "en": "Shortcut Icon"},
    "edit.type.url":               {"zh": "网址图标",                       "en": "URL Icon"},
    "edit.type.command":           {"zh": "命令图标",                       "en": "Command Icon"},
    "create.title.file":           {"zh": "新建文件图标",                   "en": "New File Icon"},
    "create.title.folder":         {"zh": "新建文件夹图标",                 "en": "New Folder Icon"},
    "create.title.shortcut":       {"zh": "新建快捷方式图标",               "en": "New Shortcut Icon"},
    "status.edited":               {"zh": "已更新图标: {name}",             "en": "Icon updated: {name}"},
    "status.edit_invalid":         {"zh": "名称不能为空",                   "en": "Name cannot be empty"},
    "icon.menu.open_location":     {"zh": "打开文件位置",                   "en": "Open File Location"},
    "icon.menu.rename":            {"zh": "重命名",                         "en": "Rename"},
    "icon.menu.remove":            {"zh": "删除",                           "en": "Remove"},
    "icon.remove.title":           {"zh": "删除图标",                       "en": "Remove Icon"},
    "icon.remove.confirm":         {"zh": "确定要从当前标签页中删除「{name}」吗？",
                                                                           "en": "Remove '{name}' from this tab?"},
    "icon.remove.unknown":         {"zh": "此图标",                         "en": "this icon"},
    "status.open_with_failed":     {"zh": "打开方式失败: {err}",            "en": "Open With failed: {err}"},

    # ── Validation ──
    "validate.name_required":      {"zh": "名称不能为空",                   "en": "Name cannot be empty"},
    "validate.path_required":      {"zh": "路径不能为空",                   "en": "Path cannot be empty"},
    "validate.url_required":       {"zh": "网址不能为空",                   "en": "URL cannot be empty"},
    "validate.command_required":   {"zh": "命令不能为空",                   "en": "Command cannot be empty"},

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
    # shortcut dialog
    "shortcut.dialog.title":       {"zh": "快捷键参考",                      "en": "Shortcut Reference"},
    "shortcut.col.action":         {"zh": "功能",                            "en": "Action"},
    "shortcut.col.key":            {"zh": "快捷键",                          "en": "Shortcut"},
    "shortcut.new_tab":           {"zh": "新建标签页",                       "en": "New Tab"},
    "shortcut.close_tab":         {"zh": "关闭当前标签页",                   "en": "Close Current Tab"},
    "shortcut.rename_tab":        {"zh": "重命名标签页",                     "en": "Rename Tab"},
    "shortcut.prev_tab":          {"zh": "上一个标签页",                     "en": "Previous Tab"},
    "shortcut.next_tab":          {"zh": "下一个标签页",                     "en": "Next Tab"},
    "shortcut.batch_mode":        {"zh": "批量管理模式",                     "en": "Batch Manage Mode"},
    "shortcut.batch_delete":      {"zh": "批量删除图标",                     "en": "Batch Delete Icons"},
    "shortcut.new_file":          {"zh": "新建文件图标",                     "en": "New File Icon"},
    "shortcut.new_folder":        {"zh": "新建文件夹图标",                   "en": "New Folder Icon"},
    "shortcut.new_url":           {"zh": "新建网址图标",                     "en": "New URL Icon"},
    "shortcut.new_command":       {"zh": "新建命令图标",                     "en": "New Command Icon"},
    "shortcut.open_icon":         {"zh": "打开图标",                         "en": "Open Icon"},
    "shortcut.rename_icon":       {"zh": "重命名图标",                       "en": "Rename Icon"},
    "shortcut.delete_icon":       {"zh": "删除图标",                         "en": "Delete Icon"},
    "shortcut.exit_app":          {"zh": "退出程序",                         "en": "Exit"},
    "app.menu.shortcuts":         {"zh": "快捷键参考(&K)",                   "en": "&Shortcut Reference"},
    "app.menu.reset":             {"zh": "重置数据",                         "en": "Reset Data"},
    "app.menu.export":            {"zh": "导出数据",                         "en": "Export Data"},
    "reset.title":                {"zh": "重置数据",                         "en": "Reset Data"},
    "reset.confirm":              {"zh": "确定要删除所有标签页和图标吗？\n此操作不可撤销！",
                                                                           "en": "Delete all tabs and icons?\nThis cannot be undone!"},
    "reset.done":                 {"zh": "数据已重置",                       "en": "Data reset complete"},
    "export.select_folder":       {"zh": "选择导出目录",                     "en": "Select Export Folder"},
    "export.done":                {"zh": "数据已导出到 {path}",              "en": "Data exported to {path}"},
    "export.title":               {"zh": "导出数据",                         "en": "Export Data"},
    "export.failed_title":        {"zh": "导出失败",                         "en": "Export Failed"},
    "export.failed":              {"zh": "导出失败: {err}",                  "en": "Export failed: {err}"},
    "app.menu.import":            {"zh": "导入数据",                         "en": "Import Data"},
    "import.title":               {"zh": "导入数据",                         "en": "Import Data"},
    "import.select_file":         {"zh": "选择备份文件",                     "en": "Select Backup File"},
    "import.done":                {"zh": "数据已导入，界面已刷新",           "en": "Data imported, UI refreshed"},
    "progress.preparing":         {"zh": "准备中...",                         "en": "Preparing..."},
    "progress.done":              {"zh": "操作完成",                          "en": "Operation Complete"},
    "progress.failed":            {"zh": "操作失败: {err}",                   "en": "Failed: {err}"},
    "shortcut.import_data":       {"zh": "导入数据",                         "en": "Import Data"},
    "shortcut.reset_data":        {"zh": "重置数据",                         "en": "Reset Data"},
    "shortcut.export_data":       {"zh": "导出数据",                         "en": "Export Data"},

    "data.default_tab":            {"zh": "主页",                           "en": "Home"},
    # bulk delete
    "app.menu.batch":              {"zh": "批量管理",                       "en": "Batch Manage"},
    "app.menu.batch_delete":       {"zh": "批量删除勾选图标",                "en": "Delete Checked Icons"},
    "batch.none_checked":          {"zh": "未选中任何图标",                  "en": "No icons checked"},
    "bulk_delete.title":           {"zh": "批量删除图标",                    "en": "Bulk Delete Icons"},
    "bulk_delete.confirm":         {"zh": "确定要删除选中的 {count} 个图标吗？",
                                                                           "en": "Delete {count} selected icon(s)?"},
    "bulk_delete.done":            {"zh": "已删除 {count} 个图标",          "en": "Deleted {count} icon(s)"},
    "status.batch_mode_on":        {"zh": "批量管理模式：勾选图标后点击「批量删除勾选图标」",
                                                                           "en": "Batch mode: check icons then click 'Delete Checked Icons'"},

    # ── Search ──
    "search.placeholder":          {"zh": "搜索图标…",                      "en": "Search icons…"},

    # ── Progress dialog ──
    "progress.cannot_close":       {"zh": "操作进行中，无法关闭",           "en": "Operation in progress, cannot close"},

    # ── Shortcut dialog extras ──
    "shortcut.new_shortcut":       {"zh": "新建快捷方式图标",               "en": "New Shortcut Icon"},
    "shortcut.find":               {"zh": "查找图标",                       "en": "Find Icons"},
}
# fmt: on
