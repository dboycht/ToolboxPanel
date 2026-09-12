"""主题引擎：预置主题 + 高度自定义参数。

设计目标（用户要求「主题高度自定义」）：
1. **继承原版**：令牌语义与原 `themes.py` 的 LIGHT/DARK 保持一致（window/base/text/
   accent/border…），这样旧认知与配色习惯不丢；
2. **预置 + 细调**：`preset` 选预置（dark/light/…），`params` 覆盖可调参数，
   `overrides` 逐令牌覆盖颜色 —— 三层叠加，后层覆盖前层；
3. **对 QML 友好**：全部令牌挂在一个 `tokens` 对象上，并提供 `version` 计数器；
   QML 侧写 `theme.c.accent`，主题一变只需 `version++`，Qt 会自动重算所有绑定
   （不需要在 QML 里手写刷新逻辑 —— 这也是 QML 相对 QSS 的核心优势）。

⚠️ QML 访问注意（踩过，见 ERROR.md E6 同类问题）：
QML **不能** 对 Python 对象做 `theme.tokens[key]` 动态取值并保持绑定，
必须暴露成**具名属性**（本文件用 `_TokenView` 逐项 property）。
"""
from __future__ import annotations

from typing import Any

from PyQt6.QtCore import QObject, pyqtProperty, pyqtSignal, pyqtSlot

from .theme_base import TokenViewBase

# ── 可调参数（数字类，带范围与默认）────────────────────────────────────
# key: (默认值, 最小值, 最大值, 步长, 中文名, 英文名)
PARAM_SPECS: dict[str, tuple] = {
    "window_opacity":   (0.82, 0.20, 1.00, 0.02, "窗口不透明度", "Window opacity"),
    "card_opacity":     (0.16, 0.00, 0.60, 0.02, "卡片不透明度", "Card opacity"),
    "card_hover_opacity": (0.26, 0.00, 0.80, 0.02, "卡片悬停", "Card hover"),
    "blur_radius":      (26.0, 0.0, 64.0, 1.0, "模糊强度", "Blur radius"),
    "radius":           (16.0, 0.0, 32.0, 1.0, "圆角", "Corner radius"),
    "anim_ms":          (260.0, 0.0, 800.0, 10.0, "动效时长(ms)", "Animation (ms)"),
    "hover_ms":         (160.0, 0.0, 600.0, 10.0, "悬停时长(ms)", "Hover (ms)"),
}

# ── 预置主题：颜色令牌 ────────────────────────────────────────────────
# 令牌语义继承原版 themes.py（window/base/alt_base/text/border/accent/highlight…）
DARK: dict[str, str] = {
    "window":        "#14161f",
    "base":          "#1b1e29",
    "alt_base":      "#222634",
    "text":          "#f2f4f8",
    "text_dim":      "#9aa3b2",
    "text_hint":     "#6b7385",
    "border":        "#33ffffff",
    "border_soft":   "#1fffffff",
    "accent":        "#4c8bf5",
    "accent_hover":  "#6ba1ff",
    "accent_pressed": "#3a72d4",
    "on_accent":     "#0d0f16",
    "highlight":     "#4c8bf5",
    "highlighted_text": "#0d0f16",
    "card":          "#ffffff",
    "card_hover":    "#ffffff",
    "glow_1":        "#3a4c8bf5",
    "glow_2":        "#3a7c6bf0",
    "danger":        "#e04b4b",
    "success":       "#57c08a",
    "warning":       "#f2b24c",
}

LIGHT: dict[str, str] = {
    "window":        "#eef0f5",
    "base":          "#ffffff",
    "alt_base":      "#f5f7fa",
    "text":          "#1b1e29",
    "text_dim":      "#5a6274",
    "text_hint":     "#8b93a5",
    "border":        "#33000000",
    "border_soft":   "#1a000000",
    "accent":        "#2f6fe4",
    "accent_hover":  "#4a86f0",
    "accent_pressed": "#1f56bd",
    "on_accent":     "#ffffff",
    "highlight":     "#2f6fe4",
    "highlighted_text": "#ffffff",
    "card":          "#000000",
    "card_hover":    "#000000",
    "glow_1":        "#334c8bf5",
    "glow_2":        "#337c6bf0",
    "danger":        "#d63c3c",
    "success":       "#3a9e6c",
    "warning":       "#cf8f2a",
}

