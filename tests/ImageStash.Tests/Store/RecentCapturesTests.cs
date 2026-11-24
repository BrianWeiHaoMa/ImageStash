using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// G4's Recent with captures: an item a screenshot, a recording or Stash clipboard image added is listed while no use of
/// it is recorded, placed by when it was added among the items placed by their latest use, and once used by that use
/// alone; an import is not, whatever its name; a capture is still no use (G10, G19) and search places it among the
/// never-used (G4); its place survives a rename, a Delete's Undo, a reopening, Export and Import stash… and Move… (G36,
/// G30), while its notification's Undo and a Delete take it out (G16, G18); a Save as new item's copy is no capture
/// (G26); and Recent stays a fixed number of statements over the whole stash (E4).
/// </summary>
[TestClass]
public sealed class RecentCapturesTests
{
    private readonly List<Failure> failures = [];

    [TestMethod]
    public async Task AnUnusedCaptureIsPlacedByWhenItWasAddedAmongItemsPlacedByTheirLatestUseAndAUsedOneOnceByItsUse()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var start = stash.Clock.Now;
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        await stash.Import("never used");
        await store.RecordUses([a], start.AddSeconds(1));
        stash.Clock.Now = start.AddSeconds(2);
        var used = await stash.Capture("used capture");
        stash.Clock.Now = start.AddSeconds(3);
        var unused = await stash.Capture("unused capture");
        await store.RecordUses([b], start.AddSeconds(4));
        await store.RecordUses([used], start.AddSeconds(5));

        var recent = await store.Recent();

