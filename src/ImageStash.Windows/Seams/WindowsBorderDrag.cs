using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using ImageStash.Seams;

namespace ImageStash.Windows.Seams;

/// <summary>
/// Answers WM_SIZING, which only a drag of the border sends, with the rectangle held at the client's minimum plus the
/// frame the window has now. Avalonia 12.1.4 tells Windows a minimum whose frame is the system DPI's, too small on a
/// display of another scaling, and has its layout grow the window back from its top-left: a dragged left or top edge
/// past the minimum would walk the window instead of stopping it.
/// </summary>
public sealed class WindowsBorderDrag : IBorderDrag
{
    internal const uint WmSizing = 0x0214;

    private const int EdgeLeft = 1;
    private const int EdgeRight = 2;
    private const int EdgeTop = 3;
    private const int EdgeTopLeft = 4;
    private const int EdgeTopRight = 5;
    private const int EdgeBottom = 6;
    private const int EdgeBottomLeft = 7;
    private const int EdgeBottomRight = 8;

    public void StopAtMinimum(Avalonia.Controls.Window window) =>
        Win32Properties.AddWndProcHookCallback(window, (nint handle, uint message, nint wParam, nint lParam, ref bool handled) =>
        {
            if (message != WmSizing || !GetWindowRect(handle, out var outer) || !GetClientRect(handle, out var client))
            {
                return 0;
            }

            var frame = new PixelSize(outer.Right - outer.Left - client.Right, outer.Bottom - outer.Top - client.Bottom);
            var least = Least(new Size(window.MinWidth, window.MinHeight), window.RenderScaling, frame);
            var asked = Marshal.PtrToStructure<Rectangle>(lParam);
            Marshal.StructureToPtr(Stopped(asked, (int)wParam, least), lParam, fDeleteOld: false);
            handled = true;
            return 1;
        });

    /// <summary>The whole window at the client's <paramref name="minimum"/>: rounded up, so the client is never short of it and the layout grows nothing back.</summary>
    internal static PixelSize Least(Size minimum, double scaling, PixelSize frame) =>
        new((int)Math.Ceiling(minimum.Width * scaling) + frame.Width, (int)Math.Ceiling(minimum.Height * scaling) + frame.Height);

    /// <summary><paramref name="asked"/> no smaller than <paramref name="least"/>, the dragged <paramref name="edge"/>'s side giving way and the opposite one left where it is.</summary>
    internal static Rectangle Stopped(Rectangle asked, int edge, PixelSize least)
    {
        var stopped = asked;
        if (asked.Right - asked.Left < least.Width)
        {
            if (edge is EdgeLeft or EdgeTopLeft or EdgeBottomLeft)
            {
                stopped.Left = asked.Right - least.Width;
            }
            else if (edge is EdgeRight or EdgeTopRight or EdgeBottomRight)
            {
                stopped.Right = asked.Left + least.Width;
            }
        }

        if (asked.Bottom - asked.Top < least.Height)
        {
            if (edge is EdgeTop or EdgeTopLeft or EdgeTopRight)
            {
                stopped.Top = asked.Bottom - least.Height;
            }
            else if (edge is EdgeBottom or EdgeBottomLeft or EdgeBottomRight)
            {
                stopped.Bottom = asked.Top + least.Height;
            }
        }

        return stopped;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rectangle rectangle);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out Rectangle rectangle);
}
