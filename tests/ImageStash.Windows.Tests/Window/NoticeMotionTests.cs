using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ImageStash.Files;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.CaptureDesk;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// Notification fades leave the window whole, stash notices close their rooms in place, hover holds both notification
/// clocks and preserves the stack's placement, and a real click cannot Undo from a fading card (F10, G16).
/// They need a signed-in desktop: a shell in Windows session 0 cannot run them.
/// </summary>
[TestClass]
public sealed class NoticeMotionTests
{
    [TestMethod]
    public async Task ANotificationFadesInAndOutWithoutChangingTheWindowsSizeAndTheLastExitClosesIt()
    {
        using var shown = new ShownNotifications();
        var notice = TrayDesktop.Get(() => shown.Notices.Raise("Saved a picture"));
        await Placed();

        TrayDesktop.Run(() =>
        {
            var window = shown.Notification.Shown!;
            window.UpdateLayout();
            var outer = Outer(window);
            var closed = false;
            window.Closed += (_, _) => closed = true;
            var room = shown.Stack.RoomOf(notice)!;
            Assert.IsGreaterThan(0, room.Bounds.Height, "the card takes its room before it is visible");
            Assert.AreEqual(0d, room.Opacity, "start of the entrance");
            shown.Advance(NoticeMotion.Entrance / 2);
            Assert.AreEqual(0.5, room.Opacity, 0.01, "middle of the entrance");
            shown.Advance(NoticeMotion.Entrance / 2);
            Assert.AreEqual(1d, room.Opacity, "end of the entrance");

            shown.Advance(Notices.Lasting - NoticeMotion.Entrance);
            shown.Notices.Sweep();
            Assert.IsEmpty(shown.Notices.Shown, "the notice has expired");
            Assert.AreEqual(1d, room.Opacity, "start of the exit");
            Assert.AreEqual(outer, Outer(window), "start of the exit leaves the window whole");
            shown.Advance(NoticeMotion.Exit / 2);
            Assert.AreEqual(0.5, room.Opacity, 0.01, "middle of the exit");
            Assert.AreEqual(outer, Outer(window), "middle of the exit leaves the window whole");
            var lastTick = TimeSpan.FromTicks(1);
            shown.Advance(NoticeMotion.Exit / 2 - lastTick);
            Assert.AreEqual(outer, Outer(window), "the window is still whole just before the exit ends");
            Assert.AreSame(window, shown.Notification.Shown);
            Assert.IsFalse(closed, "the window remains open until the fade ends");
            shown.Advance(lastTick);
            Assert.AreEqual(0d, room.Opacity, "end of the exit");
            Assert.IsNull(shown.Stack.RoomOf(notice));
            Assert.IsNull(shown.Notification.Shown, "the last card's exit closes the window");
            Assert.IsTrue(closed, "the window raised Closed");
        });
    }

    [TestMethod]
    public async Task ALeavingStashNoticeClosesItsRoomAndTheCardsAboveMoveDownInTheSameWindow()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(DisplayPlaces.At(DisplayPlaces.Primary(), 1050, 700));
        using var shown = ShownWindow.Open();
        await shown.OpenStash();
        var (upper, middle, lower) = TrayDesktop.Get(() =>
            (shown.Notices.Raise("First notice"), shown.Notices.Raise("Second notice"), shown.Notices.Raise("Third notice")));
        await Placed();

