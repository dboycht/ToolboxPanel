# 工具箱 · Toolbox

一个 **手机桌面风格** 的 Windows 启动器，使用 PyQt6 构建。  
拖入文件/文件夹/快捷方式即可生成图标，支持自定义 URL 和命令行图标。

A **phone‑home‑screen‑style** launcher for Windows, built with PyQt6.  
Drag files, folders, or shortcuts onto the grid to create icons. Supports custom URL and command icons.

---

## 预览 · Preview

```
┌──────────────────────────────────────────────┐
│  文件  帮助                                   │  ← 菜单栏
├──────────────────────────────────────────────┤
│  [主页]  [工作]  [工具]                      │  ← 多行标签栏（缩小时自动换行）
│                                              │
├──────────────────────────────────────────────┤
│   ┌──────┐   ┌──────┐   ┌──────┐           │
│   │  📁  │   │  📄  │   │  🌐  │           │  ← 48×48 图标网格
│   │ 项目  │   │ 报表  │   │ GitHub│           │
│   └──────┘   └──────┘   └──────┘           │
│                                              │
├──────────────────────────────────────────────┤
│  就绪 — 拖入文件或右键空白区域创建图标       │  ← 状态栏
└──────────────────────────────────────────────┘
```

---

## 功能 · Features

| | |
|---|---|
| 🖱️ **拖放添加** | 从资源管理器拖入文件/文件夹/.lnk 快捷方式即可创建图标 |
| 🔗 **自定义图标** | 右键空白区域 → 新建网址/命令图标 |
| ✅ **批量管理** | 文件 → 批量管理，勾选图标后批量删除 |
| 📱 **手机网格布局** | 图标自动排列，窗口缩放时自动调整列数 |
| ↔️ **拖动排序** | 图标可在标签页内拖动排序，也可跨标签页移动 |
| 🏷️ **多行标签栏** | 窗口缩小时标签页自动换行，双击/F2 重命名，←→ 切换 |
| ✏️ **图标改名** | 双击图标文字即可编辑名称 |
| 🌐 **中英双语** | 文件 → 语言 切换，偏好自动保存 |
| 📦 **导入导出** | ZIP 压缩包备份/恢复，含版本信息和进度条 |
| ⌨️ **快捷键** | 15+ 快捷键覆盖所有功能，Ctrl+B/Shift+Del/←→ 等 |
| 💾 **自动保存** | 所有操作即时写入 `data/tabs.json`，支持启动恢复 |
| 🎨 **Win11 风格** | 圆角、浅色主题、Fluent Design 风格 |

| | |
|---|---|
| 🖱️ **Drag & drop** | Drop files, folders, or .lnk shortcuts from Explorer to create icons |
| 🔗 **Custom icons** | Right-click empty area → New URL / Command icon |
| ✅ **Batch manage** | File → Batch Manage, check icons to bulk delete |
| 📱 **Phone‑grid layout** | Auto‑flow grid, columns adjust on window resize |
| ↔️ **Drag to reorder** | Rearrange icons within a tab; drag to another tab to move |
| 🏷️ **Multi-row tabs** | Tabs wrap on narrow windows; double-click/F2 to rename; arrows to switch |
| ✏️ **Rename icons** | Double‑click the label to edit |
| 🌐 **Bilingual** | File → Language, preference auto‑saved |
| 📦 **Backup** | ZIP export/import with metadata and progress bar |
| ⌨️ **Shortcuts** | 15+ keyboard shortcuts: Ctrl+B, Shift+Del, arrows, and more |
| 💾 **Auto‑save** | Instant persistence to `data/tabs.json`; survives restart |
| 🎨 **Win11 themed** | Rounded corners, light theme, Fluent Design aesthetic |

---

## 安装 · Install

```bash
# 克隆项目
git clone <repo-url>
cd VibeCoding

# 安装依赖 (仅两个)
pip install -r requirements.txt
```

### 依赖 · Dependencies

- **Python** ≥ 3.10
- **PyQt6** ≥ 6.5 — GUI 框架
- **pywin32** ≥ 305 — Windows .lnk 快捷方式解析

---

## 运行 · Run

```bash
python run.py
```

首次运行会自动创建 `data/` 文件夹并初始化配置。

---

## 编译 · Build

将工具箱打包为独立文件夹，无需安装 Python 即可运行。

```bash
# 安装 PyInstaller（一次性）
pip install pyinstaller

# 编译
python build.py

# 清理编译产物
python build.py clean
```

编译完成后，`dist/Toolbox/` 文件夹可复制到任意 Windows 电脑直接运行：

```
dist/Toolbox/
├── Toolbox.exe        ← 双击启动
└── _internal/         ← 运行时依赖（勿手动修改）
```

> 目录打包（`--onedir`）比单文件（`--onefile`）更可靠，启动更快，也是 PyInstaller 推荐的分发方式。

---

## 项目结构 · Project Structure

