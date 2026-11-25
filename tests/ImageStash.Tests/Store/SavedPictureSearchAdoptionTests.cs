using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.CaptionedItems;

namespace ImageStash.Tests.Store;

[TestClass]
public sealed class SavedPictureSearchAdoptionTests
{
    private const string Name = "landscape";
    private const string NameQuery = "land";
    private const string CaptionWord = "#OBAL";
    private const string CaptionQuery = NameQuery + " " + CaptionWord;
    private static readonly byte[] Rewrite = MediaFixtures.Bytes("photo.jpg");

    [TestMethod]
    public async Task RefusedRemovalDuringAdoptionKeepsTheOutgoingCaptionSearchable()
    {
        using var stash = new TestStash();
        var (saved, render) = await CaptionWithRewrite(stash);
        var store = stash.Store!;

        using var hold = TestStash.Hold(saved.FilePath);
        var adoption = await store.AdoptRewrite(saved.Id);
        Assert.AreEqual(HeldFileRetry.HeldReason, adoption.Failure?.Reason);
        Assert.IsFalse(File.Exists(stash.Folder.InRoot(Name + ".jpg")));

        await ListedPicture(store, saved.Id, saved.FilePath, render, edited: true, pending: true);
        await SearchListsPicture(store, CaptionQuery, saved.Id, saved.FilePath);
    }

    [TestMethod]
    public async Task RefusedPlacementDuringAdoptionLeavesNoSearchablePicture()
    {
        using var stash = new TestStash();
        var (saved, _) = await CaptionWithRewrite(stash);
        var store = stash.Store!;
        List<FileStream> holds = [];
        stash.Reader = OpenedWith.Holding(holds);

        try
        {
            var adoption = await store.AdoptRewrite(saved.Id);
            Assert.AreEqual(HeldFileRetry.HeldReason, adoption.Failure?.Reason);
            Assert.IsFalse(File.Exists(saved.FilePath), "the outgoing render was removed");
            Assert.IsFalse(File.Exists(stash.Folder.InRoot(Name + ".jpg")), "the rewrite has yet to land");
            var staged = stash.Folder.InWork(stash.WorkFiles().Single());
            Assert.AreEqual(staged, holds.Single().Name);
            CollectionAssert.AreEqual(Rewrite, File.ReadAllBytes(staged));

            await NoListedPicture(store);
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }
    }

    [TestMethod]
    public async Task ThePlacedAdoptionDropsTheCaptionWhileItsFinalTransactionIsRefused()
    {
        using var stash = new TestStash();
        var (saved, _) = await CaptionWithRewrite(stash);
        var store = stash.Store!;
        RefusePictureCompletion(stash);

        var adoption = await store.AdoptRewrite(saved.Id);

        Assert.IsFalse(adoption.Succeeded);
        Assert.Contains(CompletionRefused, adoption.Failure!.Reason);
        Assert.IsFalse(File.Exists(saved.FilePath));
        await ListedPicture(store, saved.Id, stash.Folder.InRoot(Name + ".jpg"), Rewrite, edited: false, pending: true);
        await Finds(store, CaptionWord);
        await Finds(store, CaptionQuery);
    }

    [TestMethod]
    public async Task PublishedAdoptionDropsTheCaptionAndSuccessfulUndoBringsItBack()
    {
        using var stash = new TestStash();
        var (saved, render) = await CaptionWithRewrite(stash);
        var store = stash.Store!;
        var adopted = await OpenedWith.Adopt(stash, saved.Id);
        var adoptedPath = stash.Folder.InRoot(Name + ".jpg");

        Assert.IsFalse(File.Exists(saved.FilePath));
        await ListedPicture(store, saved.Id, adoptedPath, Rewrite, edited: false, pending: false);
        await Finds(store, CaptionWord);
        await Finds(store, CaptionQuery);

        await OpenedWith.UndoAdoption(stash, saved.Id, adopted.RenderedRevision);

        Assert.IsFalse(File.Exists(adoptedPath));
        await ListedPicture(store, saved.Id, saved.FilePath, render, edited: true, pending: false);
        await SearchListsPicture(store, CaptionQuery, saved.Id, saved.FilePath);
    }