# 预置 = 颜色表 + 参数默认值（参数未列出则用 PARAM_SPECS 默认）
PRESETS: dict[str, dict] = {
    "dark": {
        "zh": "深色", "en": "Dark",
        "colors": DARK,
        "params": {"window_opacity": 0.82, "card_opacity": 0.16,
                   "card_hover_opacity": 0.26, "blur_radius": 26.0,
                   "radius": 16.0, "anim_ms": 260.0, "hover_ms": 160.0},
    },
    "light": {
        "zh": "浅色", "en": "Light",
        "colors": LIGHT,
        "params": {"window_opacity": 0.88, "card_opacity": 0.10,
                   "card_hover_opacity": 0.18, "blur_radius": 26.0,
                   "radius": 16.0, "anim_ms": 260.0, "hover_ms": 160.0},
    },
    "midnight": {
        "zh": "午夜蓝", "en": "Midnight",
        "colors": {**DARK,
                   "window": "#0b1020", "base": "#111830", "alt_base": "#16203c",
                   "accent": "#5b8def", "accent_hover": "#7aa5f5",
                   "glow_1": "#3a5b8def", "glow_2": "#3a3f6fd8"},
        "params": {"window_opacity": 0.80, "card_opacity": 0.18,
                   "card_hover_opacity": 0.28, "blur_radius": 32.0,
                   "radius": 18.0, "anim_ms": 300.0, "hover_ms": 180.0},
    },
    "grape": {
        "zh": "葡萄紫", "en": "Grape",
        "colors": {**DARK,
                   "window": "#150f22", "base": "#1d142e", "alt_base": "#251a3a",
                   "accent": "#a06bf0", "accent_hover": "#b78bf7",
                   "accent_pressed": "#8450d6",
                   "glow_1": "#3aa06bf0", "glow_2": "#3ae45c8a"},
        "params": {"window_opacity": 0.82, "card_opacity": 0.18,
                   "card_hover_opacity": 0.28, "blur_radius": 30.0,
                   "radius": 20.0, "anim_ms": 280.0, "hover_ms": 170.0},
    },
    "matcha": {
        "zh": "抹茶绿", "en": "Matcha",
        "colors": {**DARK,
                   "window": "#0f1a14", "base": "#142219", "alt_base": "#1b2c21",
                   "accent": "#4fb07a", "accent_hover": "#6bc794",
                   "accent_pressed": "#3d8f61",
                   "glow_1": "#3a4fb07a", "glow_2": "#3ac9a24b"},
        "params": {"window_opacity": 0.84, "card_opacity": 0.16,
                   "card_hover_opacity": 0.26, "blur_radius": 26.0,
                   "radius": 16.0, "anim_ms": 260.0, "hover_ms": 160.0},
    },
}

DEFAULT_PRESET = "dark"


def preset_names() -> list[str]:
    return list(PRESETS.keys())


def preset_label(name: str, lang: str = "zh") -> str:
    p = PRESETS.get(name) or PRESETS[DEFAULT_PRESET]
    return p.get(lang) or p.get("en") or name


class _TokenViewBase(TokenViewBase):
    """向后兼容别名：基类已抽到 `theme_base.py`。

    为什么抽出：本文件需要 `theme_props.py` 的视图类，
    而视图类需要基类 —— 基类留在本文件会形成循环导入。
    """


# ── 键集合（由预置推导，保证完备）──────────────────────────────────
_COLOR_KEYS = sorted(set(DARK.keys()) | set(LIGHT.keys()))
_NUM_KEYS = sorted(PARAM_SPECS.keys())

# 静态生成的视图类（见 theme_props.py，勿手改）
from .theme_props import ColorView, NumberView  # noqa: E402


