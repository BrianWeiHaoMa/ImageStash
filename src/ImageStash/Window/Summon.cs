using Avalonia;
using Avalonia.Controls;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Where the open shortcut's flyout is placed from, in physical pixels (G1): the pointer at the press, and the text caret
/// of the focused control in the app the user came from; either null when it could not be found; and, read only when the
/// pointer could not be, the window in front at the press, whose display is the active one.
/// </summary>
public sealed record SummonAnchor(PixelPoint? Pointer, PixelRect? Caret = null, PixelRect? FrontWindow = null);

/// <summary>What Esc does: an open tag list closes first, then the preview, then the search clears, then the flyout hides (G2).</summary>
public enum EscapeAction
{
    ClosePicker,
    ClosePreview,
    ClearSearch,
    Hide,
    Nothing,
}

/// <summary>
/// Where the summon shows the window, and when it hides (G1, G2): the rules as arithmetic on physical pixels, so a
/// window sized in device-independent units lands whole on a display of any scaling (E1). A size is raised to the
/// window's minimum, scaled by the target display's scaling, and reduced to fit its working area, only for that
/// showing; the remembered size stays.
/// </summary>
public static class Summon
{
    /// <summary>The space between the flyout and the tray icon, in device-independent units.</summary>
    public const double IconGap = 12;

    /// <summary>
    /// The strip along the pinned window's top edge the user grabs it by, its title bar's height, in device-independent
    /// units: fixed, since the frame Windows gives it is known only once it shows.
    /// </summary>
    public const double TitleStrip = 32;

    /// <summary>
    /// The flyout summoned from the tray (G1): above the tray icon, or below it when the taskbar is at the top,
    /// centred on the icon where the display allows, and always wholly inside the working area of the icon's display.
    /// With no icon rectangle, centred on the primary display's working area.
    /// </summary>
    public static PixelRect AboveTrayIcon(PixelRect? icon, IReadOnlyList<Display> displays, WindowSize size)
    {
        if (icon is not { } at)
        {
            return Centred(Primary(displays), size);
        }

        var display = DisplayAt(at.Center, displays);
        return AboveIcon(at, display, SizeOn(display, size));
    }

    /// <summary>The flyout's left edge left of the caret's, in device-independent units.</summary>
    public const double CaretInset = 28;

    /// <summary>The space between the flyout and the caret's line, in device-independent units.</summary>
    public const double CaretGap = 8;

    /// <summary>
    /// The flyout summoned by the open shortcut (G1): beside the caret, its left edge <see cref="CaretInset"/> left of
    /// the caret's and its bottom <see cref="CaretGap"/> above the caret's top, or that gap below the caret's bottom when
    /// the working area above has no room for it; when neither has, that gap right of the caret, else left of it,
    /// centred on its line, narrowed to the wider side's room when neither side has room for its width, so the caret
    /// stays in sight, but never below the window's minimum width, over the caret when no side has room for even that;
    /// without a caret, its top-left corner at the pointer; without either, centred on the active
    /// display, the one of the window in front at the press, else the primary display. Always moved the least distance
    /// that puts it wholly inside the working area of the caret's or the pointer's display, and sized by that display's
    /// scaling.
    /// </summary>
    public static PixelRect AtAnchor(SummonAnchor anchor, IReadOnlyList<Display> displays, WindowSize size)
    {
        if (anchor.Caret is { } caret)
        {
            return BesideCaret(caret, displays, size);
        }

        if (anchor.Pointer is not { } pointer)
        {
            return Centred(anchor.FrontWindow is { } front ? DisplayAt(front.Center, displays) : Primary(displays), size);
        }

        return TopLeftAt(pointer, displays, size);
    }

    /// <summary>
    /// A notification (G16): above the tray icon when its rectangle lies on the primary display, else above that
    /// display's bottom-right working-area corner, and always wholly inside the primary display's working area.
    /// </summary>
    public static PixelRect NotificationPlace(PixelRect? icon, IReadOnlyList<Display> displays, WindowSize size)
    {
        var primary = Primary(displays);
        // Sized to its words alone: a notification never holds the editor, so the window's minimum is not its own.
        var (width, height) = Scaled(primary, size);
        if (icon is { } at && primary.Bounds.Contains(at.Center))
        {
            return AboveIcon(at, primary, (width, height));
        }

        var gap = (int)Math.Round(IconGap * primary.Scaling);
        var area = primary.WorkingArea;
        return Inside(area, new PixelRect(area.Right - gap - width, area.Bottom - gap - height, width, height));
    }