    [TestMethod]
    public async Task RefusedSetAsideDuringUndoKeepsOnlyTheAdoptedPictureSearchable()
    {
        using var stash = new TestStash();
        var (saved, _) = await CaptionWithRewrite(stash);
        var store = stash.Store!;
        var adopted = await OpenedWith.Adopt(stash, saved.Id);

        using var hold = TestStash.Hold(adopted.FilePath);
        var undo = await store.UndoAdoption(saved.Id, adopted.RenderedRevision);
        Assert.AreEqual(HeldFileRetry.HeldReason, undo.Failure?.Reason);
        Assert.IsFalse(File.Exists(saved.FilePath), "the restored caption render has yet to land");

        await ListedPicture(store, saved.Id, adopted.FilePath, Rewrite, edited: false, pending: true);
        await Finds(store, CaptionWord);
        await Finds(store, CaptionQuery);
    }

    [TestMethod]
    public async Task RefusedRenderPlacementDuringUndoLeavesNoSearchablePicture()
    {
        using var stash = new TestStash();
        var (saved, render) = await CaptionWithRewrite(stash);
        var store = stash.Store!;
        var adopted = await OpenedWith.Adopt(stash, saved.Id);
        List<FileStream> holds = [];
        stash.Renderer = EditedItems.Holding(holds);

        try
        {
            var undo = await store.UndoAdoption(saved.Id, adopted.RenderedRevision);

            Assert.AreEqual(HeldFileRetry.HeldReason, undo.Failure?.Reason);
            Assert.IsFalse(File.Exists(adopted.FilePath), "The adopted file was set aside.");
            Assert.IsFalse(File.Exists(saved.FilePath), "The restored render has yet to land.");
            CollectionAssert.AreEqual(render, File.ReadAllBytes(holds.Single().Name));
            await NoListedPicture(store);
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }
    }

    [TestMethod]
    public async Task RefusedRecycleDuringUndoLeavesTheRestoredCaptionSearchable()
    {
        using var stash = new TestStash();
        var (saved, render) = await CaptionWithRewrite(stash);
        var store = stash.Store!;
        var adopted = await OpenedWith.Adopt(stash, saved.Id);
        stash.RecycleBin.Refusing = path => OpenedWith.Holds(path, Rewrite);

        var undo = await store.UndoAdoption(saved.Id, adopted.RenderedRevision);
        Assert.AreEqual("the test Recycle Bin refuses it", undo.Failure?.Reason);
        Assert.IsFalse(File.Exists(adopted.FilePath));
        CollectionAssert.AreEqual(Rewrite, File.ReadAllBytes(stash.RecycleBin.Asked.Single()));

        await ListedPicture(store, saved.Id, saved.FilePath, render, edited: true, pending: true);
        await SearchListsPicture(store, CaptionQuery, saved.Id, saved.FilePath);
    }

    private static async Task<(StashItem Saved, byte[] Render)> CaptionWithRewrite(TestStash stash)
    {
        await stash.OpenCleanly();
        var item = await stash.ImportFixture(Name, "still.png");
        var saved = await Save(stash, item, Caption("Cobalt skyline"));
        var render = File.ReadAllBytes(saved.FilePath);
        await OpenedWith.AwaitingRewrite(stash, saved, Rewrite);
        return (saved, render);
    }

    private static async Task ListedPicture(StashStore store, long id, string path, byte[] bytes, bool edited, bool pending)
    {
        var all = await store.All();
        Assert.IsTrue(all.Succeeded, all.Failure?.ToString());
        Assert.HasCount(1, all.Value!);
        var item = all.Value![0];
        Assert.AreEqual((id, path, edited, pending), (item.Id, item.FilePath, item.Edited, item.StepsPendingWhenRead));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(item.FilePath));
        await SearchListsPicture(store, NameQuery, id, path);
    }

    private static async Task SearchListsPicture(StashStore store, string query, long id, string path)
    {
        var item = (await Finds(store, query, id)).Single();
        Assert.AreEqual(path, item.FilePath);
    }

    private static async Task NoListedPicture(StashStore store)
    {
        var all = await store.All();
        Assert.IsTrue(all.Succeeded, all.Failure?.ToString());
        Assert.IsEmpty(all.Value!);
        await Finds(store, NameQuery);
        await Finds(store, CaptionWord);
        await Finds(store, CaptionQuery);
    }
}
