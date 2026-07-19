"""备份管理器 — ZIP 导入导出 + 元数据。"""
import json
import zipfile
import shutil
import secrets
from datetime import datetime
from pathlib import Path

from PyQt6.QtCore import QThread, pyqtSignal


def unique_filename() -> str:
    """生成唯一文件名：时间戳 + 随机码。"""
    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    code = secrets.token_hex(3)
    return f"Toolbox_Backup_{ts}_{code}.zip"


def build_metadata(data_dir: Path) -> dict:
    """从 data/ 目录读取当前状态并生成元数据。"""
    tabs_file = data_dir / "tabs.json"
    tab_count = 0
    icon_count = 0
    if tabs_file.exists():
        try:
            data = json.loads(tabs_file.read_text(encoding="utf-8"))
            tabs = data.get("tabs", [])
            tab_count = len(tabs)
            icon_count = sum(len(t.get("icons", [])) for t in tabs)
        except Exception:
            pass
    return {
        "version": "1.10.3",
        "exported_at": datetime.now().isoformat(),
        "tab_count": tab_count,
        "icon_count": icon_count,
    }


class BackupWorker(QThread):
    """后台线程：执行导出或导入操作。"""

    progress = pyqtSignal(int, int)            # current, total
    log = pyqtSignal(str)                      # 日志消息
    finished = pyqtSignal(bool, str)           # success, message

    def __init__(self, mode: str, data_dir: Path, target_path: str = ""):
        super().__init__()
        self.mode = mode       # "export" or "import"
        self.data_dir = data_dir
        self.target_path = target_path  # export: dest zip path; import: source zip path

    def run(self):
        if self.mode == "export":
            self._do_export()
        else:
            self._do_import()

    def _do_export(self):
        data_dir = self.data_dir
        self.log.emit("正在收集数据文件...")
        files = list(data_dir.rglob("*"))
        files = [f for f in files if f.is_file()]
        total = len(files) + 1  # +1 for metadata

        zip_path = Path(self.target_path)
        self.log.emit(f"创建压缩包: {zip_path.name}")
        try:
            with zipfile.ZipFile(str(zip_path), "w", zipfile.ZIP_DEFLATED) as zf:
                # 1) 元数据
                self.log.emit("写入元数据...")
                meta = build_metadata(data_dir)
                zf.writestr("metadata.json", json.dumps(meta, indent=2, ensure_ascii=False))
                self.progress.emit(1, total)

                # 2) 数据文件
                for i, fp in enumerate(files, start=1):
                    rel = fp.relative_to(data_dir)
                    self.log.emit(f"压缩: {rel}")
                    zf.write(str(fp), str(Path("data") / rel))
                    self.progress.emit(i + 1, total)

            size_kb = zip_path.stat().st_size / 1024
            self.log.emit(f"导出完成 ({size_kb:.1f} KB)")
            self.finished.emit(True, str(zip_path))
        except Exception as e:
            self.finished.emit(False, str(e))

    def _do_import(self):
        zip_path = Path(self.target_path)
        if not zip_path.exists():
            self.finished.emit(False, "文件不存在")
            return

        self.log.emit(f"打开压缩包: {zip_path.name}")
        try:
            with zipfile.ZipFile(str(zip_path), "r") as zf:
                namelist = zf.namelist()
                total = len(namelist)
                # 验证 metadata
                if "metadata.json" not in namelist:
                    self.finished.emit(False, "无效的备份文件：缺少 metadata.json")
                    return

                self.log.emit("读取元数据...")
                meta = json.loads(zf.read("metadata.json").decode("utf-8"))
                self.log.emit(f"  版本: {meta.get('version', '?')}")
                self.log.emit(f"  导出时间: {meta.get('exported_at', '?')}")
                self.log.emit(f"  标签页: {meta.get('tab_count', '?')}  图标: {meta.get('icon_count', '?')}")

                # 清空当前 data/（保留目录结构）
                data_dir = self.data_dir
                self.log.emit("清除当前数据...")
                icons_dir = data_dir / "icons"
                tabs_file = data_dir / "tabs.json"
                config_file = data_dir / "config.json"
                if icons_dir.exists():
                    for f in icons_dir.iterdir():
                        f.unlink()
                for f in (tabs_file, config_file):
                    if f.exists():
                        f.unlink()

                # 解压（跳过 metadata.json）
                for i, name in enumerate(namelist):
                    if name == "metadata.json":
                        self.progress.emit(i + 1, total)
                        continue
                    # name 格式: "data/tabs.json" 等
                    rel = Path(name).relative_to("data") if name.startswith("data/") else Path(name)
                    dest = data_dir / rel
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    self.log.emit(f"解压: {rel}")
                    with zf.open(name) as src, open(str(dest), "wb") as dst:
                        dst.write(src.read())
                    self.progress.emit(i + 1, total)

            self.log.emit("导入完成")
            self.finished.emit(True, str(zip_path))
        except Exception as e:
            self.finished.emit(False, str(e))
