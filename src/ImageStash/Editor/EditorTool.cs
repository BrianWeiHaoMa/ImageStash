using System.Drawing;
using ImageStash.Layers;

namespace ImageStash.Editor;

/// <summary>
/// One of the editor's tools (G20): its name on the tool bar, the sentence its option row states about the mouse, the
/// layers it owns, and its pointer handling over the session in the original's pixels, by G21's and G23's rules. A click
/// (a press released within the drag threshold) selects the topmost layer under it and makes that layer's own tool
/// current; on empty picture it is the current tool's (<see cref="Clicked"/>: Hide and Draw do nothing and leave the
/// selection as it is). A drag that starts on the selected layer moves it, started on a corner of a selected layer that
/// resizes (<see cref="LayerGeometry.Resizes"/>) resizes it, and started on a text layer's round handle turns it; any
/// other drag, one starting on an unselected layer included, draws a new layer of this tool's kind, selected once
/// released, or nothing for a tool that draws nothing by dragging. While a drag runs the session shows its result; its
/// release is one step Undo reverses (G25). An option changed while a layer of this tool is selected changes that layer
/// too. A field open for typing is closed before any of this (<see cref="EditorSession.EndTyping"/>), but an option
/// leaves open one whose layer holds no letter (<see cref="EditorSession.OptionsKeepField"/>). A tool may take a
/// press for its own instead (<see cref="Taken"/>): its drag then shows what the tool says, and a click on it selects nothing.
/// A press on an end of a range the filmstrip shows (<see cref="PressEnd"/>) is the current tool's gesture too.
/// </summary>
public abstract class EditorTool
{
    private Gesture? gesture;

    private protected EditorTool(EditorSession session) => Session = session;

    /// <summary>The tool's name on the tool bar.</summary>
    public abstract string Name { get; }

    /// <summary>What the mouse does with this tool, as its option row states it (G20).</summary>
    public abstract string Hint { get; }

    private protected EditorSession Session { get; }

    /// <summary>Whether <paramref name="layer"/> is of the kind this tool makes, so selecting it makes this tool current.</summary>
    public abstract bool Owns(Layer layer);

    /// <summary>
    /// The layer a drag from <paramref name="from"/> to <paramref name="to"/> draws, having passed over the pixels
    /// <paramref name="visited"/> in order, the press's first and the latest's last; null for a tool that draws nothing
    /// by dragging.
    /// </summary>
    private protected virtual Layer? Drawn(PointF from, PointF to, IReadOnlyList<PixelPoint> visited) => null;

    /// <summary>A click at <paramref name="point"/> that is on no layer and started off the selected one: nothing, but for a tool that adds a layer there.</summary>
    private protected virtual void Clicked(PointF point)
    {
    }

    /// <summary>Whether a press on <paramref name="layer"/>, not selected, takes it to move: a text layer for Text (G21); a mark only once selected (G23).</summary>
    private protected virtual bool MovesWhenPressed(Layer layer) => false;

    /// <summary>
    /// For a tool that takes a press at <paramref name="point"/> for its own, the layers its drag shows for an offset in
    /// whole pixels from the press, given the layers as they were at the press; null, the default, leaves the press to
    /// the rules above.
    /// </summary>
    private protected virtual Func<int, int, LayerList>? Taken(PointF point) => null;

    internal void Press(PointF point)
    {
        Abandon();
        if (Taken(point) is { } own)
        {
            gesture = new Gesture(point, Session.Now, Grip.Own, null) { Own = own };
            return;
        }

        var (layers, selected) = (Session.Layers, Session.Selected);
        var (tolerance, original) = (Session.HitTolerance, Session.OriginalSize);
        var handle = selected is { } index ? LayerGeometry.HandleAt(layers, index, point, tolerance, original, Session.HandleRoom) : null;
        if (handle is null && !(selected is { } on && LayerGeometry.Hits(layers, on, point, tolerance, original))
            && LayerGeometry.TopmostAt(layers, point, tolerance, original) is { } under && MovesWhenPressed(layers.Layers[under]))
        {
            Session.Select(under);
            selected = under;
        }

        gesture = handle == Handle.Rotate ? new Gesture(point, Session.Now, Grip.Rotate, handle)
            : handle is { } corner && LayerGeometry.Resizes(layers.Layers[selected!.Value]) ? new Gesture(point, Session.Now, Grip.Resize, corner)
            : selected is { } at && LayerGeometry.Hits(layers, at, point, tolerance, original) ? new Gesture(point, Session.Now, Grip.Move, null)
            : new Gesture(point, Session.Now, Grip.Draw, null) { Visited = { LayerGeometry.PixelUnder(point, LayerGeometry.Picture(layers, original)) } };
    }

