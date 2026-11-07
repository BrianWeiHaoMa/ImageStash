using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// What a kill or a live failure leaves is finished or undone at the next open, before anything reads (G28, State:
/// Every change, Files); a step still refused is named and every other item opens (G35). The on-disk states are built
/// by hand: rows through SQL on the closed store, files placed directly.
/// </summary>
[TestClass]
public sealed class PendingStepRecoveryTests
{
    [TestMethod]
    public async Task AnImportWhoseFileIsStillInTheWorkFolderIsRemovedWithIt()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var kept = await stash.Import("kept");
        var staged = await stash.Stage("unfinished.png", TestStash.Bytes(20));
        stash.Close();
        InsertPendingImport(stash, "unfinished", Path.GetFileName(staged), "unfinished.png");

        await stash.OpenCleanly();

        Assert.IsFalse(File.Exists(staged));
        Assert.AreEqual(kept.Id, (await stash.All()).Single().Id);
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items WHERE name = 'unfinished'", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AnUnfinishedImportsTagsAreRemovedWithItsRow()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var kept = await stash.Import("kept");
        await stash.Store!.AddTag([kept], "dog");
        var staged = await stash.Stage("unfinished.png", TestStash.Bytes(21));
        stash.Close();
        InsertPendingImport(stash, "unfinished", Path.GetFileName(staged), "unfinished.png");
        stash.Sql("INSERT INTO tags (item_id, tag) SELECT id, 'dog' FROM items WHERE name = 'unfinished'");

        await stash.OpenCleanly();

