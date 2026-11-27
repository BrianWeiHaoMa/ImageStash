using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Which stack a notification stands in (G16, F4): the tray's for the primary display or none; else one stack per other
/// display, opened once, whoever raises into it, the paste fallback and a capture's card together; and every stack it
/// holds waited on by Move, Export stash… and Import stash… while an Undo stands there (State: Move, G16).
/// </summary>
[TestClass]
public sealed class NotificationStacksTests
{
    [TestMethod]
    public void ThePrimaryDisplayOrNoneIsTheTraysStackAndAnotherDisplayHasOneStackOpenedOnce()
    {
        var tray = new Notices(new SetClock());
        var stacks = new NotificationStacks(tray);
        var opened = new List<(Notices Stack, Display Display)>();
        stacks.Opened += (stack, display) => opened.Add((stack, display));

        Assert.AreSame(tray, stacks.On(null));
        Assert.AreSame(tray, stacks.On(RecordingScreen.Primary));
        Assert.IsEmpty(opened);

        var second = stacks.On(TwoDisplays.Second);
        // Found by its bounds: a display reported again with another working area is the same display.
        var again = stacks.On(TwoDisplays.Second with { WorkingArea = TwoDisplays.Second.Bounds });

        Assert.AreSame(second, again);
        Assert.AreNotSame(tray, second);
        CollectionAssert.AreEqual(new[] { (second, TwoDisplays.Second) }, opened);
        CollectionAssert.AreEqual(new[] { tray, second }, stacks.All.ToList());
    }

    [TestMethod]
    public async Task APasteFallbackAndAScreenshotsCardOnTheSecondDisplayStandInOneStackOldestFirst()
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;

        listed.Handoff.Fallback.Raise(PasteTargetReasons.Gone, "Relay", TwoDisplays.Second);
        await displays.Screenshot(TwoDisplays.Second);

        Assert.HasCount(1, displays.Opened, "one stack, one notifications window, for the display");
        var shown = displays.OnSecond.Shown;
        Assert.HasCount(2, shown);
        Assert.AreEqual(PasteFallback.Copied, shown[0].Heading, "the paste fallback first");
        StringAssert.StartsWith(shown[1].Text, "Screenshot stashed:", "then the card");
        Assert.IsEmpty(listed.Notifications.Shown);
    }

    [TestMethod]
    public async Task AMoveWaitsOnACardsUndoInAStackOpenedSinceStartAndBeginsOnceItHasEnded()
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;
        await displays.Screenshot(TwoDisplays.Second);
        var card = displays.OnSecond.Shown.Single();
        listed.Dialogs.PickFolderAnswer = () => Task.FromResult<string?>(MovingStash.Chosen(listed.Stash));

        var moving = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the Undo of a notification before moving your stash…");
        Assert.IsFalse(moving.IsCompleted, "the card's Undo still stands on the second display (G16)");

        listed.Stash.Clock.Now += Notices.LastingFor(card.Words);
        listed.Turns.Act(displays.OnSecond.Sweep);
        var moved = await moving.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        Assert.HasCount(1, await listed.Stash.All(), "the screenshot moved with the stash");
    }

    [TestMethod]
    public async Task AFailureStayingPastItsTimeOnTheSecondDisplayHoldsNoMoveUp()
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;
        await listed.Stash.Import("cat");
        listed.Dialogs.PickFolderAnswer = () => Task.FromResult<string?>(MovingStash.Chosen(listed.Stash));
        listed.Turns.Act(() => listed.Handoff.Fallback.RaiseFailure(new Failure("Copy", "“cat”", "the clipboard is held open by another program"), TwoDisplays.Second));
        var failure = displays.OnSecond.Shown.Single();
        listed.Stash.Clock.Now += Notices.LastingFor(failure.Words);
        listed.Turns.Act(displays.OnSecond.Sweep);

        var moved = await listed.Moving.MoveTo(null!).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        Assert.AreSame(failure, displays.OnSecond.Shown.Single(), "still standing: only an Undo still offered is waited on");
    }

    [TestMethod]
    public async Task AnExportWaitsOnACardsUndoOnTheSecondDisplay()
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;
        await displays.Screenshot(TwoDisplays.Second);
        var card = displays.OnSecond.Shown.Single();
        var archive = listed.Stash.Outside("mine.zip");
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(archive);

        var exporting = listed.Transferring.Export(null!);
        await Handing.Eventually(() => listed.Transferring.ExportLine == "Waiting for the Undo of a notification before exporting your stash…");
        Assert.IsFalse(File.Exists(archive));

        listed.Stash.Clock.Now += Notices.LastingFor(card.Words);
        listed.Turns.Act(displays.OnSecond.Sweep);
        var exported = await exporting.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(exported.Succeeded, exported.Failure?.Sentence);
        Assert.IsTrue(File.Exists(archive));
    }
}
