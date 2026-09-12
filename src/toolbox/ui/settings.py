"""应用设置：读写 data/config.json，并承载「主题高度自定义」的参数。

与原版（v1.11.6）保持一致的部分：
- 文件名、位置不变：`data/config.json`（打包后在 .exe 同级 data/）
- 既有字段语义不变：`language` / `icon_size` / `theme`
- **原子写**（tmp → replace），与原版 DataStore 同样的做法
- 读取失败一律回落默认值，绝不因配置损坏而启动失败

新增（v2.0.1 主题自定义）：
- `theme_tokens`：覆盖主题令牌（如强调色、圆角、模糊强度）
- `theme_overrides` 与 `theme` 分开存：`theme` 是预置主题名，前者是细调
"""
from __future__ import annotations

import json
import os
from pathlib import Path

# ── 既有字段的合法取值（与原版一致）────────────────────────────────────
LANGS = ("zh", "en")
ICON_SIZES = ("small", "medium", "large")

DEFAULTS: dict = {
    "language": "zh",
    "icon_size": "medium",
    "theme": "dark",
    # v2.0.1 新增：主题细调（键为主题令牌名，值为字符串颜色或数字）
    "theme_overrides": {},
}


def get_data_dir() -> Path:
    """获取数据目录（与原版 app_window.get_data_dir 行为一致）。

    - 打包后（sys.frozen）：取 .exe 同级 `data/`
    - 开发时：取项目根 `data/`
    """
    import sys
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent / "data"
    # src/toolbox/ui/settings.py -> 上溯 4 层 = 项目根
    return Path(__file__).resolve().parent.parent.parent.parent / "data"


class Settings:
    """config.json 的读写封装（单例式使用，但保留可多实例以便测试）。"""

    def __init__(self, data_dir: Path | None = None):
        self.data_dir = Path(data_dir) if data_dir else get_data_dir()
        self.path = self.data_dir / "config.json"
        self._data: dict = dict(DEFAULTS)
        self.load()

    # ── 读写 ───────────────────────────────────────────────────────────

    def load(self) -> dict:
        """读配置；任何异常都回落默认值（与原版容错策略一致）。"""
        self._data = dict(DEFAULTS)
        try:
            if self.path.exists():
                raw = json.loads(self.path.read_text(encoding="utf-8"))
                if isinstance(raw, dict):
                    self._data.update(raw)
        except (json.JSONDecodeError, OSError, UnicodeDecodeError):
            # 配置损坏：不备份、不报错，直接用默认值（保持启动可用）
            pass

        # 归一化非法值（与原版 AppWindow._load_* 的校验一致）
        if self._data.get("language") not in LANGS:
            self._data["language"] = DEFAULTS["language"]
        if self._data.get("icon_size") not in ICON_SIZES:
            self._data["icon_size"] = DEFAULTS["icon_size"]
        if not isinstance(self._data.get("theme"), str) or not self._data["theme"]:
            self._data["theme"] = DEFAULTS["theme"]
        if not isinstance(self._data.get("theme_overrides"), dict):
            self._data["theme_overrides"] = {}
        return self._data

    def save(self):
        """原子写：先写 .tmp 再 replace（与原版 DataStore.save 同策略）。"""
        self.data_dir.mkdir(parents=True, exist_ok=True)
        tmp = self.path.with_suffix(".tmp")
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(self._data, f, indent=2, ensure_ascii=False)
        os.replace(tmp, self.path)

    # ── 取/存 ──────────────────────────────────────────────────────────

    def get(self, key: str, default=None):
        return self._data.get(key, DEFAULTS.get(key, default))

    def set(self, key: str, value, autosave: bool = True):
        self._data[key] = value
        if autosave:
            self.save()

    def update(self, values: dict, autosave: bool = True):
        self._data.update(values)
        if autosave:
            self.save()

    # ── 主题细调 ───────────────────────────────────────────────────────

    def overrides(self) -> dict:
        ov = self._data.get("theme_overrides")
        return dict(ov) if isinstance(ov, dict) else {}

    def set_override(self, token: str, value, autosave: bool = True):
        """设置单个令牌覆盖值；value 为 None 表示移除该覆盖。"""
        ov = dict(self._data.get("theme_overrides") or {})
        if value is None:
            ov.pop(token, None)
        else:
            ov[token] = value
        self._data["theme_overrides"] = ov
        if autosave:
            self.save()

    def clear_overrides(self, autosave: bool = True):
        self._data["theme_overrides"] = {}
        if autosave:
            self.save()
