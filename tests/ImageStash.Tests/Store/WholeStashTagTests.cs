using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

[TestClass]
public sealed class WholeStashTagTests
{
    [TestMethod]
    public async Task RemoveFromAllUsesMembershipsAndUndoRestoresOnlyTheLossesInTheCurrentSpelling()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var edited = await stash.ImportFixture("edited", "still.png");
        var captionOnly = await stash.ImportFixture("caption", "transparent.png");
        var outsideQuery = await stash.Import("outside query", TestStash.Bytes(1));
        var held = await stash.Import("held by Delete", TestStash.Bytes(2));
        var nameOnly = await stash.Import("Ärger", TestStash.Bytes(3));
        var layers = new LayerList([new CaptionBarLayer("Ärger", "Segoe UI", 16, new Rgba(0, 0, 0, 255), Outline.None)]);
        Assert.IsTrue((await stash.SaveEdit(edited, layers)).Succeeded);
        Assert.IsTrue((await stash.SaveEdit(captionOnly, layers)).Succeeded);
        Assert.IsTrue((await store.SetFavorite([edited], true)).Single().Succeeded);
        Assert.IsTrue((await store.RecordUses([edited], stash.Clock.Now)).Succeeded);
        Assert.IsTrue((await store.AddTag([edited, outsideQuery, held], "Ärger")).All(outcome => outcome.Succeeded));
        Assert.IsTrue((await store.AddTag([captionOnly, nameOnly], "keep")).All(outcome => outcome.Succeeded));
        var deleted = (await store.Delete([held.Id])).Single();
        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        var factsBefore = (await store.Item(edited.Id)).Value!;
        var filesAndUses = stash.FilesAndUses();
        CollectionAssert.AreEquivalent(new[] { edited.Id, captionOnly.Id, nameOnly.Id, outsideQuery.Id },
            (await store.Search("ärger")).Value!.Select(item => item.Id).ToArray());
        Assert.AreEqual(edited.Id, (await store.Search("edited ärger")).Value!.Single().Id);

        var removal = Value(await store.RemoveTagFromAll("ärger", 2));

        Assert.AreEqual(new TagCount("Ärger", 2), removal.Count);
        Assert.IsNotNull(removal.Receipt);
        CollectionAssert.AreEquivalent(new[] { edited.Id, outsideQuery.Id },
            removal.Receipt.Changes.Select(change => change.Item.Id).ToArray());
        Assert.IsTrue(removal.Receipt.Changes.All(change => change.RemovedTags.SequenceEqual(["Ärger"]) &&
            change.AddedTags.IsEmpty && change.PreviousFavorite is null));
        await AssertCounts(store, new TagCount("keep", 2));
        CollectionAssert.AreEquivalent(new[] { edited.Id, captionOnly.Id, nameOnly.Id },
            (await store.Search("ärger")).Value!.Select(item => item.Id).ToArray());
        CollectionAssert.AreEqual(filesAndUses, stash.FilesAndUses());
        TestStash.AssertSameItem(factsBefore with { Tags = [] }, (await store.Item(edited.Id)).Value!);

        Assert.IsTrue((await store.FinishDelete([held.Id])).Single().Succeeded);
        Assert.IsTrue((await store.AddTag([nameOnly], "ärger")).Single().Succeeded);
        Assert.IsTrue((await store.AddTag([edited], "Later")).Single().Succeeded);
        var undone = await store.UndoItemFacts(removal.Receipt);
        Assert.IsTrue(undone.Outcomes.All(outcome => outcome.Succeeded), TestStash.Failures(undone.Outcomes));
        Assert.HasCount(2, undone.Changes);