class Theme(QObject):
    """主题对象：QML 的 `theme` 上下文属性。

    组成（后层覆盖前层）：
      预置颜色/参数  →  params（可调参数）  →  overrides（逐令牌颜色覆盖）
    """

    changed = pyqtSignal()
    versionChanged = pyqtSignal()

    def __init__(self, settings=None, parent=None):
        super().__init__(parent)
        self._settings = settings
        self._preset = DEFAULT_PRESET
        self._params: dict[str, float] = {}
        self._overrides: dict[str, Any] = {}
        self._colors: dict[str, str] = {}

        self._c = ColorView({}, self)    # 颜色令牌
        self._n = NumberView({}, self)   # 数字参数
        self._c.changed.connect(self._on_view_changed)

        self._version = 0
        self._resolve()
        self._push()

    # ── 属性（QML 可读）────────────────────────────────────────────

    # ── QML 视图对象 ────────────────────────────────────────────────
    # ⚠️ 关键（实测）：**必须是 pyqtProperty**！
    # 在 __init__ 里 `self.c = ColorView(...)` 只是普通 Python 属性，
    # QML 读 `theme.c` 会得到 `undefined` —— QML 只能看见 QObject 的
    # pyqtProperty / 槽 / 信号，看不见裸 Python 属性。这是本文件最大的坑。

    @pyqtProperty(QObject, constant=True)
    def c(self):
        return self._c

    @pyqtProperty(QObject, constant=True)
    def n(self):
        return self._n

    # ── 属性（QML 可读）────────────────────────────────────────────

    @pyqtProperty(int, notify=versionChanged)
    def version(self) -> int:
        """版本号：每次主题变化自增，用于强制 QML 重算绑定。"""
        return self._version

    @pyqtProperty(str, notify=changed)
    def preset(self) -> str:
        return self._preset

    @pyqtProperty("QVariantList", notify=changed)
    def presetNames(self) -> list:
        return preset_names()

    @pyqtProperty("QVariantList", notify=changed)
    def presetLabels(self) -> list:
        """按当前语言返回预置主题名列表（与 presetNames 顺序一一对应）。"""
        lang = "zh"
        if self._settings is not None:
            lang = self._settings.get("language", "zh")
        return [preset_label(n, lang) for n in preset_names()]

    @pyqtProperty("QVariantList", notify=changed)
    def paramSpecs(self) -> list:
        """参数元数据供设置面板自动生成控件：[{key,label,min,max,step,value}]"""
        lang = "zh"
        if self._settings is not None:
            lang = self._settings.get("language", "zh")
        out = []
        for key, (default, lo, hi, step, zh, en) in PARAM_SPECS.items():
            out.append({
                "key": key,
                "label": zh if lang == "zh" else en,
                "min": lo, "max": hi, "step": step,
                "value": float(self._params.get(key, default)),
            })
        return out

    # ── 内部：解析与下发 ────────────────────────────────────────────

    def _resolve(self):
        """按「预置 → 参数覆盖 → 令牌覆盖」顺序算出最终值。"""
        preset = PRESETS.get(self._preset) or PRESETS[DEFAULT_PRESET]
        colors = dict(preset["colors"])
        params = {k: float(v[0]) for k, v in PARAM_SPECS.items()}
        params.update({k: float(v) for k, v in preset.get("params", {}).items()})
        params.update({k: float(v) for k, v in self._params.items() if k in PARAM_SPECS})
        # 颜色令牌覆盖（只接受颜色键，忽略未知键）
        for k, v in self._overrides.items():
            if k in _COLOR_KEYS and isinstance(v, str):
                colors[k] = v
        self._colors = colors
        self._resolved_params = params

    def _push(self):
        self._c.set_values(self._colors)
        self._n.set_values(self._resolved_params)

    def _on_view_changed(self):
        # 视图自身变化（目前仅内部回写）→ 广播
        self.changed.emit()

    def _bump(self):
        self._version += 1
        self.versionChanged.emit()
        self.changed.emit()

    # ── 对外操作（QML 调用 / 程序调用）──────────────────────────────

    @pyqtSlot(str)
    def setPreset(self, name: str):
        if name not in PRESETS or name == self._preset:
            return
        self._preset = name
        # 切预置时清掉逐令牌覆盖，避免旧覆盖把新预置改得面目全非
        self._overrides = {}
        if self._settings is not None:
            self._settings.update({"theme": name, "theme_overrides": {}})
        self._resolve()
        self._push()
        self._bump()

    @pyqtSlot(str, float)
    def setParam(self, key: str, value: float):
        if key not in PARAM_SPECS:
            return
        default, lo, hi, step, _zh, _en = PARAM_SPECS[key]
        value = max(lo, min(hi, float(value)))
        self._params[key] = value
        if self._settings is not None:
            ov = dict(self._settings.overrides())
            ov[f"param:{key}"] = value
            self._settings.set("theme_overrides", ov)
        self._resolve()
        self._push()
        self._bump()

    @pyqtSlot(str, str)
    def setColor(self, token: str, color: str):
        """逐令牌覆盖颜色（高级自定义）。"""
        if token not in _COLOR_KEYS:
            return
        if not color or not str(color).strip():
            return
        self._overrides[token] = str(color)
        if self._settings is not None:
            ov = dict(self._settings.overrides())
            ov[token] = str(color)
            self._settings.set("theme_overrides", ov)
        self._resolve()
        self._push()
        self._bump()

    @pyqtSlot()
    def resetOverrides(self):
        self._overrides = {}
        self._params = {}
        if self._settings is not None:
            self._settings.update({"theme_overrides": {}})
        self._resolve()
        self._push()
        self._bump()

    @pyqtSlot(str)
    def resetParam(self, key: str):
        self._params.pop(key, None)
        if self._settings is not None:
            ov = dict(self._settings.overrides())
            ov.pop(f"param:{key}", None)
            self._settings.set("theme_overrides", ov)
        self._resolve()
        self._push()
        self._bump()

    def reload(self):
        self._resolve()
        self._push()
        self._bump()

    # ── 从设置载入 ─────────────────────────────────────────────────

    def load_from_settings(self):
        """启动时从 config.json 恢复主题选择与细调。"""
        if self._settings is None:
            return
        name = self._settings.get("theme", DEFAULT_PRESET)
        self._preset = name if name in PRESETS else DEFAULT_PRESET
        ov = self._settings.overrides()
        self._overrides = {k: v for k, v in ov.items()
                           if k in _COLOR_KEYS and isinstance(v, str)}
        self._params = {}
        for k, v in ov.items():
            if k.startswith("param:"):
                key = k.split(":", 1)[1]
                if key in PARAM_SPECS:
                    try:
                        self._params[key] = float(v)
                    except (TypeError, ValueError):
                        pass
        self._resolve()
        self._push()
        self._bump()

    # ── 语言切换时刷新标签 ──────────────────────────────────────────

    def refresh_labels(self):
        self.changed.emit()
