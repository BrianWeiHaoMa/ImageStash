using System.Drawing;
using ImageStash.Layers;

namespace ImageStash.Editor;

/// <summary>
/// Crop (G22): while it is current the canvas shows the whole original with the crop's rectangle framed, and every press
/// is the tool's own. A drag from one of the frame's handles (<see cref="CropFrame.Handles"/>) moves that handle: a
/// corner two sides, the opposite corner kept, a side's middle that side alone; a drag that starts inside a crop moves it
/// over the picture, its size kept; any other drag, outside the crop or anywhere on a picture with none, draws a new
/// frame over what it covers. Each is by whole pixels, inside the original and at least 1 × 1, and each one step Undo
/// reverses (G25); a click changes nothing. 1:1 makes the rectangle square and keeps it square as it is dragged;
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

    public override string Hint => "Drag across the part to keep, or drag the edges";

    /// <summary>Whether 1:1 is on: the rectangle squared when it was turned on, and kept square as it is dragged.</summary>
    public bool Square { get; private set; }

    /// <summary>The rectangle the canvas frames: the part of the original the crop keeps, or the whole original without one.</summary>
    public PixelRect Frame => LayerGeometry.Picture(Session.Layers, Session.OriginalSize);

    /// <summary>The frame's handles where the canvas draws them and a press takes them.</summary>
    public IReadOnlyList<(CropHandle Handle, PointF At)> Handles => CropFrame.Handles(Frame);

    private PixelRect Whole => new(0, 0, Session.OriginalSize.Width, Session.OriginalSize.Height);

    /// <summary>
    /// Turns 1:1 on or off. On, the rectangle becomes the square of its shorter side about its centre, as one step; off,
    /// it stays as it is and drags freely again.
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
    /// Every press: on a handle of the frame its drag moves that handle, with or without a crop; inside a crop its drag
    /// moves the crop; elsewhere its drag draws a new frame from the press to the pointer, in place of any crop, but
    /// none while the pointer is back within the drag threshold of the press, where a let-go is no drag (G25).
    /// </summary>
    private protected override Func<int, int, PointF, LayerList> Taken(PointF point)
    {
        var (before, frame, original, square, threshold) = (Session.Layers, Frame, Session.OriginalSize, Square, Session.DragThreshold);
        if (CropFrame.HandleAt(frame, point, Session.HitTolerance) is { } handle)
        {
            return (dx, dy, _) => Cropped(before, CropFrame.Dragged(frame, handle, dx, dy, original, square));
        }

        return before.Crop is not null && LayerGeometry.Over(frame, point, 0)
            ? (dx, dy, _) => Cropped(before, LayerGeometry.Shifted(frame, dx, dy, original))
            : (_, _, to) => MathF.Sqrt((to.X - point.X) * (to.X - point.X) + (to.Y - point.Y) * (to.Y - point.Y)) <= threshold ? before : Cropped(before, CropFrame.Drawn(point, to, original, square));
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
}
