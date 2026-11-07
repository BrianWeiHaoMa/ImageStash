using Avalonia;
using ImageStash.Capture;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Record a clip over a real stash, the Screen seam's double whose picture changes at every grab, a preset region, the
/// frame and bar's stand-in, Media's GIF encoder and a clock the check steps (G15): the released region waiting until
/// Record, the bar's or a second press of the act's, with nothing written; the clip one GIF item named at Record in
/// local time, one image per sample, saved with no tag and no use, the window's scope, query and selection kept (F12,
/// F3, G10, G11); the window hidden before anything is grabbed and back only once the item is listed and no capture is
/// in progress any more, a screenshot during the wait or the recording included (G3); then, while Also copy is on, the
/// item on the clipboard as its facts decide, recording no use (G8, G10); then the capture notification with the clip's
/// length from Record, saying on the clipboard only when it is (G16); the tray told of the wait and the sampling (G15),
/// its Record a clip beginning the act, reading Start recording starting it and reading Stop recording stopping it
/// (G34); a press during the wait starting it and one while it samples stopping it, a screenshot's overlay up or not,
/// and starting nothing else otherwise; the cap and the pointer as the settings read them at the press (G32); a cancel,
/// while waiting or recording, silent (E3); a frame refused for the wait, a refused grab, a failing encoder, a stash not
/// open, a maximum length below a second, a commit refused and a step after it refused named above the tray, nothing
/// announced before the file is in place (G35, R9, G28).
/// </summary>
[TestClass]
public sealed class RecordClipTests
{
    private const string Clip = "Recording 2026-10-20 17.30.00";

    private const string AMinuteLater = "Recording 2026-10-20 17.31.00";

    private static readonly PixelRect Region = new(100, 50, 32, 24);

