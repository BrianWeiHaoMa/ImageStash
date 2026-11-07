using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Look;

namespace ImageStash.Editor;

/// <summary>
/// The editor's filmstrip (G22): under the picture of an animated item, whatever the tool, every frame
/// the editor can show as a thumbnail in a cell of its own, the cells of equal width across the strip, and a red
/// playhead on the frame showing; a press, or a drag along the cells, seeks to the frame under the pointer, which pauses
/// the play (G25). The thumbnails are the session's small ones, drawn to fill their cells, never a full-size frame (R7).
/// While the session reads the frames the strip says so, naming the item (G35); a frame it could not read is named once
/// through the editor's failures, and the strip holds the frames read before it. The frames outside the trim are dimmed
/// whatever the tool; while Trim is current its two handles stand at the kept frames' outer edges, and a press on one
/// drags that end of the trim (G22). The lane above the cells holds the playhead's head and the selected text, hide or
/// draw layer's start and end as a bar whose ends drag (G22). A handle or a bar's end goes to the cell edge nearest the
/// pointer as it moves. While its window is hidden or minimised the play is held, and it goes on when the window shows
/// again, as the preview's does (G6, G3). It owns the mappings between a frame and its cell (<see cref="CellLeft"/>,
/// <see cref="CellRight"/>, <see cref="FrameAt"/>, <see cref="EdgeAt"/>).
/// </summary>
public sealed class Filmstrip : Control
{
    /// <summary>The automation name the desktop checks, and UI Automation's clients, find it by.</summary>
    public const string AutomationName = "Filmstrip";

    /// <summary>The lane above the cells, for the playhead's head (L2).</summary>
    public const double LaneHeight = 16;

    /// <summary>The cells' height (L2).</summary>
    public const double CellsHeight = 44;

    /// <summary>How far from a handle or a bar's end, in the strip's units, a press still takes it (L2).</summary>
    public const double Reach = 8;

    /// <summary>A trim handle's width (L2).</summary>
    public const double HandleWidth = 6;

    /// <summary>The playhead's colour.</summary>
    public static readonly Color PlayheadColor = Color.FromRgb(0xE5, 0x48, 0x4D);

    private static readonly IBrush PlayheadBrush = new SolidColorBrush(PlayheadColor);
    private static readonly IPen HandleEdge = new Pen(Brushes.White, 1.5);

    // The area's own fill over a frame outside the trim, so it shows at about a quarter of its strength.
    private static readonly IBrush LightDim = new SolidColorBrush(Color.FromArgb(0xB8, 0xED, 0xEE, 0xF2));
    private static readonly IBrush DarkDim = new SolidColorBrush(Color.FromArgb(0xB8, 0x30, 0x31, 0x38));
    private static readonly IBrush PillBackground = new SolidColorBrush(Color.FromArgb(0xDB, 0x14, 0x15, 0x1B));
    private static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");

    private readonly DrawnLook look;

    /// <summary>The trim's handles and a layer's range: the selection's mark in the system's accent (F10), as on the canvas.</summary>
    private IBrush HandleBrush => look.Brush(LookKeys.SelectionRing) ?? Brushes.Transparent;

    private IBrush? BarBackground => DrawnLook.Wash(HandleBrush, 0.22);
    private readonly Action<Failure> failed;
    private readonly List<Bitmap> cells = [];
    private EditorSession? session;
    private EditorSession? named;

    /// <summary>The frame a press on the cells, or the drag it began, asked for last; null while no press is on them.</summary>
    private int? sought;

    /// <summary>
    /// The trim handle, or the bar's end, a press holds: how far the end stood from the press, so it moves with the pointer
    /// from where it was grabbed, and the frame it bounds now; null while none is held.
    /// </summary>
    private (bool Handle, RangeEnd End, double Offset, int Frame)? holding;

    private bool heldWhileHidden;
    private TopLevel? window;

