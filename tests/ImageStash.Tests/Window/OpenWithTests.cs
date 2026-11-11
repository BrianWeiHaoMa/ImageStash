using System.Reflection;
using System.Runtime.CompilerServices;
using ImageStash.Editor;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Open with… in the window over a real stash, its dialog the check's own (G27, How the guarantees hold G27, State: Open
/// with…): the dialog on Store's copy outside the stash folder, the flag held while it is up (G2), the copy watched from
/// before it opens; a rewrite in place or by delete and create adopted, the notification above the tray raised once the
/// adoption has answered (R9, G16), the item listed at its new revision so its tile and preview read it anew; a copy
/// held without sharing asked again once a second; an undecodable rewrite named (G35); a rewrite waiting while an edit
/// of its item is open or still opening (G20); Undo, expiry by time and by ×, a second adoption's one Undo, two items'
/// two notifications (G27, G29); Open with… asked again; every refusal named; no use recorded (G10). Across a restart,
/// every recorded session watched again and each adoption the start made said (State, R9); a session watched until its
/// item is removed for good and across a Delete's Undo, and a copy whose folder went asked once a second until it is
/// back (How the guarantees hold G27, G35).
/// </summary>
[TestClass]
public sealed class OpenWithTests
{
    private static readonly byte[] Still = MediaFixtures.Bytes("still.png");

    [TestMethod]
    public async Task FromTheItemMenuTheDialogOpensOnStoresCopyWatchedBeforeItAndARewriteInPlaceIsAdoptedThenSaid()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        var dialog = new TaskCompletionSource<Outcome>();
        listed.Dialogs.OpenWithAnswer = () => dialog.Task;
        var up = new List<bool>();
        // Read at the notifications' first change, on the thread that made it: this check runs Open with…'s calls on the
        // watcher's thread, not on one UI thread, so the notices are read only once this has run, never mid-change.
        var listedWhenSaid = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
        listed.Notifications.Changed += () => listedWhenSaid.TrySetResult(listed.Listing.Items.SingleOrDefault(item => item.Id == cat.Id)?.RenderedRevision);

        var opening = listed.ItemMenu.Act(ItemMenuAction.OpenWith, null!, [cat], null, () => Assert.Fail("Open with… hides nothing"), up.Add);
        await Handing.Eventually(() => listed.Dialogs.OpenedWith.Count == 1);

