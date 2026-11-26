using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using ImageStash.Look;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// How a card arrives and leaves (F10, L2, G16, G1): in the stash window's stack and the notifications' alike it fades in
/// over the look's entrance and out over its exit, its time driven here by the notices' clock; motion never lengthens
/// what a card can do, its Undo, × and button dead from the instant it leaves though it still fades; a problem notice
/// staying past its time stays whole; a hidden window's cards are whole at once and a card that left there is gone; with
/// the system's animation effects off nothing moves; the pointer on the notifications' window holds every card, a card
/// leaving meanwhile keeping its room until the pointer has gone.
/// </summary>
[TestClass]
public sealed class NoticeMotionTests
{
    private static readonly TimeSpan Entrance = (TimeSpan)Look()[LookKeys.CardEntrance]!;
    private static readonly TimeSpan Exit = (TimeSpan)Look()[LookKeys.CardExit]!;

    private readonly SetClock clock = new();

    [TestMethod]
    public void TheLooksDurationsAreAboutASixthOfASecondInAndALittleFasterOut()
    {
        // F10's one motion, short enough never to read as the window moving.
        Assert.IsTrue(Entrance > TimeSpan.FromMilliseconds(100) && Entrance <= TimeSpan.FromMilliseconds(250), $"in over {Entrance}");
        Assert.IsTrue(Exit > TimeSpan.Zero && Exit < Entrance, $"out over {Exit}, faster than in");
    }

    [TestMethod]
    public void AWindowsNoticeFadesInTakingItsRoomAtOnceAndFadesOutAtItsTimeGivingUpItsRoomAtTheEnd()
    {
        var notices = InWindow();
        ControlThread.Run(() =>
        {
            var stack = Stack(notices, plays: () => true, holdsTogether: false);
            var notice = notices.Raise("3 added");
            stack.Show();
            var room = stack.RoomOf(notice)!;

            Assert.HasCount(1, stack.View.Children, "its room taken at once");
            Assert.AreEqual(0, room.Opacity, "the first frame: not yet shown");
            Step(stack, Entrance / 2);
            Assert.AreEqual(0.5, room.Opacity, 0.01, "halfway in");
            Step(stack, Entrance / 2);
            Assert.AreEqual(1, room.Opacity, "whole at the entrance's end");
            Assert.IsFalse(stack.Moving);

            clock.Now += Notices.Lasting - Entrance;
            notices.Sweep();
            stack.Show();
            Assert.IsEmpty(notices.Shown, "its time is six seconds, the fade taking none of it and adding none (G16)");
            Assert.AreSame(room, stack.RoomOf(notice), "still there, fading");
            Assert.AreEqual(1, room.Opacity, "the exit's first frame");
            Step(stack, Exit / 2);
            Assert.AreEqual(0.5, room.Opacity, 0.01, "halfway out");
            Assert.HasCount(1, stack.View.Children);
            Step(stack, Exit / 2);
            Assert.IsEmpty(stack.View.Children, "its room given up once it has faded");
            Assert.IsFalse(stack.HoldsRoom);
        });
    }

    [TestMethod]
    public void FromTheTimesEndThroughTheFadeACardsUndoCloseAndButtonDoNothing()
    {
        // G16: "an Undo acts for the notice's time, not for as long as a notice stays past it"; G18's Undo while offered.
        var notices = InWindow();
        var undone = 0;
        var shown = 0;
        var left = 0;
        ControlThread.Run(() =>
        {
            var stack = Stack(notices, plays: () => true, holdsTogether: false);
            var notice = notices.Raise("Deleted “cat”.", "Undo", () =>
            {
                undone++;
                return Task.FromResult(Outcome.Done);
            }, left: () =>
            {
                left++;
                return Task.CompletedTask;
            }, button: new NoticeButton("Show", () =>
            {
                shown++;
                return Task.CompletedTask;
            }));
            stack.Show();
            Step(stack, Entrance);
            var card = stack.CardOf(notice)!;

            clock.Now += Notices.LastingFor(notice.Words) - Entrance;
            notices.Sweep();
            stack.Show();
            Step(stack, Exit / 2);

            Assert.AreSame(card, stack.CardOf(notice), "still fading out");
            Assert.IsFalse(card.IsHitTestVisible, "no pointer reaches it from the instant it left");
            Assert.IsTrue(Button(card, "Undo").IsVisible, "the card keeps its shape as it goes");
            foreach (var label in new[] { "Undo", "Show", "Close" })
            {
                Button(card, label).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            }
        });

        Assert.AreEqual(0, undone, "Undo after its time does nothing");
        Assert.AreEqual(0, shown, "Show once the notice has gone does nothing");
        Assert.AreEqual(1, left, "what it was given for leaving ran once, at its time; the × ran nothing again");
    }

