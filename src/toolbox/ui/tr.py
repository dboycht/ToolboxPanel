"""把 Python 侧现成的 i18n 暴露给 QML。

为什么不用 Qt 的 `qsTr()` / `qsTrId()`：
- 本项目已有完整的翻译表（`i18n.py`，182 条，zh/en），再加一套 .ts/.qm 会**双轨**，
  且语言切换要同时走两套机制（见 DEVELOPMENT.md §0.7 风险项）；
- 原版的行为是「`set_language()` → 回调刷新 UI」，QML 侧只要在语言变化时
  重新求值绑定即可 —— 这里用 `changed` 信号实现同一语义。

QML 用法：`text: tr.t("app.menu.new_tab")`
语言切换 → `changed` 触发 → 所有调用 `tr.t()` 的绑定自动重算。
"""
from __future__ import annotations

from PyQt6.QtCore import QObject, pyqtProperty, pyqtSignal, pyqtSlot

from ..i18n import current_lang, tr as _tr


class Tr(QObject):
    """QML `tr` 上下文属性。"""

    changed = pyqtSignal()

    @pyqtSlot(str, result=str)
    def t(self, key: str) -> str:
        """按当前语言取翻译；未知 key 返回 `??key??`（与原版 tr() 行为一致）。"""
        return _tr(key)

    @pyqtSlot(str, "QVariantMap", result=str)
    def tf(self, key: str, params) -> str:
        """带占位符：QML 传对象，如 `tr.tf("app.status.loaded", {"n": 3})`。"""
        try:
            return _tr(key, **{str(k): v for k, v in dict(params).items()})
        except Exception:
            return _tr(key)

    @pyqtProperty(str, notify=changed)
    def lang(self) -> str:
        return current_lang()

    def notify_language_changed(self):
        self.changed.emit()
