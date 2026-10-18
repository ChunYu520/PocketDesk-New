using System.Runtime.InteropServices;
using System.Text;

namespace PocketDesk.Host.Native;

/// <summary>宿主使用的 Win32 互操作声明。</summary>
internal static class Win32
{
    internal const int GWL_STYLE = -16;
    internal const int GWL_EXSTYLE = -20;
    internal const int GWLP_HWNDPARENT = -8;

    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_NOACTIVATE = 0x08000000;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_LAYERED = 0x00080000;

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOOWNERZORDER = 0x0200;
    internal const uint SWP_SHOWWINDOW = 0x0040;

    internal static readonly IntPtr HWND_TOP = IntPtr.Zero;
    internal static readonly IntPtr HWND_BOTTOM = new(1);
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);

    internal const uint WM_NCHITTEST = 0x0084;
    internal const uint WM_MOUSEACTIVATE = 0x0021;
    internal const uint WM_WINDOWPOSCHANGING = 0x0046;
    internal const uint WM_ACTIVATEAPP = 0x001C;
    internal const uint WM_DPICHANGED = 0x02E0;

    internal const int MA_ACTIVATE = 1;
    internal const int MA_NOACTIVATE = 3;

    internal const int HTTRANSPARENT = -1;
    internal const int HTCLIENT = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern void SwitchToThisWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool altTab);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    internal static extern IntPtr SetFocus(IntPtr hWnd);

    internal const int SW_SHOWNOACTIVATE = 4;

    internal static string ClassNameOf(IntPtr hwnd)
    {
        var buffer = new StringBuilder(64);
        GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    internal static bool IsDesktopWindow(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && ClassNameOf(hwnd) is "Progman" or "WorkerW";

    /// <summary>
    /// 返回桌面层最上层窗口的“上方窗口”，用于把组件层插到所有 Progman/WorkerW 之上、
    /// 普通应用之下。返回 IntPtr.Zero 时表示应使用 HWND_TOP。
    /// </summary>
    internal static IntPtr FindDesktopInsertAfter()
    {
        var previous = IntPtr.Zero;
        var result = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            var cls = ClassNameOf(hWnd);
            if (cls is "Progman" or "WorkerW")
            {
                result = previous;
                return false;
            }

            previous = hWnd;
            return true;
        }, IntPtr.Zero);
        return result;
    }

    internal static IntPtr FindDesktopWindow()
    {
        var found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (ClassNameOf(hWnd) == "Progman")
            {
                found = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    internal static IntPtr GetWindowLongPtrSafe(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr(hWnd, nIndex) : new IntPtr((int)GetWindowLongPtr(hWnd, nIndex));

    internal static void AddExStyle(IntPtr hwnd, int flags)
    {
        var current = GetWindowLongPtrSafe(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(current | (uint)flags));
    }

    internal static void RemoveExStyle(IntPtr hwnd, int flags)
    {
        var current = GetWindowLongPtrSafe(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(current & ~(uint)flags));
    }
}
