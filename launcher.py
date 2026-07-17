"""PyInstaller 入口 — 不对 sys.path 做动态修改以保持依赖分析正确。

开发时仍用 run.py；打包时用此文件作为 PyInstaller 入口。
"""
from src.toolbox.main import main

if __name__ == "__main__":
    main()
