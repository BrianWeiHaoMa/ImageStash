using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// Store's side of the lit tags on an import (G11, G28, R9): a new item gains every one in its insert; the item a file
/// already was in the stash as gains those it lacks, all in one transaction, and the answer says which, as committed;
/// a refused write leaves it with none and is the file's failure beside the answer that stands; an import's Undo takes
/// back exactly the tags it gave.
/// </summary>
[TestClass]
public sealed class ImportDuplicateTagsTests
{
    [TestMethod]
    public async Task TheItemAlreadyInTheStashGainsTheTagsItLacksAndTheAnswerSaysWhich()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(40);
        var existing = await stash.Import("cat", bytes);
        stash.Tag(existing.Id, "dog");
        var staged = await stash.Stage("cat.png", bytes);

        var found = await store.CommitImport(TestStash.Candidate(staged, "cat", bytes) with { Tags = ["Dog", "funny"] });

        Assert.IsTrue(found.Succeeded, found.Failure?.ToString());
        Assert.IsTrue(found.Value!.AlreadyInStash);
        CollectionAssert.AreEqual(new[] { "funny" }, found.Value.GainedTags.ToList(), "the tag it carried, in any spelling, is not gained");
        CollectionAssert.AreEqual(new[] { "dog", "funny" }, found.Value.Item.Tags.ToList(), "the item as the store now holds it");
        TestStash.AssertSameItem(found.Value.Item, (await stash.All()).Single());
        CollectionAssert.AreEqual(new[] { (existing.Id, "dog"), (existing.Id, "funny") }, stash.TagRows());
        Assert.IsFalse(File.Exists(staged), "the copy is discarded as before");
    }

    [TestMethod]
    public async Task ANewItemGainsEveryTagAndADuplicateWithNoTagsGainsNone()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(41);

        var added = await store.CommitImport(TestStash.Candidate(await stash.Stage("cat.png", bytes), "cat", bytes) with { Tags = ["funny", "dog"] });
        var again = await store.CommitImport(TestStash.Candidate(await stash.Stage("cat.png", bytes), "cat", bytes));

        CollectionAssert.AreEqual(new[] { "funny", "dog" }, added.Value!.GainedTags.ToList());
        Assert.IsTrue(again.Succeeded, again.Failure?.ToString());
        Assert.IsTrue(again.Value!.AlreadyInStash);
        Assert.IsEmpty(again.Value.GainedTags);
    }

    [TestMethod]
    public async Task ARefusedTagWriteLeavesTheItemWithNoneOfTheTagsAndIsTheFilesFailureBesideTheAnswer()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(42);
        var existing = await stash.Import("cat", bytes);
        // The store refuses the second tag's row (E3: a refused write); the first was written in the same transaction.
        stash.SqlWhileOpen("CREATE TRIGGER refuse_dog BEFORE INSERT ON tags WHEN NEW.tag = 'dog' BEGIN SELECT RAISE(ABORT, 'refused'); END");
        var staged = await stash.Stage("cat.png", bytes);

        var found = await store.CommitImport(TestStash.Candidate(staged, "cat", bytes) with { Tags = ["funny", "dog"] });

        Assert.IsFalse(found.Succeeded);
        Assert.AreEqual((ItemTags.AddAction, StashFolder.StoreFileName), (found.Failure!.Action, found.Failure.Subject));
        Assert.IsTrue(found.Value!.AlreadyInStash, "the answer stands beside the failure (G12)");
        Assert.AreEqual(existing.Id, found.Value.Item.Id);
        Assert.IsEmpty(found.Value.GainedTags, "no tag is claimed (R9)");
        Assert.IsEmpty(found.Value.Item.Tags);
        Assert.IsEmpty(stash.TagRows(), "neither tag stays: they are one transaction (G28)");
        Assert.IsFalse(File.Exists(staged), "the copy is still discarded");
    }

    [TestMethod]
    public async Task AnImportsUndoTakesBackOnlyTheTagsItGaveAndRemovesWhatItAdded()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(43);
        var existing = await stash.Import("cat", bytes);
        stash.Tag(existing.Id, "dog");
        var found = (await store.CommitImport(TestStash.Candidate(await stash.Stage("cat.png", bytes), "cat", bytes) with { Tags = ["dog", "funny"] })).Value!;
        var fresh = TestStash.Bytes(44);
        var added = (await store.CommitImport(TestStash.Candidate(await stash.Stage("new.png", fresh), "new", fresh) with { Tags = ["dog", "funny"] })).Value!;

        var undone = await store.UndoImport([added.Item.Id], [(existing.Id, found.GainedTags)]);

        Assert.AreEqual("", TestStash.Failures(undone));
        Assert.HasCount(1, undone, "one outcome per item removed, the tags taken back with them");
        CollectionAssert.AreEqual(new[] { (existing.Id, "dog") }, stash.TagRows(), "the tag the item carried before stays");
        Assert.AreEqual(existing.Id, (await stash.All()).Single().Id);
        CollectionAssert.AreEqual(new[] { new TagCount("dog", 1) }, (await store.TagCounts()).Value!.ToList(), "funny ceases to exist (G17)");
    }

    [TestMethod]
    public async Task AnImportsUndoThatCannotMarkItsItemsTakesBackNoTagEither()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(45);
        var existing = await stash.Import("cat", bytes);
        var found = (await store.CommitImport(TestStash.Candidate(await stash.Stage("cat.png", bytes), "cat", bytes) with { Tags = ["funny"] })).Value!;
        var fresh = TestStash.Bytes(46);
        var added = (await store.CommitImport(TestStash.Candidate(await stash.Stage("new.png", fresh), "new", fresh) with { Tags = ["funny"] })).Value!;
        // The store refuses the removal's mark (E3: a refused write): the Undo is one change, absent or complete (G28).
        stash.SqlWhileOpen("CREATE TRIGGER refuse_mark BEFORE UPDATE OF deleted ON items BEGIN SELECT RAISE(ABORT, 'refused'); END");

        var undone = await store.UndoImport([added.Item.Id], [(existing.Id, found.GainedTags)]);

        Assert.IsTrue(undone.All(outcome => !outcome.Succeeded), "the Undo is named as failed");
        CollectionAssert.AreEqual(new[] { (existing.Id, "funny"), (added.Item.Id, "funny") }, stash.TagRows(), "the tag given stays with the item that stays");
        Assert.HasCount(2, await stash.All());
    }
}
