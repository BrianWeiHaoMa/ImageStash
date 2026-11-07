using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Rename through the window's one rename path over a real stash (G17, P1): the store's Rename, the item and its file
/// carrying the name, " 2" while it is taken, an empty name committed; the listing refreshed only after the store has
/// answered (R9), nothing said on success and no use recorded (G10); the busy state naming the rename while the store
/// works (G35); a refusal and a file still held after the commit named, the listing showing the store's answer (G35,
/// State: A live failure); nothing asked of the store before it has opened.
/// </summary>
[TestClass]
public sealed class RenamingTests
{
    [TestMethod]
    public async Task ARenameNamesTheItemAndItsFileThenRefreshesTheListingAndSaysNothing()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Listing.ShowScope(Scope.All);
        var busy = new List<string?>();
        listed.Renaming.Changed += () => busy.Add(listed.Renaming.Busy);

        var renamed = await listed.Renaming.Rename(cat, "dog");

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.Sentence);
        var shown = listed.Listing.Items.Single();
        Assert.AreEqual(("dog", "dog.png", listed.Stash.Folder.InRoot("dog.png")), (shown.Name, shown.FileName, shown.FilePath), "the listing shows the store's answer (G17, P1)");
        CollectionAssert.AreEqual(new[] { "dog.png" }, listed.Stash.RootFiles(), "the file carries the name");
        CollectionAssert.AreEqual(new[] { "Renaming “cat”…", null }, busy, "the busy state names the rename while the store works, then goes (G35)");
        Assert.IsEmpty(listed.Notices.Shown, "a rename that succeeded says nothing more");
        Assert.AreEqual(0L, listed.Uses(), "renaming is not a use (G10)");
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task ATakenNameGetsTwoAndAnEmptyNameIsCommittedWithItsFileUntitled()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("dog", TestStash.Bytes(1));
        var cat = await listed.Stash.Import("cat", TestStash.Bytes(2));
        var emu = await listed.Stash.Import("emu", TestStash.Bytes(3));
        await listed.Listing.ShowScope(Scope.All);

        var taken = await listed.Renaming.Rename(cat, "dog");
        var empty = await listed.Renaming.Rename(emu, "");

        Assert.IsTrue(taken.Succeeded && empty.Succeeded);
        var byId = listed.Listing.Items.ToDictionary(item => item.Id);
        Assert.AreEqual(("dog", "dog 2.png"), (byId[cat.Id].Name, byId[cat.Id].FileName), "a name taken by another item's file gets \" 2\" (State: File names)");
        Assert.AreEqual(("", "Untitled.png"), (byId[emu.Id].Name, byId[emu.Id].FileName), "nothing requires a name (G17)");
        CollectionAssert.AreEquivalent(new[] { "dog.png", "dog 2.png", "Untitled.png" }, listed.Stash.RootFiles());
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task ARenamedItemThatLeavesTheQueryIsNoLongerListedThereAndTheSelectionMoves()
    {
        using var listed = await ListedStash.Open();
        var other = await listed.Stash.Import("cat 2", TestStash.Bytes(1));
        var cat = await listed.Stash.Import("cat 1", TestStash.Bytes(2));
        await listed.Listing.SetQuery("cat");
        listed.Listing.Focus(cat.Id);

        var renamed = await listed.Renaming.Rename(cat, "dog");

        Assert.IsTrue(renamed.Succeeded);
        CollectionAssert.AreEqual(new[] { other.Id }, listed.Listing.Items.Select(item => item.Id).ToList(), "the query's matches as the store holds them now (G5)");
        CollectionAssert.AreEqual(new[] { other.Id }, listed.Listing.Selection.ToList(), "the selection moved to the first match");
        Assert.AreEqual("dog", (await listed.Stash.Store!.Item(cat.Id)).Value!.Name, "still in the stash, found by identity");
    }

    [TestMethod]
    public async Task AFileHeldPastTheRetryIsNamedAndTheListingShowsTheNewNameWithTheFileWhereItIs()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Listing.ShowScope(Scope.All);

        Outcome renamed;
        using (TestStash.Hold(cat.FilePath))
        {
            renamed = await listed.Renaming.Rename(cat, "dog");
        }

        var named = $"{ItemRename.Action} failed: cat.png, {HeldFileRetry.HeldReason}";
        Assert.AreEqual(named, renamed.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { named }, listed.Notices.Shown.Select(notice => notice.Text).ToList(), "named once, with the file and the reason (G35)");
        var shown = listed.Listing.Items.Single();
        Assert.AreEqual(("dog", cat.FilePath), (shown.Name, shown.FilePath), "the name as committed, the picture on disk where it is (State: A live failure)");
        Assert.IsNull(listed.Renaming.Busy);

        await listed.Reopen();

        Assert.AreEqual(("dog", listed.Stash.Folder.InRoot("dog.png")), (listed.Listing.Items.Single().Name, listed.Listing.Items.Single().FilePath), "the next start finishes it");
        CollectionAssert.AreEqual(new[] { "dog.png" }, listed.Stash.RootFiles());
    }

    [TestMethod]
    public async Task AnItemTheStoreSavedRenamesBothItsFilesEachKeepingItsExtensionAndAHeldOriginalStaysWhereItIs()
    {
        using var listed = await ListedStash.Open();
        var saved = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.webp"), EditedItems.BlackBoxAndBox)).Value!;
        await listed.Listing.ShowScope(Scope.All);

        var dog = await listed.Renaming.Rename(listed.Listing.Items.Single(), "dog");

        Assert.IsTrue(dog.Succeeded, dog.Failure?.Sentence);
        var shown = listed.Listing.Items.Single();
        Assert.AreEqual(("dog", "dog.png", listed.Stash.Folder.InRoot("dog.png"), saved.Facts), (shown.Name, shown.FileName, shown.FilePath, shown.Facts), "the render, under the new name (P1)");
        CollectionAssert.AreEqual(new[] { "dog.png" }, listed.Stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "dog.webp" }, EditedItems.OriginalFiles(listed.Stash), "the original keeps its own extension (State: File names)");

        Outcome back;
        using (TestStash.Hold(listed.Stash.Folder.InOriginals("dog.webp")))
        {
            back = await listed.Renaming.Rename(shown, "cat");
        }

        Assert.AreEqual($"{ItemRename.Action} failed: dog.webp, {HeldFileRetry.HeldReason}", back.Failure?.Sentence);
        shown = listed.Listing.Items.Single();
        Assert.AreEqual(("cat", listed.Stash.Folder.InRoot("cat.png")), (shown.Name, shown.FilePath), "listed with its files where they are (State: A live failure)");
        Assert.AreEqual(listed.Stash.Folder.InOriginals("dog.webp"), (await EditedItems.ForEdit(listed.Stash.Store!, shown.Id)).OriginalPath);

        await listed.Reopen();

        CollectionAssert.AreEqual(new[] { "cat.png" }, listed.Stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "cat.webp" }, EditedItems.OriginalFiles(listed.Stash), "the next start finishes it");
    }

    [TestMethod]
    public async Task AnItemNoLongerInTheStashIsRefusedByNameAndTheOthersStayAsTheyWere()
    {
        using var listed = await ListedStash.Open();
        var kept = await listed.Stash.Import("kept", TestStash.Bytes(1));
        var gone = await listed.Stash.Import("gone", TestStash.Bytes(2));
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsTrue((await listed.Stash.Store!.Delete([gone.Id])).Single().Succeeded);

        var refused = await listed.Renaming.Rename(gone, "dog");

        Assert.AreEqual($"{ItemRename.Action} failed: gone, {ItemFactChange.NotInStash}", refused.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { refused.Failure!.Sentence }, listed.Notices.Shown.Select(notice => notice.Text).ToList());
        CollectionAssert.AreEqual(new[] { "kept" }, listed.Listing.Items.Select(item => item.Name).ToList());
        CollectionAssert.AreEqual(new[] { "kept.png" }, listed.Stash.RootFiles());
    }

    [TestMethod]
    public async Task WhileTheStashOpensOrAfterItFailedTheItemIsRefusedByNameAndNothingIsAskedOfTheStore()
    {
        var notices = new Notices(TimeProvider.System);
        var listing = new Listing(_ => { });
        var renaming = new Renaming(listing, notices, new Handoff(listing, notices, notices, new RecordingClipboard(TimeProvider.System), new RecordingTarget(), TimeProvider.System, TestStash.ShortRetry, TestStash.ShortRetry));

        var opening = await renaming.Rename(TestItems.Sized(1, 10, 10), "dog");
        listing.StashFailed(new Failure("Open the stash", "stash", "the disk is gone"));
        var failed = await renaming.Rename(TestItems.Sized(2, 10, 10), "dog");

        Assert.AreEqual("Rename failed: “item 1”, your stash is still opening", opening.Failure?.Sentence);
        Assert.AreEqual("Rename failed: “item 2”, your stash could not be opened", failed.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { opening.Failure!.Sentence, failed.Failure!.Sentence }, notices.Shown.Select(notice => notice.Text).ToList());
        Assert.IsNull(renaming.Busy);
    }

    [TestMethod]
    public async Task NamingACopiedRecordingInItsNotificationPutsTheRenamedFileOnTheClipboardAndRecordsNoUse()
    {
        using var listed = await ListedStash.Open();
        var clip = await listed.Stash.ImportFixture("Recording 2026-11-05 12.00.00", "animated.gif");
        await listed.Listing.Refresh();
        Assert.IsTrue((await listed.Handoff.CopyCapture(null!, clip)).Succeeded);
        listed.CaptureNotification.Raise(clip, CaptureKind.Recording, true, null);

        // The card's field takes this path on Enter.
        var renamed = await listed.CaptureNotification.Renaming.Rename(clip, "my reaction");

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.Sentence);
        var again = listed.Clipboard.WrittenAgain.Single();
        Assert.AreEqual((clip.Id, 1), (again.SavedItem, again.WrittenBefore), "asked once, through the seam's guarded write-again, after the copy (G26)");
        var path = again.Content.FilePaths.Single();
        Assert.AreEqual(listed.Stash.Folder.InRoot("my reaction.gif"), path, "the renamed file (G17, P1)");
        Assert.IsTrue(File.Exists(path), "the clipboard's only format for an animation names a file that exists (G8)");
        Assert.AreEqual(0L, listed.Uses(), "neither the capture's copy nor the refresh is a use (G10)");
        Assert.IsEmpty(listed.Notices.Shown);
        Assert.IsEmpty(listed.Notifications.Shown.Where(notice => notice.UndoLabel is null), "nothing named above the tray");
    }

    [TestMethod]
    public async Task ARenameInTheWindowRefreshesTheClipboardOnceAndOnlyWhenImageStashsLatestWriteHoldsTheItem()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat", TestStash.Bytes(1));
        var dog = await listed.Stash.Import("dog", TestStash.Bytes(2));
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsTrue((await listed.Handoff.Copy(null!, [cat, dog])).Succeeded);
        var usesAfterCopy = listed.Uses();

        var renamed = await listed.Renaming.Rename(dog, "puppy");

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.Sentence);
        var again = listed.Clipboard.WrittenAgain.Single();
        CollectionAssert.AreEqual(new[] { cat.Id, dog.Id }, again.Content.Items.ToList(), "the latest write's items, the multi-selection kept (G26)");
        CollectionAssert.AreEqual(new[] { listed.Stash.Folder.InRoot("cat.png"), listed.Stash.Folder.InRoot("puppy.png") }, again.Content.FilePaths.ToList());
        Assert.IsNull(again.Content.Bitmap, "several items go as files only, as the handoff wrote them (G8)");
        Assert.AreEqual(usesAfterCopy, listed.Uses(), "the refresh records no use (G10)");

        var emu = await listed.Stash.Import("emu", TestStash.Bytes(3));
        await listed.Listing.Refresh();
        Assert.IsTrue((await listed.Renaming.Rename(emu, "ostrich")).Succeeded);

        Assert.HasCount(1, listed.Clipboard.WrittenAgain, "an item ImageStash's latest write does not hold leaves the clipboard alone");
    }

    [TestMethod]
    public async Task AClipboardAnotherProgramChangedSinceTheCopyIsLeftAloneAndNothingIsNamed()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsTrue((await listed.Handoff.Copy(null!, [cat])).Succeeded);
        // As the seam answers when the clipboard's sequence number has moved since its write.
        listed.Clipboard.WritesAgain = Outcome<bool>.Of(false);

        var renamed = await listed.Renaming.Rename(cat, "dog");

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.Sentence);
        Assert.HasCount(1, listed.Clipboard.WrittenAgain, "asked once, and the seam declined");
        Assert.HasCount(1, listed.Clipboard.Written, "never written outright");
        Assert.IsEmpty(listed.Notices.Shown, "a changed clipboard is no failure (G26)");
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task ARenameWhoseFileIsHeldPastTheRetryLeavesTheClipboardAsItWas()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsTrue((await listed.Handoff.Copy(null!, [cat])).Succeeded);

        Outcome renamed;
        using (TestStash.Hold(cat.FilePath))
        {
            renamed = await listed.Renaming.Rename(cat, "dog");
        }

        Assert.AreEqual($"{ItemRename.Action} failed: cat.png, {HeldFileRetry.HeldReason}", renamed.Failure?.Sentence);
        Assert.AreEqual(("dog", cat.FilePath), (listed.Listing.Items.Single().Name, listed.Listing.Items.Single().FilePath), "the name committed, the file where it was");
        Assert.IsEmpty(listed.Clipboard.WrittenAgain, "no refresh from a state the next start still has to finish (State: A live failure)");
        Assert.IsTrue(File.Exists(listed.Clipboard.Written.Single().Content.FilePaths.Single()), "the clipboard's file is still where it was written");
    }

    [TestMethod]
    public async Task TheWindowsRenameAndTheCardsRenameEachRefreshTheClipboardOnce()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsTrue((await listed.Handoff.Copy(null!, [cat])).Succeeded);

        // F2 and the preview's name field share the window's rename path; the card's field has its own.
        Assert.IsTrue((await listed.Renaming.Rename(cat, "dog")).Succeeded);
        var dog = listed.Listing.Items.Single();
        Assert.IsTrue((await listed.CaptureNotification.Renaming.Rename(dog, "emu")).Succeeded);

        CollectionAssert.AreEqual(new[] { listed.Stash.Folder.InRoot("dog.png"), listed.Stash.Folder.InRoot("emu.png") },
            listed.Clipboard.WrittenAgain.Select(again => again.Content.FilePaths.Single()).ToList(), "one refresh per rename, each at the file's new path (G17)");
        Assert.IsNotNull(listed.Clipboard.WrittenAgain[^1].Content.Png, "a single still keeps its PNG and bitmap (G8)");
    }

    [TestMethod]
    public async Task ARefreshTheClipboardRefusesIsNamedInTheWindowAndTheRenameStands()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsTrue((await listed.Handoff.Copy(null!, [cat])).Succeeded);
        listed.Clipboard.WritesAgain = Outcome<bool>.Fail(RecordingClipboard.Held);

        var renamed = await listed.CaptureNotification.Renaming.Rename(cat, "dog");

        Assert.IsTrue(renamed.Succeeded, "the rename itself succeeded");
        Assert.AreEqual("dog", listed.Listing.Items.Single().Name);
        CollectionAssert.AreEqual(new[] { $"{Handoff.RefreshAction} failed: dog, {RecordingClipboard.Held.Reason}" },
            listed.Notices.Shown.Select(notice => notice.Text).ToList(), "named in the window's notices, which last whether or not it shows (G35, G16)");
    }

}
