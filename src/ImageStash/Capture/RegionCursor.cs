using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using ImageStash.Media;

namespace ImageStash.Capture;

/// <summary>
/// The cross the pointer shows over the capture overlay, before and during the drag (G14, G15): ImageStash's own, light
/// arms with a dark edge all round, so it shows on white, on black and on the dimmed grey between, where the system's
/// inverting cross fades into mid-grey. It is drawn at its display's scaling (E1), since Windows shows a picture cursor
/// pixel for pixel, and its hot spot is the middle pixel of the arms' crossing, the point a drag pressed there starts from.
/// </summary>
public static class RegionCursor
{
    private static readonly Color Light = Color.FromRgb(250, 250, 250);

    // Never blue 0: Avalonia builds a picture cursor's mask from each pixel's first byte, blue, and a pixel whose blue is
    // 0 is masked out as transparent, so a pure black edge would not show.
    private static readonly Color Dark = Color.FromRgb(16, 16, 28);

    /// <summary>
    /// The cross for a display at <paramref name="scaling"/>, as a frame in the boundary pixel contract, transparent
    /// outside the arms and their edge, and its hot spot in that frame's pixels.
    /// </summary>
    public static (MediaFrame Picture, PixelPoint HotSpot) Drawn(double scaling)
    {
        var light = Math.Max(1, (int)Math.Round(2 * scaling) - 1);
        var edge = Math.Max(1, (int)Math.Round(scaling));
        var arm = Math.Max(1, (int)Math.Round(11 * scaling));
        var side = light + 2 * (arm + edge);
        var from = arm + edge;
        var pixels = new byte[4 * side * side];
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var colour = Across(y, x) || Across(x, y) ? Light : Near(y, x) || Near(x, y) ? Dark : default(Color?);
                if (colour is { } c)
                {
                    var at = 4 * (y * side + x);
                    (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = (c.B, c.G, c.R, 255);
                }
            }
        }

        return (new MediaFrame(side, side, 0, pixels), new PixelPoint(from + (light - 1) / 2, from + (light - 1) / 2));

        // On the light arm that runs along the second coordinate: within its thickness across, inside its dark ends.
        bool Across(int across, int along) => across >= from && across < from + light && along >= edge && along < side - edge;

        // Within the edge's width of that arm, its ends included.
        bool Near(int across, int along) => across >= from - edge && across < from + light + edge && along >= 0 && along < side;
    }

    /// <summary>The cross as the cursor of an overlay window on a display at <paramref name="scaling"/>; its owner disposes it once the window has closed.</summary>
    public static Cursor For(double scaling)
    {
        var (picture, hotSpot) = Drawn(scaling);
        // The cursor holds its own copy of the pixels, so the bitmap goes at once.
        using var bitmap = FrameBitmap.Of(picture);
        return new Cursor(bitmap, hotSpot);
    }
}
