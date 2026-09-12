"""主题令牌的 QML 视图类（**静态生成**，勿手改）。

为什么是静态类而不是 `type()` 动态构造：
PyQt6 要求 property 的 `notify` 信号**定义在声明该 property 的类里**，
且必须是真实 pyqtSignal 对象；用 `type()` 在类体之后建类时，
闭包里拿不到该信号，会报
`the notify signal 'changed()' was not defined in this class`。
静态类把信号与 property 写在同一个类体中，PyQt6 元类能正确识别。

令牌键集合来自 `theme.py` 的 `DARK/LIGHT` 与 `PARAM_SPECS`；
新增令牌时需重新生成本文件（生成方式见 DEVELOPMENT.md §0.4）。
"""
from PyQt6.QtCore import pyqtProperty, pyqtSignal

from .theme_base import TokenViewBase


class ColorView(TokenViewBase):
    """颜色令牌视图。

    QML 用法：`theme.c.accent` / `theme.c.text` / `theme.c.border` …
    """

    changed = pyqtSignal()

    @pyqtProperty(str, notify=changed)
    def accent(self):
        return self._get("accent")

    @pyqtProperty(str, notify=changed)
    def accent_hover(self):
        return self._get("accent_hover")

    @pyqtProperty(str, notify=changed)
    def accent_pressed(self):
        return self._get("accent_pressed")

    @pyqtProperty(str, notify=changed)
    def alt_base(self):
        return self._get("alt_base")

    @pyqtProperty(str, notify=changed)
    def base(self):
        return self._get("base")

    @pyqtProperty(str, notify=changed)
    def border(self):
        return self._get("border")

    @pyqtProperty(str, notify=changed)
    def border_soft(self):
        return self._get("border_soft")

    @pyqtProperty(str, notify=changed)
    def card(self):
        return self._get("card")

    @pyqtProperty(str, notify=changed)
    def card_hover(self):
        return self._get("card_hover")

    @pyqtProperty(str, notify=changed)
    def danger(self):
        return self._get("danger")

    @pyqtProperty(str, notify=changed)
    def glow_1(self):
        return self._get("glow_1")

    @pyqtProperty(str, notify=changed)
    def glow_2(self):
        return self._get("glow_2")

    @pyqtProperty(str, notify=changed)
    def highlight(self):
        return self._get("highlight")

    @pyqtProperty(str, notify=changed)
    def highlighted_text(self):
        return self._get("highlighted_text")

    @pyqtProperty(str, notify=changed)
    def on_accent(self):
        return self._get("on_accent")

    @pyqtProperty(str, notify=changed)
    def success(self):
        return self._get("success")

    @pyqtProperty(str, notify=changed)
    def text(self):
        return self._get("text")

    @pyqtProperty(str, notify=changed)
    def text_dim(self):
        return self._get("text_dim")

    @pyqtProperty(str, notify=changed)
    def text_hint(self):
        return self._get("text_hint")

    @pyqtProperty(str, notify=changed)
    def warning(self):
        return self._get("warning")

    @pyqtProperty(str, notify=changed)
    def window(self):
        return self._get("window")


class NumberView(TokenViewBase):
    """数字参数视图（本文件由脚本生成，勿手改）。"""

    changed = pyqtSignal()

    @pyqtProperty(float, notify=changed)
    def anim_ms(self):
        return float(self._get("anim_ms") or 0.0)

    @pyqtProperty(float, notify=changed)
    def blur_radius(self):
        return float(self._get("blur_radius") or 0.0)

    @pyqtProperty(float, notify=changed)
    def card_hover_opacity(self):
        return float(self._get("card_hover_opacity") or 0.0)

    @pyqtProperty(float, notify=changed)
    def card_opacity(self):
        return float(self._get("card_opacity") or 0.0)

    @pyqtProperty(float, notify=changed)
    def hover_ms(self):
        return float(self._get("hover_ms") or 0.0)

    @pyqtProperty(float, notify=changed)
    def radius(self):
        return float(self._get("radius") or 0.0)

    @pyqtProperty(float, notify=changed)
    def window_opacity(self):
        return float(self._get("window_opacity") or 0.0)
