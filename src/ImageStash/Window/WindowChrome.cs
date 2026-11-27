using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The stash window's frame in its two states and where it is placed (G1, G2): the flyout, with no frame of its own but
/// the edge it draws (<see cref="FlyoutEdge"/>), above other windows but its own Settings page while that is active
/// (<see cref="FlyoutTopmost"/>), and without a taskbar button; the pinned window, with a title bar drawn without the
/// icon and a taskbar button, at the position the user last gave it, or the first pin gave it where the flyout
/// stood. Each is resized by dragging its border, and each size is remembered apart from the other's: the flyout's for
/// every later flyout, the pinned window's for every later pin. In either state never smaller than the window's minimum,
/// at which the editor fits whole (G20, G21). Only a resize or move the user makes is remembered, and the first pin's
/// place: the chrome's own placements are marked while they run. It
/// shows, hides and places the window and never activates it or moves its focus, which stay the window's own (G8).
/// </summary>
public sealed class WindowChrome
{
    private readonly Avalonia.Controls.Window window;
    private readonly SettingsKeeper settings;
    private readonly WindowPin pin;
    private readonly FlyoutEdge edge;
    private readonly IQuietWindows quietWindows;
    private readonly FlyoutTopmost topmost = new();
    private bool pinned;
    private bool placing;

    public WindowChrome(Avalonia.Controls.Window window, SettingsKeeper settings, WindowPin pin, IQuietWindows quietWindows)
    {
        this.window = window;
        this.settings = settings;
        this.pin = pin;
        this.quietWindows = quietWindows;
        edge = new FlyoutEdge(window.BeginResizeDrag);
        // Rounded as Windows 11 rounds its windows, in either state, the flyout's edge following the same corner (F10).
        quietWindows.RoundCorners(window);
        // In force from the start, so every way the window is sized stops at it: a placement, a drag of the border, a
        // size set directly (G20, G21); lowered only while the window is on a display too small for it (G1).
        window.MinWidth = WindowSize.Minimum.Width;
        window.MinHeight = WindowSize.Minimum.Height;
        window.Resized += (_, e) =>
        {
            // A drag of the border, a snap or any other resize the user makes; never one of the window's own placements,
            // which narrow it beside a caret, hold it to a small display, raise it to the minimum or change its frame.
            if (!placing && e.Reason is (WindowResizeReason.User or WindowResizeReason.Unspecified) && window.WindowState == WindowState.Normal)
            {
                // The whole window, frame included, as Place puts it back.
                var outer = window.FrameSize ?? e.ClientSize;
                settings.Change(s => Remembered(s, pinned, new WindowSize(outer.Width, outer.Height)));
            }
        };
        window.PositionChanged += (_, e) =>
        {
            // Windows moves a window it minimises off every display before Avalonia reads the state as minimised, so a
            // place on no display is that move, never the user's: remembered, the next pin would show on the primary (G2).
            var outer = PixelSize.FromSize(window.FrameSize ?? window.ClientSize, window.RenderScaling);
            var onADisplay = Displays().Any(display => display.Bounds.Intersects(new PixelRect(e.Point, outer)));
            if (pinned && !placing && window.IsVisible && window.WindowState == WindowState.Normal && onADisplay)
            {
                settings.Change(s => s with { PinnedPosition = new WindowPosition(e.Point.X, e.Point.Y) });
            }

            // Moved by the user onto another display, it takes that display's minimum, as a placement there gives it.
            if (!placing && window.IsVisible && window.WindowState != WindowState.Minimized && onADisplay)
            {
                Bound(Summon.DisplayAt(new PixelRect(e.Point, outer).Center, Displays()));
            }
        };
    }

    /// <summary>The flyout's edge and the grips its border is dragged by, laid over the whole window.</summary>
    public Control Edge => edge.Layer;

    /// <summary>
    /// <paramref name="settings"/> with <paramref name="size"/>, the whole window in device-independent units, kept as the
    /// size of the state it was given in, the other state's left as it was (G2: remembered separately).
    /// </summary>
    public static Settings Remembered(Settings settings, bool pinned, WindowSize size) =>
        pinned ? settings with { PinnedSize = size } : settings with { FlyoutSize = size };

    /// <summary>Whether the window is pinned: it stays on losing focus, on Esc and after a paste (G2).</summary>
    public bool Pinned => pinned;

    /// <summary>
    /// The pin button: the pinned frame, at the pinned window's remembered place and size; the first time, with no place
    /// remembered, where the flyout stands, and that place is the pinned window's from then on (G2).
    /// </summary>
    public void Pin()
    {
        // Read before the frame changes: the flyout has no frame, so the window Windows reports is the flyout seen.
        var flyout = Outer();
        var place = Summon.Pinned(settings.Current.PinnedPosition, Displays(), PinnedSize(), flyout);
        Apply(pinnedNow: true);
        PlaceSettled(place);
        if (settings.Current.PinnedPosition is null)
        {
            settings.Change(s => s with { PinnedPosition = new WindowPosition(place.X, place.Y) });
        }
    }

    /// <summary>The pin button again: the window stays showing, as a flyout where it is, at the flyout's size; its activation is the caller's.</summary>
    public void Unpin()
    {
        var at = window.Position;
        // Losing the taskbar button gives the window a hidden owner, which hands its activation to another app; done
        // while hidden and shown again, the flyout keeps showing with focus, as Avalonia does for the other direction.
        placing = true;
        window.Hide();
        placing = false;
        Apply(pinnedNow: false);
        ShowAt(Summon.Unpinned(at, Displays(), settings.Current.FlyoutSize.UsableOr(WindowSize.FlyoutDefault)));
    }

