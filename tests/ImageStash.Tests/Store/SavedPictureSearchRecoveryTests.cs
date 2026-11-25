using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.CaptionedItems;

namespace ImageStash.Tests.Store;

[TestClass]
public sealed class SavedPictureSearchRecoveryTests
{
    private const string Earlier = "echidna";
    private const string Later = "platypus";

    [TestMethod]
    [DataRow(false, DisplayName = "release the old picture before reopening")]
    [DataRow(true, DisplayName = "hold the old picture through reopen so recovery rolls back")]
    public async Task UntilReplacementLandsOnlyTheEarlierPictureMatches(bool rollBack)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await Save(stash, await stash.ImportFixture("picture", "still.png"), Caption(Earlier));
        var oldBytes = File.ReadAllBytes(item.FilePath);
        var nextLayers = Caption(Later);
        var next = await stash.Render(item, nextLayers);
        var newBytes = File.ReadAllBytes(next.WorkPath);
        CollectionAssert.AreNotEqual(oldBytes, newBytes);
        using var hold = TestStash.Hold(item.FilePath);

        var refused = await store.Save(item, nextLayers, next);

        Assert.AreEqual(new Failure("Save", item.Name, HeldFileRetry.HeldReason), refused.Failure);
        Assert.IsTrue(File.Exists(next.WorkPath));
        await PictureMatches(store, item.Id, Earlier, Later, oldBytes);

        if (!rollBack)
        {
            hold.Dispose();
        }

