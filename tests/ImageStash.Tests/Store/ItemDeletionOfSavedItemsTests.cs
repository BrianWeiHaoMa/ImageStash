using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditedItems;

namespace ImageStash.Tests.Store;

/// <summary>
/// Delete with Undo on an edited item the store saved (G18, State: Delete; Done when 4): both its files,
/// the render in the root and the original in originals\, held as copies and recycled; Undo bringing both back with the
/// item's name, tags, favorite flag, uses and the render's facts; expiry deleting both copies and removing the row; a
/// kill before the recycle or before the mark leaving the item fully in the Recycle Bin or fully present.
/// </summary>
[TestClass]
public sealed class ItemDeletionOfSavedItemsTests
{
    [TestMethod]
    public async Task BothFilesAreHeldAndRecycledAndUndoBringsBothBackWithEverythingTheItemHad()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var saved = await SavedCat(stash);
        var render = File.ReadAllBytes(saved.FilePath);

        var deleted = (await store.Delete([saved.Id])).Single();

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { stash.Folder.InRoot("cat.png"), stash.Folder.InOriginals("cat.webp") }, stash.RecycleBin.Asked, "the render and the original");
        Assert.HasCount(2, deleted.Value!.RecycledIdentities);
        Assert.IsEmpty(stash.RootFiles());
        Assert.IsEmpty(OriginalFiles(stash));
        CollectionAssert.AreEqual(render, File.ReadAllBytes(stash.HeldCopy(saved.Id)));
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.webp"), File.ReadAllBytes(stash.HeldCopy(saved.Id, "original_file")));
        Assert.IsEmpty(await stash.All());

        var restored = (await store.UndoDelete([deleted.Value])).Single();

        Assert.IsTrue(restored.Succeeded && restored.Value!.Named.Count == 0, restored.Failure?.ToString());
        var back = await Listed(store, saved.Id);
        TestStash.AssertSameItem(saved, back);
        Assert.AreEqual((ImageFormat.Png, true), (back.Facts.Format, back.Favorite), "the render's facts, and the flag it had");
        CollectionAssert.AreEqual(new[] { "yes" }, back.Tags.ToList());
        Assert.AreEqual(1, (await store.UseHistory(saved.Id)).Value!.Count);
        CollectionAssert.AreEqual(render, File.ReadAllBytes(stash.Folder.InRoot("cat.png")));
        Assert.AreEqual(Hash(MediaFixtures.Bytes("still.webp")), Hash(stash.Folder.InOriginals("cat.webp")));
        Assert.AreEqual((BlackBoxAndBox, stash.Folder.InOriginals("cat.webp")), ((await ForEdit(store, saved.Id)).Layers, (await ForEdit(store, saved.Id)).OriginalPath));
        CollectionAssert.AreEqual(deleted.Value.RecycledIdentities.ToList(), stash.RecycleBin.Removed, "the bin's entries are removed");
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ItsExpiryDeletesBothHeldCopiesAndRemovesTheRowWithBothFilesLeftInTheBin()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var saved = await SavedCat(stash);
        Assert.IsTrue((await store.Delete([saved.Id])).Single().Succeeded);

        var finished = (await store.FinishDelete([saved.Id])).Single();

        Assert.IsTrue(finished.Succeeded, finished.Failure?.ToString());
        Assert.IsEmpty(stash.WorkFiles(), "both held copies deleted");
        Assert.HasCount(2, Directory.GetFiles(stash.RecycleBin.Folder));
        stash.Close();
        Assert.AreEqual(0L, stash.SqlQuery("SELECT (SELECT COUNT(*) FROM items) + (SELECT COUNT(*) FROM tags) + (SELECT COUNT(*) FROM uses)", r => r.GetInt64(0)).Single());
    }

    [TestMethod]
    public async Task AKillAfterTheMarkAndBeforeTheRecycleLeavesTheStartToPutBothFilesInTheBin()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var saved = await SavedCat(stash);
        // Marked with both copies recorded and both files still in place, as a kill before the recycle leaves it.
        stash.RecycleBin.Refusing = _ => true;
        await store.Delete([saved.Id]);
        stash.Close();
        stash.RecycleBin.Refusing = _ => false;
        stash.RecycleBin.Asked.Clear();

        await stash.OpenCleanly();

        CollectionAssert.AreEquivalent(new[] { stash.Folder.InRoot("cat.png"), stash.Folder.InOriginals("cat.webp") }, stash.RecycleBin.Asked);
        Assert.HasCount(2, Directory.GetFiles(stash.RecycleBin.Folder), "fully in the Recycle Bin");
        Assert.IsEmpty(stash.RootFiles());
        Assert.IsEmpty(OriginalFiles(stash));
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(await stash.All());
        stash.Close();
        Assert.AreEqual(0L, stash.SqlQuery("SELECT COUNT(*) FROM items", r => r.GetInt64(0)).Single());
    }

    [TestMethod]
    public async Task AKillAfterTheCopiesAndBeforeTheMarkLeavesTheItemFullyPresent()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var saved = await SavedCat(stash);
        stash.Close();
        File.Copy(saved.FilePath, stash.Folder.InWork(Guid.NewGuid().ToString("N") + ".png"));
        File.Copy(stash.Folder.InOriginals("cat.webp"), stash.Folder.InWork(Guid.NewGuid().ToString("N") + ".webp"));

        var store = await stash.OpenCleanly();

        TestStash.AssertSameItem(saved, (await stash.All()).Single());
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "cat.webp" }, OriginalFiles(stash));
        Assert.IsEmpty(stash.WorkFiles(), "the copies no row refers to are cleared");
        Assert.IsEmpty(stash.RecycleBin.Asked);
        Assert.AreEqual(BlackBoxAndBox, (await ForEdit(store, saved.Id)).Layers);
    }

    // A WebP "cat" tagged "yes", a favorite with one use, saved with a black box and a box: cat.png in the root and
    // cat.webp in originals\, as the stash lists it now.
    private static async Task<StashItem> SavedCat(TestStash stash)
    {
        var store = stash.Store!;
        var cat = await stash.ImportFixture("cat", "still.webp");
        stash.Tag(cat.Id, "yes");
        stash.SqlWhileOpen("UPDATE items SET favorite = 1 WHERE id = $id", ("$id", cat.Id));
        Assert.IsEmpty((await store.RecordUses([cat], stash.Clock.Now)).Value!);
        var saved = await stash.SaveEdit(await Listed(store, cat.Id), BlackBoxAndBox);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        Assert.AreEqual(stash.Folder.InRoot("cat.png"), saved.Value!.FilePath);
        return await Listed(store, cat.Id);
    }
}
