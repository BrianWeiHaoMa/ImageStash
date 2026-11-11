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

        Assert.AreEqual(new Failure("Add to Favorites", "gone", "it is no longer in the stash"), set[0].Failure);
        Assert.AreEqual(new Failure("Add to Favorites", "marked", "it is no longer in the stash"), set[1].Failure);
        Assert.IsTrue(set[2].Succeeded, set[2].Failure?.ToString());
        CollectionAssert.AreEqual(new[] { (marked.Id, false), (a.Id, true) }, setFlags);
        Assert.AreEqual(new Failure("Remove from Favorites", "gone", "it is no longer in the stash"), cleared[0].Failure);
        Assert.AreEqual(new Failure("Remove from Favorites", "marked", "it is no longer in the stash"), cleared[1].Failure);
        Assert.IsTrue(cleared[2].Succeeded, cleared[2].Failure?.ToString());
        CollectionAssert.AreEqual(new[] { (marked.Id, false), (a.Id, false) }, Flags(stash));
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

        var refused = await store.SetFavorite(items, true);
        var favoritesAfterRefusal = (await store.Favorites()).Value!.Count;
        stash.SqlWhileOpen("DROP TRIGGER refuse_last");
        var clock = Stopwatch.StartNew();
        var set = await store.SetFavorite(items, true);
        clock.Stop();

        TestContext.WriteLine($"SetFavorite over 5,000 items took {clock.Elapsed.TotalMilliseconds:F0} ms");
        Assert.IsTrue(refused.All(o => !o.Succeeded), "the last item's refusal fails the whole batch");
        Assert.AreEqual(0, favoritesAfterRefusal, "no item of the batch was changed by its own transaction");
        Assert.IsTrue(set.All(o => o.Succeeded), TestStash.Failures(set));
        Assert.HasCount(5000, (await store.Favorites()).Value!);
        // Generous for the hosted runner under the parallel suite (once 1.5 s; tens of ms on a PC): a commit per item
        // in the store's rollback journal would still take several times longer.
        Assert.IsLessThan(5000, clock.Elapsed.TotalMilliseconds);
    }

    private static List<(long Id, bool Favorite)> Flags(TestStash stash) =>
        stash.SqlQueryWhileOpen("SELECT id, favorite FROM items ORDER BY id", r => (r.GetInt64(0), r.GetBoolean(1)));
}
