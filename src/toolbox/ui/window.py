"""窗口控制器：无边框 + 系统毛玻璃 + 拖动 / 最小化 / 最大化 / 关闭。

做法全部来自 `demo-v2` 的真机验证结论（见 ERROR.md E3），要点：
1. **acrylic 用 ctypes 调 DWM**：Win11 22621+ 走 `DWMWA_SYSTEMBACKDROP_TYPE`，
   Win10/早期 Win11 回退未文档化的 `SetWindowCompositionAttribute`；
2. **务必保留 `WS_CAPTION|WS_THICKFRAME`**（去掉只会让窗口拖不动）；
3. **拖动用 `ReleaseCapture` + `WM_SYSCOMMAND(SC_MOVE, 2)`**：
   `WM_NCLBUTTONDOWN(HTCAPTION)` 对无边框窗口无效；`WM_NCHITTEST` 在 Flutter
   场景下压根不被调用（QML 不同，但 SC_MOVE 同样可用且更省事）；
4. **拖动区不能盖住窗口按钮** —— 拖动会进入系统模态循环，
   按下即阻塞 QML 的 pointerUp，按钮 onTapped 永不触发（E3 的核心坑）。

对 QML 暴露为上下文属性 `win`。
"""
from __future__ import annotations

import ctypes
import sys
from ctypes import wintypes

from PyQt6.QtCore import QObject, pyqtProperty, pyqtSignal, pyqtSlot

_IS_WINDOWS = sys.platform == "win32"

# ── DWM / user32 常量 ─────────────────────────────────────────────────
_DWMWA_USE_IMMERSIVE_DARK_MODE = 20
_DWMWA_SYSTEMBACKDROP_TYPE = 38
_DWMWA_NCRENDERING_POLICY = 2
_DWMNCRP_DISABLED = 1
_DWMSBT_MAINWINDOW = 2        # Mica
_DWMSBT_TRANSIENTWINDOW = 3   # Acrylic

_WM_SYSCOMMAND = 0x0112
_SC_MOVE = 0xF010
_SC_MINIMIZE = 0xF020
_SC_MAXIMIZE = 0xF030
_SC_RESTORE = 0xF120
_SC_CLOSE = 0xF060

# Win10 未文档化结构（对应 ACCENT_POLICY）
_ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
_WCA_ACCENT_POLICY = 19


class _AccentPolicy(ctypes.Structure):
    _fields_ = [("accent_state", ctypes.c_int), ("accent_flags", ctypes.c_int),
                ("gradient_color", wintypes.DWORD), ("animation_id", ctypes.c_int)]


class _WinCompositionAttrData(ctypes.Structure):
    _fields_ = [("attribute", ctypes.c_int), ("data", ctypes.c_void_p),
                ("size_of_data", ctypes.c_size_t)]


def _dwm_set(hwnd: int, attr: int, value: int) -> bool:
    try:
        dwmapi = ctypes.WinDLL("dwmapi")
        dwmapi.DwmSetWindowAttribute.argtypes = [
            wintypes.HWND, ctypes.c_int, ctypes.c_void_p, ctypes.c_int]
        dwmapi.DwmSetWindowAttribute.restype = ctypes.c_long
        v = ctypes.c_int(value)
        hr = dwmapi.DwmSetWindowAttribute(wintypes.HWND(hwnd), attr,
                                          ctypes.byref(v), ctypes.sizeof(v))
        return hr == 0
    except Exception:
        return False


def _apply_accent_policy(hwnd: int, tint: int = 0x99201B15) -> bool:
    """Win10 回退：SetWindowCompositionAttribute（未文档化但广泛使用）。"""
    try:
        user32 = ctypes.WinDLL("user32")
        set_wca = user32.SetWindowCompositionAttribute
        set_wca.argtypes = [wintypes.HWND, ctypes.c_void_p]
        set_wca.restype = wintypes.BOOL

        policy = _AccentPolicy()
        policy.accent_state = _ACCENT_ENABLE_ACRYLICBLURBEHIND
        policy.accent_flags = 2
        policy.gradient_color = tint          # AABBGGRR
        data = _WinCompositionAttrData()
        data.attribute = _WCA_ACCENT_POLICY
        data.data = ctypes.cast(ctypes.pointer(policy), ctypes.c_void_p)
        data.size_of_data = ctypes.sizeof(policy)
        return bool(set_wca(wintypes.HWND(hwnd), ctypes.byref(data)))
    except Exception:
        return False


