using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// A window of the check's own process on the primary display, brought to the front by a real click on it, as ImageStash's
/// own window would be; it keeps the keys it receives, so a check sees a keystroke that should have gone nowhere.
/// </summary>
public sealed class FrontWindow : IDisposable
{
    private readonly List<Key> keys = [];

    private FrontWindow(Avalonia.Controls.Window window, nint handle)
    {
        Window = window;
        Handle = handle;
    }

    public Avalonia.Controls.Window Window { get; }

    public nint Handle { get; }

    /// <summary>The keys it has received while in front.</summary>
    public List<Key> Keys() => TrayDesktop.Get(() => keys.ToList());

    /// <summary>Shows it with its top-left corner <paramref name="offset"/> into the primary display's working area.</summary>
    public static FrontWindow Show(PixelPoint offset) => TrayDesktop.Get(() =>
    {
        var window = new Avalonia.Controls.Window { Title = "ImageStash check window", Width = 300, Height = 200, CanResize = false };
        var area = window.Screens.Primary!.WorkingArea;
        window.Position = new PixelPoint(area.X + offset.X, area.Y + offset.Y);
        var front = new FrontWindow(window, window.TryGetPlatformHandle()!.Handle);
        window.KeyDown += (_, e) => front.keys.Add(e.Key);
        window.Show();
        return front;
    });

    /// <summary>A real click on it, then until it is the window in front.</summary>
    public void ClickToFront()
    {
        var centre = TrayDesktop.Get(() => new PixelRect(Window.Position, PixelSize.FromSize(Window.ClientSize, Window.RenderScaling)).Center);
        // A window just shown is on the screen a moment later, and a click before that lands on what is beneath it.
        var clock = Stopwatch.StartNew();
        while (Under(centre) != Handle && clock.ElapsedMilliseconds < 2000)
        {
            Thread.Sleep(20);
        }

        TestMouse.Click(centre);
        WaitUntilInFront(Handle);
    }

    /// <summary>Waits until <paramref name="window"/> is the window in front, for a few seconds at most.</summary>
    public static void WaitUntilInFront(nint window)
    {
        var clock = Stopwatch.StartNew();
        while (GetForegroundWindow() != window)
        {
            Assert.IsLessThan(5000, clock.ElapsedMilliseconds, $"{window:X} did not come to the front; {GetForegroundWindow():X} \"{NativeText(GetForegroundWindow())}\" is in front");
            Thread.Sleep(20);
        }
    }

    /// <summary>Waits until the window in front has stayed the same for <paramref name="milliseconds"/>, for five seconds at most, whichever it is.</summary>
    public static void WaitUntilTheFrontSettles(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        var (front, since) = (GetForegroundWindow(), clock.ElapsedMilliseconds);
        while (clock.ElapsedMilliseconds - since < milliseconds && clock.ElapsedMilliseconds < 5000)
        {
            Thread.Sleep(20);
            if (GetForegroundWindow() != front)
            {
                (front, since) = (GetForegroundWindow(), clock.ElapsedMilliseconds);
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="act"/> and watches the window in front without pause until <paramref name="milliseconds"/>
    /// after it: the first window other than <paramref name="window"/> seen in front, or 0, so an activation that lasts a
    /// moment is caught.
    /// </summary>
    public static nint ElsewhereInFront(nint window, Action act, int milliseconds)
    {
        nint elsewhere = 0;
        var watching = true;
        var watcher = new Thread(() =>
        {
            while (Volatile.Read(ref watching))
            {
                if (GetForegroundWindow() is var now && now != window && elsewhere == 0)
                {
                    elsewhere = now;
                }

                Thread.Yield();
            }
        }) { IsBackground = true };
        watcher.Start();
        try
        {
            act();
            Thread.Sleep(milliseconds);
        }
        finally
        {
            Volatile.Write(ref watching, false);
            watcher.Join();
        }

        return elsewhere;
    }

    /// <summary>The native control with the keyboard focus in the thread in front.</summary>
    public static nint FocusedHandle()
    {
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        Assert.IsTrue(GetGUIThreadInfo(0, ref info));
        return info.Focus;
    }

    /// <summary>The active window of the thread in front.</summary>
    public static nint ActiveHandle()
    {
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        Assert.IsTrue(GetGUIThreadInfo(0, ref info));
        return info.Active;
    }

    /// <summary>The text a native window holds: a window's title, or a native edit control's text, a file picker's name box among them.</summary>
    public static string NativeText(nint handle)
    {
        var text = new StringBuilder(1024);
        GetWindowTextW(handle, text, text.Capacity);
        return text.ToString();
    }

    /// <summary>The top-level window a click at <paramref name="point"/>, in physical pixels on the desktop, would reach.</summary>
    public static nint Under(PixelPoint point) => GetAncestor(WindowFromPoint(new ScreenPoint { X = point.X, Y = point.Y }), GaRoot);

    public void Dispose() => TrayDesktop.Run(Window.Close);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern nint GetForegroundWindow();

    private const uint GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint { public int X, Y; }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint WindowFromPoint(ScreenPoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetAncestor(nint window, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiRectangle { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public GuiRectangle CaretRectangle;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, StringBuilder text, int capacity);
}
