using System.Drawing;
using ImageStash.Layers;

namespace ImageStash.Editor;

/// <summary>
/// Where a text layer's box may lie on the original's grid as it is moved, resized or retyped (G21): anywhere its box,
/// turned about its centre as Media draws it, stays inside the original, or on an axis where the turned box is longer
/// than the original, anywhere it still covers the original's span with its centre on it. So a turned text reaches the
/// space it visibly has, and its centre, which a turn keeps, always lies on the picture: no turn, and no turn back, takes
/// it out of reach. The box itself may then start left of or above the grid, which the model admits for a text layer
/// alone; unturned and no larger than the original, the box and its turned bounds are one, so it is kept inside the
/// original as every other layer is.
/// </summary>
internal static class TextPlacement
{
    /// <summary>The rectangle round <paramref name="box"/> turned by <paramref name="rotation"/> degrees about its centre.</summary>
    public static RectangleF Bounds(PixelRect box, double rotation)
    {
        var (sin, cos) = Math.SinCos(rotation * Math.PI / 180);
        double halfWidth = (box.Width * Math.Abs(cos) + box.Height * Math.Abs(sin)) / 2, halfHeight = (box.Width * Math.Abs(sin) + box.Height * Math.Abs(cos)) / 2;
        double centreX = box.X + box.Width / 2d, centreY = box.Y + box.Height / 2d;
        // Single precision, as the hits read it, drops the trace a quarter turn's cosine leaves on an edge of whole pixels.
        return RectangleF.FromLTRB((float)(centreX - halfWidth), (float)(centreY - halfHeight), (float)(centreX + halfWidth), (float)(centreY + halfHeight));
    }

    /// <summary>
    /// <paramref name="box"/>, turned by <paramref name="rotation"/>, moved by whole pixels as far as its turned bounds stay
    /// inside the original, or on an axis where they are longer, as far as they still cover it with its centre on it, its
    /// size kept.
    /// </summary>
    public static PixelRect Shifted(PixelRect box, double rotation, int dx, int dy, PixelSize original)
    {
        var bounds = Bounds(box, rotation);
        return box with { X = Placed(box.X, dx, bounds.Left, bounds.Right, original.Width), Y = Placed(box.Y, dy, bounds.Top, bounds.Bottom, original.Height) };
    }

    /// <summary><paramref name="box"/>, turned by <paramref name="rotation"/>, moved only as far as <see cref="Shifted"/> keeps it.</summary>
    public static PixelRect Kept(PixelRect box, double rotation, PixelSize original) => Shifted(box, rotation, 0, 0, original);

    /// <summary>
    /// An origin moved by <paramref name="by"/> as far as the span <paramref name="low"/> … <paramref name="high"/> moving with
    /// it stays inside 0 … <paramref name="limit"/>, or, longer than that, covers it with its middle inside.
    /// </summary>
    private static int Placed(int start, int by, float low, float high, int limit)
    {
        double from = low, to = high, middle = (from + to) / 2;
        var (least, most) = to - from <= limit
            ? (Math.Ceiling(-from), Math.Floor(limit - to))
            : (Math.Max(Math.Ceiling(limit - to), Math.Ceiling(-middle)), Math.Min(Math.Floor(-from), Math.Floor(limit - middle)));
        // Whole pixels can leave no room where the span is within a pixel of the limit's length: the least move then serves.
        var moved = least > most ? (long)least : Math.Clamp(by, (long)least, (long)most);
        return (int)Math.Clamp(start + moved, int.MinValue, int.MaxValue);
    }
}