        var reopening = await stash.Reopen();
        Assert.IsNotNull(reopening.Store, string.Join("; ", reopening.Failures));
        if (rollBack)
        {
            Assert.AreEqual("Finish an unfinished save", reopening.Failures.Single().Action);
            await PictureMatches(reopening.Store, item.Id, Earlier, Later, oldBytes);
        }
        else
        {
            Assert.IsEmpty(reopening.Failures, string.Join("; ", reopening.Failures));
            await PictureMatches(reopening.Store, item.Id, Later, Earlier, newBytes);
        }
        Assert.IsEmpty(EditedItems.Row(stash, item.Id).Steps);
    }

    [TestMethod]
    public async Task ThePlacedReplacementMatchesWhileItsFinalTransactionIsRefused()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await Save(stash, await stash.ImportFixture("picture", "still.png"), Caption(Earlier));
        var nextLayers = Caption(Later);
        var next = await stash.Render(item, nextLayers);
        var newBytes = File.ReadAllBytes(next.WorkPath);
        CollectionAssert.AreNotEqual(File.ReadAllBytes(item.FilePath), newBytes);
        RefusePictureCompletion(stash);

        var refused = await store.Save(item, nextLayers, next);

        Assert.IsFalse(refused.Succeeded);
        Assert.Contains(CompletionRefused, refused.Failure!.Reason);
        Assert.IsFalse(File.Exists(next.WorkPath), "The render reached the root.");
        var row = EditedItems.Row(stash, item.Id);
        Assert.AreEqual(item.RenderedRevision, row.RenderedRevision);
        Assert.IsGreaterThan(row.RenderedRevision, row.LayerRevision);
        await PictureMatches(store, item.Id, Later, Earlier, newBytes);

        stash.SqlWhileOpen("DROP TRIGGER refuse_finish");
        var reopening = await stash.Reopen();
        Assert.IsNotNull(reopening.Store, string.Join("; ", reopening.Failures));
        Assert.IsEmpty(reopening.Failures, string.Join("; ", reopening.Failures));
        await PictureMatches(reopening.Store, item.Id, Later, Earlier, newBytes);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "the held render leaves the copy unlisted")]
    [DataRow(true, DisplayName = "both files are placed when completion is refused")]
    public async Task AnIndependentCopyContributesItsOwnCaptionOnlyWhenListed(bool placed)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var source = await Save(stash, await stash.ImportFixture("picture", "still.webp"), Caption(Earlier));
        var sourceBytes = File.ReadAllBytes(source.FilePath);
        var layers = Caption(Later);
        var render = await stash.Render(source, layers);
        var copyBytes = File.ReadAllBytes(render.WorkPath);
        if (placed)
        {
            RefusePictureCompletion(stash);
        }

        Outcome<StashItem> refused;
        using (placed ? null : TestStash.Hold(render.WorkPath))
        {
            refused = await store.SaveAsNewItem(source, layers, render);
        }
        Assert.IsFalse(refused.Succeeded);
        Assert.IsNotNull(refused.Value, "Both refusals occur after the copy's insert.");
        var copy = refused.Value;
        Assert.AreNotEqual(source.Id, copy.Id);
        await CopyListing(stash, store, source.Id, sourceBytes, copy, copyBytes, placed);

        if (placed)
        {
            Assert.Contains(CompletionRefused, refused.Failure!.Reason);
            stash.SqlWhileOpen("DROP TRIGGER refuse_finish");
        }
        else
        {
            Assert.AreEqual(HeldFileRetry.HeldReason, refused.Failure!.Reason);
            Assert.IsTrue(File.Exists(render.WorkPath));
        }

        var reopening = await stash.Reopen();
        Assert.IsNotNull(reopening.Store, string.Join("; ", reopening.Failures));
        Assert.IsEmpty(reopening.Failures, string.Join("; ", reopening.Failures));
        await CopyListing(stash, reopening.Store, source.Id, sourceBytes, copy, copyBytes, placed);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "the original cannot move back")]
    [DataRow(true, DisplayName = "the original is back and the edited render cannot be deleted")]
    public async Task ARefusedRevertMatchesThePictureTheStashLists(bool originalMoves)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await Save(stash, await stash.ImportFixture("picture", "still.webp"), Caption(Earlier));
        var oldBytes = File.ReadAllBytes(item.FilePath);
        using var held = TestStash.Hold(originalMoves ? item.FilePath : stash.Folder.InOriginals("picture.webp"));

        var reverted = await store.Save(item, LayerList.Empty, null);

        Assert.AreEqual(new Failure("Revert to original", item.Name, HeldFileRetry.HeldReason), reverted.Failure);
        Assert.AreEqual(originalMoves, File.Exists(stash.Folder.InRoot("picture.webp")));
        Assert.IsTrue(File.Exists(item.FilePath));
        if (originalMoves)
        {
            await PictureMatches(store, item.Id, "picture", Earlier, MediaFixtures.Bytes("still.webp"));
        }
        else
        {
            await PictureMatches(store, item.Id, Earlier, Later, oldBytes);
        }
    }

    [TestMethod]
    public async Task RevertRemovesTheCaptionAndItsNoticeUndoFindsTheRenderedPictureAgain()
    {
        using var listed = await ListedStash.Open();
        var stash = listed.Stash;
        var store = stash.Store!;
        var item = await Save(stash, await stash.ImportFixture("picture", "still.png"), Caption(Earlier));
        var rendered = File.ReadAllBytes(item.FilePath);
        await listed.Listing.Refresh();

        var reverted = await listed.Editing.RevertToOriginal(item);

        Assert.IsTrue(reverted.Succeeded, reverted.Failure?.ToString());
        await PictureMatches(store, item.Id, "picture", Earlier, MediaFixtures.Bytes("still.png"));

        await listed.Notices.Undo(listed.Notices.Shown.Single());
        Assert.IsEmpty(listed.Notices.Shown);
        await PictureMatches(store, item.Id, Earlier, Later, rendered);
    }

    private static async Task PictureMatches(StashStore store, long id, string hit, string miss, byte[] bytes)
    {
        var listed = await store.All();
        Assert.IsTrue(listed.Succeeded, listed.Failure?.ToString());
        var found = (await Finds(store, hit, id)).Single();
        TestStash.AssertSameItem(listed.Value!.Single(), found);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(found.FilePath));
        await Finds(store, miss);
    }

    private static async Task CopyListing(TestStash stash, StashStore store, long sourceId, byte[] sourceBytes,
        StashItem copy, byte[] copyBytes, bool placed)
    {
        var source = (await Finds(store, Earlier, sourceId)).Single();
        CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(source.FilePath));
        if (placed)
        {
            var found = (await Finds(store, Later, copy.Id)).Single();
            TestStash.AssertSameItem((await stash.All()).First(), found);
            CollectionAssert.AreEqual(copyBytes, File.ReadAllBytes(found.FilePath));
        }
        else
        {
            Assert.AreEqual(sourceId, (await stash.All()).Single().Id);
            Assert.IsFalse(File.Exists(copy.FilePath));
            await Finds(store, Later);
        }
    }
}