        CollectionAssert.AreEqual(new[] { (kept.Id, "dog") }, stash.TagRows());
        CollectionAssert.AreEqual(new[] { new TagCount("dog", 1) }, (await stash.Store!.TagCounts()).Value!.ToList());
    }

    [TestMethod]
    public async Task AnImportWhoseFileReachedTheRootHasItsStepCleared()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.Import("renamed");
        stash.Close();
        // A kill after the rename, before the step was cleared.
        stash.Sql("UPDATE items SET pending_steps = $steps", ("$steps", PendingStep.Encode([PendingStep.Place("gone.png", "renamed.png")])));

        await stash.OpenCleanly();

        TestStash.AssertSameItem(item, (await stash.All()).Single());
        Assert.IsTrue(File.Exists(item.FilePath));
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT pending_steps IS NULL FROM items", r => r.GetBoolean(0)).Single());
    }

    [TestMethod]
    public async Task AWorkFileNoRowRefersToIsDeletedAtOpen()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        stash.Close();
        File.WriteAllBytes(stash.Folder.InWork("leftover.gif"), TestStash.Bytes(21));
        Directory.CreateDirectory(stash.Folder.InWork("leftover folder"));

        await stash.OpenCleanly();

        Assert.IsEmpty(Directory.GetFileSystemEntries(stash.Folder.Work));
    }

    [TestMethod]
    public async Task AnImportWhoseWorkFileIsHeldStaysForTheNextStartAndEveryOtherItemOpens()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var kept = await stash.Import("kept");
        var staged = await stash.Stage("held.png", TestStash.Bytes(22));
        stash.Close();
        InsertPendingImport(stash, "held", Path.GetFileName(staged), "held.png");

        StoreOpening opening;
        using (new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            opening = await stash.Open();
        }

        Assert.IsNotNull(opening.Store);
        Assert.AreEqual(new Failure("Undo an unfinished import", "held.png", HeldFileRetry.HeldReason), opening.Failures.Single());
        Assert.AreEqual(kept.Id, (await stash.All()).Single().Id);
        Assert.IsTrue(File.Exists(staged));
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT pending_steps IS NOT NULL FROM items WHERE name = 'held'", r => r.GetBoolean(0)).Single());

        await stash.OpenCleanly();

        Assert.IsFalse(File.Exists(staged));
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items WHERE name = 'held'", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task HeldCopiesMadeBeforeADeletesMarkAreDeletedAtOpenAndTheItemStays()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.Import("kept");
        stash.Close();
        // A kill after the held copy was made, before the mark.
        File.Copy(item.FilePath, stash.Folder.InWork(Guid.NewGuid().ToString("N") + ".png"));

        await stash.OpenCleanly();

        TestStash.AssertSameItem(item, (await stash.All()).Single());
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(stash.RecycleBin.Asked);
    }

    [TestMethod]
    public async Task ADeletedRowWhoseFilesAreStillInPlaceIsFinishedAtOpen()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        // Marked with its copies recorded and its files still in the root, as a kill before the recycle leaves it.
        stash.RecycleBin.Refusing = _ => true;
        await store.Delete([a.Id, b.Id]);
        stash.Close();
        stash.RecycleBin.Refusing = _ => false;
        stash.RecycleBin.Asked.Clear();

        await stash.OpenCleanly();

        CollectionAssert.AreEquivalent(new[] { a.FilePath, b.FilePath }, stash.RecycleBin.Asked);
        Assert.IsEmpty(stash.RootFiles());
        Assert.IsEmpty(stash.WorkFiles());
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task ADeletedRowWhoseFileWindowsRemovesForGoodAtOpenIsEndedWithNothingNamed()
    {
        // State, the Delete bullet: a marked row's files are recycled at start and the row removed; a removal for good
        // completes that recycle (G29).
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("too big");
        // Marked with its copy recorded and its file still in the root, as a kill before the recycle leaves it.
        stash.RecycleBin.Refusing = _ => true;
        await store.Delete([item.Id]);
        stash.Close();
        stash.RecycleBin.Refusing = _ => false;
        stash.RecycleBin.RemovingForGood = _ => true;

        var opening = await stash.Open();

        Assert.IsEmpty(opening.Failures);
        Assert.IsEmpty(await stash.All());
        Assert.IsEmpty(stash.RootFiles());
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(stash.RecycleBin.Removed);
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task ADeletedRowWhoseFilesWentToTheBinIsRemovedWithItsCopiesAtOpen()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("gone");
        // A kill after the recycle, while the notice showed.
        await store.Delete([item.Id]);
        stash.Close();

        await stash.OpenCleanly();

        CollectionAssert.AreEqual(new[] { item.FilePath }, stash.RecycleBin.Asked);
        Assert.IsEmpty(stash.WorkFiles());
        Assert.HasCount(1, Directory.GetFiles(stash.RecycleBin.Folder));
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AnUndoInterruptedAfterItsRenamesIsFinishedAtOpenAsTheDelete()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("undone");
        await store.Delete([item.Id]);
        stash.Close();
        // An Undo killed after renaming the copy back, before the mark was cleared.
        File.Move(stash.HeldCopy(item.Id), item.FilePath);

        await stash.OpenCleanly();

        CollectionAssert.AreEqual(new[] { item.FilePath, item.FilePath }, stash.RecycleBin.Asked);
        Assert.IsEmpty(stash.RootFiles());
        Assert.IsEmpty(stash.WorkFiles());
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AnEditedItemHalfPutBackIsFinishedAtOpenWithNothingLeftInTheWorkFolder()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.Import("edited", TestStash.Bytes(60));
        stash.Close();
        stash.MakeEdited(item, TestStash.Bytes(61));
        var store = await stash.OpenCleanly();
        await store.Delete([item.Id]);
        stash.Close();
        // An Undo killed between its two renames: the finished picture back in the root, the original still a copy.
        File.Move(stash.HeldCopy(item.Id), item.FilePath);
        stash.RecycleBin.Asked.Clear();

        await stash.OpenCleanly();

        CollectionAssert.AreEqual(new[] { item.FilePath }, stash.RecycleBin.Asked);
        Assert.IsEmpty(stash.RootFiles());
        Assert.IsFalse(File.Exists(stash.Folder.InOriginals("edited.png")));
        Assert.IsEmpty(stash.WorkFiles());
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AnInterruptedUndoWhoseRecycleIsRefusedAtOpenComesBackWholeFromTheCopiesTheStartMade()
    {
        using var stash = new TestStash();
        var (item, original) = await InterruptedUndoOfAnEditedItem(stash);
        stash.RecycleBin.Refusing = path => path == original;

        var opening = await stash.Open();

        Assert.IsNotNull(opening.Store);
        Assert.AreEqual(new Failure("Move to the Recycle Bin", "edited.png", "the test Recycle Bin refuses it"), opening.Failures.Single());
        CollectionAssert.AreEqual(new[] { item.FilePath, original }, stash.RecycleBin.Asked, "the finished picture was recycled and came back");
        Assert.IsFalse(File.Exists(stash.RecycleBin.Removed.Single()), "the entry the start made is removed with the undo");
        await AssertBackWhole(stash, item, original);
    }

    [TestMethod]
    public async Task AnInterruptedUndoAtOpenIsFinishedWhenNothingIsRefused()
    {
        using var stash = new TestStash();
        var (item, original) = await InterruptedUndoOfAnEditedItem(stash);

        await stash.OpenCleanly();

        CollectionAssert.AreEqual(new[] { item.FilePath, original }, stash.RecycleBin.Asked);
        Assert.IsFalse(File.Exists(item.FilePath) || File.Exists(original));
        Assert.IsEmpty(stash.WorkFiles());
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AnInterruptedUndoOneOfWhoseFilesIsRemovedForGoodAndTheOtherRefusedAtOpenComesBackWholeAskingNoRemoval()
    {
        // A file removed for good leaves nothing in the bin, so undoing the delete asks no removal for it (G29, G28).
        using var stash = new TestStash();
        var (item, original) = await InterruptedUndoOfAnEditedItem(stash);
        stash.RecycleBin.RemovingForGood = path => path == item.FilePath;
        stash.RecycleBin.Refusing = path => path == original;

        var opening = await stash.Open();

        Assert.IsNotNull(opening.Store);
        Assert.AreEqual(new Failure("Move to the Recycle Bin", "edited.png", "the test Recycle Bin refuses it"), opening.Failures.Single());
        CollectionAssert.AreEqual(new[] { item.FilePath, original }, stash.RecycleBin.Asked);
        Assert.IsEmpty(stash.RecycleBin.Removed);
        await AssertBackWhole(stash, item, original);
    }

    [TestMethod]
    public async Task ACopyThatCannotBeMadeAgainAtOpenIsARefusalAndTheItemComesBackWithNothingRecycled()
    {
        using var stash = new TestStash();
        var (item, original) = await InterruptedUndoOfAnEditedItem(stash);
        stash.RecycleBin.Refusing = path => path == original;
        var hold = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        stash.RecycleBin.BeforeEach = _ => hold.Dispose();

        StoreOpening opening;
        try
        {
            opening = await stash.Open();
        }
        finally
        {
            hold.Dispose();
        }

        Assert.IsNotNull(opening.Store);
        Assert.IsEmpty(stash.RecycleBin.Asked);
        Assert.AreEqual(new Failure("Finish an unfinished delete", "edited.png", HeldFileRetry.HeldReason), opening.Failures.Single());
        await AssertBackWhole(stash, item, original);
    }

    [TestMethod]
    public async Task AnEditedItemThatCannotBePutBackAtOpenStaysDeletedWithItsCopyForTheNextStart()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.Import("edited", TestStash.Bytes(50));
        stash.Close();
        stash.MakeEdited(item, TestStash.Bytes(51));
        var original = stash.Folder.InOriginals("edited.png");
        var store = await stash.OpenCleanly();
        // A live refusal: the finished picture went to the bin, the original stayed.
        stash.RecycleBin.Refusing = path => path == original;
        await store.Delete([item.Id]);
        stash.Close();
        var finishedCopy = stash.HeldCopy(item.Id);
        var originalCopy = stash.HeldCopy(item.Id, "original_file");

        StoreOpening opening;
        using (new FileStream(finishedCopy, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            opening = await stash.Open();
        }

        Assert.IsNotNull(opening.Store);
        CollectionAssert.AreEquivalent(
            new[]
            {
                new Failure("Move to the Recycle Bin", "edited.png", "the test Recycle Bin refuses it"),
                new Failure("Undo an unfinished delete", "edited.png", HeldFileRetry.HeldReason),
            },
            opening.Failures.ToList());
        Assert.IsEmpty(await stash.All());
        Assert.IsTrue(File.Exists(finishedCopy), "the clearing keeps a marked row's held copy");
        Assert.IsFalse(File.Exists(originalCopy));
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT deleted FROM items", r => r.GetBoolean(0)).Single());

        stash.RecycleBin.Refusing = _ => false;
        await stash.OpenCleanly();

        Assert.IsFalse(File.Exists(original));
        Assert.IsEmpty(stash.WorkFiles());
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task ADeleteStillRefusedAtOpenIsUndoneFromItsCopiesAndStaysUndone()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var imported = await stash.Import("refused");
        stash.Tag(imported.Id, "mood");
        var item = (await stash.All()).Single();
        stash.RecycleBin.Refusing = _ => true;
        await store.Delete([item.Id]);
        stash.Close();

        var opening = await stash.Open();

        Assert.IsNotNull(opening.Store);
        Assert.AreEqual(new Failure("Move to the Recycle Bin", "refused.png", "the test Recycle Bin refuses it"), opening.Failures.Single());
        TestStash.AssertSameItem(item, (await stash.All()).Single());
        Assert.IsEmpty(stash.WorkFiles());
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT deleted = 0 AND held_copies IS NULL FROM items", r => r.GetBoolean(0)).Single());

        stash.RecycleBin.Refusing = _ => false;
        stash.RecycleBin.Asked.Clear();
        await stash.OpenCleanly();

        Assert.IsEmpty(stash.RecycleBin.Asked);
        TestStash.AssertSameItem(item, (await stash.All()).Single());
    }

    [TestMethod]
    public async Task ARenameKilledBetweenItsCommitAndItsFileStepIsFinishedAtTheNextStart()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(50));
        var upper = await stash.Import("Upper");
        stash.Close();
        CommitRename(stash, cat.Id, "dog", "dog.png", "dog.png", Rename(PendingStep.RenameInRoot, "cat.png", "dog.png", "cat"));
        CommitRename(stash, upper.Id, "upper", "upper.png", "upper.png", Rename(PendingStep.RenameInRoot, "Upper.png", "upper.png", "Upper"));

        await stash.OpenCleanly();

        var items = (await stash.All()).ToDictionary(i => i.Id);
        Assert.AreEqual(("dog", stash.Folder.InRoot("dog.png")), (items[cat.Id].Name, items[cat.Id].FilePath));
        Assert.AreEqual("upper", items[upper.Id].Name);
        CollectionAssert.AreEquivalent(new[] { "dog.png", "upper.png" }, stash.RootFiles(), "a change of case finished in its new case");
        CollectionAssert.AreEqual(TestStash.Bytes(50), File.ReadAllBytes(stash.Folder.InRoot("dog.png")));
        AssertNothingPending(stash);
    }

    [TestMethod]
    public async Task ARenameKilledAfterItsFileStepOnlyHasItsStepCleared()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(51));
        stash.Close();
        CommitRename(stash, cat.Id, "dog", "dog.png", "dog.png", Rename(PendingStep.RenameInRoot, "cat.png", "dog.png", "cat"));
        File.Move(stash.Folder.InRoot("cat.png"), stash.Folder.InRoot("dog.png"));

        await stash.OpenCleanly();

        TestStash.AssertSameItem(cat with { Name = "dog", FileName = "dog.png", FilePath = stash.Folder.InRoot("dog.png") }, (await stash.All()).Single());
        CollectionAssert.AreEqual(new[] { "dog.png" }, stash.RootFiles());
        AssertNothingPending(stash);
    }

    [TestMethod]
    public async Task AnEditedItemsRenameKilledBetweenItsTwoFileStepsIsFinishedAtTheNextStart()
    {
        using var stash = new TestStash();
        var (cat, _) = await EditedItemRenamedHalfway(stash);

        await stash.OpenCleanly();

        var item = (await stash.All()).Single();
        Assert.AreEqual((cat.Id, "dog", stash.Folder.InRoot("dog.png")), (item.Id, item.Name, item.FilePath));
        CollectionAssert.AreEqual(new[] { "dog.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "dog.png" }, Directory.GetFiles(stash.Folder.Originals).Select(Path.GetFileName).ToArray());
        CollectionAssert.AreEqual(TestStash.Bytes(52), File.ReadAllBytes(stash.Folder.InOriginals("dog.png")));
        AssertNothingPending(stash);
    }

    [TestMethod]
    public async Task ARenameStillRefusedAtStartIsUndoneToThePreviousNameNamedAndEveryOtherItemOpens()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(54));
        var other = await stash.Import("other");
        stash.Close();
        CommitRename(stash, cat.Id, "dog", "dog.png", "dog.png", Rename(PendingStep.RenameInRoot, "cat.png", "dog.png", "cat"));

        StoreOpening opening;
        using (TestStash.Hold(stash.Folder.InRoot("cat.png")))
        {
            opening = await stash.Open();
        }

        CollectionAssert.AreEqual(new[] { new Failure("Finish an unfinished rename", "cat.png", HeldFileRetry.HeldReason) }, opening.Failures.ToList());
        var items = (await stash.All()).ToDictionary(i => i.Id);
        TestStash.AssertSameItem(cat, items[cat.Id]);
        Assert.AreEqual("other", items[other.Id].Name);
        CollectionAssert.AreEquivalent(new[] { "cat.png", "other.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(TestStash.Bytes(54), File.ReadAllBytes(cat.FilePath));
        AssertNothingPending(stash);
        stash.Close();
        await stash.OpenCleanly();
        TestStash.AssertSameItem(cat, (await stash.All()).Single(i => i.Id == cat.Id));
    }

    [TestMethod]
    public async Task AnEditedItemsRenameUndoneAtStartPutsItsRenamedFileBack()
    {
        using var stash = new TestStash();
        var (cat, original) = await EditedItemRenamedHalfway(stash);

        StoreOpening opening;
        using (TestStash.Hold(original))
        {
            opening = await stash.Open();
        }

        CollectionAssert.AreEqual(new[] { new Failure("Finish an unfinished rename", "cat.png", HeldFileRetry.HeldReason) }, opening.Failures.ToList());
        var item = (await stash.All()).Single();
        Assert.AreEqual(("cat", "cat.png", cat.FilePath), (item.Name, item.FileName, item.FilePath));
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(TestStash.Bytes(53), File.ReadAllBytes(cat.FilePath));
        CollectionAssert.AreEqual(TestStash.Bytes(52), File.ReadAllBytes(original));
        AssertNothingPending(stash);
    }

    [TestMethod]
    public async Task ARenameThatCannotGoBackAtStartStaysListedWithItsFilesWhereTheyAreForTheNextStart()
    {
        using var stash = new TestStash();
        var (cat, original) = await EditedItemRenamedHalfway(stash);

        StoreOpening opening;
        using (TestStash.Hold(original))
        using (TestStash.Hold(stash.Folder.InRoot("dog.png")))
        {
            opening = await stash.Open();
        }

        CollectionAssert.AreEqual(
            new[] { new Failure("Finish an unfinished rename", "cat.png", HeldFileRetry.HeldReason), new Failure("Undo an unfinished rename", "dog.png", HeldFileRetry.HeldReason) },
            opening.Failures.ToList());
        var item = (await stash.All()).Single();
        Assert.AreEqual((cat.Id, "dog", stash.Folder.InRoot("dog.png")), (item.Id, item.Name, item.FilePath));
        Assert.IsTrue(File.Exists(original), "the original stays where it is");
        stash.Close();
        await stash.OpenCleanly();
        Assert.AreEqual("dog", (await stash.All()).Single().Name);
        CollectionAssert.AreEqual(new[] { "dog.png" }, Directory.GetFiles(stash.Folder.Originals).Select(Path.GetFileName).ToArray());
        AssertNothingPending(stash);
    }

    [TestMethod]
    public async Task ARenameADeleteAndARemovalCouldNotFinishIsUndoneAtTheNextStartWithNothingMarked()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var deleted = await stash.Import("deleted");
        var removed = await stash.Import("removed");
        using (TestStash.Hold(deleted.FilePath))
        using (TestStash.Hold(removed.FilePath))
        {
            await store.Rename(deleted, "deleted later");
            await store.Rename(removed, "removed later");
            Assert.IsFalse((await store.Delete([deleted.Id])).Single().Succeeded);
            Assert.IsFalse((await store.Remove([removed.Id])).Single().Succeeded);
            stash.Close();

            var opening = await stash.Open();

            CollectionAssert.AreEqual(
                new[] { new Failure("Finish an unfinished rename", "deleted.png", HeldFileRetry.HeldReason), new Failure("Finish an unfinished rename", "removed.png", HeldFileRetry.HeldReason) },
                opening.Failures.ToList());
        }

        var items = (await stash.All()).ToDictionary(i => i.Id);
        TestStash.AssertSameItem(deleted, items[deleted.Id]);
        TestStash.AssertSameItem(removed, items[removed.Id]);
        AssertNothingPending(stash);
        Assert.IsEmpty(stash.SqlQueryWhileOpen("SELECT id FROM items WHERE deleted = 1", r => r.GetInt64(0)));
    }

    /// <summary>
    /// An edited item "cat" whose Rename to "dog" was killed after its finished picture was renamed and before its
    /// original was: the row committed with both steps, the root file now dog.png, the original still cat.png.
    /// </summary>
    private static async Task<(StashItem Item, string Original)> EditedItemRenamedHalfway(TestStash stash)
    {
        await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(52));
        stash.Close();
        stash.MakeEdited(cat, TestStash.Bytes(53));
        CommitRename(stash, cat.Id, "dog", "dog.png", "dog.png",
            Rename(PendingStep.RenameInRoot, "cat.png", "dog.png", "cat"),
            Rename(PendingStep.RenameInOriginals, "cat.png", "dog.png", "cat"));
        File.Move(stash.Folder.InRoot("cat.png"), stash.Folder.InRoot("dog.png"));
        return (cat, stash.Folder.InOriginals("cat.png"));
    }

    private static PendingStep Rename(string step, string from, string to, string previousName) => new(step, from, to, previousName);

    /// <summary>The row a Rename commits before its file steps: the new name and file names with its steps.</summary>
    private static void CommitRename(TestStash stash, long id, string name, string finished, string original, params PendingStep[] steps) =>
        stash.Sql(
            "UPDATE items SET name = $name, finished_file = $finished, original_file = $original, pending_steps = $steps WHERE id = $id",
            ("$id", id),
            ("$name", name),
            ("$finished", finished),
            ("$original", original),
            ("$steps", PendingStep.Encode(steps)));

    // Read between the store's calls, which hold no transaction.
    private static void AssertNothingPending(TestStash stash)
    {
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(stash.SqlQueryWhileOpen("SELECT id FROM items WHERE pending_steps IS NOT NULL", r => r.GetInt64(0)));
    }

    /// <summary>
    /// An edited item deleted, then its Undo killed after both renames and the bin's removal, before the mark was
    /// cleared: both files in place, the row marked with its copies recorded, the copies gone.
    /// </summary>
    private static async Task<(StashItem Item, string Original)> InterruptedUndoOfAnEditedItem(TestStash stash)
    {
        await stash.OpenCleanly();
        var item = await stash.Import("edited", TestStash.Bytes(40));
        stash.Close();
        stash.MakeEdited(item, TestStash.Bytes(41));
        var original = stash.Folder.InOriginals("edited.png");
        var store = await stash.OpenCleanly();
        await store.Delete([item.Id]);
        stash.Close();
        File.Move(stash.HeldCopy(item.Id), item.FilePath);
        File.Move(stash.HeldCopy(item.Id, "original_file"), original);
        stash.RecycleBin.Asked.Clear();
        return (item, original);
    }

    private static async Task AssertBackWhole(TestStash stash, StashItem item, string original)
    {
        Assert.AreEqual(item.Id, (await stash.All()).Single().Id);
        CollectionAssert.AreEqual(TestStash.Bytes(41), File.ReadAllBytes(item.FilePath));
        CollectionAssert.AreEqual(TestStash.Bytes(40), File.ReadAllBytes(original));
        Assert.IsEmpty(stash.WorkFiles());
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT deleted = 0 AND held_copies IS NULL FROM items", r => r.GetBoolean(0)).Single());
    }

    /// <summary>The row an import commits before its rename: "place in root" pending, its file still in .work\.</summary>
    private static void InsertPendingImport(TestStash stash, string name, string workFile, string rootFile) =>
        stash.Sql(
            """
            INSERT INTO items (name, added_at, format, width, height, animated, frame_count, duration_ms, original_file, finished_file, original_sha256, pending_steps)
            VALUES ($name, 0, 'Png', 8, 6, 0, 1, 0, $file, $file, randomblob(32), $steps)
            """,
            ("$name", name),
            ("$file", rootFile),
            ("$steps", PendingStep.Encode([PendingStep.Place(workFile, rootFile)])));
}
