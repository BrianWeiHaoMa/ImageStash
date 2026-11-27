using Avalonia;
using Avalonia.Controls;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>What a notifications window asks to be placed by: the size its cards want, the displays now, and the tray icon.</summary>
public sealed record StackRoom(WindowSize Wanted, IReadOnlyList<Display> Displays, PixelRect? TrayIcon);

/// <summary>
/// Where every notifications window stands (G16, F4), the one place that knows where the stash window stands: each
/// window is placed by <see cref="Summon.NotificationPlace"/> clear of the stash window's whole rectangle while that
/// window shows over its place, when a notification is raised there and again whenever the stash window shows, moves,
/// is resized or hides, so a card already showing never stays over a window opened after it. Placing again moves a
/// window and nothing else: no notice's time, hold or order changes. Used on the UI thread.
/// </summary>
public sealed class NotificationPlaces
{
    private readonly List<Placed> placed = [];
    private PixelRect? stashWindow;

    /// <summary>
    /// A notifications window placed here from now on: <paramref name="room"/> null while it has no window to place,
    /// <paramref name="on"/> the display its stack stands on (null above the tray), read at each placing, and
    /// <paramref name="moveTo"/> putting it at the place asked.
    /// </summary>
    public Placed Track(Func<StackRoom?> room, Func<Display?>? on, Action<PixelRect> moveTo)
    {
        var tracked = new Placed(room, on, moveTo);
        placed.Add(tracked);
        return tracked;
    }

    /// <summary>Places <paramref name="tracked"/> now, if it has a window to place.</summary>
    public void Place(Placed tracked)
    {
        if (tracked.Room() is { } room)
        {
            // Beside the same window, the side it already took is kept as its cards come and go.
            var standing = tracked.ClearOf == stashWindow ? tracked.Standing : null;
            var place = Summon.NotificationPlace(room.TrayIcon, room.Displays, room.Wanted, tracked.On?.Invoke(), stashWindow, standing);
            (tracked.Standing, tracked.ClearOf) = (place, stashWindow);
            tracked.MoveTo(place);
        }
    }

    /// <summary>
    /// The stash window now stands at <paramref name="standing"/>, or, null, shows nowhere: each notifications window with
    /// a window to place is placed again, clear of it or back in its usual place.
    /// </summary>
    public void StashWindowAt(PixelRect? standing)
    {
        if (standing == stashWindow)
        {
            return;
        }

        stashWindow = standing;
        foreach (var tracked in placed)
        {
            Place(tracked);
        }
    }

    /// <summary>
    /// Follows <paramref name="window"/>, the stash window: each showing, move, resize, minimising and hiding tells the
    /// notifications where its whole rectangle, frame included, stands.
    /// </summary>
    public void Follow(Avalonia.Controls.Window window)
    {
        void Changed() => StashWindowAt(window is { IsVisible: true, WindowState: not WindowState.Minimized } ? WindowChrome.OuterOf(window) : null);
        window.PositionChanged += (_, _) => Changed();
        window.Resized += (_, _) => Changed();
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty || e.Property == Avalonia.Controls.Window.WindowStateProperty)
            {
                Changed();
            }
        };
    }

    /// <summary>A notifications window as <see cref="Track"/> keeps it, and where it was last placed, beside which stash window.</summary>
    public sealed class Placed(Func<StackRoom?> room, Func<Display?>? on, Action<PixelRect> moveTo)
    {
        internal Func<StackRoom?> Room { get; } = room;

        internal Func<Display?>? On { get; } = on;

        internal Action<PixelRect> MoveTo { get; } = moveTo;

        internal PixelRect? Standing { get; set; }

        internal PixelRect? ClearOf { get; set; }
    }
}
