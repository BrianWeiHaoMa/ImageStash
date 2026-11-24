using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G2's hide on losing focus to another app while the pointer's primary button is held: the flyout stays until the
/// button has come up, a drag from outside over it holding it until its drop, and then hides only if focus is still
/// with another app; with no button held it hides at once. Driven as the window's activation asks the rule, over a
/// primary button the check holds and lets go, as the Quiet-windows seam reads it, on a clock the check steps.
/// </summary>
[TestClass]
public sealed class FocusLossHideTests
{
    [TestMethod]
    public void AFocusLossWithNoButtonHeldHidesAtOnce()
    {
        var flyout = new Flyout();

        flyout.Rule.FocusMayHaveLeft();

        Assert.AreEqual(1, flyout.Hides, "G2: the flyout hides on losing focus to another app, as before");
        Assert.IsFalse(flyout.Rule.Waiting);
        Assert.IsNull(flyout.Clock.NextDue, "nothing is left reading the button");
    }

    [TestMethod]
    public void HeldAtTheLossItStaysWhileTheButtonIsDownAndHidesShortlyAfterItComesUpWithNothingElseHappening()
    {
        var flyout = new Flyout();
        flyout.Button.ButtonHeld = true;

        flyout.Rule.FocusMayHaveLeft();
        flyout.Clock.Advance(TimeSpan.FromSeconds(5));

        Assert.AreEqual(0, flyout.Hides, "G2: it stays while the press begun in the other app is held");
        Assert.IsTrue(flyout.Rule.Waiting);

        flyout.Button.ButtonHeld = false;
        flyout.Clock.Advance(FocusLossHide.Reading);

        Assert.AreEqual(1, flyout.Hides, "no window event comes when the button comes up elsewhere: the next reading hides it");
        Assert.IsFalse(flyout.Rule.Waiting);
        Assert.IsNull(flyout.Clock.NextDue, "and lets the timer go");
        flyout.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(1, flyout.Hides);
    }

    [TestMethod]
    public void APressThatEndsWithFocusBackLeavesTheFlyoutShowing()
    {
        var flyout = new Flyout();
        flyout.Button.ButtonHeld = true;
        flyout.Rule.FocusMayHaveLeft();

        // Focus back with the flyout, or with ImageStash's own notification or Settings page, or a dialog, drag or edit
        // of its own keeping it: the window's ordinary rule no longer says focus has left.
        flyout.FocusLeft = false;
        flyout.Button.ButtonHeld = false;
        flyout.Clock.Advance(FocusLossHide.Reading);

        Assert.AreEqual(0, flyout.Hides, "G2: it hides only if focus is then still with another app");
        Assert.IsFalse(flyout.Rule.Waiting);
        Assert.IsNull(flyout.Clock.NextDue);
    }

    [TestMethod]
    public void ADragOverTheFlyoutHoldsItPastTheButtonsReleaseUntilItsDropWhichHidesItInTheDropsTurnWithoutReadingTheButton()
    {
        var flyout = new Flyout();
        flyout.Button.ButtonHeld = true;
        flyout.Rule.FocusMayHaveLeft();
        flyout.DragOver = true;

        // The button comes up over the flyout before the drop reaches it.
        flyout.Button.ButtonHeld = false;
        flyout.Clock.Advance(FocusLossHide.Reading * 4);

        Assert.AreEqual(0, flyout.Hides, "G2, G11: a drag over it keeps it until the drop is taken");

        flyout.DragOver = false;
        flyout.Button.ButtonHeld = true;
        var readings = flyout.Button.ButtonReadings;
        flyout.Rule.Dropped();

        Assert.AreEqual(1, flyout.Hides, "the drop is followed by the hide in its own turn, so its import ends in a hidden flyout whatever its speed");
        Assert.AreEqual(readings, flyout.Button.ButtonReadings, "without another reading of the button");
        Assert.IsFalse(flyout.Rule.Waiting);
        Assert.IsNull(flyout.Clock.NextDue);
    }

    [TestMethod]
    public void ADragAlreadyOverTheFlyoutWhenTheLossIsJudgedHoldsItThoughTheButtonReadsUpUntilItsDrop()
    {
        var flyout = new Flyout();

        // A quick drag: in over the flyout and its button let go before the loss of focus is judged, the drop not yet delivered.
        flyout.DragOver = true;
        flyout.Rule.FocusMayHaveLeft();
        flyout.Clock.Advance(FocusLossHide.Reading * 3);

        Assert.AreEqual(0, flyout.Hides, "G2, G11: the drag over it keeps it until the drop is taken");

        flyout.DragOver = false;
        flyout.Rule.Dropped();

        Assert.AreEqual(1, flyout.Hides, "then hidden in the drop's turn");
    }

