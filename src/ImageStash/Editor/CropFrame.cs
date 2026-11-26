using System.Drawing;
using ImageStash.Layers;

namespace ImageStash.Editor;

/// <summary>A handle of Crop's frame: a corner, which moves two sides, or the middle of a side, which moves that side alone (G22).</summary>
public enum CropHandle
{
    Top,
    Bottom,
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Where Crop's frame has its handles and how a drag changes the frame, on the original's grid (G22; What crosses every
/// boundary): every rectangle made here is whole pixels inside the original and at least 1 × 1, and with 1:1 on a square.
/// The canvas draws <see cref="Handles"/>, and a press takes one by <see cref="LayerGeometry.HandleOn"/>, the rule a
/// selected layer's handles follow, so a handle is drawn where a press takes it and a small frame still moves from its middle.
/// </summary>
public static class CropFrame
{
    /// <summary>
    /// The handles of <paramref name="frame"/>: the middle of each side, then each corner, listed after the sides so that a
    /// corner wins a tie and is drawn over a side on a frame too small to part them.
    /// </summary>
    public static IReadOnlyList<(CropHandle Handle, PointF At)> Handles(PixelRect frame)
    {
        float left = frame.X, top = frame.Y, right = frame.X + frame.Width, bottom = frame.Y + frame.Height;
        float middleX = left + frame.Width / 2f, middleY = top + frame.Height / 2f;
        return
        [
            (CropHandle.Top, new(middleX, top)),
            (CropHandle.Bottom, new(middleX, bottom)),
            (CropHandle.Left, new(left, middleY)),
            (CropHandle.Right, new(right, middleY)),
            (CropHandle.TopLeft, new(left, top)),
            (CropHandle.TopRight, new(right, top)),
            (CropHandle.BottomLeft, new(left, bottom)),
            (CropHandle.BottomRight, new(right, bottom)),
        ];
    }

    /// <summary>The handle of <paramref name="frame"/> a press at <paramref name="point"/> takes within <paramref name="tolerance"/>, or null.</summary>
    internal static CropHandle? HandleAt(PixelRect frame, PointF point, float tolerance) => LayerGeometry.HandleOn(Handles(frame), frame, point, tolerance);

    /// <summary>
    /// <paramref name="frame"/> with <paramref name="handle"/> dragged by whole pixels, the opposite side or corner kept,
    /// turning round when dragged past it, as a hide's corner does (<see cref="LayerGeometry.Stretched"/>). With 1:1 on, a
    /// corner gives the square of the shorter side the drag gave, from the kept corner; a side gives the square of the
    /// length it dragged out, from the kept side, centred where the frame was across it and kept inside the original.
    /// </summary>
    internal static PixelRect Dragged(PixelRect frame, CropHandle handle, int dx, int dy, PixelSize original, bool square)
    {
        var (corner, alongX, alongY) = handle switch
        {
            CropHandle.Top => (Handle.TopLeft, false, true),
            CropHandle.Bottom => (Handle.BottomLeft, false, true),
            CropHandle.Left => (Handle.TopLeft, true, false),
            CropHandle.Right => (Handle.TopRight, true, false),
            CropHandle.TopLeft => (Handle.TopLeft, true, true),
            CropHandle.TopRight => (Handle.TopRight, true, true),
            CropHandle.BottomLeft => (Handle.BottomLeft, true, true),
            _ => (Handle.BottomRight, true, true),
        };
        var stretched = LayerGeometry.Stretched(frame, corner, alongX ? dx : 0, alongY ? dy : 0, original);
        if (!square)
        {
            return stretched;
        }

        var keptX = corner is Handle.TopLeft or Handle.BottomLeft ? frame.X + frame.Width : frame.X;
        var keptY = corner is Handle.TopLeft or Handle.TopRight ? frame.Y + frame.Height : frame.Y;
        if (alongX && alongY)
        {
            var side = Math.Min(stretched.Width, stretched.Height);
            return new PixelRect(stretched.X == keptX ? keptX : keptX - side, stretched.Y == keptY ? keptY : keptY - side, side, side);
        }

        // A side's square spans its dragged length, as far as the original holds it across.
        if (alongX)
        {
            var side = Math.Min(stretched.Width, original.Height);
            return new PixelRect(stretched.X == keptX ? keptX : keptX - side, Across(frame.Y, frame.Height, side, original.Height), side, side);
        }

        var tall = Math.Min(stretched.Height, original.Width);
        return new PixelRect(Across(frame.X, frame.Width, tall, original.Width), stretched.Y == keptY ? keptY : keptY - tall, tall, tall);
    }

    /// <summary>
    /// The frame a drag from <paramref name="from"/> to <paramref name="to"/> draws: the whole pixels it covers, kept inside
    /// the original; with 1:1 on, the square of its shorter side from the corner the drag started at.
    /// </summary>
    internal static PixelRect Drawn(PointF from, PointF to, PixelSize original, bool square)
    {
        var covered = LayerGeometry.Covering(from, to, new PixelRect(0, 0, original.Width, original.Height));
        if (!square)
        {
            return covered;
        }

        var side = Math.Min(covered.Width, covered.Height);
        return new PixelRect(
            to.X >= from.X ? covered.X : covered.X + covered.Width - side,
            to.Y >= from.Y ? covered.Y : covered.Y + covered.Height - side,
            side,
            side);
    }

    /// <summary>Where a span of <paramref name="length"/> starts, centred on the span <paramref name="start"/> … + <paramref name="span"/>, inside 0 … <paramref name="limit"/>.</summary>
    private static int Across(int start, int span, int length, int limit) => Math.Clamp(start + (span - length) / 2, 0, limit - length);
}