        TrayDesktop.Run(() =>
        {
            var window = shown.Window;
            var stack = NoticeMotion.StashStack(window);
            NoticeMotion.Advance(shown.Notices, stack, NoticeMotion.Entrance);
            window.UpdateLayout();
            var room = stack.RoomOf(lower)!;
            var above = new[] { stack.CardOf(upper)!, stack.CardOf(middle)! };
            var positions = above.Select(card => card.PointToScreen(default).Y).ToArray();
            var outer = Outer(window);
            var whole = room.Bounds.Height;
            Assert.IsGreaterThan(0, whole);

            shown.Notices.Close(lower);
            window.UpdateLayout();
            Assert.AreEqual(whole, room.Bounds.Height, "the leaving room starts at its full height");
            Assert.AreEqual(outer, Outer(window));
            NoticeMotion.Advance(shown.Notices, stack, NoticeMotion.Exit / 2);
            window.UpdateLayout();
            Assert.AreEqual(whole / 2, room.Height, 0.01, "the leaving room's height is halfway closed");
            Assert.IsTrue(room.Bounds.Height > 0 && room.Bounds.Height < whole, "layout has closed part of the room");
            Assert.AreEqual(outer, Outer(window));
            var halfway = above.Select(card => card.PointToScreen(default).Y).ToArray();

            NoticeMotion.Advance(shown.Notices, stack, NoticeMotion.Exit / 2);
            window.UpdateLayout();
            Assert.IsNull(stack.RoomOf(lower), "the leaving room is gone at the end");
            Assert.AreEqual(outer, Outer(window), "the stash window itself did not move or resize");
            for (var index = 0; index < above.Length; index++)
            {
                Assert.IsGreaterThan(positions[index], halfway[index], "the card above moves down during the exit");
                Assert.IsGreaterThan(halfway[index], above[index].PointToScreen(default).Y, "it moves farther down by the end");
            }
        });
    }

    [TestMethod]
    public async Task PointerOnTheUpperNotificationHoldsBothClocksAndClosingTheLowerKeepsItsRoomUntilPointerExit()
    {
        using var shown = new ShownNotifications();
        var (upper, lower) = TrayDesktop.Get(() =>
            (shown.Notices.Raise("First notice"), shown.Notices.Raise("Second notice")));
        await Placed();
        var (window, upperCard, lowerClose) = TrayDesktop.Get(() =>
        {
            shown.Advance(NoticeMotion.Entrance);
            var window = shown.Notification.Shown!;
            window.UpdateLayout();
            return (window, shown.Stack.CardOf(upper)!, CloseButton(shown.Stack.CardOf(lower)!));
        });
        var upperPoint = TestMouse.Centre(upperCard);
        var closePoint = TestMouse.Centre(lowerClose);
        await Eventually(() => TrayDesktop.Get(() => NoticeMotion.Hits(window, upperPoint, upperCard) && NoticeMotion.Hits(window, closePoint, lowerClose)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TrayDesktop.Run(() => window.PointerEntered += (_, _) => entered.TrySetResult());
        TestMouse.MoveTo(upperPoint);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var outer = TrayDesktop.Get(() => Outer(window));
        TrayDesktop.Run(() =>
        {
            shown.Advance(TimeSpan.FromSeconds(30));
            shown.Notices.Sweep();
            CollectionAssert.AreEqual(new[] { upper, lower }, shown.Notices.Shown.ToArray(), "the pointer on the upper card holds both clocks");
        });

        TestMouse.Click(closePoint);
        await Eventually(() => TrayDesktop.Get(() => !shown.Notices.Shown.Contains(lower)));
        TestMouse.MoveTo(upperPoint);
        TrayDesktop.Run(() =>
        {
            shown.Advance(NoticeMotion.Exit);
            Assert.AreEqual(0d, shown.Stack.RoomOf(lower)!.Opacity, "the lower room is faded but held");
            Assert.AreEqual(outer, Outer(window), "the window keeps its place while held");
            Assert.AreEqual(upperPoint, TestMouse.Centre(upperCard), "the upper card stays under the pointer");
        });

        TestMouse.MoveTo(shown.Away);
        await Eventually(() => TrayDesktop.Get(() => shown.Stack.RoomOf(lower) is null));
        TrayDesktop.Run(() =>
        {
            window.UpdateLayout();
            Assert.IsLessThan(outer.Height, Outer(window).Height, "the window shrinks on pointer exit");
            Assert.IsGreaterThan(outer.Y, window.Position.Y, "the smaller window is placed back above the tray");
            Assert.IsGreaterThan(upperPoint.Y, TestMouse.Centre(upperCard).Y, "the upper card moves only once the pointer leaves");
            shown.Advance(Notices.Lasting - NoticeMotion.Entrance);
            shown.Notices.Sweep();
            Assert.IsEmpty(shown.Notices.Shown, "the upper card's remaining time runs after pointer exit");
        });
    }

    [TestMethod]
    public async Task ARealClickAtTheFadingUndoLeavesTheAddedItemInTheStash()
    {
        using var stash = ShownWindow.Open();
        var item = (await stash.OpenStash(stash.Picture("kept.png"))).Single();
        TrayDesktop.Run(stash.Window.Hide);
        using var shown = new ShownNotifications();
        var (notice, card) = TrayDesktop.Get(() =>
        {
            var capture = new CaptureNotification(shown.Notices, stash.Listing, stash.Handoff, shown.Quiet, HeldFileRetry.Duration);
            capture.Raise(item, CaptureKind.Screenshot, onClipboard: false, lengthMs: null);
            var notice = shown.Notices.Shown.Single();
            shown.Advance(NoticeMotion.Entrance);
            return (notice, (CaptureCard)shown.Stack.CardOf(notice)!);
        });
        await Placed();
        await Eventually(() => TrayDesktop.Get(() => card.Size is not null));
        var (window, undo) = TrayDesktop.Get(() =>
        {
            var window = shown.Notification.Shown!;
            window.UpdateLayout();
            return (window, UndoButton(card));
        });
        var at = TestMouse.Centre(undo);
        await Eventually(() => TrayDesktop.Get(() => NoticeMotion.Hits(window, at, undo)));

        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var undoClicks = 0;
        TrayDesktop.Run(() =>
        {
            undo.Click += (_, _) => undoClicks++;
            window.AddHandler(InputElement.PointerReleasedEvent, (_, _) => released.TrySetResult(), RoutingStrategies.Tunnel, handledEventsToo: true);
            shown.Notices.Close(notice);
            shown.Advance(NoticeMotion.Exit / 2);
            Assert.IsFalse(card.IsHitTestVisible, "the fading card takes no pointer");
            Assert.AreEqual(0.5, shown.Stack.RoomOf(notice)!.Opacity, 0.01, "the card is still visible");
        });
        var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TrayDesktop.Run(() => window.RequestAnimationFrame(_ => frame.TrySetResult()));
        await frame.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Eventually(() => TrayDesktop.Get(() => NoticeMotion.Hits(window, at, shown.Stack.RoomOf(notice)!) && !NoticeMotion.Hits(window, at, card)));
        TestMouse.Click(at);
        await released.Task.WaitAsync(TimeSpan.FromSeconds(10));

        TrayDesktop.Run(() => Assert.AreEqual(0, undoClicks, "the window received the click, but the Undo button did not"));
        var items = await TrayDesktop.Get(() => stash.Listing.Store!.All());
        CollectionAssert.AreEqual(new[] { item.Id }, items.Value!.Select(saved => saved.Id).ToArray());
        Assert.IsTrue(File.Exists(item.FilePath), "the added file stays in the stash");
    }

    private static async Task Placed() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private sealed class ShownNotifications : IDisposable
    {
        private readonly IDisposable pointer = TestMouse.PutBackAfterwards();
        private readonly WindowsTray tray = new();

        public ShownNotifications()
        {
            TestMouse.MoveTo(Away);
            Quiet = new TrayIconMidScreen(new WindowsQuietWindows(tray), animationEffectsShown: true);
            Notification = TrayDesktop.Get(() => new Notification(Notices, Quiet, new WindowsScreen(), _ => { }));
        }

        public PixelPoint Away { get; } = DisplayPlaces.At(DisplayPlaces.Primary(), 100, 650);

        public Notices Notices { get; } = new(new StoppedClock());

        public TrayIconMidScreen Quiet { get; }

        public Notification Notification { get; }

        public NoticeStack Stack => Notification.Stack;

        public void Advance(TimeSpan by) => NoticeMotion.Advance(Notices, Stack, by);

        public void Dispose()
        {
            try
            {
                TrayDesktop.Run(() =>
                {
                    foreach (var notice in Notices.Shown.ToArray())
                    {
                        Notices.Close(notice);
                    }

                    Stack.Finish();
                });
            }
            finally
            {
                tray.Dispose();
                pointer.Dispose();
            }
        }
    }
}
