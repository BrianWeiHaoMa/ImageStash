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
        stash.Tag(a.Id, "Cat");

        var added = await store.AddTag([b], "cat");

        Assert.IsTrue(added.Single().Succeeded, TestStash.Failures(added));
        CollectionAssert.AreEqual(new[] { (a.Id, "Cat"), (b.Id, "Cat") }, stash.TagRows());
        AssertCounts([("Cat", 2)], await store.TagCounts());
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
        await store.SetFavorite([a, b], true);
        await store.SetFavorite([b], false);
        await store.Favorites();
        await store.TagCounts();

        stash.Close();
        CollectionAssert.AreEqual(before, stash.FilesAndUses());
    }

    private static void AssertCounts((string Tag, int Items)[] expected, Outcome<IReadOnlyList<TagCount>> counts)
    {
        Assert.IsTrue(counts.Succeeded, counts.Failure?.ToString());
        CollectionAssert.AreEqual(expected.Select(e => new TagCount(e.Tag, e.Items)).ToList(), counts.Value!.ToList());
    }

}