    [TestMethod]
    public void ACardClosedByItsCloseIsDeadAtOnceThoughItFades()
    {
        var notices = new Notices(clock);
        var undone = 0;
        ControlThread.Run(() =>
        {
            var stack = Stack(notices, plays: () => true, holdsTogether: true);
            var notice = notices.Raise("Screenshot stashed", "Undo", () =>
            {
                undone++;
                return Task.FromResult(Outcome.Done);
            });
            stack.Show();
            Step(stack, Entrance);
            var card = stack.CardOf(notice)!;

            Button(card, "Close").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            stack.Show();
            Button(card, "Undo").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

            Assert.AreSame(card, stack.CardOf(notice), "fading out");
            Assert.IsFalse(card.IsHitTestVisible);
        });

        Assert.AreEqual(0, undone, "a notice closed by its × offers its Undo no more");
    }

    [TestMethod]
    public void AProblemNoticePastItsTimeStaysWholeWithItsCloseAndShow()
    {
        var notices = InWindow();
        ControlThread.Run(() =>
        {
            var stack = Stack(notices, plays: () => true, holdsTogether: false);
            var notice = notices.Raise("Skipped clip.mp4 (a video).", namesAProblem: true, button: new NoticeButton("Show", () => Task.CompletedTask));
            stack.Show();
            Step(stack, Entrance);

            clock.Now += Notices.LastingAtMost;
            notices.Sweep();
            stack.Show();
            Step(stack, Exit);

            var room = stack.RoomOf(notice)!;
            Assert.AreEqual(1, room.Opacity, "it stays past its time and does not fade out and in again (G16)");
            Assert.IsFalse(stack.Moving);
            Assert.IsTrue(stack.CardOf(notice)!.IsHitTestVisible, "its × and Show still answer");
        });
    }

    [TestMethod]
    public void WhileTheStashWindowIsHiddenACardArrivesWholeAndLeavesAtOnceAndAHideEndsAMotionUnderWay()
    {
        // G1: at a summon every card is whole in its first frame, and one that left while hidden is simply gone.
        var notices = InWindow();
        var showing = true;
        ControlThread.Run(() =>
        {
            var stack = Stack(notices, plays: () => showing, holdsTogether: false);
            var leaving = notices.Raise("Renamed “cat”.");
            var arriving = notices.Raise("Tagged “cat”.");
            stack.Show();
            Step(stack, Entrance);
            notices.Close(leaving);
            stack.Show();
            Step(stack, Exit / 2);
            var later = notices.Raise("Favorited “cat”.");
            stack.Show();
            Step(stack, Entrance / 2);

            showing = false;
            stack.Finish();
            Assert.IsNull(stack.RoomOf(leaving), "an exit under way at the hide ends there");
            Assert.AreEqual(1, stack.RoomOf(later)!.Opacity, "an entrance under way at the hide ends whole");

            var raisedHidden = notices.Raise("Copied “cat”.");
            stack.Show();
            Assert.AreEqual(1, stack.RoomOf(raisedHidden)!.Opacity, "raised while hidden: whole at once");
            notices.Close(arriving);
            stack.Show();
            Assert.IsNull(stack.RoomOf(arriving), "closed while hidden: gone at once");
            Assert.IsFalse(stack.Moving, "nothing waits for the next show");
        });
    }