    /// <summary>
    /// The pinned window shown afresh: where the user last left it, or centred on the primary display when it has not
    /// been placed; a place whose title strip lies on no display's working area, as when the display it was left on is
    /// gone, is brought wholly onto the nearest one (G1, G2). A place the user left across a display's edge with its title
    /// bar reachable is kept.
    /// </summary>
    public static PixelRect Pinned(WindowPosition? position, IReadOnlyList<Display> displays, WindowSize size)
    {
        if (position is null)
        {
            return Centred(Primary(displays), size);
        }

        var at = new PixelPoint(position.X, position.Y);
        var display = DisplayAt(at, displays);
        var (width, height) = SizeOn(display, size);
        var placed = new PixelRect(at.X, at.Y, width, height);
        // Windows scales a window by the display holding most of it, which sets its title bar's height.
        var holding = displays.MaxBy(d => Area(d.Bounds.Intersect(placed)))!;
        var scaling = Area(holding.Bounds.Intersect(placed)) > 0 ? holding.Scaling : display.Scaling;
        var strip = new PixelRect(at.X, at.Y, width, Math.Min(height, (int)Math.Ceiling(TitleStrip * scaling)));
        return displays.Any(d => d.WorkingArea.Intersects(strip)) ? placed : Inside(display.WorkingArea, placed);
    }

    /// <summary>Unpinned by its button: a flyout where the window is, at the flyout size, wholly on its display.</summary>
    public static PixelRect Unpinned(PixelPoint at, IReadOnlyList<Display> displays, WindowSize size) => TopLeftAt(at, displays, size);

    /// <summary>
    /// The smallest client the window takes on <paramref name="display"/>, in device-independent units: the window's
    /// minimum, lowered on a display whose working area, less the window's <paramref name="frame"/>, is smaller, so the
    /// window is still wholly on screen there (G1), where the editor's rows may not fit (G20, G21).
    /// </summary>
    public static WindowSize MinimumOn(Display display, Size frame) => new(
        Math.Min(WindowSize.Minimum.Width, Math.Max(1, display.WorkingArea.Width / display.Scaling - frame.Width)),
        Math.Min(WindowSize.Minimum.Height, Math.Max(1, display.WorkingArea.Height / display.Scaling - frame.Height)));

    /// <summary>
    /// Where the window lies once it has its real <paramref name="size"/>, frame included, which exceeds a place sized to
    /// the minimum by the frame, since the minimum is the client's: a place wholly inside a display's working area is
    /// moved the least distance that keeps the whole window inside it (G1); any other place, as a pinned window the user
    /// left across a display's edge, keeps its corner (G2).
    /// </summary>
    public static PixelRect Grown(PixelRect place, PixelSize size, IReadOnlyList<Display> displays)
    {
        var grown = new PixelRect(place.Position, size);
        return displays.FirstOrDefault(d => d.WorkingArea.Contains(place)) is { } holding ? Inside(holding.WorkingArea, grown) : grown;
    }

    /// <summary>The displays as <paramref name="screens"/> reports them, or one 1280×720 display when it reports none.</summary>
    public static IReadOnlyList<Display> DisplaysOf(Screens screens)
    {
        var displays = screens.All.Select(s => new Display(s.Bounds, s.WorkingArea, s.Scaling, s.IsPrimary)).ToList();
        return displays.Count > 0 ? displays : [new Display(new PixelRect(0, 0, 1280, 720), new PixelRect(0, 0, 1280, 720), 1, true)];
    }

    /// <summary>The display whose bounds hold <paramref name="point"/>, else the nearest.</summary>
    public static Display DisplayAt(PixelPoint point, IReadOnlyList<Display> displays) =>
        displays.FirstOrDefault(d => d.Bounds.Contains(point)) ?? displays.MinBy(d => Distance(d.Bounds, point))!;

    /// <summary>
    /// Losing focus to another app, the open shortcut and Enter's paste hide the flyout, unless a dialog it opened is up,
    /// a drag it started is in progress or an edit is open; none hides the pinned window (G2).
    /// </summary>
    public static bool FlyoutHides(bool pinned, bool dialogUp, bool dragInProgress, bool editOpen) => !pinned && !dialogUp && !dragInProgress && !editOpen;

    /// <summary>Esc closes an open tag list and nothing else, else the preview, else clears the search, else hides the flyout; pinned, it goes no further (G2).</summary>
    public static EscapeAction OnEscape(string searchText, bool pinned, bool previewOpen, bool pickerOpen = false) =>
        pickerOpen ? EscapeAction.ClosePicker
        : previewOpen ? EscapeAction.ClosePreview
        : searchText.Length > 0 ? EscapeAction.ClearSearch
        : pinned ? EscapeAction.Nothing
        : EscapeAction.Hide;

