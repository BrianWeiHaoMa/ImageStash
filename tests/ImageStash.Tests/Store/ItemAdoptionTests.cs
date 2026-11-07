using System.Diagnostics;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// An Open with… rewrite adopted as the item's original (State: Open with…; G27, G29, G12, G35, F6, F7, P1): the
/// adopted bytes under the item's name with their own format's extension, the layers dropped, their SHA-256 the row's;
/// an unedited item's previous file held in .work\, an edited item's render deleted and its original kept in
/// originals\, either named by the one undo record; Edit, Save's render check and the duplicate check reading the
/// adopted original; the listed item describing the picture on disk while the steps wait; the refusals that change
/// nothing; a later change finishing a pending adoption first; and Edit and a copy answered once a running adoption
/// has run.
/// </summary>
[TestClass]
public sealed class ItemAdoptionTests
{
    private static readonly byte[] Still = MediaFixtures.Bytes("still.png");

    [TestMethod]
    public async Task AnUneditedItemAdoptsTheRewriteUnderItsNameAndHoldsItsPreviousFileForUndo()
    {
        using var stash = new TestStash();
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var rewrite = File.ReadAllBytes(copy);

        var adopted = await OpenedWith.Adopt(stash, cat.Id);

        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(stash.Folder.InRoot("cat.png")));
        var row = EditedItems.Row(stash, cat.Id);
        var facts = new MediaFacts(ImageFormat.Png, 12, 10, false, 1, 0);
        Assert.AreEqual(facts, row.Original);
        Assert.AreEqual(("[]", (string?)null, 1L, 1L, (MediaFacts?)null, 0), (row.Layers, row.PreviousLayers, row.LayerRevision, row.RenderedRevision, row.Finished, row.Steps.Count));
        Assert.AreEqual(EditedItems.Hash(rewrite), OpenedWith.OriginalHash(stash, cat.Id), "the adopted file's hash is the original's");
        TestStash.AssertSameItem(cat with { Facts = facts, RenderedRevision = 1 }, adopted);
        TestStash.AssertSameItem(adopted, await EditedItems.Listed(stash.Store!, cat.Id));
        var held = stash.WorkFiles().Single();
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(stash.Folder.InWork(held)), "the previous original, held, never replaced (G29)");
        Assert.AreEqual(new AdoptionUndo(1, "[]", "cat.png", InOriginals: false, held, cat.Facts, EditedItems.Hash(Still), "cat.png"), OpenedWith.Undo(stash, cat.Id));
        Assert.AreEqual(new OpenWithRecord(OpenedWith.FolderOf(copy), "cat.png", EditedItems.Hash(rewrite), Awaiting: false), OpenedWith.Record(stash, cat.Id));
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(copy), "the copy is read, never written");
        Assert.IsEmpty(stash.RecycleBin.Asked, "the previous original waits for Undo or expiry");
    }

    [TestMethod]
    public async Task ARewriteInAnotherFormatTakesItsOwnFormatsExtension()
    {
        using var stash = new TestStash();
        var (cat, _) = await OpenedWith.AwaitingCat(stash, MediaFixtures.Bytes("photo.jpg"));

        var adopted = await OpenedWith.Adopt(stash, cat.Id);

        CollectionAssert.AreEqual(new[] { "cat.jpg" }, stash.RootFiles());
        Assert.AreEqual(("cat.jpg", stash.Folder.InRoot("cat.jpg"), ImageFormat.Jpeg), (adopted.FileName, adopted.FilePath, adopted.Facts.Format));
    }

    [TestMethod]
    public async Task TheAdoptedFileTakesACounterWhileAnotherItemsFileHasItsName()
    {
        using var stash = new TestStash();
        var (cat, _) = await OpenedWith.AwaitingCat(stash, MediaFixtures.Bytes("photo.jpg"));
        Assert.AreEqual("cat.jpg", (await stash.Import("cat", extension: ".jpg")).FileName);

        var adopted = await OpenedWith.Adopt(stash, cat.Id);

        Assert.AreEqual("cat 2.jpg", adopted.FileName);
        CollectionAssert.AreEquivalent(new[] { "cat.jpg", "cat 2.jpg" }, stash.RootFiles());
    }

    [TestMethod]
    public async Task AnEditedItemAdoptsInPlaceOfItsRenderAndKeepsItsOriginalUnderItsNameInOriginals()
    {
        using var stash = new TestStash();
        var (imported, cat) = await EditedItems.SavedCat(stash);
        var render = EditedItems.Hash(cat.FilePath);
        var rewrite = MediaFixtures.DistinctPng(5, 12, 10);
        await OpenedWith.AwaitingRewrite(stash, cat, rewrite);

        var adopted = await OpenedWith.Adopt(stash, cat.Id);

        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(stash.Folder.InRoot("cat.png")));
        Assert.IsEmpty(stash.WorkFiles(), "the render is deleted, never held");
        Assert.IsEmpty(stash.RecycleBin.Asked, "nor recycled (G29)");
        Assert.IsFalse(EditedItems.FilesWithHashes(stash).Any(line => line.EndsWith(render)), "the replaced render is not kept");
        CollectionAssert.AreEqual(new[] { "cat.png" }, EditedItems.OriginalFiles(stash));
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(stash.Folder.InOriginals("cat.png")));
        var row = EditedItems.Row(stash, cat.Id);
        Assert.AreEqual((false, "cat.png", "cat.png", (MediaFacts?)null), (row.OriginalInOriginals, row.FinishedFile, row.OriginalFile, row.Finished));
        Assert.AreEqual((false, new MediaFacts(ImageFormat.Png, 12, 10, false, 1, 0), 2L), (adopted.Edited, adopted.Facts, adopted.RenderedRevision));
        Assert.AreEqual(
            new AdoptionUndo(2, EditedItems.BlackBoxAndBox.ToJson(), "cat.png", InOriginals: true, null, imported.Facts, EditedItems.Hash(Still), "cat.png"),
            OpenedWith.Undo(stash, cat.Id));

        var saved = await stash.SaveEdit(adopted, EditedItems.Arrow);

        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        CollectionAssert.AreEquivalent(new[] { "cat.png", "cat 2.png" }, EditedItems.OriginalFiles(stash), "the record keeps cat.png taken in originals\\");
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(stash.Folder.InOriginals("cat.png")), "the previous original as it was (G29)");
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(stash.Folder.InOriginals("cat 2.png")));
    }

    [TestMethod]
    public async Task AnEditedItemAdoptsAnAnimationUnderTheGifExtensionAndEditOpensOnIt()
    {
        using var stash = new TestStash();
        var (_, cat) = await EditedItems.SavedCat(stash);
        var gif = MediaFixtures.Bytes("animated.gif");
        await OpenedWith.AwaitingRewrite(stash, cat, gif);

        var adopted = await OpenedWith.Adopt(stash, cat.Id);

        CollectionAssert.AreEqual(new[] { "cat.gif" }, stash.RootFiles(), "the render cat.png is gone");
        Assert.IsEmpty(stash.RecycleBin.Asked);
        Assert.IsTrue(adopted.Facts.Animated);
        var edit = await EditedItems.ForEdit(stash.Store!, cat.Id);
        Assert.AreEqual((stash.Folder.InRoot("cat.gif"), EditedItems.Hash(gif), ImageFormat.Gif), (edit.OriginalPath, Convert.ToHexString(edit.OriginalSha256), edit.FinishedFormat));
        Assert.IsTrue(edit.Layers.IsEmpty && edit.RenderedLayers.IsEmpty);
    }

    [TestMethod]
    public async Task AfterTheAdoptionEditSaveAndTheDuplicateCheckReadTheAdoptedOriginal()
    {
        using var stash = new TestStash();
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var rewrite = File.ReadAllBytes(copy);
        var stale = await stash.Render(cat, EditedItems.Arrow);
        var adopted = await OpenedWith.Adopt(stash, cat.Id);

        var edit = await EditedItems.ForEdit(stash.Store!, cat.Id);
        Assert.AreEqual((adopted.FilePath, EditedItems.Hash(rewrite), true), (edit.OriginalPath, Convert.ToHexString(edit.OriginalSha256), edit.Layers.IsEmpty));
        var before = OpenedWith.WholeRow(stash, cat.Id);
        var refused = await stash.Store!.Save(adopted, EditedItems.Arrow, stale);
        Assert.AreEqual(new Failure(ItemSave.Action, "cat", ItemSave.ChangedWhileRendered), refused.Failure, "a render of the previous original");
        Assert.AreEqual(before, OpenedWith.WholeRow(stash, cat.Id), "nothing committed");
        Assert.IsTrue(File.Exists(stale.WorkPath), "the render is the caller's");
        var saved = await stash.SaveEdit(adopted, EditedItems.Arrow);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());

        var again = await Imported(stash, rewrite);
        var previous = await Imported(stash, Still);

        Assert.AreEqual((true, cat.Id), (again.AlreadyInStash, again.Item.Id), "the adopted bytes are the item's original (G12)");
        Assert.IsFalse(previous.AlreadyInStash, "the previous original is no item's any more");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ARenderMadeBeforeAPendingAdoptionIsRefusedOnceTheChangeHasFinishedTheAdoption(bool asNewItem)
    {
        using var stash = new TestStash();
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var stale = await stash.Render(cat, EditedItems.Arrow);
        using (TestStash.Hold(cat.FilePath))
        {
            Assert.IsNotNull((await stash.Store!.AdoptRewrite(cat.Id)).Failure, "committed with its hold refused");
        }

        var refused = asNewItem ? await stash.Store!.SaveAsNewItem(cat, EditedItems.Arrow, stale) : await stash.Store!.Save(cat, EditedItems.Arrow, stale);

        Assert.AreEqual(new Failure(asNewItem ? ItemSaveAsNewItem.Action : ItemSave.Action, "cat", ItemSave.ChangedWhileRendered), refused.Failure);
        var listed = await EditedItems.Listed(stash.Store!, cat.Id);
        Assert.AreEqual((false, stash.Folder.InRoot("cat.png")), (listed.Edited, listed.FilePath));
        CollectionAssert.AreEqual(File.ReadAllBytes(copy), File.ReadAllBytes(listed.FilePath), "the adoption finished first");
        Assert.IsEmpty(EditedItems.Row(stash, cat.Id).Steps);
        Assert.IsTrue(File.Exists(stale.WorkPath), "the render is the caller's");
        Assert.HasCount(1, await stash.All(), "no second item");
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task OnceTheAdoptedFileHasLandedTheDuplicateCheckComparesItThoughTheRowStillHoldsThePreviousHash()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var rewrite = File.ReadAllBytes(copy);
        List<FileStream> holds = [];
        stash.Reader = OpenedWith.Holding(holds);

        // The place waits in its retry while the taken file is held; the store is held before it is let go, so the file
        // lands and the last transaction is refused (polling for the landed file instead would race that transaction).
        var adopting = stash.Store!.AdoptRewrite(cat.Id);
        await Until(() => EditedItems.Row(stash, cat.Id).Steps.Count > 0 && holds.Count == 1);
        Outcome<StashItem> adopted;
        using (stash.HoldStore())
        {
            holds.ForEach(hold => hold.Dispose());
            adopted = await adopting;
        }

        Assert.IsNotNull(adopted.Failure);
        var listed = await EditedItems.Listed(stash.Store, cat.Id);
        Assert.AreEqual((stash.Folder.InRoot("cat.png"), new MediaFacts(ImageFormat.Png, 12, 10, false, 1, 0), 1L), (listed.FilePath, listed.Facts, listed.RenderedRevision), "listed as adopted");
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(listed.FilePath));
        Assert.AreEqual(EditedItems.Hash(Still), OpenedWith.OriginalHash(stash, cat.Id), "the column still the previous original's");
        Assert.AreEqual(EditedItems.Hash(rewrite), Convert.ToHexString((await EditedItems.ForEdit(stash.Store, cat.Id)).OriginalSha256));

        var again = await Imported(stash, rewrite);
        var previous = await Imported(stash, Still);

        Assert.AreEqual((true, cat.Id), (again.AlreadyInStash, again.Item.Id), "the adopted bytes are already in the stash (G12)");
        Assert.IsFalse(previous.AlreadyInStash, "the previous original's bytes are added");
        var saved = await stash.SaveEdit(listed, EditedItems.Arrow);
        Assert.IsTrue(saved.Succeeded, $"a render of the adopted original goes, the Save running the last transaction first: {saved.Failure}");
        Assert.AreEqual(EditedItems.Hash(rewrite), OpenedWith.OriginalHash(stash, cat.Id));
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(stash.Folder.InOriginals("cat.png")));
    }

    [TestMethod]
    public async Task WithNothingAwaitingAdoptionIsRefusedByNameAndChangesNothing()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var beforeSession = OpenedWith.WholeRow(stash, cat.Id);
        var noSession = await stash.Store!.AdoptRewrite(cat.Id);
        var afterNoSession = OpenedWith.WholeRow(stash, cat.Id);
        await OpenedWith.Begin(stash, cat);
        var before = OpenedWith.WholeRow(stash, cat.Id);

        var unchanged = await stash.Store.AdoptRewrite(cat.Id);

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", ItemAdoption.NoRewrite), noSession.Failure);
        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", ItemAdoption.NoRewrite), unchanged.Failure);
        Assert.AreEqual((beforeSession, before), (afterNoSession, OpenedWith.WholeRow(stash, cat.Id)));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task ACopyStillHeldIsRefusedAsHeldWithTheMarkKeptAndTheStoreAnswersBetweenTheAttempts()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var before = OpenedWith.WholeRow(stash, cat.Id);
        Outcome<StashItem> refused;
        bool answeredInTime, adoptingWhenAnswered;
        using (TestStash.Hold(copy))
        {
            var adopting = stash.Store!.AdoptRewrite(cat.Id);
            await Task.Delay(200);
            // Waited for on this thread, which the store's own answer wakes.
            var all = stash.Store.All();
            answeredInTime = all.Wait(TimeSpan.FromMilliseconds(500));
            adoptingWhenAnswered = adopting.IsCompleted;
            refused = await adopting;
        }

        Assert.IsTrue(answeredInTime, "within half a second (G1)");
        Assert.IsFalse(adoptingWhenAnswered);
        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat.png", HeldFileRetry.HeldReason), refused.Failure);
        Assert.AreEqual(before, OpenedWith.WholeRow(stash, cat.Id), "the mark kept, nothing changed");
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsTrue((await stash.Store!.AdoptRewrite(cat.Id)).Succeeded, "adopted once let go");
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AStoreCallAskedWhileAnAdoptionsStepWaitsOnAHeldFileIsAnsweredBetweenTheAttempts()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        var (cat, _) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        List<FileStream> holds = [];
        stash.Reader = OpenedWith.Holding(holds);

        var watch = Stopwatch.StartNew();
        var adopting = stash.Store!.AdoptRewrite(cat.Id);
        await Until(() => EditedItems.Row(stash, cat.Id).Steps.Count > 0);
        await Task.Delay(200);
        // Waited for on this thread, which the store's own answer wakes.
        var all = stash.Store.All();
        var answeredInTime = all.Wait(TimeSpan.FromMilliseconds(500));
        var adoptingWhenAnswered = adopting.IsCompleted;
        var adopted = await adopting;
        holds.ForEach(hold => hold.Dispose());

        Assert.IsTrue(answeredInTime, "within half a second (G1)");
        Assert.IsTrue(all.Result.Succeeded, all.Result.Failure?.ToString());
        Assert.IsFalse(adoptingWhenAnswered, "answered while the place still waited");
        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", HeldFileRetry.HeldReason), adopted.Failure);
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(3), watch.Elapsed, "once the retry has run");
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AnAdoptionAnotherChangeFinishedBetweenItsAttemptsIsNotRunAgain()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var hold = TestStash.Hold(cat.FilePath);
        var adopting = stash.Store!.AdoptRewrite(cat.Id);
        await Until(() => EditedItems.Row(stash, cat.Id).Steps.Count > 0);

        // The rename finishes the adoption first, inside its own retry, ahead of the adoption's next attempt.
        var renaming = stash.Store.Rename(cat, "dog");
        await Task.Delay(300);
        hold.Dispose();
        var renamed = await renaming;
        var adopted = await adopting;

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        Assert.IsTrue(adopted.Succeeded, adopted.Failure?.ToString());
        Assert.AreEqual(("dog", "dog.png"), (adopted.Value!.Name, adopted.Value.FileName), "the item as the stash lists it then");
        var row = EditedItems.Row(stash, cat.Id);
        Assert.AreEqual(("dog.png", "dog.png", 0), (row.FinishedFile, row.OriginalFile, row.Steps.Count), "the adoption's last transaction not run again over the rename");
        CollectionAssert.AreEqual(File.ReadAllBytes(copy), File.ReadAllBytes(stash.Folder.InRoot("dog.png")));
    }

    [TestMethod]
    public async Task ARewriteThatNoLongerDecodesIsRefusedAsUnreadableWithItsHashRecordedAndTheMarkCleared()
    {
        using var stash = new TestStash();
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var files = EditedItems.FilesWithHashes(stash);
        var garbage = OpenedWith.Garbage();
        OpenedWith.Rewrite(copy, garbage);

        var refused = await stash.Store!.AdoptRewrite(cat.Id);

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat.png", MediaDecoder.UnreadableReason), refused.Failure);
        Assert.AreEqual(new OpenWithRecord(OpenedWith.FolderOf(copy), "cat.png", EditedItems.Hash(garbage), Awaiting: false), OpenedWith.Record(stash, cat.Id));
        CollectionAssert.AreEqual(files, EditedItems.FilesWithHashes(stash), "the item keeps its previous original (G27)");
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", ItemAdoption.NoRewrite), (await stash.Store.AdoptRewrite(cat.Id)).Failure);
    }

    [TestMethod]
    public async Task AnItemADeleteMarkedIsRefusedAsNoLongerInTheStash()
    {
        using var stash = new TestStash();
        var (cat, _) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        Assert.IsTrue((await stash.Store!.Delete([cat.Id])).Single().Succeeded);
        var before = OpenedWith.WholeRow(stash, cat.Id);

        var refused = await stash.Store.AdoptRewrite(cat.Id);

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", ItemFactChange.NotInStash), refused.Failure);
        Assert.AreEqual(before, OpenedWith.WholeRow(stash, cat.Id));
    }

    [TestMethod]
    public async Task APendingSaveThatCannotFinishRefusesTheAdoptionAndOnceItCanTheAdoptionFollowsIt()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await EditedItems.FirstSaveMoved(stash);
        await OpenedWith.AwaitingRewrite(stash, (await EditedItems.Listed(stash.Store!, cat.Id)), MediaFixtures.DistinctPng(5, 12, 10));
        var before = OpenedWith.WholeRow(stash, cat.Id);
        var work = stash.WorkFiles();
        stash.Renderer = EditedItems.Refusing;

        var refused = await stash.Store!.AdoptRewrite(cat.Id);

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", "its earlier save cannot finish yet: " + EditedItems.RefusedReason), refused.Failure);
        Assert.AreEqual(before, OpenedWith.WholeRow(stash, cat.Id), "the mark kept, the Save still pending");
        CollectionAssert.AreEquivalent(work, stash.WorkFiles(), "the taken file is discarded");
        stash.Renderer = FinishedPicture.Render;

        var adopted = await OpenedWith.Adopt(stash, cat.Id);

        Assert.AreEqual("cat.png", adopted.FileName);
        CollectionAssert.AreEqual(new[] { "cat.png" }, EditedItems.OriginalFiles(stash), "the Save finished first, its original in originals\\");
        Assert.AreEqual((true, EditedItems.BlackBoxAndBox.ToJson()), (OpenedWith.Undo(stash, cat.Id)!.InOriginals, OpenedWith.Undo(stash, cat.Id)!.Layers));
        Assert.IsEmpty(stash.WorkFiles(), "the finished Save's render replaced, not kept");
    }

    [TestMethod]
    public async Task WhileItsHoldIsRefusedTheItemIsThePreviousPictureAndARenameFinishesTheAdoptionFirst()
    {
        using var stash = new TestStash();
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        Outcome<StashItem> adopting;
        StashItem listed;
        ItemForEdit edit;
        using (TestStash.Hold(cat.FilePath))
        {
            adopting = await stash.Store!.AdoptRewrite(cat.Id);
            listed = await EditedItems.Listed(stash.Store, cat.Id);
            edit = await EditedItems.ForEdit(stash.Store, cat.Id);
        }

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", HeldFileRetry.HeldReason), adopting.Failure);
        Assert.IsNotNull(adopting.Value, "failed after the commit");
        Assert.HasCount(2, EditedItems.Row(stash, cat.Id).Steps);
        Assert.AreEqual((stash.Folder.InRoot("cat.png"), cat.Facts, cat.RenderedRevision), (listed.FilePath, listed.Facts, listed.RenderedRevision), "the picture on disk");
        Assert.AreEqual((cat.FilePath, EditedItems.Hash(Still)), (edit.OriginalPath, Convert.ToHexString(edit.OriginalSha256)));

        var renamed = await stash.Store!.Rename(listed, "dog");

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { "dog.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(File.ReadAllBytes(copy), File.ReadAllBytes(stash.Folder.InRoot("dog.png")));
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(stash.Folder.InWork(OpenedWith.Undo(stash, cat.Id)!.HeldCopy!)), "still held for Undo");
    }

    [TestMethod]
    public async Task WhileTheAdoptedFileCannotBePlacedTheItemIsNotListed()
    {
        using var stash = new TestStash();
        var (cat, _) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        List<FileStream> holds = [];
        stash.Reader = OpenedWith.Holding(holds);

        var adopting = await stash.Store!.AdoptRewrite(cat.Id);
        var whileHeld = await stash.Store.Item(cat.Id);
        holds.ForEach(hold => hold.Dispose());

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", HeldFileRetry.HeldReason), adopting.Failure);
        Assert.IsNull(whileHeld.Value, "its picture is in .work\\, where nothing is an item");
        Assert.HasCount(2, stash.WorkFiles(), "the held previous original and the adopted file");
        Assert.IsTrue((await stash.Store.Rename(cat, "cat")).Failure is { Reason: ItemFactChange.NotInStash }, "no change reaches it until the next start");
    }

    [TestMethod]
    public async Task WhileAnEditedItemsRenderCannotBeRemovedTheItemIsTheRenderAndEditOpensOnItsLayers()
    {
        using var stash = new TestStash();
        var (_, cat) = await EditedItems.SavedCat(stash);
        await OpenedWith.AwaitingRewrite(stash, cat, MediaFixtures.DistinctPng(5, 12, 10));
        Outcome<StashItem> adopting;
        StashItem listed;
        ItemForEdit edit;
        using (TestStash.Hold(cat.FilePath))
        {
            adopting = await stash.Store!.AdoptRewrite(cat.Id);
            listed = await EditedItems.Listed(stash.Store, cat.Id);
            edit = await EditedItems.ForEdit(stash.Store, cat.Id);
        }

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", HeldFileRetry.HeldReason), adopting.Failure);
        Assert.AreEqual((cat.FilePath, cat.Facts, cat.RenderedRevision), (listed.FilePath, listed.Facts, listed.RenderedRevision), "the render, with its facts");
        Assert.AreEqual((stash.Folder.InOriginals("cat.png"), EditedItems.Hash(Still), EditedItems.BlackBoxAndBox.ToJson()), (edit.OriginalPath, Convert.ToHexString(edit.OriginalSha256), edit.RenderedLayers.ToJson()), "Edit opens on the layers the render was made from (State: A live failure)");
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(false, false, DisplayName = "unedited, adopting another format, its step waiting on a held file")]
    [DataRow(true, false, DisplayName = "edited, its step waiting on a held file")]
    [DataRow(false, true, DisplayName = "unedited, adopting another format, while it reads the copy it took")]
    public async Task EditAndACopyAskedWhileTheAdoptionRunsAreAnsweredOnceItHasRun(bool edited, bool whileReading)
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        if (edited)
        {
            cat = (await stash.SaveEdit(cat, EditedItems.BlackBoxAndBox)).Value!;
        }

        var rewrite = edited ? MediaFixtures.DistinctPng(5, 12, 10) : MediaFixtures.Bytes("photo.jpg");
        await OpenedWith.AwaitingRewrite(stash, cat, rewrite);
        using var reading = new SemaphoreSlim(0);
        using var resume = new ManualResetEventSlim();
        if (whileReading)
        {
            stash.Reader = (path, name, retry) =>
            {
                reading.Release();
                resume.Wait(TimeSpan.FromSeconds(10));
                return MediaDecoder.ReadPicture(path, name, retry);
            };
        }

        Task<Outcome<StashItem>> adopting;
        Task<Outcome<ItemForEdit?>> editing;
        Task<Confirmation> copying;
        bool answeredWhileRunning;
        using (whileReading ? null : TestStash.Hold(cat.FilePath))
        {
            adopting = stash.Store!.AdoptRewrite(cat.Id);
            if (whileReading)
            {
                Assert.IsTrue(await reading.WaitAsync(TimeSpan.FromSeconds(10)), "the adoption never read the copy it took");
            }
            else
            {
                await EditorSessions.Until(() => EditedItems.Row(stash, cat.Id).Steps.Count > 0, "the adoption never committed");
            }

            editing = stash.Store.ForEdit(cat.Id);
            copying = stash.Store.Confirm([cat], "Copy");
            await Task.Delay(300);
            answeredWhileRunning = editing.IsCompleted || copying.IsCompleted;
            resume.Set();
        }

        var adopted = await adopting;
        var edit = (await editing).Value!;
        using var copy = await copying;

        Assert.IsTrue(adopted.Succeeded, adopted.Failure?.ToString());
        Assert.IsFalse(answeredWhileRunning, "neither answered while the adoption ran");
        Assert.AreEqual((adopted.Value!.FilePath, EditedItems.Hash(rewrite), true, true), (edit.OriginalPath, Convert.ToHexString(edit.OriginalSha256), edit.Layers.IsEmpty, edit.RenderedLayers.IsEmpty), "Edit opens once the adoption has run, on the adopted original (G20)");
        Assert.AreEqual(adopted.Value.FilePath, copy.Items.Single().Value?.FilePath, "the copy hands over the adopted file, never a path the adoption emptied (G8)");
    }

    /// <summary>Imports <paramref name="bytes"/>, a PNG, as the product's import does, whether or not the stash already holds them.</summary>
    private static async Task<ImportResult> Imported(TestStash stash, byte[] bytes)
    {
        var staged = await stash.Stage("source.png", bytes);
        var read = MediaDecoder.ReadFacts(staged, "source.png", TestStash.ShortRetry);
        var committed = await stash.Store!.CommitImport(new ImportCandidate(staged, "source", read.Value!.Facts, read.Value.Sha256));
        Assert.IsTrue(committed.Succeeded, committed.Failure?.ToString());
        return committed.Value!;
    }

    private static async Task Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.IsLessThan(TimeSpan.FromSeconds(10), watch.Elapsed, "the store never got there");
            await Task.Delay(5);
        }
    }
}