    [TestMethod]
    public void WithTheSystemsAnimationEffectsOffACardAppearsAndLeavesInOneStep()
    {
        // F10: "no motion as a notification or a notice arrives or leaves while the system's animation effects are off".
        foreach (var holdsTogether in new[] { false, true })
        {
            var notices = holdsTogether ? new Notices(clock) : InWindow();
            var effectsShown = false;
            ControlThread.Run(() =>
            {
                var stack = Stack(notices, plays: () => effectsShown, holdsTogether);
                var notice = notices.Raise("3 added");
                stack.Show();
                Assert.AreEqual(1, stack.RoomOf(notice)!.Opacity, $"{Place(holdsTogether)}: whole in its first frame");

                notices.Close(notice);
                stack.Show();
                Assert.IsNull(stack.RoomOf(notice), $"{Place(holdsTogether)}: gone in one step");
                Assert.IsFalse(stack.Moving);

                effectsShown = true;
                var next = notices.Raise("2 added");
                stack.Show();
                Assert.AreEqual(0, stack.RoomOf(next)!.Opacity, $"{Place(holdsTogether)}: read afresh, the effects turned on again move");

                Step(stack, Entrance / 2);
                effectsShown = false;
                Step(stack, TimeSpan.FromMilliseconds(1));
                Assert.AreEqual(1, stack.RoomOf(next)!.Opacity, $"{Place(holdsTogether)}: turned off mid-fade, the card is whole at the next frame");
                notices.Close(next);
                stack.Show();
                Assert.IsNull(stack.RoomOf(next), $"{Place(holdsTogether)}: and leaves in one step");
            });
        }
    }

    [TestMethod]
    public void BothStacksReadTheirDurationsFromTheLookTheyAreDressedIn()
    {
        // F10, L2: one motion, the look's, never a duration of a control's own.
        var (entrance, exit) = (TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(300));
        ResourceDictionary Slower()
        {
            var look = Look();
            look[LookKeys.CardEntrance] = entrance;
            look[LookKeys.CardExit] = exit;
            return look;
        }

        var window = InWindow();
        var tray = new Notices(clock);
        ControlThread.Run(() =>
        {
            var inWindow = Stack(window, plays: () => true, holdsTogether: false, look: Slower());
            var aboveTray = Stack(tray, plays: () => true, holdsTogether: true, look: Slower());
            var a = window.Raise("3 added");
            var b = tray.Raise("Screenshot stashed");
            inWindow.Show();
            aboveTray.Show();

            Step(inWindow, entrance - TimeSpan.FromMilliseconds(1));
            aboveTray.Step();
            Assert.IsTrue(inWindow.RoomOf(a)!.Opacity < 1 && aboveTray.RoomOf(b)!.Opacity < 1, "both still fading in a moment before the look's entrance ends");
            Step(inWindow, TimeSpan.FromMilliseconds(1));
            aboveTray.Step();
            Assert.IsTrue(inWindow.RoomOf(a)!.Opacity == 1 && aboveTray.RoomOf(b)!.Opacity == 1, "both whole at its end");

            window.Close(a);
            tray.Close(b);
            inWindow.Show();
            aboveTray.Show();
            Step(inWindow, exit - TimeSpan.FromMilliseconds(1));
            aboveTray.Step();
            Assert.IsTrue(inWindow.HoldsRoom && aboveTray.HoldsRoom, "both still fading out a moment before the look's exit ends");
            Step(inWindow, TimeSpan.FromMilliseconds(1));
            aboveTray.Step();
            Assert.IsFalse(inWindow.HoldsRoom || aboveTray.HoldsRoom, "both gone at its end");
        });
    }