    [TestMethod]
    public async Task TheRegionWaitsForRecordAndTheClipIsOneGifItemNamedAtRecordThenCopiedAsAFileThenAnnouncedWithItsLength()
    {
        using var recording = await Recording();
        var older = await recording.Stash.Import("older");
        await recording.Listing.ShowScope(Scope.Favorites);
        await recording.Listing.SetQuery("older");
        recording.Listing.Focus(older.Id);
        var pictures = recording.Screen.EachGrab!;
        string? atTheFirstGrab = null;
        recording.Screen.EachGrab = index =>
        {
            atTheFirstGrab ??= string.Join(", ", recording.WindowSteps);
            return pictures(index);
        };
        var atTheWrite = "";
        recording.Clipboard.AtWrite = async () =>
        {
            var placed = (await recording.Stash.All()).Single(each => each.Id != older.Id);
            atTheWrite = $"{string.Join(", ", recording.WindowSteps)}; placed {File.Exists(placed.FilePath)}; {recording.Notifications.Shown.Count} notifications";
        };

        var acting = recording.RecordClip.StartOrStop();
        await recording.WhileWaiting();
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting }, recording.TrayRecording, "Start recording, no red dot, while the region waits (G34, G15)");
        CollectionAssert.AreEqual(new[] { "hide" }, recording.WindowSteps, "the window away while the region waits (G3)");
        Assert.IsEmpty(recording.Stash.WorkFiles(), "nothing written while the region waits (G28)");
        Assert.HasCount(1, recording.Screen.Grabs, "nothing sampled while the region waits: the overlay's grab alone");
        // Record pressed a minute after the release.
        recording.Stepped!.Pass(TimeSpan.FromMinutes(1));
        recording.Frame.PressRecord();
        await recording.WhileSampling();
        await Handing.Eventually(() => recording.TrayRecording.Count == 2);
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling }, recording.TrayRecording, "the red dot and Stop recording once it samples (G15)");
        // Four samples, at 0, 70, 140 and 210 ms, then Stop at 280 ms.
        recording.StepRecording(210);
        recording.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
        recording.Frame.PressStop();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        var item = (await recording.Stash.All()).Single(each => each.Id != older.Id);
        Assert.AreEqual(AMinuteLater, item.Name, "12:01:00 UTC, Record, in local time, not the release (F12)");
        Assert.AreEqual(AMinuteLater + ".gif", item.FileName);
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 32, 24, true, 4, 280), item.Facts, "one image per sample from Record, the last lasting until Stop (G15, F3)");
        Assert.IsEmpty(item.Tags);
        Assert.AreEqual(0L, recording.Uses(), "neither the capture nor its copy is a use (G10)");
        Assert.AreEqual("hide", atTheFirstGrab, "hidden before anything is grabbed (G3)");
        Assert.AreEqual("hide, restore Flyout; placed True; 0 notifications", atTheWrite, "copied once the window is back and the file in place, before the notification (G15, G3, R9)");
        var content = recording.Clipboard.Written.Single().Content;
        CollectionAssert.AreEqual(new[] { item.FilePath }, content.FilePaths.ToList());
        Assert.IsNull(content.Png, "an animation goes as a file only (G8)");
        Assert.IsNull(content.Bitmap);
        var notification = recording.Notifications.Shown.Single();
        Assert.AreEqual($"Saved “{AMinuteLater}” to your stash. It is on the clipboard too. It is 0.3 s long.", notification.Text, "in the stash, on the clipboard, and its length from Record (G16)");
        Assert.AreEqual("Undo", notification.UndoLabel);
        Assert.AreEqual("older", recording.Listing.Query, "the query as it was");
        CollectionAssert.AreEqual(new[] { older.Id }, recording.Listing.Selection.ToList(), "the selection kept");
        Assert.AreEqual(Scope.Favorites, recording.Listing.Scope, "the scope the user was in");
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, recording.TrayRecording, "the red dot and Stop recording gone at Stop, while the clip is saved (G15)");
        CollectionAssert.AreEqual(new[] { "show", "record", "saving", "close" }, recording.Frame.Calls.ToList());
        Assert.AreEqual((0, 0), (recording.Clipboard.Reads, recording.Clipboard.ImageReads), "the act reads nothing (N9)");
        Assert.IsEmpty(recording.Notices.Shown, "nothing in the window");
        Assert.IsEmpty(recording.Stash.WorkFiles());
        Assert.IsEmpty(recording.Failures);
        Assert.IsFalse(recording.Capture.AnyInProgress);
    }

    [TestMethod]
    public async Task AClipOfOneImageIsCopiedAsAStillAndAnnouncedWithTheLengthItWasRecordedFor()
    {
        using var recording = await Recording();

        await Record(recording, untilMs: 0);

        var item = (await recording.Stash.All()).Single();
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 32, 24, false, 1, 0), item.Facts, "one image is a still to Media's decoder");
        var content = recording.Clipboard.Written.Single().Content;
        CollectionAssert.AreEqual(new[] { item.FilePath }, content.FilePaths.ToList());
        Assert.IsNotNull(content.Png, "a still goes with its PNG bytes (G8)");
        Assert.AreEqual((32, 24), (content.Bitmap!.Width, content.Bitmap.Height), "and its bitmap");
        Assert.AreEqual($"Saved “{Clip}” to your stash. It is on the clipboard too. It is 0.1 s long.", recording.Notifications.Shown.Single().Text,
            "the length the encoder closed it at, not the facts' none (G16)");
        Assert.AreEqual(0L, recording.Uses());
    }

    [TestMethod]
    public async Task WithAlsoCopyOffNothingIsWrittenAndTheNotificationSaysInTheStashOnly()
    {
        using var recording = await Recording();
        recording.Settings.Change(settings => settings with { AlsoCopyCapture = false });

        await Record(recording);

        Assert.AreEqual(0, recording.Clipboard.Begun, "the clipboard untouched (G15)");
        Assert.AreEqual($"Saved “{Clip}” to your stash. It is 0.3 s long.", recording.Notifications.Shown.Single().Text, "nothing of the clipboard (G16)");
        Assert.HasCount(1, await recording.Stash.All());
    }

    [TestMethod]
    public async Task ACopyRefusedIsNamedBesideTheCaptureNotificationWhichSaysInTheStashOnly()
    {
        using var recording = await Recording();
        recording.Clipboard.FailNext = RecordingClipboard.Held;

        await Record(recording);

        CollectionAssert.AreEqual(
            new[] { $"Copy failed: {Clip}, {HeldClipboard.Reason}", $"Saved “{Clip}” to your stash. It is 0.3 s long." },
            recording.Notifications.Shown.Select(notice => notice.Text).ToList(),
            "the capture stands, and the notification claims nothing of the clipboard (G16, G35)");
        Assert.HasCount(1, await recording.Stash.All());
        Assert.AreEqual(0L, recording.Uses());
    }

    [TestMethod]
    public async Task AMaximumLengthBelowASecondIsRefusedByNameBeforeAnythingIsHiddenOrGrabbed()
    {
        using var recording = await Recording();
        recording.Settings.Change(settings => settings with { RecordingMaxSeconds = 0 });

        await recording.RecordClip.StartOrStop();

        Assert.AreEqual("Record a clip failed: settings.json, the recording's maximum length is not a number of seconds above zero",
            recording.Notifications.Shown.Single().Text, "the file that holds the cap named (G35, State)");
        Assert.AreEqual((0, 0), (recording.Screen.DisplayReads, recording.Screen.Grabs.Count), "nothing grabbed");
        Assert.IsEmpty(recording.WindowSteps, "nothing hidden");
        Assert.IsEmpty(recording.TrayRecording);
    }

    [TestMethod]
    public async Task NothingIsGrabbedOrHiddenWhileTheStashIsStillOpeningOrDidNotOpen()
    {
        using var recording = await Recording();
        var opening = new Listing(_ => { });
        var act = new RecordClip(opening, recording.Notifications, new CaptureNotification(recording.Notifications, opening, recording.Handoff, new NoWindowShown(), TestStash.ShortRetry),
            recording.Handoff, recording.Capture, recording.Settings, recording.Hiding, null!, _ => { }, TestStash.ShortRetry);

        await act.StartOrStop();
        opening.StashFailed(new Failure("Open the stash", "stash.db", "it is broken"));
        await act.StartOrStop();

        CollectionAssert.AreEqual(
            new[] { "Record a clip failed: the screen, your stash is still opening", "Record a clip failed: the screen, your stash could not be opened" },
            recording.Notifications.Shown.Select(notice => notice.Text).ToList());
        Assert.AreEqual((0, 0), (recording.Screen.DisplayReads, recording.Screen.Grabs.Count), "nothing grabbed (G28)");
        Assert.IsEmpty(recording.WindowSteps, "nothing hidden");
    }

    [TestMethod]
    public async Task ASecondPressWhileTheRegionWaitsStartsItAThirdWhileItSamplesStopsItAndNeitherDoesAnythingElse()
    {
        using var recording = await Recording();

        var acting = recording.RecordClip.StartOrStop();
        await recording.WhileWaiting();
        recording.Stepped!.Pass(TimeSpan.FromSeconds(4));
        await recording.RecordClip.StartOrStop();
        await recording.WhileSampling();
        recording.StepRecording(140);
        recording.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
        await recording.RecordClip.StartOrStop();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        var item = (await recording.Stash.All()).Single();
        Assert.AreEqual("Recording 2026-10-20 17.30.04", item.Name, "started at the second press, as Record starts it (F5, F12)");
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 32, 24, true, 3, 210), item.Facts, "ended at the third, as Stop ends it (G15)");
        Assert.AreEqual($"Saved “{item.Name}” to your stash. It is on the clipboard too. It is 0.2 s long.", recording.Notifications.Shown.Single().Text, "no refusal, only the clip");
        CollectionAssert.AreEqual(new[] { CaptureMode.Recording }, recording.Selection.Modes, "no second overlay");
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, recording.WindowSteps, "not hidden again");
        CollectionAssert.AreEqual(new[] { "show", "record", "saving", "close" }, recording.Frame.Calls.ToList(), "the bar names the saving from the press (G35)");
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, recording.TrayRecording);
    }

    [TestMethod]
    public async Task CancelWhileTheRegionWaitsAddsNothingSaysNothingWritesNothingAndBringsTheWindowBack()
    {
        using var recording = await Recording();

        var acting = recording.RecordClip.StartOrStop();
        await recording.WhileWaiting();
        recording.Frame.PressCancel();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsEmpty(await recording.Stash.All());
        Assert.IsEmpty(recording.Notifications.Shown, "a cancel says nothing (E3)");
        Assert.AreEqual(0, recording.Clipboard.Begun);
        Assert.IsEmpty(recording.Stash.WorkFiles(), "nothing written");
        CollectionAssert.AreEqual(new[] { "show", "close" }, recording.Frame.Calls.ToList(), "the frame and the bar gone (F4)");
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, recording.WindowSteps);
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.None }, recording.TrayRecording, "Record a clip again, never left reading Start recording (G34)");
        Assert.IsFalse(recording.Capture.AnyInProgress);
    }

    [TestMethod]
    public async Task AFrameThePlatformRefusesForTheWaitIsNamedOnTheScreenAndAddsNothing()
    {
        using var recording = await Recording();
        recording.Frame.ShowAnswer = new Failure("Record a clip", "ImageStash recording", "Windows would not show the frame around the region");

        await recording.RecordClip.StartOrStop();

        Assert.AreEqual("Record a clip failed: the screen, Windows would not show the frame around the region", recording.Notifications.Shown.Single().Text, "no auto name exists before Record (G35)");
        Assert.IsEmpty(await recording.Stash.All());
        Assert.IsEmpty(recording.Stash.WorkFiles(), "nothing written");
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, recording.WindowSteps, "the window back");
        Assert.IsEmpty(recording.TrayRecording, "the tray never read Start recording");
    }

    [TestMethod]
    public async Task TheCapAndThePointerAreTheSettingsReadAtThePressNotAtRecord()
    {
        using var recording = await Recording();
        recording.Settings.Change(settings => settings with { RecordingMaxSeconds = 1, RecordPointer = false });

        var acting = recording.RecordClip.StartOrStop();
        await recording.WhileWaiting();
        // The settings changed while the region waits, five seconds, longer than the cap.
        recording.Settings.Change(settings => settings with { RecordingMaxSeconds = 30, RecordPointer = true });
        recording.Stepped!.Pass(TimeSpan.FromSeconds(5));
        recording.Frame.PressRecord();
        await recording.WhileSampling();
        // Past the cap at 1,000 ms, which no tick of 70 ms lands on.
        recording.StepRecording(1_050);
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        var item = (await recording.Stash.All()).Single();
        Assert.AreEqual(1_000L, item.Facts.DurationMs, "the press's one-second cap, counted from Record (G32, G15)");
        Assert.AreEqual("Recording 2026-10-20 17.30.05", item.Name);
        Assert.IsTrue(recording.Screen.PointerAsked.All(asked => !asked), "the press's pointer setting, off (G15)");
        Assert.AreEqual($"Saved “{item.Name}” to your stash. It is on the clipboard too. It is 1.0 s long.", recording.Notifications.Shown.Single().Text, "its length from Record (G16)");
    }

    [TestMethod]
    public async Task TheTraysRecordAClipBeginsTheRecordingReadsStartRecordingWhileItWaitsAndStopRecordingWhileItSamplesEachChosenInTurn()
    {
        using var recording = await Recording();
        var tray = new RecordingTray();
        RecordClip? act = null;
        var presses = new List<Task>();
        var menu = new TrayMenu(tray, () => { }, () => { }, () => presses.Add(act!.StartOrStop()), () => { }, () => { }, () => { });
        // Bound as ImageStash binds them: the entry runs the act, and the act tells the menu of the sampling.
        act = new RecordClip(recording.Listing, recording.Notifications, recording.CaptureNotification, recording.Handoff, recording.Capture, recording.Settings, recording.Hiding, null!,
            menu.SetRecording, TestStash.ShortRetry);
        menu.Show(new GlobalShortcuts(recording.Settings, GlobalShortcuts.Bindings(() => { }, () => { }, () => { }, () => { }), new RecordingShortcuts(), _ => { }, action => action()));
        var entry = tray.Entries.Single(each => each.Header == TrayMenu.RecordClipHeader);

        entry.Chosen();
        await recording.WhileWaiting();
        CollectionAssert.AreEqual(new[] { (TrayMenu.RecordClipHeader, TrayMenu.StartRecordingHeader) }, tray.HeaderChanges, "the entry reads Start recording while the region waits (G34)");
        Assert.IsFalse(tray.Recording, "no red dot before the recording runs (G15)");
        entry.Chosen();
        await recording.WhileSampling();
        await Handing.Eventually(() => tray.Recording);
        Assert.AreEqual((TrayMenu.RecordClipHeader, TrayMenu.StopRecordingHeader), tray.HeaderChanges[^1], "Start recording started it; the entry reads Stop recording while the recording samples (G15, G34)");
        recording.StepRecording(140);
        recording.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
        entry.Chosen();
        await Task.WhenAll(presses).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.HasCount(3, presses, "each choice ran the act once");
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 32, 24, true, 3, 210), (await recording.Stash.All()).Single().Facts, "Stop recording ended it at the choice, as Stop does (G15)");
        CollectionAssert.AreEqual(
            new[] { (TrayMenu.RecordClipHeader, TrayMenu.StartRecordingHeader), (TrayMenu.RecordClipHeader, TrayMenu.StopRecordingHeader), (TrayMenu.RecordClipHeader, TrayMenu.RecordClipHeader) },
            tray.HeaderChanges, "and Record a clip again");
        Assert.IsFalse(tray.Recording, "the red dot gone");
    }

    [TestMethod]
    public async Task APressWhileTheOverlayIsUpOrTheClipIsSavingSaysNothingAndStartsNothing()
    {
        using var recording = await Recording();
        var releasing = new TaskCompletionSource();
        recording.Selection.Gate = releasing.Task;

        var acting = recording.RecordClip.StartOrStop();
        await Handing.Eventually(() => recording.Selection.Handed.Count == 1);
        await recording.RecordClip.StartOrStop();

        Assert.IsEmpty(recording.Notifications.Shown, "the overlay is the busy state (G35)");
        CollectionAssert.AreEqual(new[] { "hide" }, recording.WindowSteps, "neither hidden again nor brought back");
        Assert.HasCount(1, recording.Screen.Grabs, "no second overlay");
        releasing.SetResult();
        await recording.RecordFromTheWait();
        recording.StepRecording(0);
        recording.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
        recording.Frame.PressStop();
        // Stop pressed: the clip is being finished and saved, the bar naming it.
        Assert.IsTrue(recording.Capture.RecordingInProgress && !recording.Capture.RecordingSampling && !recording.Capture.RecordingWaiting, "saving");
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, recording.TrayRecording, "the red dot and Stop recording gone at Stop, before the clip is saved (G15)");
        await recording.RecordClip.StartOrStop();

        Assert.IsEmpty(recording.Notifications.Shown, "the bar is the busy state (G35)");
        await acting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.HasCount(1, await recording.Stash.All(), "one recording");
        Assert.AreEqual($"Saved “{Clip}” to your stash. It is on the clipboard too. It is 0.1 s long.", recording.Notifications.Shown.Single().Text);
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, recording.WindowSteps);
    }

    [TestMethod]
    public async Task APressWhileAScreenshotIsBeingSavedAfterItsReleaseIsRefusedByName()
    {
        // The store's retry outlasts the hold, so the screenshot's commit waits on it however slowly the press comes.
        using var recording = await Recording(storeRetry: TimeSpan.FromSeconds(30));
        Task screenshot;
        using (recording.Stash.HoldStore())
        {
            screenshot = recording.Screenshot.Take();
            await Handing.Eventually(() => recording.Stash.WorkFiles().Length == 1);
            await recording.RecordClip.StartOrStop();

            CollectionAssert.AreEqual(new[] { "Record a clip failed: the screen, the last screenshot is still being saved" },
                recording.Notifications.Shown.Select(notice => notice.Text).ToList(), "nothing on screen says the act is busy, so the refusal is named (G35)");
            CollectionAssert.AreEqual(new[] { "hide" }, recording.WindowSteps, "the screenshot's hide only");
            Assert.HasCount(1, recording.Screen.Grabs, "the screenshot's grab only (Approach)");
        }

        await screenshot;
        Assert.IsEmpty(recording.Selection.Modes.Where(mode => mode == CaptureMode.Recording), "no recording started");
        Assert.HasCount(1, await recording.Stash.All(), "the screenshot saved once the store is let go");
    }

    [TestMethod]
    public async Task AScreenshotDuringTheWaitIsAllowedAndTheWindowComesBackOnlyOnceTheWaitIsCancelled()
    {
        using var recording = await Recording();

        var acting = recording.RecordClip.StartOrStop();
        await recording.WhileWaiting();
        // A summon during the wait shows the window and forgets the slot, as the window's summons do (G3).
        recording.Hiding.Forget();
        recording.WindowSteps.Add("summon");
        await recording.Screenshot.Take();

        CollectionAssert.AreEqual(new[] { "hide", "summon", "hide" }, recording.WindowSteps, "hidden again for the screenshot, and its end does not bring it back while the region waits (G3)");
        Assert.HasCount(1, await recording.Stash.All(), "the screenshot saved (Approach)");
        Assert.IsTrue(recording.Capture.RecordingWaiting, "the region still waits");
        recording.Frame.PressCancel();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        CollectionAssert.AreEqual(new[] { "hide", "summon", "hide", "restore Flyout" }, recording.WindowSteps, "back once no capture is in progress any more");
        Assert.IsEmpty(recording.Notifications.Shown.Where(notice => notice.Text.StartsWith("Record a clip", StringComparison.Ordinal)), "the cancel said nothing");
    }

    [TestMethod]
    public async Task APressDuringTheWaitWithAScreenshotsOverlayUpStartsTheRecording()
    {
        using var recording = await Recording();

        var acting = recording.RecordClip.StartOrStop();
        await recording.WhileWaiting();
        recording.HidesAs = CaptureHide.Nothing;
        var releasing = new TaskCompletionSource();
        recording.Selection.Gate = releasing.Task;
        recording.Selection.Answer = null;
        var screenshot = recording.Screenshot.Take();
        await Handing.Eventually(() => recording.Selection.Handed.Count == 2);
        // The record shortcut pressed with the screenshot's overlay up: it starts the waiting recording (F5, G15).
        await recording.RecordClip.StartOrStop();
        await recording.WhileSampling();
        releasing.SetResult();
        await screenshot;
        recording.StepRecording(70);
        recording.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
        recording.Frame.PressStop();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 32, 24, true, 2, 140), (await recording.Stash.All()).Single().Facts, "the clip recorded from the press");
        Assert.IsEmpty(recording.Notifications.Shown.Where(notice => notice.Text.Contains("failed", StringComparison.Ordinal)), "no refusal");
    }

    [TestMethod]
    public async Task AScreenshotAfterASummonDuringTheRecordingHidesTheWindowAgainUntilTheRecordingEnds()
    {
        using var recording = await Recording();

        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        recording.StepRecording(0);
        // A summon during the recording shows the window and forgets the slot, as the window's summons do (G3).
        recording.Hiding.Forget();
        recording.WindowSteps.Add("summon");
        await recording.Screenshot.Take();

        CollectionAssert.AreEqual(new[] { "hide", "summon", "hide" }, recording.WindowSteps, "hidden again for the screenshot, and its end does not bring it back while the recording runs (G3)");
        Assert.HasCount(1, await recording.Stash.All(), "the screenshot saved");
        recording.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
        recording.Frame.PressStop();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        CollectionAssert.AreEqual(new[] { "hide", "summon", "hide", "restore Flyout" }, recording.WindowSteps, "back once no capture is in progress any more");
        CollectionAssert.AreEqual(new[] { Clip, "Screenshot 2026-10-20 17.30.00" }, (await recording.Stash.All()).Select(item => item.Name).Order().ToList());
    }

    [TestMethod]
    public async Task ARecordingStoppedWhileAScreenshotsOverlayIsUpLeavesTheWindowAwayUntilTheScreenshotEnds()
    {
        using var recording = await Recording();

        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        recording.StepRecording(0);
        // The window is hidden already, so the screenshot's start does nothing to it.
        recording.HidesAs = CaptureHide.Nothing;
        var releasing = new TaskCompletionSource();
        recording.Selection.Gate = releasing.Task;
        var screenshot = recording.Screenshot.Take();
        await Handing.Eventually(() => recording.Selection.Handed.Count == 2);
        recording.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
        // The record shortcut pressed with the screenshot's overlay up: it stops the recording (G15).
        await recording.RecordClip.StartOrStop();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.HasCount(1, await recording.Stash.All(), "the clip saved");
        CollectionAssert.AreEqual(new[] { "hide", "hide" }, recording.WindowSteps, "not back while the screenshot's overlay is up (G3)");
        releasing.SetResult();
        await screenshot;
        CollectionAssert.AreEqual(new[] { "hide", "hide", "restore Flyout" }, recording.WindowSteps, "back once the screenshot ends");
    }

    [TestMethod]
    public async Task CancelAddsNothingSaysNothingLeavesNoClipAndBringsTheWindowBack()
    {
        using var recording = await Recording();

        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        recording.StepRecording(140);
        recording.Frame.PressCancel();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsEmpty(await recording.Stash.All());
        Assert.IsEmpty(recording.Notifications.Shown, "a cancel says nothing (E3)");
        Assert.AreEqual(0, recording.Clipboard.Begun);
        Assert.IsEmpty(recording.Stash.WorkFiles(), "the clip discarded (G15)");
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, recording.WindowSteps);
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, recording.TrayRecording);
    }

    [TestMethod]
    public async Task AGrabTheScreenRefusesMidClipIsNamedUnchangedAndAddsNothing()
    {
        using var recording = await Recording();
        var refusal = new Failure("Grab the screen", "the screen area at 100, 50 (32 × 24)", "access is denied");
        var pictures = recording.Screen.EachGrab!;
        // Grab 0 is the overlay's; the clip's samples follow, and its second is refused.
        recording.Screen.EachGrab = index => index == 2 ? refusal : pictures(index);

        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        recording.StepRecording(140);
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(refusal.Sentence, recording.Notifications.Shown.Single().Text, "the seam's failure, unchanged (E5, G35)");
        Assert.IsEmpty(await recording.Stash.All());
        Assert.IsEmpty(recording.Stash.WorkFiles(), "the clip discarded");
        Assert.AreEqual("close", recording.Frame.Calls[^1], "the frame and the bar gone (F4)");
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, recording.WindowSteps);
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, recording.TrayRecording);
    }

    [TestMethod]
    public async Task AnEncoderFailingMidClipIsNamedOnTheClipAndAddsNothing()
    {
        var encoder = new RecordingClipEncoder { AppendAnswer = index => index == 1 ? new Failure("Write the clip", "Recording", "the disk is full") : Outcome.Done };
        using var recording = await Recording(encoder.Opener);

        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        recording.StepRecording(140);
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual($"Record a clip failed: {Clip}, the disk is full", recording.Notifications.Shown.Single().Text, "named as the action on the clip (G35)");
        Assert.IsEmpty(await recording.Stash.All());
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, recording.WindowSteps);
    }

    [TestMethod]
    public async Task AStoreAnotherProgramHoldsThroughTheCommitIsNamedAndLeavesNothing()
    {
        using var recording = await Recording();

        using (recording.Stash.HoldStore())
        {
            await Record(recording);
        }

        var notification = recording.Notifications.Shown.Single();
        Assert.AreEqual($"Add to the stash failed: {StashFolder.StoreFileName}, {HeldFileRetry.HeldReason}", notification.Text, "retried, then named unchanged (G35)");
        Assert.IsNull(notification.UndoLabel, "no capture notification (R9)");
        Assert.AreEqual(0, recording.Clipboard.Begun, "nothing to copy");
        Assert.IsEmpty(await recording.Stash.All());
        Assert.IsEmpty(recording.Stash.WorkFiles(), "the clip discarded");
        Assert.IsEmpty(recording.Stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, recording.WindowSteps);
    }

    [TestMethod]
    public async Task ARenameIntoTheRootRefusedIsNamedNotCopiedNotAnnouncedAndTheNextStartRemovesTheRow()
    {
        using var recording = await Recording();
        FileStream? heldClip;
        Task recorded;
        // The store held until the finished clip is held too, with read sharing, so its facts are read and the row is
        // committed once the store is let go, and only its rename into the root is refused.
        using (recording.Stash.HoldStore())
        {
            recorded = Record(recording);
            heldClip = await recording.Stash.HoldTheWorkFileOnceWritten();
        }

        using (heldClip)
        {
            await recorded;

            var notification = recording.Notifications.Shown.Single();
            Assert.AreEqual($"Add to the stash failed: {Clip}.gif, {HeldFileRetry.HeldReason}", notification.Text, "the file step's failure, unchanged");
            Assert.IsNull(notification.UndoLabel, "no capture notification before the file is in place (R9)");
            Assert.AreEqual(0, recording.Clipboard.Begun, "no copy of an item not in place");
            Assert.IsEmpty(await recording.Stash.All(), "committed, but no query returns it");
            Assert.HasCount(1, recording.Stash.WorkFiles(), "nothing discarded: the pending step names it");
        }

        await recording.Reopen();

        Assert.IsEmpty(await recording.Stash.All(), "absent, never half there (G28)");
        Assert.IsEmpty(recording.Stash.WorkFiles());
        Assert.IsEmpty(recording.Stash.RootFiles());
    }

    /// <summary>
    /// An open stash whose captures run on a clock the check steps, reading local time five and a half hours ahead; the
    /// region the check drags; the display's picture different at every grab, as a live screen's is.
    /// </summary>
    private static async Task<ListedStash> Recording(ClipOpener? opener = null, TimeSpan? storeRetry = null)
    {
        var recording = await ListedStash.OpenRecording(opener, storeRetry);
        recording.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, Region);
        MediaFrame[] pictures = [recording.Screen.Paint(RecordingScreen.Primary, seed: 1), recording.Screen.Paint(RecordingScreen.Primary, seed: 2)];
        recording.Screen.EachGrab = index => Outcome<MediaFrame>.Of(pictures[index % 2]);
        return recording;
    }

    /// <summary>One recording through the act: Record pressed on the waiting region, a sample at each tick up to <paramref name="untilMs"/>, then Stop 70 ms after the last.</summary>
    private static async Task Record(ListedStash recording, long untilMs = 210)
    {
        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        recording.StepRecording(untilMs);
        recording.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
        recording.Frame.PressStop();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