    internal void Drag(PointF point)
    {
        if (gesture is not { Grip: not (Grip.End or Grip.Color) } drag)
        {
            return;
        }

        if (!drag.Dragging)
        {
            if (Distance(drag.Start, point) <= Session.DragThreshold)
            {
                return;
            }

            drag.Dragging = true;
        }

        var before = drag.Before;
        var original = Session.OriginalSize;
        var (dx, dy) = (Whole(point.X - drag.Start.X), Whole(point.Y - drag.Start.Y));
        switch (drag.Grip)
        {
            case Grip.Move:
                Session.Show(Replaced(before, LayerGeometry.Moved(before.Layers.Layers[before.Selected!.Value], dx, dy, original)), before.Selected);
                break;
            case Grip.Resize:
                Session.Show(Replaced(before, LayerGeometry.Resized(before.Layers, before.Selected!.Value, drag.Handle!.Value, dx, dy, original)), before.Selected);
                break;
            case Grip.Rotate:
                // The angle is the pointer's from the centre, not the drag's from its start (G21), and Shift is read at each move.
                Session.Show(Replaced(before, LayerGeometry.Turned(before.Layers.Layers[before.Selected!.Value], point, Session.ShiftHeld)), before.Selected);
                break;
            case Grip.Own:
                Session.Show(drag.Own!(dx, dy), before.Selected);
                break;
            default:
                // Thinned to the distinct pixels the pointer passed over, one after another.
                var pixel = LayerGeometry.PixelUnder(point, LayerGeometry.Picture(before.Layers, original));
                if (drag.Visited[^1] != pixel)
                {
                    drag.Visited.Add(pixel);
                }

                if (Drawn(drag.Start, point, drag.Visited) is { } drawn)
                {
                    Session.Show(new LayerList([.. before.Layers.Layers, drawn]), before.Layers.Layers.Count);
                }

                break;
        }
    }

    internal void Release(PointF point)
    {
        Drag(point);
        if (gesture is not { Grip: not (Grip.End or Grip.Color) } done)
        {
            return;
        }

        gesture = null;
        if (done.Dragging)
        {
            Session.Commit(done.Before, Session.Layers, Session.Selected);
        }
        else if (done.Grip is not (Grip.Rotate or Grip.Own) && LayerGeometry.TopmostAt(Session.Layers, point, Session.HitTolerance, Session.OriginalSize) is { } index)
        {
            // A click on the round handle is the handle's, so its double-click resets the turn whatever lies beneath (G21).
            Session.Select(index);
        }
        else if (done.Grip == Grip.Draw)
        {
            // A click on the selected layer's own handle, outside its box, is no click on open picture.
            Clicked(point);
        }
    }

    /// <summary>
    /// A press on <paramref name="end"/> of <paramref name="range"/> as the filmstrip shows it (the trim's handles, a ranged
    /// layer's bar; G22): its drag moves that end to a frame among those the editor can show, never past the other end,
    /// so at least one frame stays inside, and shows <paramref name="placed"/>'s layers for the range so far; its release
    /// is one step Undo reverses (G25), none when the range came back to where it was.
    /// </summary>
    internal void PressEnd(FrameRange range, RangeEnd end, Func<FrameRange, LayerList> placed)
    {
        Abandon();
        gesture = new Gesture(default, Session.Now, Grip.End, null) { Ends = (range, end, placed) };
    }

    /// <summary>The end pressed moved to <paramref name="frame"/>.</summary>
    internal void DragEnd(int frame)
    {
        if (gesture is { Grip: Grip.End, Ends: { } ends } drag)
        {
            var (range, end, placed) = ends;
            var at = Math.Clamp(frame, 0, Math.Max(0, Session.FrameCount - 1));
            var moved = end == RangeEnd.First ? range with { First = Math.Min(at, range.Last) } : range with { Last = Math.Max(at, range.First) };
            drag.Dragging = true;
            Session.Show(moved == range ? drag.Before.Layers : placed(moved), drag.Before.Selected);
        }
    }

    /// <summary>The end pressed let go where its drag left it: one step, when the layers changed.</summary>
    internal void ReleaseEnd()
    {
        if (gesture is { Grip: Grip.End } done)
        {
            gesture = null;
            Session.Commit(done.Before, Session.Layers, Session.Selected);
        }
    }

    /// <summary>Ends a press in progress, putting back what its drag showed, so a change made meanwhile starts from what was committed.</summary>
    internal void Abandon()
    {
        if (gesture is { } abandoned)
        {
            gesture = null;
            if (abandoned.Dragging)
            {
                Session.Show(abandoned.Before.Layers, abandoned.Before.Selected);
            }
        }
    }

