using Avalonia.Controls;
using Avalonia.Media;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// A stack of notice cards, oldest first, and the one motion each arrives and leaves with, in the stash window's notices
/// and the notifications above the tray alike (F10, L2). A card raised takes its room at once and fades in over the look's
/// <see cref="LookKeys.CardEntrance"/>; a card whose notice has left (its time, its ×, its Undo, a later notice of its act)
/// fades out over <see cref="LookKeys.CardExit"/>, dead from that instant: it takes no pointer, and what its buttons ask of
/// <see cref="Notices"/> is refused for a notice no longer shown, so motion never lengthens what a card can do (G16). The
/// pointer on the stack, the notifications' window or the stash window's notices, holds every card's clock in it
/// (<see cref="Held"/>; G16's hold read for the whole stack), and a card that leaves meanwhile keeps its room, faded,
/// until the pointer has gone, so nothing slides under a hand reaching for Undo or Show. Unheld, in the stash window
/// (<c>inWindow</c>) the room a leaving card gives up closes over its fade; above the tray it goes once the card has
/// faded, at once, the window then sized again. No motion plays while <c>mayPlay</c>
/// says not (the stash window hidden or minimised, or the system's animation effects off): a card then appears and leaves
/// in one step, and <see cref="Finish"/> ends a motion under way, so a hidden window's cards are whole at its next show.
/// Time is the notices' clock, stepped at each frame the stack's window renders, or by a check through <see cref="Step"/>.
/// </summary>
public sealed class NoticeStack
{
    /// <summary>The space between two cards.</summary>
    public const double Gap = 6;

    private readonly Notices notices;
    private readonly Func<Notice, Control> cardFor;
    private readonly Func<bool> mayPlay;
    private readonly bool inWindow;
    private readonly List<Room> rooms = [];
    private bool held;
    private bool frameAsked;

    /// <param name="cardFor">The card a notice is drawn as.</param>
    /// <param name="mayPlay">Whether motion may play now; read as each card arrives or leaves.</param>
    /// <param name="inWindow">The stash window's stack: each card's gap its own, a room closing as its card fades out.</param>
    public NoticeStack(Notices notices, Func<Notice, Control> cardFor, Func<bool> mayPlay, bool inWindow)
    {
        this.notices = notices;
        this.cardFor = cardFor;
        this.mayPlay = mayPlay;
        this.inWindow = inWindow;
        // In the stash window the gap is each card's own, so it closes with the card's room and nothing jumps at the end.
        View = new StackPanel { Spacing = inWindow ? 0 : Gap };
    }

    /// <summary>The cards, the newest at the bottom.</summary>
    public StackPanel View { get; }

    /// <summary>Raised when a card takes its room or gives it up whole, so a window sized to the stack places itself again.</summary>
    public event Action? RoomChanged;

    /// <summary>Whether a card holds room: one whose notice shows, or one still fading out or keeping its room under the pointer.</summary>
    public bool HoldsRoom => rooms.Count > 0;

    /// <summary>Whether a card is still fading in or out.</summary>
    public bool Moving => rooms.Any(room => room.EnteringSince is not null || room.LeavingSince is not null);

    /// <summary>The card drawn for <paramref name="notice"/> while it holds room, fading out included; null otherwise.</summary>
    public Control? CardOf(Notice notice) => rooms.FirstOrDefault(room => room.Notice == notice)?.Card;

    /// <summary>The room <paramref name="notice"/>'s card stands in, whose opacity and height are the motion's; null once given up.</summary>
    public Border? RoomOf(Notice notice) => rooms.FirstOrDefault(room => room.Notice == notice)?.View;

    /// <summary>The notices changed: a card for each notice raised, and each card whose notice has left begins to leave.</summary>
    public void Show()
    {
        var now = notices.Clock.GetUtcNow();
        var plays = mayPlay();
        var changed = false;
        var leaving = rooms.Where(room => !room.Leaving && !notices.Shown.Contains(room.Notice)).ToList();
        // The arriving cards take their rooms first, so a card leaving in one step under the pointer, one taking its place
        // included, keeps its room while held, and the hold goes on to the arrival.
        foreach (var notice in notices.Shown.Where(notice => rooms.All(room => room.Notice != notice)).ToList())
        {
            var room = new Room(notice, cardFor(notice), inWindow);
            if (held)
            {
                notices.Hover(notice, over: true);
            }

            var entrance = plays ? Lasting(LookKeys.CardEntrance) : TimeSpan.Zero;
            if (entrance > TimeSpan.Zero)
            {
                room.View.Opacity = 0;
                room.EnteringSince = now;
                room.Entrance = entrance;
            }

            rooms.Add(room);
            View.Children.Add(room.View);
            changed = true;
        }

        foreach (var room in leaving)
        {
            changed |= Leave(room, plays ? Lasting(LookKeys.CardExit) : TimeSpan.Zero, now);
        }

        if (changed)
        {
            RoomChanged?.Invoke();
        }

        AskFrame();
    }

