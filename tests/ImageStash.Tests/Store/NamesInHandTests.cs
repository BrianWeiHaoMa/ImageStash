using ImageStash.Store;
using ImageStash.Tests.Support;
using Microsoft.Data.Sqlite;

namespace ImageStash.Tests.Store;

/// <summary>
/// The file names a copy, a paste or a drag-out holds while it hands them over (State: File names; G8, G9): taken for
/// every other row's new or renamed file until the hold ends, whether the store could be read at the confirmation or
/// not; never for the row holding it; and a drag of an item read before its name went to another file is refused.
/// </summary>
[TestClass]
public sealed class NamesInHandTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ANameAConfirmationHoldsIsTakenForANewItemUntilItIsDisposed(bool storeHeld)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat");
        Confirmation confirmed;
        using (storeHeld ? stash.HoldStore() : null)
        {
            confirmed = await store.Confirm([cat], "Copy");
        }

        Assert.IsTrue(confirmed.Items.Single().Succeeded, confirmed.Items.Single().Failure?.Sentence);
        Assert.IsTrue((await store.Remove([cat.Id])).Single().Succeeded);
        var whileHeld = await stash.Import("cat");
        confirmed.Dispose();
        Assert.IsTrue((await store.Remove([whileHeld.Id])).Single().Succeeded);
        var afterwards = await stash.Import("cat");

        Assert.AreEqual("cat 2.png", whileHeld.FileName, "the name in hand is taken although its item is gone");
        Assert.AreEqual("cat.png", afterwards.FileName, "free again once the handoff is done");
    }

    [TestMethod]
    public async Task TheItemHoldingANameMayRenameIntoItAndNoOtherMay()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat");
        var dog = await stash.Import("dog");
        using var confirmed = await store.Confirm([cat], "Copy");

        var kitten = (await store.Rename(cat, "kitten")).Value!;
        var dogAsCat = (await store.Rename(dog, "cat")).Value!;
        var catAgain = (await store.Rename(kitten, "cat")).Value!;

        Assert.AreEqual("cat 2.png", dogAsCat.FileName, "another row does not come under a name in hand");
        Assert.AreEqual("cat.png", catAgain.FileName, "the row holding it does");
        Assert.IsTrue(File.Exists(stash.Folder.InRoot("cat.png")));
    }

    [TestMethod]
    public async Task ARenameRolledBackOntoTheNameTheItemHasLeavesItDraggable()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat?");
        Outcome<StashItem> renamed;
        // Another program's read keeps the rename's commit out, so it is rolled back; "cat*" is made safe as cat? is.
        using (var reader = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = stash.Folder.StoreFile, Pooling = false }.ToString()))
        {
            reader.Open();
            using var read = reader.CreateCommand();
            read.CommandText = "BEGIN; SELECT COUNT(*) FROM items";
            read.ExecuteScalar();
            renamed = await store.Rename(cat, "cat*");
        }

        var unchanged = (await store.Item(cat.Id)).Value!;
        var dragged = store.HoldWhileDragged([unchanged], "Drag out");
        using var held = dragged.Value;

        Assert.IsFalse(renamed.Succeeded);
        Assert.AreEqual((cat.Name, cat.FilePath, cat.CommitsWhenRead), (unchanged.Name, unchanged.FilePath, unchanged.CommitsWhenRead), "nothing committed");
        Assert.IsTrue(dragged.Succeeded, dragged.Failure?.Sentence);
    }

    [TestMethod]
    public async Task ADragOfAnItemReadBeforeItsNameWentToAnotherFileIsRefusedAndOneReadSinceIsHeld()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var shown = await stash.Import("cat");
        var unrelated = await stash.Import("dog");
        Assert.IsTrue((await store.Remove([shown.Id])).Single().Succeeded);
        var newCat = await stash.Import("cat");
        Assert.AreEqual(shown.FileName, newCat.FileName);

        var refused = store.HoldWhileDragged([unrelated, shown], "Drag out");
        var heldOutcome = store.HoldWhileDragged([unrelated, newCat], "Drag out");

        Assert.AreEqual("Drag out failed: cat, it changed in the stash since it was shown", refused.Failure?.Sentence);
        Assert.IsTrue(heldOutcome.Succeeded, heldOutcome.Failure?.Sentence);
        using (heldOutcome.Value)
        {
            Assert.IsTrue((await store.Remove([newCat.Id])).Single().Succeeded);
            Assert.AreEqual("cat 2.png", (await stash.Import("cat")).FileName, "the dragged name is taken while the drag runs");
        }

        Assert.IsTrue((await store.Remove([unrelated.Id])).Single().Succeeded);
        Assert.AreEqual("dog.png", (await stash.Import("dog")).FileName, "nothing stays in hand: the refused drag held nothing and the other let go");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AnOriginalHandedOverWhileItsSaveWaitsIsTakenInOriginalsForEveryOtherRow(bool dragged)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.webp");
        var shown = await SaveWaitingOnItsRender(stash, cat);
        StashItem other;
        using (dragged ? store.HoldWhileDragged([shown], "Drag out").Value! : await store.Confirm([shown], "Copy"))
        {
            Assert.IsTrue((await store.Rename(shown, "dog")).Succeeded, "its own row may leave the name, its Save finished first");
            other = await stash.ImportFixture("cat", "transparent.webp");
            Assert.IsTrue((await stash.SaveEdit(other, EditedItems.Arrow)).Succeeded);
        }

        Assert.AreEqual(stash.Folder.InOriginals("cat 2.webp"), (await EditedItems.ForEdit(store, other.Id)).OriginalPath, "another row's original does not come under it");
        Assert.IsFalse(File.Exists(shown.FilePath), "so the path handed over names no other item's file");
    }

    [TestMethod]
    public async Task ADragOfAnItemReadWhileItsOriginalWasItsPictureIsRefusedOnceThatNameWentToAnotherOriginal()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var other = await stash.ImportFixture("cat", "transparent.webp");
        var shown = await SaveWaitingOnItsRender(stash, await stash.ImportFixture("cat", "still.webp"));
        Assert.IsTrue((await store.Rename(shown, "dog")).Succeeded);
        Assert.IsTrue((await stash.SaveEdit(other, EditedItems.Arrow)).Succeeded);
        Assert.AreEqual(shown.FilePath, (await EditedItems.ForEdit(store, other.Id)).OriginalPath, "the name went to the other item's original");

        var refused = store.HoldWhileDragged([shown], "Drag out");
        using var held = refused.Value;

        Assert.AreEqual("Drag out failed: cat, it changed in the stash since it was shown", refused.Failure?.Sentence);
    }

    // A first Save whose render's place is refused: the item is listed on its original, moved into originals\.
    private static async Task<StashItem> SaveWaitingOnItsRender(TestStash stash, StashItem item)
    {
        var render = await stash.Render(item, EditedItems.BlackBoxAndBox);
        using (TestStash.Hold(render.WorkPath))
        {
            Assert.IsFalse((await stash.Store!.Save(item, EditedItems.BlackBoxAndBox, render)).Succeeded);
        }

        var shown = (await stash.Store.Item(item.Id)).Value!;
        Assert.AreEqual(stash.Folder.InOriginals("cat.webp"), shown.FilePath, "the picture on disk (State: A live failure)");
        return shown;
    }
}