    private static Display Primary(IReadOnlyList<Display> displays) => displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];

    /// <summary>The size on the display under <paramref name="at"/>, its top-left corner there, wholly inside that display's working area.</summary>
    private static PixelRect TopLeftAt(PixelPoint at, IReadOnlyList<Display> displays, WindowSize size)
    {
        var display = DisplayAt(at, displays);
        var (width, height) = SizeOn(display, size);
        return Inside(display.WorkingArea, new PixelRect(at.X, at.Y, width, height));
    }

    private static PixelRect BesideCaret(PixelRect caret, IReadOnlyList<Display> displays, WindowSize size)
    {
        var display = DisplayAt(caret.TopLeft, displays);
        var (width, height) = SizeOn(display, size);
        var inset = (int)Math.Round(CaretInset * display.Scaling);
        var gap = (int)Math.Round(CaretGap * display.Scaling);
        var area = display.WorkingArea;
        var above = caret.Y - gap - height;
        var below = caret.Bottom + gap;
        if (above >= area.Y)
        {
            return Inside(area, new PixelRect(caret.X - inset, above, width, height));
        }

        if (below + height <= area.Bottom)
        {
            return Inside(area, new PixelRect(caret.X - inset, below, width, height));
        }

        // Taller than the room above and below the caret's line: beside it, so the caret stays in sight.
        var side = caret.Y + caret.Height / 2 - height / 2;
        if (caret.Right + gap + width <= area.Right)
        {
            return Inside(area, new PixelRect(caret.Right + gap, side, width, height));
        }

        if (caret.X - gap - width >= area.X)
        {
            return Inside(area, new PixelRect(caret.X - gap - width, side, width, height));
        }

        // Wider than the room on either side too, as on a small display scaled up: narrowed, for this showing only, to
        // the wider side's room, so the caret stays in sight and the flyout wholly on screen; never below the window's
        // minimum width, which then lies over the caret, since the editor's rows need that width (G1, G20, G21).
        var least = Math.Min(width, (int)Math.Round(WindowSize.Minimum.Width * display.Scaling));
        var rightRoom = area.Right - (caret.Right + gap);
        var leftRoom = caret.X - gap - area.X;
        return rightRoom >= leftRoom
            ? Inside(area, new PixelRect(caret.Right + gap, side, Math.Max(least, rightRoom), height))
            : Inside(area, new PixelRect(area.X, side, Math.Max(least, leftRoom), height));
    }

    /// <summary>
    /// <paramref name="size"/>, in physical pixels, above <paramref name="icon"/> on <paramref name="display"/>, or below
    /// it when the taskbar is at the top, centred on the icon where the display allows, wholly inside the working area.
    /// </summary>
    private static PixelRect AboveIcon(PixelRect icon, Display display, (int Width, int Height) size)
    {
        var gap = (int)Math.Round(IconGap * display.Scaling);
        var taskbarBelow = icon.Center.Y >= display.Bounds.Center.Y;
        var x = icon.Center.X - size.Width / 2;
        var y = taskbarBelow ? icon.Y - gap - size.Height : icon.Bottom + gap;
        return Inside(display.WorkingArea, new PixelRect(x, y, size.Width, size.Height));
    }

    private static PixelRect Centred(Display display, WindowSize size)
    {
        var (width, height) = SizeOn(display, size);
        var area = display.WorkingArea;
        return new PixelRect(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
    }

    /// <summary>
    /// The window's size in physical pixels on <paramref name="display"/>: raised to the window's minimum, below which it
    /// takes no size (G20, G21), and no larger than the working area, as <see cref="MinimumOn"/> yields to it.
    /// </summary>
    private static (int Width, int Height) SizeOn(Display display, WindowSize size) =>
        Scaled(display, new WindowSize(Math.Max(size.Width, WindowSize.Minimum.Width), Math.Max(size.Height, WindowSize.Minimum.Height)));

    /// <summary>The size in physical pixels on <paramref name="display"/>, no larger than its working area.</summary>
    private static (int Width, int Height) Scaled(Display display, WindowSize size) => (
        Math.Clamp((int)Math.Round(size.Width * display.Scaling), 1, Math.Max(1, display.WorkingArea.Width)),
        Math.Clamp((int)Math.Round(size.Height * display.Scaling), 1, Math.Max(1, display.WorkingArea.Height)));

    /// <summary>
    /// Moves <paramref name="rect"/> the least distance that puts it wholly inside <paramref name="area"/>; a rectangle
    /// larger than the area, by a pixel's rounding, at the area's left or top edge.
    /// </summary>
    private static PixelRect Inside(PixelRect area, PixelRect rect) => new(
        Math.Max(area.X, Math.Min(rect.X, area.Right - rect.Width)),
        Math.Max(area.Y, Math.Min(rect.Y, area.Bottom - rect.Height)),
        rect.Width,
        rect.Height);

    private static long Area(PixelRect rect) => (long)rect.Width * rect.Height;

    private static double Distance(PixelRect bounds, PixelPoint point)
    {
        var dx = Math.Max(0, Math.Max(bounds.X - point.X, point.X - bounds.Right));
        var dy = Math.Max(0, Math.Max(bounds.Y - point.Y, point.Y - bounds.Bottom));
        return Math.Sqrt((double)dx * dx + (double)dy * dy);
    }
}
