using System.Runtime.InteropServices;
using Avalonia;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The checks' own reading of another program's Windows caret on the screen, apart from the product's lookup (G1): the
/// caret rectangle a thread reports is in its caret window's logical client coordinates, so it is placed at that client
/// area's origin, read in physical pixels on a per-monitor-aware thread, with each of its coordinates and its size
/// scaled by the window's display DPI over the window's own DPI and rounded: 1 for a per-monitor-aware window, 1.5 for a
/// DPI-unaware one on a 150 % display.
/// </summary>
public static class ScreenCaret
{
    private const int MonitorNearest = 2;
    private const int EffectiveDpi = 0;
    private static readonly nint PerMonitorAware = -4;

    /// <summary>The Windows caret of <paramref name="window"/>'s thread on the screen in physical pixels, or null for none.</summary>
    public static PixelRect? Of(nint window)
    {
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        Assert.IsTrue(GetGUIThreadInfo(GetWindowThreadProcessId(window, out _), ref info));
        if (info.Caret == 0)
        {
            return null;
        }

        var ratio = DisplayPerOwn(info.Caret);
        int Scaled(int logical) => (int)Math.Round(logical * ratio);
        var caret = info.CaretRectangle;
        var origin = ClientOrigin(info.Caret);
        return new PixelRect(origin.X + Scaled(caret.Left), origin.Y + Scaled(caret.Top), Scaled(caret.Right - caret.Left), Scaled(caret.Bottom - caret.Top));
    }

    /// <summary>The focused window of <paramref name="window"/>'s thread, such as its edit box.</summary>
    public static nint FocusOf(nint window)
    {
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        Assert.IsTrue(GetGUIThreadInfo(GetWindowThreadProcessId(window, out _), ref info));
        return info.Focus;
    }

    /// <summary>The display DPI of <paramref name="window"/> over its own: how many physical pixels one of its logical pixels is.</summary>
    public static double DisplayPerOwn(nint window)
    {
        var own = GetDpiForWindow(window);
        Assert.IsGreaterThan(0u, own, "the caret window's DPI");
        Assert.AreEqual(0, GetDpiForMonitor(MonitorFromWindow(window, MonitorNearest), EffectiveDpi, out var display, out _), "the display's DPI");
        return display / (double)own;
    }

    /// <summary>The top-left of <paramref name="window"/>'s client area in physical pixels, read on a per-monitor-aware thread, where nothing is virtualized.</summary>
    private static PixelPoint ClientOrigin(nint window)
    {
        var before = SetThreadDpiAwarenessContext(PerMonitorAware);
        try
        {
            var corner = new PointOnScreen();
            Assert.IsTrue(ClientToScreen(window, ref corner));
            return new PixelPoint(corner.X, corner.Y);
        }
        finally
        {
            SetThreadDpiAwarenessContext(before);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointOnScreen
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rectangle CaretRectangle;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref PointOnScreen point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint MonitorFromWindow(nint window, int flags);

    [DllImport("shcore.dll", ExactSpelling = true)]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetThreadDpiAwarenessContext(nint context);
}
