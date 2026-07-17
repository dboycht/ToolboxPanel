"""打包脚本 — 编译为独立文件夹。

用法:
    python build.py          # 编译到 dist/Toolbox/
    python build.py clean    # 清理
"""
import sys, os, shutil, subprocess
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent


def clean():
    for p in (PROJECT_ROOT / "dist", PROJECT_ROOT / "build"):
        if p.exists():
            shutil.rmtree(p)
            print(f"  已删除: {p}")
    for s in PROJECT_ROOT.glob("*.spec"):
        s.unlink()
        print(f"  已删除: {s}")
    print("清理完成。")


def build():
    print("=" * 50)
    print("  工具箱 v1.00.2 编译")
    print("=" * 50)

    cmd = [
        sys.executable, "-m", "PyInstaller",
        "--name", "Toolbox",
        "--onedir",
        "--windowed",
        "--clean",
        "--noconfirm",

        # 入口 — 使用 launcher.py（直接 import，不修改 sys.path）
        str(PROJECT_ROOT / "launcher.py"),

        # PyQt6 全部收集（含 qt plugins、dlls）
        "--collect-all", "PyQt6",

        # pywin32 .lnk 解析
        "--collect-all", "pywin32",

        # 标准库守护 — 显式列出 PyInstaller 偶尔遗漏的模块
        "--hidden-import", "json",
        "--hidden-import", "json.encoder",
        "--hidden-import", "json.decoder",
        "--hidden-import", "json.scanner",
        "--hidden-import", "encodings",
        "--hidden-import", "encodings.utf_8",
        "--hidden-import", "encodings.gbk",
        "--hidden-import", "os",
        "--hidden-import", "os.path",
        "--hidden-import", "sys",
        "--hidden-import", "re",
        "--hidden-import", "time",
        "--hidden-import", "datetime",
        "--hidden-import", "collections",
        "--hidden-import", "collections.abc",
        "--hidden-import", "functools",
        "--hidden-import", "itertools",
        "--hidden-import", "pathlib",
        "--hidden-import", "shutil",
        "--hidden-import", "subprocess",
        "--hidden-import", "webbrowser",
        "--hidden-import", "uuid",
        "--hidden-import", "urllib.parse",
        "--hidden-import", "copy",
        "--hidden-import", "traceback",
        "--hidden-import", "dataclasses",
        "--hidden-import", "enum",
        "--hidden-import", "typing",
        "--hidden-import", "textwrap",
        "--hidden-import", "pythoncom",
        "--hidden-import", "win32com.client",
    ]

    print(f"  正在编译 ...")
    result = subprocess.run(cmd, cwd=str(PROJECT_ROOT))
    if result.returncode != 0:
        print("\n编译失败！")
        sys.exit(1)

    exe = PROJECT_ROOT / "dist" / "Toolbox" / "Toolbox.exe"
    if exe.exists():
        total = sum(
            f.stat().st_size
            for f in (PROJECT_ROOT / "dist" / "Toolbox").rglob("*")
            if f.is_file()
        ) / (1024 * 1024)
        print(f"\n  编译成功！")
        print(f"  📁 {PROJECT_ROOT / 'dist' / 'Toolbox'}")
        print(f"  📦 文件夹大小: {total:.1f} MB")
    else:
        print("未找到 .exe，请检查日志。")
        sys.exit(1)


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "clean":
        clean()
    else:
        clean()
        build()
