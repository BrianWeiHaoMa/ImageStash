using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ImageStash.Media;
using ImageStash.Seams;

namespace ImageStash.Windows.Seams;

/// <summary>
/// The displays read as Avalonia 12.1.4 reads its screens (each monitor's rectangles and its effective DPI), in physical
/// pixels because Avalonia makes the process per-monitor DPI aware (S10), so the summon's displays and the capture's are
/// the same numbers (E1); a grab of the composed desktop through <c>GetDC(NULL)</c> and <c>BitBlt</c> (S4), the pointer
/// drawn into it with <c>DrawIconEx</c> when asked (G15); and a window kept out of every capture by its display affinity
/// (S4).
/// </summary>
public sealed class WindowsScreen : IScreen
{
    private const string GrabAction = "Grab the screen";
    private const string ExcludeAction = "Hide a window from captures";
    private const uint MonitorInfoPrimary = 1;
    private const int EffectiveDpi = 0;
    private const int ScreenWidth = 0;
    private const int ScreenHeight = 1;
    private const uint SrcCopy = 0x00CC0020;
    private const uint DibRgbColors = 0;
    private const uint WdaExcludeFromCapture = 0x11;
    private const uint CursorShowing = 0x1;
    private const uint CursorSuppressed = 0x2;
    private const uint DiNormal = 0x3;

