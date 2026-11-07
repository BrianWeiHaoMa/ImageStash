using System.Runtime.Versioning;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// Beginning an Open with… session (State: Open with…; G9, G27): the finished picture copied under its file name into a
/// new folder of its own under the root, the session recorded with the copy's hash, the item itself untouched (R3); a
/// second Begin refreshing an unchanged copy into a fresh folder, and leaving a rewrite awaiting adoption or a copy
/// another program holds as it is; an undecodable rewrite its classification is the first to read named (G35); an item
/// not in the stash, or a finished picture held past the retry, refused by name with nothing left behind.
/// </summary>
[TestClass]
public sealed class OpenWithBeginTests
{
    [TestMethod]
    public async Task BeginCopiesAnUneditedItemsFileUnderItsFileNameIntoANewFolderAndRecordsItsHash()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");

        var copy = await OpenedWith.Begin(stash, cat);

        Assert.AreEqual(stash.OpenWithRoot, Path.GetDirectoryName(Path.GetDirectoryName(copy)));
        Assert.AreEqual("cat.png", Path.GetFileName(copy));
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(copy));
        Assert.AreEqual(new OpenWithRecord(OpenedWith.FolderOf(copy), "cat.png", EditedItems.Hash(copy), Awaiting: false), OpenedWith.Record(stash, cat.Id));
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(cat.FilePath));
        TestStash.AssertSameItem(cat, (await stash.All()).Single());
        Assert.IsEmpty(stash.WorkFiles());
        Assert.AreEqual(OpenWithCopy.Unchanged, await OpenedWith.Classify(stash, cat.Id));
    }

    [TestMethod]
    [DataRow("still.png")]
    [DataRow("still.webp")]
    public async Task BeginOnAnEditedItemCopiesItsFinishedPictureUnderItsFileName(string fixture)
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", fixture);
        var saved = await stash.SaveEdit(cat, EditedItems.BlackBoxAndBox);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());

        var copy = await OpenedWith.Begin(stash, saved.Value!);

        Assert.AreEqual("cat.png", Path.GetFileName(copy), "the finished picture's name, in F3's format");
        CollectionAssert.AreEqual(File.ReadAllBytes(saved.Value!.FilePath), File.ReadAllBytes(copy), "the render, not the original");
        CollectionAssert.AreNotEqual(MediaFixtures.Bytes(fixture), File.ReadAllBytes(copy));
    }

    [TestMethod]
    public async Task ASecondBeginAfterASaveRefreshesTheCopyIntoAFreshFolderAndKeepsOneSession()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var copy = await OpenedWith.Begin(stash, cat);
        var saved = await stash.SaveEdit(cat, EditedItems.BlackBoxAndBox);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());

        var refreshed = await OpenedWith.Begin(stash, saved.Value!);

        Assert.AreNotEqual(OpenedWith.FolderOf(copy), OpenedWith.FolderOf(refreshed));
        CollectionAssert.AreEqual(new[] { OpenedWith.FolderOf(refreshed) }, OpenedWith.Folders(stash), "the earlier folder deleted once the fresh one was recorded");
        Assert.AreEqual("cat.png", Path.GetFileName(refreshed));
        CollectionAssert.AreEqual(File.ReadAllBytes(saved.Value!.FilePath), File.ReadAllBytes(refreshed));
        Assert.AreEqual(new OpenWithRecord(OpenedWith.FolderOf(refreshed), "cat.png", EditedItems.Hash(refreshed), Awaiting: false), OpenedWith.Record(stash, cat.Id));
        CollectionAssert.AreEqual(new[] { new OpenWithSession(cat.Id, refreshed) }, (await store.OpenWithSessions()).Value!.Sessions.ToList());
    }

    [TestMethod]
    public async Task ASecondBeginAfterARenameNamesTheFreshCopyAfterTheItemsNewFile()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        await OpenedWith.Begin(stash, cat);
        var renamed = await store.Rename(cat, "dog: a fool");
        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());

        var refreshed = await OpenedWith.Begin(stash, renamed.Value!);

        Assert.AreEqual(renamed.Value!.FileName, Path.GetFileName(refreshed), "the file's name, which Windows can spell (P1)");
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(refreshed));
    }

    [TestMethod]
    public async Task ASecondBeginLeavesARewriteAwaitingAdoptionAsItIs()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var copy = await OpenedWith.Begin(stash, cat);
        var rewrite = MediaFixtures.DistinctPng(5, 12, 10);
        OpenedWith.Rewrite(copy, rewrite);
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id));

        var again = await OpenedWith.Begin(stash, cat);

        Assert.AreEqual(copy, again);
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(copy));
        Assert.HasCount(1, OpenedWith.Folders(stash));
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id));
    }

    [TestMethod]
    public async Task ASecondBeginLeavesACopyAnotherProgramHoldsAsItIs()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var copy = await OpenedWith.Begin(stash, cat);
        var recorded = OpenedWith.Record(stash, cat.Id);

        string again;
        using (var hold = TestStash.Hold(copy))
        {
            again = await OpenedWith.Begin(stash, cat);
            var held = new byte[hold.Length];
            hold.ReadExactly(held);
            CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), held, "the hold undisturbed");
        }

        Assert.AreEqual(copy, again);
        Assert.HasCount(1, OpenedWith.Folders(stash));
        Assert.AreEqual(recorded, OpenedWith.Record(stash, cat.Id));
    }

    [TestMethod]
    public async Task ASecondBeginFirstToReadAnUndecodableRewriteNamesItAndALaterOneRefreshes()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var copy = await OpenedWith.Begin(stash, cat);
        var garbage = OpenedWith.Garbage();
        OpenedWith.Rewrite(copy, garbage);

        var begun = await store.BeginOpenWith(cat);

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat.png", MediaDecoder.UnreadableReason), begun.Failure);
        Assert.AreEqual(new OpenWithRecord(OpenedWith.FolderOf(copy), "cat.png", EditedItems.Hash(garbage), Awaiting: false), OpenedWith.Record(stash, cat.Id));
        Assert.HasCount(1, OpenedWith.Folders(stash), "nothing refreshed");

        var refreshed = await OpenedWith.Begin(stash, cat);

        Assert.AreNotEqual(copy, refreshed);
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(refreshed));
        CollectionAssert.AreEqual(new[] { OpenedWith.FolderOf(refreshed) }, OpenedWith.Folders(stash));
    }

    [TestMethod]
    public async Task ARewriteLandingWhileARefreshIsMadeIsKeptAndItsPathAnswered()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var copy = await OpenedWith.Begin(stash, cat);
        var rewrite = MediaFixtures.DistinctPng(5, 12, 10);
        var rewritten = false;
        // The outside program saves the copy it was given while the fresh copy is being read.
        stash.Reader = (path, name, retry) =>
        {
            if (!rewritten && path.StartsWith(stash.OpenWithRoot!, StringComparison.OrdinalIgnoreCase) && path != copy)
            {
                rewritten = true;
                OpenedWith.Rewrite(copy, rewrite);
            }

            return MediaDecoder.ReadPicture(path, name, retry);
        };

        var again = await OpenedWith.Begin(stash, cat);

        Assert.IsTrue(rewritten);
        Assert.AreEqual(copy, again, "the copy the outside program wrote");
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(copy));
        CollectionAssert.AreEqual(new[] { OpenedWith.FolderOf(copy) }, OpenedWith.Folders(stash), "the fresh folder deleted");
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id));
    }

    [TestMethod]
    public async Task ARewriteKeepingTheCopysLengthAndDateLandingWhileARefreshIsMadeIsKept()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.gif");
        var copy = await OpenedWith.Begin(stash, cat);
        // The first colour of the GIF's palette changed: other bytes of the same length, which still decode.
        var rewrite = MediaFixtures.Bytes("still.gif");
        rewrite[13] ^= 0xFF;
        var rewritten = false;
        // The outside program saves the copy it was given while the fresh copy is being read, keeping the file's date as
        // an editor told to preserve it does.
        stash.Reader = (path, name, retry) =>
        {
            if (!rewritten && path.StartsWith(stash.OpenWithRoot!, StringComparison.OrdinalIgnoreCase) && path != copy)
            {
                rewritten = true;
                var date = File.GetLastWriteTimeUtc(copy);
                OpenedWith.Rewrite(copy, rewrite);
                File.SetLastWriteTimeUtc(copy, date);
            }

            return MediaDecoder.ReadPicture(path, name, retry);
        };

        var again = await OpenedWith.Begin(stash, cat);

        Assert.IsTrue(rewritten);
        Assert.AreEqual(copy, again, "the copy the outside program wrote");
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(copy));
        CollectionAssert.AreEqual(new[] { OpenedWith.FolderOf(copy) }, OpenedWith.Folders(stash), "the fresh folder deleted");
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AReplacementSavedWhileARefreshWaitsOnTheStoreIsRefusedAndNeverDeletedUnread()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var copy = await OpenedWith.Begin(stash, cat);
        var saved = stash.Outside("saved.png");
        File.WriteAllBytes(saved, MediaFixtures.DistinctPng(5, 12, 10));
        using var freshRead = new ManualResetEventSlim();
        Microsoft.Data.Sqlite.SqliteConnection? storeHeld = null;
        // Another program holds the store while the fresh copy is read, so the refresh's record waits for it.
        stash.Reader = (path, name, retry) =>
        {
            if (path.StartsWith(stash.OpenWithRoot!, StringComparison.OrdinalIgnoreCase) && path != copy && !freshRead.IsSet)
            {
                storeHeld = stash.HoldStore();
                freshRead.Set();
            }

            return MediaDecoder.ReadPicture(path, name, retry);
        };

        var refreshing = OpenedWith.Begin(stash, cat);
        Assert.IsTrue(freshRead.Wait(TimeSpan.FromSeconds(10)));
        await Task.Delay(300);
        // Meanwhile the outside program saves by replacement: its copy renamed aside, the saved file renamed in.
        IOException? refused = null;
        try
        {
            File.Move(copy, stash.Outside("aside.png"));
            File.Move(saved, copy);
        }
        catch (IOException e)
        {
            refused = e;
        }
        finally
        {
            storeHeld?.Dispose();
        }

        var refreshed = await refreshing;

        Assert.IsNotNull(refused, "the outside program's save fails while the copy it would replace is held, rather than being deleted unread");
        Assert.IsFalse(File.Exists(stash.Outside("aside.png")));
        Assert.IsTrue(File.Exists(saved), "the outside program keeps what it would have saved");
        Assert.AreNotEqual(copy, refreshed);
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(refreshed));
        CollectionAssert.AreEqual(new[] { OpenedWith.FolderOf(refreshed) }, OpenedWith.Folders(stash));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ASecondBeginOnACopyThatStaysMissingMakesItAgain(bool folderToo)
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var copy = await OpenedWith.Begin(stash, cat);
        File.Delete(copy);
        if (folderToo)
        {
            Directory.Delete(Path.GetDirectoryName(copy)!);
        }

        var again = await OpenedWith.Begin(stash, cat);

        Assert.AreNotEqual(copy, again);
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(again));
        CollectionAssert.AreEqual(new[] { OpenedWith.FolderOf(again) }, OpenedWith.Folders(stash));
        Assert.AreEqual(new OpenWithRecord(OpenedWith.FolderOf(again), "cat.png", EditedItems.Hash(again), Awaiting: false), OpenedWith.Record(stash, cat.Id));
    }

    [TestMethod]
    public async Task BeginIsRefusedByNameForAnItemNoLongerInTheStash()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var (gone, marked, _) = await stash.GoneMarkedAndListed();

        var refusedGone = await store.BeginOpenWith(gone);
        var refusedMarked = await store.BeginOpenWith(marked);

        Assert.AreEqual(new Failure(OpenWithBegin.Action, "gone", ItemFactChange.NotInStash), refusedGone.Failure);
        Assert.AreEqual(new Failure(OpenWithBegin.Action, "marked", ItemFactChange.NotInStash), refusedMarked.Failure);
        Assert.IsEmpty(OpenedWith.Folders(stash));
    }

    [TestMethod]
    [DoNotParallelize]
    [SupportedOSPlatform("windows")]
    public async Task AFinishedPictureHeldPastTheRetryIsNamedWithNothingLeftAndOneLetGoWithinItIsCopied()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(1) };
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");

        var held = HeldFiles.HeldFor(cat.FilePath, TimeSpan.FromSeconds(2.5));
        var refused = await store.BeginOpenWith(cat);
        await held;

        Assert.AreEqual(new Failure(OpenWithBegin.Action, "cat", HeldFileRetry.HeldReason), refused.Failure);
        Assert.IsNull(OpenedWith.Record(stash, cat.Id));
        Assert.IsEmpty(OpenedWith.Folders(stash));

        var letGo = HeldFiles.HeldFor(cat.FilePath, TimeSpan.FromMilliseconds(200));
        var copy = await OpenedWith.Begin(stash, cat);
        await letGo;

        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(copy));
    }

    [TestMethod]
    [DoNotParallelize]
    [SupportedOSPlatform("windows")]
    public async Task AStoreCallAskedWhileTheFinishedPictureIsHeldIsAnsweredBetweenTheAttempts()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var held = HeldFiles.HeldFor(cat.FilePath, TimeSpan.FromSeconds(2));

        var beginning = store.BeginOpenWith(cat);
        await Task.Delay(200);
        // Waited for on this thread, which the store's own answer wakes.
        var all = store.All();
        var answeredInTime = all.Wait(TimeSpan.FromMilliseconds(500));
        var beginningWhenAnswered = beginning.IsCompleted;
        var begun = await beginning;
        await held;

        Assert.IsTrue(answeredInTime, "within half a second (G1)");
        Assert.IsTrue(all.Result.Succeeded, all.Result.Failure?.ToString());
        Assert.IsFalse(beginningWhenAnswered, "answered before the copy was made");
        Assert.IsTrue(begun.Succeeded, begun.Failure?.ToString());
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(begun.Value!));
        CollectionAssert.AreEqual(new[] { OpenedWith.FolderOf(begun.Value!) }, OpenedWith.Folders(stash), "the refused attempts' folders deleted");
    }
}