def _send(hwnd: int, msg: int, wparam: int, lparam: int) -> int:
    try:
        user32 = ctypes.WinDLL("user32")
        user32.SendMessageW.argtypes = [
            wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
        user32.SendMessageW.restype = ctypes.c_ssize_t
        return int(user32.SendMessageW(wintypes.HWND(hwnd), msg, wparam, lparam))
    except Exception:
        return 0


class WindowController(QObject):
    """QML `win` 上下文属性。需在窗口显示后调用 `attach(window)`。"""

    backdropChanged = pyqtSignal()
    maximizedChanged = pyqtSignal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self._window = None
        self._hwnd = 0
        self._backdrop_active = False
        self._backdrop_mode = "acrylic"
        self._maximized = False

    # ── 挂载 ───────────────────────────────────────────────────────

    def attach(self, window):
        """绑定 QQuickWindow 并立即应用毛玻璃与深色边框。"""
        self._window = window
        try:
            self._hwnd = int(window.winId())   # winId() 会强制创建原生窗口
        except Exception:
            self._hwnd = 0
        self.apply_backdrop()

    @pyqtProperty(bool, notify=backdropChanged)
    def backdropActive(self) -> bool:
        """毛玻璃是否真的生效（未生效时 QML 应回退为不透明背景）。"""
        return self._backdrop_active

    @pyqtProperty(str, notify=backdropChanged)
    def backdropMode(self) -> str:
        return self._backdrop_mode

    @pyqtProperty(bool, notify=maximizedChanged)
    def maximized(self) -> bool:
        return self._maximized

    @pyqtProperty(bool, constant=True)
    def isWindows(self) -> bool:
        return _IS_WINDOWS

    # ── 毛玻璃 ─────────────────────────────────────────────────────

    @pyqtSlot()
    @pyqtSlot(str)
    def apply_backdrop(self, mode: str = "acrylic"):
        """应用系统 backdrop。返回是否成功；失败时 QML 用自绘半透明兜底。"""
        self._backdrop_mode = mode
        if not _IS_WINDOWS or not self._hwnd:
            self._backdrop_active = False
            self.backdropChanged.emit()
            return False

        # 深色非客户区（与深色主题一致）
        _dwm_set(self._hwnd, _DWMWA_USE_IMMERSIVE_DARK_MODE, 1)
        # 不让 DWM 画边框（无边框观感），但保留 WS_CAPTION 以保住拖动能力
        _dwm_set(self._hwnd, _DWMWA_NCRENDERING_POLICY, _DWMNCRP_DISABLED)

        want = _DWMSBT_TRANSIENTWINDOW if mode == "acrylic" else _DWMSBT_MAINWINDOW
        ok = _dwm_set(self._hwnd, _DWMWA_SYSTEMBACKDROP_TYPE, want)
        if not ok:
            # Win11 22621 以下 / 调用失败 → 老 API
            ok = _apply_accent_policy(self._hwnd)
        self._backdrop_active = bool(ok)
        self.backdropChanged.emit()
        return self._backdrop_active

    # ── 窗口操作 ───────────────────────────────────────────────────

    def _sync_maximized(self):
        try:
            if self._window is not None:
                from PyQt6.QtCore import Qt
                m = bool(self._window.windowStates() & Qt.WindowState.WindowMaximized)
                if m != self._maximized:
                    self._maximized = m
                    self.maximizedChanged.emit()
        except Exception:
            pass

    @pyqtSlot()
    def startDrag(self):
        """开始系统拖动。QML 在标题拖动区 onPressed 时调用。"""
        if not _IS_WINDOWS or not self._hwnd:
            return
        try:
            ctypes.WinDLL("user32").ReleaseCapture()
        except Exception:
            pass
        _send(self._hwnd, _WM_SYSCOMMAND, _SC_MOVE, 2)

    @pyqtSlot()
    def minimize(self):
        if _IS_WINDOWS and self._hwnd:
            _send(self._hwnd, _WM_SYSCOMMAND, _SC_MINIMIZE, 0)

    @pyqtSlot()
    def toggleMaximize(self):
        if not (_IS_WINDOWS and self._hwnd):
            return
        self._sync_maximized()
        _send(self._hwnd, _WM_SYSCOMMAND,
              _SC_RESTORE if self._maximized else _SC_MAXIMIZE, 0)
        self._sync_maximized()

    @pyqtSlot()
    def close(self):
        if _IS_WINDOWS and self._hwnd:
            _send(self._hwnd, _WM_SYSCOMMAND, _SC_CLOSE, 0)
        elif self._window is not None:
            self._window.close()
