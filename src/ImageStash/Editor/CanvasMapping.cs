using System.Drawing;
using ImageStash.Layers;
using ImageStash.Media;
using Point = Avalonia.Point;
using Rect = Avalonia.Rect;
using Size = Avalonia.Size;

namespace ImageStash.Editor;

/// <summary>
/// Where the editor's canvas shows the picture, and the way back from a point of the canvas to the original's grid: the
/// output picture fitted into the canvas downscale-only, as the preview shows a picture (<see cref="ShownPicture.Fit"/>),
/// and the inverse of the whole drawing transform, the fit and Shrink's scale on each axis and the original's place in
/// the composition (the crop's origin, the caption bands' height), so the session gets every pointer position as a
/// fractional point on the original's pixels (What crosses every boundary) and a hit tolerance in them.
/// </summary>
/// <param name="Box">Where the output picture shows, in the canvas's units.</param>
/// <param name="Output">The output picture's size, which <paramref name="Box"/> shows.</param>
/// <param name="Composition">The composition's size before Shrink scales it: the shown part of the original with the bands above it.</param>
/// <param name="Shown">The part of the original the composition shows, on the original's grid.</param>
/// <param name="Top">The height of the caption bands above it.</param>
public sealed record CanvasMapping(Rect Box, PixelSize Output, PixelSize Composition, PixelRect Shown, int Top)
{
    /// <summary>
    /// The mapping for an original of this size under <paramref name="shown"/>, the list the canvas draws, in a canvas of
    /// <paramref name="area"/> on a display of <paramref name="scaling"/>; null while there is nothing to show.
    /// </summary>
    public static CanvasMapping? Of(int originalWidth, int originalHeight, LayerList shown, Size area, double scaling)
    {
        var output = LayerComposer.OutputSize(originalWidth, originalHeight, shown);
        var unshrunk = shown.Shrink == Shrink.Full ? shown : new LayerList(shown.Layers.Where(layer => layer is not ShrinkLayer));
        if (output.Width < 1 || output.Height < 1 || area.Width <= 0 || area.Height <= 0
            || LayerComposer.Placement(originalWidth, originalHeight, unshrunk) is not { } placement)
        {
            return null;
        }

        var box = ShownPicture.Fit(new Avalonia.PixelSize(output.Width, output.Height), area, scaling);
        return box.Width <= 0 || box.Height <= 0 ? null
            : new CanvasMapping(box, output, LayerComposer.OutputSize(originalWidth, originalHeight, unshrunk), placement.Shown, placement.Top);
    }

    /// <summary>The point of the original's grid under <paramref name="point"/> of the canvas; pixel (x, y) covers x to x + 1.</summary>
    public PointF ToOriginal(Point point) => new(
        (float)((point.X - Box.X) * Composition.Width / Box.Width + Shown.X),
        (float)((point.Y - Box.Y) * Composition.Height / Box.Height - Top + Shown.Y));

    /// <summary>The point of the canvas over the point (<paramref name="x"/>, <paramref name="y"/>) of the original's grid.</summary>
    public Point ToCanvas(float x, float y) => new(
        Box.X + (x - Shown.X) * Box.Width / Composition.Width,
        Box.Y + (y - Shown.Y + Top) * Box.Height / Composition.Height);

    /// <summary>A rectangle of the original's grid as it shows on the canvas.</summary>
    public Rect ToCanvas(RectangleF rect) => new(ToCanvas(rect.Left, rect.Top), ToCanvas(rect.Right, rect.Bottom));

    /// <summary>A distance on the canvas, in its units, as a distance on the original's grid along its wider scale.</summary>
    public float ToOriginal(double units) => (float)(units * Math.Max(Composition.Width / Box.Width, Composition.Height / Box.Height));
}