    public IReadOnlyList<Display> Displays()
    {
        var displays = new List<Display>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            if (Read(monitor) is { } display)
            {
                displays.Add(display);
            }

            return true;
        }, 0);
        if (displays.Count > 0)
        {
            return displays;
        }

        // Never empty: with no monitor read, the primary display as the system metrics give it.
        var primary = new PixelRect(0, 0, GetSystemMetrics(ScreenWidth), GetSystemMetrics(ScreenHeight));
        return [new Display(primary, primary, 1, IsPrimary: true)];
    }

    public Outcome<MediaFrame> Grab(PixelRect rectangle, bool withPointer)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return new Failure(GrabAction, AreaWords(rectangle), "it is empty");
        }

        if (Displays().FirstOrDefault(d => d.Bounds.Contains(rectangle)) is not { } display)
        {
            return new Failure(GrabAction, AreaWords(rectangle), "it does not lie on one display");
        }

        var subject = $"the display at {display.Bounds.X}, {display.Bounds.Y} ({display.Bounds.Width} × {display.Bounds.Height})";
        var screen = GetDC(0);
        if (screen == 0)
        {
            return Refused(subject);
        }

        nint memory = 0, bitmap = 0, replaced = 0;
        try
        {
            memory = CreateCompatibleDC(screen);
            if (memory == 0)
            {
                return Refused(subject);
            }

            // A negative height makes the section top-down, as the pixel contract's rows are.
            var header = new BitmapInfoHeader { Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = rectangle.Width, Height = -rectangle.Height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(screen, ref header, DibRgbColors, out var bits, 0, 0);
            if (bitmap == 0 || (replaced = SelectObject(memory, bitmap)) == 0)
            {
                return Refused(subject);
            }

            // Waits for the next composition, so a window hidden a moment before the call is already gone from the desktop.
            DwmFlush();
            if (!BitBlt(memory, 0, 0, rectangle.Width, rectangle.Height, screen, rectangle.X, rectangle.Y, SrcCopy))
            {
                return Refused(subject);
            }

            if (withPointer && !DrawPointer(memory, rectangle))
            {
                return Refused(subject);
            }

            // A blit or drawing GDI batched reports its failure only here.
            if (!GdiFlush())
            {
                return Refused(subject);
            }

            var pixels = GC.AllocateUninitializedArray<byte>(rectangle.Width * rectangle.Height * 4);
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            // GDI leaves the alpha of a screen grab undefined; the desktop is opaque, as every later PNG and bitmap must be.
            for (var alpha = 3; alpha < pixels.Length; alpha += 4)
            {
                pixels[alpha] = 255;
            }

            return Outcome<MediaFrame>.Of(new MediaFrame(rectangle.Width, rectangle.Height, 0, pixels));
        }
        catch (OutOfMemoryException)
        {
            return new Failure(GrabAction, subject, "there is not enough memory to hold it");
        }
        finally
        {
            if (replaced != 0)
            {
                SelectObject(memory, replaced);
            }

            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }

            if (memory != 0)
            {
                DeleteDC(memory);
            }

            ReleaseDC(0, screen);
        }
    }

    public Outcome ExcludeFromCapture(TopLevel window)
    {
        if (window.TryGetPlatformHandle() is not { Handle: var handle } || handle == 0)
        {
            return new Failure(ExcludeAction, TitleOf(window), "it has no window on the screen");
        }

        return SetWindowDisplayAffinity(handle, WdaExcludeFromCapture)
            ? Outcome.Done
            : new Failure(ExcludeAction, TitleOf(window), WindowsRefusal.ReasonFor(Marshal.GetLastPInvokeError()));
    }

    /// <summary>
    /// Draws the pointer as the shell shows it now into <paramref name="memory"/>, which holds the grab of
    /// <paramref name="rectangle"/>: at its screen position less the rectangle's origin and the cursor's hot spot, cut to
    /// the bitmap by GDI. A pointer the shell is not showing (hidden, or suppressed for touch or pen) is none and draws
    /// nothing. False when Windows refuses to read or draw it, its reason left as the last error.
    /// </summary>
    private static bool DrawPointer(nint memory, PixelRect rectangle)
    {
        var cursor = new CursorInfo { Size = (uint)Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref cursor))
        {
            return false;
        }

        if ((cursor.Flags & CursorShowing) == 0 || (cursor.Flags & CursorSuppressed) != 0 || cursor.Cursor == 0)
        {
            return true;
        }

        if (!GetIconInfo(cursor.Cursor, out var icon))
        {
            return false;
        }

        try
        {
            // A 32-bit cursor is blended over the grab (DI_NORMAL). An animated one is drawn at its first frame: the shell
            // reports no step it is showing (CURSORINFO), and DrawIconEx draws the step it is given.
            return DrawIconEx(memory, cursor.Position.X - (int)icon.XHotspot - rectangle.X, cursor.Position.Y - (int)icon.YHotspot - rectangle.Y, cursor.Cursor, 0, 0, 0, 0, DiNormal);
        }
        finally
        {
            // GetIconInfo hands over copies of the cursor's two bitmaps, which are the caller's to delete.
            if (icon.Mask != 0)
            {
                DeleteObject(icon.Mask);
            }

            if (icon.Color != 0)
            {
                DeleteObject(icon.Color);
            }
        }
    }

    /// <summary>The window's title; a popup's, the title of the window it belongs to.</summary>
    private static string TitleOf(TopLevel window) => window switch
    {
        Avalonia.Controls.Window titled => titled.Title ?? "",
        PopupRoot popup => TitleOf(popup.ParentTopLevel),
        _ => "",
    };

    /// <summary>The monitor's bounds and working area, its scaling from its effective DPI, and whether it is primary; null when Windows cannot say.</summary>
    private static Display? Read(nint monitor)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info))
        {
            return null;
        }

        var scaling = GetDpiForMonitor(monitor, EffectiveDpi, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96d : 1;
        return new Display(PixelRectOf(info.Monitor), PixelRectOf(info.Work), scaling, (info.Flags & MonitorInfoPrimary) != 0);
    }

    private static PixelRect PixelRectOf(Rectangle rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);

    private static string AreaWords(PixelRect rectangle) =>
        $"the screen area at {rectangle.X}, {rectangle.Y} ({rectangle.Width} × {rectangle.Height})";

    private static Failure Refused(string subject) => new(GrabAction, subject, WindowsRefusal.ReasonFor(Marshal.GetLastPInvokeError()));

    private delegate bool MonitorProcedure(nint monitor, nint deviceContext, nint rectangle, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rectangle Monitor;
        public Rectangle Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public uint Size;
        public uint Flags;
        public nint Cursor;
        public Point Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint XHotspot;
        public uint YHotspot;
        public nint Mask;
        public nint Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint deviceContext, nint clip, MonitorProcedure procedure, nint data);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [DllImport("shcore.dll", ExactSpelling = true)]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateDIBSection(nint deviceContext, ref BitmapInfoHeader header, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint SelectObject(nint deviceContext, nint item);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GdiFlush();

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint item);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmFlush();

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(nint icon, out IconInfo info);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(nint deviceContext, int x, int y, nint icon, int width, int height, uint frame, nint flickerFreeBrush, uint flags);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
}
