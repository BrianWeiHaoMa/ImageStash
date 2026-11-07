using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>The badge a tile carries (G6): the file an animated item is handed over as; a still carries none.</summary>
public static class TileBadge
{
    /// <summary>The face the grid draws the badge's words in.</summary>
    // Segoe UI by name (F10): Avalonia 12.1.4 draws a Segoe UI Variable instance name at its default weight, dropping the SemiBold.
    public static readonly Typeface Face = new("Segoe UI", FontStyle.Normal, FontWeight.SemiBold, FontStretch.Normal);

    public static string? Of(StashItem item) =>
        !item.Facts.Animated ? null
        : Path.GetExtension(item.FileName).Equals(".webp", StringComparison.OrdinalIgnoreCase) ? "WEBP" : "GIF";
}

/// <summary>
/// The grid (G6): the listing's items in justified rows, drawn for the rows in view only, each tile its thumbnail over
/// the neutral tile, so transparency shows on it, with its badge, the selection and, while the grid has focus, the
/// focused item's ring. Thumbnails are asked for the rows in view and a margin around them, never for the rest, so
/// 5,000 items show as fast as ten (G1, E4). One animated item plays, the one <see cref="Listing.PlayingItem"/> names
/// among the hovered and the focused tiles in view, and only while the window shows and nothing covers the grid (G6).
/// A click selects, with <c>Ctrl</c> and <c>Shift</c> as G17 says, and moves focus to the grid, as a click on the space
/// between or beyond the tiles does, leaving the selection as it is (G7); a double-click asks for the handoff, and with
/// <c>Ctrl</c> for the preview; a drag from a tile drags the selection out, and a right-click on a tile asks for the
/// item menu (G8, G9, G19).
/// </summary>
public sealed class ThumbnailGrid : Control
{
    /// <summary>How far beyond the view, in viewport heights, thumbnails are asked for ahead of scrolling.</summary>
    private const double AheadOfView = 1;

    /// <summary>How far the pointer moves with the left button down, in device-independent pixels, before a press on a tile becomes a drag (L2).</summary>
    private const double DragDistance = 4;

    /// <summary>The width of the selected tile's ring, in device-independent pixels.</summary>
    private const double RingWidth = 2;

