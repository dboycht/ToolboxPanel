"""双语翻译模块 · Bilingual i18n module.

Usage:
    from .i18n import tr, set_language, current_lang, on_language_changed
    label.setText(tr("key", name="World"))
    on_language_changed(lambda: refresh_ui())
"""
from __future__ import annotations
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
    "app.menu.theme":              {"zh": "主题",                           "en": "Theme"},
    "theme.light":                 {"zh": "浅色",                           "en": "Light"},
    "theme.dark":                  {"zh": "深色",                           "en": "Dark"},
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
        "工具箱 v{version} — 手机桌面风格的启动器\n"
        "作者: dboycht\n"
        "项目地址: https://github.com/dboycht/ToolboxPanel\n\n"
        "• 从资源管理器拖入文件/文件夹/快捷方式即可创建图标\n"
        "• 双击图标打开，右键查看更多选项\n"
        "• 右键空白区域创建 URL / 命令图标\n"
        "• 图标和标签页均可拖动排序\n"
        "• 数据自动保存到 data/ 文件夹\n"
        "• 支持搜索过滤、图标大小切换、打开方式",
                                    "en":
        "Toolbox v{version} — Phone‑home‑screen style launcher\n"
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

    # ── Settings / theme (v2.0.1 新增：主题高度自定义) ──
    "settings.title":              {"zh": "设置",                           "en": "Settings"},
    "settings.preset":             {"zh": "预置主题",                       "en": "Preset theme"},
    "settings.tuning":             {"zh": "外观微调",                       "en": "Appearance tuning"},
    "settings.language":           {"zh": "语言",                           "en": "Language"},
    "settings.reset":              {"zh": "恢复默认外观",                   "en": "Reset appearance"},
    "settings.tuning_hint":        {"zh": "拖动滑杆即时生效，偏好自动保存",
                                                                           "en": "Sliders apply instantly; preferences are saved"},
    "theme.midnight":              {"zh": "午夜蓝",                         "en": "Midnight"},
    "theme.grape":                 {"zh": "葡萄紫",                         "en": "Grape"},
    "theme.matcha":                {"zh": "抹茶绿",                         "en": "Matcha"},
    "search.no_result":            {"zh": "没有匹配的图标",                 "en": "No matching icons"},
    "grid.empty_hint":             {"zh": "拖入文件 / 文件夹 / 快捷方式即可创建图标\n或右键空白区域新建",
                                                                           "en": "Drag files / folders / shortcuts here to create icons\nor right-click empty area"},

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
    "btn.close":                   {"zh": "关闭",                           "en": "Close"},
    "edit.browse":                 {"zh": "浏览…",                          "en": "Browse…"},
    # list tab
    "list.new_tab":                {"zh": "新建列表标签页",                 "en": "New List Tab"},
    "list.default_name":           {"zh": "列表",                           "en": "List"},
    "list.new_item":               {"zh": "新建列表项",                     "en": "New List Item"},
    "list.edit_item":              {"zh": "编辑列表项",                     "en": "Edit List Item"},
    "list.col.desc":               {"zh": "说明",                           "en": "Description"},
    "list.col.path":               {"zh": "路径",                           "en": "Path"},
    "list.desc_ph":                {"zh": "文本说明",                       "en": "Description"},
    "list.select_file":            {"zh": "选择文件",                       "en": "Select File"},
    "list.select_folder":          {"zh": "选择文件夹",                     "en": "Select Folder"},
    "list.empty_hint":             {"zh": "空白列表 — 右键空白处新建列表项", "en": "Empty list — right-click to add an item"},
    "list.delete_title":           {"zh": "删除列表项",                     "en": "Delete List Item"},
    "list.confirm_delete":         {"zh": "确定要删除列表项「{desc}」吗？", "en": "Delete list item '{desc}'?"},
    "list.this_row":               {"zh": "此行",                           "en": "this row"},
    "list.desc_updated":           {"zh": "说明已更新",                     "en": "Description updated"},
    "list.path_updated":           {"zh": "路径已更新",                     "en": "Path updated"},
    "list.item_added":             {"zh": "已添加列表项: {desc}",           "en": "List item added: {desc}"},
    "list.reordered":              {"zh": "列表项顺序已更新",               "en": "List order updated"},
    "list.pick_title":             {"zh": "为「{title}」选择路径",          "en": "Pick a path for '{title}'"},
    "list.pick_hint":              {"zh": "选择文件或文件夹：",             "en": "Choose a file or folder:"},
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
    "import.confirm.title":       {"zh": "确认导入",                         "en": "Confirm Import"},
    "import.confirm.text":        {"zh": "导入将清空当前所有标签页、图标和设置，并用备份文件的内容覆盖。\n此操作不可撤销，确定继续吗？",
                                                                            "en": "Importing will clear all current tabs, icons and settings,\nthen overwrite them with the backup contents.\nThis cannot be undone. Continue?"},
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

    "status.theme_switched":       {"zh": "已切换到{theme}主题",            "en": "Switched to {theme} theme"},

    # ── Search ──
    "search.placeholder":          {"zh": "搜索图标…",                      "en": "Search icons…"},

    # ── Progress dialog ──
    "progress.cannot_close":       {"zh": "操作进行中，无法关闭",           "en": "Operation in progress, cannot close"},

    # ── Shortcut dialog extras ──
    "shortcut.new_shortcut":       {"zh": "新建快捷方式图标",               "en": "New Shortcut Icon"},
    "shortcut.find":               {"zh": "查找图标",                       "en": "Find Icons"},

    # ══════════════════════════════════════════════════════════════════════════
    # WinUI 3 线新增（2026-09-18，v2.0.3 i18n）
    #   · 上面原有 key **一个字都没改**（key 名与文案照旧，C# 侧有保真测试逐条比对）；
    #   · 下面这些是 WinUI 线独有的界面元素 / 提示语（原版没有对应条目）：
    #     关于对话框的分项字段、批量管理条的按钮、搜索计数、拖放失败原因、
    #     备份进度日志、示例图标名字（首次运行时写入 tabs.json 的数据）。
    # ══════════════════════════════════════════════════════════════════════════

    # ── 关于对话框（WinUI 线把原版 QMessageBox 拆成了结构化字段）──
    "about.subtitle":              {"zh": "手机桌面风格的启动器",           "en": "Phone-home-screen style launcher"},
    "about.author":                {"zh": "作者: {author}",                 "en": "Author: {author}"},
    "about.project":               {"zh": "项目地址: {url}",                "en": "Project: {url}"},
    "about.diagnostics":           {"zh": "诊断信息:",                      "en": "Diagnostics:"},
    "about.data_dir":              {"zh": "数据目录",                       "en": "Data directory"},
    "about.mode":                  {"zh": "运行模式",                       "en": "Run mode"},
    "about.mode.normal":           {"zh": "正常模式",                       "en": "Normal mode"},
    "about.mode.demo":             {"zh": "演示模式（不读写数据文件）",      "en": "Demo mode (no files read or written)"},
    "about.unlocated":             {"zh": "（未定位）",                     "en": "(not located)"},
    "about.unknown":               {"zh": "未知",                           "en": "Unknown"},

    # ── 校验 / 拖放 / 启动的补充错误 ──
    "validate.unknown_type":       {"zh": "未知的图标类型: {type}",         "en": "Unknown icon type: {type}"},
    "list.error.name_required":    {"zh": "说明不能为空",                   "en": "Description cannot be empty"},
    "list.error.both_empty":       {"zh": "说明和路径至少要填一个",         "en": "Description or path is required"},
    "status.url_empty":            {"zh": "URL 为空",                       "en": "URL is empty"},
    "status.path_empty":           {"zh": "路径为空",                       "en": "Path is empty"},
    "status.file_not_found":       {"zh": "文件不存在",                     "en": "File not found"},
    "status.shortcut_target_missing": {"zh": "快捷方式目标不存在: {path}",  "en": "Shortcut target not found: {path}"},
    "drag.error.source_missing":   {"zh": "来源标签页已不存在",             "en": "Source tab no longer exists"},
    "drag.error.target_missing":   {"zh": "目标标签页已不存在",             "en": "Target tab no longer exists"},
    "drag.error.icon_to_list":     {"zh": "图标不能放到列表页",             "en": "Icons cannot be dropped on a list tab"},
    "drag.error.list_to_grid":     {"zh": "列表项不能放到网格页",           "en": "List items cannot be dropped on a grid tab"},
    "drag.error.icon_gone":        {"zh": "被拖动的图标已不存在",           "en": "The dragged icon no longer exists"},
    "drag.error.list_item_gone":   {"zh": "被拖动的列表项已不存在",         "en": "The dragged list item no longer exists"},
    "drag.error.item_gone":        {"zh": "被拖动的项已不存在",             "en": "The dragged item no longer exists"},
    "drag.error.target_not_grid":  {"zh": "目标标签页不存在或不是网格页",   "en": "Target tab does not exist or is not a grid tab"},

    # ── 搜索（WinUI 线的补充）──
    "search.no_result_list":       {"zh": "没有匹配的列表项",               "en": "No matching list items"},
    "search.count":                {"zh": "匹配 {matched} / {total}",       "en": "{matched} / {total} matched"},

    # ── 批量管理条（原版是菜单项，WinUI 线做成了一条工具栏）──
    "bulk.exit":                   {"zh": "退出批量管理",                   "en": "Exit Batch Mode"},
    "bulk.select_all":             {"zh": "全选",                           "en": "Select All"},
    "bulk.selected":               {"zh": "已选 {count} 个",                "en": "{count} selected"},

    # ── 备份 / 恢复的进度日志与失败原因（WinUI 线进度对话框）──
    "backup.log.collect":          {"zh": "正在收集数据文件...",            "en": "Collecting data files..."},
    "backup.log.create_zip":       {"zh": "创建压缩包: {name}",             "en": "Creating archive: {name}"},
    "backup.log.write_metadata":   {"zh": "写入元数据...",                  "en": "Writing metadata..."},
    "backup.log.compress":         {"zh": "压缩: {name}",                   "en": "Compressing: {name}"},
    "backup.log.exported":         {"zh": "导出完成 ({size} KB)",           "en": "Export complete ({size} KB)"},
    "backup.log.open_zip":         {"zh": "打开压缩包: {name}",             "en": "Opening archive: {name}"},
    "backup.log.read_metadata":    {"zh": "读取元数据...",                  "en": "Reading metadata..."},
    "backup.log.meta_version":     {"zh": "版本: {version}",                "en": "Version: {version}"},
    "backup.log.meta_exported":    {"zh": "导出时间: {time}",               "en": "Exported at: {time}"},
    "backup.log.meta_counts":      {"zh": "标签页: {tabs} 图标: {icons}",   "en": "Tabs: {tabs} Icons: {icons}"},
    "backup.log.clear":            {"zh": "清除当前数据...",                "en": "Clearing current data..."},
    "backup.log.skip_unsafe":      {"zh": "跳过越界条目: {name}",           "en": "Skipping unsafe entry: {name}"},
    "backup.log.extract":          {"zh": "解压: {name}",                   "en": "Extracting: {name}"},
    "backup.log.imported":         {"zh": "导入完成",                       "en": "Import complete"},
    "backup.error.not_found":      {"zh": "找不到备份文件",                 "en": "Backup file not found"},
    "backup.error.no_metadata":    {"zh": "无效备份：缺少 metadata.json",   "en": "Invalid backup: metadata.json is missing"},
    "backup.error.bad_metadata":   {"zh": "无效备份：metadata.json 解析失败",
                                                                           "en": "Invalid backup: cannot parse metadata.json"},

    # ── 示例图标（首次运行写入 tabs.json 的名字；跟着"创建时"的界面语言走）──
    "data.sample_tab":             {"zh": "示例",                           "en": "Sample"},
    "sample.notepad":              {"zh": "记事本",                         "en": "Notepad"},
    "sample.calc":                 {"zh": "计算器",                         "en": "Calculator"},
    "sample.cmd":                  {"zh": "命令提示符",                     "en": "Command Prompt"},
    "sample.powershell":           {"zh": "PowerShell",                     "en": "PowerShell"},
    "sample.explorer":             {"zh": "资源管理器",                     "en": "File Explorer"},
    "sample.taskmgr":              {"zh": "任务管理器",                     "en": "Task Manager"},
    "sample.regedit":              {"zh": "注册表编辑器",                   "en": "Registry Editor"},
    "sample.msinfo":               {"zh": "系统信息",                       "en": "System Information"},
    "sample.cleanmgr":             {"zh": "磁盘清理",                       "en": "Disk Cleanup"},
    "sample.eventvwr":             {"zh": "事件查看器",                     "en": "Event Viewer"},
    "sample.devmgmt":              {"zh": "设备管理器",                     "en": "Device Manager"},
    "sample.charmap":              {"zh": "字符映射表",                     "en": "Character Map"},
    "sample.osk":                  {"zh": "屏幕键盘",                       "en": "On-Screen Keyboard"},
    "sample.control":              {"zh": "控制面板",                       "en": "Control Panel"},
    "sample.etc":                  {"zh": "配置目录",                       "en": "Config Folder"},
    "sample.windows":              {"zh": "Windows 目录",                   "en": "Windows Folder"},
    "sample.website":              {"zh": "示例网站",                       "en": "Example Website"},
    "sample.bing":                 {"zh": "必应搜索",                       "en": "Bing Search"},
    "sample.echo":                 {"zh": "回显命令",                       "en": "Echo Command"},

    # ── WinUI 3 线的界面骨架（标题栏 / 状态栏 / 设置面板 / 空页提示）──
    "titlebar.settings":           {"zh": "设置",                           "en": "Settings"},
    "titlebar.about":              {"zh": "关于",                           "en": "About"},
    "search.tip":                  {"zh": "搜索（Ctrl+F）",                 "en": "Search (Ctrl+F)"},
    "search.close_tip":            {"zh": "关闭搜索（Esc）",                "en": "Close search (Esc)"},
    "search.no_match_hint":        {"zh": "换个关键词试试，或按 Esc 关闭搜索",
                                                                           "en": "Try another keyword, or press Esc to close the search"},
    "grid.empty_title":            {"zh": "这一页还没有图标",               "en": "This tab has no icons yet"},
    "list.empty_title":            {"zh": "这一页还没有列表项",             "en": "This tab has no list items yet"},
    "tab.count.icons":             {"zh": "{count} 个图标",                 "en": "{count} icon(s)"},
    "tab.count.items":             {"zh": "{count} 项",                     "en": "{count} item(s)"},
    "tab.unnamed":                 {"zh": "(未命名标签页)",                 "en": "(Unnamed tab)"},
    "icon.unnamed":                {"zh": "(未命名)",                       "en": "(Unnamed)"},
    "icon.type.file":              {"zh": "文件",                           "en": "File"},
    "icon.type.folder":            {"zh": "文件夹",                         "en": "Folder"},
    "icon.type.shortcut":          {"zh": "快捷方式",                       "en": "Shortcut"},
    "icon.type.url":               {"zh": "网址",                           "en": "URL"},
    "icon.type.command":           {"zh": "命令",                           "en": "Command"},
    "status.loading":              {"zh": "正在载入…",                      "en": "Loading…"},
    "status.summary":              {"zh": "{tabs} 个标签页 · {icons} 个图标 · {items} 个列表项",
                                                                           "en": "{tabs} tab(s) · {icons} icon(s) · {items} list item(s)"},
    "status.summary.extracted":    {"zh": "（新提取 {count} 个图标）",      "en": " ({count} icon(s) newly extracted)"},
    "status.demo.summary":         {"zh": "演示数据（不读写任何文件）",     "en": "Demo data (no files read or written)"},
    "status.demo.data_dir":        {"zh": "(演示模式：未使用数据目录)",     "en": "(Demo mode: no data directory)"},
    "demo.no_save":                {"zh": "演示模式：不会真的保存",         "en": "Demo mode: nothing is saved"},
    "demo.no_open":                {"zh": "演示模式：不会真的打开",         "en": "Demo mode: nothing is opened"},
    "demo.no_export":              {"zh": "演示模式：不会真的导出",         "en": "Demo mode: nothing is exported"},
    "demo.no_import":              {"zh": "演示模式：不会真的导入",         "en": "Demo mode: nothing is imported"},
    "demo.title":                  {"zh": "ToolboxPanel · 纯 UI 演示",      "en": "ToolboxPanel · UI demo"},
    "window.backdrop.initializing": {"zh": "窗口材质：未初始化",            "en": "Window material: initializing"},
    "window.backdrop.solid":       {"zh": "纯色回退",                       "en": "Solid color fallback"},
    "window.backdrop.failed":      {"zh": "构造失败",                       "en": "Creation failed"},
    "window.backdrop.line":        {"zh": "窗口材质 = {label}；本机支持 = {supported}；启用 = {glass}",
                                                                           "en": "Window material = {label}; supported = {supported}; active = {glass}"},
    "status.tab_rejects":          {"zh": "「{name}」不收这一类项目",       "en": "'{name}' does not accept this kind of item"},
    "status.moved_to":             {"zh": "已移动：{item} → 「{tab}」",     "en": "Moved: {item} → '{tab}'"},
    "status.nothing_to_add":       {"zh": "没有可添加的项目",               "en": "Nothing to add"},
    "status.drop_failed":          {"zh": "拖入添加失败: {err}",            "en": "Drop import failed: {err}"},
    "status.rename_failed":        {"zh": "重命名失败",                     "en": "Rename failed"},
    "status.action_failed":        {"zh": "{action}失败",                   "en": "{action} failed"},
    "status.open_short":           {"zh": "已打开：{name}",                 "en": "Opened: {name}"},
    "status.open_failed_short":    {"zh": "打开失败（{name}）：{err}",      "en": "Open failed ({name}): {err}"},
    "progress.failed_short":       {"zh": "操作失败",                       "en": "Operation failed"},
    "progress.action_done":        {"zh": "{action}完成：{message}",        "en": "{action} complete: {message}"},
    "progress.action_failed":      {"zh": "{action}失败：{message}",        "en": "{action} failed: {message}"},
    "action.export":               {"zh": "导出",                           "en": "Export"},
    "action.import":               {"zh": "导入",                           "en": "Import"},
    "action.create":               {"zh": "新建",                           "en": "Create"},
    "action.edit":                 {"zh": "编辑",                           "en": "Edit"},
    "action.rename":               {"zh": "重命名",                         "en": "Rename"},
    "action.delete":               {"zh": "删除",                           "en": "Delete"},
    "action.move":                 {"zh": "移动",                           "en": "Move"},
    "action.open":                 {"zh": "打开",                           "en": "Open"},
    "status.action_failed_detail": {"zh": "{action}失败: {err}",            "en": "{action} failed: {err}"},
    "import.button":               {"zh": "导入",                           "en": "Import"},
    "import.failed":               {"zh": "导入失败: {err}",                "en": "Import failed: {err}"},
    "icon.rename.title":           {"zh": "重命名图标",                     "en": "Rename Icon"},
    "list.rename.title":           {"zh": "重命名列表项",                   "en": "Rename List Item"},
    "edit.field.name_ph":          {"zh": "显示名称",                       "en": "Display name"},
    "edit.title.path_ph":          {"zh": "目标程序或 .lnk 路径",           "en": "Target program or .lnk path"},
    "about.title":                 {"zh": "关于 {product}",                 "en": "About {product}"},
    "about.diagnostics_header":    {"zh": "诊断信息",                       "en": "Diagnostics"},
    "about.author_short":          {"zh": "作者: {author}",                 "en": "Author: {author}"},
    "diag.dotnet":                 {"zh": ".NET 运行时",                    "en": ".NET runtime"},
    "diag.windows_app_sdk":        {"zh": "Windows App SDK",                "en": "Windows App SDK"},
    "diag.system":                 {"zh": "系统",                           "en": "System"},
    "diag.exe_path":               {"zh": "程序路径",                       "en": "Executable path"},

    # ── 设置面板（WinUI 线的分节与文案）──
    "settings.section.theme":      {"zh": "主题",                           "en": "Theme"},
    "settings.ui_theme":           {"zh": "界面主题",                       "en": "Interface theme"},
    "settings.theme.system":       {"zh": "跟随系统",                       "en": "Follow system"},
    "settings.theme.hint":         {"zh": "主题是全局的：主窗口、标签栏、状态栏与这个设置面板用同一套颜色令牌。",
                                                                           "en": "The theme is global: the window, tab bar, status bar and this panel share one set of color tokens."},
    "settings.section.window":     {"zh": "窗口",                           "en": "Window"},
    "settings.backdrop":           {"zh": "窗口材质",                       "en": "Window material"},
    "settings.backdrop.mica":      {"zh": "Mica（推荐）",                   "en": "Mica (recommended)"},
    "settings.backdrop.mica_alt":  {"zh": "Mica 变体（BaseAlt）",           "en": "Mica Alt (BaseAlt)"},
    "settings.backdrop.acrylic":   {"zh": "桌面 Acrylic",                   "en": "Desktop Acrylic"},
    "settings.backdrop.acrylic_thin": {"zh": "桌面 Acrylic（更透）",        "en": "Desktop Acrylic (thinner)"},
    "settings.backdrop.none":      {"zh": "纯色（不启用材质）",             "en": "Solid color (no material)"},
    "settings.backdrop.hint":      {"zh": "Mica 需要 Win11；系统在省电 / 关闭透明效果 / 远程桌面等情况下会自动回退纯色。",
                                                                           "en": "Mica needs Windows 11; the system falls back to a solid color on battery saver, when transparency is off, over remote desktop, etc."},
    "settings.section.tabbar":     {"zh": "标签栏",                         "en": "Tab bar"},
    "settings.tab_icons":          {"zh": "标签图标",                       "en": "Tab icons"},
    "settings.tab_icons.text":     {"zh": "只保留文字",                     "en": "Text only"},
    "settings.tab_icons.always":   {"zh": "常驻显示图标",                   "en": "Always show icons"},
    "settings.tab_icons.hover":    {"zh": "悬停时显示（图标浮现 + 标签拉伸）",
                                                                           "en": "On hover (icon fades in, tab expands)"},
    "settings.show_counts":        {"zh": "显示数量",                       "en": "Show counts"},
    "settings.on":                 {"zh": "显示",                           "en": "Show"},
    "settings.off":                {"zh": "隐藏",                           "en": "Hide"},
    "settings.tabbar.hint":        {"zh": "网格页的图标字形是密集方格，列表页是项目符号列表，便于一眼区分。",
                                                                           "en": "Grid tabs use a dense-grid glyph, list tabs a bullet list — easy to tell apart at a glance."},
    "settings.section.icons":      {"zh": "图标",                           "en": "Icons"},
    "settings.icon_size":          {"zh": "图标大小",                       "en": "Icon size"},
    "settings.icon_size.medium":   {"zh": "中（默认）",                     "en": "Medium (default)"},
    "settings.icons.hint":         {"zh": "只影响网格页图块的显示大小；图标位图仍是同一份缓存，切换不会重新提取。",
                                                                           "en": "Only the tile size on grid tabs changes; the icon bitmaps are reused and never re-extracted."},
    "settings.section.animation":  {"zh": "动效",                           "en": "Animations"},
    "settings.animations.enabled": {"zh": "启用动效",                       "en": "Enable animations"},
    "settings.animations.on":      {"zh": "开",                             "en": "On"},
    "settings.animations.off":     {"zh": "关",                             "en": "Off"},
    "settings.animations.duration": {"zh": "动画时长（毫秒）",              "en": "Duration (ms)"},
    "settings.animations.easing":  {"zh": "动画曲线",                       "en": "Easing"},
    "settings.animations.soft":    {"zh": "柔和（起步快、收尾很慢）",       "en": "Soft (quick start, very slow finish)"},
    "settings.animations.standard": {"zh": "标准（推荐）",                  "en": "Standard (recommended)"},
    "settings.animations.snappy":  {"zh": "干脆（收尾快）",                 "en": "Snappy (fast finish)"},
    "settings.animations.preview": {"zh": "预览动效",                       "en": "Preview animation"},
    "settings.animations.hint":    {"zh": "入场是整页一起淡入（图标 / 列表项同时从无到有），没有逐个浮现的交错。",
                                                                           "en": "The entrance is a whole-page fade-in (all items appear together); there is no per-item stagger."},
    "settings.section.data":       {"zh": "数据",                           "en": "Data"},
    "settings.data_dir":           {"zh": "数据目录：{path}",               "en": "Data directory: {path}"},
    "settings.export":             {"zh": "导出备份…",                      "en": "Export backup…"},
    "settings.export.tip":         {"zh": "把 tabs.json / config.json / 图标缓存打包成 ZIP（Ctrl+Shift+E）",
                                                                           "en": "Pack tabs.json, config.json and the icon cache into a ZIP (Ctrl+Shift+E)"},
    "settings.import":             {"zh": "导入备份…",                      "en": "Import backup…"},
    "settings.import.tip":         {"zh": "从 ZIP 恢复数据（会先清空当前数据，Ctrl+Shift+I）",
                                                                           "en": "Restore data from a ZIP (current data is cleared first, Ctrl+Shift+I)"},
    "settings.data.hint":          {"zh": "备份包为 ZIP：内含 metadata.json 与 data/ 下的 tabs.json、config.json、图标缓存；与旧版（v1.11.6 / v2.0.1）互相兼容。",
                                                                           "en": "The backup is a ZIP with metadata.json plus tabs.json, config.json and the icon cache; compatible with v1.11.6 / v2.0.1."},
    "settings.reset_all":          {"zh": "恢复默认设置",                   "en": "Reset settings to defaults"},
    "settings.close_tip":          {"zh": "关闭（Esc / 点空白处也可以）",   "en": "Close (Esc or click outside)"},
    "settings.language.hint":      {"zh": "切换后界面立即变成所选语言；数据里的名字与路径保持原样。",
                                                                           "en": "The UI switches immediately; names and paths inside your data are left untouched."},
    "settings.ui_language":        {"zh": "界面语言",                       "en": "Interface language"},
    "settings.lang.zh":            {"zh": "中文",                           "en": "Chinese"},
    "settings.lang.en":            {"zh": "English",                        "en": "English"},

    # ── WinUI 追加（2026-09-19，v2.0.6「快捷键设置」）────────────────────────────
    # ⚠️ 这一段是 **WinUI 线专用的界面文案**（原版 v1.11.6 只有只读的「快捷键参考」窗口）。
    #    加在这里而不是只加 C# 侧，是因为 `tests/I18nTests.cs` 有一条**保真测试**：
    #    它用真实 Python 解析本文件，要求**两边的 key 集合完全一致、中英逐字一致** ——
    #    只改 C# 那半边会立刻变红（见开发副本 ERROR.md E36）。
    #    改完这里必须重跑 `python gen-i18n-table.py` 重新生成 `core/I18n.Table.cs`。
    "shortcut.settings.title":     {"zh": "快捷键设置",                     "en": "Shortcut Settings"},
    "shortcut.col.status":         {"zh": "状态",                           "en": "Status"},
    "shortcut.hint":               {"zh": "点「快捷键」那一列的按钮即可改键；改动立即生效并保存。",
                                                                           "en": "Click a key to rebind it; changes apply and are saved immediately."},
    "shortcut.status.ok":          {"zh": "可用",                           "en": "Available"},
    "shortcut.status.duplicate":   {"zh": "与「{name}」重复",               "en": "Duplicate of '{name}'"},
    "shortcut.status.text_editing": {"zh": "输入文字时会先被输入框接管",     "en": "Taken by text boxes while typing"},
    "shortcut.status.reserved":    {"zh": "系统保留组合",                   "en": "Reserved by Windows"},
    "shortcut.status.taken":       {"zh": "已被其他程序占用",               "en": "Taken by another app"},
    "shortcut.status.no_modifier": {"zh": "必须包含 Ctrl 或 Alt",           "en": "Must include Ctrl or Alt"},
    "shortcut.recording":          {"zh": "请按下新的组合键…（Esc 取消）",   "en": "Press the new shortcut... (Esc to cancel)"},
    "shortcut.changed":            {"zh": "「{name}」已改为 {keys}",        "en": "'{name}' is now {keys}"},
    "shortcut.reset_one":          {"zh": "恢复这一条的默认键位",           "en": "Reset this shortcut"},
    "shortcut.reset_all":          {"zh": "全部恢复默认",                   "en": "Reset all to defaults"},
    "shortcut.reset_done":         {"zh": "快捷键已全部恢复默认",           "en": "Shortcuts reset to defaults"},
    "shortcut.check":              {"zh": "检测冲突",                       "en": "Check conflicts"},
    "shortcut.check_done":         {"zh": "已检测 {total} 条：{issues} 条有问题",
                                                                           "en": "Checked {total}: {issues} issue(s)"},
    "shortcut.check_clean":        {"zh": "未发现冲突",                     "en": "No conflicts found"},
}
# fmt: on
