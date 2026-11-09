using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using ImageStash.Layers;
using ImageStash.Media;
using LayerPixelPoint = ImageStash.Layers.PixelPoint;
using LayerPixelRect = ImageStash.Layers.PixelRect;
using LayerPixelSize = ImageStash.Layers.PixelSize;
using ImageStash.Look;

namespace ImageStash.Editor;

/// <summary>
/// The editor's canvas (G20, G23, G6): the session's original at the frame showing, which plays on an animated original
/// (G22, G25), with its layers live at that frame, composed by Media's one routine
/// onto the canvas Avalonia's Skia lease hands out, scaled to the screen (R4, S10), never a copy of the picture drawn
/// as an image; downscale-only, as the preview shows a picture, inside a margin that keeps handles beyond the picture's
/// edges on the canvas, over the checkerboard so transparency shows; while
/// Compare is held, the original alone (G20); while Crop is current, the whole original with the crop's frame over it
/// (G22). The selected layer's outline and handles, and the crop's frame, are drawn over it; they are not layers. Every
/// pointer position goes to the session on the original's grid (<see cref="CanvasMapping"/>), a gesture's through the
/// mapping its press had, with a hit
/// tolerance in the original's pixels from a screen distance, with Shift's state, a second press of a double-click
/// on the Text tool going to the session as a double-click; while a colour is being picked a click samples the picture
/// through the session instead (G21). Each draw tells, on the UI thread, which families of the picture this PC lacks
/// (<see cref="Fallbacks"/>), so the editor says so (G21).
/// </summary>
public sealed class EditorCanvas : Control
{
    /// <summary>How near, in the canvas's units, a press must come to a layer or a handle to be on it; half is the drag threshold (L2).</summary>
    public const double PressReach = 8;

    internal const double HandleSize = 8;

    // L2: the original outside the crop's frame dimmed, the frame a white line, its size in a dark pill.
    private static readonly IBrush Dimmed = new SolidColorBrush(Color.FromArgb(0x8C, 0x0C, 0x0E, 0x16));
    private static readonly IPen FramePen = new Pen(Brushes.White, 1);
    private static readonly IBrush PillBackground = new SolidColorBrush(Color.FromArgb(0xDB, 0x14, 0x15, 0x1B));
    private static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");

    private readonly DrawnLook look;

    /// <summary>The selected layer's outline and handles and the crop's handles: the selection's mark in the system's accent (F10), white inside.</summary>
    private IBrush HandleBrush => look.Brush(LookKeys.SelectionRing) ?? Brushes.Transparent;

    private IPen OutlinePen => new Pen(HandleBrush, 1);

    private IPen RoundHandlePen => new Pen(HandleBrush, 2);
    private readonly Action<Failure> failed;
    private EditorSession? session;
    private EditorSession? named;
    private Action<Rgba>? picking;
    private bool pressing;
    private CanvasMapping? pressedOn;
    private Point pressedAt;
    private long drawsMade;
    private long drawReported;

