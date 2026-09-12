"""QML 版应用入口：装配引擎、注入上下文属性、应用主题。

启动顺序有讲究（踩过坑）：
1. **先设 `QT_QUICK_CONTROLS_STYLE=Basic`** —— QtQuick.Controls 默认走原生 Windows
   样式，而原生样式**不允许**自定义 background/contentItem；必须在 QML 引擎加载前
   用环境变量切到 Basic（PyQt6 未提供 `QQuickStyle` 类，见 DEVELOPMENT.md §0.2）。
2. 再建 `QGuiApplication`（QML 场景用 QGuiApplication 而非 QApplication）。
3. 单实例守卫（复用原版 `single_instance.py` 的 QSharedMemory 逻辑，仅换窗口类型）。
4. 主题从 config.json 恢复后注入，避免首帧闪一下默认配色。
"""
from __future__ import annotations

import os

# ⚠️ 必须早于任何 QtQuick.Controls 的加载
os.environ.setdefault("QT_QUICK_CONTROLS_STYLE", "Basic")

import sys
from pathlib import Path

from PyQt6.QtCore import QUrl
from PyQt6.QtGui import QFont, QGuiApplication
from PyQt6.QtQml import QQmlApplicationEngine

from .. import __version__
from ..models.data_store import DataStore
from .bridge import Bridge
from .settings import Settings, get_data_dir
from .theme import Theme
from .tr import Tr
from .window import WindowController

QML_DIR = Path(__file__).resolve().parent.parent / "qml"


def main() -> int:
    qapp = QGuiApplication(sys.argv)
    qapp.setApplicationName("工具箱")
    qapp.setOrganizationName("Toolbox")

    # ── Win11 系统字体（与原版 main.py 一致）────────────────────────
    qapp.setFont(QFont("Microsoft YaHei UI", 9))

    # ── 单实例守卫 ──────────────────────────────────────────────────
    # data/tabs.json 是单文件即时保存，多实例并发会互相覆盖（与原版同样的理由）
    guard = None
    try:
        from ..single_instance import SingleInstanceGuard
        guard = SingleInstanceGuard()
        if not guard.acquire():
            guard.activate_existing()
            return 0
    except Exception:
        guard = None

    # ── 设置 / 主题 / 数据 ─────────────────────────────────────────
    settings = Settings(get_data_dir())
    theme = Theme(settings)
    theme.load_from_settings()

    store = DataStore(get_data_dir())
    bridge = Bridge(settings, store)
    win = WindowController()
    trc = Tr()

    # 语言切换 → 通知 QML 侧 `tr.t()` 的绑定重算（语义等同原版的
    # set_language() 回调刷新，见 tr.py 顶部说明）
    bridge.languageChanged.connect(trc.notify_language_changed)
    bridge.languageChanged.connect(theme.refresh_labels)

    engine = QQmlApplicationEngine()
    ctx = engine.rootContext()
    ctx.setContextProperty("theme", theme)
    ctx.setContextProperty("app", bridge)
    ctx.setContextProperty("win", win)
    ctx.setContextProperty("tr", trc)
    ctx.setContextProperty("appVersion", __version__)

    engine.addImportPath(str(QML_DIR))
    engine.load(QUrl.fromLocalFile(str(QML_DIR / "Main.qml")))

    roots = engine.rootObjects()
    if not roots:
        print("[toolbox] QML 加载失败，详见上方错误", file=sys.stderr)
        return 1

    window = roots[0]
    win.attach(window)

    # 窗口显示后把 HWND 写入共享内存，供后续实例拉取窗口
    if guard is not None:
        try:
            guard.register_window(window)
        except Exception:
            pass

    code = 0
    try:
        code = qapp.exec()
    finally:
        # ⚠️ 退出期噪音治理（实测定论）：
        # 若把 engine/根对象直接留给进程退出，Python 会先清模块全局，
        # 而 QML 引擎仍在，绑定重算读到 null，刷出一屏
        # "Cannot read property 'c' of null" —— **纯退出期噪音，非功能缺陷**。
        # 处理要点：`deleteLater()` 在事件循环**已停止**时不会再执行，
        # 所以必须**同步**拆除：先把上下文属性置空，再 delete 根对象
        # （根对象没了 → 所有 QML 绑定随之销毁 → 不会再有重算）。
        for name in ("theme", "app", "win", "tr", "appVersion"):
            try:
                ctx.setContextProperty(name, None)
            except Exception:
                pass
        for obj in list(engine.rootObjects()):
            try:
                obj.deleteLater()
            except Exception:
                pass
        try:
            del engine  # 释放引擎（此时已无绑定存活）
        except Exception:
            pass
    return code