    /// <summary>
    /// Where the pinned window shows with no flyout showing, as at the first run: where the user last left it, at its
    /// remembered size, else centred on the primary display (G2).
    /// </summary>
    public PixelRect PinnedPlace() => Summon.Pinned(settings.Current.PinnedPosition, Displays(), PinnedSize());

    private WindowSize PinnedSize() => settings.Current.PinnedSize.UsableOr(WindowSize.PinnedDefault);

    /// <summary>The pinned window's frame, or the flyout's, with the pin button showing which (G2).</summary>
    public void Apply(bool pinnedNow)
    {
        // Changing the frame resizes and moves the window at once; that is not the user placing the pinned window.
        placing = true;
        pinned = pinnedNow;
        // The flyout has no frame: a resizing one would add borders Windows does not draw, so the window it reports would
        // not be the flyout a person sees, and a band along its top. Its edge and grips are its own, over its content.
        window.WindowDecorations = pinned ? WindowDecorations.Full : WindowDecorations.None;
        if (pinned)
        {
            // Asked at every change to the titled frame, which Windows may draw afresh: the first pinned show, Pin, and
            // Pin again after Unpin.
            quietWindows.PlainTitleBar(window);
        }

        window.CanResize = true;
        edge.Shown = !pinned;
        window.CanMinimize = pinned;
        window.CanMaximize = pinned;
        window.ShowInTaskbar = pinned;
        topmost.Framed(pinned);
        window.Topmost = topmost.Topmost;
        pin.Show(pinned);
        placing = false;
    }

    /// <summary>
    /// ImageStash's Settings page, <paramref name="page"/>, became the active window: the flyout no longer stands above
    /// it. Windows puts a window leaving the topmost ones above every window that is not, the page included, so the page
    /// is put back on top of the flyout, neither activated.
    /// </summary>
    public void GiveWayTo(Avalonia.Controls.Window page)
    {
        var wasAbove = window.Topmost;
        topmost.PageActivated();
        window.Topmost = topmost.Topmost;
        if (wasAbove && !window.Topmost && window.IsVisible && page.IsVisible)
        {
            // Avalonia applies a change of Topmost through SetWindowPos without activating; set and cleared, the page
            // goes on top of every window that is not topmost, the flyout now among them.
            page.Topmost = true;
            page.Topmost = false;
        }
    }

    /// <summary>The stash window itself became the active window: the flyout above every other window again.</summary>
    public void WindowActivated()
    {
        topmost.WindowActivated();
        window.Topmost = topmost.Topmost;
    }

    /// <summary>The Settings page closed: the flyout above every other window again.</summary>
    public void PageClosed()
    {
        topmost.PageClosed();
        window.Topmost = topmost.Topmost;
    }

    /// <summary>The whole window, frame included, on the desktop in physical pixels.</summary>
    public PixelRect Outer() => new(window.Position, PixelSize.FromSize(window.FrameSize ?? window.ClientSize, window.RenderScaling));

    /// <summary>Shows the window placed on <paramref name="place"/>, without activating it.</summary>
    public void ShowAt(PixelRect place)
    {
        Place(place);
        placing = true;
        window.Show();
        placing = false;
        // Placed again once shown: a window that moved to a display of another scaling sizes itself by that scaling only now.
        PlaceSettled(place);
    }

    /// <summary>
    /// Places the window now and once more after the pending layout, because a frame just changed reports its new size
    /// only then, and the first placement allowed for the old one.
    /// </summary>
    private void PlaceSettled(PixelRect place)
    {
        Place(place);
        Dispatcher.UIThread.Post(() => Place(place), DispatcherPriority.Background);
    }

    /// <summary>Puts the whole window, frame included, on the given physical rectangle, grown to the window's minimum where it is smaller.</summary>
    private void Place(PixelRect place)
    {
        placing = true;
        var scaling = window.RenderScaling;
        var frame = Frame();
        var displays = Displays();
        var least = Bound(Summon.DisplayAt(place.Center, displays));
        var width = Math.Max(least.Width, place.Width / scaling - frame.Width);
        var height = Math.Max(least.Height, place.Height / scaling - frame.Height);
        // The minimum is the client's, so a place sized to it grows by the frame, and is moved to stay on screen (G1).
        window.Position = Summon.Grown(place, PixelSize.FromSize(new Size(width + frame.Width, height + frame.Height), scaling), displays).Position;
        window.Width = width;
        window.Height = height;
        placing = false;
    }

    /// <summary>Puts in force the window's minimum on <paramref name="display"/>, which the window takes no smaller, and answers it.</summary>
    private WindowSize Bound(Display display)
    {
        var least = Summon.MinimumOn(display, Frame());
        window.MinWidth = least.Width;
        window.MinHeight = least.Height;
        return least;
    }

    /// <summary>The frame round the client in device-independent units, once the window shows and reports it; none before.</summary>
    private Size Frame() => window.FrameSize is { } outer && window.IsVisible ? outer - window.ClientSize : default;

    /// <summary>The displays the window is placed on, in physical pixels.</summary>
    public IReadOnlyList<Display> Displays() => Summon.DisplaysOf(window.Screens);
}
