using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>Removing a batch (State: the Delete bullet, G29): marked, recycled through the seam, removed; a held file fails alone (G35).</summary>
[TestClass]
public sealed class BatchRemovalTests
{
    [TestMethod]
    public async Task ABatchIsMarkedThenRecycledThenRemoved()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var kept = await stash.Import("kept");
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        var markedWhenRecycled = new List<bool>();
        stash.RecycleBin.BeforeEach = path => markedWhenRecycled.Add(MarkedOnDisk(stash, Path.GetFileName(path)));

        var removed = await store.Remove([a.Id, b.Id]);

        Assert.IsTrue(removed.All(o => o.Succeeded), string.Join("; ", removed.Select(o => o.Failure)));
        CollectionAssert.AreEquivalent(new[] { a.FilePath, b.FilePath }, stash.RecycleBin.Asked);
        CollectionAssert.AreEqual(new[] { true, true }, markedWhenRecycled);
        Assert.AreEqual(kept.Id, (await stash.All()).Single().Id);
        stash.Close();
        CollectionAssert.AreEqual(new[] { kept.Id }, stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
        Assert.IsTrue(File.Exists(kept.FilePath));
    }

    [TestMethod]
    [DataRow(2, DisplayName = "an import batch's Undo")]
    [DataRow(1, DisplayName = "a capture's Undo")]
    public async Task AFileWindowsRemovesForGoodCompletesTheRemovalOfItsItem(int count)
    {
        // G29: a removal for good completes the import batch's or capture's Undo that asked for it.
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var kept = await stash.Import("kept");
        var batch = new List<ImageStash.Store.StashItem> { await stash.Import("too big") };
        if (count == 2)
        {
            batch.Add(await stash.Import("small"));
        }

        stash.RecycleBin.RemovingForGood = path => path == batch[0].FilePath;

        var removed = await store.Remove([.. batch.Select(i => i.Id)]);

        Assert.IsTrue(removed.All(o => o.Succeeded), string.Join("; ", removed.Select(o => o.Failure)));
        Assert.AreEqual(kept.Id, (await stash.All()).Single().Id);
        Assert.IsFalse(batch.Any(i => File.Exists(i.FilePath)));
        Assert.HasCount(count - 1, Directory.Exists(stash.RecycleBin.Folder) ? Directory.GetFiles(stash.RecycleBin.Folder) : []);
        stash.Close();
        CollectionAssert.AreEqual(new[] { kept.Id }, stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task MarkedRowsWithTheirFilesStillInTheRootAreFinishedAtOpen()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        stash.Close();
        // A kill after the mark, before any file was recycled.
        stash.Sql("UPDATE items SET deleted = 1");

        await stash.OpenCleanly();

        CollectionAssert.AreEquivalent(new[] { a.FilePath, b.FilePath }, stash.RecycleBin.Asked);
        Assert.IsEmpty(await stash.All());
        Assert.IsEmpty(stash.RootFiles());
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task MarkedRowsWhoseFilesAreGoneAreRemovedAtOpen()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var a = await stash.Import("a");
        stash.Close();
        // A kill after the recycle, before the rows were removed.
        stash.Sql("UPDATE items SET deleted = 1");
        File.Move(a.FilePath, Path.Combine(stash.Outside("a recycled.png")));

        await stash.OpenCleanly();

        Assert.IsEmpty(stash.RecycleBin.Asked);
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AHeldFileFailsAloneAndItsRowStaysMarkedUntilTheNextStart()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var held = await stash.Import("held");
        var free = await stash.Import("free");

        IReadOnlyList<Outcome> removed;
        using (new FileStream(held.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            removed = await store.Remove([held.Id, free.Id]);
            Assert.AreEqual("held 2.png", (await stash.Import("held")).FileName, "the held file keeps its name taken");
        }

        Assert.AreEqual(new Failure("Move to the Recycle Bin", "held.png", HeldFileRetry.HeldReason), removed[0].Failure);
        Assert.IsTrue(removed[1].Succeeded);
        CollectionAssert.DoesNotContain((await stash.All()).Select(i => i.Id).ToList(), held.Id);
        CollectionAssert.DoesNotContain((await stash.All()).Select(i => i.Id).ToList(), free.Id);
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT deleted FROM items WHERE id = $id", r => r.GetBoolean(0), ("$id", held.Id)).Single());

        await stash.OpenCleanly();

        Assert.IsFalse(File.Exists(held.FilePath));
        Assert.AreEqual(held.FilePath, stash.RecycleBin.Asked.Last());
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items WHERE id = $id", r => r.GetInt64(0), ("$id", held.Id)));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task TheListingIsAnsweredWhileABatchsRecycleWaitsOnAHeldFileAndTheRowIsThenRemoved()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var dog = await stash.Import("dog");
        var cat = await stash.Import("cat");
        Assert.IsTrue((await store.RecordUses([dog, cat], stash.Clock.Now)).Succeeded);

        var (recent, removed) = await ReadsWhileAChangeWaits.Asked(TestStash.Hold(cat.FilePath), () => store.Remove([cat.Id]), store.Recent);

        Assert.AreEqual(dog.Id, recent.Value!.Single().Id, "the batch's row is marked, returned by no query (G1)");
        Assert.IsTrue(removed.Single().Succeeded, removed.Single().Failure?.ToString());
        Assert.IsFalse(File.Exists(cat.FilePath));
        Assert.IsGreaterThan(1, stash.RecycleBin.Asked.Count, "asked again while held (G35)");
        stash.Close();
        CollectionAssert.AreEqual(new[] { dog.Id }, stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task ARemovalStillRefusedAtOpenIsUndoneAndTheItemIsBack()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.Import("refused");
        stash.Close();
        stash.Sql("UPDATE items SET deleted = 1");
        stash.RecycleBin.Refusing = _ => true;

        var opening = await stash.Open();

        Assert.IsNotNull(opening.Store);
        Assert.AreEqual("refused.png", opening.Failures.Single().Subject);
        TestStash.AssertSameItem(item, (await stash.All()).Single());
        Assert.IsTrue(File.Exists(item.FilePath));
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT deleted = 0 AND pending_steps IS NULL FROM items", r => r.GetBoolean(0)).Single());

        stash.RecycleBin.Refusing = _ => false;
        stash.RecycleBin.Asked.Clear();
        await stash.OpenCleanly();

        Assert.IsEmpty(stash.RecycleBin.Asked);
        Assert.AreEqual(item.Id, (await stash.All()).Single().Id);
    }

    [TestMethod]
    public async Task ARemovalFinishesAnUnfinishedRenameFirstAndLeavesThatItemNamedWhileItCannot()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat");
        var other = await stash.Import("other");
        IReadOnlyList<Outcome> refused;
        using (TestStash.Hold(cat.FilePath))
        {
            await store.Rename(cat, "dog");
            refused = await store.Remove([cat.Id, other.Id]);
        }

        Assert.AreEqual(new Failure(BatchRemoval.Action, "dog", $"its earlier rename cannot finish yet: {HeldFileRetry.HeldReason}"), refused[0].Failure);
        Assert.IsTrue(refused[1].Succeeded, refused[1].Failure?.ToString());
        Assert.AreEqual(cat.Id, (await stash.All()).Single().Id, "left in the stash, unmarked");

        var removed = await store.Remove([cat.Id]);

        Assert.IsTrue(removed.Single().Succeeded, removed.Single().Failure?.ToString());
        CollectionAssert.AreEqual(new[] { other.FilePath, stash.Folder.InRoot("dog.png") }, stash.RecycleBin.Asked, "the rename finished first");
        Assert.IsEmpty(await stash.All());
        Assert.IsEmpty(stash.RootFiles());
    }

    [TestMethod]
    public async Task AnEditedItemHalfRecycledAtOpenStaysRemovedUntilTheNextStartFinishesIt()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.Import("edited");
        stash.Close();
        // An edited item, its original under originals\ and its finished picture in the root, marked as a kill left it.
        var original = stash.Folder.InOriginals("edited.png");
        File.Copy(item.FilePath, original);
        stash.Sql("UPDATE items SET original_in_originals = 1, deleted = 1");
        stash.RecycleBin.Refusing = path => path == original;

        var opening = await stash.Open();

        Assert.IsNotNull(opening.Store);
        Assert.AreEqual("edited.png", opening.Failures.Single().Subject);
        Assert.IsFalse(File.Exists(item.FilePath), "the finished picture went to the Recycle Bin");
        Assert.IsEmpty(await stash.All(), "an item missing its finished picture is not listed");
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT deleted FROM items", r => r.GetBoolean(0)).Single());

        stash.RecycleBin.Refusing = _ => false;
        await stash.OpenCleanly();

        Assert.IsFalse(File.Exists(original));
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AnItemWaitingOnItsImportStepIsRemovedAndDoesNotComeBack()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("pending");
        // The import's rename done and its step not yet cleared, as a failed final transaction leaves it.
        stash.SqlWhileOpen("UPDATE items SET pending_steps = $steps", ("$steps", PendingStep.Encode([PendingStep.Place("gone.png", item.FileName)])));

        var removed = await store.Remove([item.Id]);

        Assert.IsTrue(removed.Single().Succeeded, removed.Single().Failure?.ToString());
        CollectionAssert.AreEqual(new[] { item.FilePath }, stash.RecycleBin.Asked);
        await stash.Reopen();
        Assert.IsEmpty(await stash.All());
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task ADeletedItemIsLeftToItsDeleteByTheBatchsUndoSoTheDeletesUndoStillBringsItBackWhole()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("both", TestStash.Bytes(80));
        var deleted = (await store.Delete([item.Id])).Single().Value!;

        var removed = await store.Remove([item.Id]);

        Assert.IsTrue(removed.Single().Succeeded, removed.Single().Failure?.ToString());
        CollectionAssert.AreEqual(new[] { item.FilePath }, stash.RecycleBin.Asked, "the file is recycled once, by the Delete");
        Assert.IsTrue(File.Exists(stash.HeldCopy(item.Id)), "the Delete's copy is kept for its Undo (G18)");

        var undone = (await store.UndoDelete([deleted])).Single();

        Assert.IsTrue(undone.Succeeded, undone.Failure?.ToString());
        TestStash.AssertSameItem(item, (await stash.All()).Single());
        CollectionAssert.AreEqual(TestStash.Bytes(80), File.ReadAllBytes(item.FilePath));
        CollectionAssert.AreEqual(deleted.RecycledIdentities.ToList(), stash.RecycleBin.Removed);
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ADeletedItemTheBatchsUndoLeftIsEndedOnceByTheDeletesFinish()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("both");
        await store.Delete([item.Id]);
        await store.Remove([item.Id]);

        var finished = (await store.FinishDelete([item.Id])).Single();

        Assert.IsTrue(finished.Succeeded, finished.Failure?.ToString());
        Assert.IsEmpty(stash.WorkFiles());
        Assert.HasCount(1, Directory.GetFiles(stash.RecycleBin.Folder), "the Recycle Bin keeps its one entry");
        CollectionAssert.AreEqual(new[] { item.FilePath }, stash.RecycleBin.Asked, "no file touched twice");
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AnUndeletedItemTheBatchsUndoRemovesGoesToTheBinAgain()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("both");
        var deleted = (await store.Delete([item.Id])).Single().Value!;
        await store.UndoDelete([deleted]);

        var removed = await store.Remove([item.Id]);

        Assert.IsTrue(removed.Single().Succeeded, removed.Single().Failure?.ToString());
        CollectionAssert.AreEqual(new[] { item.FilePath, item.FilePath }, stash.RecycleBin.Asked);
        Assert.HasCount(1, Directory.GetFiles(stash.RecycleBin.Folder), "Undo removed the first entry");
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task ADeletedItemWhoseFileTheBinStillRefusesKeepsItsCopyThroughTheBatchsUndoAndComesBackAtOpen()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("both");
        stash.RecycleBin.Refusing = path => path == item.FilePath;
        await store.Delete([item.Id]);

        var removed = await store.Remove([item.Id]);

        Assert.AreEqual(new Failure("Move to the Recycle Bin", "both.png", "the test Recycle Bin refuses it"), removed.Single().Failure, "the batch's Undo tries its file again and names the refusal");
        CollectionAssert.AreEqual(new[] { item.FilePath, item.FilePath }, stash.RecycleBin.Asked);
        Assert.IsTrue(File.Exists(item.FilePath), "never a stash file with no row");
        Assert.IsTrue(File.Exists(stash.HeldCopy(item.Id)));
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT deleted = 1 AND held_copies IS NOT NULL FROM items", r => r.GetBoolean(0)).Single());

        var opening = await stash.Open();

        Assert.AreEqual("both.png", opening.Failures.Single().Subject);
        TestStash.AssertSameItem(item, (await stash.All()).Single());
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ADeletedItemWhoseRecycleWasRefusedGoesToTheBinWithTheBatchsUndoAndStaysGoneAtTheNextStart()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("both", TestStash.Bytes(81));
        stash.RecycleBin.Refusing = path => path == item.FilePath;
        await store.Delete([item.Id]);
        stash.RecycleBin.Refusing = _ => false;

        var removed = await store.Remove([item.Id]);

        Assert.IsTrue(removed.Single().Succeeded, removed.Single().Failure?.ToString());
        Assert.IsFalse(File.Exists(item.FilePath), "its file went to the Recycle Bin");
        Assert.IsTrue(File.Exists(stash.HeldCopy(item.Id)), "its copy stays with the Delete's mark");

        stash.RecycleBin.Refusing = _ => true;
        var opening = await stash.Reopen();

        Assert.IsEmpty(opening.Failures, "nothing of it is in the stash to refuse");
        Assert.IsEmpty(await stash.All(), "it does not come back");
        Assert.IsEmpty(stash.WorkFiles());
        CollectionAssert.AreEqual(TestStash.Bytes(81), File.ReadAllBytes(Directory.GetFiles(stash.RecycleBin.Folder).Single()));
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task ADeletedItemWhoseRecycleWasRefusedStillComesBackWithTheDeletesUndoAfterTheBatchsUndo()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("both", TestStash.Bytes(82));
        stash.RecycleBin.Refusing = path => path == item.FilePath;
        var deleted = (await store.Delete([item.Id])).Single().Value!;
        stash.RecycleBin.Refusing = _ => false;
        await store.Remove([item.Id]);

        var restored = (await store.UndoDelete([deleted])).Single();

        Assert.IsTrue(restored.Succeeded, restored.Failure?.ToString());
        TestStash.AssertSameItem(item, (await stash.All()).Single());
        CollectionAssert.AreEqual(TestStash.Bytes(82), File.ReadAllBytes(item.FilePath));
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(Directory.GetFiles(stash.RecycleBin.Folder), "the entry the batch's Undo made goes with the Delete's Undo, as the Delete's own would");
    }

    [TestMethod]
    public async Task ADeletedItemWhoseFileTheBatchsUndoRemovedForGoodComesBackWithTheDeletesUndoAskingNoRemoval()
    {
        // The batch's Undo keeps for the Delete's Undo only the entries the bin gave back; a removal for good gave none (G18, G29).
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("both", TestStash.Bytes(83));
        stash.RecycleBin.Refusing = path => path == item.FilePath;
        var deleted = (await store.Delete([item.Id])).Single().Value!;
        stash.RecycleBin.Refusing = _ => false;
        stash.RecycleBin.RemovingForGood = _ => true;
        Assert.IsTrue((await store.Remove([item.Id])).Single().Succeeded);
        Assert.IsFalse(File.Exists(item.FilePath));

        var restored = (await store.UndoDelete([deleted])).Single();

        Assert.IsTrue(restored.Succeeded && restored.Value!.Named.Count == 0, restored.Failure?.ToString() ?? string.Join("; ", restored.Value!.Named));
        CollectionAssert.AreEqual(TestStash.Bytes(83), File.ReadAllBytes(item.FilePath));
        Assert.IsEmpty(stash.RecycleBin.Removed);
        Assert.IsEmpty(stash.WorkFiles());
        TestStash.AssertSameItem(item, (await stash.All()).Single());
    }

    [TestMethod]
    public async Task ADeletedEditedItemKeepsBothItsCopiesThroughTheBatchsUndoForItsUndo()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.Import("edited", TestStash.Bytes(90));
        stash.Close();
        stash.MakeEdited(item, TestStash.Bytes(91));
        var store = await stash.OpenCleanly();
        var deleted = (await store.Delete([item.Id])).Single().Value!;
        var copies = new[] { stash.HeldCopy(item.Id), stash.HeldCopy(item.Id, "original_file") };

        var removed = await store.Remove([item.Id]);

        Assert.IsTrue(removed.Single().Succeeded, removed.Single().Failure?.ToString());
        Assert.IsTrue(copies.All(File.Exists), "both copies stay for the Delete's Undo");

        var restored = (await store.UndoDelete([deleted])).Single();

        Assert.IsTrue(restored.Succeeded, restored.Failure?.ToString());
        CollectionAssert.AreEqual(TestStash.Bytes(91), File.ReadAllBytes(item.FilePath));
        CollectionAssert.AreEqual(TestStash.Bytes(90), File.ReadAllBytes(stash.Folder.InOriginals("edited.png")));
        Assert.IsEmpty(stash.WorkFiles());
    }

    // Read through a second connection while the store's own holds no transaction.
    private static bool MarkedOnDisk(TestStash stash, string fileName)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={stash.Folder.StoreFile};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT deleted FROM items WHERE finished_file = $file";
        command.Parameters.AddWithValue("$file", fileName);
        return Convert.ToBoolean(command.ExecuteScalar());
    }
}
