using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The notification that an Open with… rewrite replaced an item, over a real stash (G27, G16): an adoption said late
/// never takes down the notification of a later adoption of the same item, whose Undo is the one Store still keeps; an
/// Undo Store no longer acts on, or refuses before changing anything, is refused by name, and the previous original goes
/// to the Recycle Bin as once the notification has gone (G35, G27), as is one asked after a Save of the item that left it
/// without layers, before the notification was raised or after; an Undo asked while the item's edit runs Save as new item
/// acts once it has, the item edited until then left as it was (G26).
/// </summary>
[TestClass]
public sealed class AdoptionNotificationsTests
{
    [TestMethod]
    [DataRow(true, DisplayName = "the item saved since")]
    [DataRow(false, DisplayName = "the item deleted, its Undo standing")]
    public async Task AnUndoStoreNoLongerActsOnIsRefusedByNameAndThePreviousOriginalGoesToTheRecycleBin(bool saved)
    {
        using var listed = await ListedStash.Open();
        var still = MediaFixtures.Bytes("still.png");
        var cat = await listed.Stash.ImportPicture("cat", "cat.png", still);
        await OpenedWith.AwaitingRewrite(listed.Stash, cat, MediaFixtures.DistinctPng(52, 6, 4));
        var adopted = await OpenedWith.Adopt(listed.Stash, cat.Id);
        await listed.Listing.Refresh();
        var said = new AdoptionNotifications(listed.Listing, listed.Notifications, listed.Editing, () => listed.Listing.Refresh());
        said.Raise(adopted, adopted.RenderedRevision);
        var shown = listed.Notifications.Shown.Single();

        if (saved)
        {
            Assert.IsTrue((await listed.Stash.SaveEdit(adopted, EditedItems.BlackBoxAndBox)).Succeeded);
        }
        else
        {
            await listed.Deletion.Delete([adopted]);
        }

        await listed.Listing.Refresh();
        await listed.Notifications.Undo(shown);

        var reason = saved ? AdoptionNotifications.ChangedSince : ItemFactChange.NotInStash;
        CollectionAssert.AreEqual(new[] { new Failure(ItemAdoptionUndo.Action, "cat", reason).Sentence }, listed.Notifications.Shown.Select(notice => notice.Text).ToList(), "the refusal named (G35)");
        CollectionAssert.Contains(OpenedWith.Recycled(listed.Stash), EditedItems.Hash(still), "the previous original in the Recycle Bin, the notification gone (G27, G29)");
        Assert.IsNull(OpenedWith.Undo(listed.Stash, cat.Id), "the record cleared");
        if (saved)
        {
            Assert.AreEqual(EditedItems.BlackBoxAndBox, (await EditedItems.ForEdit(listed.Stash.Store!, cat.Id)).Layers, "the Save stands");
        }
    }

    [TestMethod]
    public async Task AnUndoStoreRefusesBeforeChangingAnythingIsNamedAndThePreviousOriginalGoesToTheRecycleBin()
    {
        using var listed = await ListedStash.Open();
        var still = MediaFixtures.Bytes("still.png");
        var rewrite = MediaFixtures.DistinctPng(53, 6, 4);
        var cat = await listed.Stash.ImportPicture("cat", "cat.png", still);
        var saved = await listed.Stash.SaveEdit(cat, EditedItems.BlackBoxAndBox);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        await OpenedWith.AwaitingRewrite(listed.Stash, saved.Value!, rewrite);
        var adopted = await OpenedWith.Adopt(listed.Stash, cat.Id);
        await listed.Listing.Refresh();
        var said = new AdoptionNotifications(listed.Listing, listed.Notifications, listed.Editing, () => listed.Listing.Refresh());
        said.Raise(adopted, adopted.RenderedRevision);
        var shown = listed.Notifications.Shown.Single();
        // The edited previous picture cannot be made again, as when the write of its render is refused (E3).
        listed.Stash.Renderer = EditedItems.Refusing;

        await listed.Notifications.Undo(shown);

        CollectionAssert.AreEqual(new[] { new Failure(ItemAdoptionUndo.Action, "cat", EditedItems.RefusedReason).Sentence }, listed.Notifications.Shown.Select(notice => notice.Text).ToList(), "the refusal named (G35)");
        CollectionAssert.Contains(OpenedWith.Recycled(listed.Stash), EditedItems.Hash(still), "the previous original in the Recycle Bin, its Undo refused (G27, G29)");
        Assert.IsNull(OpenedWith.Undo(listed.Stash, cat.Id), "the record cleared");
        Assert.AreEqual(EditedItems.Hash(rewrite), OpenedWith.OriginalHash(listed.Stash, cat.Id), "the item stays the adopted picture");
    }

