using System.Drawing;
using ImageStash.Layers;

namespace ImageStash.Editor;

/// <summary>
/// Crop (G22): while it is current the canvas shows the whole original with the crop's rectangle framed, and every press
/// is the tool's own. A drag from one of the frame's corners resizes the rectangle, the opposite corner kept; a drag
/// that starts on it moves it over the picture, its size kept; each by whole pixels, inside the original and at least
/// 1 × 1, and each one step Undo reverses (G25). 1:1 makes the rectangle square and keeps it square as its corners drag;
/// Reset gives the whole picture back. The rectangle lies on the original's grid (What crosses every boundary), so the
/// whole original is kept and a crop can be widened later, and one covering the whole original is no crop: the list then
/// holds none. The crop is never a selected layer, so a click with any tool selects nothing of it and Del never removes it.
/// </summary>
public sealed class CropTool : EditorTool
{
    internal CropTool(EditorSession session)
        : base(session)
    {
    }

    public override string Name => "Crop";

    public override string Hint => "Drag the corners, or the picture";

    /// <summary>Whether 1:1 is on: the rectangle squared when it was turned on, and kept square as its corners drag.</summary>
    public bool Square { get; private set; }

    /// <summary>The rectangle the canvas frames: the part of the original the crop keeps, or the whole original without one.</summary>
    public PixelRect Frame => LayerGeometry.Picture(Session.Layers, Session.OriginalSize);

    private PixelRect Whole => new(0, 0, Session.OriginalSize.Width, Session.OriginalSize.Height);

    /// <summary>
    /// Turns 1:1 on or off. On, the rectangle becomes the square of its shorter side about its centre, as one step; off,
    /// it stays as it is and its corners drag freely again.
    /// </summary>
    public void ChooseSquare(bool on)
    {
        Square = on;
        var frame = Frame;
        var side = Math.Min(frame.Width, frame.Height);
        var squared = new PixelRect(frame.X + (frame.Width - side) / 2, frame.Y + (frame.Height - side) / 2, side, side);
        if (on && Session.Accepts && squared != frame)
        {
            Session.Change(Cropped(Session.Layers, squared), null);
        }
        else
        {
            Session.RaiseChanged();
        }
    }

    /// <summary>Gives the whole picture back, with 1:1 off, as one step.</summary>
    public void Reset()
    {
        Square = false;
        if (Session.Accepts && Session.Layers.Crop is not null)
        {
            Session.Change(Cropped(Session.Layers, Whole), null);
        }
        else
        {
            Session.RaiseChanged();
        }
    }

    /// <summary>The crop is never selected, so no layer is this tool's to select.</summary>
    public override bool Owns(Layer layer) => false;

    /// <summary>
    /// Every press: on a corner of the frame its drag resizes the rectangle, on the frame its drag moves it, and
    /// elsewhere its drag changes nothing.
    /// </summary>
    private protected override Func<int, int, LayerList> Taken(PointF point)
    {
        var (before, frame, original, tolerance) = (Session.Layers, Frame, Session.OriginalSize, Session.HitTolerance);
        if (LayerGeometry.CornerAt(frame, point, tolerance) is { } corner)
        {
            return (dx, dy) => Cropped(before, Resized(frame, corner, dx, dy, original));
        }

        return LayerGeometry.Over(frame, point, tolerance) ? (dx, dy) => Cropped(before, LayerGeometry.Shifted(frame, dx, dy, original)) : (_, _) => before;
    }

    /// <summary>
    /// <paramref name="layers"/> with <paramref name="rect"/> as their crop, in the crop's place in the list or after the
    /// rest; with no crop when it covers the whole original, which a crop never needs to.
    /// </summary>
    private LayerList Cropped(LayerList layers, PixelRect rect)
    {
        var kept = layers.Layers.Where(layer => layer is not CropLayer).ToList();
        if (rect != Whole)
        {
            kept.Insert(layers.Layers.ToList().FindIndex(layer => layer is CropLayer) is var at and >= 0 ? at : kept.Count, new CropLayer(rect));
        }

        return new LayerList(kept);
    }

    /// <summary>
    /// The frame with <paramref name="corner"/> dragged by whole pixels as a hide's corner drags; with 1:1 on, then the
    /// square of the shorter side the drag gave, from the corner the drag kept, so it stays inside what the drag covered.
    /// </summary>
    private PixelRect Resized(PixelRect frame, Handle corner, int dx, int dy, PixelSize original)
    {
        var stretched = LayerGeometry.Stretched(frame, corner, dx, dy, original);
        if (!Square)
        {
            return stretched;
        }

        var keptX = corner is Handle.TopLeft or Handle.BottomLeft ? frame.X + frame.Width : frame.X;
        var keptY = corner is Handle.TopLeft or Handle.TopRight ? frame.Y + frame.Height : frame.Y;
        var side = Math.Min(stretched.Width, stretched.Height);
        return new PixelRect(stretched.X == keptX ? keptX : keptX - side, stretched.Y == keptY ? keptY : keptY - side, side, side);
    }
}