    /// <param name="failed">Told, once a session, of a picture the screen could not draw (G35).</param>
    public EditorCanvas(Action<Failure> failed)
    {
        look = new DrawnLook(this);
        this.failed = failed;
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    /// <summary>Raised on the UI thread when <see cref="Fallbacks"/> changes.</summary>
    public event Action? FallbacksChanged;

    /// <summary>The families the latest draw of the session's picture lacked on this PC and drew in a fallback, each once.</summary>
    public IReadOnlyList<string> Fallbacks { get; private set; } = [];

    /// <summary>Whether the next click on the picture samples its colour instead of reaching the tools.</summary>
    public bool Picking => picking is not null;

    /// <summary>Shows <paramref name="next"/>, or nothing; a colour being picked is forgotten.</summary>
    public void Show(EditorSession? next)
    {
        if (session is not null)
        {
            session.Changed -= Redraw;
            session.FrameChanged -= Redraw;
        }

        session = next;
        picking = null;
        pressing = false;
        pressedOn = null;
        SetFallbacks([]);
        if (next is not null)
        {
            next.Changed += Redraw;
            next.FrameChanged += Redraw;
        }

        InvalidateVisual();
    }

    /// <summary>Makes the next click on the picture sample its colour, handed to <paramref name="picked"/> opaque (G21).</summary>
    public void PickColor(Action<Rgba> picked) => picking = picked;

    /// <summary>Stops picking a colour without one.</summary>
    public void StopPicking() => picking = null;

    /// <summary>
    /// Where the picture shows on the canvas now, inside a margin round it, and the way back to the original's grid; null
    /// while there is nothing to show.
    /// </summary>
    public CanvasMapping? Mapping()
    {
        if (session is null)
        {
            return null;
        }

        // L2: room round the picture for handles beyond its edges: a new text at the top has its
        // round handle above the picture by 3.5% of the picture's width, which this keeps on the canvas with its ring; a
        // larger text's is pulled back to the canvas (HandleRoom, G21).
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var margin = Math.Round(Math.Min(Bounds.Width * 0.04 + HandleSize, Math.Min(Bounds.Width, Bounds.Height) / 4) * scaling) / scaling;
        var inside = new Size(Math.Max(0, Bounds.Width - 2 * margin), Math.Max(0, Bounds.Height - 2 * margin));
        return CanvasMapping.Of(session.Frame.Width, session.Frame.Height, session.ShownLayers, inside, scaling) is { } mapping
            ? mapping with { Box = mapping.Box.Translate(new Vector(margin, margin)) }
            : null;
    }

    /// <summary>
    /// Where on the original's grid the canvas shows a handle whole, a handle's size inside its edges, which the session
    /// places a text's handles within (G21); null while there is nothing to show.
    /// </summary>
    public System.Drawing.RectangleF? HandleRoom() => Mapping() is { } mapping ? Room(mapping) : null;

    private System.Drawing.RectangleF? Room(CanvasMapping mapping)
    {
        if (Bounds.Width <= 2 * HandleSize || Bounds.Height <= 2 * HandleSize)
        {
            return null;
        }

        var (from, to) = (mapping.ToOriginal(new Point(HandleSize, HandleSize)), mapping.ToOriginal(new Point(Bounds.Width - HandleSize, Bounds.Height - HandleSize)));
        return System.Drawing.RectangleF.FromLTRB(from.X, from.Y, to.X, to.Y);
    }

    /// <summary>
    /// The operation <see cref="Render"/> hands the compositor: a snapshot taken now of what it draws, the frame showing
    /// with its index and the list, all immutable and kept alive by the operation itself, so a render the compositor runs
    /// after the session has ended or changed still draws the picture as it was (S10); null while there is nothing to show.
    /// </summary>
    public ICustomDrawOperation? DrawOperation() =>
        session is { } shown && Mapping() is { } mapping
            ? new ComposeOperation(new Rect(Bounds.Size), mapping, shown.Showing, shown.ShownLayers, shown.Subject, Report(shown), shown.Compare ? null : Drawn(shown, ++drawsMade))
            : null;

    public override void Render(DrawingContext context)
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        if (look.Brush(LookKeys.PictureSurround) is { } surround)
        {
            context.FillRectangle(surround, new Rect(Bounds.Size), (float)look.Radius(LookKeys.SurroundRadius));
        }

        if (session is not { } shown || Mapping() is not { } mapping)
        {
            return;
        }

        context.FillRectangle(ShownPicture.Checkerboard(dark, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1), mapping.Box);
        context.Custom(DrawOperation()!);
        var original = new LayerPixelSize(shown.Frame.Width, shown.Frame.Height);
        if (!shown.Compare && shown.Tool is CropTool crop)
        {
            DrawCropFrame(context, mapping, crop.Frame, original);
        }
        else if (!shown.Compare && shown.Selected is { } selected)
        {
            DrawSelection(context, mapping, shown.Layers, selected, original);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || session is not { } shown)
        {
            return;
        }

        Focus(NavigationMethod.Pointer);
        e.Handled = true;
        shown.ShiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // The original alone shows while Compare is held: nothing is drawn on what the layers are not shown over.
        if (shown.Compare || Mapping() is not { } mapping)
        {
            return;
        }

        var at = mapping.ToOriginal(point.Position);
        if (picking is { } picked)
        {
            picking = null;
            if (shown.SampleColor(new LayerPixelPoint((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y))) is { } color)
            {
                picked(color with { A = 0xFF });
            }

            return;
        }

        shown.HitTolerance = mapping.ToOriginal(PressReach);
        shown.HandleRoom = Room(mapping);
        if (e.ClickCount == 2 && shown.Tool is TextTool)
        {
            // A double-click edits a text or resets its turn, and is no press of its own: no release follows it (G21).
            shown.DoubleClick(at);
            return;
        }

        shown.Press(at);
        pressing = true;
        // The press's mapping serves the whole gesture: a caption it grows changes the picture's place on the canvas, and a
        // pointer that stops must keep the size it gave, not be read again through the new place (G21).
        pressedOn = mapping;
        pressedAt = point.Position;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (pressing && session is { } shown && pressedOn is { } mapping)
        {
            pressedAt = e.GetPosition(this);
            // Read at each move, so Shift pressed or let go mid-drag snaps or frees the turn at once (G21).
            shown.ShiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            shown.Drag(mapping.ToOriginal(pressedAt));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (pressing)
        {
            pressedAt = e.GetPosition(this);
            if (session is { } shown)
            {
                shown.ShiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            }

            Finish();
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        // The press ends where the pointer was last seen, so a drag cut short still lands as one step.
        Finish();
    }

    private void Finish()
    {
        if (pressing && session is { } shown && pressedOn is { } mapping)
        {
            pressing = false;
            shown.Release(mapping.ToOriginal(pressedAt));
        }

        pressing = false;
        pressedOn = null;
    }

    /// <summary>
    /// The selected layer's outline, but round a box or an arrow, whose own lines show where they are, turned with a text
    /// layer about its centre; and its handles where the session has them, already turned, each on whole display pixels:
    /// squares at the corners, and a round one joined to the box's top for turning a text (G21).
    /// </summary>
    private void DrawSelection(DrawingContext context, CanvasMapping mapping, LayerList layers, int index, LayerPixelSize original)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var bounds = LayerGeometry.Bounds(layers, index, original);
        var turn = LayerGeometry.Rotation(layers.Layers[index]);
        var outline = bounds is { } box ? mapping.ToCanvas(box) : default;
        if (layers.Layers[index] is not DrawMarkLayer { Kind: MarkKind.Box or MarkKind.Arrow } && bounds is not null)
        {
            if (turn == 0)
            {
                context.DrawRectangle(null, OutlinePen, new Rect(Snapped(outline.X, scaling) + 0.5 / scaling, Snapped(outline.Y, scaling) + 0.5 / scaling, Snapped(outline.Width, scaling), Snapped(outline.Height, scaling)));
            }
            else
            {
                var centre = outline.Center;
                using (context.PushTransform(Matrix.CreateTranslation(-centre.X, -centre.Y) * Matrix.CreateRotation(turn * Math.PI / 180) * Matrix.CreateTranslation(centre.X, centre.Y)))
                {
                    context.DrawRectangle(null, OutlinePen, outline);
                }
            }
        }

        foreach (var (kind, at) in LayerGeometry.Handles(layers, index, original, Room(mapping)))
        {
            var centre = mapping.ToCanvas(at.X, at.Y);
            if (kind == Handle.Rotate)
            {
                // The stem runs from the handle towards the box's centre as far as the box's top.
                var (dx, dy) = (outline.Center.X - centre.X, outline.Center.Y - centre.Y);
                var reach = Math.Sqrt(dx * dx + dy * dy);
                if (reach > outline.Height / 2)
                {
                    var stem = (reach - outline.Height / 2) / reach;
                    context.DrawLine(OutlinePen, centre, new Point(centre.X + dx * stem, centre.Y + dy * stem));
                }

                context.DrawEllipse(Brushes.White, RoundHandlePen, centre, HandleSize * 0.75, HandleSize * 0.75);
                continue;
            }

            var handle = new Rect(Snapped(centre.X - HandleSize / 2, scaling), Snapped(centre.Y - HandleSize / 2, scaling), HandleSize, HandleSize);
            context.FillRectangle(HandleBrush, handle);
            context.FillRectangle(Brushes.White, handle.Deflate(2));
        }
    }

    /// <summary>
    /// Crop's frame over the whole original (G22): the original outside the rectangle dimmed, the rectangle
    /// outlined, a square handle at each of its corners as a selected layer has, and its size in pixels in a pill at its foot.
    /// </summary>
    private void DrawCropFrame(DrawingContext context, CanvasMapping mapping, LayerPixelRect frame, LayerPixelSize original)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var picture = Snapped(mapping.ToCanvas(new System.Drawing.RectangleF(0, 0, original.Width, original.Height)), scaling);
        var kept = Snapped(mapping.ToCanvas(new System.Drawing.RectangleF(frame.X, frame.Y, frame.Width, frame.Height)), scaling);
        foreach (var outside in new[]
        {
            new Rect(picture.Left, picture.Top, picture.Width, kept.Top - picture.Top),
            new Rect(picture.Left, kept.Bottom, picture.Width, picture.Bottom - kept.Bottom),
            new Rect(picture.Left, kept.Top, kept.Left - picture.Left, kept.Height),
            new Rect(kept.Right, kept.Top, picture.Right - kept.Right, kept.Height),
        })
        {
            if (outside.Width > 0 && outside.Height > 0)
            {
                context.FillRectangle(Dimmed, outside);
            }
        }

        context.DrawRectangle(null, FramePen, kept.Deflate(0.5 / scaling));
        foreach (var corner in new[] { kept.TopLeft, kept.TopRight, kept.BottomLeft, kept.BottomRight })
        {
            var handle = new Rect(Snapped(corner.X - HandleSize / 2, scaling), Snapped(corner.Y - HandleSize / 2, scaling), HandleSize, HandleSize);
            context.FillRectangle(HandleBrush, handle);
            context.FillRectangle(Brushes.White, handle.Deflate(2));
        }

        var words = new FormattedText($"{frame.Width} × {frame.Height}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Font, FontStyle.Normal, FontWeight.SemiBold), 11, Brushes.White);
        var pill = new Rect(kept.Center.X - words.Width / 2 - 7, kept.Bottom - 6 - words.Height - 6, words.Width + 14, words.Height + 6);
        context.DrawRectangle(PillBackground, null, new RoundedRect(pill, look.Radius(LookKeys.SmallRadius)));
        context.DrawText(words, new Point(pill.X + 7, pill.Y + 3));
    }

    private static double Snapped(double units, double scaling) => Math.Round(units * scaling) / scaling;

    /// <summary>A rectangle with each edge on whole display pixels.</summary>
    private static Rect Snapped(Rect rect, double scaling) =>
        new(new Point(Snapped(rect.Left, scaling), Snapped(rect.Top, scaling)), new Point(Snapped(rect.Right, scaling), Snapped(rect.Bottom, scaling)));

    private void Redraw()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            InvalidateVisual();
        }
        else
        {
            Dispatcher.UIThread.Post(InvalidateVisual);
        }
    }

    /// <summary>
    /// What a draw tells of the families it lacked: kept on the UI thread while the session is still shown, and only when
    /// no later draw has told already, so a draw the compositor runs late never puts back an older answer.
    /// </summary>
    private Action<IReadOnlyList<string>> Drawn(EditorSession of, long draw) => families => Dispatcher.UIThread.Post(() =>
    {
        if (ReferenceEquals(session, of) && draw > drawReported)
        {
            drawReported = draw;
            SetFallbacks(families);
        }
    });

    private void SetFallbacks(IReadOnlyList<string> families)
    {
        if (!Fallbacks.SequenceEqual(families))
        {
            Fallbacks = families;
            FallbacksChanged?.Invoke();
        }
    }

    /// <summary>What the compositor's thread tells of a picture it could not draw: named on the UI thread, once for the session.</summary>
    private Action<Failure> Report(EditorSession of) => failure => Dispatcher.UIThread.Post(() =>
    {
        if (!ReferenceEquals(named, of))
        {
            named = of;
            failed(failure);
        }
    });

    /// <summary>
    /// One render of the picture as it stood when the operation was made: on the compositor's thread, or on the UI thread
    /// for a <c>RenderTargetBitmap</c>. It holds only managed, immutable things, so it stays valid until the compositor
    /// disposes it, whatever the session has done since; a lease the platform refuses is named, never drawn round (S10).
    /// </summary>
    private sealed class ComposeOperation(Rect bounds, CanvasMapping mapping, ShownFrame frame, LayerList layers, string name, Action<Failure> failed, Action<IReadOnlyList<string>>? drawn) : ICustomDrawOperation
    {
        private const string Action = "Show the picture in the editor";

        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
            {
                failed(new Failure(Action, name, "the screen gave the editor nothing to draw on"));
                return;
            }

            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            canvas.Translate((float)mapping.Box.X, (float)mapping.Box.Y);
            canvas.Scale((float)(mapping.Box.Width / mapping.Output.Width), (float)(mapping.Box.Height / mapping.Output.Height));
            var composed = LayerComposer.Compose(canvas, frame.Frame, layers, frame.Index, name);
            canvas.Restore();
            if (!composed.Succeeded)
            {
                failed(composed.Failure);
                return;
            }

            drawn?.Invoke(composed.Value!);
        }
    }
}
