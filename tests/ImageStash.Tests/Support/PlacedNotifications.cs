using Avalonia;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;

namespace ImageStash.Tests.Support;

/// <summary>
/// Where a check's stash would put its windows, with no window to put: one <see cref="NotificationPlaces"/> as the app
/// makes it, every stack of the notifications' home (the tray's and each opened later) tracked by it as a notifications
/// window is, at <see cref="Card"/> a card, placed when its notices change while any show; and the flyout a card's opening
/// asks for, at the place the window's summon from a notification takes, then told to the places as the window showing
/// there tells them. The check moves the stash window itself through <see cref="Places"/>, as the window's own moves do.
/// </summary>
public sealed class PlacedNotifications
{
    /// <summary>One card's size, in device-independent units, as a capture card is about.</summary>
    public static readonly WindowSize Card = new(480, 130);

    private readonly ListedStash listed;
    private readonly Dictionary<Notices, List<PixelRect>> asked = [];

    public PlacedNotifications(ListedStash listed)
    {
        this.listed = listed;
        foreach (var stack in listed.Stacks.All)
        {
            Track(stack, null);
        }

        listed.Stacks.Opened += (stack, display) => Track(stack, display);
        // As the window's summon from a notification shows the flyout (StashWindow.SummonFromNotification).
        listed.SummonedFromNotification += (_, stoodOn) =>
        {
            var place = Summon.FromNotification(TrayIcon, listed.Screen.Shown, listed.Settings.Current.FlyoutSize.UsableOr(WindowSize.FlyoutDefault), stoodOn);
            FlyoutAsked.Add(place);
            Places.StashWindowAt(place);
        };
    }

    /// <summary>Where every notifications window stands, as the app makes it.</summary>
    public NotificationPlaces Places { get; } = new();

    /// <summary>The tray icon's rectangle, as the Quiet-windows seam reads it; null, unread.</summary>
    public PixelRect? TrayIcon { get; set; }

    /// <summary>Each place a card's opening asked the flyout to take, in order.</summary>
    public List<PixelRect> FlyoutAsked { get; } = [];

    /// <summary>Each place <paramref name="stack"/>'s window was asked to take, in order.</summary>
    public IReadOnlyList<PixelRect> AskedOf(Notices stack) => asked[stack];

    /// <summary>The place <paramref name="stack"/>'s window was last asked to take.</summary>
    public PixelRect PlaceOf(Notices stack) => asked[stack][^1];

    private void Track(Notices stack, Display? display)
    {
        asked[stack] = [];
        var tracked = Places.Track(
            () => stack.Shown.Count == 0 ? null : new StackRoom(new WindowSize(Card.Width, Card.Height * stack.Shown.Count), listed.Screen.Shown, TrayIcon),
            display is null ? null : () => display,
            asked[stack].Add);
        // As a notifications window places itself when a card takes or gives up its room.
        stack.Changed += () => Places.Place(tracked);
    }
}
