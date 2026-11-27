using Avalonia;
using ImageStash.Capture;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

[TestClass]
public sealed class NotificationSameActionTests
{
    private static readonly TimeSpan PastAnyReadingTime = Notices.LastingAtMost + TimeSpan.FromSeconds(1);
    private static readonly Display Right = new(new PixelRect(1920, 0, 2560, 1440), new PixelRect(1920, 0, 2560, 1392), 1.5, IsPrimary: false);
    private const string NoClipboardImage = "Can't stash clipboard image: the clipboard holds no image.";

    [TestMethod]
    public async Task OneScreenshotCanReportACopyFailureAndTheSavedScreenshot()
    {
        using var listed = await ListedStash.Open(SetClock.Ahead);
        SelectScreenshotRegion(listed);
        listed.Clipboard.FailNext = RecordingClipboard.Held;

        await listed.Screenshot.Take();

        var tray = listed.Notifications;
        Assert.HasCount(2, tray.Shown, "One screenshot shows both its copy failure and its saved capture.");
        var failure = tray.Shown[0];
        Assert.StartsWith("Couldn't copy", failure.Text);
        Assert.StartsWith("Screenshot stashed:", tray.Shown[1].Text);
        Assert.IsTrue(tray.OffersUndo(tray.Shown[1]), "The saved screenshot offers Undo alongside its copy failure.");

        listed.Stash.Clock.Now += PastAnyReadingTime;
        tray.Sweep();

        CollectionAssert.AreEqual(new[] { failure }, tray.Shown.ToArray(), "The screenshot's copy failure stays after the saved capture expires.");
    }

    [TestMethod]
    public async Task TheNextScreenshotReplacesTheCopyFailureAndKeepsTheEarlierCaptureUndo()
    {
        using var listed = await ListedStash.Open(SetClock.Ahead);
        SelectScreenshotRegion(listed);
        listed.Clipboard.FailNext = RecordingClipboard.Held;
        await listed.Screenshot.Take();
        var tray = listed.Notifications;
        var failure = tray.Shown.Single(notice => notice.Text.StartsWith("Couldn't copy", StringComparison.Ordinal));
        var firstCapture = tray.Shown.Single(tray.OffersUndo);
        listed.Stash.Clock.Now += TimeSpan.FromSeconds(1);

        await listed.Screenshot.Take();

        Assert.IsTrue(tray.OffersUndo(firstCapture), "The earlier screenshot still offers Undo.");
        Assert.IsFalse(tray.Shown.Contains(failure), "The next screenshot replaces the earlier screenshot's copy failure.");
        Assert.HasCount(2, tray.Shown, "Both saved captures still show.");
        Assert.IsTrue(tray.OffersUndo(tray.Shown.Last()), "The new screenshot offers its own Undo.");
    }

    [TestMethod]
    public async Task ARecordingRefusalReplacesARecordingFailureEvenWhenAScreenshotNamedTheSameFailure()
    {
        using var listed = await ListedStash.Open();
        var grab = new Failure("Grab the screen", "the display", "access is denied");
        listed.Screen.Refuse(RecordingScreen.Primary, grab);
        var tray = listed.Notifications;
        await listed.Screenshot.Take();
        var screenshot = tray.Shown.Single();
        await listed.RecordClip.StartOrStop();
        Assert.HasCount(2, tray.Shown, "Screenshot and recording failures coexist even with identical words.");
        var recording = tray.Shown.Last();
        Assert.AreEqual(grab.Sentence, screenshot.Text);
        Assert.AreEqual(grab.Sentence, recording.Text);

        listed.Settings.Change(settings => settings with { RecordingMaxSeconds = 0 });
        await listed.RecordClip.StartOrStop();

        Assert.IsTrue(tray.Shown.Contains(screenshot), "The recording refusal leaves the screenshot failure in place.");
        Assert.IsFalse(tray.Shown.Contains(recording), "The recording refusal replaces the previous recording failure.");
        Assert.HasCount(2, tray.Shown);
        Assert.StartsWith("Couldn't record a clip (settings.json):", tray.Shown.Last().Text);
    }

    [TestMethod]
    public async Task TheNoImageRefusalStaysUntilItsClose()
    {
        using var listed = await ListedStash.Open();

        await listed.StashClipboardImage.Stash();
        var tray = listed.Notifications;
        var refusal = tray.Shown.Single();
        Assert.AreEqual(NoClipboardImage, refusal.Text);
        listed.Stash.Clock.Now += PastAnyReadingTime;
        tray.Sweep();

        CollectionAssert.AreEqual(new[] { refusal }, tray.Shown.ToArray(), "The no-image refusal stays past its reading time.");
        tray.Close(refusal);
        Assert.IsEmpty(tray.Shown, "The refusal leaves on its ×.");
    }