    [TestMethod]
    public async Task TheStashWindowsNoticesMoveOnlyWhileItShowsAndTheEffectsAreOnAndAHideEndsTheirMotion()
    {
        // Through the window's own wiring: its rule of showing (G16) and the Quiet-windows seam's reading (F10).
        using var listed = await ListedStash.Open();
        var effectsShown = true;
        ControlThread.Run(() =>
        {
            var status = new WindowStatus(listed.Listing, listed.Notices, listed.Settings, listed.FooterHints, new ScrollViewer(), () => false, () => { }, listed.WaysIn, () => effectsShown);
            status.NoticeArea.Resources.MergedDictionaries.Add(Look());
            var showing = new WindowShowing(listed.Notices, status.NoticesShowing);
            showing.Seen(visible: true, minimised: false);
            var stack = (StackPanel)status.NoticeArea.Content!;

            listed.Notices.Raise("3 added");
            status.ShowNotices();
            Assert.AreEqual(0, stack.Children[0].Opacity, "showing, the effects on: it fades in");

            showing.Seen(visible: true, minimised: true);
            Assert.AreEqual(1, stack.Children[0].Opacity, "minimised: whole at once, for the next show");
            listed.Notices.Raise("2 added");
            status.ShowNotices();
            Assert.AreEqual(1, stack.Children[1].Opacity, "raised while minimised: whole at once");

            showing.Seen(visible: true, minimised: false);
            effectsShown = false;
            listed.Notices.Raise("1 added");
            status.ShowNotices();
            Assert.AreEqual(1, stack.Children[2].Opacity, "the effects off: no motion");
        });
    }

    [TestMethod]
    public void ThePointerOnTheNotificationsHoldsEveryCardAndALeavingCardKeepsItsRoomUntilThePointerHasGone()
    {
        // G16 "longer while hovered", read for the whole stack above the tray: nothing slides under a hand reaching for Undo.
        var notices = new Notices(clock);
        ControlThread.Run(() =>
        {
            var stack = Stack(notices, plays: () => true, holdsTogether: true);
            var upper = notices.Raise("Screenshot stashed: “one”.");
            var lower = notices.Raise("Screenshot stashed: “two”.");
            stack.Show();
            Step(stack, Entrance);
            clock.Now += TimeSpan.FromSeconds(2) - Entrance;

            stack.Held(over: true);
            clock.Now += TimeSpan.FromSeconds(30);
            notices.Sweep();
            stack.Show();
            CollectionAssert.AreEqual(new[] { upper, lower }, notices.Shown.ToList(), "the pointer on either holds both clocks");

            notices.Close(lower);
            stack.Show();
            Step(stack, Exit);
            Assert.HasCount(2, stack.View.Children, "the closed card keeps its room while the pointer is on the stack");
            Assert.AreEqual(0, stack.RoomOf(lower)!.Opacity, "faded out where it stood");
            var raisedMeanwhile = notices.Raise("Recording stashed: “three”.");
            stack.Show();
            Step(stack, Entrance);
            clock.Now += TimeSpan.FromSeconds(10);
            notices.Sweep();
            stack.Show();
            CollectionAssert.Contains(notices.Shown.ToList(), raisedMeanwhile, "a card raised while the pointer is on the stack is held from its first frame");

            stack.Held(over: false);
            Assert.IsNull(stack.RoomOf(lower), "its room given up once the pointer has gone");
            CollectionAssert.AreEqual(new[] { upper, raisedMeanwhile }, notices.Shown.ToList());

            clock.Now += TimeSpan.FromSeconds(4) - TimeSpan.FromMilliseconds(1);
            notices.Sweep();
            CollectionAssert.Contains(notices.Shown.ToList(), upper, "its clock runs on from where it stopped, four seconds left");
            clock.Now += TimeSpan.FromMilliseconds(1);
            notices.Sweep();
            CollectionAssert.AreEqual(new[] { raisedMeanwhile }, notices.Shown.ToList(), "it leaves at its time");
            clock.Now += TimeSpan.FromSeconds(2);
            notices.Sweep();
            Assert.IsEmpty(notices.Shown, "a card raised under the pointer was held too, and runs its six seconds once it has gone");
        });
    }

