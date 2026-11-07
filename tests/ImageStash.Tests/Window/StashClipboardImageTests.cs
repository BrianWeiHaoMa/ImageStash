using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Stash clipboard image over a real stash, the clipboard's image-only read preset (G13): the image one still PNG item
/// named like a screenshot in local time (F12, F3), saved even when equal to an item, with no tag and no use and the
/// window's scope, query and selection left as they were (G10, G11, G12), announced by the capture notification above
/// the tray with Undo to the Recycle Bin (G16, G29); no image, a read that failed, an unreadable picture and a stash not
/// yet open each named there, the last before anything is read (G35, N9); the commit refused before it, and each step
/// after it refused, named with no capture notification and the item left for the next start (G28, R9); and the tray
/// entry its only caller.
/// </summary>
[TestClass]
public sealed class StashClipboardImageTests
{
    private const string StillOnly = "A pasted picture is always a still; to add an animated one, drag its file in.";

    [TestMethod]
    public async Task APngIsOneItemNamedLikeAScreenshotInLocalTimeAnnouncedAboveTheTrayWithUndo()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        var png = MediaFixtures.Bytes("transparent.png");
        stashing.Clipboard.ImageReading = Image(png, null);

        await stashing.StashClipboardImage.Stash();

        var item = (await stashing.Stash.All()).Single();
        Assert.AreEqual("Screenshot 2026-10-16 17.30.00", item.Name, "12:00 UTC in local time, no extension in the name (F12)");
        Assert.AreEqual("Screenshot 2026-10-16 17.30.00.png", item.FileName);
        CollectionAssert.AreEqual(png, File.ReadAllBytes(item.FilePath), "a PNG's bytes as they are");
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 8, 6, false, 1, 0), item.Facts);
        Assert.IsEmpty(item.Tags);
        Assert.AreEqual(0, (await stashing.Stash.Store!.UseHistory(item.Id)).Value!.Count, "a capture is no use (G10)");
        var notification = stashing.Notifications.Shown.Single();
        Assert.AreEqual($"Saved “Screenshot 2026-10-16 17.30.00” to your stash. {StillOnly}", notification.Text, "in the stash, nothing of the clipboard (G13, G16)");
        Assert.AreEqual("Undo", notification.UndoLabel);
        Assert.IsEmpty(stashing.Notices.Shown, "nothing in the window, which the act does not open");
        Assert.AreEqual((1, 0), (stashing.Clipboard.ImageReads, stashing.Clipboard.Reads), "one image-only read, never the paste's (G13, N9)");
        Assert.IsEmpty(stashing.Stash.WorkFiles());
        Assert.IsEmpty(stashing.Failures);
    }

    [TestMethod]
    public async Task ABitmapIsEncodedAsAPngKeepingItsAlpha()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        // BGRA, straight alpha: opaque red, half-transparent blue, fully transparent, opaque white, translucent green, opaque black.
        byte[] pixels = [0, 0, 255, 255, 255, 0, 0, 128, 0, 0, 0, 0, 255, 255, 255, 255, 0, 200, 0, 64, 0, 0, 0, 255];
        stashing.Clipboard.ImageReading = Image(null, new MediaFrame(3, 2, 0, pixels));

        await stashing.StashClipboardImage.Stash();

        var item = (await stashing.Stash.All()).Single();
        Assert.AreEqual("Screenshot 2026-10-16 17.30.00.png", item.FileName);
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 3, 2, false, 1, 0), item.Facts);
        CollectionAssert.AreEqual(pixels, MediaFixtures.FirstFrame(item.FilePath).Pixels, "the stored PNG holds the bitmap's pixels, alpha included (F3)");
        Assert.StartsWith("Saved “Screenshot 2026-10-16 17.30.00” to your stash.", stashing.Notifications.Shown.Single().Text);
    }

    [TestMethod]
    public async Task GifBytesAProgramLabelledPngBecomeAStillPngOfTheirFirstFrame()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        stashing.Clipboard.ImageReading = Image(MediaFixtures.Bytes("animated.gif"), null);

        await stashing.StashClipboardImage.Stash();

        var item = (await stashing.Stash.All()).Single();
        Assert.AreEqual("Screenshot 2026-10-16 17.30.00.png", item.FileName, "a still PNG whatever the label (G13, F3)");
        Assert.IsFalse(item.Facts.Animated);
        CollectionAssert.AreEqual(MediaFixtures.FirstFrame(MediaFixtures.Path("animated.gif")).Pixels, MediaFixtures.FirstFrame(item.FilePath).Pixels, "the GIF's first frame");
        CollectionAssert.AreEqual(new[] { item.FileName }, stashing.Stash.RootFiles(), "no .gif in the stash");
        Assert.IsEmpty(stashing.Stash.WorkFiles(), "neither staged copy is left");
    }

    [TestMethod]
    public async Task TheSamePictureTwiceInOneSecondIsTwoItemsOfOneNameTheSecondsFileWithACounter()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        stashing.Clipboard.ImageReading = Image(MediaFixtures.Bytes("transparent.png"), null);

        await stashing.StashClipboardImage.Stash();
        await stashing.StashClipboardImage.Stash();

        var both = (await stashing.Stash.All()).OrderBy(item => item.Id).ToList();
        CollectionAssert.AreEqual(new[] { "Screenshot 2026-10-16 17.30.00", "Screenshot 2026-10-16 17.30.00" }, both.Select(item => item.Name).ToList(), "saved though its bytes equal an item's, never \"already in the stash\" (G12)");
        CollectionAssert.AreEqual(new[] { "Screenshot 2026-10-16 17.30.00.png", "Screenshot 2026-10-16 17.30.00 2.png" }, both.Select(item => item.FileName).ToList(), "P1");
        Assert.HasCount(2, stashing.Notifications.Shown);
    }

    [TestMethod]
    public async Task TheWindowsQueryTagChipScopeAndSelectionAreLeftAndTheNewItemGetsNoTag()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        var tagged = await stashing.Stash.Import("tagged");
        stashing.Stash.Tag(tagged.Id, "reaction");
        await stashing.Listing.ShowScope(Scope.Favorites);
        await stashing.Listing.SetQuery("reaction");
        stashing.Listing.Focus(tagged.Id);
        var ended = 0;
        stashing.Import.Finishing += () => ended++;
        stashing.Clipboard.ImageReading = Image(MediaFixtures.Bytes("transparent.png"), null);
        CollectionAssert.AreEqual(new[] { "reaction" }, stashing.Listing.LitTags.ToList(), "the chip is lit before the act");

        await stashing.StashClipboardImage.Stash();

        var added = (await stashing.Stash.All())[0];
        Assert.AreNotEqual(tagged.Id, added.Id, "All lists the new item first, newest added (G4)");
        Assert.IsEmpty(added.Tags, "no tag from a chip the user cannot see (G11)");
        Assert.AreEqual("reaction", stashing.Listing.Query, "the query as it was");
        CollectionAssert.AreEqual(new[] { "reaction" }, stashing.Listing.LitTags.ToList());
        CollectionAssert.AreEqual(new[] { tagged.Id }, stashing.Listing.Items.Select(item => item.Id).ToList(), "the query's matches, refreshed");
        CollectionAssert.AreEqual(new[] { tagged.Id }, stashing.Listing.Selection.ToList(), "the selection kept");
        Assert.AreEqual(0, ended, "no import ended, so a preview over the grid stays");
        Assert.IsEmpty(stashing.Notices.Shown);

        Assert.AreEqual(Scope.Favorites, stashing.Listing.Scope, "the scope the user was in, not All as an import's end shows (G12)");
    }

    [TestMethod]
    public async Task NoImageAndAReadThatFailedAreNamedAndNothingIsAdded()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        await stashing.Listing.SetQuery("kept");

        await stashing.StashClipboardImage.Stash();
        stashing.Clipboard.ImageReading = new Failure(StashClipboardImage.Action, "the clipboard", HeldClipboard.Reason);
        await stashing.StashClipboardImage.Stash();

        CollectionAssert.AreEqual(
            new[]
            {
                "Stash clipboard image failed: the clipboard, it holds no image",
                "Stash clipboard image failed: the clipboard, another program is holding the clipboard open",
            },
            stashing.Notifications.Shown.Select(notice => notice.Text).ToList());
        Assert.IsTrue(stashing.Notifications.Shown.All(notice => notice.UndoLabel is null));
        Assert.AreEqual("kept", stashing.Listing.Query);
        Assert.IsEmpty(await stashing.Stash.All());
        Assert.IsEmpty(stashing.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task BytesThatAreNoPictureAreUnreadableByTheItemsNameAndLeaveNothing()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        stashing.Clipboard.ImageReading = Image([0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16], null);

        await stashing.StashClipboardImage.Stash();

        Assert.AreEqual("Stash clipboard image failed: Screenshot 2026-10-16 17.30.00, it is unreadable", stashing.Notifications.Shown.Single().Text);
        Assert.IsEmpty(await stashing.Stash.All());
        Assert.IsEmpty(stashing.Stash.RootFiles());
        Assert.IsEmpty(stashing.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task NothingIsReadWhileTheStashIsStillOpeningOrDidNotOpen()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var notifications = new Notices(stash.Clock);
        var opening = new Listing(_ => { });
        var clipboard = new RecordingClipboard(stash.Clock) { ImageReading = Image(MediaFixtures.Bytes("transparent.png"), null) };
        var handoff = new Handoff(opening, notifications, notifications, clipboard, new RecordingTarget(), stash.Clock, TestStash.ShortRetry, TestStash.ShortRetry);
        var act = new StashClipboardImage(opening, notifications, new CaptureNotification(notifications, opening, handoff, new NoWindowShown(), TestStash.ShortRetry), clipboard, stash.Clock, TestStash.ShortRetry, TestStash.ShortRetry);

        await act.Stash();
        opening.StashFailed(new Failure("Open the stash", "stash.db", "it is broken"));
        await act.Stash();

        CollectionAssert.AreEqual(
            new[]
            {
                "Stash clipboard image failed: the clipboard, your stash is still opening",
                "Stash clipboard image failed: the clipboard, your stash could not be opened",
            },
            notifications.Shown.Select(notice => notice.Text).ToList());
        Assert.AreEqual((0, 0), (clipboard.ImageReads, clipboard.Reads), "nothing could be added, so nothing is read (N9)");
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(await stash.All());
    }

    [TestMethod]
    public async Task AnInsertTheStoreRefusesIsNamedAndLeavesNothing()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        stashing.Stash.SqlWhileOpen("CREATE TRIGGER refuse_insert BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT, 'the write was refused'); END");
        stashing.Clipboard.ImageReading = Image(MediaFixtures.Bytes("transparent.png"), null);

        await stashing.StashClipboardImage.Stash();

        var notification = stashing.Notifications.Shown.Single();
        Assert.StartsWith("Add to the stash failed: ", notification.Text, "the store's failure shown unchanged (G35)");
        Assert.IsNull(notification.UndoLabel, "a plain failure, no capture notification (R9)");
        Assert.IsEmpty(await stashing.Stash.All());
        Assert.IsEmpty(stashing.Stash.WorkFiles(), "the staged copy discarded");
        Assert.IsEmpty(stashing.Stash.RootFiles());
    }

    [TestMethod]
    public async Task AStoreAnotherProgramHoldsThroughTheCommitIsNamedAndLeavesNothing()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        stashing.Clipboard.ImageReading = Image(MediaFixtures.Bytes("transparent.png"), null);

        using (stashing.Stash.HoldStore())
        {
            await stashing.StashClipboardImage.Stash();
        }

        var notification = stashing.Notifications.Shown.Single();
        Assert.AreEqual($"Add to the stash failed: {StashFolder.StoreFileName}, {HeldFileRetry.HeldReason}", notification.Text, "retried, then named with the store and why, unchanged (G35)");
        Assert.IsNull(notification.UndoLabel);
        Assert.IsEmpty(await stashing.Stash.All());
        Assert.IsEmpty(stashing.Stash.WorkFiles(), "the staged copy discarded");
        Assert.IsEmpty(stashing.Stash.RootFiles());
    }

    [TestMethod]
    public async Task ARenameIntoTheRootRefusedIsNamedNotAnnouncedAndTheNextStartRemovesTheRowAndItsCopy()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        stashing.Clipboard.ImageReading = Image(MediaFixtures.Bytes("transparent.png"), null);
        FileStream? heldCopy;
        Task stashed;
        // The store held until the staged copy is held too, with read sharing, so its facts are read and the row is
        // committed once the store is let go, and only its rename into the root is refused.
        using (stashing.Stash.HoldStore())
        {
            stashed = stashing.StashClipboardImage.Stash();
            heldCopy = await stashing.Stash.HoldTheWorkFileOnceWritten();
        }

        using (heldCopy)
        {
            await stashed;

            var notification = stashing.Notifications.Shown.Single();
            Assert.AreEqual($"Add to the stash failed: Screenshot 2026-10-16 17.30.00.png, {HeldFileRetry.HeldReason}", notification.Text, "the file step's failure, unchanged");
            Assert.IsNull(notification.UndoLabel, "no capture notification before the file is in place (R9)");
            Assert.IsEmpty(await stashing.Stash.All(), "the row is committed but no query returns it");
            Assert.HasCount(1, stashing.Stash.WorkFiles(), "its copy is left for the next start, which the pending step names");
            Assert.IsEmpty(stashing.Stash.RootFiles());
        }

        await stashing.Reopen();

        Assert.IsEmpty(await stashing.Stash.All(), "absent, never half there (G28)");
        Assert.IsEmpty(stashing.Stash.WorkFiles());
        Assert.IsEmpty(stashing.Stash.RootFiles());
        Assert.IsEmpty(stashing.Stash.SqlQueryWhileOpen("SELECT id FROM items", r => r.GetInt64(0)), "the row removed");
    }

    [TestMethod]
    public async Task AClearingRefusedIsNamedNotAnnouncedTheItemListedAndTheNextStartClearsItsStep()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        stashing.Stash.SqlWhileOpen(
            "CREATE TRIGGER refuse_clearing BEFORE UPDATE OF pending_steps ON items WHEN NEW.pending_steps IS NULL BEGIN SELECT RAISE(ABORT, 'the write was refused'); END");
        stashing.Clipboard.ImageReading = Image(MediaFixtures.Bytes("transparent.png"), null);

        await stashing.StashClipboardImage.Stash();

        var notification = stashing.Notifications.Shown.Single();
        Assert.StartsWith("Add to the stash failed: ", notification.Text);
        Assert.IsNull(notification.UndoLabel, "no capture notification before the last step (R9)");
        var item = (await stashing.Stash.All()).Single();
        Assert.IsTrue(File.Exists(item.FilePath), "its picture is in place, so the stash lists it (State: A live failure)");

        await stashing.Reopen(stash => stash.Sql("DROP TRIGGER refuse_clearing"));

        Assert.AreEqual(item.Id, (await stashing.Stash.All()).Single().Id);
        Assert.IsTrue(stashing.Stash.SqlQueryWhileOpen("SELECT pending_steps IS NULL FROM items", r => r.GetBoolean(0)).Single(), "the step cleared at start");
    }

    [TestMethod]
    public async Task UndoRemovesTheItemItsFileToTheRecycleBinAndActsOnce()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        var older = await stashing.Stash.Import("older");
        stashing.Clipboard.ImageReading = Image(MediaFixtures.Bytes("transparent.png"), null);
        await stashing.StashClipboardImage.Stash();
        var item = (await stashing.Stash.All()).Single(each => each.Id != older.Id);
        await stashing.Listing.ShowScope(Scope.All);
        Assert.HasCount(2, stashing.Listing.Items);
        var notification = stashing.Notifications.Shown.Single();

        await stashing.Notifications.Undo(notification);
        await stashing.Notifications.Undo(notification);

        CollectionAssert.AreEqual(new[] { older.Id }, (await stashing.Stash.All()).Select(each => each.Id).ToList());
        CollectionAssert.AreEqual(new[] { older.Id }, stashing.Listing.Items.Select(each => each.Id).ToList(), "the listing refreshed");
        CollectionAssert.AreEqual(new[] { item.FilePath }, stashing.Stash.RecycleBin.Asked, "its file to the Recycle Bin, once (G29)");
        Assert.IsFalse(File.Exists(item.FilePath));
        Assert.IsEmpty(stashing.Notifications.Shown, "done, nothing to name");
    }

    [TestMethod]
    public async Task AnUndoTheRecycleBinRefusesIsNamedAndTheNextStartRecyclesTheFile()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        stashing.Clipboard.ImageReading = Image(MediaFixtures.Bytes("transparent.png"), null);
        await stashing.StashClipboardImage.Stash();
        var item = (await stashing.Stash.All()).Single();

        using (new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await stashing.Notifications.Undo(stashing.Notifications.Shown.Single());
        }

        Assert.AreEqual($"Move to the Recycle Bin failed: {item.FileName}, {HeldFileRetry.HeldReason}", stashing.Notifications.Shown.Single().Text, "the refusal named (G35)");
        Assert.IsEmpty(await stashing.Stash.All(), "the row stays marked, which no query returns");
        Assert.IsTrue(File.Exists(item.FilePath));
        var askedLive = stashing.Stash.RecycleBin.Asked.Count(path => path == item.FilePath);
        Assert.IsGreaterThan(1, askedLive, "asked again while held before the refusal is named (G35)");

        await stashing.Reopen();

        Assert.IsEmpty(await stashing.Stash.All());
        Assert.IsFalse(File.Exists(item.FilePath), "recycled at the next start");
        Assert.AreEqual(askedLive + 1, stashing.Stash.RecycleBin.Asked.Count(path => path == item.FilePath), "asked once more, at the next start");
    }

    [TestMethod]
    public async Task UndoAfterTheItemWasDeletedIsDoneAndDoesNotBringItBack()
    {
        using var stashing = await ListedStash.Open(SetClock.Ahead);
        stashing.Clipboard.ImageReading = Image(MediaFixtures.Bytes("transparent.png"), null);
        await stashing.StashClipboardImage.Stash();
        var item = (await stashing.Stash.All()).Single();
        await stashing.Deletion.Delete([item]);

        await stashing.Notifications.Undo(stashing.Notifications.Shown.Single());

        Assert.IsEmpty(stashing.Notifications.Shown, "done, never an exception or a failure");
        Assert.IsEmpty(await stashing.Stash.All(), "the item does not come back");
    }

    [TestMethod]
    public void TheTrayEntryAndItsShortcutAloneRunTheAct()
    {
        var callers = ProductCalls.Methods()
            .Where(method => ProductCalls.CalledMethods(method).Any(called => called.DeclaringType == typeof(StashClipboardImage) && called.Name == nameof(StashClipboardImage.Stash)))
            .Select(method => $"{ProductCalls.Outermost(method.DeclaringType!).FullName}.{method.Name}")
            .ToList();

        CollectionAssert.AreEquivalent(new[] { "ImageStash.App.StashFromTray", "ImageStash.App.StashFromShortcut" }, callers, "the user's act, never at start or from a listener (N9)");
        // And those are only handed to the tray as its entry's action and to the shortcut as its act, never called
        // themselves, at start or anywhere else.
        var startedDirectly = ProductCalls.Methods()
            .Where(method => ProductCalls.CalledMethods(method).Any(called => called.DeclaringType == typeof(App) && called.Name is "StashFromTray" or "StashFromShortcut"))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .ToList();
        Assert.IsEmpty(startedDirectly, string.Join(", ", startedDirectly));
    }

    private static Outcome<ClipboardImage?> Image(byte[]? png, MediaFrame? frame) => Outcome<ClipboardImage?>.Of(new ClipboardImage(png, frame));
}
