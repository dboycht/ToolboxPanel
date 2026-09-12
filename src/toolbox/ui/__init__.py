"""UI 层（QML 版，v2.0.1 起）。

- `app.py`      启动与引擎装配
- `window.py`   无边框窗口 + 系统毛玻璃 + 拖动/窗口按钮
- `theme.py`    主题引擎（预置 + 参数细调 + 颜色令牌覆盖）
- `settings.py` config.json 读写
- `bridge.py`   QML ↔ Python 数据桥接（标签页/图标模型 + 操作槽）
- `tr.py`       把 i18n 暴露给 QML
"""
