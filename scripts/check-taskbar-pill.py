"""任务栏贴片窗口探针（纯 ctypes、只读）：数「同一个任务栏上有几个 VigilWatt 贴片窗口」。

用来验证 `TaskbarPillWindow` 的窗口唯一性——显示配置切换后如果出现 2 个，就是
runtime-pitfalls.md §16.12 说的"贴片多一个并重叠"。不做任何写入、不注入输入。

用法：
    python check-taskbar-pill.py            # 打印显示器/任务栏/贴片窗口
退出码：贴片窗口 > 1 时返回 1（贴片关掉时 0 个不算错）。
"""
import ctypes
import sys
from ctypes import wintypes

u32 = ctypes.WinDLL("user32", use_last_error=True)

EnumProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
u32.EnumWindows.argtypes = [EnumProc, wintypes.LPARAM]
u32.EnumChildWindows.argtypes = [wintypes.HWND, EnumProc, wintypes.LPARAM]
u32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
u32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
u32.IsWindowVisible.argtypes = [wintypes.HWND]
u32.GetWindow.argtypes = [wintypes.HWND, ctypes.c_uint]
u32.GetWindow.restype = wintypes.HWND
u32.GetParent.argtypes = [wintypes.HWND]
u32.GetParent.restype = wintypes.HWND
u32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
u32.GetWindowThreadProcessId.restype = wintypes.DWORD
u32.GetDpiForWindow.argtypes = [wintypes.HWND]
u32.GetDpiForWindow.restype = ctypes.c_uint
u32.GetMonitorInfoW.argtypes = [wintypes.HMONITOR, ctypes.c_void_p]

PILL_CLASS = "VigilWattTaskbarPill"
TASKBAR_CLASSES = ("Shell_TrayWnd", "Shell_SecondaryTrayWnd")


class MONITORINFOEXW(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.DWORD), ("rcMonitor", wintypes.RECT), ("rcWork", wintypes.RECT),
                ("dwFlags", wintypes.DWORD), ("szDevice", ctypes.c_wchar * 32)]


def cls(h):
    buf = ctypes.create_unicode_buffer(256)
    u32.GetClassNameW(h, buf, 256)
    return buf.value


def rect(h):
    r = wintypes.RECT()
    u32.GetWindowRect(h, ctypes.byref(r))
    return (r.left, r.top, r.right, r.bottom)


def pid_of(h):
    pid = wintypes.DWORD()
    u32.GetWindowThreadProcessId(h, ctypes.byref(pid))
    return pid.value


taskbars = []


@EnumProc
def top_cb(h, _):
    if cls(h) in TASKBAR_CLASSES:
        taskbars.append(h)
    return True


u32.EnumWindows(top_cb, 0)

print("== 显示器 ==")
mon_proc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HMONITOR, wintypes.HDC,
                              ctypes.POINTER(wintypes.RECT), wintypes.LPARAM)
u32.EnumDisplayMonitors.argtypes = [wintypes.HDC, ctypes.POINTER(wintypes.RECT), mon_proc, wintypes.LPARAM]


@mon_proc
def mon_cb(hm, _hdc, _rc, _lp):
    mi = MONITORINFOEXW()
    mi.cbSize = ctypes.sizeof(MONITORINFOEXW)
    u32.GetMonitorInfoW(hm, ctypes.byref(mi))
    print(f"  {mi.szDevice} primary={bool(mi.dwFlags & 1)} "
          f"bounds=({mi.rcMonitor.left},{mi.rcMonitor.top},{mi.rcMonitor.right},{mi.rcMonitor.bottom})")
    return True


u32.EnumDisplayMonitors(None, None, mon_cb, 0)

total = 0
print("\n== 任务栏上的贴片窗口 ==")
for tb in taskbars:
    print(f"-- {cls(tb)} 0x{tb:X} dpi={u32.GetDpiForWindow(tb)} rect={rect(tb)} --")
    kids = []

    @EnumProc
    def child_cb(h, _, kids=kids):
        if cls(h).startswith("VigilWatt"):
            kids.append(h)
        return True

    u32.EnumChildWindows(tb, child_cb, 0)
    for k in kids:
        total += 1
        print(f"   hwnd=0x{k:X} cls={cls(k)} pid={pid_of(k)} "
              f"vis={bool(u32.IsWindowVisible(k))} rect={rect(k)}")

print(f"\n贴片窗口总数：{total}")
if total > 1:
    print("异常：同一任务栏上有多个贴片窗口（见 docs/dev/runtime-pitfalls.md §16.12）")
    sys.exit(1)
print("正常")
