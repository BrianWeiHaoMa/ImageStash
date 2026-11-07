using System.Diagnostics;
using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// Delete with Undo on the store (G18, State: the Delete bullet): the files held as copies in .work\, the rows marked
/// with their name, tags, favorite flag and uses kept, the files recycled; Undo from the copies, the bin's entries
/// removed, the marks cleared; the finish deleting the copies and removing the rows; each refusal named while the rest
/// of the batch goes through (G35); no use recorded (G10).
/// </summary>
[TestClass]
public sealed class ItemDeletionTests
{
    [TestMethod]
    public async Task DeletedItemsAreHeldAsCopiesMarkedAndRecycledWithTheirFactsKept()
    {
        using var stash = new TestStash();
        var (store, items) = await ThreeItems(stash);
        var bytes = items.Select(i => File.ReadAllBytes(i.FilePath)).ToList();

        var deleted = await store.Delete(Ids(items));

        Assert.IsTrue(deleted.All(o => o.Succeeded), TestStash.Failures(deleted));
        CollectionAssert.AreEqual(items.Select(i => (i.Id, i.Name)).ToList(), deleted.Select(o => (o.Value!.Id, o.Value.Name)).ToList());
        Assert.IsTrue(deleted.All(o => o.Value!.RecycledIdentities.Count == 1 && File.Exists(o.Value.RecycledIdentities[0])));
        CollectionAssert.AreEqual(items.Select(i => i.FilePath).ToList(), stash.RecycleBin.Asked);
        Assert.IsEmpty(stash.RootFiles());
        Assert.HasCount(3, stash.WorkFiles());
        foreach (var (item, original) in items.Zip(bytes))
        {
            CollectionAssert.AreEqual(original, File.ReadAllBytes(stash.HeldCopy(item.Id)));
        }

        Assert.IsEmpty(await stash.All());
        Assert.IsEmpty((await store.Recent()).Value!);
        Assert.AreEqual("tagged 2.png", (await stash.Import("tagged")).FileName, "a deleted item keeps its file name taken");
        stash.Close();
        CollectionAssert.AreEqual(
            items.Select(_ => true).ToList(),
            stash.SqlQuery("SELECT deleted = 1 AND held_copies IS NOT NULL FROM items WHERE id <= $last ORDER BY id", r => r.GetBoolean(0), ("$last", items[2].Id)));
        Assert.AreEqual(1L, stash.SqlQuery("SELECT COUNT(*) FROM tags WHERE item_id = $id", r => r.GetInt64(0), ("$id", items[0].Id)).Single());
        Assert.AreEqual(2L, stash.SqlQuery("SELECT COUNT(*) FROM uses WHERE item_id = $id", r => r.GetInt64(0), ("$id", items[2].Id)).Single());
    }