    [TestMethod]
    [DataRow(false, DisplayName = "The no-image refusal is still within its reading time")]
    [DataRow(true, DisplayName = "The no-image refusal is past its reading time")]
    public async Task StashingAnImageReplacesTheEarlierNoImageRefusal(bool afterReadingTime)
    {
        using var listed = await ListedStash.Open(SetClock.Ahead);
        await listed.StashClipboardImage.Stash();
        var tray = listed.Notifications;
        var refusal = tray.Shown.Single();
        Assert.AreEqual(NoClipboardImage, refusal.Text);
        if (afterReadingTime)
        {
            listed.Stash.Clock.Now += PastAnyReadingTime;
            tray.Sweep();
            Assert.IsTrue(tray.Shown.Contains(refusal), "The no-image refusal is still showing when the next act starts.");
        }

        PutImageOnClipboard(listed);

        await listed.StashClipboardImage.Stash();

        Assert.IsFalse(tray.Shown.Contains(refusal), "Stashing an image replaces the earlier no-image refusal.");
        var saved = tray.Shown.Single();
        Assert.StartsWith("Clipboard image stashed:", saved.Text);
        Assert.IsTrue(tray.OffersUndo(saved));
    }

    [TestMethod]
    [DataRow(false, DisplayName = "The newer notification is visible")]
    [DataRow(true, DisplayName = "The newer notification has closed")]
    public async Task AnOlderClipboardFailureThatAnswersLateNeverShows(bool closeNewer)
    {
        using var listed = await ListedStash.Open();
        var tray = listed.Notifications;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listed.Clipboard.HoldRead = release.Task;
        var older = listed.StashClipboardImage.Stash();
        var lateFailure = new Failure(StashClipboardImage.Action, "the clipboard", HeldClipboard.Reason);
        var lateWasShown = false;
        tray.Changed += () => lateWasShown |= tray.Shown.Any(notice => notice.Text == lateFailure.Sentence);

        try
        {
            Assert.AreEqual(1, listed.Clipboard.ImageReads, "The older act has reached its held read.");
            Assert.IsFalse(older.IsCompleted);
            listed.Clipboard.HoldRead = null;
            await listed.StashClipboardImage.Stash();
            var newer = tray.Shown.Single();
            Assert.AreEqual(NoClipboardImage, newer.Text);
            if (closeNewer)
            {
                tray.Close(newer);
            }

            listed.Clipboard.ImageReading = lateFailure;
        }
        finally
        {
            release.SetResult();
            await older;
        }

        Assert.IsFalse(lateWasShown, "The older clipboard failure never shows after a newer act has raised.");
        Assert.HasCount(closeNewer ? 0 : 1, tray.Shown);
    }

    [TestMethod]
    public async Task ALongCaptureNotificationKeepsItsUndoForItsReadingTime()
    {
        using var listed = await ListedStash.Open(SetClock.Ahead);
        PutImageOnClipboard(listed);
        await listed.StashClipboardImage.Stash();
        var tray = listed.Notifications;
        var saved = tray.Shown.Single();
        var readingTime = Notices.LastingFor(saved.Text);
        var tick = TimeSpan.FromMilliseconds(1);
        Assert.IsGreaterThan(Notices.Lasting + tick, readingTime, "The capture's words call for more than the minimum time.");

        listed.Stash.Clock.Now += readingTime - tick;
        tray.Sweep();
        Assert.IsTrue(tray.OffersUndo(saved), "The capture still offers Undo just before its reading time ends.");

        listed.Stash.Clock.Now += tick;
        tray.Sweep();
        Assert.IsEmpty(tray.Shown, "The success notification leaves when its reading time ends.");
    }

    [TestMethod]
    public async Task AnUnreadableOpenWithRewriteStaysUntilItsClose()
    {
        using var listed = await ListedStash.Open();
        var failure = await NameUnreadableRewrite(listed);
        var tray = listed.Notifications;

        listed.Stash.Clock.Now += PastAnyReadingTime;
        tray.Sweep();

        CollectionAssert.AreEqual(new[] { failure }, tray.Shown.ToArray(), "The unreadable rewrite failure stays past its reading time.");
        tray.Close(failure);
        Assert.IsEmpty(tray.Shown, "The rewrite failure leaves on its ×.");
    }

    [TestMethod]
    public async Task AnOpenWithAdoptionReplacesAnEarlierRewriteFailureForAnotherItem()
    {
        using var listed = await ListedStash.Open();
        var failure = await NameUnreadableRewrite(listed);
        var dog = await listed.Stash.ImportFixture("dog", "transparent.png");
        await listed.Listing.Refresh();

        var adopted = await OpenedWith.ReplacedThroughTheMenu(listed, dog, MediaFixtures.DistinctPng(12, 6, 4));

        var tray = listed.Notifications;
        Assert.IsFalse(tray.Shown.Contains(failure), "The adoption of another item replaces the earlier rewrite failure.");
        Assert.AreSame(adopted, tray.Shown.Single());
        Assert.IsTrue(tray.OffersUndo(adopted));
    }

