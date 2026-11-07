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
/// Take a screenshot over a real stash, a preset screen and a preset region (G14): the region's frozen pixels one PNG
/// item named at the release in local time (F12, F3), saved even when equal to an item, with no tag and no use, the
/// window's scope, query and selection left as they were (G10, G11); the window hidden for the capture and back only
/// once the item is listed (G3); then, while Also copy is on, the item on the clipboard as a copy puts it, recording no
/// use, under the copies' one gate (G8, G10); then the capture notification, saying on the clipboard only when it is
/// (G16); a cancel silent (E3); a refused grab, a stash not open, a commit refused before it and each step after it
/// refused named above the tray, nothing announced before the file is in place, a kill's leftovers finished or undone at
/// the next start (G35, R9, G28); a second press while the overlay is up doing nothing, and once it is released refused by
/// name (Approach, G35).
/// </summary>
[TestClass]
public sealed class ScreenshotTests
{
    private static readonly PixelRect Region = new(100, 50, 30, 20);

    [TestMethod]
    public async Task TheRegionIsOnePngItemNamedAtTheReleaseThenCopiedThenAnnouncedWithTheWindowBackFirst()
    {
        using var taking = await Taking();
        var older = await taking.Stash.Import("older");
        await taking.Listing.ShowScope(Scope.Favorites);
        await taking.Listing.SetQuery("older");
        taking.Listing.Focus(older.Id);
        var painted = taking.Screen.Paint(RecordingScreen.Primary, seed: 3);
        taking.Selection.AtTheRelease = () => taking.Stash.Clock.Now += TimeSpan.FromSeconds(7);
        var atTheWrite = "";
        taking.Clipboard.AtWrite = async () =>
        {
            var placed = (await taking.Stash.All()).Single(each => each.Id != older.Id);
            atTheWrite = $"{string.Join(", ", taking.WindowSteps)}; placed {File.Exists(placed.FilePath)}; {taking.Notifications.Shown.Count} notifications";
        };

        await taking.Screenshot.Take();

        var item = (await taking.Stash.All()).Single(each => each.Id != older.Id);
        Assert.AreEqual("Screenshot 2026-10-16 17.30.07", item.Name, "12:00:07 UTC, the release, in local time (F12)");
        Assert.AreEqual("Screenshot 2026-10-16 17.30.07.png", item.FileName);
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 30, 20, false, 1, 0), item.Facts);
        CollectionAssert.AreEqual(RecordingScreen.Cut(painted, Region).Pixels, MediaFixtures.FirstFrame(item.FilePath).Pixels, "the frozen pixels of the region (G14, F3)");
        Assert.IsEmpty(item.Tags);
        Assert.AreEqual(0L, taking.Uses(), "neither the capture nor its copy is a use (G10)");
        Assert.AreEqual("hide, restore Flyout; placed True; 0 notifications", atTheWrite, "copied once the window is back and the file in place, before the notification (G14, G3, R9)");
        var content = taking.Clipboard.Written.Single().Content;
        CollectionAssert.AreEqual(new[] { item.FilePath }, content.FilePaths.ToList());
        CollectionAssert.AreEqual(File.ReadAllBytes(item.FilePath), content.Png, "its PNG bytes, as a copy's (G8)");
        CollectionAssert.AreEqual(RecordingScreen.Cut(painted, Region).Pixels, content.Bitmap!.Pixels, "its bitmap, opaque as grabbed");
        var notification = taking.Notifications.Shown.Single();
        Assert.AreEqual("Saved “Screenshot 2026-10-16 17.30.07” to your stash. It is on the clipboard too.", notification.Text, "in the stash and on the clipboard (G16)");
        Assert.AreEqual("Undo", notification.UndoLabel);
        Assert.AreEqual("older", taking.Listing.Query, "the query as it was");
        CollectionAssert.AreEqual(new[] { older.Id }, taking.Listing.Items.Select(each => each.Id).ToList(), "the query's matches, refreshed");
        CollectionAssert.AreEqual(new[] { older.Id }, taking.Listing.Selection.ToList(), "the selection kept");
        Assert.AreEqual(Scope.Favorites, taking.Listing.Scope, "the scope the user was in, not All as an import's end shows (G12)");
        Assert.AreEqual((0, 0), (taking.Clipboard.Reads, taking.Clipboard.ImageReads), "the act reads nothing (N9)");
        Assert.IsEmpty(taking.Notices.Shown, "nothing in the window");
        Assert.IsEmpty(taking.Stash.WorkFiles());
        Assert.IsEmpty(taking.Failures);
        Assert.IsFalse(taking.Capture.InProgress);
    }

    [TestMethod]
    public async Task TheWindowComesBackOnlyOnceTheItemIsCommittedAndListed()
    {
        using var taking = await Taking();
        await taking.Listing.ShowScope(Scope.All);
        var listedAtTheRestore = -1;
        var hiding = new CaptureHiding(() => CaptureHide.Pinned, _ => listedAtTheRestore = taking.Listing.Items.Count);
        var act = new Screenshot(taking.Listing, taking.Notifications, taking.CaptureNotification, taking.Handoff, taking.Capture, taking.Settings, hiding, null!, TestStash.ShortRetry);

        await act.Take();

        Assert.AreEqual(1, listedAtTheRestore, "not at the release, before the item is listed (G3)");
    }

    [TestMethod]
    public async Task TheSameRegionTwiceInOneSecondIsTwoItemsOfOneName()
    {
        using var taking = await Taking();

        await taking.Screenshot.Take();
        await taking.Screenshot.Take();

        var both = (await taking.Stash.All()).OrderBy(each => each.Id).ToList();
        CollectionAssert.AreEqual(new[] { "Screenshot 2026-10-16 17.30.00", "Screenshot 2026-10-16 17.30.00" }, both.Select(each => each.Name).ToList(), "never already in the stash (G12)");
        CollectionAssert.AreEqual(new[] { "Screenshot 2026-10-16 17.30.00.png", "Screenshot 2026-10-16 17.30.00 2.png" }, both.Select(each => each.FileName).ToList(), "P1");
    }

    [TestMethod]
    public async Task TheItemGetsNoTagFromALitChip()
    {
        using var taking = await Taking();
        var tagged = await taking.Stash.Import("tagged");
        taking.Stash.Tag(tagged.Id, "reaction");
        await taking.Listing.SetQuery("reaction");
        CollectionAssert.AreEqual(new[] { "reaction" }, taking.Listing.LitTags.ToList());

        await taking.Screenshot.Take();

        Assert.IsEmpty((await taking.Stash.All()).Single(each => each.Id != tagged.Id).Tags, "a chip the user cannot see while the window is hidden (G11)");
    }

    [TestMethod]
    public async Task WithAlsoCopyOffNothingIsWrittenAndTheNotificationSaysInTheStashOnly()
    {
        using var taking = await Taking();
        taking.Settings.Change(settings => settings with { AlsoCopyCapture = false });

        await taking.Screenshot.Take();

        Assert.AreEqual(0, taking.Clipboard.Begun, "the clipboard untouched (G14)");
        Assert.AreEqual("Saved “Screenshot 2026-10-16 17.30.00” to your stash.", taking.Notifications.Shown.Single().Text, "nothing of the clipboard (G16)");
        Assert.HasCount(1, await taking.Stash.All());
    }

    [TestMethod]
    public async Task ACopyRefusedIsNamedBesideTheCaptureNotificationWhichSaysInTheStashOnly()
    {
        using var taking = await Taking();
        taking.Clipboard.FailNext = RecordingClipboard.Held;

        await taking.Screenshot.Take();

        CollectionAssert.AreEqual(
            new[]
            {
                $"Copy failed: Screenshot 2026-10-16 17.30.00, {HeldClipboard.Reason}",
                "Saved “Screenshot 2026-10-16 17.30.00” to your stash.",
            },
            taking.Notifications.Shown.Select(notice => notice.Text).ToList(),
            "the capture stands, and the notification claims nothing of the clipboard (G16, G35)");
        Assert.HasCount(1, await taking.Stash.All());
        Assert.AreEqual(0L, taking.Uses());
    }

    [TestMethod]
    public async Task ACopyOnTheClipboardKeepsTheCapturesCopyWaitingAndOnlyItRecordsAUse()
    {
        using var taking = await Taking();
        var older = await taking.Stash.Import("older", MediaFixtures.Bytes("transparent.png"));
        var release = new TaskCompletionSource();
        taking.Clipboard.Hold = release.Task;

        var copying = taking.Handoff.Copy(null!, [older]);
        await Handing.Eventually(() => taking.Clipboard.Begun == 1);
        var capturing = taking.Screenshot.Take();
        await Handing.Eventually(() => taking.Stash.All().GetAwaiter().GetResult().Count == 2);
        await Task.Delay(100);

        Assert.AreEqual(1, taking.Clipboard.Begun, "the capture's write waits behind the copy's (G8)");
        Assert.IsEmpty(taking.Notifications.Shown, "nothing announced before its copy is made or refused");
        release.SetResult();
        await Task.WhenAll(copying, capturing);
        var screenshot = (await taking.Stash.All()).Single(each => each.Id != older.Id);
        CollectionAssert.AreEqual(new[] { older.FilePath, screenshot.FilePath }, taking.Clipboard.Written.Select(write => write.Content.FilePaths.Single()).ToList(), "both, whole, in order");
        Assert.AreEqual(1L, taking.Uses(), "the copy's use alone (G10)");
        Assert.EndsWith("It is on the clipboard too.", taking.Notifications.Shown.Single().Text);
    }

    [TestMethod]
    public async Task ACancelAddsNothingSaysNothingWritesNothingAndBringsTheWindowBack()
    {
        using var taking = await Taking();
        taking.Selection.Answer = null;

        await taking.Screenshot.Take();

        Assert.IsEmpty(await taking.Stash.All());
        Assert.IsEmpty(taking.Notifications.Shown, "a cancel says nothing (E3)");
        Assert.IsEmpty(taking.Notices.Shown);
        Assert.AreEqual(0, taking.Clipboard.Begun);
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, taking.WindowSteps);
        Assert.IsEmpty(taking.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task AGrabTheScreenRefusesIsNamedUnchangedAndTheWindowComesBack()
    {
        using var taking = await Taking();
        var refusal = new Failure("Grab the screen", "the display at 0, 0 (1280 × 800)", "access is denied");
        taking.Screen.Refuse(RecordingScreen.Primary, refusal);

        await taking.Screenshot.Take();

        Assert.AreEqual(refusal.Sentence, taking.Notifications.Shown.Single().Text, "the seam's failure, unchanged (E5, G35)");
        Assert.IsNull(taking.Notifications.Shown.Single().UndoLabel);
        Assert.IsEmpty(taking.Selection.Handed, "no overlay over a refused grab");
        Assert.IsEmpty(await taking.Stash.All());
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, taking.WindowSteps, "the hide and the restore still paired");
    }

    [TestMethod]
    public async Task NothingIsGrabbedOrHiddenWhileTheStashIsStillOpeningOrDidNotOpen()
    {
        using var taking = await Taking();
        var opening = new Listing(_ => { });
        var act = new Screenshot(opening, taking.Notifications, new CaptureNotification(taking.Notifications, opening, taking.Handoff, new NoWindowShown(), TestStash.ShortRetry),
            taking.Handoff, taking.Capture, taking.Settings, taking.Hiding, null!, TestStash.ShortRetry);

        await act.Take();
        opening.StashFailed(new Failure("Open the stash", "stash.db", "it is broken"));
        await act.Take();

        CollectionAssert.AreEqual(
            new[]
            {
                "Take a screenshot failed: the screen, your stash is still opening",
                "Take a screenshot failed: the screen, your stash could not be opened",
            },
            taking.Notifications.Shown.Select(notice => notice.Text).ToList());
        Assert.AreEqual((0, 0), (taking.Screen.DisplayReads, taking.Screen.Grabs.Count), "nothing grabbed (G28)");
        Assert.IsEmpty(taking.WindowSteps, "nothing hidden");
    }

    [TestMethod]
    public async Task ASecondPressWhileTheFirstIsSelectingDoesNothingAndTheFirstBringsTheWindowBack()
    {
        using var taking = await Taking();
        var released = new TaskCompletionSource();
        taking.Selection.Gate = released.Task;

        var first = taking.Screenshot.Take();
        await Handing.Eventually(() => taking.Selection.Handed.Count == 1);
        await taking.Screenshot.Take();

        Assert.IsEmpty(taking.Notifications.Shown, "the overlay is the busy state; nothing is said (G35)");
        CollectionAssert.AreEqual(new[] { "hide" }, taking.WindowSteps, "neither hidden again nor brought back by the second press");
        Assert.HasCount(1, taking.Screen.Grabs, "no second capture");
        released.SetResult();
        await first;
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, taking.WindowSteps);
        Assert.HasCount(1, await taking.Stash.All());
    }

    [TestMethod]
    public async Task ASecondPressOnceTheFirstIsReleasedAndStillSavingIsRefusedByName()
    {
        using var taking = await Taking();
        Task first;
        using (taking.Stash.HoldStore())
        {
            first = taking.Screenshot.Take();
            // Released and written, its commit waiting on the store: no overlay is up to say the act is busy.
            await Handing.Eventually(() => taking.Stash.WorkFiles().Length == 1);
            await taking.Screenshot.Take();

            CollectionAssert.AreEqual(new[] { "Take a screenshot failed: the screen, the last screenshot is still being saved" },
                taking.Notifications.Shown.Select(notice => notice.Text).ToList(), "the refusal named (G35)");
            CollectionAssert.AreEqual(new[] { "hide" }, taking.WindowSteps, "neither hidden again nor brought back by the second press");
            Assert.HasCount(1, taking.Screen.Grabs, "no second capture (Approach)");
        }

        await first;
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, taking.WindowSteps);
    }

    [TestMethod]
    public async Task AStoreAnotherProgramHoldsThroughTheCommitIsNamedAndLeavesNothing()
    {
        using var taking = await Taking();

        using (taking.Stash.HoldStore())
        {
            await taking.Screenshot.Take();
        }

        var notification = taking.Notifications.Shown.Single();
        Assert.AreEqual($"Add to the stash failed: {StashFolder.StoreFileName}, {HeldFileRetry.HeldReason}", notification.Text, "retried, then named unchanged (G35)");
        Assert.IsNull(notification.UndoLabel, "no capture notification (R9)");
        Assert.AreEqual(0, taking.Clipboard.Begun, "nothing to copy");
        Assert.IsEmpty(await taking.Stash.All());
        Assert.IsEmpty(taking.Stash.WorkFiles(), "the written file discarded");
        Assert.IsEmpty(taking.Stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, taking.WindowSteps);
    }

    [TestMethod]
    public async Task ARenameIntoTheRootRefusedIsNamedNotCopiedNotAnnouncedAndTheNextStartRemovesTheRow()
    {
        using var taking = await Taking();
        FileStream? heldCopy;
        Task took;
        // The store held until the written file is held too, with read sharing, so its facts are read and the row is
        // committed once the store is let go, and only its rename into the root is refused.
        using (taking.Stash.HoldStore())
        {
            took = taking.Screenshot.Take();
            heldCopy = await taking.Stash.HoldTheWorkFileOnceWritten();
        }

        using (heldCopy)
        {
            await took;

            var notification = taking.Notifications.Shown.Single();
            Assert.AreEqual($"Add to the stash failed: Screenshot 2026-10-16 17.30.00.png, {HeldFileRetry.HeldReason}", notification.Text, "the file step's failure, unchanged");
            Assert.IsNull(notification.UndoLabel, "no capture notification before the file is in place (R9)");
            Assert.AreEqual(0, taking.Clipboard.Begun, "no copy of an item not in place");
            Assert.IsEmpty(await taking.Stash.All(), "committed, but no query returns it");
            Assert.HasCount(1, taking.Stash.WorkFiles(), "nothing discarded: the pending step names it");
        }

        await taking.Reopen();

        Assert.IsEmpty(await taking.Stash.All(), "absent, never half there (G28)");
        Assert.IsEmpty(taking.Stash.WorkFiles());
        Assert.IsEmpty(taking.Stash.RootFiles());
        Assert.IsEmpty(taking.Stash.SqlQueryWhileOpen("SELECT id FROM items", r => r.GetInt64(0)), "the row removed");
    }

    [TestMethod]
    public async Task AClearingRefusedIsNamedNotCopiedNotAnnouncedAndTheNextStartClearsItsStep()
    {
        using var taking = await Taking();
        taking.Stash.SqlWhileOpen(
            "CREATE TRIGGER refuse_clearing BEFORE UPDATE OF pending_steps ON items WHEN NEW.pending_steps IS NULL BEGIN SELECT RAISE(ABORT, 'the write was refused'); END");

        await taking.Screenshot.Take();

        var notification = taking.Notifications.Shown.Single();
        Assert.StartsWith("Add to the stash failed: ", notification.Text);
        Assert.IsNull(notification.UndoLabel, "no capture notification before the last step (R9)");
        Assert.AreEqual(0, taking.Clipboard.Begun);
        var item = (await taking.Stash.All()).Single();
        Assert.IsTrue(File.Exists(item.FilePath), "in place, so the stash lists it (State: A live failure)");

        await taking.Reopen(stash => stash.Sql("DROP TRIGGER refuse_clearing"));

        Assert.AreEqual(item.Id, (await taking.Stash.All()).Single().Id);
        Assert.IsTrue(taking.Stash.SqlQueryWhileOpen("SELECT pending_steps IS NULL FROM items", r => r.GetBoolean(0)).Single(), "the step cleared at start");
    }

    /// <summary>An open stash in local time five and a half hours ahead, its screen painted and its region the one the check drags.</summary>
    private static async Task<ListedStash> Taking()
    {
        var taking = await ListedStash.Open(SetClock.Ahead);
        taking.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, Region);
        return taking;
    }
}