    [TestMethod]
    public void UnheldACardLeavingTheNotificationsGivesUpItsRoomOnceFadedAndTheLastLetsGoOfTheHold()
    {
        var notices = new Notices(clock);
        ControlThread.Run(() =>
        {
            var stack = Stack(notices, plays: () => true, holdsTogether: true);
            var upper = notices.Raise("one");
            var lower = notices.Raise("two");
            stack.Show();
            Step(stack, Entrance);
            notices.Close(lower);
            stack.Show();
            Step(stack, Exit - TimeSpan.FromMilliseconds(1));
            Assert.HasCount(2, stack.View.Children, "the room kept whole while it fades, so the window is never resized under it");
            Step(stack, TimeSpan.FromMilliseconds(1));
            Assert.HasCount(1, stack.View.Children, "given up once faded");

            stack.Held(over: true);
            notices.Close(upper);
            stack.Show();
            Step(stack, Exit);
            Assert.IsFalse(stack.HoldsRoom, "with no card left nothing could slide: the last one's room goes and the window closes");

            var next = notices.Raise("three");
            stack.Show();
            clock.Now += Notices.Lasting;
            notices.Sweep();
            Assert.DoesNotContain(next, notices.Shown.ToList(), "no hold outlives the window: the next notification leaves at its time");
        });
    }

    [TestMethod]
    public async Task ACaptureCardWhoseNoticeHasLeftOpensNothingThoughItStillFades()
    {
        // G16: Edit, Trim and the click on the card's body act for the card that shows, never one that has left.
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        var opened = new List<CaptureOpening>();
        listed.CaptureNotification.OpenRequested += opened.Add;
        listed.CaptureNotification.Raise(cat, CaptureKind.Screenshot, onClipboard: false, lengthMs: null);
        var notice = listed.Notifications.Shown.Single();
        ControlThread.Run(() =>
        {
            var stack = Stack(listed.Notifications, plays: () => true, holdsTogether: true, card: n => n.Card!(n, listed.Notifications));
            stack.Show();
            Step(stack, Entrance, listed.Stash.Clock);
            var card = (CaptureCard)stack.CardOf(notice)!;

            listed.Notifications.Close(notice);
            stack.Show();
            Button(card, "Edit").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

            Assert.AreSame(card, stack.CardOf(notice), "fading out");
            Assert.IsFalse(card.IsHitTestVisible);
        });

        Assert.IsEmpty(opened, "its Edit asks for nothing once the card has left");
    }

    private Notices InWindow()
    {
        var notices = Notices.InWindow(clock);
        notices.WindowShows(true);
        return notices;
    }

    /// <summary>A stack as the windows make it, dressed in the product's look, its cards the notice card unless given.</summary>
    private static NoticeStack Stack(Notices notices, Func<bool> plays, bool holdsTogether, Func<Notice, Control>? card = null, ResourceDictionary? look = null)
    {
        var stack = new NoticeStack(notices, card ?? (notice => NoticeCard.For(notice, notices)), plays, holdsTogether);
        stack.View.Resources.MergedDictionaries.Add(look ?? Look());
        return stack;
    }

    private static ResourceDictionary Look() => Looks.All[0].Resources(ThemeVariant.Light, Looks.DefaultAccent);

    /// <summary>Time passing on the clock, and the frame the window would render then.</summary>
    private void Step(NoticeStack stack, TimeSpan passing) => Step(stack, passing, clock);

    private static void Step(NoticeStack stack, TimeSpan passing, SetClock on)
    {
        on.Now += passing;
        stack.Step();
    }

    private static Avalonia.Controls.Button Button(Control card, string label) =>
        card.GetSelfAndLogicalDescendants().OfType<Avalonia.Controls.Button>().Single(button => Equals(button.Content, label) || Equals(Avalonia.Automation.AutomationProperties.GetName(button), label));

    private static string Place(bool holdsTogether) => holdsTogether ? "above the tray" : "in the stash window";
}