    [TestMethod]
    public void UnavailableShortcutsStayPastTheirReadingTime()
    {
        var started = StartWithUnavailableShortcuts();
        var tray = started.Notifications;
        Assert.HasCount(4, tray.Shown);
        var unavailable = tray.Shown.ToArray();

        started.Clock.Advance(PastAnyReadingTime);
        tray.Sweep();

        CollectionAssert.AreEqual(unavailable, tray.Shown.ToArray(), "All four unavailable shortcuts stay past their reading time.");
        tray.Close(unavailable[0]);
        CollectionAssert.AreEqual(unavailable[1..], tray.Shown.ToArray(), "Closing one unavailable shortcut leaves the other three.");
    }

    [TestMethod]
    [DataRow("Ctrl+Alt+Space")]
    [DataRow("Ctrl+PrtScn")]
    [DataRow("Ctrl+Shift+PrtScn")]
    [DataRow("Win+Alt+V")]
    public void ANewReasonReplacesOnlyTheSameUnavailableShortcut(string combination)
    {
        var started = StartWithUnavailableShortcuts();
        var tray = started.Notifications;
        Assert.HasCount(4, tray.Shown);
        var previous = tray.Shown.Single(notice => notice.Text.StartsWith(combination + " ", StringComparison.Ordinal));
        var otherShortcuts = tray.Shown.Where(notice => notice != previous).ToArray();
        Assert.StartsWith(combination + " is used by another app", previous.Text);
        var shortcut = Shortcut.Parse(combination).Value!;

        started.Seam.Refuse(shortcut, ShortcutRefusal.TypesACharacter("x"));
        started.Shortcuts.BindFromSettings();

        Assert.IsFalse(tray.Shown.Contains(previous), $"The new refusal replaces the previous one for {combination}.");
        Assert.HasCount(4, tray.Shown);
        CollectionAssert.AreEqual(otherShortcuts, tray.Shown.Take(3).ToArray(), "The other three unavailable shortcuts stay in place.");
        Assert.StartsWith(combination + " types x", tray.Shown.Last().Text);
    }

    [TestMethod]
    [DataRow(false, false, DisplayName = "Copied fallback above the tray, which names no failure")]
    [DataRow(false, true, DisplayName = "Clipboard failure above the tray")]
    [DataRow(true, false, DisplayName = "Copied fallback on another display, which names no failure")]
    [DataRow(true, true, DisplayName = "Clipboard failure on another display")]
    public async Task APastesClipboardFailureStaysUntilItsCloseAndCopiedLeavesAtItsTime(bool otherDisplay, bool clipboardFails)
    {
        using var handing = await Handing.Open("photo.jpg");
        Notices? elsewhere = null;
        handing.Stacks.Opened += (stack, _) => elsewhere = stack;

        var outcome = await FailPaste(handing, otherDisplay ? Right : null, clipboardFails);

        var stack = otherDisplay ? elsewhere : handing.Notifications;
        Assert.IsNotNull(stack, "The failed paste opened a stack on the other display.");
        if (otherDisplay)
        {
            Assert.IsEmpty(handing.Notifications.Shown);
        }

        var failure = PasteNotification(stack, outcome, clipboardFails);
        handing.Stash.Clock.Now += PastAnyReadingTime;
        stack.Sweep();

        if (!clipboardFails)
        {
            Assert.IsEmpty(stack.Shown, "\"Copied · press Ctrl+V\" names no failure and leaves at its time.");
            return;
        }

        CollectionAssert.AreEqual(new[] { failure }, stack.Shown.ToArray(), "The failed paste notification stays past its reading time.");
        stack.Close(failure);
        Assert.IsEmpty(stack.Shown, "The failed paste notification leaves on its ×.");
    }

