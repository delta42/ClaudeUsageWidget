using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClaudeUsageWidget;

// Makes widgets snap to each other while dragged, leaving a fixed gap between them,
// and makes widgets that sit snapped together move as one group. Each profile runs
// as its own process, so the other widgets are found as top-level windows (tagged
// with a window property) rather than as WPF objects. Hold Ctrl while dragging —
// from the start or partway through — to pull a single widget out of its group.
public sealed class WidgetSnapping
{
    private const string WidgetProp = "ClaudeUsageWidget.Widget";
    private const double GapDip = 8;
    private const double SnapDistanceDip = 12;

    private readonly Window _window;
    private IntPtr _hwnd;

    // State for the drag in progress, captured when it starts.
    private RECT _dragStart;
    private readonly List<(IntPtr Hwnd, RECT Start)> _group = new();
    private readonly List<RECT> _others = new();
    private int _gap;
    private int _snapDistance;
    private int _lastDx, _lastDy;

    public WidgetSnapping(Window window)
    {
        _window = window;
        _window.SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(_window).Handle;
            SetProp(_hwnd, WidgetProp, new IntPtr(1));
            HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        };
        _window.Closed += (_, _) => RemoveProp(_hwnd, WidgetProp);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_ENTERSIZEMOVE:
                BeginDrag();
                break;
            case WM_MOVING:
                if (_group.Count > 0 && CtrlDown()) LeaveGroup();
                var rect = Marshal.PtrToStructure<RECT>(lParam);
                rect = Snap(rect);
                Marshal.StructureToPtr(rect, lParam, false);
                MoveGroup(rect);
                handled = true;
                return new IntPtr(1);
            case WM_EXITSIZEMOVE:
                _group.Clear();
                _others.Clear();
                break;
        }
        return IntPtr.Zero;
    }

    private void BeginDrag()
    {
        var scale = VisualTreeHelper.GetDpi(_window).DpiScaleX;
        _gap = (int)Math.Round(GapDip * scale);
        _snapDistance = (int)Math.Round(SnapDistanceDip * scale);

        GetWindowRect(_hwnd, out _dragStart);
        _lastDx = _lastDy = 0;
        _group.Clear();
        _others.Clear();

        var peers = new List<(IntPtr Hwnd, RECT Rect)>();
        EnumWindows((h, _) =>
        {
            if (h != _hwnd && IsWindowVisible(h) && GetProp(h, WidgetProp) != IntPtr.Zero &&
                GetWindowRect(h, out var r))
            {
                peers.Add((h, r));
            }
            return true;
        }, IntPtr.Zero);

        // The group is every widget reachable through a chain of snapped neighbours.
        var inGroup = new bool[peers.Count];
        if (!CtrlDown())
        {
            var frontier = new Queue<RECT>();
            frontier.Enqueue(_dragStart);
            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                for (var i = 0; i < peers.Count; i++)
                {
                    if (inGroup[i] || !AreAttached(current, peers[i].Rect)) continue;
                    inGroup[i] = true;
                    frontier.Enqueue(peers[i].Rect);
                }
            }
        }

        for (var i = 0; i < peers.Count; i++)
        {
            if (inGroup[i]) _group.Add(peers[i]);
            else _others.Add(peers[i].Rect);
        }
    }

    // Two widgets count as attached when they sit side by side (or stacked) with
    // exactly the snap gap between them, allowing a pixel for rounding.
    private bool AreAttached(RECT a, RECT b)
    {
        const int tolerance = 1;
        var overlapX = a.Left < b.Right && b.Left < a.Right;
        var overlapY = a.Top < b.Bottom && b.Top < a.Bottom;
        return (overlapY && (Math.Abs(a.Left - b.Right - _gap) <= tolerance || Math.Abs(b.Left - a.Right - _gap) <= tolerance))
            || (overlapX && (Math.Abs(a.Top - b.Bottom - _gap) <= tolerance || Math.Abs(b.Top - a.Bottom - _gap) <= tolerance));
    }

    // Snaps the whole group, not just the dragged widget, against every widget outside it.
    private RECT Snap(RECT proposed)
    {
        var dx = proposed.Left - _dragStart.Left;
        var dy = proposed.Top - _dragStart.Top;

        int? bestX = null, bestY = null;
        void Consider(RECT moving)
        {
            foreach (var other in _others)
            {
                // Only snap to a widget that is close on the other axis too, so a widget
                // across the screen doesn't tug on this one.
                var nearY = moving.Top < other.Bottom + _snapDistance && other.Top < moving.Bottom + _snapDistance;
                var nearX = moving.Left < other.Right + _snapDistance && other.Left < moving.Right + _snapDistance;
                if (nearY)
                {
                    Pick(ref bestX, other.Right + _gap - moving.Left);
                    Pick(ref bestX, other.Left - _gap - moving.Right);
                    Pick(ref bestX, other.Left - moving.Left);
                    Pick(ref bestX, other.Right - moving.Right);
                }
                if (nearX)
                {
                    Pick(ref bestY, other.Bottom + _gap - moving.Top);
                    Pick(ref bestY, other.Top - _gap - moving.Bottom);
                    Pick(ref bestY, other.Top - moving.Top);
                    Pick(ref bestY, other.Bottom - moving.Bottom);
                }
            }
        }

        Consider(proposed);
        foreach (var member in _group) Consider(member.Start.Offset(dx, dy));

        return proposed.Offset(bestX ?? 0, bestY ?? 0);
    }

    private void Pick(ref int? best, int delta)
    {
        if (Math.Abs(delta) > _snapDistance) return;
        if (best == null || Math.Abs(delta) < Math.Abs(best.Value)) best = delta;
    }

    // Keyboard.Modifiers only tracks keys pressed while this process has keyboard
    // focus, which a widget clicked straight from another app usually doesn't — so
    // read the physical key state instead.
    private static bool CtrlDown() => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

    // The rest of the group stays wherever the drag has carried it so far, and from
    // here on counts as ordinary widgets to snap against.
    private void LeaveGroup()
    {
        foreach (var (_, start) in _group) _others.Add(start.Offset(_lastDx, _lastDy));
        _group.Clear();
    }

    private void MoveGroup(RECT dragged)
    {
        var dx = _lastDx = dragged.Left - _dragStart.Left;
        var dy = _lastDy = dragged.Top - _dragStart.Top;
        foreach (var (hwnd, start) in _group)
        {
            // Async so a busy widget in another process can't stall this drag.
            SetWindowPos(hwnd, IntPtr.Zero, start.Left + dx, start.Top + dy, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
        }
    }

    private const int VK_CONTROL = 0x11;
    private const int WM_MOVING = 0x0216;
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_ASYNCWINDOWPOS = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;

        public RECT Offset(int dx, int dy) =>
            new() { Left = Left + dx, Top = Top + dy, Right = Right + dx, Bottom = Bottom + dy };
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetProp(IntPtr hwnd, string name, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetProp(IntPtr hwnd, string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr RemoveProp(IntPtr hwnd, string name);
}
