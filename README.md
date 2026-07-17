# 工具箱 · Toolbox

一个 **手机桌面风格** 的 Windows 启动器，使用 PyQt6 构建。  
拖入文件/文件夹/快捷方式即可生成图标，支持自定义 URL 和命令行图标。

A **phone‑home‑screen‑style** launcher for Windows, built with PyQt6.  
Drag files, folders, or shortcuts onto the grid to create icons. Supports custom URL and command icons.

---

## 预览 · Preview

```
┌──────────────────────────────────────────────────┐
│  文件  帮助                                       │  ← 菜单栏
├──────────────────────────────────────────────────┤
│  [主页]  [工作]  [工具]                    [+]  │  ← 可拖动排序的标签页
├──────────────────────────────────────────────────┤
│                                                  │
│   ┌──────┐   ┌──────┐   ┌──────┐               │
│   │  📁  │   │  📄  │   │  🌐  │               │  ← 48×48 图标网格
│   │ 项目  │   │ 报表  │   │ GitHub│               │
│   └──────┘   └──────┘   └──────┘               │
│                                                  │
├──────────────────────────────────────────────────┤
│  已加载 1 个标签页                                │  ← 状态栏
└──────────────────────────────────────────────────┘
```

---

## 功能 · Features

| | |
|---|---|
| 🖱️ **拖放添加** | 从资源管理器拖入文件/文件夹/.lnk 快捷方式即可创建图标 |
| 🔗 **自定义图标** | 右键空白区域 → 新建网址/命令图标 |
| 📱 **手机网格布局** | 图标自动排列，窗口缩放时自动调整列数 |
| ↔️ **拖动排序** | 图标可在标签页内拖动排序，也可跨标签页移动 |
| 🏷️ **标签页管理** | 拖动标签页排序、右键重命名/新建/删除 |
| ✏️ **图标改名** | 双击图标文字即可编辑名称 |
| 🖱️ **右键菜单** | 打开、打开文件位置、重命名、删除 |
| 💾 **自动保存** | 所有操作即时写入 `data/tabs.json`，支持启动恢复 |
| 🎨 **Win11 风格** | 圆角、浅色主题、Fluent Design 风格 |

| | |
|---|---|
| 🖱️ **Drag & drop** | Drop files, folders, or .lnk shortcuts from Explorer to create icons |
| 🔗 **Custom icons** | Right-click empty area → New URL / Command icon |
| 📱 **Phone‑grid layout** | Auto‑flow grid, columns adjust on window resize |
| ↔️ **Drag to reorder** | Rearrange icons within a tab; drag to another tab to move |
| 🏷️ **Tab management** | Drag tabs to reorder; right‑click to rename / add / delete |
| ✏️ **Rename icons** | Double‑click the label to edit |
| 🖱️ **Context menu** | Open, open file location, rename, remove |
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
│   └── icons/                     # 图标 PNG 缓存
└── src/toolbox/
    ├── main.py                    # QApplication + Win11 样式
    ├── app_window.py              # QMainWindow 主窗口
    ├── tab_widget.py              # QTabWidget — 标签页管理
    ├── icon_grid.py               # QScrollArea — 图标网格 & 拖放处理
    ├── icon_widget.py             # 单个图标组件 (图标 + 标签)
    ├── icon_label.py              # 可编辑标签 (双击改名)
    ├── flow_layout.py             # 自定义 QLayout — 手机网格布局
    ├── context_menu.py            # 右键菜单工厂
    ├── models/
    │   ├── data_store.py          # JSON 持久化
    │   ├── tab_model.py           # 标签页数据模型
    │   └── icon_model.py          # 图标数据模型 + IconType 枚举
    ├── services/
    │   ├── icon_resolver.py       # 系统图标提取 & 缓存
    │   ├── launcher.py            # 打开文件/URL/运行命令
    │   └── drag_data.py           # MIME 数据解析
    └── utils/
        └── windows_shortcut.py    # pywin32 .lnk 解析
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

### v1.00.2 (2026-07-17)

**修复**
- 修复图标网格对齐 — 不同标签高度的图标现在顶部对齐
- 修复文字被裁切（如 "Bandon" 显示为 "andon"）— 加宽组件并减少内边距
- 修复长英文单词生硬截断 — 添加连字符（hyphenation）支持
- 修复 QScrollArea 拖放事件被容器拦截 — 使用自定义 DropContainer
- 修复 FlowLayout 未将 Widget 纳入父容器 — 图标数据保存但不可见

**新增**
- 中英文双语切换（`文件 → 语言`），偏好自动保存
- 图标文字自动换行，超过三行省略为 "…"
- 完整 Win11 Fluent Design 风格重做
- 关于对话框：版本号、作者 dboycht、项目地址
- 图标文字白色 + 半透明暗色圆角底
- 图标悬停高亮效果

### v1.00 (2026-07-16)

- 初始版本
- 多标签页 + 手机网格布局
- 拖入文件/文件夹/.lnk 创建图标
- 自定义 URL / 命令图标
- 图标拖动排序、跨标签页移动
- 标签页拖动排序、右键重命名
- 图标双击改名、右键菜单
- JSON 自动持久化

---

## 许可 · License

MIT