        var reopened = await Reopen(stash);
        await AssertCounts(reopened, new TagCount("ärger", 3), new TagCount("keep", 2), new TagCount("Later", 1));
        TestStash.AssertSameItem(factsBefore with { Tags = ["Later", "ärger"] }, (await reopened.Item(edited.Id)).Value!);
        CollectionAssert.AreEqual(new[]
        {
            (edited.Id, "Later"), (edited.Id, "ärger"), (captionOnly.Id, "keep"), (outsideQuery.Id, "ärger"),
            (nameOnly.Id, "keep"), (nameOnly.Id, "ärger")
        }, stash.TagRows());
    }

    [TestMethod]
    [DataRow(1, 2)]
    [DataRow(2, 1)]
    [DataRow(2, 0)]
    public async Task AChangedRemovalCountReturnsTheCurrentCountWithoutWriting(int offered, int current)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a", TestStash.Bytes(1));
        var b = await stash.Import("b", TestStash.Bytes(2));
        StashItem[] items = [a, b];
        await store.AddTag(items[..offered], "Cat");
        var countShown = Value(await store.TagCounts()).Single().Items;
        if (current > offered)
        {
            await store.AddTag(items[offered..current], "cat");
        }
        else
        {
            await store.RemoveTag(items[current..offered], "Cat");
        }

        var before = stash.TagRows();
        var listingBefore = store.ListingVersion;
        var result = Value(await store.RemoveTagFromAll("Cat", countShown));

        Assert.AreEqual(new TagCount("Cat", current), result.Count);
        Assert.IsNull(result.Receipt);
        CollectionAssert.AreEqual(before, stash.TagRows());
        Assert.AreEqual(listingBefore, store.ListingVersion, "a stale offer leaves the last listing valid");
        if (current > 0)
        {
            var fresh = Value(await store.RemoveTagFromAll(result.Count.Tag, result.Count.Items));
            Assert.HasCount(current, fresh.Receipt!.Changes);
        }
        else
        {
            Assert.IsFalse((await store.RemoveTagFromAll("Cat", 0)).Succeeded);
        }

        await AssertCounts(store);
    }

    [TestMethod]
    public async Task RemovalChecksTheCountButUsesWhoActuallyCarriesTheTagAtTheWrite()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a", TestStash.Bytes(1));
        var b = await stash.Import("b", TestStash.Bytes(2));
        await store.AddTag([a], "Cat");
        var count = Value(await store.TagCounts()).Single();
        await store.AddTag([b], "cat");
        await store.RemoveTag([a], "cat");

        var removed = Value(await store.RemoveTagFromAll(count.Tag, count.Items));

        Assert.AreEqual(b.Id, removed.Receipt!.Changes.Single().Item.Id);
        await AssertCounts(store);
    }

    [TestMethod]
    public async Task RemovalUndoCannotChangeAnotherStashEvenWhenItReusesAnItemId()
    {
        using var stash = new TestStash();
        using var replacement = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a", TestStash.Bytes(1));
        await store.AddTag([a], "Cat");
        var receipt = Value(await store.RemoveTagFromAll("Cat", 1)).Receipt!;
        var second = await replacement.OpenCleanly();
        var b = await replacement.Import("b", TestStash.Bytes(2));
        Assert.AreEqual(a.Id, b.Id);
        await second.AddTag([b], "Keep");

        var undone = await second.UndoItemFacts(receipt);

        Assert.AreEqual("the change belongs to another stash", undone.Outcomes.Single().Failure?.Reason);
        Assert.IsEmpty(undone.Changes);
        CollectionAssert.AreEqual(new[] { (b.Id, "Keep") }, replacement.TagRows());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("  # ## ")]
    [DataRow("two words")]
    [DataRow("cat #CAT")]
    public async Task AWholeStashRenameRequiresOneTagWord(string text)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("item", TestStash.Bytes(1));
        await store.AddTag([item], "Cat Keep");
        var before = stash.TagRows();

        var refused = await store.RenameTag("Cat", text);

        Assert.AreEqual("Rename tag", refused.Failure?.Action);
        Assert.AreEqual("#Cat", refused.Failure?.Subject);
        StringAssert.Contains(refused.Failure?.Reason, "one");
        CollectionAssert.AreEqual(before, stash.TagRows());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ARenameRefusesWordsThatCannotBeStoredVerbatim(bool containsNull)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("picture", TestStash.Bytes(1));
        await store.AddTag([item], "Cat");

        var requested = containsNull ? "Dog\0" + new string('x', 509) : "\uD800";
        var refused = await store.RenameTag("Cat", requested);

        Assert.AreEqual("Rename tag", refused.Failure?.Action);
        Assert.AreEqual("#Cat", refused.Failure?.Subject);
        StringAssert.Contains(refused.Failure?.Reason, containsNull ? "null character" : "invalid Unicode");
        CollectionAssert.AreEqual(new[] { (item.Id, "Cat") }, stash.TagRows());
        Assert.AreEqual(new TagRename("😺", 1, false), Value(await store.RenameTag("Cat", "😺")));
        CollectionAssert.AreEqual(new[] { (item.Id, "😺") }, stash.TagRows());
    }

    [TestMethod]
    public async Task AWholeStashRenameRequiresALiveCarrier()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("Cat", TestStash.Bytes(1));
        var held = await stash.Import("held", TestStash.Bytes(2));
        await store.AddTag([item], "Keep");
        await store.AddTag([held], "Cat");
        var deleted = (await store.Delete([held.Id])).Single();
        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        var before = stash.TagRows();

        var refused = await store.RenameTag("Cat", "Dog");

        Assert.AreEqual("Rename tag", refused.Failure?.Action);
        Assert.AreEqual("#Cat", refused.Failure?.Subject);
        StringAssert.Contains(refused.Failure?.Reason, "no item carries");
        CollectionAssert.AreEqual(before, stash.TagRows());
    }

    [TestMethod]
    public async Task RenameToANewWordAndMergeIntoAnExistingTagKeepTheExistingSpellingAndUnion()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a", TestStash.Bytes(1));
        var b = await stash.Import("b", TestStash.Bytes(2));
        var c = await stash.Import("c", TestStash.Bytes(3));
        await store.AddTag([a, b], "Cat");
        await store.AddTag([b, c], "PÉt");
        await store.AddTag([a], "Keep");

        Assert.AreEqual(new TagRename("Dog", 2, false), Value(await store.RenameTag("cat", "  ##Dog  ")));
        await AssertCounts(store, new TagCount("Dog", 2), new TagCount("PÉt", 2), new TagCount("Keep", 1));
        Assert.AreEqual(new TagRename("PÉt", 2, true), Value(await store.RenameTag("dog", "pét")));

        var reopened = await Reopen(stash);
        await AssertCounts(reopened, new TagCount("PÉt", 3), new TagCount("Keep", 1));
        CollectionAssert.AreEqual(new[] { (a.Id, "Keep"), (a.Id, "PÉt"), (b.Id, "PÉt"), (c.Id, "PÉt") }, stash.TagRows());
    }

    [TestMethod]
    public async Task RenameUsesTheSpellingKeptByDeleteWithoutCountingItAsALiveDestination()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var live = await stash.Import("live", TestStash.Bytes(1));
        var held = await stash.Import("held", TestStash.Bytes(2));
        await store.AddTag([live], "Cat");
        await store.AddTag([held], "PÉt");
        var deleted = (await store.Delete([held.Id])).Single();
        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());

        Assert.AreEqual(new TagRename("PÉt", 1, false), Value(await store.RenameTag("Cat", "pét")));
        Assert.AreEqual(new TagRename("PÉt", 0, false), Value(await store.RenameTag("pét", "PÉt")));
        await AssertCounts(store, new TagCount("PÉt", 1));
        Assert.IsTrue((await store.UndoDelete([deleted.Value!])).Single().Succeeded);
        await AssertCounts(store, new TagCount("PÉt", 2));
    }

    [TestMethod]
    public async Task WholeStashWritesRespectListingEligibilityAndLeavePendingFileStepsAlone()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var placed = await stash.Import("placed", TestStash.Bytes(1));
        var waiting = await stash.Import("waiting", TestStash.Bytes(2));
        await store.AddTag([placed, waiting], "Cat Keep");
        var staged = await stash.Stage("waiting.png", TestStash.Bytes(40));
        var placedSteps = PendingStep.Encode([PendingStep.Place("gone.png", placed.FileName)]);
        var waitingSteps = PendingStep.Encode([PendingStep.Place(Path.GetFileName(staged), waiting.FileName)]);
        const string recordStep = "UPDATE items SET pending_steps = $steps WHERE id = $id";
        stash.SqlWhileOpen(recordStep, ("$id", placed.Id), ("$steps", placedSteps));
        stash.SqlWhileOpen(recordStep, ("$id", waiting.Id), ("$steps", waitingSteps));
        File.Move(waiting.FilePath, stash.Outside("waiting elsewhere.png"));
        CollectionAssert.AreEqual(new[] { placed.Id }, (await stash.All()).Select(item => item.Id).ToArray());

        Assert.AreEqual(new TagRename("Dog", 1, false), Value(await store.RenameTag("Cat", "Dog")));
        var removal = Value(await store.RemoveTagFromAll("Keep", 1));

        Assert.AreEqual(new TagCount("Keep", 1), removal.Count);
        Assert.AreEqual(placed.Id, removal.Receipt!.Changes.Single().Item.Id);
        CollectionAssert.AreEqual(new[] { (placed.Id, "Dog"), (waiting.Id, "Cat"), (waiting.Id, "Keep") }, stash.TagRows());
        CollectionAssert.AreEqual(new[] { placedSteps, waitingSteps },
            stash.SqlQueryWhileOpen("SELECT pending_steps FROM items ORDER BY id", row => row.GetString(0)));
        Assert.IsTrue(File.Exists(staged));
        Assert.IsTrue(File.Exists(placed.FilePath));
    }

    [TestMethod]
    [DataRow("Friend", "Ärger", null)]
    [DataRow("friend", "Ärger", "Friend")]
    [DataRow("ärger", "ärger", null)]
    public async Task RenameAndDeleteUndoPreserveTheHeldMembershipWithItsCurrentSpelling(
        string text, string restoredTag, string? existingDestination)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var live = await stash.Import("live", TestStash.Bytes(1));
        var held = await stash.Import("held", TestStash.Bytes(2));
        await store.AddTag([live, held], "Ärger");
        await store.AddTag([held], "Other");
        if (existingDestination is not null)
        {
            await store.AddTag([live], existingDestination);
        }

        var deleted = (await store.Delete([held.Id])).Single();
        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());

        var listedTag = existingDestination ?? text;
        Assert.AreEqual(new TagRename(listedTag, 1, existingDestination is not null), Value(await store.RenameTag("ÄRGER", text)));
        await AssertCounts(store, new TagCount(listedTag, 1));
        var restored = (await store.UndoDelete([deleted.Value!])).Single();

        Assert.IsTrue(restored.Succeeded, restored.Failure?.ToString());
        var reopened = await Reopen(stash);
        CollectionAssert.AreEquivalent(new[] { (live.Id, listedTag), (held.Id, restoredTag), (held.Id, "Other") }, stash.TagRows());
        Assert.HasCount(2, (await reopened.All()).Value!);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task CorrectingOlderSpellingsAdvancesTheVersionAndKeepsOneMembership(bool heldHasBoth, bool liveHasBoth)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var live = await stash.Import("live", TestStash.Bytes(1));
        var held = await stash.Import("held", TestStash.Bytes(2));
        await store.AddTag([live], liveHasBoth ? "a\uFFFD A\uD800" : "a\uD800");
        await store.AddTag([held], heldHasBoth ? "A\uD800 a\uFFFD" : "A\uD800");
        Assert.HasCount(2 + (heldHasBoth ? 1 : 0) + (liveHasBoth ? 1 : 0), stash.TagRows());
        var deleted = (await store.Delete([held.Id])).Single();
        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        var before = store.ListingVersion;

        Assert.AreEqual(new TagRename("a\uFFFD", liveHasBoth ? 1 : 0, false), Value(await store.RenameTag("a\uFFFD", "a\uFFFD")));

        CollectionAssert.AreEqual(new[] { (live.Id, "a\uFFFD"), (held.Id, "a\uFFFD") }, stash.TagRows());
        Assert.AreNotEqual(before, store.ListingVersion);
    }

    [TestMethod]
    public async Task MergingIntoAnOlderTagKeepsOneSpellingOnListedAndRestoredItems()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var held = await stash.Import("held", TestStash.Bytes(1));
        var destination = await stash.Import("destination", TestStash.Bytes(2));
        var source = await stash.Import("source", TestStash.Bytes(3));
        // Older writes compared the input before SQLite replaced its lone surrogate.
        await store.AddTag([held], "A\uD800");
        await store.AddTag([destination, source], "a\uD800");
        await store.AddTag([source], "Cat Keep");
        var deleted = Value((await store.Delete([held.Id])).Single());
        await AssertCounts(store, new TagCount("a\uFFFD", 2), new TagCount("Cat", 1), new TagCount("Keep", 1));

        Assert.AreEqual(new TagRename("a\uFFFD", 1, true), Value(await store.RenameTag("Cat", "A\uFFFD")));
        await AssertCounts(store, new TagCount("a\uFFFD", 2), new TagCount("Keep", 1));
        Assert.IsTrue((await store.UndoDelete([deleted])).Single().Succeeded);

        var reopened = await Reopen(stash);
        await AssertCounts(reopened, new TagCount("a\uFFFD", 3), new TagCount("Keep", 1));
        CollectionAssert.AreEquivalent(
            new[] { (held.Id, "a\uFFFD"), (destination.Id, "a\uFFFD"), (source.Id, "a\uFFFD"), (source.Id, "Keep") },
            stash.TagRows());
    }

    [TestMethod]
    public async Task DeleteUndoLeavesOffARecasedTagThatImportUndoTookBackWhileTheItemWasDeleted()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(1);
        var imported = await stash.Import("duplicate", bytes);
        var live = await stash.Import("live", TestStash.Bytes(2));
        await store.AddTag([imported], "Keep");
        await store.AddTag([live], "Ärger");
        var staged = await stash.Stage("duplicate.png", bytes);
        var duplicate = Value(await store.CommitImport(TestStash.Candidate(staged, "duplicate", bytes) with { Tags = ["Ärger"] }));
        Assert.IsTrue(duplicate.AlreadyInStash);
        CollectionAssert.AreEqual(new[] { "Ärger" }, duplicate.GainedTags.ToArray());
        var deleted = Value((await store.Delete([imported.Id])).Single());

        Assert.AreEqual(new TagRename("ärger", 1, false), Value(await store.RenameTag("Ärger", "ärger")));
        var undone = await store.UndoImport([], [(imported.Id, duplicate.GainedTags)]);
        Assert.IsTrue(undone.Single().Succeeded, TestStash.Failures(undone));
        Assert.IsTrue((await store.UndoDelete([deleted])).Single().Succeeded);

        var reopened = await Reopen(stash);
        await AssertCounts(reopened, new TagCount("Keep", 1), new TagCount("ärger", 1));
        CollectionAssert.AreEquivalent(new[] { (imported.Id, "Keep"), (live.Id, "ärger") }, stash.TagRows());
    }

    [TestMethod]
    [DataRow(false, "PÉt", true)]
    [DataRow(true, "PÉt", true)]
    [DataRow(false, "ärger", false)]
    public async Task OlderImportAndOrganisingReceiptsKeepTheOriginalWord(bool alreadyCarried, string destination, bool keepsDestination)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(1);
        var imported = await stash.Import("duplicate", bytes);
        var organised = await stash.Import("organised", TestStash.Bytes(2));
        var other = await stash.Import("other", TestStash.Bytes(3));
        await store.AddTag([other], "PÉt");
        if (alreadyCarried)
        {
            await store.AddTag([imported, organised], "PÉt");
        }

        var staged = await stash.Stage("duplicate.png", bytes);
        var duplicate = Value(await store.CommitImport(TestStash.Candidate(staged, "duplicate", bytes) with { Tags = ["Ärger"] }));
        Assert.IsTrue(duplicate.AlreadyInStash);
        CollectionAssert.AreEqual(new[] { "Ärger" }, duplicate.GainedTags.ToArray());
        var gain = await store.AddTagWithChanges([organised], "Ärger");
        Assert.HasCount(1, gain.Changes);
        Assert.AreEqual(destination, Value(await store.RenameTag("ÄRGER", destination)).Tag);

        var importUndo = await store.UndoImport([], [(imported.Id, duplicate.GainedTags)]);
        var organisingUndo = await store.UndoItemFacts(gain);

        Assert.IsTrue(importUndo.Single().Succeeded, TestStash.Failures(importUndo));
        Assert.IsTrue(organisingUndo.Outcomes.Single().Succeeded, TestStash.Failures(organisingUndo.Outcomes));
        await AssertCounts(store, new TagCount("PÉt", keepsDestination ? 3 : 1));
        Assert.HasCount(keepsDestination ? 0 : 1, organisingUndo.Changes);
    }

    [TestMethod]
    [DataRow("remove")]
    [DataRow("rename")]
    [DataRow("recase")]
    public async Task ARefusalAfterTheFirstMembershipWriteRollsTheWholeChangeBack(string operation)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a", TestStash.Bytes(1));
        var b = await stash.Import("b", TestStash.Bytes(2));
        var other = await stash.Import("other", TestStash.Bytes(3));
        await store.AddTag([a, b], "Ärger");
        await store.AddTag([b, other], "PÉt");
        await store.AddTag([other], "Keep");
        var before = stash.TagRows();
        // FAIL leaves preceding changes in the statement intact. Only rolling back the transaction restores them.
        // The count predicate guarantees a source membership has already changed regardless of statement order.
        var statement = operation == "recase" ? "UPDATE OF tag" : "DELETE";
        stash.SqlWhileOpen($"""
            CREATE TRIGGER refuse_later_tag BEFORE {statement} ON tags
            WHEN OLD.tag = 'Ärger' AND (SELECT count(*) FROM tags WHERE tag = 'Ärger') = 1
            BEGIN SELECT RAISE(FAIL, 'refused after the first change'); END
            """);

        Outcome result = operation switch
        {
            "remove" => await store.RemoveTagFromAll("ärger", 2),
            "rename" => await store.RenameTag("ärger", "pét"),
            _ => await store.RenameTag("ÄRGER", "ärger")
        };

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(operation == "remove" ? "Remove tag from all items" : "Rename tag", result.Failure.Action);
        StringAssert.Contains(result.Failure.Reason, "refused after the first change");
        CollectionAssert.AreEqual(before, stash.TagRows());
        await Reopen(stash);
        CollectionAssert.AreEqual(before, stash.TagRows());
    }

    private static T Value<T>(Outcome<T> result)
    {
        Assert.IsTrue(result.Succeeded, result.Failure?.ToString());
        return result.Value!;
    }

    private static async Task AssertCounts(StashStore store, params TagCount[] counts) =>
        CollectionAssert.AreEqual(counts, Value(await store.TagCounts()).ToArray());

    private static async Task<StashStore> Reopen(TestStash stash)
    {
        stash.Close();
        return await stash.OpenCleanly();
    }
}
