using System.Diagnostics;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.EditedItems;

namespace ImageStash.Tests.Store;

/// <summary>
/// Save and Revert to original on the store, live (State: Save, Every change, A live failure, File names; F3, F6, F7,
/// G12, G29, G35, R9): the layers committed with the original moved into originals\ and the render placed under the
/// item's name in F3's format, both names from one stem; a second Save replacing the render; Revert putting the one file
/// back; every refusal before the commit changing nothing; a step refused after it keeping its steps, the item listed on
/// the picture on disk meanwhile, and the summon's listing answered while it waits on a held file (G1); the renderer
/// never called by a Save the caller rendered.
/// </summary>
[TestClass]
public sealed class ItemSaveTests
{
    private static readonly MediaFacts StillPng = new(ImageFormat.Png, 8, 6, false, 1, 0);

    [TestMethod]
    public async Task AFirstSaveKeepsTheOriginalInOriginalsAndPlacesTheRenderUnderTheItemsName()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var render = await stash.Render(cat, BlackBoxAndBox);
        var rendered = File.ReadAllBytes(render.WorkPath);
        var before = await Commits(store, cat.Id);

        var saved = await store.Save(cat, BlackBoxAndBox, render);

        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        Assert.AreEqual((1L, StillPng, stash.Folder.InRoot("cat.png"), "cat.png"), (saved.Value!.RenderedRevision, saved.Value.Facts, saved.Value.FilePath, saved.Value.FileName));
        CollectionAssert.AreEqual(rendered, File.ReadAllBytes(stash.Folder.InRoot("cat.png")), "the render is the finished picture");
        Assert.AreNotEqual(Hash(MediaFixtures.Bytes("still.png")), Hash(stash.Folder.InRoot("cat.png")));
        Assert.AreEqual((0, 0, 0, 255), Pixel(MediaFixtures.FirstFrame(stash.Folder.InRoot("cat.png")), 3, 3), "the black box is drawn");
        Assert.AreEqual(Hash(MediaFixtures.Bytes("still.png")), Hash(stash.Folder.InOriginals("cat.png")), "the original's bytes, unchanged (G29)");
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "cat.png" }, OriginalFiles(stash));
        Assert.IsEmpty(stash.WorkFiles());
        Assert.AreEqual(before + 2, await Commits(store, cat.Id), "the commit and the last transaction, nothing more");
        Assert.IsEmpty(stash.RenderCalls, "the caller rendered");
        var row = Row(stash, cat.Id);
        Assert.AreEqual(new SavedRow(BlackBoxAndBox.ToJson(), null, 1, 1, true, "cat.png", "cat.png", row.Steps, StillPng, StillPng), row);
        Assert.IsEmpty(row.Steps);
        TestStash.AssertSameItem(saved.Value, await Listed(store, cat.Id));
    }

    [TestMethod]
    public async Task ASecondSaveReplacesTheRenderAndLeavesNoThirdFile()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var first = (await stash.SaveEdit(cat, BlackBoxAndBox)).Value!;
        var edit = await ForEdit(store, cat.Id);
        Assert.AreEqual((BlackBoxAndBox, BlackBoxAndBox, stash.Folder.InOriginals("cat.png")), (edit.Layers, edit.RenderedLayers, edit.OriginalPath));
        var render = await stash.Render(first, Arrow);
        var rendered = File.ReadAllBytes(render.WorkPath);

        var second = await store.Save(first, Arrow, render);

        Assert.IsTrue(second.Succeeded, second.Failure?.ToString());
        Assert.AreEqual((2L, stash.Folder.InRoot("cat.png")), (second.Value!.RenderedRevision, second.Value.FilePath));
        CollectionAssert.AreEqual(rendered, File.ReadAllBytes(stash.Folder.InRoot("cat.png")));
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles(), "the earlier render is not kept (G29)");
        CollectionAssert.AreEqual(new[] { "cat.png" }, OriginalFiles(stash));
        Assert.AreEqual(Hash(MediaFixtures.Bytes("still.png")), Hash(stash.Folder.InOriginals("cat.png")));
        Assert.IsEmpty(stash.RecycleBin.Asked, "a replaced render is not recycled");
        edit = await ForEdit(store, cat.Id);
        Assert.AreEqual((Arrow, Arrow), (edit.Layers, edit.RenderedLayers));
        Assert.AreEqual(Arrow.ToJson(), Row(stash, cat.Id).Layers);
        Assert.IsNull(Row(stash, cat.Id).PreviousLayers);

        var same = await stash.SaveEdit(second.Value, Arrow);

        Assert.IsTrue(same.Succeeded, same.Failure?.ToString());
        Assert.AreEqual(3L, same.Value!.RenderedRevision, "the same list again renders and replaces too");
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(stash.RenderCalls);
    }

    [TestMethod]
    [DataRow("still.webp", "cat.png", "cat.webp", ImageFormat.Png, false, 1, 0L, null)]
    [DataRow("animated.webp", "cat.gif", "cat.webp", ImageFormat.Gif, true, 3, 600L, "GIF")]
    [DataRow("photo.jpeg", "cat.jpg", "cat.jpeg", ImageFormat.Jpeg, false, 1, 0L, null)]
    public async Task TheFinishedPictureIsInTheFormatF3GivesAndTheOriginalKeepsItsOwnFile(
        string fixture, string finished, string original, ImageFormat format, bool animated, int frames, long duration, string? badge)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", fixture);

        var saved = await stash.SaveEdit(cat, BlackBoxAndBox);

        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        Assert.AreEqual((finished, stash.Folder.InRoot(finished)), (saved.Value!.FileName, saved.Value.FilePath));
        Assert.AreEqual((format, animated, frames, duration), (saved.Value.Facts.Format, saved.Value.Facts.Animated, saved.Value.Facts.FrameCount, saved.Value.Facts.DurationMs));
        Assert.AreEqual(badge, TileBadge.Of(saved.Value), "the badge names the file handed over (G6)");
        CollectionAssert.AreEqual(new[] { finished }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { original }, OriginalFiles(stash));
        Assert.AreEqual(Hash(MediaFixtures.Bytes(fixture)), Hash(stash.Folder.InOriginals(original)));
        Assert.AreEqual(format, MediaDecoder.ReadFacts(saved.Value.FilePath, finished, TestStash.ShortRetry).Value!.Facts.Format);
        var row = Row(stash, cat.Id);
        Assert.AreEqual(cat.Facts, row.Original, "the original's record is the import's");
        Assert.AreEqual(saved.Value.Facts, row.Finished);
    }

    [TestMethod]
    public async Task AClipTrimmedToOneFrameIsAStillGifAndItsNextSaveIsStillAGif()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var clip = await stash.ImportFixture("cat", "animated.gif");
        var oneFrame = new LayerList([new TrimLayer(new FrameRange(1, 1))]);

        var saved = await stash.SaveEdit(clip, oneFrame);

        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        Assert.AreEqual(("cat.gif", ImageFormat.Gif, false, 1), (saved.Value!.FileName, saved.Value.Facts.Format, saved.Value.Facts.Animated, saved.Value.Facts.FrameCount));
        Assert.IsNull(TileBadge.Of(saved.Value));
        Assert.AreEqual(ImageFormat.Gif, (await ForEdit(store, clip.Id)).FinishedFormat, "F3 reads the original's record");
        CollectionAssert.AreEqual(new[] { "cat.gif" }, OriginalFiles(stash));
    }

    [TestMethod]
    public async Task AFirstSaveNamesBothFilesWithOneCounterWhileItsNameIsTaken()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var png = await stash.ImportFixture("cat", "still.png");
        var webp = await stash.ImportFixture("cat", "still.webp");
        Assert.AreEqual("cat.webp", webp.FileName);

        var saved = await stash.SaveEdit(webp, BlackBoxAndBox);

        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        Assert.AreEqual("cat 2.png", saved.Value!.FileName);
        CollectionAssert.AreEquivalent(new[] { "cat.png", "cat 2.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "cat 2.webp" }, OriginalFiles(stash), "the original takes the same stem (State: File names)");
        Assert.AreEqual(("cat 2.png", "cat 2.webp"), (Row(stash, webp.Id).FinishedFile, Row(stash, webp.Id).OriginalFile));

        Assert.IsTrue((await store.Remove([png.Id])).Single().Succeeded);
        var second = await stash.SaveEdit(saved.Value, Arrow);

        Assert.AreEqual("cat 2.png", second.Value!.FileName, "a second Save chooses no stem, though cat.png is free again");
        CollectionAssert.AreEqual(new[] { "cat 2.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "cat 2.webp" }, OriginalFiles(stash));
    }

    [TestMethod]
    public async Task AFirstSaveTakesACounterForANameACopyHoldsInHand()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var png = await stash.ImportFixture("cat", "still.png");
        var webp = await stash.ImportFixture("cat", "still.webp");
        using var copy = await store.Confirm([png], "Copy");
        Assert.IsTrue((await store.Rename(png, "dog")).Succeeded);
        Assert.IsFalse(File.Exists(stash.Folder.InRoot("cat.png")), "cat.png is free on disk and in every row");

        var saved = await stash.SaveEdit(webp, BlackBoxAndBox);

        Assert.AreEqual("cat 2.png", saved.Value!.FileName, "the copy still hands over cat.png (G8)");
        CollectionAssert.AreEqual(new[] { "cat 2.webp" }, OriginalFiles(stash));
    }

    [TestMethod]
    public async Task RevertPutsTheOriginalBackAsTheItemsOneFileAndDeletesTheRender()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.webp");
        var saved = (await stash.SaveEdit(cat, BlackBoxAndBox)).Value!;
        var before = await Commits(store, cat.Id);

        var reverted = await store.Save(saved, LayerList.Empty, null);

        Assert.IsTrue(reverted.Succeeded, reverted.Failure?.ToString());
        Assert.AreEqual(("cat.webp", stash.Folder.InRoot("cat.webp"), cat.Facts, 2L), (reverted.Value!.FileName, reverted.Value.FilePath, reverted.Value.Facts, reverted.Value.RenderedRevision));
        CollectionAssert.AreEqual(new[] { "cat.webp" }, stash.RootFiles());
        Assert.AreEqual(Hash(MediaFixtures.Bytes("still.webp")), Hash(stash.Folder.InRoot("cat.webp")));
        Assert.IsEmpty(OriginalFiles(stash));
        Assert.IsEmpty(stash.RecycleBin.Asked, "the render is deleted, not recycled (G29)");
        Assert.IsEmpty(stash.WorkFiles());
        Assert.AreEqual(before + 2, await Commits(store, cat.Id));
        var row = Row(stash, cat.Id);
        Assert.AreEqual(new SavedRow("[]", null, 2, 2, false, "cat.webp", "cat.webp", row.Steps, cat.Facts, null), row);
        Assert.IsEmpty(row.Steps);
        var edit = await ForEdit(store, cat.Id);
        Assert.AreEqual((LayerList.Empty, LayerList.Empty, stash.Folder.InRoot("cat.webp")), (edit.Layers, edit.RenderedLayers, edit.OriginalPath));
        Assert.IsEmpty(stash.RenderCalls);
    }

    [TestMethod]
    public async Task RevertOfARenderOfTheOriginalsOwnNameReplacesItWithTheOriginal()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var saved = (await stash.SaveEdit(cat, BlackBoxAndBox)).Value!;

        var reverted = await store.Save(saved, LayerList.Empty, null);

        Assert.IsTrue(reverted.Succeeded, reverted.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        Assert.AreEqual(Hash(MediaFixtures.Bytes("still.png")), Hash(stash.Folder.InRoot("cat.png")), "the original's pixels again");
        Assert.IsEmpty(OriginalFiles(stash));
        Assert.AreEqual((StillPng, 2L), (reverted.Value!.Facts, reverted.Value.RenderedRevision));
    }

    [TestMethod]
    public async Task RevertOnAnUneditedItemChangesNothing()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var before = await Commits(store, cat.Id);
        var written = File.GetLastWriteTimeUtc(cat.FilePath);
        var storeWritten = File.GetLastWriteTimeUtc(stash.Folder.StoreFile);

        var reverted = await store.Save(cat, LayerList.Empty, null);

        Assert.IsTrue(reverted.Succeeded, reverted.Failure?.ToString());
        TestStash.AssertSameItem(cat, reverted.Value);
        Assert.AreEqual(before, await Commits(store, cat.Id), "no transaction");
        Assert.AreEqual((written, storeWritten), (File.GetLastWriteTimeUtc(cat.FilePath), File.GetLastWriteTimeUtc(stash.Folder.StoreFile)));
        Assert.AreEqual((0L, 0L, "[]"), (Row(stash, cat.Id).LayerRevision, Row(stash, cat.Id).RenderedRevision, Row(stash, cat.Id).Layers));
    }

    [TestMethod]
    public async Task RevertTakesACounterWhenAnotherItemHasTheOriginalsNameInTheRoot()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.webp");
        var saved = (await stash.SaveEdit(cat, BlackBoxAndBox)).Value!;
        var other = await stash.ImportFixture("cat", "transparent.webp");
        Assert.AreEqual("cat.webp", other.FileName);

        var reverted = await store.Save(saved, LayerList.Empty, null);

        Assert.IsTrue(reverted.Succeeded, reverted.Failure?.ToString());
        Assert.AreEqual("cat 2.webp", reverted.Value!.FileName);
        CollectionAssert.AreEquivalent(new[] { "cat.webp", "cat 2.webp" }, stash.RootFiles());
        Assert.AreEqual(Hash(MediaFixtures.Bytes("transparent.webp")), Hash(stash.Folder.InRoot("cat.webp")), "the other item's file is untouched");
        Assert.AreEqual(Hash(MediaFixtures.Bytes("still.webp")), Hash(stash.Folder.InRoot("cat 2.webp")));
        Assert.AreEqual(("cat 2.webp", "cat 2.webp"), (Row(stash, cat.Id).FinishedFile, Row(stash, cat.Id).OriginalFile));
    }

    [TestMethod]
    public async Task ASaveRefusedBeforeItsCommitChangesNothingAndLeavesTheRenderToTheCaller()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var gone = await stash.ImportFixture("gone", "still.webp");
        var render = await stash.Render(cat, BlackBoxAndBox);
        var goneRender = await stash.Render(gone, BlackBoxAndBox);
        Assert.IsTrue((await store.Remove([gone.Id])).Single().Succeeded);
        var outside = stash.Outside("outside.png");
        File.Copy(render.WorkPath, outside);
        var vanished = await stash.Render(cat, BlackBoxAndBox);
        File.Delete(vanished.WorkPath);
        var before = await Commits(store, cat.Id);
        var files = FilesWithHashes(stash);

        Assert.AreEqual(new Failure("Save", "gone", ItemFactChange.NotInStash), (await store.Save(gone, BlackBoxAndBox, goneRender)).Failure);
        Assert.IsTrue(File.Exists(goneRender.WorkPath), "the render stays the caller's");
        Assert.AreEqual(new Failure("Save", "cat", "its rendered picture is not in the stash's work folder"), (await store.Save(cat, BlackBoxAndBox, render with { WorkPath = outside })).Failure);
        Assert.AreEqual(new Failure("Save", "cat", "its rendered picture is gone"), (await store.Save(cat, BlackBoxAndBox, vanished)).Failure);
        Assert.AreEqual(new Failure("Save", "cat", ItemSave.ChangedWhileRendered), (await store.Save(cat, BlackBoxAndBox, render with { OriginalSha256 = goneRender.OriginalSha256 })).Failure, "decoded from bytes that are not the item's original");
        Assert.AreEqual(new Failure("Save", "cat", "its layers came without the picture rendered from them"), (await store.Save(cat, BlackBoxAndBox, null)).Failure);
        Assert.AreEqual(new Failure("Revert to original", "cat", "a revert to the original takes no rendered picture"), (await store.Save(cat, LayerList.Empty, render)).Failure);

        Assert.AreEqual(before, await Commits(store, cat.Id), "nothing committed");
        CollectionAssert.AreEqual(files, FilesWithHashes(stash), "nothing written");
        Assert.AreEqual((0L, "[]", false), (Row(stash, cat.Id).LayerRevision, Row(stash, cat.Id).Layers, Row(stash, cat.Id).OriginalInOriginals));
        Assert.IsEmpty(stash.RenderCalls);
    }

    [TestMethod]
    public async Task AStoreHeldPastTheRetryRefusesTheSaveAndWritesNothing()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var render = await stash.Render(cat, BlackBoxAndBox);

        Outcome<StashItem> saved;
        using (stash.HoldStore())
        {
            saved = await store.Save(cat, BlackBoxAndBox, render);
        }

        Assert.AreEqual(new Failure("Save", StashFolder.StoreFileName, HeldFileRetry.HeldReason), saved.Failure);
        Assert.IsNull(saved.Value);
        Assert.AreEqual((0L, false), (Row(stash, cat.Id).LayerRevision, Row(stash, cat.Id).OriginalInOriginals));
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        Assert.IsTrue(File.Exists(render.WorkPath));
    }

    [TestMethod]
    public async Task AHeldOriginalFailsAFirstSaveAfterItsCommitAndTheItemStaysListedOnItUntilALaterChangeFinishesIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var render = await stash.Render(cat, BlackBoxAndBox);

        Outcome<StashItem> saved;
        using (TestStash.Hold(cat.FilePath))
        {
            saved = await store.Save(cat, BlackBoxAndBox, render);
        }

        Assert.AreEqual(new Failure("Save", "cat", HeldFileRetry.HeldReason), saved.Failure);
        var row = Row(stash, cat.Id);
        Assert.AreEqual((BlackBoxAndBox.ToJson(), "[]", 1L, 0L, true), (row.Layers, row.PreviousLayers, row.LayerRevision, row.RenderedRevision, row.OriginalInOriginals));
        CollectionAssert.AreEqual(new[] { PendingStep.MoveToOriginals, PendingStep.PlaceRender }, row.Steps.Select(s => s.Step).ToList());
        Assert.IsNull(row.Finished, "no facts of a picture not yet on disk (R9)");
        var listed = await Listed(store, cat.Id);
        TestStash.AssertSameItem(listed, saved.Value);
        Assert.AreEqual((stash.Folder.InRoot("cat.png"), "cat.png", StillPng, 0L), (listed.FilePath, listed.FileName, listed.Facts, listed.RenderedRevision));
        Assert.AreEqual(listed.FilePath, (await stash.All()).Single().FilePath);
        var edit = await ForEdit(store, cat.Id);
        Assert.AreEqual((BlackBoxAndBox, LayerList.Empty, stash.Folder.InRoot("cat.png")), (edit.Layers, edit.RenderedLayers, edit.OriginalPath));
        Assert.IsTrue(File.Exists(render.WorkPath));
        Assert.IsTrue((await store.AddTag([listed], "mood")).Single().Succeeded);
        Assert.IsTrue((await store.SetFavorite([listed], true)).Single().Succeeded);
        Assert.IsTrue((await store.RecordUses([listed], stash.Clock.Now)).Succeeded);

        var renamed = await store.Rename(listed, "dog");

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        Assert.HasCount(1, stash.RenderCalls, "the Rename finished the Save first, rendering it again");
        Assert.AreEqual((stash.Folder.InRoot("cat.png"), BlackBoxAndBox, ImageFormat.Png), stash.RenderCalls[0], "over the original where it is, before its move");
        Assert.AreEqual(("dog", "dog.png", 1L, true), (renamed.Value!.Name, renamed.Value.FileName, renamed.Value.RenderedRevision, renamed.Value.Favorite));
        CollectionAssert.AreEqual(new[] { "dog.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "dog.png" }, OriginalFiles(stash));
        Assert.AreEqual(Hash(MediaFixtures.Bytes("still.png")), Hash(stash.Folder.InOriginals("dog.png")));
        Assert.AreEqual((0, 0, 0, 255), Pixel(MediaFixtures.FirstFrame(stash.Folder.InRoot("dog.png")), 3, 3));
        Assert.AreEqual(StillPng, Row(stash, cat.Id).Finished);
        Assert.IsEmpty(Row(stash, cat.Id).Steps);
        Assert.IsEmpty(stash.WorkFiles(), "the first render, superseded, is gone");
    }

    [TestMethod]
    public async Task BetweenTheMoveAndThePlaceTheItemIsItsOriginalAndThePendingNamesStayTaken()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.webp");
        var render = await stash.Render(cat, BlackBoxAndBox);

        Outcome<StashItem> saved;
        using (TestStash.Hold(render.WorkPath))
        {
            saved = await store.Save(cat, BlackBoxAndBox, render);
        }

        Assert.AreEqual(new Failure("Save", "cat", HeldFileRetry.HeldReason), saved.Failure);
        var listed = await Listed(store, cat.Id);
        Assert.AreEqual((stash.Folder.InOriginals("cat.webp"), "cat.webp", cat.Facts), (listed.FilePath, listed.FileName, listed.Facts), "the item's one picture, where it is now");
        Assert.IsNull(TileBadge.Of(listed));
        CollectionAssert.AreEqual(new[] { stash.Folder.InOriginals("cat.webp") }, StoredPaths(stash, cat.Id).ToList());
        using (var confirmed = await store.Confirm([cat], "Copy"))
        {
            Assert.AreEqual(stash.Folder.InOriginals("cat.webp"), confirmed.Items.Single().Value!.FilePath);
        }

        var webp = await stash.ImportFixture("another", "transparent.webp");
        var renamed = await store.Rename(webp, "cat");

        Assert.AreEqual("cat 2.webp", renamed.Value!.FileName, "the name the original moved from is the Save's while it waits");
        Assert.IsEmpty(stash.RootFiles().Where(name => name is "cat.png" or "cat.webp"));
    }

    [TestMethod]
    public async Task AHeldEarlierRenderIsRetriedBeforeTheSecondSaveFailsAndCompletesOnceReleased()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var first = (await stash.SaveEdit(cat, BlackBoxAndBox)).Value!;
        var render = await stash.Render(first, Arrow);

        var clock = Stopwatch.StartNew();
        Outcome<StashItem> saved;
        using (TestStash.Hold(first.FilePath))
        {
            saved = await store.Save(first, Arrow, render);
        }

        Assert.AreEqual(new Failure("Save", "cat", HeldFileRetry.HeldReason), saved.Failure, "a sync client holding the last render is waited for (E3)");
        Assert.IsGreaterThanOrEqualTo(TestStash.ShortRetry, clock.Elapsed);
        var listed = await Listed(store, cat.Id);
        Assert.AreEqual((first.FilePath, first.Facts, 1L), (listed.FilePath, listed.Facts, listed.RenderedRevision), "listed on the earlier render");
        Assert.HasCount(1, Row(stash, cat.Id).Steps);
        Assert.AreEqual(BlackBoxAndBox, (await ForEdit(store, cat.Id)).RenderedLayers);
    }

    [TestMethod]
    public async Task AnEarlierRenderReleasedWithinTheRetryLetsTheSecondSaveComplete()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(5) };
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var first = (await stash.SaveEdit(cat, BlackBoxAndBox)).Value!;
        var render = await stash.Render(first, Arrow);
        var hold = TestStash.Hold(first.FilePath);

        var saving = store.Save(first, Arrow, render);
        await Task.Delay(400);
        hold.Dispose();
        var saved = await saving;

        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        Assert.AreEqual(2L, saved.Value!.RenderedRevision);
    }

    [TestMethod]
    public async Task ARevertOverAHeldRenderOfItsOwnNameIsRetriedThenNamedAndListsTheRender()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var saved = (await stash.SaveEdit(cat, BlackBoxAndBox)).Value!;

        Outcome<StashItem> reverted;
        using (TestStash.Hold(saved.FilePath))
        {
            reverted = await store.Save(saved, LayerList.Empty, null);
        }

        Assert.AreEqual(new Failure("Revert to original", "cat", HeldFileRetry.HeldReason), reverted.Failure);
        var listed = await Listed(store, cat.Id);
        Assert.AreEqual((saved.FilePath, saved.Facts, 1L), (listed.FilePath, listed.Facts, listed.RenderedRevision), "the render is the picture on disk");
        Assert.AreEqual(BlackBoxAndBox, (await ForEdit(store, cat.Id)).RenderedLayers);
        Assert.AreEqual(stash.Folder.InOriginals("cat.png"), (await ForEdit(store, cat.Id)).OriginalPath);
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow("first Save", DisplayName = "a first Save over its held original")]
    [DataRow("second Save", DisplayName = "a second Save over its held earlier render")]
    [DataRow("Revert", DisplayName = "a Revert over its held render")]
    public async Task TheSummonsListingIsAnsweredWhileASaveWaitsOnAHeldFileListingTheItemOnThePictureOnDisk(string change)
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var item = change == "first Save" ? cat : (await stash.SaveEdit(cat, BlackBoxAndBox)).Value!;
        Assert.IsTrue((await store.RecordUses([item], stash.Clock.Now)).Succeeded);
        var layers = change == "Revert" ? LayerList.Empty : Arrow;
        var render = change == "Revert" ? null : await stash.Render(item, layers);
        var onDisk = item.FilePath;

        var (recent, saved) = await ReadsWhileAChangeWaits.Asked(TestStash.Hold(onDisk), () => store.Save(item, layers, render), store.Recent);

        var listed = recent.Value!.Single();
        Assert.AreEqual((cat.Id, onDisk), (listed.Id, listed.FilePath), "listed on the picture on disk (State: A live failure)");
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        Assert.AreEqual(change != "Revert", saved.Value!.Edited);
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task TheOriginalsBytesAreStillInTheStashAfterASaveAndTheRendersAreNot()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var saved = (await stash.SaveEdit(cat, BlackBoxAndBox)).Value!;
        var renderBytes = File.ReadAllBytes(saved.FilePath);

        var again = await store.CommitImport(TestStash.Candidate(await stash.Stage("again.png", MediaFixtures.Bytes("still.png")), "again", MediaFixtures.Bytes("still.png")));
        var render = await store.CommitImport(TestStash.Candidate(await stash.Stage("render.png", renderBytes), "render", renderBytes));

        Assert.IsTrue(again.Value!.AlreadyInStash, "G12 asks of the original");
        Assert.AreEqual(cat.Id, again.Value.Item.Id);
        Assert.IsFalse(render.Value!.AlreadyInStash);
    }

    internal static (int B, int G, int R, int A) Pixel(MediaFrame frame, int x, int y)
    {
        var at = (y * frame.Width + x) * 4;
        return (frame.Pixels[at], frame.Pixels[at + 1], frame.Pixels[at + 2], frame.Pixels[at + 3]);
    }
}
