using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PocketDesk.Host.Native;

namespace PocketDesk.Host.Services;

/// <summary>
/// 负责桌面组件层的原生窗口行为：透明、无焦点、桌面层停靠、卡片区域命中测试与按需键盘激活。
/// 这部分是“像手机上一样”的核心：视觉始终活跃，但只有输入控件才拿到系统焦点。
/// </summary>
public sealed class WindowController : IDisposable
{
    private readonly Window _window;
    private readonly Func<Point, bool> _isPointOverCard;
    private readonly DispatcherTimer _watcher;
    private IntPtr _hwnd;
    private bool _inputActive;
    private bool _activating;
    private bool _disposed;
    private IntPtr _previousForeground;

    public WindowController(Window window, Func<Point, bool> isPointOverCard)
    {
        _window = window;
        _isPointOverCard = isPointOverCard;
        _watcher = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _watcher.Tick += (_, _) => WatchForeground();
    }

    public bool InputActive => _inputActive;

    public nint Handle => _hwnd;

    public event Action? InputStateChanged;

    public void Attach()
    {
        _hwnd = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_hwnd == IntPtr.Zero)
        {
            Core.LogService.Warn("无法获取宿主窗口句柄，桌面层行为未启用。");
            return;
        }

        Win32Properties.AddWindowStylesCallback(_window, AddWindowStyles);
        Win32Properties.AddWndProcHookCallback(_window, WndProcHook);
        // 样式回调在句柄创建时生效；窗口已存在时再显式补齐一次，确保任务栏/Alt+Tab 与默认焦点行为正确。
        Win32.AddExStyle(_hwnd, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
        DockToDesktopLayer();
        _watcher.Start();
    }

    private static (uint Style, uint ExStyle) AddWindowStyles(uint style, uint exStyle)
    {
        // 不进入任务栏 / Alt+Tab；默认不抢系统焦点。透明由 Avalonia 的 TransparencyLevelHint 处理。
        exStyle |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;
        return (style, exStyle);
    }

    private IntPtr WndProcHook(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32.WM_NCHITTEST:
            {
                var screenX = unchecked((short)(long)lParam);
                var screenY = unchecked((short)((long)lParam >> 16));
                var point = _window.PointToClient(new PixelPoint(screenX, screenY));
                var overCard = _isPointOverCard(point);

                // 卡片之外的透明区域整体穿透：点击/框选落到桌面或下层应用。
                handled = true;
                return new IntPtr(overCard ? Win32.HTCLIENT : Win32.HTTRANSPARENT);
            }

            case Win32.WM_MOUSEACTIVATE:
                // 只有输入控件处于激活状态时才允许系统激活窗口。
                handled = true;
                return new IntPtr(_inputActive ? Win32.MA_ACTIVATE : Win32.MA_NOACTIVATE);

            case Win32.WM_ACTIVATEAPP:
                // 激活过程中系统会先给当前窗口发一次 WM_ACTIVATEAPP(0) 再发 (1)，
                // 延迟检查真实前台窗口，既可以忽略激活竞态，又能在用户点到其他应用后恢复 NOACTIVATE。
                if (wParam == IntPtr.Zero && _inputActive)
                {
                    DispatcherTimer.RunOnce(() =>
                    {
                        if (!_inputActive || _activating || Win32.GetForegroundWindow() == _hwnd)
                        {
                            return;
                        }

                        _inputActive = false;
                        Win32.AddExStyle(_hwnd, Win32.WS_EX_NOACTIVATE);
                        InputStateChanged?.Invoke();
                    }, TimeSpan.FromMilliseconds(140));
                }

                break;
        }

