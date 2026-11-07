using System.Drawing;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using SkiaSharp;

namespace ImageStash.Editor;

/// <summary>
/// Measures the file a list would render, as <see cref="FinishedPicture.Estimate"/> does: the footer's size (G24); a
/// cancelled <paramref name="cancel"/> asks it to stop at the frame it is on, its answer then dropped.
/// </summary>
public delegate Outcome<long> SizeEstimator(string originalPath, byte[] originalSha256, string itemName, LayerList layers, ImageFormat finishedFormat, TimeSpan heldFileRetry, CancellationToken cancel);

/// <summary>
/// One open edit (Composition: Editor), on the UI thread: the item's original as the frame showing at full size, which
/// on an animated original plays (<c>EditorSessionFrames.cs</c>; R7), the live layers over it with the selected one, the
/// tools, Compare, undo and redo within the session (G25), Shrink and the output's size and estimated file size (G24);
/// how it ends is in <c>EditorSessionEnding.cs</c>. The view hands it
/// pointer positions on the original's grid (What crosses every boundary) and draws what it holds through Media's one
/// routine (R4). While a Save runs, or once the session has ended, nothing changes the layers.
/// </summary>
public sealed partial class EditorSession : IDisposable
{
    // L2: a press that moves no farther than half the hit tolerance, a screen distance the view sets, is a click.
    private const float DragThresholdOfTolerance = 0.5f;

    private readonly StashStore store;
    private readonly FinishedPictureRenderer renderer;
    private readonly TimeSpan heldFileRetry;

    // A Save and the estimate never run beside each other (the GIF encoder quantizes on the pool).
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SizeEstimate estimate;
    private readonly Stack<Snapshot> undo = new();
    private readonly Stack<Snapshot> redo = new();
    private EditorTool tool;
    private bool compare;
    private float hitTolerance = 1;
    private RectangleF? handleRoom;
    private Task<EditResult>? saving;
    private bool closed;

    // The original's hash as the session decoded and shows it, which every read of the item since (an estimate's, a
    // Save's) is compared against before a Save renders (G21).
    private byte[] shownOriginal;

    private EditorSession(
        StashStore store, ItemForEdit edit, MediaDecoder original, MediaFrame first, LayerList layers, FinishedPictureRenderer renderer, ColorChoices colors, TimeSpan heldFileRetry,
        SizeEstimator estimator, TimeProvider time, SynchronizationContext? ui)
    {
        this.store = store;
        this.renderer = renderer;
        this.heldFileRetry = heldFileRetry;
        Edit = edit;
        shownOriginal = edit.OriginalSha256;
        this.first = first;
        Layers = layers;
        Colors = colors;
        // The frames first: whether the original is animated decides whether Trim is listed (G20).
        StartFrames(original, time, ui);
        Tools = EditorTools.For(this);
        tool = Tools[0];
        estimate = new SizeEstimate(this, estimator, gate, heldFileRetry);
        estimate.Ask(layers);
    }

    /// <summary>Raised after every change of the layers, the selection, the tool or its options, Compare, the estimate or the busy state.</summary>
    public event Action? Changed;

    /// <summary>Raised once, as the session ends: saved, failed after the commit, gone, or cancelled.</summary>
    public event Action<EditResult>? Ended;

    /// <summary>The item being edited, as the stash listed it when last read; after Save as new item, the new one.</summary>
    public StashItem Item => Edit.Item;

    /// <summary>What Edit opens on, read again at each Save: the original's place and record and the finished picture's format.</summary>
    public ItemForEdit Edit { get; private set; }

    /// <summary>The live layers.</summary>
    public LayerList Layers { get; private set; }

    /// <summary>The index of the selected layer in <see cref="Layers"/>, or null.</summary>
    public int? Selected { get; private set; }

    public Layer? SelectedLayer => Selected is { } index ? Layers.Layers[index] : null;

    /// <summary>The tools in tool-bar order.</summary>
    public IReadOnlyList<EditorTool> Tools { get; }

    /// <summary>The current tool; choosing one that does not own the selected layer deselects it.</summary>
    public EditorTool Tool
    {
        get => tool;
        set
        {
            if (!Tools.Contains(value))
            {
                throw new ArgumentException("The tool is not one of this session's.", nameof(value));
            }

            EndTyping();
            if (value == tool)
            {
                return;
            }

            tool.Abandon();
            tool = value;
            if (SelectedLayer is { } layer && !value.Owns(layer))
            {
                Selected = null;
            }

            RaiseChanged();
        }
    }

