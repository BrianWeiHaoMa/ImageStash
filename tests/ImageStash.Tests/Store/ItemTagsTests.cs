using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;
using Microsoft.Data.Sqlite;

namespace ImageStash.Tests.Store;

/// <summary>
/// The tag rule (G17, In use: "Tags are search words"): one tag per word with every leading # removed, one spelling per
/// tag across the stash ignoring case for every script, a tag existing only while a listed item carries it; each
/// change one transaction over the batch with one outcome per identity (State, G28, G35); the counts by item (G4).
/// </summary>
[TestClass]
public sealed class ItemTagsTests
{
    [TestMethod]
    public async Task EveryWordOfTheTextIsATagOnEveryItem()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");

        var added = await store.AddTag([a, b], "  #Dog  cat ");

        Assert.IsTrue(added.All(o => o.Succeeded), TestStash.Failures(added));
        Assert.HasCount(2, added);
        CollectionAssert.AreEqual(new[] { (a.Id, "Dog"), (a.Id, "cat"), (b.Id, "Dog"), (b.Id, "cat") }, stash.TagRows());
        CollectionAssert.AreEqual(new[] { "Dog", "cat" }, (await stash.All()).Single(i => i.Id == a.Id).Tags.ToList());
    }

    [TestMethod]
    [DataRow("dog")]
    [DataRow("#dog")]
    [DataRow("##dog")]
    [DataRow("  dog  ")]
    public void ATagIsAWordWithEveryLeadingHashRemoved(string text) =>
        CollectionAssert.AreEqual(new[] { "dog" }, ItemTags.Words(text).ToList());

    [TestMethod]
    public async Task TextWithNoWordsChangesNothing()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        stash.Tag(a.Id, "kept");

        var added = await store.AddTag([a, b], "  ## #  ");

        Assert.HasCount(2, added);
        Assert.IsTrue(added.All(o => o.Succeeded), TestStash.Failures(added));
        CollectionAssert.AreEqual(new[] { (a.Id, "kept") }, stash.TagRows());
    }

    [TestMethod]
    public async Task ATagAlreadyStoredKeepsItsSpelling()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        await store.AddTag([a], "Ärger");

        var added = await store.AddExistingTag([a, b], "ärger");

        Assert.HasCount(2, added.Outcomes);
        Assert.IsTrue(added.Outcomes.All(o => o.Succeeded), TestStash.Failures(added.Outcomes));
        var gain = added.Changes.Single();
        Assert.AreEqual(b.Id, gain.Item.Id);
        CollectionAssert.AreEqual(new[] { "Ärger" }, gain.AddedTags.ToArray());
        CollectionAssert.AreEqual(new[] { (a.Id, "Ärger"), (b.Id, "Ärger") }, stash.TagRows());
        AssertCounts([("Ärger", 2)], await store.TagCounts());

        var undone = await store.UndoItemFacts(added);

        var loss = undone.Changes.Single();
        Assert.AreEqual(b.Id, loss.Item.Id);
        CollectionAssert.AreEqual(new[] { "Ärger" }, loss.RemovedTags.ToArray());
        CollectionAssert.AreEqual(new[] { (a.Id, "Ärger") }, stash.TagRows());
    }

    [TestMethod]
    public async Task OneSpellingHoldsBeyondAscii()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        stash.Tag(a.Id, "Ärger");

        await store.AddTag([b], "ärger");

        CollectionAssert.AreEqual(new[] { (a.Id, "Ärger"), (b.Id, "Ärger") }, stash.TagRows());
    }

    [TestMethod]
    public async Task TwoWordsOfOneTagInOneTextAreOneTag()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");

        await store.AddTag([a], "Dog dog DOG");

        CollectionAssert.AreEqual(new[] { (a.Id, "Dog") }, stash.TagRows());
    }

    [TestMethod]
    public async Task AnItemDeletedMeanwhileKeepsTheOneSpellingThroughItsUndo()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        stash.Tag(a.Id, "Cat");
        var deleted = (await store.Delete([a.Id])).Single();
        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());

        await store.AddTag([b], "cat");
        var whileDeleted = await store.TagCounts();
        var restored = (await store.UndoDelete([deleted.Value!])).Single();

        Assert.IsTrue(restored.Succeeded, restored.Failure?.ToString());
        AssertCounts([("Cat", 1)], whileDeleted);
        AssertCounts([("Cat", 2)], await store.TagCounts());
        CollectionAssert.AreEqual(new[] { (a.Id, "Cat"), (b.Id, "Cat") }, stash.TagRows());
    }

    [TestMethod]
    public async Task ATagIsRemovedInAnySpellingAndCeasesToExistWithItsLastItem()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        await store.AddTag([a, b], "Cat dog");

        var fromA = await store.RemoveTag([a], "cat");
        var afterA = stash.TagRows();
        var fromB = await store.RemoveTag([b], "CAT");
        var again = await store.RemoveTag([a], "Cat");

        Assert.IsTrue(fromA.Concat(fromB).Concat(again).All(o => o.Succeeded), TestStash.Failures([.. fromA, .. fromB, .. again]));
        CollectionAssert.AreEqual(new[] { (a.Id, "dog"), (b.Id, "Cat"), (b.Id, "dog") }, afterA);
        CollectionAssert.AreEqual(new[] { (a.Id, "dog"), (b.Id, "dog") }, stash.TagRows());
        AssertCounts([("dog", 2)], await store.TagCounts());
        Assert.IsEmpty((await store.Search("cat")).Value!, "no item carries it, and no name holds it");
    }

    [TestMethod]
    public async Task AnItemNoLongerInTheStashIsRefusedByNameAndTheOthersAreChanged()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var (gone, marked, a) = await stash.GoneMarkedAndListed();
        stash.Tag(marked.Id, "x");

        var added = await store.AddTag([gone, marked, a], "x");
        var addedRows = stash.TagRows();
        var removed = await store.RemoveTag([gone, marked, a], "x");

        Assert.AreEqual(new Failure("Add a tag", "gone", "it is no longer in the stash"), added[0].Failure, "a row that is gone is named as it was handed in");
        Assert.AreEqual(new Failure("Add a tag", "marked", "it is no longer in the stash"), added[1].Failure);
        Assert.IsTrue(added[2].Succeeded, added[2].Failure?.ToString());
        CollectionAssert.AreEqual(new[] { (marked.Id, "x"), (a.Id, "x") }, addedRows);
        Assert.AreEqual(new Failure("Remove a tag", "gone", "it is no longer in the stash"), removed[0].Failure);
        Assert.AreEqual(new Failure("Remove a tag", "marked", "it is no longer in the stash"), removed[1].Failure);
        Assert.IsTrue(removed[2].Succeeded, removed[2].Failure?.ToString());
        CollectionAssert.AreEqual(new[] { (marked.Id, "x") }, stash.TagRows(), "the marked row keeps its tags for its Undo");
    }

    [TestMethod]
    public async Task ARepeatedIdentityIsChangedOnceAndAnsweredAsTheFirst()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");

        var missing = a with { Id = 9999, Name = "missing" };

        var added = await store.AddTag([a, missing, a, missing], "x");

        Assert.HasCount(4, added);
        Assert.IsTrue(added[0].Succeeded && added[2].Succeeded);
        Assert.AreEqual(new Failure("Add a tag", "missing", "it is no longer in the stash"), added[1].Failure);
        Assert.AreEqual(added[1].Failure, added[3].Failure);
        CollectionAssert.AreEqual(new[] { (a.Id, "x") }, stash.TagRows());
    }

    [TestMethod]
    public async Task MultiwordAddReportsEachItemsActualGainsAndUndoKeepsLaterTags()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a", TestStash.Bytes(1));
        var b = await stash.Import("b", TestStash.Bytes(2));
        var c = await stash.Import("c", TestStash.Bytes(3));
        await store.AddTag([b, c], "Pearl");
        var staleB = (await store.Item(b.Id)).Value!;
        await store.AddTag([a], "Ärger Cozy");
        await store.AddTag([b], "Work");
        await store.RemoveTag([b], "Pearl");
        await store.RecordUses([a], stash.Clock.Now);
        stash.Close();
        var filesAndUses = stash.FilesAndUses();
        store = await stash.OpenCleanly();

        var added = await store.AddTagWithChanges([a, staleB, a], "##ärger PEARL cozy COZY work");

        Assert.HasCount(3, added.Outcomes);
        Assert.IsTrue(added.Outcomes.All(o => o.Succeeded), TestStash.Failures(added.Outcomes));
        CollectionAssert.AreEqual(new[] { a.Id, b.Id }, added.Changes.Select(change => change.Item.Id).ToList());
        CollectionAssert.AreEqual(new[] { "Pearl", "Work" }, added.Changes[0].AddedTags.ToArray());
        CollectionAssert.AreEqual(new[] { "Ärger", "Pearl", "Cozy" }, added.Changes[1].AddedTags.ToArray());
        var addedTags = new[] { "Ärger", "Cozy", "Pearl", "Work" };
        CollectionAssert.AreEquivalent(
            addedTags.SelectMany(tag => new[] { (a.Id, tag), (b.Id, tag) }).Append((c.Id, "Pearl")).ToList(),
            stash.TagRows());
        var unchanged = await store.AddTagWithChanges([a, b], "##ärger PEARL cozy work");
        Assert.IsTrue(unchanged.Outcomes.All(o => o.Succeeded), TestStash.Failures(unchanged.Outcomes));
        Assert.IsEmpty(unchanged.Changes);
        await store.AddTag([b], "Later");

        var undone = await store.UndoItemFacts(added);

        Assert.IsTrue(undone.Outcomes.All(o => o.Succeeded), TestStash.Failures(undone.Outcomes));
        Assert.HasCount(2, undone.Changes);
        var restored = new[] { (a.Id, "Ärger"), (a.Id, "Cozy"), (b.Id, "Work"), (b.Id, "Later"), (c.Id, "Pearl") };
        CollectionAssert.AreEquivalent(restored, stash.TagRows());
        stash.Close();
        CollectionAssert.AreEqual(filesAndUses, stash.FilesAndUses());
        await stash.OpenCleanly();
        CollectionAssert.AreEquivalent(restored, (await stash.All()).SelectMany(item => item.Tags.Select(tag => (item.Id, tag))).ToList());
    }

    [TestMethod]
    public async Task UndoRestoresAVanishedTagToItsFormerCarriers()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a", TestStash.Bytes(1));
        var b = await stash.Import("b", TestStash.Bytes(2));
        await store.AddTag([a], "Ärger");

        var removed = await store.RemoveTagWithChanges([a, b, a], "ärger");

        Assert.IsTrue(removed.Outcomes.All(o => o.Succeeded), TestStash.Failures(removed.Outcomes));
        var loss = removed.Changes.Single();
        Assert.AreEqual(a.Id, loss.Item.Id);
        CollectionAssert.AreEqual(new[] { "Ärger" }, loss.RemovedTags.ToArray());
        AssertCounts([], await store.TagCounts());
        var failedAdd = await store.AddExistingTag([b], "ärger");
        Assert.AreEqual(new Failure("Add a tag", "b", "no item carries #ärger any more"), failedAdd.Outcomes.Single().Failure);
        Assert.IsEmpty(failedAdd.Changes);
        await store.AddTag([a, b], "Later");

        var undone = await store.UndoItemFacts(removed);

        Assert.IsTrue(undone.Outcomes.Single().Succeeded, TestStash.Failures(undone.Outcomes));
        AssertCounts([("Later", 2), ("Ärger", 1)], await store.TagCounts());
        CollectionAssert.AreEquivalent(new[] { (a.Id, "Ärger"), (a.Id, "Later"), (b.Id, "Later") }, stash.TagRows());
    }

    [TestMethod]
    public async Task UndoUsesTheCurrentSpellingOfATagThatWentAwayAndCameBack()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a", TestStash.Bytes(1));
        var b = await stash.Import("b", TestStash.Bytes(2));
        var added = await store.AddTagWithChanges([a], "ÄRGER");
        var removed = await store.RemoveTagWithChanges([a], "ÄRGER");
        await store.AddTag([b], "ärger");

        var undoRemove = await store.UndoItemFacts(removed);

        Assert.IsTrue(undoRemove.Outcomes.Single().Succeeded, TestStash.Failures(undoRemove.Outcomes));
        CollectionAssert.AreEqual(new[] { (a.Id, "ärger"), (b.Id, "ärger") }, stash.TagRows());

        var undoAdd = await store.UndoItemFacts(added);

        Assert.IsTrue(undoAdd.Outcomes.Single().Succeeded, TestStash.Failures(undoAdd.Outcomes));
        CollectionAssert.AreEqual(new[] { (b.Id, "ärger") }, stash.TagRows());
    }

    [TestMethod]
    public async Task UndoRefusesGoneAndMarkedItemsAndChangesTheListedSibling()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var gone = await stash.Import("gone", TestStash.Bytes(1));
        var marked = await stash.Import("marked", TestStash.Bytes(2));
        var live = await stash.Import("live", TestStash.Bytes(3));
        var added = await store.AddTagWithChanges([gone, marked, live], "x");
        Assert.HasCount(3, added.Changes);
        Assert.IsTrue((await store.Remove([gone.Id])).Single().Succeeded);
        Assert.IsTrue((await store.Delete([marked.Id])).Single().Succeeded);

        var undone = await store.UndoItemFacts(added);

        CollectionAssert.AreEqual(new[] { "gone", "marked" }, undone.Outcomes.Take(2).Select(outcome => outcome.Failure?.Subject).ToList());
        Assert.IsTrue(undone.Outcomes[2].Succeeded);
        Assert.AreEqual(live.Id, undone.Changes.Single().Item.Id);
        CollectionAssert.AreEqual(new[] { (marked.Id, "x") }, stash.TagRows());
        CollectionAssert.AreEqual(new[] { live.Id }, (await stash.All()).Select(item => item.Id).ToList());
    }

    [TestMethod]
    public async Task AnotherOrClosedStoreRefusesUndoEvenWithTheSameItemId()
    {
        using var stash = new TestStash();
        using var replacement = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a", TestStash.Bytes(1));
        var added = await store.AddTagWithChanges([a], "x");
        var otherStore = await replacement.OpenCleanly();
        var b = await replacement.Import("b", TestStash.Bytes(2));
        Assert.AreEqual(a.Id, b.Id);
        await otherStore.AddTag([b], "x");

        var wrongStore = await otherStore.UndoItemFacts(added);
        stash.Close();
        var closedStore = await store.UndoItemFacts(added);

        foreach (var refused in new[] { wrongStore, closedStore })
        {
            Assert.AreEqual("a", refused.Outcomes.Single().Failure?.Subject);
            Assert.IsEmpty(refused.Changes);
        }

        CollectionAssert.AreEqual(new[] { (b.Id, "x") }, replacement.TagRows());
        var reopened = await stash.OpenCleanly();
        var stale = await reopened.UndoItemFacts(added);
        Assert.AreEqual("a", stale.Outcomes.Single().Failure?.Subject);
        Assert.IsEmpty(stale.Changes);
        CollectionAssert.AreEqual(new[] { (a.Id, "x") }, stash.TagRows());
    }

    [TestMethod]
    public async Task ARowWaitingOnAStepDoneOnDiskIsTaggedAndKeepsTheStepAndOneNotDoneIsRefused()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var placed = await stash.Import("placed");
        var waiting = await stash.Import("waiting");
        var staged = await stash.Stage("waiting.png", TestStash.Bytes(40));
        // placed: the rename done and the step not cleared, as a failed final transaction leaves it, so it is listed;
        // waiting: its file still in .work\, as a held rename leaves it, so the stash does not list it.
        var placedSteps = PendingStep.Encode([PendingStep.Place("gone.png", placed.FileName)]);
        stash.SqlWhileOpen("UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", placed.Id), ("$steps", placedSteps));
        stash.SqlWhileOpen("UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", waiting.Id), ("$steps", PendingStep.Encode([PendingStep.Place(Path.GetFileName(staged), waiting.FileName)])));
        File.Move(waiting.FilePath, stash.Outside("waiting elsewhere.png"));

        var added = await store.AddTag([placed, waiting], "x");

        Assert.IsTrue(added[0].Succeeded, added[0].Failure?.ToString());
        Assert.AreEqual(new Failure("Add a tag", "waiting", "it is no longer in the stash"), added[1].Failure);
        CollectionAssert.AreEqual(new[] { (placed.Id, "x") }, stash.TagRows());
        Assert.AreEqual(placedSteps, stash.SqlQueryWhileOpen("SELECT pending_steps FROM items WHERE id = $id", r => r.GetString(0), ("$id", placed.Id)).Single(), "the step stays as recorded");
    }

    [TestMethod]
    public async Task ABatchIsOneTransactionSoARefusedStoreLeavesNoTagOfIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        stash.SqlWhileOpen($"CREATE TRIGGER refuse_b BEFORE INSERT ON tags WHEN NEW.item_id = {b.Id} BEGIN SELECT RAISE(ABORT, 'refused for the check'); END");

        var added = await store.AddTag([a, b], "dog");

        Assert.HasCount(2, added);
        Assert.IsTrue(added.All(o => !o.Succeeded && o.Failure.Action == "Add a tag" && o.Failure.Subject == StashFolder.StoreFileName), TestStash.Failures(added));
        Assert.IsEmpty(stash.TagRows(), "a's tag went with the transaction b's refusal ended");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ARefusedWriteReportsNoCommittedChangesInEitherDirection(bool adding)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a", TestStash.Bytes(1));
        var b = await stash.Import("b", TestStash.Bytes(2));
        var other = await stash.Import("other", TestStash.Bytes(3));
        await store.AddTag([other], "x");
        if (!adding)
        {
            await store.AddTag([a, b], "x");
        }

        var before = stash.TagRows();
        var forward = adding ? "INSERT ON tags WHEN NEW" : "DELETE ON tags WHEN OLD";
        var inverse = adding ? "DELETE ON tags WHEN OLD" : "INSERT ON tags WHEN NEW";
        stash.SqlWhileOpen($"CREATE TRIGGER refuse_b BEFORE {forward}.item_id = {b.Id} BEGIN SELECT RAISE(ABORT, 'refused'); END");

        var refused = adding ? await store.AddTagWithChanges([a, b], "x") : await store.RemoveTagWithChanges([a, b], "x");

        AssertRefused(refused, adding ? "Add a tag" : "Remove a tag");
        CollectionAssert.AreEqual(before, stash.TagRows());
        stash.SqlWhileOpen("DROP TRIGGER refuse_b");
        var applied = adding ? await store.AddTagWithChanges([a, b], "x") : await store.RemoveTagWithChanges([a, b], "x");
        Assert.HasCount(2, applied.Changes);
        var after = stash.TagRows();
        stash.SqlWhileOpen($"CREATE TRIGGER refuse_b BEFORE {inverse}.item_id = {b.Id} BEGIN SELECT RAISE(ABORT, 'refused'); END");

        var undoRefused = await store.UndoItemFacts(applied);

        AssertRefused(undoRefused, adding ? "Undo adding a tag" : "Undo removing a tag");
        CollectionAssert.AreEqual(after, stash.TagRows());
        stash.SqlWhileOpen("DROP TRIGGER refuse_b");
        var undone = await store.UndoItemFacts(applied);
        Assert.HasCount(2, undone.Changes);
        CollectionAssert.AreEqual(before, stash.TagRows());
    }

    private static void AssertRefused(ItemFactResult result, string action)
    {
        Assert.HasCount(2, result.Outcomes);
        Assert.IsTrue(result.Outcomes.All(o => o.Failure is { } failure && failure.Action == action &&
            failure.Subject == StashFolder.StoreFileName && failure.Reason.Contains("refused", StringComparison.Ordinal)),
            TestStash.Failures(result.Outcomes));
        Assert.IsEmpty(result.Changes);
    }

    [TestMethod]
    public async Task AHeldStoreIsNamedForEveryItem()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");

        IReadOnlyList<Outcome> added;
        using (var holder = new SqliteConnection($"Data Source={stash.Folder.StoreFile};Pooling=False"))
        {
            holder.Open();
            using var begin = holder.CreateCommand();
            begin.CommandText = "BEGIN IMMEDIATE";
            begin.ExecuteNonQuery();
            added = await store.AddTag([a, b], "dog");
        }

        var failure = new Failure("Add a tag", StashFolder.StoreFileName, HeldFileRetry.HeldReason);
        CollectionAssert.AreEqual(new[] { failure, failure }, added.Select(o => o.Failure).ToList());
        Assert.IsEmpty(stash.TagRows());
    }

    [TestMethod]
    public async Task TheCountsAreTheItemsTheStashListsByCountThenSpelling()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        var c = await stash.Import("c");
        var waiting = await stash.Import("waiting");
        // Stored in an order of their own, so neither the order they were stored in nor an ordinal comparison, which
        // puts every capital first, gives the order equal counts take.
        await store.AddTag([b], "Banana Cherry");
        await store.AddTag([a], "apple Banana");
        await store.AddTag([c], "zebra apple");
        await store.AddTag([waiting], "unlisted");
        await store.Delete([c.Id]);
        var staged = await stash.Stage("waiting.png", TestStash.Bytes(41));
        stash.SqlWhileOpen("UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", waiting.Id), ("$steps", PendingStep.Encode([PendingStep.Place(Path.GetFileName(staged), waiting.FileName)])));
        File.Move(waiting.FilePath, stash.Outside("waiting elsewhere.png"));

        AssertCounts([("Banana", 2), ("apple", 1), ("Cherry", 1)], await store.TagCounts());
    }

    [TestMethod]
    public async Task NoChangeOrCountRecordsAUseOrTouchesAFile()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        await store.RecordUses([a], stash.Clock.Now);
        stash.Close();
        var before = stash.FilesAndUses();
        store = await stash.OpenCleanly();

        await store.AddTag([a, b], "dog cat");
        await store.RemoveTag([a], "dog");
        var starred = await store.SetFavoriteWithChanges([a, b], true);
        await store.SetFavorite([b], false);
        await store.Favorites();
        await store.TagCounts();

        CollectionAssert.AreEqual(before, stash.FilesAndUses());
        var unstarred = await store.UndoItemFacts(starred);
        Assert.AreEqual(a.Id, unstarred.Changes.Single().Item.Id);
        stash.Close();
        CollectionAssert.AreEqual(before, stash.FilesAndUses());
    }

    private static void AssertCounts((string Tag, int Items)[] expected, Outcome<IReadOnlyList<TagCount>> counts)
    {
        Assert.IsTrue(counts.Succeeded, counts.Failure?.ToString());
        CollectionAssert.AreEqual(expected.Select(e => new TagCount(e.Tag, e.Items)).ToList(), counts.Value!.ToList());
    }

}