        CollectionAssert.AreEqual(new[] { used.Id, b.Id, unused.Id, a.Id }, Ids(recent),
            "G4: the used capture once, by its use; the unused one by when it was added, between the uses either side of it; the import never used left out");
    }

    [TestMethod]
    public async Task EqualTimesPutTheLaterIdentityFirstWhetherAUseOrAnAddingPlacesIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var capture = await stash.Capture("capture");
        var e = await stash.Import("e");
        await store.RecordUses([a, e], stash.Clock.Now);

        CollectionAssert.AreEqual(new[] { e.Id, capture.Id, a.Id }, Ids(await store.Recent()));
    }

    [TestMethod]
    public async Task AnImportNamedLikeAScreenshotIsNotListedUntilUsedAndACaptureRenamedStays()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var imported = await stash.Import("Screenshot 2026-10-16 17.30.00");
        var capture = await stash.Capture("Screenshot 2026-10-16 17.30.01");
        Assert.IsTrue((await store.Rename(capture, "the audacity")).Succeeded);

        CollectionAssert.AreEqual(new[] { capture.Id }, Ids(await store.Recent()), "what added an item decides, never its name (G4, F12)");

        stash.Clock.Now += TimeSpan.FromSeconds(1);
        await store.RecordUses([imported], stash.Clock.Now);
        CollectionAssert.AreEqual(new[] { imported.Id, capture.Id }, Ids(await store.Recent()), "the import once used");
    }

    [TestMethod]
    public async Task ACaptureItsNotificationsUndoRemovedOrDeletedIsNotListedAndADeletesUndoListsItAgainWhereItWas()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var start = stash.Clock.Now;
        var a = await stash.Import("a");
        stash.Clock.Now = start.AddSeconds(1);
        var removed = await stash.Capture("removed");
        await store.RecordUses([a], start.AddSeconds(2));
        stash.Clock.Now = start.AddSeconds(3);
        var deleted = await stash.Capture("deleted");
        stash.Clock.Now = start.AddSeconds(4);
        var later = await stash.Capture("later");
        CollectionAssert.AreEqual(new[] { later.Id, deleted.Id, a.Id, removed.Id }, Ids(await store.Recent()));

        Assert.IsTrue((await store.Remove([removed.Id])).Single().Succeeded);
        var held = (await store.Delete([deleted.Id])).Single();
        Assert.IsTrue(held.Succeeded, held.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { later.Id, a.Id }, Ids(await store.Recent()), "neither the removed capture nor the deleted one (G16, G18)");

        // Later than every addition, so an Undo that took the time of its own would place it first.
        stash.Clock.Now = start.AddSeconds(10);
        Assert.IsTrue((await store.UndoDelete([held.Value!])).Single().Succeeded);
        CollectionAssert.AreEqual(new[] { later.Id, deleted.Id, a.Id }, Ids(await store.Recent()), "the deleted capture back where it was (G18)");
    }

    [TestMethod]
    public async Task ACaptureIsNoUseAndSearchListsItAmongTheNeverUsedNewestAddedFirst()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var start = stash.Clock.Now;
        var older = await stash.Import("x older");
        var used = await stash.Import("x used");
        stash.Clock.Now = start.AddSeconds(1);
        var capture = await stash.Capture("x shot");
        stash.Clock.Now = start.AddSeconds(2);
        var newer = await stash.Import("x newer");
        await store.RecordUses([used], start.AddSeconds(3));

        Assert.AreEqual(new UseHistory(0, null), (await store.UseHistory(capture.Id)).Value, "a capture is no use (G10, G19)");
        CollectionAssert.AreEqual(new[] { used.Id, newer.Id, capture.Id, older.Id }, Ids(await store.Search("x")),
            "G4: matches by their latest use, then the never-used newest-added first, the capture among them");
    }

    [TestMethod]
    public async Task AnUnusedCaptureIsListedAgainOnceTheStashIsOpenedAgain()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        await store.RecordUses([a], stash.Clock.Now);
        stash.Clock.Now += TimeSpan.FromSeconds(1);
        var capture = await stash.Capture("capture");

        var reopened = await stash.Reopen();

        Assert.IsNotNull(reopened.Store, string.Join("; ", reopened.Failures));
        CollectionAssert.AreEqual(new[] { capture.Id, a.Id }, Ids(await reopened.Store.Recent()), "what added it is in the store (G4, G28)");
    }

    [TestMethod]
    public async Task AnUnusedCaptureIsStillListedAfterExportAndImportStash()
    {
        using var source = new TestStash();
        var sourceStore = await source.OpenCleanly();
        var a = await source.Import("a");
        await sourceStore.RecordUses([a], source.Clock.Now);
        source.Clock.Now += TimeSpan.FromSeconds(1);
        var capture = await source.Capture("capture");
        await source.Import("imported");
        var archive = await ExportedStash.Export(source, MovingStash.Keeper(source, failures));
        using var target = new TestStash();
        var store = await target.OpenCleanly();

        var imported = await store.Import(MovingStash.Keeper(target, failures), archive);

        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        target.Close();
        var reopened = await target.Open();
        Assert.IsNotNull(reopened.Store, string.Join("; ", reopened.Failures));
        CollectionAssert.AreEqual(new[] { capture.Id, a.Id }, Ids(await reopened.Store.Recent()), "G36: found as on the PC that exported it");
        Assert.IsEmpty(failures);
    }

    [TestMethod]
    public async Task AnExportAnEarlierBuildWroteImportsWithItsScreenshotLeftOutOfRecentUntilUsed()
    {
        using var source = new TestStash();
        var sourceStore = await source.OpenCleanly();
        var a = await source.Import("a");
        await sourceStore.RecordUses([a], source.Clock.Now);
        var shot = await source.Capture("Screenshot 2026-10-16 17.30.00");
        var written = await ExportedStash.Export(source, MovingStash.Keeper(source, failures));
        // As a build at version 3 wrote it: no column records what added an item.
        var archive = ExportedStash.WithStore(written, source.Outside("earlier.zip"), "ALTER TABLE items DROP COLUMN added_by_capture; PRAGMA user_version = 3");
        using var target = new TestStash();
        var store = await target.OpenCleanly();

        var imported = await store.Import(MovingStash.Keeper(target, failures), archive);

        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        target.Close();
        var reopened = await target.Open();
        Assert.IsNotNull(reopened.Store, string.Join("; ", reopened.Failures));
        CollectionAssert.AreEqual(new[] { a.Id }, Ids(await reopened.Store.Recent()), "G4: \"unless it was added by a build that did not yet record what added an item\"");
        Assert.HasCount(2, await target.All());
        Assert.AreEqual(shot.Name, (await target.All()).Single(item => item.Id != a.Id).Name);
    }

    [TestMethod]
    public async Task AnUnusedCaptureIsStillListedAfterMove()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        await store.RecordUses([a], stash.Clock.Now);
        stash.Clock.Now += TimeSpan.FromSeconds(1);
        var capture = await stash.Capture("capture");

        var moved = await store.Move(MovingStash.Keeper(stash, failures), MovingStash.Chosen(stash));

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        stash.Close();
        var opening = await stash.OpenAt(moved.Value!);
        Assert.IsNotNull(opening.Store, string.Join("; ", opening.Failures));
        CollectionAssert.AreEqual(new[] { capture.Id, a.Id }, Ids(await opening.Store.Recent()), "G30: everything is there and works");
    }

    [TestMethod]
    public async Task ASaveAsNewItemCopyOfACaptureIsNoCaptureAndLeftOutOfRecentUntilUsed()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var capture = await stash.Capture("the audacity");

        var copy = await store.SaveAsNewItem(capture, LayerList.Empty, null);

        Assert.IsTrue(copy.Succeeded, copy.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { capture.Id }, Ids(await store.Recent()), "a copy Save as new item makes is not among the three (G26, G4)");
        Assert.HasCount(2, await stash.All());
    }

    [TestMethod]
    public async Task RecentIsAsManyStatementsOverHundredsOfItemsWithCapturesAndUsesAsOverAHandful()
    {
        var few = await StatementsOfRecent(3);
        var many = await StatementsOfRecent(1200);

        Assert.IsGreaterThan(0, few);
        Assert.AreEqual(few, many, "a fixed number of statements over the whole stash, whatever its size (E4)");
    }

    private static List<long> Ids(Outcome<IReadOnlyList<StashItem>> listed)
    {
        Assert.IsTrue(listed.Succeeded, listed.Failure?.ToString());
        return [.. listed.Value!.Select(item => item.Id)];
    }

    /// <summary>
    /// The statements SQLite runs for Recent over <paramref name="count"/> items, every other one a capture's and every
    /// third with two uses (<see cref="StoreStatements"/>).
    /// </summary>
    private static async Task<int> StatementsOfRecent(int count)
    {
        var (listed, statements) = await StoreStatements.Count(now => $"""
            WITH RECURSIVE n(k) AS (SELECT 1 UNION ALL SELECT k + 1 FROM n WHERE k < {count})
            INSERT INTO items (name, added_at, format, width, height, animated, frame_count, duration_ms, original_file, finished_file, original_sha256, added_by_capture)
            SELECT 'reaction ' || k, {now} + k, 'Png', 8, 6, 0, 1, 0, 'reaction ' || k || '.png', 'reaction ' || k || '.png', randomblob(32), k % 2 FROM n;
            INSERT INTO uses (item_id, used_at) SELECT id, {now} + id FROM items WHERE id % 3 = 0;
            INSERT INTO uses (item_id, used_at) SELECT id, {now} - id FROM items WHERE id % 3 = 0;
            """, ItemQueries.Recent);
        Assert.HasCount(Enumerable.Range(1, count).Count(k => k % 2 == 1 || k % 3 == 0), listed.Value!);
        return statements;
    }
}
