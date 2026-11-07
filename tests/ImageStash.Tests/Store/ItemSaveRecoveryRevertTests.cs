using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditedItems;

namespace ImageStash.Tests.Store;

/// <summary>
/// A Revert to original a kill or a refused step left pending, at the next start (State: Save, Every change; F7, G28,
/// G29, G35): finished without rendering, the original moved back over or beside its render and the render deleted;
/// while a step is still refused, undone where its files can go back, the original into originals\ again and the row
/// returned to the list, revision, names and flag it had; every failure named.
/// </summary>
[TestClass]
public sealed class ItemSaveRecoveryRevertTests
{
    private static readonly MediaFacts StillPng = new(ImageFormat.Png, 8, 6, false, 1, 0);

    [TestMethod]
    [DataRow("still.webp")]
    [DataRow("still.png")]
    public async Task ARevertCommittedWithNothingMovedIsFinishedAtStartWithoutRendering(string fixture)
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await RevertCommitted(stash, fixture);
        stash.Close();

        var opening = await stash.Open();

        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        Assert.IsEmpty(stash.RenderCalls);
        var file = "cat" + Path.GetExtension(fixture);
        CollectionAssert.AreEqual(new[] { file }, stash.RootFiles());
        Assert.AreEqual(Hash(MediaFixtures.Bytes(fixture)), Hash(stash.Folder.InRoot(file)));
        Assert.IsEmpty(OriginalFiles(stash));
        var row = Row(stash, cat.Id);
        Assert.AreEqual(new SavedRow("[]", null, 2, 2, false, file, file, row.Steps, row.Original, null), row);
        Assert.IsEmpty(row.Steps);
    }

    [TestMethod]
    public async Task ARevertWhoseOriginalIsBackIsFinishedAtStartByDeletingTheRender()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await RevertMoved(stash);
        var listed = await Listed(stash.Store!, cat.Id);
        Assert.AreEqual((stash.Folder.InRoot("cat.webp"), 2L, LayerList.Empty), (listed.FilePath, listed.RenderedRevision, (await ForEdit(stash.Store!, cat.Id)).RenderedLayers), "listed as reverted meanwhile");
        CollectionAssert.AreEqual(new[] { stash.Folder.InRoot("cat.webp") }, StoredPaths(stash, cat.Id).ToList());
        stash.Close();

        var opening = await stash.Open();

        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        CollectionAssert.AreEqual(new[] { "cat.webp" }, stash.RootFiles());
        Assert.IsEmpty(stash.RecycleBin.Asked);
        Assert.IsEmpty(Row(stash, cat.Id).Steps);
    }

    [TestMethod]
    public async Task ARevertWhoseOriginalCannotComeBackAtStartIsUndone()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await RevertCommitted(stash, "still.webp");
        stash.Close();

        StoreOpening opening;
        using (TestStash.Hold(stash.Folder.InOriginals("cat.webp")))
        {
            opening = await stash.Open();
        }

        Assert.AreEqual(new Failure("Finish an unfinished revert", "cat", HeldFileRetry.HeldReason), opening.Failures.Single());
        var row = Row(stash, cat.Id);
        Assert.AreEqual(new SavedRow(BlackBoxAndBox.ToJson(), null, 1, 1, true, "cat.png", "cat.webp", row.Steps, row.Original, StillPng), row, "back to the list, revision, names and flag it had");
        Assert.IsEmpty(row.Steps);
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "cat.webp" }, OriginalFiles(stash));
    }

    [TestMethod]
    public async Task ARevertWhoseRenderIsStillHeldAtStartPutsTheOriginalBackInOriginals()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await RevertMoved(stash);
        stash.Close();

        StoreOpening opening;
        using (TestStash.Hold(stash.Folder.InRoot("cat.png")))
        {
            opening = await stash.Open();
        }

        Assert.AreEqual(new Failure("Finish an unfinished revert", "cat", HeldFileRetry.HeldReason), opening.Failures.Single());
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "cat.webp" }, OriginalFiles(stash));
        Assert.AreEqual((1L, 1L, true, "cat.png", "cat.webp"), (Row(stash, cat.Id).LayerRevision, Row(stash, cat.Id).RenderedRevision, Row(stash, cat.Id).OriginalInOriginals, Row(stash, cat.Id).FinishedFile, Row(stash, cat.Id).OriginalFile));
    }
}