    /// <summary>
    /// Applies a changed option to the selected layer when it is this tool's, as one step; the option row shows the change
    /// either way. An open field closes first, but while its layer holds no letter, which closing would take away, the
    /// option changes that layer, the field stays open, and the change joins the field's step.
    /// </summary>
    private protected void ApplyToSelected(Func<Layer, Layer> change)
    {
        // A colour being dragged on the picker is put back first: this change is its own step, from what was committed.
        Abandon();
        var chosenFor = Session.SelectedLayer;
        if (Session.OptionsKeepField)
        {
            if (chosenFor is { } typed && Owns(typed) && change(typed) is var shown && !shown.Equals(typed))
            {
                Session.ShowTyped(shown);
            }
            else
            {
                Session.RaiseChanged();
            }

            return;
        }

        Session.EndTyping();
        if (Session.Accepts && Session.SelectedLayer is { } layer && ReferenceEquals(layer, chosenFor) && Owns(layer) && change(layer) is var changed && !changed.Equals(layer))
        {
            Session.Change(Replaced(Session.Now, changed), Session.Selected);
        }
        else
        {
            Session.RaiseChanged();
        }
    }

    /// <summary>
    /// The picker's square or strip pressed (G21): a colour drag begins from the layers as they are now, held as this
    /// tool's gesture, so whatever abandons a gesture (Undo, Redo, another change, Deselect, a press on the canvas, Save,
    /// Cancel, the field closing or typed into) puts back what it showed and ends it. It shows on the selected layer when
    /// that is this tool's; with none of this tool's selected it changes nothing until it is let go. An open field closes
    /// first, but for one whose layer holds no letter, which stays open as an option chosen at once leaves it.
    /// </summary>
    internal void BeginColor()
    {
        Abandon();
        var chosenFor = Session.SelectedLayer;
        if (!Session.OptionsKeepField)
        {
            Session.EndTyping();
        }

        if (!Session.Accepts)
        {
            return;
        }

        // Dragging marks a drag with a layer to show it on, as a canvas drag's does once it moves.
        var on = Session.SelectedLayer is { } layer && ReferenceEquals(layer, chosenFor) && Owns(layer);
        gesture = new Gesture(default, Session.Now, Grip.Color, null) { Dragging = on };
    }

    /// <summary>
    /// The colour drag moved: the selected layer shown as <paramref name="change"/> makes it from what it was as the drag
    /// began, as no step (G25). False when no drag is held, because something ended it: a later move continues nothing.
    /// </summary>
    private protected bool PreviewOnSelected(Func<Layer, Layer> change)
    {
        if (gesture is not { Grip: Grip.Color } held)
        {
            return false;
        }

        if (held.Dragging)
        {
            var before = held.Before;
            Session.Show(Replaced(before, change(before.Layers.Layers[before.Selected!.Value])), before.Selected);
        }

        return true;
    }

    /// <summary>
    /// The colour drag let go: one step from where it began, made by <paramref name="change"/>, when it showed on a layer
    /// (G25), or on the layer of a field still open, which the field's step takes. False when nothing holds it any more,
    /// because something abandoned it or none began: a late let-go applies nothing.
    /// </summary>
    private protected bool CommitOnSelected(Func<Layer, Layer> change)
    {
        if (gesture is not { Grip: Grip.Color } held)
        {
            return false;
        }

        gesture = null;
        if (held.Dragging)
        {
            var before = held.Before;
            var changed = change(before.Layers.Layers[before.Selected!.Value]);
            // A field open now was open as the drag began (closing or opening one ends the drag), on this layer.
            if (Session.Typing)
            {
                Session.ShowTyped(changed);
            }
            else
            {
                Session.Commit(before, Replaced(before, changed), before.Selected);
            }
        }
        else
        {
            Session.RaiseChanged();
        }

        return true;
    }

    /// <summary>Whether a colour drag is held and shows on a layer of this tool's.</summary>
    private protected bool ColorDragShowsOnLayer => gesture is { Grip: Grip.Color, Dragging: true };

    /// <summary>Ends a colour drag that will not be let go (the picker closed, Esc, its pointer lost), putting back what it showed.</summary>
    internal void AbandonColor()
    {
        if (gesture is { Grip: Grip.Color })
        {
            Abandon();
        }
    }

    private static LayerList Replaced(Snapshot at, Layer layer) =>
        new(at.Layers.Layers.Select((each, i) => i == at.Selected ? layer : each));

    private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>An offset in whole pixels: the nearest, halves away from zero.</summary>
    private static int Whole(float offset) => (int)MathF.Round(offset, MidpointRounding.AwayFromZero);

    private enum Grip
    {
        Draw,
        Move,
        Resize,
        Rotate,
        Own,
        End,
        Color,
    }

    private sealed class Gesture(PointF start, Snapshot before, Grip grip, Handle? handle)
    {
        public PointF Start { get; } = start;

        public Snapshot Before { get; } = before;

        public Grip Grip { get; } = grip;

        public Handle? Handle { get; } = handle;

        public List<PixelPoint> Visited { get; } = [];

        public Func<int, int, LayerList>? Own { get; init; }

        public (FrameRange Range, RangeEnd End, Func<FrameRange, LayerList> Placed)? Ends { get; init; }

        public bool Dragging { get; set; }
    }
}

/// <summary>An end of a frame range the filmstrip shows: the trim's left or right handle, a ranged layer's start or end (G22).</summary>
public enum RangeEnd
{
    First,
    Last,
}