```
VibeCoding/
├── run.py                         # 开发入口 · Dev entry point
├── launcher.py                    # 打包入口 · Build entry point
├── build.py                       # 编译脚本 · Build script
├── requirements.txt               # 依赖 · Dependencies
├── README.md                      # 本文件 · This file
├── data/                          # 运行时数据 (自动创建 · auto‑created)
│   ├── tabs.json                  # 标签页 & 图标状态
│   ├── config.json                # 用户配置（语言偏好等）
│   └── icons/                     # 图标 PNG 缓存
└── src/toolbox/
    ├── main.py                    # QApplication + Win11 样式
    ├── app_window.py              # QMainWindow 主窗口
    ├── tab_widget.py              # QStackedWidget + WrapTabBar — 标签页管理
    ├── wrap_tab_bar.py            # 自定义多行标签栏
    ├── i18n.py                    # 中英双语翻译模块
    ├── shortcut_dialog.py         # 快捷键参考对话框
    ├── progress_dialog.py         # 进度条 + 日志对话框
    ├── icon_grid.py               # QScrollArea + DropContainer — 图标网格
    ├── icon_widget.py             # 单个图标组件 (图标 + 标签 + 复选框)
    ├── icon_label.py              # 可编辑标签 (双击改名/连字符/省略号)
    ├── flow_layout.py             # 自定义 QLayout — 手机网格 + 可变宽度
    ├── models/
    │   ├── data_store.py          # JSON 持久化
    │   ├── tab_model.py           # 标签页数据模型
    │   └── icon_model.py          # 图标数据模型 + IconType 枚举
    ├── services/
    │   ├── backup_manager.py      # ZIP 导入/导出 + 后台线程 + 元数据
    │   ├── icon_resolver.py       # 系统图标提取 & 缓存 & .lnk 解析
    │   └── launcher.py            # 打开文件/URL/运行命令/快捷方式
    └── utils/
        └── windows_shortcut.py    # pywin32 WScript.Shell .lnk 解析
```

---

## 数据格式 · Data Format

`data/tabs.json` 使用 `sort_order` 而非像素坐标存储图标位置，窗口缩放不会影响布局。

Icons use `sort_order` (not pixel coordinates) so resizing the window never breaks the layout.

```json
{
  "version": 1,
  "tabs": [
    {
      "id": "uuid",
      "name": "主页",
      "order": 0,
      "icons": [
        {
          "id": "uuid",
          "type": "file | folder | shortcut | url | command",
          "display_name": "我的文件",
          "source_path": "C:\\Users\\...\\file.docx",
          "target_path": "C:\\Users\\...\\file.docx",
          "arguments": "",
          "working_dir": "",
          "icon_cache_file": "uuid.png",
          "sort_order": 0
        }
      ]
    }
  ]
}
```

---

## 更新日志 · Changelog

### v1.10.3 (2026-07-19)

**新增**
- 全局快捷键系统：Ctrl+B 批量管理、Ctrl+W/R 标签操作、Ctrl+Shift+* 创建图标
- 快捷键参考对话框（帮助 -> 快捷键参考），中英双语表格
- ZIP 压缩包导入/导出：含 metadata（版本/时间/统计）+ 进度条 + 实时日志
- 导出文件名含时间戳 + 随机码，永不覆盖
- 重置数据功能（Ctrl+Shift+R）
- 所有菜单项标注快捷键

**修复**
- 清理冗余代码，删除 context_menu.py、drag_data.py 等未使用文件
- 清理 10+ 处未使用导入和死信号
- README 全面更新

### v1.10.2 (2026-07-19)

**新增**
- 批量管理模式：文件 -> 批量管理，图标右上角复选框，勾选后批量删除
- 多行标签栏：窗口缩小时标签页自动换行，不再隐藏
- 标签页按钮宽度根据名称自适应
- 双击标签页名称弹出重命名对话框
- F2 快捷键重命名当前标签页
- 左右方向键切换标签页
- 新建标签页时弹出名称输入对话框
- 编译脚本 build.py + launcher.py 打包入口

**修复**
- 修复快捷方式打开报错 WinError 5 — 改用 os.startfile(.lnk)
- 修复 QInputDialog.getText 按钮中英文不一致
- 修复新建标签页对话框取消后仍创建
- 删除冗余文件 context_menu.py, drag_data.py
- 清理未使用的信号和导入

### v1.10.1 (2026-07-18)

**新增**
- 左键单击图标选中（蓝色边框）
- Ctrl+单击多选
- F2 重命名选中图标
- Delete 批量删除选中图标
- 中英文双语切换，偏好保存到 data/config.json
- 关于对话框显示版本号、作者、项目地址

**修复**
- 图标网格对齐 — AlignTop + stretch
- 文字裁切 — 加宽组件 + 减少内边距
- 长英文单词断词 — 连字符 hyphenation
- QScrollArea 拖放事件拦截 — 自定义 DropContainer
- FlowLayout 未纳入父容器 — 使用 addWidget
- 菜单栏颜色对比度 — 深色底 + 白色文字

**变更**
- 图标文字自动换行，超过三行省略
- Win11 Fluent Design 风格
- 图标文字白色 + 半透明暗色圆角底
- 图标悬停高亮效果

### v1.00 (2026-07-16)

- 初始版本，核心功能完整

---

## 许可 · License

MIT
