using System.Runtime.InteropServices;
using Avalonia;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The checks' own mouse: the pointer moved and the left button clicked through SendInput, as a person's mouse
/// arrives, so the window there comes to the front as Windows brings it for a click. A check that moves the pointer
/// puts it back where it found it: the drag checks start where the pointer is and hang near a corner of the screen.
/// </summary>
public static class TestMouse
{
    private const uint InputMouse = 0;
    private const uint MoveAbsoluteOnVirtualDesktop = 0x0001 | 0x8000 | 0x4000;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint RightDown = 0x0008;
    private const uint RightUp = 0x0010;
    private const uint Wheel = 0x0800;

    /// <summary>Puts the pointer back where it is now once disposed.</summary>
    public static IDisposable PutBackAfterwards()
    {
        Assert.IsTrue(GetCursorPos(out var now));
        return new PutBack(now);
    }

    /// <summary>A place on the primary display away from its corners, the tray and the windows the checks open.</summary>
    public static PixelPoint Away => new(GetSystemMetrics(0) / 2, GetSystemMetrics(1) * 3 / 4);

    /// <summary>The pointer moved to <paramref name="at"/>, in physical pixels on the desktop.</summary>
    public static void MoveTo(PixelPoint at) => Send(Move(at));

    /// <summary>Where the pointer is now, in physical pixels on the desktop, as Windows placed it.</summary>
    public static PixelPoint Position()
    {
        Assert.IsTrue(GetCursorPos(out var now));
        return new PixelPoint(now.X, now.Y);
    }

    /// <summary>A left click at <paramref name="at"/>, in physical pixels on the desktop.</summary>
    public static void Click(PixelPoint at) => Send(Move(at), Button(LeftDown), Button(LeftUp));

    /// <summary>The pointer moved to <paramref name="at"/> and the left button pressed there and held, for a drag.</summary>
    public static void Press(PixelPoint at) => Send(Move(at), Button(LeftDown));

    /// <summary>The pointer moved to <paramref name="at"/> and the left button released there, ending a drag.</summary>
    public static void Release(PixelPoint at) => Send(Move(at), Button(LeftUp));

    /// <summary>A right click at <paramref name="at"/>, in physical pixels on the desktop.</summary>
    public static void RightClick(PixelPoint at) => Send(Move(at), Button(RightDown), Button(RightUp));

    /// <summary>The pointer moved to <paramref name="at"/> and the wheel turned <paramref name="notches"/> towards the user, as a scroll down.</summary>
    public static void WheelDown(PixelPoint at, int notches) =>
        Send(Move(at), new Input { Type = InputMouse, Mouse = new MouseInput { Flags = Wheel, Data = unchecked((uint)(-120 * notches)) } });

    /// <summary>The centre of <paramref name="control"/> on the desktop, in physical pixels, read on the UI thread.</summary>
    public static PixelPoint Centre(Avalonia.Controls.Control control) => TrayDesktop.Get(() => control.PointToScreen(new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2)));

    private static Input Move(PixelPoint at)
    {
        var desktop = TrayDesktop.VirtualDesktop;
        return new Input
        {
            Type = InputMouse,
            Mouse = new MouseInput
            {
                X = (int)((at.X - desktop.X) * 65535L / Math.Max(1, desktop.Width - 1)),
                Y = (int)((at.Y - desktop.Y) * 65535L / Math.Max(1, desktop.Height - 1)),
                Flags = MoveAbsoluteOnVirtualDesktop,
            },
        };
    }

    private static Input Button(uint flags) => new() { Type = InputMouse, Mouse = new MouseInput { Flags = flags } };

    private static void Send(params Input[] inputs) =>
        Assert.AreEqual((uint)inputs.Length, SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()), $"the mouse input was not sent: error {Marshal.GetLastPInvokeError()}");

    private sealed class PutBack(Point at) : IDisposable
    {
        public void Dispose() => SetCursorPos(at.X, at.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetrics(int index);
}