    /// <param name="failed">Told, once a session, of a frame the editor could not read (G35).</param>
    public Filmstrip(Action<Failure> failed)
    {
        look = new DrawnLook(this);
        this.failed = failed;
        Height = LaneHeight + CellsHeight;
        ClipToBounds = true;
        IsVisible = false;
        Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(this, AutomationName);
        // A plain control is left out of UI Automation's control view; in it, the published check finds the strip's bounds.
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Control);
    }

    /// <summary>The thumbnails drawn so far, in frame order, as the screen draws them.</summary>
    public IReadOnlyList<Bitmap> Cells => cells;

    /// <summary>The number of cells: the frames the editor can show.</summary>
    public int CellCount => session?.FrameCount ?? 0;

    /// <summary>Shows <paramref name="next"/>'s frames when it is animated; else nothing, and the strip takes no room.</summary>
    public void Show(EditorSession? next)
    {
        if (session is not null)
        {
            session.Changed -= OnChanged;
            session.FrameChanged -= OnChanged;
        }

        session = next;
        sought = null;
        holding = null;
        heldWhileHidden = false;
        foreach (var bitmap in cells)
        {
            bitmap.Dispose();
        }

        cells.Clear();
        IsVisible = next is { Animated: true };
        if (next is { Animated: true })
        {
            next.Changed += OnChanged;
            next.FrameChanged += OnChanged;
            Follow();
        }

        InvalidateVisual();
    }

    /// <summary>Where frame <paramref name="index"/>'s cell begins, in the strip's units.</summary>
    public double CellLeft(int index) => CellCount < 1 ? 0 : Bounds.Width * index / CellCount;

    /// <summary>Where frame <paramref name="index"/>'s cell ends, in the strip's units.</summary>
    public double CellRight(int index) => CellCount < 1 ? 0 : Bounds.Width * (index + 1) / CellCount;

    /// <summary>The frame whose cell is under <paramref name="x"/>, the first or the last beyond the strip's ends.</summary>
    public int FrameAt(double x) =>
        CellCount < 1 || Bounds.Width <= 0 ? 0 : Math.Clamp((int)Math.Floor(x / Bounds.Width * CellCount), 0, CellCount - 1);

    /// <summary>The cell edge nearest <paramref name="x"/>, as the index of the frame whose cell it begins: 0 to <see cref="CellCount"/>.</summary>
    public int EdgeAt(double x) =>
        CellCount < 1 || Bounds.Width <= 0 ? 0 : Math.Clamp((int)Math.Round(x / Bounds.Width * CellCount, MidpointRounding.AwayFromZero), 0, CellCount);

    public override void Render(DrawingContext context)
    {
        if (session is not { Animated: true } shown)
        {
            return;
        }

        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var strip = new Rect(0, LaneHeight, Bounds.Width, CellsHeight);
        if (look.Brush(LookKeys.PictureSurround) is { } surround)
        {
            context.FillRectangle(surround, strip, (float)look.Radius(LookKeys.SmallRadius));
        }

        var count = CellCount;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        // Each cell's edges on whole device pixels, so neighbours meet without a seam; a clip with more frames than the
        // strip has device pixels gives cells under one, which are taken together until they span a pixel, the first of
        // them filling it, so no pixel of the strip is left without a frame.
        for (var index = 0; index < count;)
        {
            var left = Snapped(CellLeft(index), scaling);
            var last = index;
            while (last + 1 < count && Snapped(CellRight(last), scaling) <= left)
            {
                last++;
            }

            var right = Snapped(CellRight(last), scaling);
            if (index < cells.Count && right > left)
            {
                DrawFilling(context, cells[index], new Rect(left, strip.Top, right - left, strip.Height));
            }

            index = last + 1;
        }

        DrawRanges(context, shown, strip, dark, scaling);
        if (shown.FramesBusy is { } busy)
        {
            var words = new FormattedText(busy, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Font), 12, Brushes.White)
            {
                MaxTextWidth = Math.Max(1, Bounds.Width - 24),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            var pill = new Rect(strip.Center.X - words.Width / 2 - 8, strip.Center.Y - words.Height / 2 - 3, words.Width + 16, words.Height + 6);
            context.DrawRectangle(PillBackground, null, new RoundedRect(pill, look.Radius(LookKeys.SmallRadius)));
            context.DrawText(words, new Point(pill.X + 8, pill.Y + 3));
        }

        var index0 = Math.Clamp(shown.FrameIndex, 0, Math.Max(0, count - 1));
        var x = Snapped((CellLeft(index0) + CellRight(index0)) / 2, scaling);
        context.FillRectangle(PlayheadBrush, new Rect(x - 1, 2, 2, Bounds.Height - 2));
        var head = new StreamGeometry();
        using (var draw = head.Open())
        {
            draw.BeginFigure(new Point(x - 6, 2), true);
            draw.LineTo(new Point(x + 6, 2));
            draw.LineTo(new Point(x, LaneHeight - 2));
            draw.EndFigure(true);
        }

        context.DrawGeometry(PlayheadBrush, null, head);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || session is not { Animated: true, IsOpen: true } shown)
        {
            return;
        }

        e.Handled = true;
        e.Pointer.Capture(this);
        var (x, y) = (point.Position.X, point.Position.Y);
        if (shown.Tool is TrimTool trim && NearerEnd(trim.Kept, x) is { } end)
        {
            holding = Held(true, trim.Kept, end, x);
            trim.PressHandle(end);
        }
        else if (shown.SelectedRange is { } range && y < LaneHeight + Reach / 2 && NearerEnd(range, x) is { } barEnd)
        {
            holding = Held(false, range, barEnd, x);
            shown.PressRangeEnd(barEnd);
        }
        else
        {
            sought = FrameAt(x);
            shown.Seek(sought.Value);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (holding is not null)
        {
            Hold(e.GetPosition(this).X, release: false);
            return;
        }

        // Against the frame asked for last, not the one showing: a drag back to it before the one pressed on has arrived
        // asks for it again.
        if (sought is not null && session is { IsOpen: true } shown && FrameAt(e.GetPosition(this).X) is var frame && frame != sought)
        {
            sought = frame;
            shown.Seek(frame);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (holding is not null)
        {
            Hold(e.GetPosition(this).X, release: true);
            e.Pointer.Capture(null);
        }

        if (sought is not null)
        {
            sought = null;
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        sought = null;
        // A drag cut short lands where the pointer was last seen, as one step.
        Hold(null, release: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        window = TopLevel.GetTopLevel(this);
        if (window is not null)
        {
            window.PropertyChanged += OnWindowChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (window is not null)
        {
            window.PropertyChanged -= OnWindowChanged;
        }

        window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private static double Snapped(double units, double scaling) => Math.Round(units * scaling) / scaling;

    /// <summary>What the bar over the filmstrip names its layer by: a text's first line, a hide's style, a mark's kind.</summary>
    private static string BarWords(Layer layer) => layer switch
    {
        TextLayer text => text.Text.Split('\n')[0] is { Length: > 0 } line ? line : "Text",
        HideLayer hide => hide.Kind == HideKind.Black ? "Black box" : "Blur",
        DrawMarkLayer mark => mark.Kind.ToString(),
        _ => "",
    };

    /// <summary>
    /// The trim and the selected layer's range over the cells in <paramref name="strip"/>: the frames outside the trim
    /// dimmed under every tool, so what is kept shows; the bar, dimmed over them too; under Trim its two handles.
    /// </summary>
    private void DrawRanges(DrawingContext context, EditorSession shown, Rect strip, bool dark, double scaling)
    {
        var kept = shown.Tools.OfType<TrimTool>().FirstOrDefault()?.Kept ?? new FrameRange(0, Math.Max(0, CellCount - 1));
        var (keptLeft, keptRight) = (Snapped(CellLeft(kept.First), scaling), Snapped(CellRight(kept.Last), scaling));
        var dim = dark ? DarkDim : LightDim;
        context.FillRectangle(dim, new Rect(0, strip.Top, keptLeft, strip.Height));
        context.FillRectangle(dim, new Rect(keptRight, strip.Top, Math.Max(0, Bounds.Width - keptRight), strip.Height));
        if (shown.SelectedRange is { } range)
        {
            DrawBar(context, shown.SelectedLayer!, Snapped(CellLeft(range.First), scaling), Snapped(CellRight(range.Last), scaling), keptLeft, keptRight);
        }

        if (shown.Tool is TrimTool)
        {
            foreach (var edge in new[] { keptLeft, keptRight })
            {
                // Wholly inside the strip at its ends.
                var centre = Math.Clamp(edge, HandleWidth / 2, Math.Max(HandleWidth / 2, Bounds.Width - HandleWidth / 2));
                context.DrawRectangle(HandleBrush, HandleEdge, new RoundedRect(new Rect(centre - HandleWidth / 2, strip.Top, HandleWidth, strip.Height), look.Radius(LookKeys.SmallRadius)));
            }
        }
    }

    /// <summary>
    /// The selected layer's bar in the lane from <paramref name="left"/> to <paramref name="right"/>, its ends marked as
    /// what drags; its parts over frames outside the trim, beyond <paramref name="keptLeft"/> and <paramref name="keptRight"/>, dimmed.
    /// </summary>
    private void DrawBar(DrawingContext context, Layer layer, double left, double right, double keptLeft, double keptRight)
    {
        var bar = new Rect(left, 1, Math.Max(2, right - left), LaneHeight - 2);
        var words = new FormattedText(BarWords(layer), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Font), 10.5, HandleBrush)
        {
            MaxTextWidth = Math.Max(1, bar.Width - 12),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        var parts = new[]
        {
            (new Rect(keptLeft, 0, Math.Max(0, keptRight - keptLeft), LaneHeight), 1.0),
            (new Rect(0, 0, keptLeft, LaneHeight), 0.35),
            (new Rect(keptRight, 0, Math.Max(0, Bounds.Width - keptRight), LaneHeight), 0.35),
        };
        foreach (var (part, opacity) in parts)
        {
            using (context.PushClip(part))
            using (context.PushOpacity(opacity))
            {
                context.DrawRectangle(BarBackground, null, new RoundedRect(bar, look.Radius(LookKeys.SmallRadius)));
                context.FillRectangle(HandleBrush, new Rect(bar.Left, bar.Top, Math.Min(3, bar.Width), bar.Height));
                context.FillRectangle(HandleBrush, new Rect(bar.Right - Math.Min(3, bar.Width), bar.Top, Math.Min(3, bar.Width), bar.Height));
                if (bar.Width > 16)
                {
                    context.DrawText(words, new Point(bar.Left + 6, bar.Top + (bar.Height - words.Height) / 2));
                }
            }
        }
    }

    /// <summary>The end of <paramref name="range"/>'s span on the strip within <see cref="Reach"/> of <paramref name="x"/>, the nearer of the two; else null.</summary>
    private RangeEnd? NearerEnd(FrameRange range, double x)
    {
        var (first, last) = (Math.Abs(x - CellLeft(range.First)), Math.Abs(x - CellRight(range.Last)));
        return Math.Min(first, last) > Reach ? null : first <= last ? RangeEnd.First : RangeEnd.Last;
    }

    /// <summary><paramref name="end"/> of <paramref name="range"/> held by a press at <paramref name="x"/>.</summary>
    private (bool Handle, RangeEnd End, double Offset, int Frame) Held(bool handle, FrameRange range, RangeEnd end, double x) =>
        end == RangeEnd.First ? (handle, end, CellLeft(range.First) - x, range.First) : (handle, end, CellRight(range.Last) - x, range.Last);

    /// <summary>
    /// The end held moved with the pointer, now at <paramref name="x"/>, to the nearest cell edge, which bounds the frame
    /// it then gives; let go there when <paramref name="release"/>, where it was last with no <paramref name="x"/>.
    /// </summary>
    private void Hold(double? x, bool release)
    {
        if (holding is not { } held || session is not { } shown)
        {
            holding = null;
            return;
        }

        var trim = shown.Tools.OfType<TrimTool>().FirstOrDefault();
        if (x is { } at)
        {
            var edge = EdgeAt(at + held.Offset);
            var frame = held.End == RangeEnd.First ? Math.Min(edge, Math.Max(0, CellCount - 1)) : Math.Max(edge - 1, 0);
            if (frame != held.Frame)
            {
                held = held with { Frame = frame };
                if (held.Handle)
                {
                    trim?.DragHandle(frame);
                }
                else
                {
                    shown.DragRangeEnd(frame);
                }
            }
        }

        holding = release ? null : held;
        if (release && held.Handle)
        {
            trim?.ReleaseHandle();
        }
        else if (release)
        {
            shown.ReleaseRangeEnd();
        }
    }

    /// <summary>The thumbnail drawn to fill <paramref name="cell"/>, its middle kept where the two shapes differ.</summary>
    private static void DrawFilling(DrawingContext context, Bitmap thumbnail, Rect cell)
    {
        var (width, height) = (thumbnail.PixelSize.Width, (double)thumbnail.PixelSize.Height);
        var scale = Math.Max(cell.Width / width, cell.Height / height);
        var (shownWidth, shownHeight) = (cell.Width / scale, cell.Height / scale);
        var source = new Rect((width - shownWidth) / 2, (height - shownHeight) / 2, shownWidth, shownHeight);
        context.DrawImage(thumbnail, source, cell);
    }

    private void OnChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Follow();
        }
        else
        {
            Dispatcher.UIThread.Post(Follow);
        }
    }

    /// <summary>The thumbnails that have arrived turned into bitmaps, a failure named once, and the strip drawn again.</summary>
    private void Follow()
    {
        if (session is not { } shown)
        {
            return;
        }

        var thumbnails = shown.Thumbnails;
        for (var index = cells.Count; index < thumbnails.Count; index++)
        {
            cells.Add(FrameBitmap.Of(thumbnails[index]));
        }

        if (shown.FramesFailure is { } failure && !ReferenceEquals(named, shown))
        {
            named = shown;
            failed(failure);
        }

        InvalidateVisual();
    }

    /// <summary>Hidden or minimised, the window holds a play going on, as a pause does; shown again, it lets it go on (G6).</summary>
    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty && e.Property != Avalonia.Controls.Window.WindowStateProperty || session is not { Animated: true, IsOpen: true } shown)
        {
            return;
        }

        var hidden = window is null || !window.IsVisible || window is Avalonia.Controls.Window { WindowState: WindowState.Minimized };
        if (hidden && shown.Playing)
        {
            heldWhileHidden = true;
            shown.Pause();
        }
        else if (!hidden && heldWhileHidden)
        {
            heldWhileHidden = false;
            shown.Play();
        }
    }
}