        return IntPtr.Zero;
    }

    private void WatchForeground()
    {
        if (_disposed || _hwnd == IntPtr.Zero)
        {
            return;
        }

        var foreground = Win32.GetForegroundWindow();
        if (Win32.IsDesktopWindow(foreground))
        {
            // Win+D 等场景：桌面成为前台时组件层必须浮起，否则会被壁纸盖住。
            Win32.SetWindowPos(
                _hwnd,
                Win32.HWND_TOP,
                0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
            return;
        }

        if (!_inputActive && foreground != _hwnd)
        {
            DockToDesktopLayer();
        }
    }

    /// <summary>把窗口停靠到“桌面之上、普通应用窗口之下”。</summary>
    public void DockToDesktopLayer()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        // 关键：把桌面 Progman 设为宿主窗口的 owner。owner 关系保证窗口始终在桌面之上；
        // 再压到 z 序底部，即可落在所有普通应用窗口之下，而不会被壁纸/桌面图标盖住。
        var desktop = Win32.FindDesktopWindow();
        if (desktop != IntPtr.Zero)
        {
            var currentOwner = Win32.GetWindowLongPtrSafe(_hwnd, Win32.GWLP_HWNDPARENT);
            if (currentOwner != desktop)
            {
                Win32.SetWindowLongPtr(_hwnd, Win32.GWLP_HWNDPARENT, desktop);
            }
        }

        Win32.SetWindowPos(
            _hwnd,
            Win32.HWND_BOTTOM,
            0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);

        var exStyle = Win32.GetWindowLongPtrSafe(_hwnd, Win32.GWL_EXSTYLE).ToInt64();
        if ((exStyle & 0x8) != 0)
        {
            // owner + HWND_BOTTOM 不应进入 topmost 带；若系统仍添加，立即退出。
            Win32.SetWindowPos(
                _hwnd,
                Win32.HWND_NOTOPMOST,
                0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            Core.LogService.Warn("桌面停靠后出现 TOPMOST，已退出 topmost。");
        }
    }

    /// <summary>点击输入控件时临时获取真实键盘焦点。</summary>
    public void ActivateForInput(Control target)
    {
        if (_hwnd == IntPtr.Zero || _disposed)
        {
            target.Focus();
            return;
        }

        if (_inputActive)
        {
            target.Focus();
            return;
        }

        _previousForeground = Win32.GetForegroundWindow();
        _inputActive = true;
        _activating = true;
        Win32.RemoveExStyle(_hwnd, Win32.WS_EX_NOACTIVATE);
        Core.LogService.Info($"输入焦点开始：previous=0x{_previousForeground.ToInt64():X}");

        // 前台锁可能阻止 SetForegroundWindow，短暂附加到当前前台线程以提高成功率。
        var foreground = _previousForeground;
        var foregroundThread = foreground != IntPtr.Zero
            ? Win32.GetWindowThreadProcessId(foreground, out _)
            : 0;
        var currentThread = Win32.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
                       Win32.AttachThreadInput(foregroundThread, currentThread, true);
        try
        {
            if (!Win32.SetForegroundWindow(_hwnd))
            {
                Win32.SwitchToThisWindow(_hwnd, true);
            }

            Win32.SetFocus(_hwnd);
            _window.Activate();
            target.Focus();
        }
        finally
        {
            if (attached)
            {
                Win32.AttachThreadInput(foregroundThread, currentThread, false);
            }

            Dispatcher.UIThread.Post(() => _activating = false, DispatcherPriority.Background);
        }

        InputStateChanged?.Invoke();
    }

    /// <summary>结束输入：窗口回到无焦点状态，并把键盘交还之前的应用。</summary>
    public void DeactivateInput(bool restoreForeground)
    {
        if (!_inputActive)
        {
            return;
        }

        _inputActive = false;
        if (_hwnd != IntPtr.Zero)
        {
            Win32.AddExStyle(_hwnd, Win32.WS_EX_NOACTIVATE);
        }

        var previous = _previousForeground;
        if (restoreForeground && previous != IntPtr.Zero && previous != _hwnd && Win32.IsWindow(previous))
        {
            // 等当前输入消息处理完再交还前台，避免 SetForegroundWindow 被前台锁忽略。
            Dispatcher.UIThread.Post(() => RestoreForeground(previous), DispatcherPriority.Background);
        }

        Core.LogService.Info($"输入焦点结束：restore={restoreForeground}, previous=0x{previous.ToInt64():X}, " +
                             $"foreground=0x{Win32.GetForegroundWindow().ToInt64():X}");
        InputStateChanged?.Invoke();
    }

    private static void RestoreForeground(IntPtr target)
    {
        try
        {
            var foreground = Win32.GetForegroundWindow();
            if (foreground == target)
            {
                return;
            }

            var title = new System.Text.StringBuilder(256);
            Win32.GetWindowText(target, title, title.Capacity);
            Core.LogService.Info(
                $"尝试交还前台：target=0x{target.ToInt64():X} class={Win32.ClassNameOf(target)} title={title} " +
                $"visible={Win32.IsWindowVisible(target)} iconic={Win32.IsIconic(target)} current=0x{foreground.ToInt64():X}");

            if (Win32.IsIconic(target))
            {
                Win32.ShowWindow(target, 9); // SW_RESTORE
            }

            var foregroundThread = foreground != IntPtr.Zero
                ? Win32.GetWindowThreadProcessId(foreground, out _)
                : 0;
            var targetThread = Win32.GetWindowThreadProcessId(target, out _);
            var currentThread = Win32.GetCurrentThreadId();
            var attached = foregroundThread != 0 && foregroundThread != currentThread &&
                           Win32.AttachThreadInput(currentThread, foregroundThread, true);
            var attachedTarget = targetThread != 0 && targetThread != currentThread &&
                                 Win32.AttachThreadInput(currentThread, targetThread, true);
            bool set;
            try
            {
                set = Win32.SetForegroundWindow(target);
                if (!set)
                {
                    // 前台锁可能拒绝常规切换；SwitchToThisWindow 是系统任务切换使用的兼容路径。
                    Win32.SwitchToThisWindow(target, true);
                    set = Win32.GetForegroundWindow() == target;
                }
            }
            finally
            {
                if (attached)
                {
                    Win32.AttachThreadInput(currentThread, foregroundThread, false);
                }

                if (attachedTarget)
                {
                    Win32.AttachThreadInput(currentThread, targetThread, false);
                }
            }

            Core.LogService.Info($"已交还前台：set={set}，结果 foreground=0x{Win32.GetForegroundWindow().ToInt64():X}");
        }
        catch (Exception ex)
        {
            Core.LogService.Warn($"交还前台失败：{ex.Message}");
        }
    }

    public void BringAboveDesktop()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        Win32.SetWindowPos(
            _hwnd,
            Win32.HWND_TOP,
            0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher.Stop();
    }
}
