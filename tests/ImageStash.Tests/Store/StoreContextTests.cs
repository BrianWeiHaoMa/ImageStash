using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// The store's count of its commits, which every item it answers carries (State: Every change): raised once by each
/// change that commits, whatever it changes, a transaction retried before it commits included, and never by a read or
/// by a change the store refused.
/// </summary>
[TestClass]
public sealed class StoreContextTests
{
    [TestMethod]
    public async Task EveryChangeThatCommitsRaisesTheCountItemsCarryAndNoReadDoes()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var witness = await stash.Import("witness");
        var gone = await stash.Import("gone");
        StashItem item = null!;
        DeletedItem deleted = null!;
        (string Change, Func<Task<bool>> Run)[] changes =
        [
            ("an import", async () => (item = await stash.Import("item")) is not null),
            ("a tag", async () => (await store.AddTag([item], "mood")).Single().Succeeded),
            ("a tag taken off", async () => (await store.RemoveTag([item], "mood")).Single().Succeeded),
            ("the favorite flag", async () => (await store.SetFavorite([item], true)).Single().Succeeded),
            ("a use", async () => (await store.RecordUses([item], stash.Clock.Now)).Succeeded),
            ("a rename", async () => (await store.Rename(item, "renamed")).Succeeded),
            ("a delete", async () => (deleted = (await store.Delete([item.Id])).Single().Value!) is not null),
            ("its undo", async () => (await store.UndoDelete([deleted])).Single().Succeeded),
            ("a second delete", async () => (deleted = (await store.Delete([item.Id])).Single().Value!) is not null),
            ("its finish", async () => (await store.FinishDelete([item.Id])).Single().Succeeded),
            ("an import's undo", async () => (await store.Remove([gone.Id])).Single().Succeeded),
        ];
        (string Read, Func<Task<bool>> Run)[] reads =
        [
            ("All", async () => (await store.All()).Succeeded),
            ("Recent", async () => (await store.Recent()).Succeeded),
            ("a search", async () => (await store.Search("witness")).Succeeded),
            ("Favorites", async () => (await store.Favorites()).Succeeded),
            ("an item", async () => (await store.Item(witness.Id)).Succeeded),
            ("the tag counts", async () => (await store.TagCounts()).Succeeded),
            ("a use history", async () => (await store.UseHistory(witness.Id)).Succeeded),
        ];

        foreach (var (change, run) in changes)
        {
            var before = await Commits(store, witness);
            Assert.IsTrue(await run(), change);
            Assert.IsGreaterThan(before, await Commits(store, witness), change);
        }

        foreach (var (read, run) in reads)
        {
            var before = await Commits(store, witness);
            Assert.IsTrue(await run(), read);
            Assert.AreEqual(before, await Commits(store, witness), read);
        }
    }

    [TestMethod]
    public async Task AChangeTheHeldStoreRefusesLeavesTheCountAndOneRetriedUntilItCommitsRaisesItOnce()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(5) };
        var store = await stash.OpenCleanly();
        var witness = await stash.Import("witness");
        var before = await Commits(store, witness);

        using var reader = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={stash.Folder.StoreFile};Pooling=False");
        reader.Open();
        using var hold = reader.CreateCommand();
        hold.CommandText = "BEGIN; SELECT COUNT(*) FROM items";
        hold.ExecuteScalar();
        var tagging = store.AddTag([witness], "retried");
        try
        {
            // A read lock lets the insert in and keeps the commit out past SQLite's own wait, so the transaction runs again.
            await Task.Delay(2500);
            Assert.IsFalse(tagging.IsCompleted, "the reader still holds the commit");
        }
        finally
        {
            hold.CommandText = "ROLLBACK";
            hold.ExecuteNonQuery();
        }

        Assert.IsTrue((await tagging).Single().Succeeded);
        var retried = await Commits(store, witness);
        Assert.AreEqual(before + 1, retried, "once, though its transaction ran more than once");

        stash.Retry = TestStash.ShortRetry;
        await stash.Reopen();
        store = stash.Store!;
        var reopened = await Commits(store, witness);
        using (stash.HoldStore())
        {
            Assert.IsFalse((await store.AddTag([witness], "refused")).Single().Succeeded);
        }

        Assert.AreEqual(reopened, await Commits(store, witness), "a change the store refused committed nothing");
        CollectionAssert.AreEqual(new[] { "retried" }, (await store.Item(witness.Id)).Value!.Tags.ToList());
    }

    [TestMethod]
    public async Task AnItemReadAgainAfterAnUnrelatedCommitCarriesTheHigherCountAndIsStillTheSameItem()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var witness = await stash.Import("witness");
        var other = await stash.Import("other");
        var first = (await stash.All()).Single(item => item.Id == witness.Id);

        await store.SetFavorite([other], true);
        var again = (await stash.All()).Single(item => item.Id == witness.Id);

        Assert.AreEqual(other.CommitsWhenRead, first.CommitsWhenRead, "nothing committed between the import and the read");
        Assert.IsGreaterThan(first.CommitsWhenRead, again.CommitsWhenRead);
        TestStash.AssertSameItem(first, again);
    }

    /// <summary>The store's count as an item read now carries it; a read, so it raises nothing.</summary>
    private static async Task<long> Commits(StashStore store, StashItem witness) => (await store.Item(witness.Id)).Value!.CommitsWhenRead;
}