        var copy = listed.Dialogs.OpenedWith.Single();
        Assert.AreEqual(Path.Combine(listed.Stash.OpenWithRoot!, OpenedWith.FolderOf(copy), "cat.png"), copy, "the copy Store keeps outside the stash folder (G27)");
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(copy), "of the finished picture");
        CollectionAssert.AreEqual(new[] { true }, up, "the dialog's flag held while it is up (G2)");
        CollectionAssert.AreEqual(new[] { cat.Id }, listed.OpenWith.Watching.ToList());

        var rewrite = MediaFixtures.DistinctPng(7, 10, 8);
        Assert.IsNull(listed.OpenWithClock.NextDue, "nothing waits on the asking's clock before the rewrite");
        await OpenedWith.RewriteWhenFree(copy, rewrite);
        await OpenedWith.Eventually(listed, () => listedWhenSaid.Task.IsCompleted);
        var revisionWhenSaid = await listedWhenSaid.Task;

        // The dialog is still up and nothing waited on the asking's clock before the rewrite: only the watcher can have
        // asked first.
        Assert.IsFalse(opening.IsCompleted);
        var adopted = listed.Listing.Items.Single(item => item.Id == cat.Id);
        Assert.AreEqual(Replaced("cat"), Said(listed).Single());
        Assert.AreEqual("Undo", listed.Notifications.Shown.Single().UndoLabel);
        var stored = (await listed.Stash.All()).Single(item => item.Id == cat.Id);
        Assert.AreEqual(stored.RenderedRevision, revisionWhenSaid, "said once the adoption had answered and the listing was read again (R9)");
        Assert.IsEmpty(listed.Notices.Shown, "nothing in the window's own notices");
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(adopted.FilePath), "the rewrite is the item's picture (G27)");
        Assert.AreEqual(EditedItems.Hash(rewrite), OpenedWith.OriginalHash(listed.Stash, cat.Id), "and its original");
        Assert.AreNotEqual(ThumbnailKey.Of(cat, ThumbnailSize.Medium), ThumbnailKey.Of(adopted, ThumbnailSize.Medium), "so the tile and the preview read it anew at their next showing");

        dialog.SetResult(Outcome.Done);
        Assert.IsTrue((await opening).Succeeded);
        CollectionAssert.AreEqual(new[] { true, false }, up, "and let go once it has closed");
        Assert.AreEqual(0L, listed.Uses(), "neither Open with… nor an adoption is a use (G10)");
    }

    [TestMethod]
    public async Task ACopyDeletedAndThenMadeAgainIsAdoptedItsAbsenceAnsweredAsStillHeld()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        var dialog = new TaskCompletionSource<Outcome>();
        listed.Dialogs.OpenWithAnswer = () => dialog.Task;
        var opening = Chosen(listed, cat);
        await Handing.Eventually(() => listed.Dialogs.OpenedWith.Count == 1);
        var copy = listed.Dialogs.OpenedWith.Single();

        File.Delete(copy);
        dialog.SetResult(Outcome.Done);
        await opening;
        await Handing.Eventually(() => listed.OpenWithClock.NextDue == OpenWith.AskedEvery);
        Assert.IsEmpty(Said(listed), "the absence is a replacement half way, not a failure");

        await Task.Delay(100);
        var rewrite = MediaFixtures.DistinctPng(8, 6, 6);
        File.WriteAllBytes(copy, rewrite);
        await Handing.Eventually(() => Said(listed).Count == 1);

        Assert.AreEqual(Replaced("cat"), Said(listed).Single());
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(listed.Listing.Items.Single().FilePath));
    }

    [TestMethod]
    public async Task ACopyHeldWithoutSharingIsAskedAgainOnceASecondAndAdoptedOnceLetGo()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        var dialog = new TaskCompletionSource<Outcome>();
        listed.Dialogs.OpenWithAnswer = () => dialog.Task;
        var opening = Chosen(listed, cat);
        await Handing.Eventually(() => listed.Dialogs.OpenedWith.Count == 1);
        var copy = listed.Dialogs.OpenedWith.Single();
        var clock = listed.OpenWithClock;

        // Held as a program that keeps it open after writing holds it: any open without sharing is refused meanwhile.
        using (new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            await OpenedWith.RewriteWhenFree(copy, MediaFixtures.DistinctPng(9, 5, 7));
            await Task.Delay(500);
            dialog.SetResult(Outcome.Done);
            await opening;
            await Handing.Eventually(() => clock.NextDue == OpenWith.AskedEvery);
            Assert.IsEmpty(Said(listed), "still held: nothing adopted");

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.AreEqual(TimeSpan.FromSeconds(2), clock.NextDue, "still held at the second: asked again a second later");
            Assert.IsEmpty(Said(listed));
        }

        // Letting go writes nothing a watcher sees, so only the once-a-second asking can find the rewrite now.
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await Task.Delay(500);
        Assert.IsEmpty(Said(listed), "nothing asked before the second was up");
        clock.Advance(TimeSpan.FromMilliseconds(500));

        Assert.AreEqual(Replaced("cat"), Said(listed).Single(), "adopted at the next second (State: at least once a second)");
        Assert.IsNull(clock.NextDue, "and asked no more");
    }

    [TestMethod]
    public async Task TwoItemsAdoptedInTurnShowTwoNotificationsEachUndoingItsOwnItem()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        var dog = await Imported(listed, "dog", MediaFixtures.Bytes("transparent.png"));
        await Chosen(listed, cat);
        await Chosen(listed, dog);
        var (catCopy, dogCopy) = (listed.Dialogs.OpenedWith[0], listed.Dialogs.OpenedWith[1]);
        var (catRewrite, dogRewrite) = (MediaFixtures.DistinctPng(10, 4, 4), MediaFixtures.DistinctPng(11, 4, 4));

        await OpenedWith.RewriteWhenFree(catCopy, catRewrite);
        await OpenedWith.Eventually(listed, () => Said(listed).Count == 1);
        await OpenedWith.RewriteWhenFree(dogCopy, dogRewrite);
        await OpenedWith.Eventually(listed, () => Said(listed).Count == 2);

        CollectionAssert.AreEqual(new[] { Replaced("cat"), Replaced("dog") }, Said(listed), "one per item, both up (G27)");
        await listed.Notifications.Undo(listed.Notifications.Shown.Single(notice => notice.Text == Replaced("cat")));

        Assert.AreEqual(EditedItems.Hash(Still), OpenedWith.OriginalHash(listed.Stash, cat.Id), "cat's previous original back");
        Assert.AreEqual(EditedItems.Hash(dogRewrite), OpenedWith.OriginalHash(listed.Stash, dog.Id), "dog's adoption untouched");
        CollectionAssert.AreEqual(new[] { Replaced("dog") }, Said(listed), "dog's notification still up");
        Assert.IsNotNull(OpenedWith.Undo(listed.Stash, dog.Id), "with its Undo standing");
    }

    [TestMethod]
    public async Task AnUndecodableRewriteIsNamedOnceAndAdoptsNothingAndADecodableOneAfterItIsAdopted()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        // Edited, so a rewrite that dropped the layers without being adopted would show.
        var saved = await listed.Stash.SaveEdit(cat, EditedItems.BlackBoxAndBox);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        await listed.Listing.Refresh();
        await Chosen(listed, saved.Value!);
        var copy = listed.Dialogs.OpenedWith.Single();
        var row = OpenedWith.WholeRowBut(listed.Stash, cat.Id, "open_with_session");
        var files = ItemFiles(listed.Stash);

        await OpenedWith.RewriteWhenFree(copy, OpenedWith.Garbage());
        await OpenedWith.Eventually(listed, () => Said(listed).Count == 1);
        await Task.Delay(300);

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat.png", MediaDecoder.UnreadableReason).Sentence, Said(listed).Single(), "named with the copy's name, once (G35)");
        Assert.AreEqual(row, OpenedWith.WholeRowBut(listed.Stash, cat.Id, "open_with_session"), "the item keeps its previous original and layers (G27)");
        Assert.AreEqual(EditedItems.BlackBoxAndBox, (await EditedItems.ForEdit(listed.Stash.Store!, cat.Id)).Layers);
        CollectionAssert.AreEquivalent(files, ItemFiles(listed.Stash), "and its files");
        CollectionAssert.AreEqual(new[] { cat.Id }, listed.OpenWith.Watching.ToList(), "the session goes on");

        var rewrite = MediaFixtures.DistinctPng(12, 6, 4);
        await OpenedWith.RewriteWhenFree(copy, rewrite);
        await OpenedWith.Eventually(listed, () => Said(listed).Count == 2);

        Assert.AreEqual(Replaced("cat"), Said(listed)[1]);
        Assert.AreEqual(EditedItems.Hash(rewrite), OpenedWith.OriginalHash(listed.Stash, cat.Id));
    }

    [TestMethod]
    public async Task AFailureIsNamedOnceWhileItRecursAndTheCopyIsAskedAboutAgainAtTheNextChangeOfTheListing()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        await Chosen(listed, cat);
        var refusal = new Failure("Read the picture", "cat.png", "the disk refused");
        listed.Stash.Reader = (_, _, _) => refusal;

        await OpenedWith.RewriteWhenFree(listed.Dialogs.OpenedWith.Single(), MediaFixtures.DistinctPng(24, 6, 6));
        await OpenedWith.Eventually(listed, () => Said(listed).Count == 1);
        var reads = listed.Stash.ReadCalls.Count;
        await listed.Listing.Refresh();
        await Handing.Eventually(() => listed.Stash.ReadCalls.Count > reads);
        await Task.Delay(300);

        CollectionAssert.AreEqual(new[] { refusal.Sentence }, Said(listed), "asked again and refused again, but named once (G35)");
        listed.Stash.Reader = MediaDecoder.ReadPicture;
        await listed.Listing.Refresh();
        await Handing.Eventually(() => Said(listed).Count == 2);
        Assert.AreEqual(Replaced("cat"), Said(listed)[1], "the next change of the listing asked again, and the rewrite was adopted");
    }

    [TestMethod]
    [DataRow(true, DisplayName = "the edit saved")]
    [DataRow(false, DisplayName = "the edit cancelled")]
    public async Task ARewriteWhileTheItemsEditIsOpenIsAdoptedOnceTheEditEndsReplacingWhatTheEditSaved(bool save)
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        await Chosen(listed, cat);
        var copy = listed.Dialogs.OpenedWith.Single();
        Assert.IsTrue((await listed.Editing.Open(cat, EditOrigin.Grid)).Succeeded);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));

        var rewrite = MediaFixtures.DistinctPng(13, 7, 5);
        await OpenedWith.RewriteWhenFree(copy, rewrite);
        await OpenedWith.Eventually(listed, () => OpenedWith.Record(listed.Stash, cat.Id)?.Awaiting == true);
        await Task.Delay(300);
        Assert.IsEmpty(Said(listed), "not adopted while the item's edit is open (State)");

        if (save)
        {
            Assert.IsTrue((await listed.Editing.Save()).Succeeded);
        }
        else
        {
            Assert.IsTrue(listed.Editing.Cancel());
        }

        await Handing.Eventually(() => Said(listed).Contains(Replaced("cat")));
        var edit = await EditedItems.ForEdit(listed.Stash.Store!, cat.Id);
        Assert.IsTrue(edit.Layers.IsEmpty, "its layers dropped (G27)");
        Assert.AreEqual(EditedItems.Hash(rewrite), Convert.ToHexString(edit.OriginalSha256));
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(edit.Item.FilePath), save ? "the render the edit saved is gone (G29)" : "the rewrite is the picture");
    }

    [TestMethod]
    public async Task ARewriteWhileAnotherItemsEditIsOpenIsAdoptedAtOnce()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        var dog = await Imported(listed, "dog", MediaFixtures.Bytes("transparent.png"));
        await Chosen(listed, cat);
        Assert.IsTrue((await listed.Editing.Open(dog, EditOrigin.Grid)).Succeeded);

        await OpenedWith.RewriteWhenFree(listed.Dialogs.OpenedWith.Single(), MediaFixtures.DistinctPng(14, 4, 4));
        await OpenedWith.Eventually(listed, () => Said(listed).Count == 1);

        Assert.AreEqual(Replaced("cat"), Said(listed).Single());
        Assert.AreEqual(dog.Id, listed.Editing.Session?.Item.Id, "dog's edit open throughout");
    }

    [TestMethod]
    public async Task ARewriteWhileEditsOpenStillReadsTheOriginalWaitsForThatEditToEnd()
    {
        using var listed = await ListedStash.Open(editRetry: TimeSpan.FromSeconds(10));
        var cat = await Imported(listed, "cat");
        await Chosen(listed, cat);
        var copy = listed.Dialogs.OpenedWith.Single();
        var rewrite = MediaFixtures.DistinctPng(15, 8, 8);

        Task<Outcome> opening;
        // Held as a scanner holds it (E3): Edit's open waits in its read of the original, which Store cannot see.
        using (new FileStream(cat.FilePath, FileMode.Open, FileAccess.Read, FileShare.Delete))
        {
            opening = listed.Editing.Open(cat, EditOrigin.Grid);
            Assert.IsTrue(listed.Editing.Occupies(cat.Id) && !listed.Editing.IsOpen, "the open is reading the original");
            await OpenedWith.RewriteWhenFree(copy, rewrite);
            await OpenedWith.Eventually(listed, () => OpenedWith.Record(listed.Stash, cat.Id)?.Awaiting == true);
            await Task.Delay(300);
            Assert.IsFalse(opening.IsCompleted, "still reading");
            Assert.IsEmpty(Said(listed), "not adopted while Edit's open reads the original (G20)");
        }

        Assert.IsTrue((await opening).Succeeded, "it opens on the original it read");
        Assert.AreEqual(EditedItems.Hash(Still), EditedItems.Hash(listed.Editing.Session!.Edit.OriginalPath));
        await Task.Delay(300);
        Assert.IsEmpty(Said(listed), "nor while that edit is open");
        Assert.IsTrue(listed.Editing.Cancel());

        await Handing.Eventually(() => Said(listed).Contains(Replaced("cat")));
        Assert.AreEqual(EditedItems.Hash(rewrite), OpenedWith.OriginalHash(listed.Stash, cat.Id));
    }

    [TestMethod]
    public async Task UndoOnTheNotificationRestoresThePreviousOriginalAndLayersAndRecyclesTheAdoptedPicture()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        var saved = await listed.Stash.SaveEdit(cat, EditedItems.BlackBoxAndBox);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        await listed.Listing.Refresh();
        var finished = File.ReadAllBytes(saved.Value!.FilePath);
        await Chosen(listed, saved.Value);
        var rewrite = MediaFixtures.DistinctPng(16, 9, 9);
        await OpenedWith.RewriteWhenFree(listed.Dialogs.OpenedWith.Single(), rewrite);
        await OpenedWith.Eventually(listed, () => Said(listed).Count == 1);

        await listed.Notifications.Undo(listed.Notifications.Shown.Single());

        var restored = listed.Listing.Items.Single();
        var edit = await EditedItems.ForEdit(listed.Stash.Store!, cat.Id);
        Assert.AreEqual(EditedItems.BlackBoxAndBox, edit.Layers, "its layers back (G27)");
        Assert.AreEqual(EditedItems.Hash(Still), Convert.ToHexString(edit.OriginalSha256), "and its original");
        CollectionAssert.AreEqual(finished, File.ReadAllBytes(restored.FilePath), "its finished picture rendered again");
        CollectionAssert.Contains(OpenedWith.Recycled(listed.Stash), EditedItems.Hash(rewrite), "the adopted picture in the Recycle Bin (G29)");
        Assert.IsNull(OpenedWith.Undo(listed.Stash, cat.Id), "no Undo stands");
        Assert.IsEmpty(listed.Notifications.Shown);
        Assert.AreEqual(0L, listed.Uses(), "an Undo is no use (G10)");
    }

    [TestMethod]
    [DataRow(true, DisplayName = "by its time")]
    [DataRow(false, DisplayName = "by its ×")]
    public async Task TheNotificationGoneWithoutUndoRecyclesThePreviousOriginalAndTheItemStaysAdopted(bool byTime)
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        await Chosen(listed, cat);
        var rewrite = MediaFixtures.DistinctPng(17, 3, 5);
        await OpenedWith.RewriteWhenFree(listed.Dialogs.OpenedWith.Single(), rewrite);
        await OpenedWith.Eventually(listed, () => Said(listed).Count == 1);
        var notice = listed.Notifications.Shown.Single();

        if (byTime)
        {
            listed.Stash.Clock.Now += Notices.Lasting;
            listed.Notifications.Sweep();
        }
        else
        {
            listed.Notifications.Close(notice);
        }

        await Handing.Eventually(() => OpenedWith.Recycled(listed.Stash).Contains(EditedItems.Hash(Still)));
        // A store call queued behind the expiry answers once the expiry has run.
        await listed.Stash.Store!.Item(cat.Id);
        Assert.IsNull(OpenedWith.Undo(listed.Stash, cat.Id), "the record cleared (G27)");
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(listed.Listing.Items.Single().FilePath), "the item stays the rewrite");
        Assert.IsEmpty(listed.Notifications.Shown);
    }

    [TestMethod]
    public async Task ASecondRewriteWhileTheFirstNotificationShowsLeavesOneNotificationWhoseUndoReturnsToTheFirstAdoption()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        await Chosen(listed, cat);
        var copy = listed.Dialogs.OpenedWith.Single();
        var (first, second) = (MediaFixtures.DistinctPng(18, 4, 6), MediaFixtures.DistinctPng(19, 6, 4));
        await OpenedWith.RewriteWhenFree(copy, first);
        await OpenedWith.Eventually(listed, () => Said(listed).Count == 1);
        var firstNotice = listed.Notifications.Shown.Single();
        var firstRevision = listed.Listing.Items.Single().RenderedRevision;

        await OpenedWith.RewriteWhenFree(copy, second);
        await OpenedWith.Eventually(listed, () => listed.Listing.Items.Single().RenderedRevision != firstRevision && Said(listed).Count == 1 && !listed.Notifications.Shown.Contains(firstNotice));

        var secondNotice = listed.Notifications.Shown.Single();
        Assert.AreEqual(Replaced("cat"), secondNotice.Text);
        Assert.AreEqual(listed.Listing.Items.Single().RenderedRevision, OpenedWith.Undo(listed.Stash, cat.Id)!.Revision, "the one Undo standing is the second adoption's");
        CollectionAssert.Contains(OpenedWith.Recycled(listed.Stash), EditedItems.Hash(Still), "the first adoption's previous original expired by the second (G29)");

        await listed.Notifications.Undo(secondNotice);

        CollectionAssert.AreEqual(first, File.ReadAllBytes(listed.Listing.Items.Single().FilePath), "back to just before the second adoption");
        Assert.AreEqual(EditedItems.Hash(first), OpenedWith.OriginalHash(listed.Stash, cat.Id));
        CollectionAssert.Contains(OpenedWith.Recycled(listed.Stash), EditedItems.Hash(second));
        Assert.IsEmpty(listed.Notifications.Shown, "the first notification never came back");
    }

    [TestMethod]
    public async Task AskedAgainOnAnUnchangedCopyTheDialogOpensOnAFreshCopyWhichIsWatched()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        await Chosen(listed, cat);
        await Chosen(listed, cat);

        var (earlier, fresh) = (listed.Dialogs.OpenedWith[0], listed.Dialogs.OpenedWith[1]);
        Assert.AreNotEqual(OpenedWith.FolderOf(earlier), OpenedWith.FolderOf(fresh), "a fresh copy in a fresh folder (G27)");
        Assert.IsFalse(File.Exists(earlier));
        CollectionAssert.AreEqual(new[] { cat.Id }, listed.OpenWith.Watching.ToList());

        await OpenedWith.RewriteWhenFree(fresh, MediaFixtures.DistinctPng(20, 5, 5));
        await OpenedWith.Eventually(listed, () => Said(listed).Count == 1);
        Assert.AreEqual(Replaced("cat"), Said(listed).Single(), "the fresh copy is the one watched");
    }

    [TestMethod]
    public async Task AskedAgainWhileARewriteAwaitsTheSameCopyIsWatchedAndAdoptedOnce()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        await Chosen(listed, cat);
        Assert.IsTrue((await listed.Editing.Open(cat, EditOrigin.Grid)).Succeeded);
        var copy = listed.Dialogs.OpenedWith.Single();
        await OpenedWith.RewriteWhenFree(copy, MediaFixtures.DistinctPng(21, 5, 3));
        await OpenedWith.Eventually(listed, () => OpenedWith.Record(listed.Stash, cat.Id)?.Awaiting == true);

        await Chosen(listed, cat);
        Assert.AreEqual(copy, listed.Dialogs.OpenedWith[1], "the rewrite awaiting is kept (G27)");
        Assert.IsTrue(listed.Editing.Cancel());
        await Handing.Eventually(() => Said(listed).Count == 1);
        await Task.Delay(300);

        Assert.AreEqual(Replaced("cat"), Said(listed).Single(), "one adoption");
    }

    [TestMethod]
    public async Task ARefusedBeginIsNamedInTheWindowAndOpensNoDialog()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        await listed.Deletion.Delete([cat]);

        var refused = await Chosen(listed, cat);

        Assert.AreEqual("it is no longer in the stash", refused.Failure?.Reason, refused.Failure?.Sentence);
        Assert.AreEqual(refused.Failure!.Sentence, listed.Notices.Shown.Last().Text, "named in the window, where it was asked (G35)");
        Assert.IsEmpty(listed.Dialogs.OpenedWith, "no dialog");
        Assert.IsEmpty(listed.OpenWith.Watching);

        using var opening = new OpenWith(new Listing(_ => { }), listed.Notifications, listed.Editing, listed.Dialogs, listed.OpenWithClock, action => action());
        var notOpen = await opening.Begin(null!, cat, _ => Assert.Fail("no dialog while the stash is not open"));
        Assert.AreEqual(new Failure(OpenWith.Action, "cat", "your stash is still opening"), notOpen.Failure);
    }

    [TestMethod]
    public async Task TheDialogsFailureOrExceptionIsNamedTheFlagLetGoAndTheCopyStillWatched()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        var up = new List<bool>();
        listed.Dialogs.OpenWithAnswer = () => Task.FromResult<Outcome>(new Failure(OpenWith.Action, "cat.png", "the shell refused"));

        var failed = await Chosen(listed, cat, up.Add);
        listed.Dialogs.OpenWithAnswer = () => throw new InvalidOperationException("no shell here");
        var thrown = await Chosen(listed, cat, up.Add);

        Assert.AreEqual("Open with… failed: cat.png, the shell refused", failed.Failure?.Sentence);
        Assert.AreEqual("Open with… failed: cat.png, no shell here", thrown.Failure?.Sentence, "an exception is a failure to name (G35)");
        CollectionAssert.AreEqual(new[] { failed.Failure!.Sentence, thrown.Failure!.Sentence }, listed.Notices.Shown.Select(notice => notice.Text).ToList(), "both named in the window");
        CollectionAssert.AreEqual(new[] { true, false, true, false }, up, "the flag let go each time (G2)");

        await OpenedWith.RewriteWhenFree(listed.Dialogs.OpenedWith.Last(), MediaFixtures.DistinctPng(22, 4, 4));
        await OpenedWith.Eventually(listed, () => Said(listed).Count == 1);
        Assert.AreEqual(Replaced("cat"), Said(listed).Single(), "the copy watched all the same");
    }

    [TestMethod]
    public async Task AnAdoptionWhoseStepIsRefusedAfterItsCommitIsNamedAboveTheTrayAndSaysNothingWasReplaced()
    {
        List<FileStream> holds = [];
        using var listed = await ListedStash.Open();
        try
        {
            var cat = await Imported(listed, "cat");
            await Chosen(listed, cat);
            // The reader holds what it read, as a scanner may (E3), so the adopted file cannot be placed after the commit.
            listed.Stash.Reader = OpenedWith.Holding(holds);

            await OpenedWith.RewriteWhenFree(listed.Dialogs.OpenedWith.Single(), MediaFixtures.DistinctPng(23, 4, 4));
            await OpenedWith.Eventually(listed, () => Said(listed).Count == 1);
            await Task.Delay(300);

            Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", HeldFileRetry.HeldReason).Sentence, Said(listed).Single(), "named, and nothing says it was replaced (G35, R9)");
            Assert.IsFalse(listed.Listing.Items.Any(item => item.Id == cat.Id), "the listing read again: unlisted while its step waits");
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }
    }

    [TestMethod]
    public async Task ASessionRecordedAtStartIsWatchedAgainAndTheStartsAdoptionIsSaidWithAnUndoThatRestores()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        await Chosen(listed, cat);
        var copy = listed.Dialogs.OpenedWith.Single();
        var rewrite = MediaFixtures.DistinctPng(30, 6, 5);

        // Rewritten while ImageStash is not running: the start adopts it before anything reads the stash.
        using var restarted = await listed.Restart(_ => OpenedWith.Rewrite(copy, rewrite));
        await OpenedWith.Eventually(restarted, () => Said(restarted).Count == 1);

        Assert.AreEqual(Replaced("cat"), Said(restarted).Single(), "the start's adoption said (G27, R9)");
        CollectionAssert.AreEqual(new[] { cat.Id }, restarted.OpenWith.Watching.ToList(), "its session watched again (State)");
        Assert.AreEqual(EditedItems.Hash(rewrite), OpenedWith.OriginalHash(restarted.Stash, cat.Id));

        await restarted.Notifications.Undo(restarted.Notifications.Shown.Single());

        Assert.AreEqual(EditedItems.Hash(Still), OpenedWith.OriginalHash(restarted.Stash, cat.Id), "its Undo, by the revision the start's adoption named, restores the previous original");
        CollectionAssert.Contains(OpenedWith.Recycled(restarted.Stash), EditedItems.Hash(rewrite), "and recycles the adopted picture");
        Assert.IsEmpty(Said(restarted));

        var again = MediaFixtures.DistinctPng(31, 5, 6);
        Assert.IsNull(restarted.OpenWithClock.NextDue, "nothing waits on the asking's clock before the rewrite");
        await OpenedWith.RewriteWhenFree(copy, again);
        await OpenedWith.Eventually(restarted, () => Said(restarted).Count == 1);
        Assert.AreEqual(EditedItems.Hash(again), OpenedWith.OriginalHash(restarted.Stash, cat.Id), "a rewrite after the start adopted");
        Assert.IsNull(restarted.OpenWithClock.NextDue, "seen by the watcher first: nothing waits on the asking's clock");
        Assert.AreEqual(0L, restarted.Uses());
    }

    [TestMethod]
    public async Task AStartWhoseSessionsCannotBeReadNamesItAndSaysItsAdoptionOnceTheStoreCanBeRead()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        await Chosen(listed, cat);
        var copy = listed.Dialogs.OpenedWith.Single();
        var rewrite = MediaFixtures.DistinctPng(35, 6, 5);
        Microsoft.Data.Sqlite.SqliteConnection? holder = null;
        try
        {
            // Another program holds the store as the window is told it opened (E3).
            using var restarted = await listed.Restart(_ => OpenedWith.Rewrite(copy, rewrite), beforeListed: stash => holder = stash.HoldStore());
            await Handing.Eventually(() => Said(restarted).Count == 1);
            Assert.AreNotEqual(Replaced("cat"), Said(restarted).Single(), "the asking's failure named (G35)");
            Assert.IsEmpty(restarted.OpenWith.Watching);

            holder!.Dispose();
            await restarted.Listing.Refresh();

            await Handing.Eventually(() => Said(restarted).Contains(Replaced("cat")));
            CollectionAssert.AreEqual(new[] { cat.Id }, restarted.OpenWith.Watching.ToList(), "asked again at the next change of the listing, and watched");
            await restarted.Notifications.Undo(restarted.Notifications.Shown.Single(notice => notice.Text == Replaced("cat")));
            Assert.AreEqual(EditedItems.Hash(Still), OpenedWith.OriginalHash(restarted.Stash, cat.Id), "the start's adoption kept for its notification, whose Undo restores (G27, R9)");
        }
        finally
        {
            holder?.Dispose();
        }
    }

    [TestMethod]
    public async Task AStartsAdoptionWhoseItemCannotBeReadIsSaidAtTheNextChangeOfTheListingOnceItCan()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        await Chosen(listed, cat);
        var copy = listed.Dialogs.OpenedWith.Single();

        // The item's read refused while the sessions' read is not: a table only the item's read reads, renamed away.
        using var restarted = await listed.Restart(_ => OpenedWith.Rewrite(copy, MediaFixtures.DistinctPng(36, 6, 5)), beforeListed: stash => stash.SqlWhileOpen("ALTER TABLE tags RENAME TO tags_away"));
        await Handing.Eventually(() => Said(restarted).Count == 1);
        Assert.AreNotEqual(Replaced("cat"), Said(restarted).Single(), "the item's read named (G35)");
        CollectionAssert.AreEqual(new[] { cat.Id }, restarted.OpenWith.Watching.ToList(), "its session watched again all the same");

        restarted.Stash.SqlWhileOpen("ALTER TABLE tags_away RENAME TO tags");
        await restarted.Listing.Refresh();

        await Handing.Eventually(() => Said(restarted).Contains(Replaced("cat")));
        await restarted.Notifications.Undo(restarted.Notifications.Shown.Single(notice => notice.Text == Replaced("cat")));
        Assert.AreEqual(EditedItems.Hash(Still), OpenedWith.OriginalHash(restarted.Stash, cat.Id), "said once it could be read, with an Undo that restores (G27, R9)");
    }

    [TestMethod]
    public async Task OfSessionsRecordedAtStartOnlyTheAdoptedOneIsSaidAHeldOneIsAskedOnceASecondAndAnUnchangedOneIsWatched()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        var dog = await Imported(listed, "dog", MediaFixtures.Bytes("transparent.png"));
        var owl = await Imported(listed, "owl", MediaFixtures.DistinctPng(40, 5, 5));
        await Chosen(listed, cat);
        await Chosen(listed, dog);
        await Chosen(listed, owl);
        var (catCopy, dogCopy, owlCopy) = (listed.Dialogs.OpenedWith[0], listed.Dialogs.OpenedWith[1], listed.Dialogs.OpenedWith[2]);
        FileStream? held = null;
        try
        {
            using var restarted = await listed.Restart(_ =>
            {
                OpenedWith.Rewrite(catCopy, MediaFixtures.DistinctPng(41, 4, 4));
                OpenedWith.Rewrite(dogCopy, MediaFixtures.DistinctPng(42, 4, 4));
                // Held as a program that keeps it open after writing holds it, through the start and after.
                held = new FileStream(dogCopy, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            });
            var clock = restarted.OpenWithClock;
            await Handing.Eventually(() => Said(restarted).Count == 1 && clock.NextDue == OpenWith.AskedEvery);

            CollectionAssert.AreEqual(new[] { Replaced("cat") }, Said(restarted), "one notification: the start adopted cat alone (R9)");
            CollectionAssert.AreEquivalent(new[] { cat.Id, dog.Id, owl.Id }, restarted.OpenWith.Watching.ToList(), "every recorded session watched again (State)");

            clock.Advance(OpenWith.AskedEvery);
            Assert.AreEqual(2 * OpenWith.AskedEvery, clock.NextDue, "dog's copy still held at the second: asked again a second later");
            Assert.HasCount(1, Said(restarted));

            held!.Dispose();
            clock.Advance(OpenWith.AskedEvery);
            CollectionAssert.AreEqual(new[] { Replaced("cat"), Replaced("dog") }, Said(restarted), "let go, it is adopted at the next second");
            Assert.IsNull(clock.NextDue);

            await OpenedWith.RewriteWhenFree(owlCopy, MediaFixtures.DistinctPng(43, 4, 4));
            await OpenedWith.Eventually(restarted, () => Said(restarted).Count == 3);
            Assert.AreEqual(Replaced("owl"), Said(restarted)[2], "the unchanged one, watched again, adopted once rewritten");
        }
        finally
        {
            held?.Dispose();
        }
    }

    [TestMethod]
    public async Task ADeletesUndoKeepsTheSessionWatchedAndARewriteMadeMeanwhileIsAdoptedOnceTheItemIsBack()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        await Chosen(listed, cat);
        var copy = listed.Dialogs.OpenedWith.Single();
        await listed.Deletion.Delete([cat]);
        var deleted = listed.Notices.Shown.Single();

        var rewrite = MediaFixtures.DistinctPng(32, 7, 7);
        await OpenedWith.RewriteWhenFree(copy, rewrite);
        await Task.Delay(300);
        CollectionAssert.AreEqual(new[] { cat.Id }, listed.OpenWith.Watching.ToList(), "deleted with its Undo standing: still watched (How the guarantees hold G27)");
        Assert.IsEmpty(Said(listed), "nothing adopted or named while it is not listed");

        await listed.Notices.Undo(deleted);

        await Handing.Eventually(() => Said(listed).Count == 1);
        Assert.AreEqual(Replaced("cat"), Said(listed).Single(), "the rewrite made meanwhile adopted once the item is back");
        Assert.AreEqual(EditedItems.Hash(rewrite), OpenedWith.OriginalHash(listed.Stash, cat.Id));
    }

    [TestMethod]
    [DataRow("delete", DisplayName = "a Delete's notice gone")]
    [DataRow("capture", DisplayName = "a capture card's Undo")]
    [DataRow("import", DisplayName = "an import batch's Undo")]
    public async Task ASessionWhoseItemIsRemovedForGoodIsLetGoWithNothingSaid(string removal)
    {
        using var listed = await ListedStash.Open();
        StashItem cat;
        Func<Task> remove;
        if (removal == "import")
        {
            var dropped = await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));
            cat = dropped.Single().Item!;
            var batch = listed.Notices.Shown.Single();
            remove = () => listed.Notices.Undo(batch);
        }
        else
        {
            cat = await Imported(listed, "cat");
            if (removal == "capture")
            {
                listed.CaptureNotification.Raise(cat, CaptureKind.Screenshot, onClipboard: false, lengthMs: null);
                var card = listed.Notifications.Shown.Single();
                remove = () => listed.Notifications.Undo(card);
            }
            else
            {
                remove = async () =>
                {
                    await listed.Deletion.Delete([cat]);
                    listed.Stash.Clock.Now += Notices.Lasting;
                    listed.Notices.Sweep();
                };
            }
        }

        await Chosen(listed, cat);
        var folder = Path.GetDirectoryName(listed.Dialogs.OpenedWith.Single())!;
        CollectionAssert.AreEqual(new[] { cat.Id }, listed.OpenWith.Watching.ToList());

        await remove();

        await Handing.Eventually(() => listed.OpenWith.Watching.Count == 0);
        Assert.IsFalse(Directory.Exists(folder), "Store deleted the copy and its folder with the item (State)");
        await Task.Delay(300);
        Assert.IsEmpty(Said(listed), "nothing said or named above the tray");
        Assert.IsNull(listed.OpenWithClock.NextDue, "and nothing asks about it again");
    }

    [TestMethod]
    public async Task ACopysFolderDeletedWhileWatchedIsNamedOnceAskedOnceASecondAndWatchedAgainOnceItIsBack()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        await Chosen(listed, cat);
        var copy = listed.Dialogs.OpenedWith.Single();
        var folder = Path.GetDirectoryName(copy)!;
        var clock = listed.OpenWithClock;

        // Behind ImageStash's back, as the session still stands.
        await OpenedWith.DeleteFolderWhenFree(folder);
        await Handing.Eventually(() => Said(listed).Count == 1 && clock.NextDue == OpenWith.AskedEvery);

        StringAssert.StartsWith(Said(listed).Single(), "Watch the Open with… copy failed: cat.png, ", "named once (G35)");
        CollectionAssert.AreEqual(new[] { cat.Id }, listed.OpenWith.Watching.ToList(), "its session still recorded, so still watched");
        clock.Advance(OpenWith.AskedEvery);
        Assert.AreEqual(2 * OpenWith.AskedEvery, clock.NextDue, "nothing there at the second: asked again a second later");
        Assert.HasCount(1, Said(listed), "and not named again");

        var rewrite = MediaFixtures.DistinctPng(33, 6, 6);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(copy, rewrite);
        await Task.Delay(300);
        Assert.HasCount(1, Said(listed), "no watcher: nothing seen before the second");
        clock.Advance(OpenWith.AskedEvery);

        Assert.AreEqual(Replaced("cat"), Said(listed)[1], "the next second watched the folder again and the rewrite was adopted");
        Assert.IsNull(clock.NextDue, "watched again, so asked no more");
    }

    [TestMethod]
    public async Task AStartThatCannotWatchACopyNamesItOnceAndAsksOnceASecondUntilItCan()
    {
        using var listed = await ListedStash.Open();
        var cat = await Imported(listed, "cat");
        await Chosen(listed, cat);
        var copy = listed.Dialogs.OpenedWith.Single();
        var folder = Path.GetDirectoryName(copy)!;

        // Gone after the start classified the copy and before the window could watch it.
        using var restarted = await listed.Restart(beforeListed: _ => Directory.Delete(folder, recursive: true));
        var clock = restarted.OpenWithClock;
        await Handing.Eventually(() => Said(restarted).Count == 1 && clock.NextDue == OpenWith.AskedEvery);

        StringAssert.StartsWith(Said(restarted).Single(), "Watch the Open with… copy failed: cat.png, ", "named once, nothing crashed (G35)");
        CollectionAssert.AreEqual(new[] { cat.Id }, restarted.OpenWith.Watching.ToList());

        var rewrite = MediaFixtures.DistinctPng(34, 6, 6);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(copy, rewrite);
        clock.Advance(OpenWith.AskedEvery);

        Assert.AreEqual(Replaced("cat"), Said(restarted)[1], "the once-a-second asking stood in, then watched it");
        Assert.IsNull(clock.NextDue);
    }

    [TestMethod]
    public void ThePartKeepsNoStoreOfItsOwn()
    {
        // The part's own state and its sessions'; a store an async method holds while it runs is a local, not kept.
        var kept = new[] { typeof(OpenWith), typeof(AdoptionNotifications) }.Concat(typeof(OpenWith).GetNestedTypes(BindingFlags.NonPublic).Where(type => type.GetCustomAttribute<CompilerGeneratedAttribute>() is null));
        var fields = kept.SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));

        Assert.IsFalse(fields.Any(field => field.FieldType == typeof(StashStore)), "every call goes to the store the listing holds at that moment (Composition: Window)");
    }

    /// <summary>An item imported into the listed stash and listed: still.png unless other bytes are given.</summary>
    private static async Task<StashItem> Imported(ListedStash listed, string name, byte[]? bytes = null)
    {
        var item = await listed.Stash.ImportPicture(name, name + ".png", bytes ?? Still);
        await listed.Listing.Refresh();
        return item;
    }

    /// <summary>Open with… chosen from the item menu on <paramref name="item"/>, the dialog's flag told to <paramref name="up"/>.</summary>
    private static Task<Outcome> Chosen(ListedStash listed, StashItem item, Action<bool>? up = null) =>
        listed.ItemMenu.Act(ItemMenuAction.OpenWith, null!, [item], null, () => Assert.Fail("Open with… hides nothing"), up ?? (_ => { }));

    /// <summary>What the notifications above the tray say, oldest first.</summary>
    private static List<string> Said(ListedStash listed) => [.. listed.Notifications.Shown.ToList().Select(notice => notice.Text)];

    private static string Replaced(string name) => $"Replaced “{name}” with the picture another program saved.";

    /// <summary>
    /// The items' files, in the root and <c>originals\</c>, with their hashes; not <c>.work\</c>, where each read of a copy
    /// passes for a moment.
    /// </summary>
    private static List<string> ItemFiles(TestStash stash) =>
    [
        .. Directory.GetFiles(stash.Folder.Root).Concat(Directory.GetFiles(stash.Folder.Originals))
            .Where(path => !Path.GetFileName(path).StartsWith(StashFolder.StoreFileName))
            .Order()
            .Select(path => $"{Path.GetRelativePath(stash.Folder.Root, path)} {EditedItems.Hash(path)}"),
    ];
}
