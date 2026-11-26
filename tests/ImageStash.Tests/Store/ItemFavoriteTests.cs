using System.Diagnostics;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// The favorite flag (F9, G17): set as asked and never toggled by the store, the whole batch one transaction with one
/// outcome per identity (State, G28, G35), and 5,000 items in one (E4).
/// </summary>
[TestClass]
public sealed class ItemFavoriteTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task TheFlagIsSetAsAskedAndSettingItAgainChangesNothing()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        var other = await stash.Import("other");

        var set = await store.SetFavorite([a, b], true);
        var again = await store.SetFavorite([a], true);
        var flags = Flags(stash);
        var cleared = await store.SetFavorite([a, b], false);

        Assert.IsTrue(set.Concat(again).Concat(cleared).All(o => o.Succeeded), TestStash.Failures([.. set, .. again, .. cleared]));
        CollectionAssert.AreEqual(new[] { (a.Id, true), (b.Id, true), (other.Id, false) }, flags);
        CollectionAssert.AreEqual(new[] { (a.Id, false), (b.Id, false), (other.Id, false) }, Flags(stash));
        Assert.IsEmpty((await store.Favorites()).Value!);
    }

    [TestMethod]
    public async Task AnItemNoLongerInTheStashIsRefusedByNameAndTheOthersAreChanged()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var (gone, marked, a) = await stash.GoneMarkedAndListed();

        var set = await store.SetFavorite([gone, marked, a], true);
        var setFlags = Flags(stash);
        var cleared = await store.SetFavorite([gone, marked, a], false);

        Assert.AreEqual(Failure.Refusal("Add to Favorites", "gone", "it is no longer in the stash"), set[0].Failure);
        Assert.AreEqual(Failure.Refusal("Add to Favorites", "marked", "it is no longer in the stash"), set[1].Failure);
        Assert.IsTrue(set[2].Succeeded, set[2].Failure?.ToString());
        CollectionAssert.AreEqual(new[] { (marked.Id, false), (a.Id, true) }, setFlags);
        Assert.AreEqual(Failure.Refusal("Remove from Favorites", "gone", "it is no longer in the stash"), cleared[0].Failure);
        Assert.AreEqual(Failure.Refusal("Remove from Favorites", "marked", "it is no longer in the stash"), cleared[1].Failure);
        Assert.IsTrue(cleared[2].Succeeded, cleared[2].Failure?.ToString());
        CollectionAssert.AreEqual(new[] { (marked.Id, false), (a.Id, false) }, Flags(stash));
    }

    [TestMethod]
    public async Task SettingFavoritesReportsOnlyFlagsThatChangedAndUndoRestoresThoseFlags()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a", TestStash.Bytes(1));
        var b = await stash.Import("b", TestStash.Bytes(2));
        var c = await stash.Import("c", TestStash.Bytes(3));
        await store.SetFavorite([a], true);

        var set = await store.SetFavoriteWithChanges([a, b, b], true);
        var unchanged = await store.SetFavoriteWithChanges([b, a], true);

        Assert.HasCount(3, set.Outcomes);
        Assert.IsTrue(set.Outcomes.Concat(unchanged.Outcomes).All(o => o.Succeeded));
        Assert.IsEmpty(unchanged.Changes);
        var changed = set.Changes.Single();
        Assert.AreEqual(b.Id, changed.Item.Id);
        Assert.IsFalse(changed.PreviousFavorite);
        CollectionAssert.AreEqual(new[] { (a.Id, true), (b.Id, true), (c.Id, false) }, Flags(stash));
        Assert.IsEmpty((await store.UndoItemFacts(unchanged)).Changes);
        await store.SetFavorite([c], true);

        var undone = await store.UndoItemFacts(set);

        Assert.IsTrue(undone.Outcomes.Single().Succeeded);
        Assert.IsTrue(undone.Changes.Single().PreviousFavorite);
        CollectionAssert.AreEqual(new[] { (a.Id, true), (b.Id, false), (c.Id, true) }, Flags(stash));
        stash.Close();
        store = await stash.OpenCleanly();
        CollectionAssert.AreEquivalent(new[] { a.Id, c.Id }, (await store.Favorites()).Value!.Select(item => item.Id).ToList());
    }

    [TestMethod]
    public async Task AWrongFavoriteTargetIsNamedAndUndoUsesTheFlagAtTheWrite()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var (gone, marked, live) = await stash.GoneMarkedAndListed();
        var renamed = await store.Rename(live, "renamed");
        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        await store.SetFavorite([live], true);

        var cleared = await store.SetFavoriteWithChanges([gone, marked, live, live], false);

        Assert.HasCount(4, cleared.Outcomes);
        Assert.AreEqual(Failure.Refusal("Remove from Favorites", "gone", "it is no longer in the stash"), cleared.Outcomes[0].Failure);
        Assert.AreEqual(Failure.Refusal("Remove from Favorites", "marked", "it is no longer in the stash"), cleared.Outcomes[1].Failure);
        Assert.IsTrue(cleared.Outcomes.Skip(2).All(o => o.Succeeded));
        Assert.AreEqual(live.Id, cleared.Changes.Single().Item.Id);
        Assert.AreEqual("renamed", cleared.Changes.Single().Item.Name);
        Assert.IsTrue(cleared.Changes.Single().PreviousFavorite);
        CollectionAssert.AreEqual(new[] { (marked.Id, false), (live.Id, false) }, Flags(stash));
        var renamedAgain = await store.Rename(renamed.Value!, "renamed again");
        Assert.IsTrue(renamedAgain.Succeeded, renamedAgain.Failure?.ToString());

        var undone = await store.UndoItemFacts(cleared);

        Assert.IsTrue(undone.Outcomes.Single().Succeeded);
        var reversed = undone.Changes.Single();
        Assert.AreEqual((live.Id, "renamed again"), (reversed.Item.Id, reversed.Item.Name));
        CollectionAssert.AreEqual(new[] { (marked.Id, false), (live.Id, true) }, Flags(stash));
    }

    [TestMethod]
    public async Task RefusingTheLastFavoriteWriteDuringUndoRollsBackEarlierWrites()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a", TestStash.Bytes(1));
        var b = await stash.Import("b", TestStash.Bytes(2));
        var other = await stash.Import("other", TestStash.Bytes(3));
        var set = await store.SetFavoriteWithChanges([a, b], true);
        Assert.HasCount(2, set.Changes);
        stash.SqlWhileOpen($"CREATE TRIGGER refuse_b BEFORE UPDATE OF favorite ON items WHEN NEW.id = {b.Id} BEGIN SELECT RAISE(ABORT, 'refused'); END");

        var refused = await store.UndoItemFacts(set);

        Assert.HasCount(2, refused.Outcomes);
        Assert.IsTrue(refused.Outcomes.All(o => o.Failure is { Action: "Undo adding to Favorites", Subject: StashFolder.StoreFileName } failure &&
            failure.Reason.Contains("refused", StringComparison.Ordinal)), TestStash.Failures(refused.Outcomes));
        Assert.IsEmpty(refused.Changes);
        CollectionAssert.AreEqual(new[] { (a.Id, true), (b.Id, true), (other.Id, false) }, Flags(stash));
        stash.SqlWhileOpen("DROP TRIGGER refuse_b");

        var undone = await store.UndoItemFacts(set);

        Assert.HasCount(2, undone.Changes);
        CollectionAssert.AreEqual(new[] { (a.Id, false), (b.Id, false), (other.Id, false) }, Flags(stash));
    }

    [TestMethod]
    public async Task FiveThousandItemsAreSetInOneTransaction()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        stash.Close();
        stash.AddPictures(5000);
        var store = await stash.OpenCleanly();
        var items = await stash.All();
        // The last item the batch reaches refuses, after every other one was changed.
        stash.SqlWhileOpen($"CREATE TRIGGER refuse_last BEFORE UPDATE OF favorite ON items WHEN NEW.id = {items[^1].Id} BEGIN SELECT RAISE(ABORT, 'refused for the check'); END");

        var publicRefused = await store.SetFavorite(items, true);
        var refused = await store.SetFavoriteWithChanges(items, true);
        var favoritesAfterRefusal = (await store.Favorites()).Value!.Count;
        stash.SqlWhileOpen("DROP TRIGGER refuse_last");
        var clock = Stopwatch.StartNew();
        var set = await store.SetFavoriteWithChanges(items, true);
        clock.Stop();

        TestContext.WriteLine($"SetFavorite over 5,000 items took {clock.Elapsed.TotalMilliseconds:F0} ms");
        Assert.HasCount(5000, refused.Outcomes);
        Assert.IsTrue(refused.Outcomes.All(o => o.Failure is { Action: "Add to Favorites", Subject: StashFolder.StoreFileName } failure &&
            failure.Reason.Contains("refused for the check", StringComparison.Ordinal)), "the last item's refusal fails the whole batch");
        CollectionAssert.AreEqual(refused.Outcomes.Select(o => o.Failure).ToList(), publicRefused.Select(o => o.Failure).ToList());
        Assert.IsEmpty(refused.Changes);
        Assert.AreEqual(0, favoritesAfterRefusal, "no item of the batch was changed by its own transaction");
        Assert.HasCount(5000, set.Outcomes);
        Assert.IsTrue(set.Outcomes.All(o => o.Succeeded), TestStash.Failures(set.Outcomes));
        Assert.HasCount(5000, set.Changes);
        Assert.HasCount(5000, (await store.Favorites()).Value!);
        // Generous for the hosted runner under the parallel suite (once 1.5 s; tens of ms on a PC): a commit per item
        // in the store's rollback journal would still take several times longer.
        Assert.IsLessThan(5000, clock.Elapsed.TotalMilliseconds);
    }

    private static List<(long Id, bool Favorite)> Flags(TestStash stash) =>
        stash.SqlQueryWhileOpen("SELECT id, favorite FROM items ORDER BY id", r => (r.GetInt64(0), r.GetBoolean(1)));
}
