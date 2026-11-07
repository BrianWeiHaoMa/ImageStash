using System.Globalization;
using Avalonia;
using ImageStash.Media;
using ImageStash.Seams;

namespace ImageStash.Capture;

/// <summary>One display's frozen picture, grabbed once before any overlay shows (G14): the frame covers the display's bounds.</summary>
public sealed record DisplayGrab(Display Display, MediaFrame Frame);

/// <summary>
/// The region a drag selected (G14): a rectangle in physical pixels on the desktop, wholly inside its display's bounds
/// and at least one pixel wide and high.
/// </summary>
public sealed record SelectedRegion(Display Display, PixelRect Rectangle);

/// <summary>What the release leads to: saving the region as a screenshot (G14), or the region waiting to be recorded (G15).</summary>
public enum CaptureMode
{
    Screenshot,
    Recording,
}

/// <summary>What shows the frozen displays and lets the user drag a region over them: the overlay, or a check's stand-in.</summary>
public interface IRegionSelection
{
    /// <summary>
    /// Shows each grab dimmed, one per display, until the user releases a drag or cancels: the region, or null for Esc
    /// or a click without a drag (G14, G15: the record shortcut opens the same overlay, telling what the release leads to
    /// by <paramref name="mode"/>). Every window it showed is closed before it returns, however it ends (F4).
    /// </summary>
    Task<SelectedRegion?> Select(IReadOnlyList<DisplayGrab> grabs, CaptureMode mode);
}

/// <summary>
/// The drag's rules, as arithmetic on physical pixels so they hold on displays of any scaling (G14, E1): the rectangle
/// holds the pixel pressed and the pixel under the pointer and every pixel between, inside the display the press was
/// on, so a drag to that display's last row or column takes it in and a drag onto another display stops at the edge.
/// </summary>
public static class RegionSelection
{
    /// <summary>
    /// How far, in physical pixels along each axis, the pointer may stray between press and release and the two still be
    /// a click (L2), so a hand's tremble on a click saves no sliver.
    /// </summary>
    public const int ClickJitter = 2;

    /// <summary>The rectangle from <paramref name="press"/> to <paramref name="now"/>, kept to the bounds of <paramref name="display"/>, where the press was.</summary>
    public static PixelRect Dragged(Display display, PixelPoint press, PixelPoint now)
    {
        var (from, to) = (Inside(display.Bounds, press), Inside(display.Bounds, now));
        var (left, top) = (Math.Min(from.X, to.X), Math.Min(from.Y, to.Y));
        return new PixelRect(left, top, Math.Max(from.X, to.X) - left + 1, Math.Max(from.Y, to.Y) - top + 1);
    }

    /// <summary>
    /// What a release at <paramref name="release"/> selects: the dragged rectangle, however thin, or null when the pointer
    /// stayed within <see cref="ClickJitter"/> of the press on this display, a click without a drag, which cancels (G14).
    /// </summary>
    public static SelectedRegion? Released(Display display, PixelPoint press, PixelPoint release)
    {
        var (from, to) = (Inside(display.Bounds, press), Inside(display.Bounds, release));
        return Math.Abs(to.X - from.X) <= ClickJitter && Math.Abs(to.Y - from.Y) <= ClickJitter ? null : new SelectedRegion(display, Dragged(display, press, release));
    }

    /// <summary>
    /// The physical pixel on the desktop of a pointer <paramref name="position"/> Avalonia gives in device-independent
    /// units relative to a window whose client area starts at <paramref name="clientOrigin"/>, rounded back to the pixel it
    /// came from: Avalonia's own conversion truncates, which loses a pixel at a scaling such as 1.75 (E1).
    /// </summary>
    public static PixelPoint OnDesktop(PixelPoint clientOrigin, Point position, double scaling) =>
        new(clientOrigin.X + (int)Math.Round(position.X * scaling), clientOrigin.Y + (int)Math.Round(position.Y * scaling));

    /// <summary>The size label's words, in physical pixels (G14).</summary>
    public static string Label(PixelRect rectangle) => string.Create(CultureInfo.InvariantCulture, $"{rectangle.Width} × {rectangle.Height}");

    private static PixelPoint Inside(PixelRect bounds, PixelPoint point) => new(
        Math.Clamp(point.X, bounds.X, bounds.Right - 1),
        Math.Clamp(point.Y, bounds.Y, bounds.Bottom - 1));
}
