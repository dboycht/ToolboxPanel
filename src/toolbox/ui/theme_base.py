"""主题令牌视图的基类（单独成模块以避免循环导入）。

`theme.py` 定义令牌与解析逻辑，`theme_props.py` 声明 QML 可见的具名 property。
两者都需要这个基类，若把它放在 `theme.py` 里就会形成
`theme → theme_props → theme` 的循环导入，故独立出来。
"""
from __future__ import annotations

from typing import Any

from PyQt6.QtCore import QObject


class TokenViewBase(QObject):
    """存放令牌键值表，提供读取 / 回写 / 变更广播。

    子类（见 `theme_props.py`）负责声明具名 `pyqtProperty`；
    `changed` 信号由子类声明，因为 PyQt6 要求 notify 信号
    必须定义在声明该 property 的那个类里。
    """

    def __init__(self, values: dict[str, Any], parent=None):
        super().__init__(parent)
        self._v: dict[str, Any] = dict(values)

    def set_values(self, values: dict[str, Any]):
        self._v = dict(values)
        self.changed.emit()   # type: ignore[attr-defined]

    def value(self, key: str, default: Any = None) -> Any:
        return self._v.get(key, default)

    def keys(self) -> list[str]:
        return list(self._v.keys())

    def _get(self, key: str):
        return self._v.get(key)

    def _set(self, key: str, val):
        self._v[key] = val
        self.changed.emit()   # type: ignore[attr-defined]
