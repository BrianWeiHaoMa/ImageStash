using Avalonia;
using ImageStash.Capture;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// A notification's life, above the tray and on another display, by the rule a window notice's life follows (G16): it
/// lasts as its words need, six seconds or longer, up to twenty, from <see cref="Notices.LastingFor"/>, and longer while
/// the pointer holds it or its field has focus; one that names a refusal or a failure stays past its time until its ×,
/// its Undo ending with its time, so nothing waits on it; one that names none leaves at its time.
/// </summary>
[TestClass]
public sealed class NotificationLifeTests
{
    private static readonly Display Right = new(new PixelRect(1920, 0, 1920, 1080), new PixelRect(1920, 0, 1920, 1040), 1, IsPrimary: false);

    private readonly SetClock clock = new();

    [TestMethod]
    public void APasteFallbacksLongSentenceLastsAsItsWordsNeedAboveTheTrayAndOnAnotherDisplay()
    {
        var tray = new Notices(clock);
        var fallback = new PasteFallback(tray);
        Notices? elsewhere = null;
        fallback.Opened += (stack, _) => elsewhere = stack;
        fallback.Raise(PasteTargetReasons.RightsUnreadable, "Slack", stoodOn: null);
        fallback.Raise(PasteTargetReasons.RightsUnreadable, "Slack", Right);
        var above = tray.Shown.Single();
        var there = elsewhere!.Shown.Single();
        var lasting = Notices.LastingFor($"{above.Heading} {above.Text}");
        Assert.IsGreaterThan(Notices.Lasting + TimeSpan.FromSeconds(1), lasting, "the sentence runs to about twenty words");

        clock.Now += lasting - TimeSpan.FromMilliseconds(1);
        tray.Sweep();
        elsewhere.Sweep();
        Assert.AreSame(above, tray.Shown.Single(), "above the tray: still there a moment before its words' time ends");
        Assert.AreSame(there, elsewhere.Shown.Single(), "on another display: the same");
        clock.Now += TimeSpan.FromMilliseconds(1);
        tray.Sweep();
        elsewhere.Sweep();
        Assert.IsEmpty(tray.Shown, "\"Copied · press Ctrl+V\" names no failure: it leaves at its time");
        Assert.IsEmpty(elsewhere.Shown);
    }

    [TestMethod]
    public void AShortNotificationLastsSixSecondsAndThePointerAndAFieldsFocusStillHoldIt()
    {
        var tray = new Notices(clock);
        var plain = tray.Raise("Screenshot stashed here");
        clock.Now += Notices.Lasting - TimeSpan.FromMilliseconds(1);
        tray.Sweep();
        Assert.HasCount(1, tray.Shown, "six seconds, the least");
        clock.Now += TimeSpan.FromMilliseconds(1);
        tray.Sweep();
        Assert.IsEmpty(tray.Shown, "and no longer for three words");

        var hovered = tray.Raise("Screenshot stashed here");
        var focused = tray.Raise("Recording stashed here");
        tray.Hover(hovered, over: true);
        tray.FieldFocus(focused, focused: true);
        clock.Now += Notices.LastingAtMost * 2;
        tray.Sweep();
        CollectionAssert.AreEqual(new[] { hovered, focused }, tray.Shown.ToList(), "held by the pointer and by its field's focus");
        Assert.IsNotNull(plain);
    }

    [TestMethod]
    public void ARefusalAndAFailureAboveTheTrayStayPastTwentySecondsUntilTheirClose()
    {
        var tray = new Notices(clock);
        var refused = tray.Raise(Failure.Refusal(StashClipboardImage.Action, "the clipboard", "it holds no image"));
        var failed = tray.Raise(new Failure(Screenshot.Action, "Screenshot 2026-11-27 08.00.00", "the disk is full"));
        var said = tray.Raise("Copied · press Ctrl+V");

        clock.Now += Notices.LastingAtMost + TimeSpan.FromMinutes(1);
        tray.Sweep();
        CollectionAssert.AreEqual(new[] { refused, failed }, tray.Shown.ToList(), "a refusal and a failure stay; what names none has left");

        tray.Close(refused);
        tray.Close(failed);
        Assert.IsEmpty(tray.Shown, "each leaves at its ×");
        Assert.IsNotNull(said);
    }

    [TestMethod]
    public void AStayingNotificationsUndoEndsWithItsTimeAndItsLeavingRunsOnceThen()
    {
        var tray = new Notices(clock);
        var left = 0;
        var notice = tray.Raise("Couldn't finish replacing “cat”: the disk is full.", "Undo", () => Task.FromResult(Outcome.Done), () =>
        {
            left++;
            return Task.CompletedTask;
        }, namesAProblem: true);

        clock.Now += Notices.LastingFor(notice.Text);
        tray.Sweep();

        Assert.AreSame(notice, tray.Shown.Single(), "it stays");
        Assert.IsFalse(tray.OffersUndo(notice), "an Undo acts for the notice's time, not for as long as it stays past it");
        Assert.AreEqual(1, left, "what it was given for leaving runs at its time, not only at its ×");
        tray.Close(notice);
        Assert.AreEqual(1, left, "and once");
    }

    [TestMethod]
    public async Task ACaptureCardLeavesAtItsTimeAndItsUndoWithIt()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        listed.CaptureNotification.Raise(cat, CaptureKind.Screenshot, onClipboard: true, lengthMs: null);
        var card = listed.Notifications.Shown.Single();

        listed.Stash.Clock.Now += Notices.LastingFor(card.Words);
        listed.Notifications.Sweep();

        Assert.IsEmpty(listed.Notifications.Shown, "a capture's card names no failure and leaves at its time");
        Assert.IsFalse(listed.Notifications.OffersUndo(card));
    }

    [TestMethod]
    public async Task AMoveBeginsWhileAFailureStaysAboveTheTrayPastItsTime()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat", MediaFixtures.Bytes("still.png"));
        var chosen = MovingStash.Chosen(listed.Stash);
        listed.Dialogs.PickFolderAnswer = () => Task.FromResult<string?>(chosen);
        var failure = listed.Turns.Act(() => listed.Notifications.Raise("Couldn't finish replacing “cat”: the disk is full.", "Undo",
            () => Task.FromResult(Outcome.Done), namesAProblem: true));
        listed.Stash.Clock.Now += Notices.LastingFor(failure.Words);
        listed.Turns.Act(listed.Notifications.Sweep);

        var moved = await listed.Moving.MoveTo(null!).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        Assert.AreSame(failure, listed.Notifications.Shown.Single(), "still standing: a Move waits on an Undo still offered, never on a failure staying");
    }
}