    [TestMethod]
    public async Task UndoBringsEachItemBackWholeFromItsCopiesAndRemovesTheBinsEntries()
    {
        using var stash = new TestStash();
        var (store, items) = await ThreeItems(stash);
        var bytes = items.Select(i => File.ReadAllBytes(i.FilePath)).ToList();
        var recent = (await store.Recent()).Value!.Select(i => i.Id).ToList();
        var deleted = (await store.Delete(Ids(items))).Select(o => o.Value!).ToList();
        var atRemoval = new List<(bool FilesBack, long Marked)>();
        stash.RecycleBin.RefusingRemoval = _ =>
        {
            atRemoval.Add((items.All(i => File.Exists(i.FilePath)), MarkedOnDisk(stash)));
            return false;
        };

        var restored = await store.UndoDelete(deleted);

        Assert.IsTrue(restored.All(o => o.Succeeded && o.Value!.Named.Count == 0), TestStash.Failures(restored));
        CollectionAssert.AreEqual(Enumerable.Repeat((true, 3L), 3).ToList(), atRemoval, "the files are back before the bin's entries go, and the marks are cleared last");
        foreach (var (item, original) in items.Zip(bytes))
        {
            CollectionAssert.AreEqual(original, File.ReadAllBytes(item.FilePath));
        }

        Assert.IsEmpty(stash.WorkFiles());
        CollectionAssert.AreEqual(deleted.SelectMany(d => d.RecycledIdentities).ToList(), stash.RecycleBin.Removed);
        Assert.IsFalse(deleted.Any(d => File.Exists(d.RecycledIdentities[0])), "the bin's entries are gone");
        foreach (var (item, back) in items.Zip((await stash.All()).OrderBy(i => i.Id)))
        {
            TestStash.AssertSameItem(item, back);
        }

        CollectionAssert.AreEqual(recent, (await store.Recent()).Value!.Select(i => i.Id).ToList());

        var again = await store.UndoDelete(deleted);

        CollectionAssert.AreEqual(items.Select(i => new Failure("Undo the delete", i.Name, "it is not deleted")).ToList(), again.Select(o => o.Failure).ToList());
        Assert.HasCount(3, stash.RecycleBin.Removed);
        Assert.HasCount(3, stash.RootFiles());
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT deleted = 0 AND held_copies IS NULL FROM items", r => r.GetBoolean(0)).All(cleared => cleared));
        Assert.AreEqual(2L, stash.SqlQuery("SELECT COUNT(*) FROM uses", r => r.GetInt64(0)).Single(), "neither Delete nor Undo records a use");
    }

    [TestMethod]
    public async Task ABinEntryWhoseRemovalIsRefusedIsNamedAndItsItemIsBackAnyway()
    {
        using var stash = new TestStash();
        var (store, items) = await ThreeItems(stash);
        var deleted = (await store.Delete(Ids(items))).Select(o => o.Value!).ToList();
        var kept = deleted[1].RecycledIdentities[0];
        stash.RecycleBin.RefusingRemoval = identity => identity == kept;

        var restored = await store.UndoDelete(deleted);

        Assert.IsTrue(restored.All(o => o.Succeeded), TestStash.Failures(restored));
        Assert.AreEqual(new Failure("Remove from the Recycle Bin", Path.GetFileName(kept), "the test Recycle Bin refuses to remove it"), restored[1].Value!.Named.Single());
        Assert.IsEmpty(restored[0].Value!.Named);
        Assert.IsEmpty(restored[2].Value!.Named);
        Assert.IsTrue(File.Exists(items[1].FilePath));
        Assert.HasCount(3, await stash.All());
        Assert.IsTrue(File.Exists(kept), "the refused entry stays in the bin");
        Assert.IsFalse(File.Exists(deleted[0].RecycledIdentities[0]));
        Assert.IsFalse(File.Exists(deleted[2].RecycledIdentities[0]));
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT deleted = 0 AND held_copies IS NULL FROM items", r => r.GetBoolean(0)).All(cleared => cleared));
    }

    [TestMethod]
    public async Task TheFinishDeletesTheCopiesAndRemovesTheRowsAndLeavesTheBinsEntries()
    {
        using var stash = new TestStash();
        var (store, items) = await ThreeItems(stash);
        var deleted = (await store.Delete(Ids(items))).Select(o => o.Value!).ToList();

        var finished = await store.FinishDelete(Ids(items));

        Assert.IsTrue(finished.All(o => o.Succeeded), string.Join("; ", finished.Select(o => o.Failure)));
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(stash.RecycleBin.Removed);
        Assert.IsTrue(deleted.All(d => File.Exists(d.RecycledIdentities[0])));
        Assert.AreEqual("tagged.png", (await stash.Import("tagged")).FileName, "a removed row frees its file name");
        stash.Close();
        Assert.AreEqual(1L, stash.SqlQuery("SELECT COUNT(*) FROM items", r => r.GetInt64(0)).Single());
        Assert.AreEqual(0L, stash.SqlQuery("SELECT (SELECT COUNT(*) FROM tags) + (SELECT COUNT(*) FROM uses)", r => r.GetInt64(0)).Single());
    }