    [TestMethod]
    [DataRow(true, DisplayName = "Copied fallback on another display, which names no failure and keeps its time, then clipboard failure above the tray")]
    [DataRow(false, DisplayName = "Clipboard failure above the tray, then copied fallback on another display")]
    public async Task TheNextFailedPasteReplacesTheEarlierNotificationAcrossDisplays(bool elsewhereFirst)
    {
        using var handing = await Handing.Open("photo.jpg");
        Notices? elsewhere = null;
        handing.Stacks.Opened += (stack, _) => elsewhere = stack;

        var first = await FailPaste(handing, elsewhereFirst ? Right : null, clipboardFails: !elsewhereFirst);
        var previousStack = elsewhereFirst ? elsewhere : handing.Notifications;
        Assert.IsNotNull(previousStack);
        PasteNotification(previousStack, first, clipboardFailed: !elsewhereFirst);

        var second = await FailPaste(handing, elsewhereFirst ? null : Right, clipboardFails: elsewhereFirst);

        var nextStack = elsewhereFirst ? handing.Notifications : elsewhere;
        Assert.IsNotNull(nextStack);
        Assert.AreNotSame(previousStack, nextStack, "The two pastes put their notifications on different displays.");
        PasteNotification(nextStack, second, clipboardFailed: elsewhereFirst);
        if (elsewhereFirst)
        {
            Assert.HasCount(1, previousStack.Shown, "A notification naming no failure is not taken over; it leaves at its time.");
            return;
        }

        Assert.IsEmpty(previousStack.Shown, "The next failed paste replaces the earlier failure across displays.");
    }

    [TestMethod]
    public async Task PasteFailuresInTheStashWindowAndAboveTheTrayNeverReplaceOneAnother()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.ImportFixture("photo", "still.png");
        listed.Clipboard.FailNext = RecordingClipboard.Held;
        listed.PasteHidesFlyout = false;
        var first = await listed.PasteIntoTarget([item]);
        var inWindow = PasteNotification(listed.Notices, first, clipboardFailed: true);

        listed.Clipboard.FailNext = RecordingClipboard.Held;
        listed.PasteHidesFlyout = true;
        var second = await listed.PasteIntoTarget([item]);
        var aboveTray = PasteNotification(listed.Notifications, second, clipboardFailed: true);
        Assert.AreSame(inWindow, listed.Notices.Shown.Single(), "The hidden flyout's failure notification leaves the earlier window notice.");

        listed.Clipboard.FailNext = RecordingClipboard.Held;
        listed.PasteHidesFlyout = false;
        var third = await listed.PasteIntoTarget([item]);
        Assert.AreNotSame(inWindow, PasteNotification(listed.Notices, third, clipboardFailed: true), "The next window failure replaces the earlier window failure.");
        Assert.AreSame(aboveTray, listed.Notifications.Shown.Single(), "The next window failure leaves the tray notification.");
    }

    private static void SelectScreenshotRegion(ListedStash listed) =>
        listed.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, new PixelRect(100, 50, 30, 20));

    private static void PutImageOnClipboard(ListedStash listed) =>
        listed.Clipboard.ImageReading = Outcome<ClipboardImage?>.Of(new ClipboardImage(MediaFixtures.Bytes("transparent.png"), null));

    private static async Task<Notice> NameUnreadableRewrite(ListedStash listed)
    {
        var item = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        var chosen = await listed.ItemMenu.Act(ItemMenuAction.OpenWith, null!, [item], null,
            new PasteHide(() => throw new AssertFailedException("Open with… hides nothing")), _ => { });
        Assert.IsTrue(chosen.Succeeded, chosen.Failure?.Sentence);
        await OpenedWith.RewriteWhenFree(listed.Dialogs.OpenedWith.Single(), OpenedWith.Garbage());
        await OpenedWith.Eventually(listed, () => listed.Notifications.Shown.Count > 0);
        await Handing.Eventually(() => !listed.OpenWith.Running, OpenedWith.RewriteAnswered);

        var failure = listed.Notifications.Shown.Single();
        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat.png", MediaDecoder.UnreadableReason).Sentence, failure.Text);
        return failure;
    }

    private static StartedShortcuts StartWithUnavailableShortcuts()
    {
        var defaults = new Shortcuts();
        return StartedShortcuts.Start(held: [.. new[] { defaults.Open, defaults.Screenshot, defaults.Record, defaults.StashClipboardImage }
            .Select(combination => Shortcut.Parse(combination).Value!)]);
    }

    private static Task<Outcome> FailPaste(Handing handing, Display? display, bool clipboardFails)
    {
        handing.Target.Confirms = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.Gone);
        handing.Clipboard.FailNext = clipboardFails ? RecordingClipboard.Held : null;
        return handing.Handoff.Paste(null!, handing.Items, RecordingTarget.Relay, handing.Hide with { StoodOn = display });
    }

    private static Notice PasteNotification(Notices stack, Outcome outcome, bool clipboardFailed)
    {
        Assert.IsFalse(outcome.Succeeded);
        var notice = stack.Shown.Single();
        if (clipboardFailed)
        {
            Assert.IsNull(notice.Heading);
            Assert.AreEqual(HeldClipboard.Reason, outcome.Failure?.Reason);
            Assert.AreEqual(outcome.Failure!.Sentence, notice.Text);
        }
        else
        {
            Assert.AreEqual("Copied · press Ctrl+V", notice.Heading);
            Assert.AreEqual("ImageStash did not paste into Relay because its window is gone.", notice.Text);
        }

        return notice;
    }
}