    [TestMethod]
    public async Task AnEarlierAdoptionSaidLateLeavesTheLaterOnesNotificationAndItsUndo()
    {
        using var listed = await ListedStash.Open();
        var (first, second) = (MediaFixtures.DistinctPng(50, 4, 4), MediaFixtures.DistinctPng(51, 5, 5));
        var cat = await listed.Stash.ImportPicture("cat", "cat.png", MediaFixtures.Bytes("still.png"));
        var copy = await OpenedWith.AwaitingRewrite(listed.Stash, cat, first);
        var earlier = await OpenedWith.Adopt(listed.Stash, cat.Id);
        OpenedWith.Rewrite(copy, second);
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(listed.Stash, cat.Id));
        var later = await OpenedWith.Adopt(listed.Stash, cat.Id);
        await listed.Listing.Refresh();
        var said = new AdoptionNotifications(listed.Listing, listed.Notifications, listed.Editing, () => listed.Listing.Refresh());

        said.Raise(later, later.RenderedRevision);
        var shown = listed.Notifications.Shown.Single();
        // As the start's adoption is said once its item can be read, after a later adoption was said.
        said.Raise(earlier, earlier.RenderedRevision);

        Assert.AreSame(shown, listed.Notifications.Shown.Single(), "the later adoption's notification stays, alone");
        await listed.Notifications.Undo(shown);
        Assert.AreEqual(EditedItems.Hash(first), OpenedWith.OriginalHash(listed.Stash, cat.Id), "its Undo returns to just before the later adoption (G27)");
    }

    [TestMethod]
    public async Task AnUndoAfterASaveOfTheItemThatAddedNoLayerIsRefusedByNameAndThePreviousOriginalGoesToTheRecycleBin()
    {
        using var listed = await ListedStash.Open();
        var still = MediaFixtures.Bytes("still.png");
        var rewrite = MediaFixtures.DistinctPng(91, 7, 5);
        var cat = await listed.Stash.ImportPicture("cat", "cat.png", still);
        await listed.Listing.Refresh();
        var replaced = await OpenedWith.ReplacedThroughTheMenu(listed, cat, rewrite);
        Assert.IsTrue((await listed.Editing.Open(listed.Listing.Items.Single(), EditOrigin.Grid)).Succeeded);
        Assert.IsTrue((await listed.Editing.Save()).Succeeded, "a Save with no layers, which changes nothing in the store");

        await listed.Notifications.Undo(replaced);

        CollectionAssert.AreEqual(new[] { new Failure(ItemAdoptionUndo.Action, "cat", AdoptionNotifications.ChangedSince).Sentence }, listed.Notifications.Shown.Select(notice => notice.Text).ToList(), "refused by name after a Save of the item (G27, G35)");
        Assert.AreEqual(EditedItems.Hash(rewrite), OpenedWith.OriginalHash(listed.Stash, cat.Id), "the adopted picture stays");
        CollectionAssert.Contains(OpenedWith.Recycled(listed.Stash), EditedItems.Hash(still), "the previous original in the Recycle Bin, its Undo refused (G27, G29)");
        Assert.IsNull(OpenedWith.Undo(listed.Stash, cat.Id), "the record cleared");
    }

    [TestMethod]
    public async Task AfterAnImportTheImportedItemGivenAReplacedOnesIdentityHasItsAdoptionSaidAndItsUndoActsWhateverTheReplacedOneLived()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("a");
        await listed.Stash.Import("b");
        var cat = await listed.Stash.ImportPicture("cat", "cat.png", MediaFixtures.Bytes("still.png"));
        await listed.Listing.ShowScope(Scope.All);
        await OpenedWith.ReplacedThroughTheMenu(listed, cat, MediaFixtures.DistinctPng(93, 7, 5));
        Assert.IsTrue((await listed.Editing.Open(listed.Listing.Items.Single(item => item.Id == cat.Id), EditOrigin.Grid)).Succeeded);
        Assert.IsTrue((await listed.Editing.Save()).Succeeded, "a Save after the adoption, which ends its Undo");
        listed.Stash.Clock.Now += Notices.Lasting;
        listed.Notifications.Sweep();
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "x", "y", "z");

        Assert.IsTrue((await listed.ImportStash(archive)).Succeeded);
        await listed.Listing.ShowScope(Scope.All);
        var z = listed.Listing.Items.Single(item => item.Name == "z");
        Assert.AreEqual(cat.Id, z.Id, "the imported store gives the replaced item's identity to another");
        var before = OpenedWith.OriginalHash(listed.Stash, z.Id);

        var replaced = await OpenedWith.ReplacedThroughTheMenu(listed, z, MediaFixtures.DistinctPng(94, 7, 5));
        await listed.Notifications.Undo(replaced);

        Assert.AreEqual(before, OpenedWith.OriginalHash(listed.Stash, z.Id), "its notification said and its Undo acted, judged by its own history alone (G27)");
        Assert.IsFalse(listed.Notifications.Shown.Any(notice => notice.Text.Contains(AdoptionNotifications.ChangedSince)), "no refusal from the replaced item's Save");
    }

    [TestMethod]
    public async Task ASaveOfTheItemBeforeItsAdoptionIsSaidStillRefusesTheUndo()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        var said = new AdoptionNotifications(listed.Listing, listed.Notifications, listed.Editing, () => listed.Listing.Refresh());
        var rewrite = MediaFixtures.DistinctPng(92, 7, 5);
        await OpenedWith.AwaitingRewrite(listed.Stash, cat, rewrite);
        var adopted = await OpenedWith.Adopt(listed.Stash, cat.Id);
        await listed.Listing.Refresh();
        Assert.IsTrue((await listed.Editing.Open(adopted, EditOrigin.Grid)).Succeeded);
        Assert.IsTrue((await listed.Editing.Save()).Succeeded, "a Save with no layers before the adoption is said, as the start's are said late");

        said.Raise(adopted, adopted.RenderedRevision);
        await listed.Notifications.Undo(listed.Notifications.Shown.Single());

        CollectionAssert.AreEqual(new[] { new Failure(ItemAdoptionUndo.Action, "cat", AdoptionNotifications.ChangedSince).Sentence }, listed.Notifications.Shown.Select(notice => notice.Text).ToList(), "refused by name (G27, G35)");
        Assert.AreEqual(EditedItems.Hash(rewrite), OpenedWith.OriginalHash(listed.Stash, cat.Id));
    }

    [TestMethod]
    public async Task AnUndoAskedWhileTheItemsEditSavesAsANewItemActsOnceThatHasAndRestoresTheItem()
    {
        using var listed = await ListedStash.Open();
        var still = MediaFixtures.Bytes("still.png");
        var cat = await listed.Stash.ImportPicture("cat", "cat.png", still);
        await listed.Listing.Refresh();
        var replaced = await OpenedWith.ReplacedThroughTheMenu(listed, cat, MediaFixtures.DistinctPng(91, 7, 5));
        Assert.IsTrue((await listed.Editing.Open(listed.Listing.Items.Single(), EditOrigin.Grid)).Succeeded);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        listed.Renderer = (original, name, layers, format, work, retry) =>
        {
            reached.Set();
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10)), "released");
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        };
        var saving = listed.Editing.SaveAsNewItem();
        Assert.IsTrue(reached.Wait(TimeSpan.FromSeconds(10)), "Save as new item is rendering");

        var undoing = listed.Notifications.Undo(replaced);
        release.Set();
        Assert.IsTrue((await saving).Succeeded, "Save as new item completed");
        await undoing;

        Assert.AreEqual(EditedItems.Hash(still), OpenedWith.OriginalHash(listed.Stash, cat.Id), "Save as new item left the item as it was, so its Undo restores it (G26, G27)");
        Assert.IsTrue(listed.Editing.IsOpen, "the edit goes on with the new item");
        Assert.AreNotEqual(cat.Id, listed.Editing.Session!.Item.Id);
    }
}
