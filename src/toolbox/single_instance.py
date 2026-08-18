"""单实例守护 — 确保同时只运行一个实例，新实例拉取已有实例窗口后退出。

原理：
- `QSharedMemory` 作为跨进程互斥（进程正常退出或崩溃时自动释放，无残留锁）。
- 主实例在窗口显示后把主窗口 HWND 写入共享内存。
- 新实例启动时 `attach()` 成功 → 读出 HWND → 用 Windows API
  （ShowWindow + SetForegroundWindow）把已有实例的窗口带到前台 → 退出。

背景：数据文件 data/tabs.json 是单文件即时保存，多实例并发保存会互相覆盖
（A 实例保存 B 状态、B 实例又保存 C 状态）。因此必须单实例运行。
"""
import ctypes
import struct

from PyQt6.QtCore import QSharedMemory
from PyQt6.QtWidgets import QWidget

# 共享内存 key：全局唯一（含项目名），勿与其他应用冲突
_SHARED_KEY = "ToolboxPanel_SingleInstance_v1"
# HWND 大小（64 位 Windows 为 8 字节）
_HWND_SIZE = struct.calcsize("q")


class SingleInstanceGuard:
    """单实例守卫。

    用法：
        guard = SingleInstanceGuard()
        if not guard.acquire():
            guard.activate_existing()
            sys.exit(0)
        ...
        window.show()
        guard.register_window(window)
    """

    def __init__(self, key: str = _SHARED_KEY):
        self._key = key
        self._shared = QSharedMemory(key)
        self._is_primary = False

    def acquire(self) -> bool:
        """尝试成为主实例。返回 True = 本进程是唯一实例（主实例）。"""
        if self._shared.attach():
            # 已有主实例在运行
            self._is_primary = False
            return False
        if self._shared.create(_HWND_SIZE):
            self._is_primary = True
            return True
        # create 失败：竞态（另一个进程刚创建）或系统资源不足 → 视为已存在
        self._is_primary = False
        return False

    def register_window(self, window: QWidget):
        """主实例把主窗口 HWND 写入共享内存（窗口显示后调用）。"""
        if not self._is_primary:
            return
        hwnd = int(window.winId())  # winId() 强制创建原生窗口
        self._shared.lock()
        try:
            # PyQt6 的 QSharedMemory 未暴露 write()/clear()，
            # 通过 data() 返回的内存指针直接写入（create() 时内存已清零）
            ctypes.memmove(ctypes.c_void_p(int(self._shared.data())),
                           struct.pack("q", hwnd), _HWND_SIZE)
        finally:
            self._shared.unlock()

    def activate_existing(self) -> bool:
        """新实例：把已有实例主窗口带到前台（最小化时恢复）。"""
        if self._is_primary:
            return True
        hwnd = self._read_hwnd()
        if not hwnd:
            return False
        try:
            import win32con
            import win32gui
            if win32gui.IsIconic(hwnd):
                win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
            win32gui.SetForegroundWindow(hwnd)
            return True
        except Exception:
            return False

    # ── 内部 ──

    def _read_hwnd(self) -> int:
        if not self._shared.isAttached() and not self._shared.attach():
            return 0
        self._shared.lock()
        try:
            ptr = self._shared.data()
            if ptr is None:
                return 0
            raw = bytes(ptr.asstring(_HWND_SIZE))
        finally:
            self._shared.unlock()
        if not raw or len(raw) < _HWND_SIZE:
            return 0
        return struct.unpack("q", raw)[0]
