using System.Runtime.Versioning;
using System.Security.Cryptography;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// Store's side of an import: the commit and its rename (G28, R9, R2), a live failure (State), duplicates (G12), the
/// extension (State: File names), identity, the tags a new item gets (G11), and the summon's listing answered while the
/// place or the comparison waits on a held file (G1).
/// </summary>
[TestClass]
public sealed class ImportCommitTests
{
    [TestMethod]
    public async Task ACommitReturnsWithTheFileInPlaceAndTheStepCleared()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(10);
        var staged = await stash.Stage("cat.png", bytes);

        var committed = await store.CommitImport(TestStash.Candidate(staged, "cat", bytes));

        Assert.IsTrue(committed.Succeeded, committed.Failure?.ToString());
        Assert.IsFalse(committed.Value!.AlreadyInStash);
        Assert.IsTrue(committed.Value.Listed);
        var item = committed.Value.Item;
        Assert.AreEqual("cat", item.Name);
        Assert.AreEqual("cat.png", item.FileName);
        Assert.AreEqual(stash.Folder.InRoot("cat.png"), item.FilePath);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(item.FilePath));
        Assert.IsFalse(File.Exists(staged));
        Assert.AreEqual(stash.Clock.Now, item.AddedAt);
        TestStash.AssertSameItem(item, (await stash.All()).Single());
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT pending_steps IS NULL FROM items", r => r.GetBoolean(0)).Single());
    }

    [TestMethod]
    public async Task TheAddedItemCarriesTheStoresCommitsAfterItsStepIsClearedSoAHeldStoreStillHandsItOver()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(12);
        var staged = await stash.Stage("cat.png", bytes);

        var item = (await store.CommitImport(TestStash.Candidate(staged, "cat", bytes))).Value!.Item;

        Assert.AreEqual((await stash.All()).Single().CommitsWhenRead, item.CommitsWhenRead);
        using (stash.HoldStore())
        {
            Assert.AreSame(item, (await store.Confirm([item], "Copy")).Items.Single().Value, "nothing has committed since it was added");
        }
    }

    [TestMethod]
    public async Task AStagedFileHeldThroughTheRenameLeavesTheRowPendingUntilTheNextStartRemovesIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(11);
        var staged = await stash.Stage("held.png", bytes);

        Outcome<ImportResult> committed;
        using (new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            committed = await store.CommitImport(TestStash.Candidate(staged, "held", bytes));
        }

        Assert.IsFalse(committed.Succeeded);
        Assert.AreEqual(new Failure(ImportCommit.Action, "held.png", HeldFileRetry.HeldReason), committed.Failure);
        Assert.AreEqual("held", committed.Value!.Item.Name, "a failure after the commit carries the item as committed");
        Assert.IsFalse(committed.Value.Listed, "its file never reached the root, so no query returns it");
        Assert.IsEmpty(await stash.All());
        Assert.IsTrue(File.Exists(staged));
        Assert.IsFalse(File.Exists(stash.Folder.InRoot("held.png")));
        var other = await stash.Import("other");
        Assert.AreEqual(other.Id, (await stash.All()).Single().Id);

        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT pending_steps IS NOT NULL FROM items WHERE name = 'held'", r => r.GetBoolean(0)).Single());
        await stash.OpenCleanly();

        Assert.IsFalse(File.Exists(staged));
        Assert.AreEqual(other.Id, (await stash.All()).Single().Id);
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items WHERE name = 'held'", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AnImportWhoseFileIsInPlaceButWhoseStepIsNotClearedIsInTheStash()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(20);
        var item = await stash.Import("placed", bytes);
        // The rename done and the final transaction failed, as a held journal leaves it.
        stash.SqlWhileOpen("UPDATE items SET pending_steps = $steps", ("$steps", PendingStep.Encode([PendingStep.Place("gone.png", item.FileName)])));

        var all = await stash.All();
        var again = await store.CommitImport(TestStash.Candidate(await stash.Stage("again.png", bytes), "again", bytes));
        await store.RecordUses([item], stash.Clock.Now);

        Assert.AreEqual(item.Id, all.Single().Id, "the grid shows the picture on disk");
        Assert.IsTrue(again.Value!.AlreadyInStash);
        Assert.AreEqual(item.Id, again.Value.Item.Id);
        Assert.AreEqual(item.Id, (await store.Recent()).Value!.Single().Id);
    }

    [TestMethod]
    public async Task ANewItemGetsItsTagsInTheOneSpellingOfEach()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var other = await stash.Import("other");
        stash.Tag(other.Id, "Dog");
        var bytes = TestStash.Bytes(50);

        var committed = await store.CommitImport(TestStash.Candidate(await stash.Stage("new.png", bytes), "new", bytes) with { Tags = ["dog", "#reaction"] });

        Assert.IsTrue(committed.Succeeded, committed.Failure?.ToString());
        var item = committed.Value!.Item;
        CollectionAssert.AreEqual(new[] { "Dog", "reaction" }, item.Tags.ToList());
        TestStash.AssertSameItem(item, (await stash.All()).Single(i => i.Id == item.Id));
    }

    [TestMethod]
    public async Task AnItemAlreadyInTheStashGetsNoTag()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(51);
        var existing = await stash.Import("existing", bytes);
        stash.Tag(existing.Id, "kept");

        var duplicate = await store.CommitImport(TestStash.Candidate(await stash.Stage("again.png", bytes), "again", bytes) with { Tags = ["dog"] });

        Assert.IsTrue(duplicate.Value!.AlreadyInStash);
        CollectionAssert.AreEqual(new[] { (existing.Id, "kept") }, stash.TagRows());
    }

    [TestMethod]
    public async Task TheTagsAreCommittedWithTheRowAndGoWithItWhenTheNextStartRemovesIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(52);
        var staged = await stash.Stage("held.png", bytes);

        Outcome<ImportResult> committed;
        using (new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            committed = await store.CommitImport(TestStash.Candidate(staged, "held", bytes) with { Tags = ["dog", "cat"] });
        }

        var id = committed.Value!.Item.Id;
        Assert.IsFalse(committed.Value.Listed);
        CollectionAssert.AreEqual(new[] { "dog", "cat" }, committed.Value.Item.Tags.ToList(), "the item as committed carries its tags");
        Assert.IsEmpty((await store.TagCounts()).Value!, "no listed item carries them");
        stash.Close();
        CollectionAssert.AreEqual(new[] { "cat", "dog" }, stash.SqlQuery("SELECT tag FROM tags WHERE item_id = $id ORDER BY tag", r => r.GetString(0), ("$id", id)));
        await stash.OpenCleanly();
        stash.Close();
        Assert.AreEqual(0L, stash.SqlQuery("SELECT COUNT(*) FROM tags WHERE item_id = $id", r => r.GetInt64(0), ("$id", id)).Single());
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AStagedDuplicateThatCannotBeDiscardedIsNamed()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(21);
        var existing = await stash.Import("existing", bytes);
        var staged = await stash.Stage("copy.png", bytes);

        Outcome<ImportResult> committed;
        using (new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            committed = await store.CommitImport(TestStash.Candidate(staged, "copy", bytes));
        }

        Assert.AreEqual(new Failure("Discard a copy", "copy.png", HeldFileRetry.HeldReason), committed.Failure);
        Assert.IsTrue(committed.Value!.AlreadyInStash);
        Assert.AreEqual(existing.Id, committed.Value.Item.Id);
        Assert.AreEqual(existing.Id, (await stash.All()).Single().Id);
    }

    [TestMethod]
    public async Task EqualBytesAreAlreadyInTheStashAndAnEqualHashAloneIsNot()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(12);
        var existing = await stash.Import("existing", bytes);
        var same = await stash.Stage("same.png", bytes);
        var different = await stash.Stage("different.png", TestStash.Bytes(13));

        var duplicate = await store.CommitImport(TestStash.Candidate(same, "same", bytes));
        // The hash of the existing original handed in with different bytes.
        var notDuplicate = await store.CommitImport(new ImportCandidate(different, "different", TestStash.StillPng, SHA256.HashData(bytes)));

        Assert.IsTrue(duplicate.Succeeded, duplicate.Failure?.ToString());
        Assert.IsTrue(duplicate.Value!.AlreadyInStash);
        TestStash.AssertSameItem(existing, duplicate.Value.Item);
        Assert.IsFalse(File.Exists(same), "the store discards a duplicate's staged file");
        Assert.IsTrue(notDuplicate.Succeeded, notDuplicate.Failure?.ToString());
        Assert.IsFalse(notDuplicate.Value!.AlreadyInStash);
        Assert.HasCount(2, await stash.All());
    }

    [TestMethod]
    public async Task TwoEqualFilesStagedTogetherAreAddedOnce()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(17);
        var first = await stash.Stage("first.gif", bytes);
        var second = await stash.Stage("second.gif", bytes);

        var added = await store.CommitImport(TestStash.Candidate(first, "first", bytes));
        var again = await store.CommitImport(TestStash.Candidate(second, "second", bytes));

        Assert.IsFalse(added.Value!.AlreadyInStash);
        Assert.IsTrue(again.Value!.AlreadyInStash);
        Assert.AreEqual(added.Value.Item.Id, again.Value.Item.Id);
        Assert.AreEqual(added.Value.Item.Id, (await stash.All()).Single().Id);
    }

    [TestMethod]
    public async Task APictureSavedEvenIfInTheStashIsASecondItemOfTheSameNameWhileAFileIsStillAskedTheQuestion()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(18);
        const string name = "Screenshot 2026-10-16 12.00.00";

        var file = await store.CommitImport(TestStash.Candidate(await stash.Stage("first.png", bytes), name, bytes));
        var picture = await store.CommitImport(TestStash.Candidate(await stash.Stage("second.png", bytes), name, bytes) with { SavedEvenIfInStash = true });
        var fileAgain = await store.CommitImport(TestStash.Candidate(await stash.Stage("third.png", bytes), "third", bytes));

        Assert.IsTrue(picture.Succeeded, picture.Failure?.ToString());
        Assert.IsFalse(picture.Value!.AlreadyInStash, "a pasted picture equal to an item's original is saved");
        Assert.AreNotEqual(file.Value!.Item.Id, picture.Value.Item.Id);
        Assert.AreEqual(name, picture.Value.Item.Name, "the two share the name");
        Assert.AreEqual(name + " 2.png", picture.Value.Item.FileName, "only the file gets \" 2\" (State: File names)");
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(picture.Value.Item.FilePath));
        Assert.IsTrue(fileAgain.Value!.AlreadyInStash, "a file is still asked whether it is already in the stash (G12)");
        Assert.AreEqual(file.Value.Item.Id, fileAgain.Value.Item.Id);
        Assert.HasCount(2, await stash.All());
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task BytesAreComparedPastTheFirstBuffer()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(14, 200_000);
        var existing = await stash.Import("large", bytes);
        var tail = (byte[])bytes.Clone();
        tail[150_000] ^= 0xFF;
        var differentTail = await stash.Stage("tail.png", tail);
        var equal = await stash.Stage("equal.png", bytes);

        var notDuplicate = await store.CommitImport(new ImportCandidate(differentTail, "tail", TestStash.StillPng, SHA256.HashData(bytes)));
        var duplicate = await store.CommitImport(TestStash.Candidate(equal, "equal", bytes));

        Assert.IsFalse(notDuplicate.Value!.AlreadyInStash);
        Assert.IsTrue(duplicate.Value!.AlreadyInStash);
        TestStash.AssertSameItem(existing, duplicate.Value.Item);
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task AComparisonLockedBrieflyIsRetriedAndOneLockedPastTheRetryIsNamed()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(4) };
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(18, 100_000);
        var existing = await stash.Import("locked", bytes);

        var briefly = await stash.Stage("briefly.png", bytes);
        var lockBriefly = new FileStream(briefly, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        lockBriefly.Lock(70_000, 1_000);
        var committing = store.CommitImport(TestStash.Candidate(briefly, "briefly", bytes));
        await Task.Delay(800);
        lockBriefly.Unlock(70_000, 1_000);
        lockBriefly.Dispose();
        var found = await committing;

        stash.Retry = TestStash.ShortRetry;
        await stash.Reopen();
        var past = await stash.Stage("past.png", bytes);
        Outcome<ImportResult> refused;
        using (var lockPast = new FileStream(past, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            lockPast.Lock(70_000, 1_000);
            refused = await stash.Store!.CommitImport(TestStash.Candidate(past, "past", bytes));
            lockPast.Unlock(70_000, 1_000);
        }

        Assert.IsTrue(found.Succeeded, found.Failure?.ToString());
        Assert.IsTrue(found.Value!.AlreadyInStash);
        Assert.AreEqual(existing.Id, found.Value.Item.Id);
        Assert.AreEqual(new Failure("Look for the same picture", "past.png", HeldFileRetry.HeldReason), refused.Failure);
        Assert.IsFalse(File.Exists(past), "a failure before the commit leaves nothing staged");
        Assert.AreEqual(existing.Id, (await stash.All()).Single().Id);
    }

    [TestMethod]
    [DataRow(ImageFormat.Png, ".jpg", "cat.png")]
    [DataRow(ImageFormat.Jpeg, ".jpeg", "cat.jpeg")]
    [DataRow(ImageFormat.Jpeg, ".JPG", "cat.jpg")]
    [DataRow(ImageFormat.Gif, ".webp", "cat.gif")]
    [DataRow(ImageFormat.WebP, ".webp", "cat.webp")]
    public async Task TheFileTakesTheExtensionOfTheFormatMediaRead(ImageFormat format, string extension, string fileName)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(19);
        var staged = await stash.Stage("cat" + extension, bytes);

        var committed = await store.CommitImport(TestStash.Candidate(staged, "cat", bytes, TestStash.StillPng with { Format = format }));

        Assert.AreEqual(fileName, committed.Value!.Item.FileName);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(committed.Value.Item.FilePath));
    }

    [TestMethod]
    public async Task ARemovedItemIsNoLongerInTheStash()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(15);
        var removed = await stash.Import("removed", bytes);
        await store.Remove([removed.Id]);
        var again = await stash.Stage("removed.png", bytes);

        var committed = await store.CommitImport(TestStash.Candidate(again, "removed", bytes));

        Assert.IsTrue(committed.Succeeded, committed.Failure?.ToString());
        Assert.IsFalse(committed.Value!.AlreadyInStash);
    }

    [TestMethod]
    public async Task AnIdentityIsNeverHandedOutAgain()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        await stash.Import("a");
        var b = await stash.Import("b");
        await store.Remove([b.Id]);

        var c = await stash.Import("c");

        Assert.IsGreaterThan(b.Id, c.Id);
    }

    [TestMethod]
    public async Task ACommitRefusesAFileOutsideTheWorkFolder()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(16);
        var source = stash.Source("outside.png", bytes);

        var committed = await store.CommitImport(TestStash.Candidate(source, "outside", bytes));

        Assert.IsFalse(committed.Succeeded);
        Assert.IsNull(committed.Value);
        Assert.IsTrue(File.Exists(source));
        Assert.IsEmpty(await stash.All());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task TheSummonsListingIsAnsweredWhileAnImportWaitsToPlaceItsHeldFileAndTheItemIsThenAdded()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var used = await stash.Import("used");
        Assert.IsTrue((await store.RecordUses([used], stash.Clock.Now)).Succeeded);
        var bytes = TestStash.Bytes(19);
        var staged = await stash.Stage("held.png", bytes);

        // A scanner reading the fresh file (E3): the move into the root is refused while it reads.
        var (recent, committed) = await ReadsWhileAChangeWaits.Asked(TestStash.Hold(staged), () => store.CommitImport(TestStash.Candidate(staged, "held", bytes)), store.Recent);

        Assert.AreEqual(used.Id, recent.Value!.Single().Id, "the row being added is listed by no query until its file is in place");
        Assert.IsTrue(committed.Succeeded, committed.Failure?.ToString());
        Assert.AreEqual(stash.Folder.InRoot("held.png"), committed.Value!.Item.FilePath);
        CollectionAssert.AreEqual(new[] { committed.Value.Item.Id, used.Id }, (await stash.All()).Select(item => item.Id).ToList());
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task TheSummonsListingIsAnsweredWhileAnImportComparesAHeldDuplicateAndTheDuplicateIsThenFound()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(20);
        var existing = await stash.Import("existing", bytes);
        Assert.IsTrue((await store.RecordUses([existing], stash.Clock.Now)).Succeeded);
        var staged = await stash.Stage("again.png", bytes);

        // Shared with nobody, so the byte comparison's read is refused, not only the move a fresh file would meet (G12).
        var (recent, committed) = await ReadsWhileAChangeWaits.Asked(
            new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.None), () => store.CommitImport(TestStash.Candidate(staged, "again", bytes)), store.Recent);

        Assert.AreEqual(existing.Id, recent.Value!.Single().Id);
        Assert.IsTrue(committed.Succeeded, committed.Failure?.ToString());
        Assert.IsTrue(committed.Value!.AlreadyInStash);
        Assert.AreEqual(existing.Id, committed.Value.Item.Id);
        Assert.AreEqual(existing.Id, (await stash.All()).Single().Id, "no row added");
        Assert.IsEmpty(stash.WorkFiles(), "the staged copy discarded");
    }
}