    /// <summary>Each motion under way moved on to where the clock says it is now, or ended once motion may not play; a card faded out gives up its room.</summary>
    public void Step()
    {
        if (Moving && !mayPlay())
        {
            // The effects turned off, or the window hid, while a card moved: it ends where it was going, now (F10).
            Finish();
            return;
        }

        var now = notices.Clock.GetUtcNow();
        var changed = false;
        foreach (var room in rooms.ToList())
        {
            if (room.EnteringSince is { } entering)
            {
                var part = Part(now - entering, room.Entrance);
                room.View.Opacity = part;
                if (part >= 1)
                {
                    room.EnteringSince = null;
                }
            }
            else if (room.LeavingSince is { } leaving)
            {
                var part = Part(now - leaving, room.Exit);
                room.View.Opacity = room.OpacityAtLeaving * (1 - part);
                if (inWindow && !held && room.HeightAtLeaving > 0)
                {
                    room.View.Height = room.HeightAtLeaving * (1 - part);
                }

                if (part >= 1)
                {
                    changed |= Faded(room);
                }
            }
        }

        if (changed)
        {
            RoomChanged?.Invoke();
        }

        AskFrame();
    }

    /// <summary>Every motion under way ends now: a card fading in is whole, one fading out is gone (its room kept only under the pointer).</summary>
    public void Finish()
    {
        var changed = false;
        foreach (var room in rooms.ToList())
        {
            if (room.EnteringSince is not null)
            {
                room.EnteringSince = null;
                room.View.Opacity = 1;
            }
            else if (room.LeavingSince is not null)
            {
                changed |= Faded(room);
            }
        }

        if (changed)
        {
            RoomChanged?.Invoke();
        }
    }

    /// <summary>
    /// The pointer came onto the stack, or left it (G16): while it is there every card's clock is held, a card raised
    /// meanwhile's too; once it has gone, or the stash window holding it has stopped showing, the clocks run on from where
    /// they stopped and the room a card that left meanwhile kept is given up.
    /// </summary>
    public void Held(bool over)
    {
        if (held == over)
        {
            return;
        }

        held = over;
        foreach (var notice in notices.Shown.ToList())
        {
            notices.Hover(notice, over);
        }

        if (!over && GiveUpKeptRooms())
        {
            RoomChanged?.Invoke();
        }
    }

    /// <summary>
    /// The card begins to leave: dead at once, its ground taking a click where it lies, so the click acts on nothing
    /// beneath; it fades out over <paramref name="exit"/>, or gives up its room now when that is none.
    /// </summary>
    private bool Leave(Room room, TimeSpan exit, DateTimeOffset now)
    {
        room.Leaving = true;
        room.Card.IsHitTestVisible = false;
        room.View.Background = Brushes.Transparent;
        room.EnteringSince = null;
        if (exit <= TimeSpan.Zero)
        {
            return Faded(room);
        }

        room.LeavingSince = now;
        room.Exit = exit;
        room.OpacityAtLeaving = room.View.Opacity;
        room.HeightAtLeaving = room.View.Bounds.Height;
        // Clipped only while its room closes: a card's shadow lies beyond its bounds.
        room.View.ClipToBounds = inWindow;
        return false;
    }

    /// <summary>The card has faded out: its room goes, unless the pointer holds the stack with another card still in it.</summary>
    private bool Faded(Room room)
    {
        room.LeavingSince = null;
        room.View.Opacity = 0;
        if (held && rooms.Any(other => !other.Leaving))
        {
            return false;
        }

        rooms.Remove(room);
        View.Children.Remove(room.View);
        GiveUpKeptRooms();
        // With no card left the window closes, and no pointer is on a window that has gone: no hold outlives it.
        held &= rooms.Count > 0;
        return true;
    }

    /// <summary>Gives up the room of each card that has faded out; whether any did.</summary>
    private bool GiveUpKeptRooms()
    {
        var kept = rooms.Where(room => room.Leaving && room.LeavingSince is null).ToList();
        foreach (var room in kept)
        {
            rooms.Remove(room);
            View.Children.Remove(room.View);
        }

        return kept.Count > 0;
    }

    /// <summary>The look's duration under <paramref name="key"/>; none where no look is applied.</summary>
    private TimeSpan Lasting(string key) =>
        View.TryFindResource(key, View.ActualThemeVariant, out var value) && value is TimeSpan lasting ? lasting : TimeSpan.Zero;

    private static double Part(TimeSpan gone, TimeSpan whole) => Math.Clamp(gone / whole, 0, 1);

    /// <summary>The next frame the stack's window renders steps the motion on, while one is under way.</summary>
    private void AskFrame()
    {
        if (frameAsked || !Moving || TopLevel.GetTopLevel(View) is not { } shownIn)
        {
            return;
        }

        frameAsked = true;
        shownIn.RequestAnimationFrame(_ =>
        {
            frameAsked = false;
            Step();
        });
    }

    /// <summary>A card and the room it stands in, with its motion's state.</summary>
    private sealed class Room
    {
        public Room(Notice notice, Control card, bool inWindow)
        {
            Notice = notice;
            Card = card;
            // In the stash window the gap above a card is its room's ground too, so a hand moving between two cards stays on
            // the stack and keeps its hold; above the tray the pointer anywhere on the window holds it.
            View = inWindow
                ? new Border { Child = card, Padding = new Avalonia.Thickness(0, Gap, 0, 0), Background = Brushes.Transparent }
                : new Border { Child = card };
        }

        public Notice Notice { get; }

        public Control Card { get; }

        public Border View { get; }

        public bool Leaving { get; set; }

        public DateTimeOffset? EnteringSince { get; set; }

        public TimeSpan Entrance { get; set; }

        public DateTimeOffset? LeavingSince { get; set; }

        public TimeSpan Exit { get; set; }

        public double OpacityAtLeaving { get; set; }

        public double HeightAtLeaving { get; set; }
    }
}