    /// <summary>The swatches, the recent colors and the picker's conversions (G21).</summary>
    public ColorChoices Colors { get; }

    /// <summary>Set while Compare is held: the view draws the original alone (G20).</summary>
    public bool Compare
    {
        get => compare;
        set
        {
            if (value != compare)
            {
                compare = value;
                RaiseChanged();
            }
        }
    }

    /// <summary>
    /// The list the view draws and the eyedropper samples: the original alone while Compare is held (G20); while Crop is
    /// current, the live layers but the crop, so the whole original shows round the crop's frame (G22); else the live layers.
    /// </summary>
    public LayerList ShownLayers => compare ? LayerList.Empty
        : tool is CropTool && Layers.Crop is not null ? new LayerList(Layers.Layers.Where(layer => layer is not CropLayer))
        : Layers;

    /// <summary>
    /// How far, in the original's pixels, a press may be from a layer or a handle and still be on it: the view sets it
    /// from a screen distance whenever its scale changes. Half of it is the drag threshold.
    /// </summary>
    public float HitTolerance
    {
        get => hitTolerance;
        set => hitTolerance = float.IsFinite(value) && value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "A tolerance is a distance.");
    }

    /// <summary>
    /// Where on the original's grid the view shows a handle whole, so a press can reach it, or null for anywhere: the view
    /// sets it with <see cref="HitTolerance"/>, and a text's handles beyond it are placed at its edge (<see cref="LayerGeometry.Handles"/>).
    /// </summary>
    public RectangleF? HandleRoom
    {
        get => handleRoom;
        set => handleRoom = value is not { } room || float.IsFinite(room.Left) && float.IsFinite(room.Top) && float.IsFinite(room.Right) && float.IsFinite(room.Bottom) && room.Width >= 0 && room.Height >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "A room is a finite rectangle.");
    }

    /// <summary>The Shrink choice: the one shrink layer, none at full size; a change is one step Undo reverses (G24).</summary>
    public Shrink Shrink
    {
        get => Layers.Shrink;
        set
        {
            if (!Accepts)
            {
                return;
            }

            EndTyping();
            if (value == Layers.Shrink)
            {
                return;
            }

            // A colour dragged on the picker meanwhile is put back first: the Shrink is its own step, from what was committed.
            tool.Abandon();
            var layers = Layers.Layers.ToList();
            var at = layers.FindIndex(layer => layer is ShrinkLayer);
            if (value == Shrink.Full)
            {
                layers.RemoveAt(at);
            }
            else if (at >= 0)
            {
                layers[at] = new ShrinkLayer(value);
            }
            else
            {
                layers.Add(new ShrinkLayer(value));
            }

            ChangeKeepingSelected(new LayerList(layers));
        }
    }

    /// <summary>The finished picture's size for the live layers, as Media computes it (G24).</summary>
    public PixelSize OutputSize => LayerComposer.OutputSize(first.Width, first.Height, Layers);

    /// <summary>The estimated file size of the finished picture for the live layers, or null while none is known for them (G24).</summary>
    public long? EstimatedBytes => estimate.BytesFor(Layers);

    /// <summary>Whether an estimate is pending or running.</summary>
    public bool Estimating => estimate.Asking;

    /// <summary>The running Save's busy state in words naming the item, or null while none runs (G35).</summary>
    public string? Busy { get; private set; }

    /// <summary>Whether the session has not yet ended.</summary>
    public bool IsOpen => !closed;

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    internal PixelSize OriginalSize => new(first.Width, first.Height);

    internal float DragThreshold => hitTolerance * DragThresholdOfTolerance;

    internal Snapshot Now => new(Layers, Selected);

    /// <summary>Whether the layers may change now: the session is open and no Save runs.</summary>
    internal bool Accepts => !closed && Busy is null;

    /// <summary>The item's name as failures and busy states give it: its name, or its file's when it has none.</summary>
    internal string Subject => ItemQueries.Named(Item.Name, Item.FileName);

    /// <summary>
    /// Opens a session on <paramref name="edit"/>: its original's first frame decoded at full size, read-only, and on an
    /// animated original its play started from that frame; the live layers starting on <paramref name="startOn"/> when
    /// given (the discarded list a Cancel notice's Undo brings back) and otherwise on the layers the picture on disk was
    /// rendered from, which differ from the committed ones during a live failure (State: A live failure); undo and redo
    /// empty. An original that is held or unreadable, or another file at its path, fails, naming the item (G35).
    /// <paramref name="estimator"/> measures the footer's size; null means <see cref="FinishedPicture.Estimate"/>. The play
    /// is timed by <paramref name="time"/>, the product's clock unless a check steps its own, and its frames change on the
    /// caller's synchronization context.
    /// </summary>
    public static async Task<Outcome<EditorSession>> Open(
        StashStore store, ItemForEdit edit, FinishedPictureRenderer renderer, ColorChoices colors, TimeSpan heldFileRetry, LayerList? startOn = null, SizeEstimator? estimator = null,
        TimeProvider? time = null)
    {
        var ui = SynchronizationContext.Current;
        var name = ItemQueries.Named(edit.Item.Name, edit.Item.FileName);
        var opened = await Task.Run(() => FirstFrame(edit, name, heldFileRetry));
        if (!opened.Succeeded)
        {
            return opened.Failure;
        }

        var (original, first) = opened.Value;
        return Outcome<EditorSession>.Of(new EditorSession(
            store, edit, original, first, startOn ?? edit.RenderedLayers, renderer, colors, heldFileRetry, estimator ?? FinishedPicture.Estimate, time ?? LocalClock.Instance, ui));
    }

    public void Press(PointF point)
    {
        if (Accepts)
        {
            EndTyping();
            tool.Press(point);
        }
    }

    public void Drag(PointF point)
    {
        if (Accepts)
        {
            tool.Drag(point);
        }
    }

    public void Release(PointF point)
    {
        if (Accepts)
        {
            tool.Release(point);
        }
    }

    /// <summary>
    /// Puts back the layers and the selection before the latest change (G25), after closing an open field: what was typed
    /// is that change, and a field on a layer just added, with nothing typed, goes with its layer and nothing more.
    /// </summary>
    public void Undo()
    {
        if (Accepts && !CloseField())
        {
            Step(undo, redo);
        }
    }

    /// <summary>Makes again the change the latest Undo put back (G25).</summary>
    public void Redo()
    {
        if (Accepts)
        {
            EndTyping();
            Step(redo, undo);
        }
    }

    /// <summary>Removes the selected layer (G23, G25), as one step; a field open on it closes first, and a layer that leaves with its empty field is all that goes.</summary>
    public void DeleteSelected()
    {
        if (!Accepts)
        {
            return;
        }

        var target = SelectedLayer;
        EndTyping();
        if (Selected is { } index && ReferenceEquals(Layers.Layers[index], target))
        {
            Change(new LayerList(Layers.Layers.Where((_, i) => i != index)), null);
        }
    }

    /// <summary>Selects nothing; not a step of its own, but a field open closes as its own.</summary>
    public void Deselect()
    {
        if (!Accepts)
        {
            return;
        }

        EndTyping();
        tool.Abandon();
        if (Selected is not null)
        {
            Selected = null;
            RaiseChanged();
        }
    }

    /// <summary>Removes every layer, as one step Undo reverses; the item itself changes only at Save (G26).</summary>
    public void RevertToOriginal()
    {
        if (Accepts)
        {
            EndTyping();
            Change(LayerList.Empty, null);
        }
    }

    /// <summary>
    /// The color of the picture as shown under <paramref name="point"/> of the original, for the eyedropper (G21): the
    /// shown list (<see cref="ShownLayers"/>) composed by Media's one routine, before Shrink and without handles or the
    /// crop's frame (R4); null off the picture.
    /// </summary>
    public Rgba? SampleColor(PixelPoint point)
    {
        var shown = ShownLayers;
        var unshrunk = shown.Shrink == Shrink.Full ? shown : new LayerList(shown.Layers.Where(layer => layer is not ShrinkLayer));
        // A caption's band above the shown part is the picture too (G21: it extends the picture's height).
        var showing = Showing;
        if (LayerComposer.Placement(showing.Frame.Width, showing.Frame.Height, unshrunk) is not { } placement
            || point.X < placement.Shown.X || point.X >= placement.Shown.X + placement.Shown.Width
            || point.Y < (long)placement.Shown.Y - placement.Top || point.Y >= placement.Shown.Y + placement.Shown.Height)
        {
            return null;
        }

        using var pixel = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(pixel))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(placement.Shown.X - point.X, placement.Shown.Y - point.Y - placement.Top);
            if (!LayerComposer.Compose(canvas, showing.Frame, unshrunk, showing.Index, Subject).Succeeded)
            {
                return null;
            }
        }

        var color = pixel.GetPixel(0, 0);
        return new Rgba(color.Red, color.Green, color.Blue, color.Alpha);
    }

    internal void RaiseChanged() => Changed?.Invoke();

    /// <summary>Shows <paramref name="layers"/> while a drag runs, as no step of its own.</summary>
    internal void Show(LayerList layers, int? selected)
    {
        Layers = layers;
        Selected = selected;
        RaiseChanged();
    }

    /// <summary>Completes a change that started from <paramref name="before"/>: one step Undo reverses, when the layers differ.</summary>
    internal void Commit(Snapshot before, LayerList layers, int? selected)
    {
        if (!layers.Equals(before.Layers))
        {
            undo.Push(before);
            redo.Clear();
            estimate.Ask(layers);
        }

        Show(layers, selected);
    }

    /// <summary>A change made at once, the selected layer kept selected where it now is.</summary>
    internal void ChangeKeepingSelected(LayerList layers)
    {
        tool.Abandon();
        Commit(Now, layers, Where(layers, SelectedLayer));
    }

    /// <summary>A change made at once with the selection given by index.</summary>
    internal void Change(LayerList layers, int? selected)
    {
        tool.Abandon();
        Commit(Now, layers, selected);
    }

    /// <summary>Selects the layer at <paramref name="index"/> and makes its own tool current, so the option row shows its values.</summary>
    internal void Select(int index)
    {
        Selected = index;
        tool = OwnerOf(Layers.Layers[index]);
        RaiseChanged();
    }

    /// <summary>
    /// The original opened and its first frame read at full size; the decoder stays open for the play, the session's to
    /// close. Opened by its hash, so the editor never shows a file renames put at the path while a held original waited.
    /// </summary>
    private static Outcome<(MediaDecoder Decoder, MediaFrame First)> FirstFrame(ItemForEdit edit, string name, TimeSpan heldFileRetry)
    {
        var opened = MediaDecoder.OpenOriginal(edit.OriginalPath, name, heldFileRetry, edit.OriginalSha256);
        if (!opened.Succeeded)
        {
            return opened.Failure;
        }

        var decoder = opened.Value!;
        var frame = decoder.NextFrame(Math.Max(decoder.Width, decoder.Height));
        if (frame is { Succeeded: true, Value: { } first })
        {
            return Outcome<(MediaDecoder, MediaFrame)>.Of((decoder, first));
        }

        decoder.Dispose();
        return frame.Failure ?? new Failure("Read the picture", name, MediaDecoder.UnreadableReason);
    }

    /// <summary>The index <paramref name="layer"/>, the very object, has in <paramref name="layers"/>, or null.</summary>
    private static int? Where(LayerList layers, Layer? layer)
    {
        for (var i = 0; layer is not null && i < layers.Layers.Count; i++)
        {
            if (ReferenceEquals(layers.Layers[i], layer))
            {
                return i;
            }
        }

        return null;
    }

    private EditorTool OwnerOf(Layer layer) => Tools.FirstOrDefault(each => each.Owns(layer)) ?? tool;

    private void Step(Stack<Snapshot> from, Stack<Snapshot> to)
    {
        if (!Accepts)
        {
            return;
        }

        // A gesture under way ends at Ctrl+Z or Ctrl+Y even with no step to take, so its let-go applies nothing.
        tool.Abandon();
        if (from.Count == 0)
        {
            return;
        }

        to.Push(Now);
        var back = from.Pop();
        Layers = back.Layers;
        Selected = back.Selected;
        if (SelectedLayer is { } layer)
        {
            tool = OwnerOf(layer);
        }

        estimate.Ask(Layers);
        RaiseChanged();
    }
}

/// <summary>The live layers with their selection: what one step of undo or redo puts back (G25).</summary>
internal readonly record struct Snapshot(LayerList Layers, int? Selected);