    [TestMethod]
    public async Task AnEditedItemsTwoFilesAreHeldRecycledAndPutBackWhereTheyWere()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.Import("edited", TestStash.Bytes(30));
        stash.Close();
        stash.MakeEdited(item, TestStash.Bytes(31));
        var original = stash.Folder.InOriginals("edited.png");
        var store = await stash.OpenCleanly();

        var deleted = (await store.Delete([item.Id])).Single();

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        Assert.HasCount(2, deleted.Value!.RecycledIdentities);
        CollectionAssert.AreEqual(new[] { item.FilePath, original }, stash.RecycleBin.Asked);
        Assert.IsFalse(File.Exists(item.FilePath) || File.Exists(original));
        CollectionAssert.AreEqual(TestStash.Bytes(31), File.ReadAllBytes(stash.HeldCopy(item.Id)));
        CollectionAssert.AreEqual(TestStash.Bytes(30), File.ReadAllBytes(stash.HeldCopy(item.Id, "original_file")));

        var restored = (await store.UndoDelete([deleted.Value])).Single();

        Assert.IsTrue(restored.Succeeded && restored.Value!.Named.Count == 0, restored.Failure?.ToString());
        CollectionAssert.AreEqual(TestStash.Bytes(31), File.ReadAllBytes(item.FilePath));
        CollectionAssert.AreEqual(TestStash.Bytes(30), File.ReadAllBytes(original));
        CollectionAssert.AreEqual(deleted.Value.RecycledIdentities.ToList(), stash.RecycleBin.Removed);
        Assert.IsEmpty(stash.WorkFiles());
        Assert.AreEqual(item.Id, (await stash.All()).Single().Id);
    }

    [TestMethod]
    public async Task ARecycleTheBinRefusesFailsAloneAfterTheMarkAndUndoBringsBothBack()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var refused = await stash.Import("refused");
        var free = await stash.Import("free");
        stash.RecycleBin.Refusing = path => path == refused.FilePath;

        var deleted = await store.Delete([refused.Id, free.Id]);

        Assert.AreEqual(new Failure("Move to the Recycle Bin", "refused.png", "the test Recycle Bin refuses it"), deleted[0].Failure);
        Assert.IsEmpty(deleted[0].Value!.RecycledIdentities);
        Assert.IsTrue(deleted[1].Succeeded, deleted[1].Failure?.ToString());
        Assert.IsTrue(File.Exists(refused.FilePath));
        Assert.IsTrue(File.Exists(stash.HeldCopy(refused.Id)));
        Assert.IsEmpty(await stash.All(), "both rows are marked");

        var restored = await store.UndoDelete([deleted[0].Value!, deleted[1].Value!]);

        Assert.IsTrue(restored.All(o => o.Succeeded && o.Value!.Named.Count == 0), TestStash.Failures(restored));
        CollectionAssert.AreEqual(deleted[1].Value!.RecycledIdentities.ToList(), stash.RecycleBin.Removed);
        Assert.IsEmpty(stash.WorkFiles());
        CollectionAssert.AreEquivalent(new[] { refused.Id, free.Id }, (await stash.All()).Select(i => i.Id).ToList());
    }

    [TestMethod]
    public async Task ARenameBackThatStaysRefusedFailsTheUndoAndTheNextStartFinishesTheDelete()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("held back");
        var deleted = (await store.Delete([item.Id])).Single().Value!;

        Outcome<RestoredItem> restored;
        using (new FileStream(stash.HeldCopy(item.Id), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            restored = (await store.UndoDelete([deleted])).Single();
        }

        Assert.AreEqual(new Failure("Undo the delete", "held back.png", HeldFileRetry.HeldReason), restored.Failure);
        Assert.IsFalse(File.Exists(item.FilePath));
        Assert.IsEmpty(stash.RecycleBin.Removed, "the bin's entry stays while the file is not back");
        Assert.IsEmpty(await stash.All());

        await stash.Reopen();

        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsTrue(File.Exists(deleted.RecycledIdentities[0]));
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AnItemOneOfWhoseCopiesIsGoneIsNotTouchedByUndo()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.Import("edited", TestStash.Bytes(80));
        stash.Close();
        stash.MakeEdited(item, TestStash.Bytes(81));
        var store = await stash.OpenCleanly();
        var deleted = (await store.Delete([item.Id])).Single().Value!;
        var finishedCopy = stash.HeldCopy(item.Id);
        File.Delete(stash.HeldCopy(item.Id, "original_file"));

        var restored = (await store.UndoDelete([deleted])).Single();

        Assert.AreEqual(new Failure("Undo the delete", "edited.png", "its held copy is gone"), restored.Failure);
        Assert.IsFalse(File.Exists(item.FilePath), "nothing is put back when the item cannot come back whole");
        Assert.IsTrue(File.Exists(finishedCopy));
        Assert.IsEmpty(stash.RecycleBin.Removed);
        Assert.IsEmpty(await stash.All());
    }

    [TestMethod]
    public async Task ARepeatedIdentityIsDeletedOnceAndAnsweredAsTheFirst()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("twice");

        var deleted = await store.Delete([item.Id, item.Id]);

        Assert.IsTrue(deleted.All(o => o.Succeeded), TestStash.Failures(deleted));
        Assert.AreSame(deleted[0], deleted[1]);
        CollectionAssert.AreEqual(new[] { item.FilePath }, stash.RecycleBin.Asked);
        Assert.HasCount(1, stash.WorkFiles());
    }

    [TestMethod]
    public async Task AFileWindowsRemovesForGoodInsteadOfRecyclingCompletesTheDeleteAndUndoBringsItBackFromItsCopy()
    {
        // G29: a removal for good completes the Delete that asked for it; G18: the held copy still serves Undo.
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("too big", TestStash.Bytes(70));
        stash.RecycleBin.RemovingForGood = _ => true;

        var deleted = (await store.Delete([item.Id])).Single();

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        Assert.IsEmpty(deleted.Value!.RecycledIdentities, "nothing is in the bin to remove");
        Assert.IsFalse(File.Exists(item.FilePath));
        Assert.IsEmpty(await stash.All());
        Assert.HasCount(1, stash.WorkFiles());
        CollectionAssert.AreEqual(new[] { item.FilePath }, stash.RecycleBin.Asked);

        var restored = (await store.UndoDelete([deleted.Value])).Single();

        Assert.IsTrue(restored.Succeeded && restored.Value!.Named.Count == 0, restored.Failure?.ToString() ?? string.Join("; ", restored.Value!.Named));
        CollectionAssert.AreEqual(TestStash.Bytes(70), File.ReadAllBytes(item.FilePath), "back from its copy");
        Assert.IsEmpty(stash.RecycleBin.Removed, "no removal is asked for a file the bin never took");
        Assert.AreEqual(item.Id, (await stash.All()).Single().Id);
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task APermanentlyRemovedItemIsGoneOnceItsNoticeExpiresAndAfterARestart()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.ImportFixture("too big for the bin", "animated.gif");
        stash.RecycleBin.RemovingForGood = _ => true;

        var deleted = (await store.Delete([item.Id])).Single();
        var expired = (await store.FinishDelete([item.Id])).Single();

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        Assert.IsTrue(expired.Succeeded, expired.Failure?.ToString());
        Assert.IsEmpty(stash.WorkFiles());

        var opening = await stash.Reopen();

        Assert.IsEmpty(opening.Failures);
        Assert.IsEmpty(await stash.All(), "G18, G29: Undo was never pressed, so nothing comes back");
        Assert.IsFalse(File.Exists(item.FilePath));
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task APermanentlyRemovedItemLeftWithoutUndoOrExpiryIsEndedQuietlyAtTheNextStart()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("too big");
        stash.RecycleBin.RemovingForGood = _ => true;
        Assert.IsTrue((await store.Delete([item.Id])).Single().Succeeded);

        var opening = await stash.Reopen();

        Assert.IsEmpty(opening.Failures);
        Assert.IsEmpty(await stash.All());
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(stash.RootFiles());
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AnEditedItemWithOneFileRecycledAndTheOtherRemovedForGoodIsDeletedAndUndoRemovesTheOneEntry()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.Import("edited", TestStash.Bytes(30));
        stash.Close();
        stash.MakeEdited(item, TestStash.Bytes(31));
        var original = stash.Folder.InOriginals("edited.png");
        var store = await stash.OpenCleanly();
        stash.RecycleBin.RemovingForGood = path => path == original;

        var deleted = (await store.Delete([item.Id])).Single();

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        Assert.HasCount(1, deleted.Value!.RecycledIdentities);
        Assert.IsFalse(File.Exists(item.FilePath) || File.Exists(original));

        var restored = (await store.UndoDelete([deleted.Value])).Single();

        Assert.IsTrue(restored.Succeeded && restored.Value!.Named.Count == 0, restored.Failure?.ToString() ?? string.Join("; ", restored.Value!.Named));
        CollectionAssert.AreEqual(TestStash.Bytes(31), File.ReadAllBytes(item.FilePath));
        CollectionAssert.AreEqual(TestStash.Bytes(30), File.ReadAllBytes(original));
        CollectionAssert.AreEqual(deleted.Value.RecycledIdentities.ToList(), stash.RecycleBin.Removed);
        Assert.IsEmpty(Directory.GetFiles(stash.RecycleBin.Folder));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ARecycleRefusedWithTheFileLeftInPlaceStaysAFailureAndTheNextStartListsTheItemAgain()
    {
        // G29: only a removal for good completes a Delete; a refusal, the file left in place, is a failure put back.
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("too big", TestStash.Bytes(70));
        stash.RecycleBin.Refusing = _ => true;

        var deleted = (await store.Delete([item.Id])).Single();

        Assert.AreEqual(new Failure("Move to the Recycle Bin", "too big.png", "the test Recycle Bin refuses it"), deleted.Failure);
        CollectionAssert.AreEqual(TestStash.Bytes(70), File.ReadAllBytes(item.FilePath));
        Assert.IsEmpty(await stash.All(), "the item is still deleted");

        var finished = (await store.FinishDelete([item.Id])).Single();

        Assert.AreEqual(new Failure("Finish the delete", "too big", "its files are still in the stash"), finished.Failure);

        var opening = await stash.Reopen();

        Assert.AreEqual("too big.png", opening.Failures.Single().Subject);
        Assert.AreEqual(item.Id, (await stash.All()).Single().Id);
        CollectionAssert.AreEqual(TestStash.Bytes(70), File.ReadAllBytes(item.FilePath));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task AHeldCopyTheFinishCannotDeleteKeepsTheRowMarkedForTheNextStart()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("copy held");
        await store.Delete([item.Id]);
        var copy = stash.HeldCopy(item.Id);

        Outcome finished;
        using (new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            finished = (await store.FinishDelete([item.Id])).Single();
        }

        Assert.AreEqual(new Failure("Discard a held copy", "copy held.png", HeldFileRetry.HeldReason), finished.Failure);
        Assert.IsTrue(File.Exists(copy));
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT deleted = 1 AND held_copies IS NOT NULL FROM items", r => r.GetBoolean(0)).Single(), "the row stays for the next start");

        await stash.OpenCleanly();

        Assert.IsEmpty(stash.WorkFiles());
        stash.Close();
        Assert.IsEmpty(stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task TheFinishRefusesAnItemWhoseFilesAreStillInTheStashAndKeepsItsCopy()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var refused = await stash.Import("refused");
        stash.RecycleBin.Refusing = _ => true;
        await store.Delete([refused.Id]);

        var finished = await store.FinishDelete([refused.Id]);

        Assert.AreEqual(new Failure("Finish the delete", "refused", "its files are still in the stash"), finished.Single().Failure);
        Assert.IsTrue(File.Exists(refused.FilePath));
        Assert.IsTrue(File.Exists(stash.HeldCopy(refused.Id)));
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT deleted FROM items", r => r.GetBoolean(0)).Single());
    }

    [TestMethod]
    public async Task AFileWhoseCopyCannotBeMadeFailsAloneAndLeavesNothingHeld()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var held = await stash.Import("held");
        var free = await stash.Import("free");

        IReadOnlyList<Outcome<DeletedItem>> deleted;
        using (new FileStream(held.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            deleted = await store.Delete([held.Id, free.Id]);
        }

        Assert.AreEqual(new Failure("Delete", "held.png", HeldFileRetry.HeldReason), deleted[0].Failure);
        Assert.IsNull(deleted[0].Value);
        Assert.IsTrue(deleted[1].Succeeded, deleted[1].Failure?.ToString());
        Assert.AreEqual(held.Id, (await stash.All()).Single().Id);
        CollectionAssert.AreEqual(new[] { Path.GetFileName(stash.HeldCopy(free.Id)) }, stash.WorkFiles());
        CollectionAssert.AreEqual(new[] { free.FilePath }, stash.RecycleBin.Asked);
    }

    [TestMethod]
    public async Task AnItemNoLongerInTheStashIsRefusedByNameAndTheOthersAreDeleted()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var removing = await stash.Import("removing");
        var free = await stash.Import("free");
        // The import's Undo marked it, and the Recycle Bin refused its file.
        stash.RecycleBin.Refusing = path => path == removing.FilePath;
        await store.Remove([removing.Id]);
        stash.RecycleBin.Refusing = _ => false;
        stash.RecycleBin.Asked.Clear();

        var deleted = await store.Delete([9999, removing.Id, free.Id]);

        Assert.AreEqual(new Failure("Delete", "9999", "it is no longer in the stash"), deleted[0].Failure);
        Assert.AreEqual(new Failure("Delete", "removing", "it is no longer in the stash"), deleted[1].Failure);
        Assert.IsNull(deleted[1].Value);
        Assert.IsTrue(deleted[2].Succeeded, deleted[2].Failure?.ToString());
        CollectionAssert.AreEqual(new[] { free.FilePath }, stash.RecycleBin.Asked);
        Assert.IsTrue(File.Exists(removing.FilePath));
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT deleted = 1 AND held_copies IS NULL FROM items WHERE id = $id", r => r.GetBoolean(0), ("$id", removing.Id)).Single());
    }

    [TestMethod]
    public async Task AListedItemWaitingOnItsImportStepIsDeletedAndComesBackWithoutTheStep()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("pending");
        // The import's rename done and its step not yet cleared, as a failed final transaction leaves it: listed.
        stash.SqlWhileOpen("UPDATE items SET pending_steps = $steps", ("$steps", PendingStep.Encode([PendingStep.Place("gone.png", item.FileName)])));
        Assert.AreEqual(item.Id, (await stash.All()).Single().Id);

        var deleted = (await store.Delete([item.Id])).Single();
        var restored = (await store.UndoDelete([deleted.Value!])).Single();

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        Assert.IsTrue(restored.Succeeded, restored.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { item.FilePath }, stash.RecycleBin.Asked);
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT deleted = 0 AND pending_steps IS NULL AND held_copies IS NULL FROM items", r => r.GetBoolean(0)).Single(), "the mark dropped the step");

        await stash.OpenCleanly();

        CollectionAssert.AreEqual(new[] { item.FilePath }, stash.RecycleBin.Asked);
        TestStash.AssertSameItem(item, (await stash.All()).Single());
    }

    [TestMethod]
    public async Task ADeleteFinishesAnUnfinishedRenameFirstAndIsRefusedByNameWhileItCannot()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(70));
        Outcome<DeletedItem> refused;
        using (TestStash.Hold(cat.FilePath))
        {
            await store.Rename(cat, "dog");
            refused = (await store.Delete([cat.Id])).Single();
        }

        Assert.AreEqual(new Failure("Delete", "dog", $"its earlier rename cannot finish yet: {HeldFileRetry.HeldReason}"), refused.Failure);
        Assert.IsEmpty(stash.RecycleBin.Asked);
        Assert.IsEmpty(stash.WorkFiles(), "no copy held");
        Assert.AreEqual(("dog", cat.FilePath), ((await stash.All()).Single().Name, (await stash.All()).Single().FilePath));

        var deleted = (await store.Delete([cat.Id])).Single();

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { stash.Folder.InRoot("dog.png") }, stash.RecycleBin.Asked, "the rename finished first");
        Assert.IsTrue(stash.SqlQueryWhileOpen("SELECT deleted = 1 AND pending_steps IS NULL FROM items", r => r.GetBoolean(0)).Single(), "the mark keeps no rename step");

        var restored = (await store.UndoDelete([deleted.Value!])).Single();

        Assert.IsTrue(restored.Succeeded, restored.Failure?.ToString());
        var back = (await stash.All()).Single();
        Assert.AreEqual(("dog", stash.Folder.InRoot("dog.png")), (back.Name, back.FilePath));
        CollectionAssert.AreEqual(TestStash.Bytes(70), File.ReadAllBytes(back.FilePath));
    }

    [TestMethod]
    public async Task FiveThousandItemsAreAllMarkedBeforeTheFirstRecycle()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        stash.Close();
        stash.AddPictures(5000);
        var store = await stash.OpenCleanly();
        var ids = (await stash.All()).Select(i => i.Id).ToList();
        long? markedAtFirstRecycle = null;
        int? copiesAtFirstRecycle = null;
        stash.RecycleBin.BeforeEach = _ =>
        {
            markedAtFirstRecycle ??= MarkedOnDisk(stash);
            copiesAtFirstRecycle ??= stash.WorkFiles().Length;
        };

        var clock = Stopwatch.StartNew();
        var deleted = await store.Delete(ids);
        clock.Stop();

        Assert.IsTrue(deleted.All(o => o.Succeeded), TestStash.Failures(deleted));
        Assert.HasCount(5000, stash.RecycleBin.Asked);
        Assert.HasCount(5000, stash.WorkFiles());
        Assert.AreEqual(5000L, markedAtFirstRecycle);
        Assert.AreEqual(5000, copiesAtFirstRecycle, "every copy is held before anything is recycled");
        Console.WriteLine($"Delete of 5,000 items took {clock.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>Three items: one tagged "mood", one favorite, one used twice, in identity order, as the store lists them.</summary>
    private static async Task<(StashStore Store, StashItem[] Items)> ThreeItems(TestStash stash)
    {
        var store = await stash.OpenCleanly();
        var tagged = await stash.Import("tagged");
        var favorite = await stash.Import("favorite");
        var used = await stash.Import("used");
        stash.Tag(tagged.Id, "mood");
        stash.SqlWhileOpen("UPDATE items SET favorite = 1 WHERE id = $id", ("$id", favorite.Id));
        await store.RecordUses([used], stash.Clock.Now);
        await store.RecordUses([used], stash.Clock.Now.AddSeconds(1));
        return (store, [.. (await stash.All()).OrderBy(i => i.Id)]);
    }

    private static List<long> Ids(IEnumerable<StashItem> items) => items.Select(i => i.Id).ToList();

    // Read through a second connection while the store's own holds no transaction.
    private static long MarkedOnDisk(TestStash stash)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={stash.Folder.StoreFile};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM items WHERE deleted = 1";
        return (long)command.ExecuteScalar()!;
    }
}