    private static readonly Typeface IconFace = new("Segoe Fluent Icons", FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
    private static readonly IBrush BadgeBack = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));

    private readonly DrawnLook look;
    private readonly Listing listing;
    private readonly Thumbnails<Bitmap> thumbnails;
    private readonly Playback<Bitmap> playback;
    private readonly Func<ThumbnailSize> size;
    private IReadOnlyList<StashItem> laidOutItems = [];
    private double laidOutWidth = double.NaN;
    private double laidOutRowHeight = double.NaN;
    private IReadOnlyList<GridRow> rows = [];
    private ScrollViewer? scroller;
    private ScrollContentPresenter? viewport;
    private TopLevel? window;
    private long? shownFocus;
    private int? shownFocusPlace;

    /// <summary>Where the pointer last was over the grid, in the window's coordinates, which scrolling does not move; null once it left.</summary>
    private Point? pointer;

    private bool playQueued;

    /// <summary>The press on a tile a drag may start from, and where it was, until the button is released or the drag starts.</summary>
    private (PointerPressedEventArgs Press, Point At)? dragFrom;

    /// <summary>The selected tile a press landed on, which becomes the whole selection once the button is released without a drag.</summary>
    private long? collapseTo;

    private bool covered;

    public ThumbnailGrid(Listing listing, Thumbnails<Bitmap> thumbnails, Func<ThumbnailSize> size)
    {
        look = new DrawnLook(this);
        this.listing = listing;
        this.thumbnails = thumbnails;
        this.size = size;
        playback = new Playback<Bitmap>(LocalClock.Instance, HeldFileRetry.Duration, FrameBitmap.Of,
            action => Dispatcher.UIThread.Post(action), bitmap => bitmap.Dispose(), thumbnails.ReadFailed);
        Focusable = true;
        ClipToBounds = false;
        // The focused tile's ring is the visible focus; the default rectangle round the whole grid would hide the edge tiles.
        FocusAdorner = null;
        thumbnails.Arrived += () =>
        {
            InvalidateVisual();
            // A tile just marked as unreadable stops playing.
            QueuePlay();
        };
        playback.Changed += InvalidateVisual;
        listing.Changed += OnListingChanged;
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
        AddHandler(ContextRequestedEvent, OnContextRequested);
    }

    /// <summary>
    /// Raised by a double-click on a tile without <c>Ctrl</c> or <c>Shift</c>, once its first click has made that tile
    /// the whole selection: the handoff's pointer path (G8).
    /// </summary>
    public event Action? TileDoubleClicked;

    /// <summary>
    /// Raised by a double-click on a tile with <c>Ctrl</c>, once it has made that tile the whole selection: the preview's
    /// pointer path (G19).
    /// </summary>
    public event Action? TilePreviewRequested;

    /// <summary>
    /// Raised, with the press, once the pointer has moved a few pixels with the left button still down after a press on
    /// a tile without <c>Ctrl</c> or <c>Shift</c>: the drag-out of the selection as the press left it (G9).
    /// </summary>
    public event Action<PointerPressedEventArgs>? DragStarted;

    /// <summary>Raised, with where to open it in the grid's coordinates, once the selection is the one the item menu acts on (G8).</summary>
    public event Action<Rect>? ItemMenuRequested;

    /// <summary>The rows as last laid out, which the arrow keys move through (Listing.MoveFocus).</summary>
    public IReadOnlyList<GridRow> Rows => rows;

    /// <summary>Set while the preview covers the grid, whose animation is then the one playing, so no tile plays (G6).</summary>
    public bool Covered
    {
        get => covered;
        set
        {
            covered = value;
            QueuePlay();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        scroller = this.FindAncestorOfType<ScrollViewer>();
        viewport = this.FindAncestorOfType<ScrollContentPresenter>();
        if (scroller is not null)
        {
            scroller.ScrollChanged += OnScrolled;
        }

        window = TopLevel.GetTopLevel(this);
        if (window is not null)
        {
            window.PropertyChanged += OnWindowChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (scroller is not null)
        {
            scroller.ScrollChanged -= OnScrolled;
        }

        if (window is not null)
        {
            window.PropertyChanged -= OnWindowChanged;
        }

        scroller = null;
        viewport = null;
        window = null;
        playback.Play(null);
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// An import is replacing the stash (G36): the play stopped and every thumbnail and read let go; completes once no
    /// file of the stash is open for the grid. Nothing is asked for or played again until the listing has a stash open.
    /// </summary>
    public Task LetGo()
    {
        playback.Play(null);
        thumbnails.LetGo();
        return Task.WhenAll(playback.Idle(), thumbnails.Idle());
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : laidOutWidth;
        LayOut(double.IsFinite(width) ? width : 0);
        return new Size(Math.Max(0, width), rows.Count == 0 ? 0 : rows[^1].Y + rows[^1].Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        LayOut(finalSize.Width);
        Dispatcher.UIThread.Post(AskForThumbnails, DispatcherPriority.Background);
        QueuePlay();
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        var (top, height) = View();
        var (first, last) = JustifiedRows.InView(rows, top, height, 0);
        var corner = look.Radius(LookKeys.TileRadius);
        var neutral = look.Brush(LookKeys.TileGround);
        var accent = new Pen(look.Brush(LookKeys.SelectionRing), RingWidth);
        // Held inside the space between tiles, so a selected tile's ring never lies over its neighbour (G6).
        var standOff = Math.Clamp(look.Width(LookKeys.SelectionRingGap), 1, JustifiedRows.Gap - RingWidth - 0.5);
        // Keyboard focus fills the gap, beside the tile and inside the selection ring: on the window's ground, so it
        // shows on any picture, and apart from the selection's mark (G7).
        var ring = new Pen(look.Brush(LookKeys.FocusRing), standOff);
        var mark = look.Brush(LookKeys.TileMark);
        var badgeCorner = look.Radius(LookKeys.SmallRadius);
        var step = size();
        // The ground, before the tiles: Avalonia hit-tests a control by what it drew, so a click between or beyond the
        // tiles reaches the grid only where something, if only the transparent brush, was drawn (G7).
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        var selected = new List<Rect>();
        Rect? focused = null;
        for (var r = first; r <= last; r++)
        {
            foreach (var tile in rows[r].Tiles)
            {
                var item = laidOutItems[tile.Index];
                var box = new Rect(tile.X, tile.Y, tile.Width, tile.Height);
                var rounded = new RoundedRect(box, corner);
                context.DrawRectangle(neutral, null, rounded);
                var key = ThumbnailKey.Of(item, step);
                if ((playback.Frame(key) ?? thumbnails.Get(key)) is { } picture)
                {
                    using (context.PushClip(rounded))
                    {
                        context.DrawImage(picture, JustifiedRows.Cover(picture.PixelSize.Width, picture.PixelSize.Height, box.Width, box.Height), box);
                    }
                }
                else if (thumbnails.CouldNotRead(key))
                {
                    var glyph = Text("", IconFace, 16, mark);
                    context.DrawText(glyph, box.Center - new Point(glyph.Width / 2, glyph.Height / 2));
                }

                if (TileBadge.Of(item) is { } badge)
                {
                    var label = Text(badge, TileBadge.Face, 11, Brushes.White);
                    var back = new Rect(box.X + 6, box.Y + 6, label.Width + 10, label.Height + 2);
                    context.DrawRectangle(BadgeBack, null, new RoundedRect(back, badgeCorner));
                    context.DrawText(label, back.TopLeft + new Point(5, 1));
                }

                if (listing.Selection.Contains(item.Id))
                {
                    selected.Add(box);
                }

                if (IsFocused && listing.Focused == item.Id)
                {
                    focused = box;
                }
            }
        }

        // After every tile, and the focus ring last: two selected tiles side by side share the space between them, and
        // neither's ring then covers the other's focus mark (G7).
        foreach (var box in selected)
        {
            var reach = standOff + RingWidth / 2;
            context.DrawRectangle(null, accent, new RoundedRect(box.Inflate(reach), corner + reach));
        }

        if (focused is { } inFocus)
        {
            var reach = standOff / 2;
            context.DrawRectangle(null, ring, new RoundedRect(inFocus.Inflate(reach), corner + reach));
        }
    }

    /// <summary>
    /// A left press on the grid, on a tile or its blank space, or on the margin round it inside its scroller, gives it
    /// focus and changes nothing else (G7); whether it was one.
    /// </summary>
    public bool TakeFocusFrom(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return false;
        }

        Focus(NavigationMethod.Pointer);
        return true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        dragFrom = null;
        collapseTo = null;
        if (!TakeFocusFrom(e))
        {
            return;
        }

        if (TileAt(e.GetPosition(this)) is { } tile)
        {
            if (e.ClickCount == 2 && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // Its first click toggled the tile; the preview shows that tile alone, whichever way the toggle went.
                listing.Focus(tile.Id);
                TilePreviewRequested?.Invoke();
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                listing.Toggle(tile.Id);
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                listing.ExtendTo(tile.Id);
            }
            else if (e.ClickCount == 2)
            {
                listing.Focus(tile.Id);
                TileDoubleClicked?.Invoke();
            }
            else
            {
                // A press on a selected tile keeps the selection, so several selected tiles drag out together; released
                // without a drag, it makes that tile the whole selection, as a press on any other tile does at once (G9, G17).
                if (listing.Selection.Contains(tile.Id))
                {
                    collapseTo = tile.Id;
                }
                else
                {
                    listing.Focus(tile.Id);
                }

                dragFrom = (e, e.GetPosition(this));
            }
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        pointer = window is null ? null : e.GetPosition(window);
        QueuePlay();
        if (dragFrom is { } from && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var moved = e.GetPosition(this) - from.At;
            if (Math.Abs(moved.X) >= DragDistance || Math.Abs(moved.Y) >= DragDistance)
            {
                dragFrom = null;
                collapseTo = null;
                DragStarted?.Invoke(from.Press);
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        if (collapseTo is { } id)
        {
            listing.Focus(id);
        }

        dragFrom = null;
        collapseTo = null;
    }

    /// <summary>
    /// A right-click on a tile, or the menu key while the grid has focus, asks for the item menu at that tile: a tile
    /// outside the selection becomes the whole selection first, and a selected one leaves it as it is, so the menu acts
    /// on what it shows (G8). Empty space has no menu.
    /// </summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var fromPointer = e.TryGetPosition(this, out var at);
        var tile = fromPointer ? TileAt(at) : rows.SelectMany(row => row.Tiles).FirstOrDefault(tile => tile.Id == listing.Focused);
        if (tile is null)
        {
            return;
        }

        e.Handled = true;
        if (!listing.Selection.Contains(tile.Id))
        {
            listing.Focus(tile.Id);
        }

        Focus(NavigationMethod.Pointer);
        // A pixel at the pointer, never an empty rectangle: Avalonia cuts the anchor to the grid's bounds, and what that
        // leaves of an empty one is the grid's top-left corner.
        ItemMenuRequested?.Invoke(fromPointer ? new Rect(at, new Size(1, 1)) : new Rect(tile.X, tile.Y, tile.Width, tile.Height));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        pointer = null;
        QueuePlay();
    }

    private void OnListingChanged()
    {
        InvalidateMeasure();
        InvalidateVisual();
        // A use moves the selected item to the top of Recent, and the selection stays what shows (G1, G7); any lesser
        // shift, as an item added before it, leaves the view where the user scrolled it.
        var place = listing.Focused is { } focused ? listing.PlaceOf(focused) : null;
        var movedToTheTop = place == 0 && shownFocusPlace is > 0;
        shownFocusPlace = place;
        if (listing.Focused != shownFocus || movedToTheTop)
        {
            shownFocus = listing.Focused;
            Dispatcher.UIThread.Post(BringFocusedIntoView, DispatcherPriority.Background);
        }

        QueuePlay();
    }

    private void OnScrolled(object? sender, ScrollChangedEventArgs e)
    {
        InvalidateVisual();
        AskForThumbnails();
        QueuePlay();
    }

    /// <summary>Hidden, shown or minimised, the window starts or stops the play.</summary>
    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == Avalonia.Controls.Window.WindowStateProperty)
        {
            QueuePlay();
        }
    }

    /// <summary>Settles which item plays once the events in hand have run, so it sees the window as they leave it.</summary>
    private void QueuePlay()
    {
        if (playQueued)
        {
            return;
        }

        playQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            playQueued = false;
            playback.Play(PlayRequest());
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// The one item to play (G6): the hovered tile, else the focused one, when animated, in view and readable; none
    /// while the grid does not show, its window hidden, minimised, covered by a state of the stash or by the preview.
    /// </summary>
    private ThumbnailRequest? PlayRequest()
    {
        if (covered || window is null || !window.IsVisible || window is Avalonia.Controls.Window { WindowState: WindowState.Minimized } || !IsEffectivelyVisible
            || LaidOutStale)
        {
            return null;
        }

        var (top, height) = View();
        var (first, last) = JustifiedRows.InView(rows, top, height, 0);
        var step = size();
        StashItem? Playable(GridTile? tile) =>
            tile is null || tile.Index >= laidOutItems.Count || thumbnails.CouldNotRead(ThumbnailKey.Of(laidOutItems[tile.Index], step))
                ? null
                : laidOutItems[tile.Index];
        var inView = Enumerable.Range(first, Math.Max(0, last - first + 1)).SelectMany(r => rows[r].Tiles);
        var hovered = pointer is { } at && window.TranslatePoint(at, this) is { } here ? TileAt(here) : null;
        var focused = listing.Focused is { } id ? inView.FirstOrDefault(tile => tile.Id == id) : null;
        var playing = Listing.PlayingItem(Playable(hovered), Playable(focused));
        return playing is null ? null : Request(laidOutItems.First(item => item.Id == playing), step);
    }

    private void LayOut(double width)
    {
        var rowHeight = JustifiedRows.RowHeight(size());
        if (ReferenceEquals(laidOutItems, listing.Items) && width == laidOutWidth && rowHeight == laidOutRowHeight)
        {
            return;
        }

        laidOutItems = listing.Items;
        laidOutWidth = width;
        laidOutRowHeight = rowHeight;
        rows = JustifiedRows.Layout(laidOutItems, Math.Max(1, width), rowHeight);
    }

    /// <summary>
    /// The rows laid out are of items the listing no longer holds, until the next layout: one of them may be of a stash an
    /// import is replacing, whose files nothing may open (G36), so nothing is asked for or played from them.
    /// </summary>
    private bool LaidOutStale => !ReferenceEquals(laidOutItems, listing.Items);

    /// <summary>Asks for the thumbnails of the rows in view first, then of those just below and above.</summary>
    private void AskForThumbnails()
    {
        if (LaidOutStale)
        {
            return;
        }

        var (top, height) = View();
        var (first, last) = JustifiedRows.InView(rows, top, height, 0);
        var (from, to) = JustifiedRows.InView(rows, top, height, height * AheadOfView);
        var order = Enumerable.Range(first, Math.Max(0, last - first + 1))
            .Concat(Enumerable.Range(last + 1, Math.Max(0, to - last)))
            .Concat(Enumerable.Range(from, Math.Max(0, Math.Min(first, to + 1) - from)).Reverse());
        var step = size();
        thumbnails.Want(order
            .SelectMany(r => rows[r].Tiles)
            .Select(tile => Request(laidOutItems[tile.Index], step))
            .ToList());
    }

    /// <summary>An item's picture at the bound its tile needs at this size step, as a thumbnail and as the play ask for it.</summary>
    private ThumbnailRequest Request(StashItem item, ThumbnailSize step) =>
        new(ThumbnailKey.Of(item, step), item.FilePath, item.Name,
            Thumbnails<Bitmap>.Bound(item.Facts, JustifiedRows.RowHeight(step), TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));

    private void BringFocusedIntoView()
    {
        if (listing.Focused is not { } focused)
        {
            return;
        }

        foreach (var row in rows)
        {
            if (row.Tiles.FirstOrDefault(tile => tile.Id == focused) is { } tile)
            {
                this.BringIntoView(new Rect(tile.X, tile.Y, tile.Width, tile.Height).Inflate(JustifiedRows.Gap));
                return;
            }
        }
    }

    private GridTile? TileAt(Point point)
    {
        var (first, last) = JustifiedRows.InView(rows, point.Y, 0, 0);
        for (var r = first; r <= last; r++)
        {
            if (rows[r].Tiles.FirstOrDefault(tile => tile.X <= point.X && point.X < tile.X + tile.Width && tile.Y <= point.Y && point.Y < tile.Y + tile.Height) is { } tile)
            {
                return tile;
            }
        }

        return null;
    }

    /// <summary>
    /// The visible band of the grid, in its own coordinates: the scroller's viewport translated into the grid. Never the
    /// offset less the grid's top: the presenter places the grid at the negated offset, so that counts the scroll twice.
    /// </summary>
    private (double Top, double Height) View() =>
        viewport?.TranslatePoint(default, this) is { } top ? (top.Y, viewport.Bounds.Height) : (0, Bounds.Height);

    private static FormattedText Text(string text, Typeface face, double emSize, IBrush? brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, emSize, brush);
}
