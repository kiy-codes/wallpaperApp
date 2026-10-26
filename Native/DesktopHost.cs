using System.Drawing;
using System.Runtime.InteropServices;

namespace WallpaperApp.Native;

/// <summary>
/// Puts a window between the desktop picture and the desktop icons, the way video wallpaper apps do.
/// Sending Progman the undocumented 0x052C message makes Explorer create a WorkerW window for this;
/// where that window ends up differs between Windows versions.
/// </summary>
public static class DesktopHost
{
    const uint SpawnWorkerW = 0x052C;
    const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    const long WS_CHILD = 0x40000000, WS_POPUP = 0x80000000, WS_EX_LAYERED = 0x80000;
    const uint LWA_ALPHA = 0x2, SMTO_NORMAL = 0;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    static readonly IntPtr HWND_BOTTOM = 1;
    const uint RDW_INVALIDATE = 0x1, RDW_ERASE = 0x4, RDW_ALLCHILDREN = 0x80, RDW_UPDATENOW = 0x100;

    /// <summary>Makes <paramref name="window"/> a child of the desktop, covering <paramref name="screenBounds"/>.</summary>
    public static void Attach(IntPtr window, Rectangle screenBounds)
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) throw new InvalidOperationException("The desktop isn't ready yet.");
        SendMessageTimeout(progman, SpawnWorkerW, 0xD, 0x1, SMTO_NORMAL, 1000, out _);

        IntPtr parent, icons = IntPtr.Zero, worker = IntPtr.Zero;
        var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        var innerWorker = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (defView != IntPtr.Zero && innerWorker != IntPtr.Zero)
        {
            // Windows 11 24H2 and later: the icons, our window and the WorkerW are all children of Progman,
            // in that order from top to bottom.
            parent = progman;
            icons = defView;
            worker = innerWorker;
        }
        else
        {
            // Earlier versions: a top-level WorkerW right behind the one that holds the icons.
            parent = FindWorkerBehindIcons();
            if (parent == IntPtr.Zero) throw new InvalidOperationException("Couldn't find the desktop window to play the video in.");
        }

        SetWindowLongPtr(window, GWL_STYLE, (GetWindowLongPtr(window, GWL_STYLE) & ~unchecked((nint)WS_POPUP)) | (nint)WS_CHILD);
        SetParent(window, parent);
        if (parent == progman)
        {
            // Children of Progman only draw if they're layered, and the style has to be set after
            // SetParent: set on the top-level window beforehand, it's accepted but nothing is drawn.
            SetWindowLongPtr(window, GWL_EXSTYLE, GetWindowLongPtr(window, GWL_EXSTYLE) | (nint)WS_EX_LAYERED);
            SetLayeredWindowAttributes(window, 0, 255, LWA_ALPHA);
        }

        var topLeft = new POINT { X = screenBounds.Left, Y = screenBounds.Top };
        ScreenToClient(parent, ref topLeft);
        SetWindowPos(window, icons, topLeft.X, topLeft.Y, screenBounds.Width, screenBounds.Height,
            SWP_NOACTIVATE | (icons == IntPtr.Zero ? SWP_NOZORDER : 0));
        // Bottom rather than just under this window, so it doesn't cover the video on another monitor.
        if (worker != IntPtr.Zero)
            SetWindowPos(worker, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Asks the desktop to repaint, so the last video frame doesn't stay behind the icons.</summary>
    public static void Repaint(IntPtr parent)
    {
        if (parent != IntPtr.Zero)
            RedrawWindow(parent, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
    }

    public static IntPtr ParentOf(IntPtr window) => GetParent(window);

    static IntPtr FindWorkerBehindIcons()
    {
        IntPtr worker = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
            worker = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return false;
        }, IntPtr.Zero);
        return worker;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern nint GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern nint SetWindowLongPtr(IntPtr hwnd, int index, nint value);

    [DllImport("user32.dll")]
    static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll")]
    static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRect, IntPtr updateRegion, uint flags);
}
