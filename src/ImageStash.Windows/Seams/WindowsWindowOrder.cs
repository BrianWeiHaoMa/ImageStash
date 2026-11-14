using System.Runtime.InteropServices;
using ImageStash.Seams;

namespace ImageStash.Windows.Seams;

/// <summary>
/// The window just in front of one of ImageStash's, found by walking Windows' z-order upwards from it past the windows
/// that do not stand on the screen (hidden, minimised, or cloaked, as a window on another virtual desktop is) and past
/// the windows it owns, a picker among them, which always stand just above it and go and come back with it; the walk
/// ends at the first topmost window, above which there are only topmost ones. Put back behind it, or behind the
/// foreground window when it no longer stands, with SetWindowPos, activating nothing.
/// </summary>
public sealed class WindowsWindowOrder : IWindowOrder
{
    private const uint GwHwndPrev = 3;
    private const uint GwOwner = 4;
    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008;
    private const uint DwmwaCloaked = 14;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    public nint? InFrontOf(Avalonia.Controls.Window window)
    {
        if (window.TryGetPlatformHandle() is not { } handle)
        {
            return null;
        }

        for (var above = GetWindow(handle.Handle, GwHwndPrev); above != 0; above = GetWindow(above, GwHwndPrev))
        {
            if (Topmost(above))
            {
                return null;
            }

            if (Stands(above) && !Owns(handle.Handle, above))
            {
                return above;
            }
        }

        return null;
    }

    public void PutBack(Avalonia.Controls.Window window, nint inFront)
    {
        if (window.TryGetPlatformHandle() is not { } handle)
        {
            return;
        }

        // The window in front now stands for one that went during the capture: it came in front of every other since.
        var behind = CanGoBehind(handle.Handle, inFront) ? inFront : GetForegroundWindow();
        if (CanGoBehind(handle.Handle, behind))
        {
            SetWindowPos(handle.Handle, behind, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
        }
    }

    /// <summary>Whether <paramref name="window"/> can go directly behind <paramref name="other"/>: it stands, is neither topmost nor the desktop or a taskbar, and is neither the window nor one it owns.</summary>
    private static bool CanGoBehind(nint window, nint other) =>
        other != 0 && other != window && IsWindow(other) && Stands(other) && !Topmost(other) && !Owns(window, other) && !WindowsTarget.DesktopOrTaskbar(other);

    private static bool Owns(nint owner, nint window)
    {
        for (var up = GetWindow(window, GwOwner); up != 0; up = GetWindow(up, GwOwner))
        {
            if (up == owner)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Topmost(nint window) => (GetWindowLongPtrW(window, GwlExStyle) & (nint)WsExTopmost) != 0;

    private static bool Stands(nint window) =>
        IsWindowVisible(window) && !IsIconic(window) && !(DwmGetWindowAttribute(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
