using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ClaudeUsageWidget;

// Windows sometimes demotes a topmost window below ordinary ones while leaving its
// WS_EX_TOPMOST style set (seen with an elevated Visual Studio in front). WPF only
// looks at the style, so it considers the window still topmost and never fixes it.
// Periodically check whether any visible non-topmost window sits above the widget
// in the z-order and, if so, push it back to the top of the topmost band. Only
// reasserting when actually demoted avoids jumping over other topmost windows
// (menus, tooltips, other tools) every tick.
public sealed class TopmostGuard
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    private readonly Window _window;
    private IntPtr _hwnd;

    public TopmostGuard(Window window)
    {
        _window = window;
        var timer = new DispatcherTimer { Interval = CheckInterval };
        timer.Tick += (_, _) => Check();
        _window.SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(_window).Handle;
            timer.Start();
        };
        _window.Closed += (_, _) => timer.Stop();
    }

    private void Check()
    {
        if (!_window.Topmost || !IsWindowVisible(_hwnd)) return;
        if (!IsDemoted()) return;
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private bool IsDemoted()
    {
        for (var h = GetWindow(_hwnd, GW_HWNDPREV); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDPREV))
        {
            if (IsWindowVisible(h) && (GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0)
                return true;
        }
        return false;
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint GW_HWNDPREV = 3;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOPMOST = 0x0008;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
