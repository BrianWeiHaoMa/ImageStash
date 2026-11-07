using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using ImageStash.Look;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The stash window's frame in its two states and where it is placed (G1, G2): the flyout, with no title bar, above
/// other windows and without a taskbar button; the pinned window, with a title bar, resizing and a taskbar button, at
/// the size and position the user last gave it, remembered apart from the flyout's size; in either state never smaller
/// than the window's minimum, at which the editor fits whole (G20, G21). Only a resize or move the user makes is
/// remembered: the chrome's own placements are marked while they run. It shows, hides and places the window
/// and never activates it or moves its focus, which stay the window's own (G8).
/// </summary>
public sealed class WindowChrome
{
    private readonly Avalonia.Controls.Window window;
    private readonly SettingsKeeper settings;
    private readonly ToggleButton pin;
    private bool pinned;
    private bool placing;

    public WindowChrome(Avalonia.Controls.Window window, SettingsKeeper settings, ToggleButton pin)
    {
        this.window = window;
        this.settings = settings;
        this.pin = pin;
        // In force from the start, so every way the window is sized stops at it: a placement, a drag of the border, a
        // size set directly (G20, G21); lowered only while the window is on a display too small for it (G1).
        window.MinWidth = WindowSize.Minimum.Width;
        window.MinHeight = WindowSize.Minimum.Height;
        window.Resized += (_, e) =>
        {
            // A drag of the border, a snap or any other resize the user makes; never one of the window's own placements.
            if (pinned && !placing && e.Reason is (WindowResizeReason.User or WindowResizeReason.Unspecified) && window.WindowState == WindowState.Normal)
            {
                // The whole window, frame included, as Place puts it back.
                var outer = window.FrameSize ?? e.ClientSize;
                settings.Change(s => s with { PinnedSize = new WindowSize(outer.Width, outer.Height) });
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

    /// <summary>Whether the window is pinned: it stays on losing focus, on Esc and after a paste (G2).</summary>
    public bool Pinned => pinned;

    /// <summary>The pin button: the pinned frame, at the pinned window's remembered place and size.</summary>
    public void Pin()
    {
        Apply(pinnedNow: true);
        PlaceSettled(PinnedPlace());
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

    /// <summary>Where the pinned window shows afresh: where the user last left it, at its remembered size, else centred on the primary display (G2).</summary>
    public PixelRect PinnedPlace() =>
        Summon.Pinned(settings.Current.PinnedPosition, Displays(), settings.Current.PinnedSize.UsableOr(WindowSize.PinnedDefault));

    /// <summary>The pinned window's frame, or the flyout's, with the pin button showing which (G2).</summary>
    public void Apply(bool pinnedNow)
    {
        // Changing the frame resizes and moves the window at once; that is not the user placing the pinned window.
        placing = true;
        pinned = pinnedNow;
        window.WindowDecorations = pinned ? WindowDecorations.Full : WindowDecorations.BorderOnly;
        window.CanResize = pinned;
        window.CanMinimize = pinned;
        window.CanMaximize = pinned;
        window.ShowInTaskbar = pinned;
        // Above other windows like the system's own tray flyouts, so it shows even when Windows refuses it the foreground.
        window.Topmost = !pinned;
        pin.IsChecked = pinned;
        pin.Content = IconGlyph.Of(pinned ? "" : "", 16);
        ToolTipRule.Set(pin, pinned ? "Unpin" : "Keep open");
        AutomationProperties.SetName(pin, pinned ? "Unpin" : "Keep open");
        placing = false;
    }

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
