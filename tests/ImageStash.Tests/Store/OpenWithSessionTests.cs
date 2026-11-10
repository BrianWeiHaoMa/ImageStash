using System.Diagnostics;
using System.Runtime.Versioning;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// An item's Open with… session (State: Open with…; G27, G35, G1, R8): the copy read only once it opens without sharing,
/// its bytes taken into .work\ and read there, never written; unchanged, still held, or a rewrite whose hash is recorded,
/// awaiting adoption when it decodes and refused naming the copy when it does not; the store answering other calls
/// between the attempts at a held copy; the sessions listed; a session ended with its row removed for good and kept
/// through a Delete's mark; and every session classified at start, after the pending steps and before anything reads.
/// </summary>
[TestClass]
public sealed class OpenWithSessionTests
{
    [TestMethod]
    public async Task TheSameBytesAreUnchangedEvenWrittenAgainAndClassifyingNeverWritesTheCopy()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        var recorded = OpenedWith.Record(stash, cat.Id);
        var written = File.GetLastWriteTimeUtc(copy);

        Assert.AreEqual(OpenWithCopy.Unchanged, await OpenedWith.Classify(stash, cat.Id));
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(copy), "read, never written (R3)");
        OpenedWith.Rewrite(copy, File.ReadAllBytes(copy));
        Assert.AreEqual(OpenWithCopy.Unchanged, await OpenedWith.Classify(stash, cat.Id));

        Assert.AreEqual(recorded, OpenedWith.Record(stash, cat.Id));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ARewriteThatDecodesAwaitsAdoptionAndIsNeverReadAsUnchangedUntilAdopted()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        var rewrite = MediaFixtures.DistinctPng(5, 12, 10);
        OpenedWith.Rewrite(copy, rewrite);

        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id));
        Assert.AreEqual(new OpenWithRecord(OpenedWith.FolderOf(copy), "cat.png", EditedItems.Hash(rewrite), Awaiting: true), OpenedWith.Record(stash, cat.Id));
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id), "asked again with nothing changed");

        Assert.IsEmpty(stash.WorkFiles());
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(cat.FilePath), "nothing adopted");
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(copy));
    }

    [TestMethod]
    public async Task ARewriteIsJudgedByItsBytesSoAJpegUnderThePngNameAwaitsAdoption()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        OpenedWith.Rewrite(copy, MediaFixtures.Bytes("photo.jpg"));

        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id));
    }

    [TestMethod]
    public async Task AnUndecodableRewriteIsRefusedNamingTheCopyWithItsHashRecordedAndTheSessionGoesOn()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        var garbage = OpenedWith.Garbage();
        OpenedWith.Rewrite(copy, garbage);

        var refused = await stash.Store!.ClassifyOpenWith(cat.Id);

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat.png", MediaDecoder.UnreadableReason), refused.Failure);
        Assert.IsTrue(MediaDecoder.IsUnreadable(refused.Failure!));
        Assert.AreEqual(new OpenWithRecord(OpenedWith.FolderOf(copy), "cat.png", EditedItems.Hash(garbage), Awaiting: false), OpenedWith.Record(stash, cat.Id));
        Assert.AreEqual(OpenWithCopy.Unchanged, await OpenedWith.Classify(stash, cat.Id), "the same bytes are not refused again");

        OpenedWith.Rewrite(copy, MediaFixtures.DistinctPng(6, 10, 8));
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id));

        OpenedWith.Rewrite(copy, MediaFixtures.Bytes("still.png")[..^12]);
        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat.png", MediaDecoder.UnreadableReason), (await stash.Store.ClassifyOpenWith(cat.Id)).Failure, "a PNG cut short of its end");
        Assert.IsFalse(OpenedWith.Record(stash, cat.Id)!.Awaiting, "the mark cleared");
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ACopyReplacedByAnotherFileIsReadAtItsPath()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        var saved = copy + ".saving";
        File.WriteAllBytes(saved, MediaFixtures.DistinctPng(7, 9, 9));
        File.Move(saved, copy, overwrite: true);

        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id));
        Assert.AreEqual(EditedItems.Hash(copy), OpenedWith.Record(stash, cat.Id)!.Sha256);
    }

    [TestMethod]
    [DoNotParallelize]
    [SupportedOSPlatform("windows")]
    public async Task ACopyAnotherProgramHoldsIsStillHeldAfterTheRetryWithNothingRecordedOrWritten()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        OpenedWith.Rewrite(copy, MediaFixtures.DistinctPng(5, 12, 10));
        var recorded = OpenedWith.Record(stash, cat.Id);
        var written = File.GetLastWriteTimeUtc(copy);

        var locked = HeldFiles.HeldFor(copy, TimeSpan.FromSeconds(2));
        var watch = Stopwatch.StartNew();
        var whileLocked = await OpenedWith.Classify(stash, cat.Id);
        var took = watch.Elapsed;
        await locked;
        OpenWithCopy whileRead;
        using (TestStash.Hold(copy))
        {
            whileRead = await OpenedWith.Classify(stash, cat.Id);
        }

        Assert.AreEqual(OpenWithCopy.StillHeld, whileLocked);
        Assert.IsGreaterThanOrEqualTo(TestStash.ShortRetry, took, "retried first");
        Assert.IsLessThan(TimeSpan.FromSeconds(1.5), took, "and no longer");
        Assert.AreEqual(OpenWithCopy.StillHeld, whileRead, "a hold that shares reading still refuses an open without sharing");
        Assert.AreEqual(recorded, OpenedWith.Record(stash, cat.Id));
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(copy));
        Assert.IsEmpty(stash.WorkFiles());
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id), "read once let go");
    }

    [TestMethod]
    [DoNotParallelize]
    [SupportedOSPlatform("windows")]
    public async Task ACopyLetGoWithinTheRetryIsClassified()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(1) };
        var (cat, copy) = await Begun(stash);
        OpenedWith.Rewrite(copy, MediaFixtures.DistinctPng(5, 12, 10));

        var locked = HeldFiles.HeldFor(copy, TimeSpan.FromMilliseconds(200));
        var classified = await OpenedWith.Classify(stash, cat.Id);
        await locked;

        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, classified);
    }

    [TestMethod]
    public async Task NoFileAtTheCopysPathIsStillHeldAndTheSessionGoesOn()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        var recorded = OpenedWith.Record(stash, cat.Id);

        File.Delete(copy);
        Assert.AreEqual(OpenWithCopy.StillHeld, await OpenedWith.Classify(stash, cat.Id), "a replacement may be half way");
        Directory.Delete(Path.GetDirectoryName(copy)!);
        Assert.AreEqual(OpenWithCopy.StillHeld, await OpenedWith.Classify(stash, cat.Id), "its folder gone too");

        Assert.AreEqual(recorded, OpenedWith.Record(stash, cat.Id));
        CollectionAssert.AreEqual(new[] { new OpenWithSession(cat.Id, copy) }, (await stash.Store!.OpenWithSessions()).Value!.Sessions.ToList());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AStoreCallAskedWhileACopyIsHeldIsAnsweredBetweenTheAttempts()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        var (cat, copy) = await Begun(stash);
        using var hold = TestStash.Hold(copy);

        var watch = Stopwatch.StartNew();
        var classifying = stash.Store!.ClassifyOpenWith(cat.Id);
        await Task.Delay(200);
        // Waited for on this thread, which the store's own answer wakes.
        var all = stash.Store.All();
        var answeredInTime = all.Wait(TimeSpan.FromMilliseconds(500));
        var classifyingWhenAnswered = classifying.IsCompleted;
        var classified = await classifying;

        Assert.IsTrue(answeredInTime, "within half a second (G1)");
        Assert.IsTrue(all.Result.Succeeded, all.Result.Failure?.ToString());
        Assert.IsFalse(classifyingWhenAnswered, "answered before the classification");
        Assert.AreEqual(OpenWithCopy.StillHeld, classified.Value);
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(3), watch.Elapsed, "once the retry has run");
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AStoreCallAskedWhileTheTakenFileIsHeldIsAnsweredWithoutWaitingForItsDiscard()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        var (cat, copy) = await Begun(stash);
        OpenedWith.Rewrite(copy, MediaFixtures.DistinctPng(5, 12, 10));
        var held = new TaskCompletionSource<FileStream>(TaskCreationOptions.RunContinuationsAsynchronously);
        // A scanner takes the freshly written file in .work\ as Media finishes reading it, and lets go once the store's
        // answer has been measured. It lets go from this thread, not on a timer: a busy thread pool held a timed
        // release past the discard's three-second retry on the hosted runner.
        stash.Reader = (path, name, retry) =>
        {
            var read = MediaDecoder.ReadPicture(path, name, retry);
            held.TrySetResult(TestStash.Hold(path));
            return read;
        };

        var classifying = stash.Store!.ClassifyOpenWith(cat.Id);
        // Asked once the read has returned, while the taken file is still held; waited for on this thread, which the
        // store's own answer wakes, so what is measured is the store's thread alone.
        var hold = held.Task.Result;
        Thread.Sleep(300);
        var all = stash.Store.All();
        var answeredInTime = all.Wait(TimeSpan.FromMilliseconds(500));
        hold.Dispose();
        var classified = await classifying;

        Assert.IsTrue(answeredInTime, "within half a second (G1)");
        Assert.IsTrue(all.Result.Succeeded, all.Result.Failure?.ToString());
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, classified.Value);
        Assert.IsEmpty(stash.WorkFiles(), "discarded once let go");
    }

    [TestMethod]
    public async Task ClassifyIsRefusedByNameWithoutASessionOrForAnItemNotInTheStash()
    {
        using var stash = new TestStash();
        var (cat, _) = await Begun(stash);
        var dog = await stash.ImportFixture("dog", "photo.jpg");
        Assert.IsTrue((await stash.Store!.Delete([cat.Id])).Single().Succeeded);

        var noSession = await stash.Store.ClassifyOpenWith(dog.Id);
        var marked = await stash.Store.ClassifyOpenWith(cat.Id);

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "dog", "it has no Open with… session"), noSession.Failure);
        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", ItemFactChange.NotInStash), marked.Failure);
    }

    [TestMethod]
    public async Task SessionsAreListedAndEndWithTheirRowsRemovedForGoodButNotWithADeletesMark()
    {
        using var stash = new TestStash();
        var (cat, catCopy) = await Begun(stash);
        var dog = await stash.ImportFixture("dog", "photo.jpg");
        var dogCopy = await OpenedWith.Begin(stash, dog);
        var store = stash.Store!;

        var both = (await store.OpenWithSessions()).Value!;
        CollectionAssert.AreEqual(new[] { new OpenWithSession(cat.Id, catCopy), new OpenWithSession(dog.Id, dogCopy) }, both.Sessions.ToList());
        Assert.IsEmpty(both.Adoptions);

        Assert.IsTrue((await store.Remove([cat.Id])).Single().Succeeded);
        CollectionAssert.AreEqual(new[] { new OpenWithSession(dog.Id, dogCopy) }, (await store.OpenWithSessions()).Value!.Sessions.ToList());
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(catCopy)), "the copy and its folder deleted");

        var deleted = (await store.Delete([dog.Id])).Single();
        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { new OpenWithSession(dog.Id, dogCopy) }, (await store.OpenWithSessions()).Value!.Sessions.ToList(), "kept while Undo can come");
        Assert.IsTrue(File.Exists(dogCopy));
        Assert.IsTrue((await store.UndoDelete([deleted.Value!])).Single().Succeeded);
        Assert.AreEqual(OpenWithCopy.Unchanged, await OpenedWith.Classify(stash, dog.Id), "the session as it was");

        Assert.IsTrue((await store.Delete([dog.Id])).Single().Succeeded);
        Assert.IsTrue((await store.FinishDelete([dog.Id])).Single().Succeeded);
        Assert.IsEmpty((await store.OpenWithSessions()).Value!.Sessions);
        Assert.IsEmpty(OpenedWith.Folders(stash));
    }

    [TestMethod]
    public async Task AnEndedSessionsFolderHoldingAnotherFileKeepsItWithoutTheCopy()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        var left = Path.Combine(Path.GetDirectoryName(copy)!, "cat.png.bak");
        File.WriteAllText(left, "the outside program's");

        Assert.IsTrue((await stash.Store!.Remove([cat.Id])).Single().Succeeded);

        Assert.IsFalse(File.Exists(copy));
        Assert.IsTrue(File.Exists(left));
    }

    [TestMethod]
    public async Task AtStartARewrittenCopyIsClassifiedBeforeAnyQueryAnswers()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        stash.Close();
        OpenedWith.Rewrite(copy, MediaFixtures.DistinctPng(5, 12, 10));
        stash.ReadCalls.Clear();

        await stash.OpenCleanly();

        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.ReadCalls, "read during the open");
        Assert.IsFalse(OpenedWith.Record(stash, cat.Id)!.Awaiting, "classified, then adopted at once");
        CollectionAssert.AreEqual(new[] { OpenedWith.Undo(stash, cat.Id)!.HeldCopy }, stash.WorkFiles(), "nothing taken is left, the previous original held");
        Assert.AreEqual(OpenWithCopy.Unchanged, await OpenedWith.Classify(stash, cat.Id));
    }

    [TestMethod]
    public async Task AtStartAnUndecodableRewriteIsNamedWithItsHashRecorded()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        stash.Close();
        var garbage = OpenedWith.Garbage(2);
        OpenedWith.Rewrite(copy, garbage);

        var opening = await stash.Open();

        Assert.IsNotNull(opening.Store);
        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat.png", MediaDecoder.UnreadableReason), opening.Failures.Single());
        Assert.AreEqual(new OpenWithRecord(OpenedWith.FolderOf(copy), "cat.png", EditedItems.Hash(garbage), Awaiting: false), OpenedWith.Record(stash, cat.Id));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AtStartAMissingCopyEndsItsSessionNamedAndItsFolderGoes(bool folderToo)
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        stash.Close();
        File.Delete(copy);
        if (folderToo)
        {
            Directory.Delete(Path.GetDirectoryName(copy)!);
        }

        var opening = await stash.Open();

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat.png", HeldFileRetry.MissingReason), opening.Failures.Single());
        Assert.IsNull(OpenedWith.Record(stash, cat.Id));
        Assert.IsEmpty((await opening.Store!.OpenWithSessions()).Value!.Sessions);
        Assert.IsEmpty(OpenedWith.Folders(stash));
        TestStash.AssertSameItem(cat, (await stash.All()).Single());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AtStartACopyReplacedHalfWayIsWaitedForAndClassified()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        var (cat, copy) = await Begun(stash);
        stash.Close();
        // The outside program has removed the copy and renames its new version in a moment after the start begins.
        var saving = copy + ".saving";
        File.WriteAllBytes(saving, MediaFixtures.DistinctPng(5, 12, 10));
        File.Delete(copy);
        var landing = Task.Run(async () =>
        {
            await Task.Delay(1500);
            File.Move(saving, copy);
        });

        var opening = await stash.Open();
        await landing;

        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        Assert.IsNotNull(OpenedWith.Undo(stash, cat.Id), "classified, then adopted");
        CollectionAssert.AreEqual(File.ReadAllBytes(copy), File.ReadAllBytes(stash.Folder.InRoot("cat.png")));
    }

    [TestMethod]
    [DoNotParallelize]
    [SupportedOSPlatform("windows")]
    public async Task AtStartAHeldCopyIsLeftForLaterWithNothingNamed()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        var recorded = OpenedWith.Record(stash, cat.Id);
        stash.Close();
        OpenedWith.Rewrite(copy, MediaFixtures.DistinctPng(5, 12, 10));

        var locked = HeldFiles.HeldFor(copy, TimeSpan.FromSeconds(1.5));
        var opening = await stash.Open();
        await locked;

        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        Assert.AreEqual(recorded, OpenedWith.Record(stash, cat.Id));
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id));
    }

    [TestMethod]
    public async Task AtStartAMarkedRowsSessionEndsWithTheFinishOfItsDeleteUnread()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        Assert.IsTrue((await stash.Store!.Delete([cat.Id])).Single().Succeeded);
        stash.Close();
        OpenedWith.Rewrite(copy, MediaFixtures.DistinctPng(5, 12, 10));
        stash.ReadCalls.Clear();

        await stash.OpenCleanly();

        Assert.IsEmpty(stash.ReadCalls);
        Assert.IsEmpty(OpenedWith.Folders(stash));
        Assert.IsEmpty(stash.SqlQueryWhileOpen("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AtStartAMarkedRowWhoseFinishIsRefusedKeepsItsSessionUnreadWhileAnotherIsClassified()
    {
        using var stash = new TestStash();
        var (cat, catCopy) = await Begun(stash);
        var dog = await stash.ImportFixture("dog", "photo.jpg");
        var dogCopy = await OpenedWith.Begin(stash, dog);
        Assert.IsTrue((await stash.Store!.Delete([cat.Id])).Single().Succeeded);
        var held = stash.HeldCopy(cat.Id);
        var recorded = OpenedWith.Record(stash, cat.Id);
        stash.Close();
        OpenedWith.Rewrite(dogCopy, MediaFixtures.DistinctPng(5, 12, 10));
        stash.ReadCalls.Clear();

        StoreOpening opening;
        using (TestStash.Hold(held))
        {
            opening = await stash.Open();
        }

        Assert.AreEqual(new Failure("Discard a held copy", "cat.png", HeldFileRetry.HeldReason), opening.Failures.Single());
        Assert.IsTrue(stash.SqlQueryWhileOpen("SELECT deleted FROM items WHERE id = $id", r => r.GetBoolean(0), ("$id", cat.Id)).Single(), "still marked");
        Assert.AreEqual(recorded, OpenedWith.Record(stash, cat.Id));
        Assert.IsTrue(File.Exists(catCopy));
        CollectionAssert.AreEqual(new[] { "dog.jpg" }, stash.ReadCalls, "the marked row's copy is not read");
        CollectionAssert.AreEqual(new[] { new AdoptedAtStart(dog.Id, 1) }, (await opening.Store!.OpenWithSessions()).Value!.Adoptions.ToList(), "the other classified and adopted");
    }

    [TestMethod]
    public async Task AtStartADeletePutBackAfterARefusedRecycleIsClassifiedAsAnyItem()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        stash.RecycleBin.Refusing = path => path == cat.FilePath;
        Assert.IsFalse((await stash.Store!.Delete([cat.Id])).Single().Succeeded, "the recycle refused after the mark");
        stash.Close();
        OpenedWith.Rewrite(copy, MediaFixtures.DistinctPng(5, 12, 10));
        stash.ReadCalls.Clear();

        var opening = await stash.Open();

        Assert.IsNotNull(opening.Store);
        Assert.AreEqual(cat.Id, (await stash.All()).Single().Id, "put back");
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.ReadCalls);
        Assert.IsNotNull(OpenedWith.Undo(stash, cat.Id), "and adopted as any item's rewrite");
    }

    /// <summary>An open stash holding "cat", imported from still.png, with an Open with… session begun on it.</summary>
    private static async Task<(StashItem Cat, string Copy)> Begun(TestStash stash)
    {
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        return (cat, await OpenedWith.Begin(stash, cat));
    }
}