    [TestMethod]
    public void ADropWithFocusBackLeavesTheFlyoutShowingAndADropOnAFlyoutThatWaitsOnNothingChangesNothing()
    {
        var waiting = new Flyout();
        waiting.Button.ButtonHeld = true;
        waiting.Rule.FocusMayHaveLeft();
        waiting.FocusLeft = false;

        waiting.Rule.Dropped();

        Assert.AreEqual(0, waiting.Hides, "G2: only if focus is still with another app");
        Assert.IsFalse(waiting.Rule.Waiting);

        var showing = new Flyout();
        showing.Rule.Dropped();

        Assert.AreEqual(0, showing.Hides, "a drop on a flyout that lost no focus hides nothing");
    }

    [TestMethod]
    public void ADragThatLeavesWithTheButtonHeldWaitsOnAndHidesOnceTheButtonIsUp()
    {
        var flyout = new Flyout();
        flyout.Button.ButtonHeld = true;
        flyout.Rule.FocusMayHaveLeft();
        flyout.DragOver = true;
        flyout.Clock.Advance(FocusLossHide.Reading * 2);

        flyout.DragOver = false;
        flyout.Clock.Advance(FocusLossHide.Reading * 2);

        Assert.AreEqual(0, flyout.Hides, "the button is still held");

        flyout.Button.ButtonHeld = false;
        flyout.Clock.Advance(FocusLossHide.Reading);

        Assert.AreEqual(1, flyout.Hides);
    }

    [TestMethod]
    public void ALaterLossWhileItWaitsStartsNoSecondWaitAndAStopEndsTheWaitWithNoHide()
    {
        var flyout = new Flyout();
        flyout.Button.ButtonHeld = true;
        flyout.Rule.FocusMayHaveLeft();

        flyout.Button.ButtonHeld = false;
        flyout.Rule.FocusMayHaveLeft();

        Assert.AreEqual(0, flyout.Hides, "the wait goes on to its next reading");
        flyout.Clock.Advance(FocusLossHide.Reading);
        Assert.AreEqual(1, flyout.Hides, "one hide");

        var stopped = new Flyout();
        stopped.Button.ButtonHeld = true;
        stopped.Rule.FocusMayHaveLeft();

        // The window hid for another asker (the open shortcut, a capture), or focus came back to it.
        stopped.Rule.Stop();
        stopped.Button.ButtonHeld = false;
        stopped.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.AreEqual(0, stopped.Hides, "nothing waits any more");
        Assert.IsNull(stopped.Clock.NextDue);
    }

    [TestMethod]
    public void TheButtonIsReadOnlyWhileTheOrdinaryRuleSaysFocusHasLeft()
    {
        var flyout = new Flyout { FocusLeft = false };
        flyout.Button.ButtonHeld = true;

        flyout.Rule.FocusMayHaveLeft();

        Assert.AreEqual(0, flyout.Button.ButtonReadings, "a frame change, a pinned window, a dialog, drag or edit of its own, or focus with ImageStash's own windows starts no wait");
        Assert.IsFalse(flyout.Rule.Waiting);
        Assert.AreEqual(0, flyout.Hides);
    }

    /// <summary>The window's side of the rule: its ordinary rule's answer, a drag from outside over it, and its one hide, counted.</summary>
    private sealed class Flyout
    {
        public Flyout() => Rule = new FocusLossHide(Button.Read, () => DragOver, () => FocusLeft, () => Hides++, Clock, run => run());

        public HeldButton Button { get; } = new();

        public SteppedClock Clock { get; } = new();

        public FocusLossHide Rule { get; }

        public bool FocusLeft { get; set; } = true;

        public bool DragOver { get; set; }

        public int Hides { get; private set; }
    }

    /// <summary>The pointer's primary button as the check holds it and lets it go, each reading counted.</summary>
    private sealed class HeldButton
    {
        public bool ButtonHeld { get; set; }

        public int ButtonReadings { get; private set; }

        public bool Read()
        {
            ButtonReadings++;
            return ButtonHeld;
        }
    }
}
