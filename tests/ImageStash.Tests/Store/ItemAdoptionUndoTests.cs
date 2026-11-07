using System.Runtime.Versioning;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// The Undo of an Open with… adoption (State: Open with…; G27, G29, G25, G18, G12, P1): the previous original and layers
/// back under the item's current name, an edited previous's finished picture rendered again, the adopted picture sent to
/// the Recycle Bin last and the record cleared; nothing done for an adoption no longer live or past a later Save; the
/// refusals that change nothing; the listed item, Edit and the duplicate check describing the picture on disk while
/// the Undo's recycle is still owed; and the summon's listing answered while a change finishing that recycle waits on its
/// held file (G1); and Edit and a copy answered once a running Undo has run (G20, G8).
/// </summary>
[TestClass]
public sealed class ItemAdoptionUndoTests
{
    private static readonly byte[] Still = MediaFixtures.Bytes("still.png");

    private static readonly byte[] Rewrite = MediaFixtures.DistinctPng(5, 12, 10);

    [TestMethod]
    public async Task UndoOnAnUneditedItemPutsItsOriginalBackAndRecyclesTheAdoptedPictureOnceItIsBack()
    {
        using var stash = new TestStash();
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, Rewrite);
        var before = OpenedWith.WholeRowBut(stash, cat.Id, "layer_revision", "rendered_revision", "open_with_session");
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        var backWhenRecycled = new List<bool>();
        stash.RecycleBin.BeforeEach = _ => backWhenRecycled.Add(OpenedWith.Holds(stash.Folder.InRoot("cat.png"), Still));

        var undone = await OpenedWith.UndoAdoption(stash, cat.Id, adopted.RenderedRevision);

        TestStash.AssertSameItem(cat with { RenderedRevision = 2 }, undone);
        TestStash.AssertSameItem(undone, await EditedItems.Listed(stash.Store!, cat.Id));
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(stash.Folder.InRoot("cat.png")));
        Assert.AreEqual(before, OpenedWith.WholeRowBut(stash, cat.Id, "layer_revision", "rendered_revision", "open_with_session"), "the row as imported, the record cleared");
        var row = EditedItems.Row(stash, cat.Id);
        Assert.AreEqual(("[]", 2L, 2L), (row.Layers, row.LayerRevision, row.RenderedRevision));
        Assert.AreEqual(EditedItems.Hash(Still), OpenedWith.OriginalHash(stash, cat.Id));
        Assert.IsTrue(stash.Folder.IsWorkFile(stash.RecycleBin.Asked.Single()), "the set-aside file, from .work\\");
        CollectionAssert.AreEqual(new[] { true }, backWhenRecycled, "recycled once the previous original was back");
        CollectionAssert.AreEqual(new[] { EditedItems.Hash(Rewrite) }, OpenedWith.Recycled(stash));
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
        Assert.AreEqual(new OpenWithRecord(OpenedWith.FolderOf(copy), "cat.png", EditedItems.Hash(Rewrite), Awaiting: false), OpenedWith.Record(stash, cat.Id));
        Assert.AreEqual(OpenWithCopy.Unchanged, await OpenedWith.Classify(stash, cat.Id), "the rewrite is not adopted again");
    }

    [TestMethod]
    public async Task UndoWhoseAdoptedPictureWindowsRemovesForGoodIsCompletedWithTheRecordCleared()
    {
        // G29: a removal for good completes the Undo's recycle of the outside version (G27).
        using var stash = new TestStash();
        var (cat, _) = await OpenedWith.AwaitingCat(stash, Rewrite);
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        stash.RecycleBin.RemovingForGood = _ => true;

        var undone = await OpenedWith.UndoAdoption(stash, cat.Id, adopted.RenderedRevision);

        TestStash.AssertSameItem(cat with { RenderedRevision = 2 }, undone);
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(stash.Folder.InRoot("cat.png")));
        Assert.IsTrue(stash.Folder.IsWorkFile(stash.RecycleBin.Asked.Single()), "the set-aside file, from .work\\");
        Assert.IsEmpty(OpenedWith.Recycled(stash), "removed for good, nothing in the bin");
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
        Assert.IsEmpty(EditedItems.Row(stash, cat.Id).Steps);
    }

    [TestMethod]
    public async Task UndoAfterARewriteInAnotherFormatPutsTheOriginalBackUnderItsOwnExtension()
    {
        using var stash = new TestStash();
        var jpeg = MediaFixtures.Bytes("photo.jpg");
        var (cat, _) = await OpenedWith.AwaitingCat(stash, jpeg);
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        Assert.AreEqual("cat.jpg", adopted.FileName);

        var undone = await OpenedWith.UndoAdoption(stash, cat.Id, adopted.RenderedRevision);

        Assert.AreEqual(("cat.png", cat.Facts), (undone.FileName, undone.Facts));
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(undone.FilePath));
        CollectionAssert.AreEqual(new[] { EditedItems.Hash(jpeg) }, OpenedWith.Recycled(stash));
    }

    [TestMethod]
    public async Task UndoOnAnEditedItemRendersItsLayersAgainOverItsOriginal()
    {
        using var stash = new TestStash();
        var (imported, cat) = await EditedItems.SavedCat(stash);
        await OpenedWith.AwaitingRewrite(stash, cat, Rewrite);
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        stash.RenderCalls.Clear();

        var undone = await OpenedWith.UndoAdoption(stash, cat.Id, adopted.RenderedRevision);

        var expected = stash.Outside("expected.png");
        Assert.IsTrue(FinishedPicture.Render(stash.Folder.InOriginals("cat.png"), "cat", EditedItems.BlackBoxAndBox, ImageFormat.Png, expected, TestStash.ShortRetry).Succeeded);
        Assert.AreEqual((stash.Folder.InOriginals("cat.png"), EditedItems.BlackBoxAndBox.ToJson(), ImageFormat.Png), (stash.RenderCalls.Single().OriginalPath, stash.RenderCalls[0].Layers.ToJson(), stash.RenderCalls[0].Format));
        CollectionAssert.AreEqual(File.ReadAllBytes(expected), File.ReadAllBytes(stash.Folder.InRoot("cat.png")), "the previous layers rendered again over the previous original");
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "cat.png" }, EditedItems.OriginalFiles(stash));
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(stash.Folder.InOriginals("cat.png")), "the original as it was (G29)");
        TestStash.AssertSameItem(cat with { RenderedRevision = 3 }, undone);
        var row = EditedItems.Row(stash, cat.Id);
        Assert.AreEqual((EditedItems.BlackBoxAndBox.ToJson(), (string?)null, 3L, 3L, true, imported.Facts, cat.Facts, 0),
            (row.Layers, row.PreviousLayers, row.LayerRevision, row.RenderedRevision, row.OriginalInOriginals, row.Original, row.Finished, row.Steps.Count));
        var edit = await EditedItems.ForEdit(stash.Store!, cat.Id);
        Assert.AreEqual((stash.Folder.InOriginals("cat.png"), EditedItems.Hash(Still), EditedItems.BlackBoxAndBox.ToJson()), (edit.OriginalPath, Convert.ToHexString(edit.OriginalSha256), edit.Layers.ToJson()));
        CollectionAssert.AreEqual(new[] { EditedItems.Hash(Rewrite) }, OpenedWith.Recycled(stash));
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
    }

    [TestMethod]
    public async Task UndoAfterAnAnimationReplacedAnEditedItemsRenderPlacesTheRenderAgain()
    {
        using var stash = new TestStash();
        var (_, cat) = await EditedItems.SavedCat(stash);
        var gif = MediaFixtures.Bytes("animated.gif");
        await OpenedWith.AwaitingRewrite(stash, cat, gif);
        var adopted = await OpenedWith.Adopt(stash, cat.Id);

        await OpenedWith.UndoAdoption(stash, cat.Id, adopted.RenderedRevision);

        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles(), "cat.gif gone, the render cat.png placed");
        CollectionAssert.AreEqual(new[] { EditedItems.Hash(gif) }, OpenedWith.Recycled(stash));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UndoAfterARenameNamesThePreviousPictureAfterTheItemsCurrentName(bool edited)
    {
        using var stash = new TestStash();
        var (_, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited);
        var renamed = await stash.Store!.Rename(adopted, "fox");
        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());

        var undone = await OpenedWith.UndoAdoption(stash, adopted.Id, adopted.RenderedRevision);

        Assert.AreEqual(("fox", "fox.png"), (undone.Name, undone.FileName));
        CollectionAssert.AreEquivalent(new[] { "dog.png", "fox.png" }, stash.RootFiles());
        var row = EditedItems.Row(stash, adopted.Id);
        Assert.AreEqual(("fox.png", "fox.png", edited), (row.FinishedFile, row.OriginalFile, row.OriginalInOriginals));
        if (edited)
        {
            CollectionAssert.AreEqual(new[] { "fox.png" }, EditedItems.OriginalFiles(stash), "the original keeps the item's name under originals\\, cat.png gone");
            CollectionAssert.AreEqual(Still, File.ReadAllBytes(stash.Folder.InOriginals("fox.png")));
        }
        else
        {
            CollectionAssert.AreEqual(Still, File.ReadAllBytes(stash.Folder.InRoot("fox.png")));
        }
    }

    [TestMethod]
    [DataRow("another revision")]
    [DataRow("expired")]
    [DataRow("undone")]
    [DataRow("saved")]
    [DataRow("marked")]
    [DataRow("removed")]
    public async Task UndoOfAnAdoptionNoLongerLiveDoesNothing(string since)
    {
        using var stash = new TestStash();
        var (cat, _) = await OpenedWith.AwaitingCat(stash, Rewrite);
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        var revision = adopted.RenderedRevision;
        switch (since)
        {
            case "another revision":
                revision++;
                break;
            case "expired":
                Assert.IsTrue((await stash.Store!.ExpireAdoption(cat.Id, revision)).Succeeded);
                break;
            case "undone":
                await OpenedWith.UndoAdoption(stash, cat.Id, revision);
                break;
            case "saved":
                Assert.IsTrue((await stash.SaveEdit(adopted, EditedItems.Arrow)).Succeeded);
                break;
            case "marked":
                Assert.IsTrue((await stash.Store!.Delete([cat.Id])).Single().Succeeded);
                break;
            case "removed":
                Assert.IsTrue((await stash.Store!.Remove([cat.Id])).Single().Succeeded);
                break;
        }

        var row = since == "removed" ? null : OpenedWith.WholeRow(stash, cat.Id);
        var files = EditedItems.FilesWithHashes(stash);
        var asked = stash.RecycleBin.Asked.Count;

        var undone = await stash.Store!.UndoAdoption(cat.Id, revision);

        Assert.IsTrue(undone.Succeeded, undone.Failure?.ToString());
        Assert.IsNull(undone.Value);
        Assert.HasCount(asked, stash.RecycleBin.Asked, "the Recycle Bin asked nothing");
        CollectionAssert.AreEqual(files, EditedItems.FilesWithHashes(stash));
        if (row is not null)
        {
            Assert.AreEqual(row, OpenedWith.WholeRow(stash, cat.Id));
        }

        if (since == "saved")
        {
            Assert.IsTrue((await EditedItems.Listed(stash.Store, cat.Id)).Edited, "the Save stands (G25)");
            Assert.IsTrue((await stash.Store.ExpireAdoption(cat.Id, adopted.RenderedRevision)).Succeeded);
            CollectionAssert.AreEqual(new[] { EditedItems.Hash(Still) }, OpenedWith.Recycled(stash), "the previous original still reaches the Recycle Bin (G29)");
        }
    }

    [TestMethod]
    public async Task UndoWhosePictureCannotBeRenderedIsRefusedWithNothingChanged()
    {
        using var stash = new TestStash();
        var (_, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited: true);
        var row = OpenedWith.WholeRow(stash, adopted.Id);
        var files = EditedItems.FilesWithHashes(stash);
        stash.Renderer = EditedItems.Refusing;

        var refused = await stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision);

        Assert.AreEqual(new Failure(ItemAdoptionUndo.Action, "cat", EditedItems.RefusedReason), refused.Failure);
        Assert.AreEqual(row, OpenedWith.WholeRow(stash, adopted.Id), "the record live, nothing committed");
        CollectionAssert.AreEqual(files, EditedItems.FilesWithHashes(stash), "nothing moved");
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(stash.RecycleBin.Asked);
        stash.Renderer = FinishedPicture.Render;
        await OpenedWith.UndoAdoption(stash, adopted.Id, adopted.RenderedRevision);
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task UndoWhosePreviousOriginalIsHeldPastTheRetryIsRefusedAsHeld()
    {
        using var stash = new TestStash();
        var (_, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited: true);
        var row = OpenedWith.WholeRow(stash, adopted.Id);

        var refused = HeldFiles.WhileLocked(stash.Folder.InOriginals("cat.png"), () => stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision).GetAwaiter().GetResult());

        Assert.AreEqual(new Failure(ItemAdoptionUndo.Action, "cat", HeldFileRetry.HeldReason), refused.Failure);
        Assert.AreEqual(row, OpenedWith.WholeRow(stash, adopted.Id));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task UndoOverAnExpiryThatRecycledThePreviousOriginalButCouldNotClearItsRecordIsRefusedWithNothingChanged()
    {
        using var stash = new TestStash();
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, Rewrite);
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        OpenedWith.Rewrite(copy, MediaFixtures.DistinctPng(6, 9, 7));
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id));
        // A second adoption expires the first's record: its previous original reaches the Recycle Bin, then the store is
        // held so the transaction clearing the record is refused (E3), and the second adoption with it.
        IDisposable? held = null;
        stash.RecycleBin.BeforeEach = _ => held = stash.HoldStore();
        var second = await stash.Store!.AdoptRewrite(cat.Id);
        held!.Dispose();
        stash.RecycleBin.BeforeEach = null;
        Assert.IsNotNull(second.Failure, "the second adoption refused with its expiry");
        CollectionAssert.AreEqual(new[] { EditedItems.Hash(Still) }, OpenedWith.Recycled(stash), "the previous original recycled");
        var row = OpenedWith.WholeRow(stash, cat.Id);
        var files = EditedItems.FilesWithHashes(stash);

        var refused = await stash.Store.UndoAdoption(cat.Id, adopted.RenderedRevision);

        Assert.AreEqual(new Failure(ItemAdoptionUndo.Action, "cat", ItemAdoptionUndo.PreviousGone), refused.Failure);
        Assert.AreEqual(row, OpenedWith.WholeRow(stash, cat.Id), "nothing committed");
        CollectionAssert.AreEqual(files, EditedItems.FilesWithHashes(stash));
        CollectionAssert.AreEqual(Rewrite, File.ReadAllBytes(adopted.FilePath), "the adopted picture stays the item's, under its own facts");
        Assert.IsTrue((await stash.Store.ExpireAdoption(cat.Id, adopted.RenderedRevision)).Succeeded, "the expiry then clears the record");
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
    }

    [TestMethod]
    public async Task ASecondAdoptionsUndoRestoresTheFirstAdoptedPictureAndTheFirstsUndoDoesNothing()
    {
        using var stash = new TestStash();
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, Rewrite);
        var first = await OpenedWith.Adopt(stash, cat.Id);
        var second = MediaFixtures.DistinctPng(6, 9, 7);
        OpenedWith.Rewrite(copy, second);
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id));
        var adoptedAgain = await OpenedWith.Adopt(stash, cat.Id);

        var stale = await stash.Store!.UndoAdoption(cat.Id, first.RenderedRevision);
        var undone = await OpenedWith.UndoAdoption(stash, cat.Id, adoptedAgain.RenderedRevision);

        Assert.IsTrue(stale.Succeeded && stale.Value is null, "the first adoption's record expired with the second");
        Assert.AreEqual(first.Facts, undone.Facts);
        CollectionAssert.AreEqual(Rewrite, File.ReadAllBytes(undone.FilePath), "to the state just before the latest adoption");
        CollectionAssert.AreEquivalent(new[] { EditedItems.Hash(Still), EditedItems.Hash(second) }, OpenedWith.Recycled(stash));
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
    }

    [TestMethod]
    public async Task ADeletesUndoLeavesTheSessionAndTheRecordForTheAdoptionsUndo()
    {
        using var stash = new TestStash();
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, Rewrite);
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        var deleted = (await stash.Store!.Delete([cat.Id])).Single();
        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        var whileDeleted = (await stash.Store.OpenWithSessions()).Value!.Sessions;
        Assert.IsTrue((await stash.Store.UndoDelete([deleted.Value!])).Single().Succeeded);

        var undone = await OpenedWith.UndoAdoption(stash, cat.Id, adopted.RenderedRevision);

        CollectionAssert.AreEqual(new[] { new OpenWithSession(cat.Id, copy) }, whileDeleted.ToList());
        TestStash.AssertSameItem(cat with { RenderedRevision = 2 }, undone);
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(stash.Folder.InRoot("cat.png")));
        CollectionAssert.AreEqual(new[] { new OpenWithSession(cat.Id, copy) }, (await stash.Store.OpenWithSessions()).Value!.Sessions.ToList());
    }

    [TestMethod]
    public async Task AnExpiryAskedWhileTheUndoWaitsFinishesItAndRecyclesNothingOfTheRestoredItem()
    {
        using var stash = new TestStash();
        var (before, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited: true);
        stash.RecycleBin.Refusing = path => OpenedWith.Holds(path, Rewrite);
        Assert.IsNotNull((await stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision)).Failure);
        stash.RecycleBin.Refusing = _ => false;

        var expired = await stash.Store.ExpireAdoption(adopted.Id, adopted.RenderedRevision);

        Assert.IsTrue(expired.Succeeded, expired.Failure?.ToString());
        TestStash.AssertSameItem(before with { RenderedRevision = adopted.RenderedRevision + 1 }, await EditedItems.Listed(stash.Store, adopted.Id));
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(stash.Folder.InOriginals("cat.png")), "the restored original stays the item's (G29)");
        CollectionAssert.AreEqual(new[] { EditedItems.Hash(Rewrite) }, OpenedWith.Recycled(stash), "the adopted picture alone");
        Assert.IsNull(OpenedWith.Undo(stash, adopted.Id));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AChangeAskedWhileTheUndoWaitsRendersItAgainAndTheWaitingUndoKeepsToTheStepsRecordedNow()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromMilliseconds(900) };
        var (before, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited: true);
        stash.RenderCalls.Clear();
        PendingStep made;
        Outcome<StashItem> renamed;
        Outcome<StashItem?> undone;
        using (TestStash.Hold(adopted.FilePath))
        {
            var undoing = stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision);
            await EditorSessions.Until(() => EditedItems.Row(stash, adopted.Id).Steps.Count > 0, "the Undo never committed");
            made = EditedItems.Row(stash, adopted.Id).Steps.Single(step => step.Step == PendingStep.PlaceRender);
            // Asked between the waiting Undo's attempts: its finish of the Undo renders the picture again and records the
            // steps again before the held picture refuses it too.
            renamed = await stash.Store.Rename(adopted, "fox");
            undone = await undoing;
        }

        var recorded = EditedItems.Row(stash, adopted.Id).Steps.Single(step => step.Step == PendingStep.PlaceRender);
        Assert.AreEqual($"its earlier undo of an adoption cannot finish yet: {HeldFileRetry.HeldReason}", renamed.Failure?.Reason);
        Assert.AreEqual(HeldFileRetry.HeldReason, undone.Failure?.Reason, "not done: the render it made was replaced, never placed");
        Assert.HasCount(2, stash.RenderCalls, "rendered again by the Rename's finish");
        Assert.AreNotEqual(made.From, recorded.From);
        Assert.IsFalse(File.Exists(made.FromPath(stash.Folder)), "the Undo's own render discarded");
        Assert.IsTrue(File.Exists(recorded.FromPath(stash.Folder)), "the render recorded now still to place");
        CollectionAssert.AreEqual(Rewrite, File.ReadAllBytes((await EditedItems.Listed(stash.Store!, adopted.Id)).FilePath), "still the adopted picture");
        var edit = await EditedItems.ForEdit(stash.Store!, adopted.Id);
        Assert.AreEqual((adopted.FilePath, EditedItems.Hash(Rewrite), true), (edit.OriginalPath, Convert.ToHexString(edit.OriginalSha256), edit.RenderedLayers.IsEmpty), "Edit opens on the adopted picture with no layers (State: A live failure)");
        Assert.IsEmpty(stash.RecycleBin.Asked, "nothing recycled before the previous picture is back");

        Assert.IsEmpty((await stash.Reopen()).Failures);

        TestStash.AssertSameItem(before with { RenderedRevision = adopted.RenderedRevision + 1 }, await EditedItems.Listed(stash.Store!, adopted.Id));
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(stash.Folder.InOriginals("cat.png")));
        CollectionAssert.AreEqual(new[] { EditedItems.Hash(Rewrite) }, OpenedWith.Recycled(stash), "the adopted picture alone");
        Assert.IsNull(OpenedWith.Undo(stash, adopted.Id));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task WhileTheRecycleIsOwedTheRestoredOriginalIsTheDuplicateAndTheAdoptedBytesAreNot()
    {
        using var stash = new TestStash();
        var (cat, _) = await OpenedWith.AwaitingCat(stash, Rewrite);
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        stash.RecycleBin.Refusing = path => OpenedWith.Holds(path, Rewrite);

        var undoing = await stash.Store!.UndoAdoption(cat.Id, adopted.RenderedRevision);
        var again = await stash.ImportOrFind("again", "again.png", Still);
        var adoptedBytes = await stash.ImportOrFind("rewrite", "rewrite.png", Rewrite);

        Assert.AreEqual(new Failure(ItemAdoptionUndo.Action, "cat", "the test Recycle Bin refuses it"), undoing.Failure);
        Assert.AreEqual(EditedItems.Hash(Rewrite), OpenedWith.OriginalHash(stash, cat.Id), "the column still the adopted original's");
        Assert.AreEqual((true, cat.Id), (again.AlreadyInStash, again.Item.Id), "the restored original is the item's (G12)");
        Assert.IsFalse(adoptedBytes.AlreadyInStash, "the adopted bytes are no item's original any more");
        stash.RecycleBin.Refusing = _ => false;
        Assert.IsEmpty((await stash.Reopen()).Failures);
        Assert.AreEqual(EditedItems.Hash(Still), OpenedWith.OriginalHash(stash, cat.Id), "fully restored at the next start");
        Assert.IsEmpty(EditedItems.Row(stash, cat.Id).Steps);
    }

    [TestMethod]
    public async Task WhileTheRecycleIsOwedEditOpensOnTheRestoredOriginalsRecordNotTheAdoptedColumns()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var webp = MediaFixtures.Bytes("animated.webp");
        var imported = await stash.ImportFixture("cat", "animated.webp");
        var saved = (await stash.SaveEdit(imported, EditedItems.BlackBoxAndBox)).Value!;
        var jpeg = MediaFixtures.Bytes("photo.jpg");
        await OpenedWith.AwaitingRewrite(stash, saved, jpeg);
        var adopted = await OpenedWith.Adopt(stash, saved.Id);
        stash.RecycleBin.Refusing = path => OpenedWith.Holds(path, jpeg);

        Assert.IsNotNull((await stash.Store!.UndoAdoption(saved.Id, adopted.RenderedRevision)).Failure);
        var edit = await EditedItems.ForEdit(stash.Store, saved.Id);
        var listed = await EditedItems.Listed(stash.Store, saved.Id);

        Assert.AreEqual(ImageFormat.Jpeg, EditedItems.Row(stash, saved.Id).Original.Format, "the columns still the adopted JPEG's");
        Assert.AreEqual((imported.Facts, ImageFormat.Gif, stash.Folder.InOriginals("cat.webp")), (edit.OriginalFacts, edit.FinishedFormat, edit.OriginalPath));
        Assert.AreEqual((EditedItems.Hash(webp), EditedItems.BlackBoxAndBox.ToJson(), EditedItems.BlackBoxAndBox.ToJson()), (Convert.ToHexString(edit.OriginalSha256), edit.Layers.ToJson(), edit.RenderedLayers.ToJson()));
        Assert.AreEqual((stash.Folder.InRoot("cat.gif"), saved.Facts, true), (listed.FilePath, listed.Facts, listed.Edited), "listed as restored");
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task TheListingIsAnsweredWhileAChangeFinishesAPendingUndoWhoseAdoptedPictureIsHeldAndTheChangeThenRuns()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var (cat, _) = await OpenedWith.AwaitingCat(stash, Rewrite);
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        Assert.IsTrue((await stash.Store!.RecordUses([adopted], stash.Clock.Now)).Succeeded);
        stash.RecycleBin.Refusing = path => OpenedWith.Holds(path, Rewrite);
        Assert.IsNotNull((await stash.Store.UndoAdoption(cat.Id, adopted.RenderedRevision)).Failure);
        stash.RecycleBin.Refusing = _ => false;
        var setAside = stash.RecycleBin.Asked.Last();

        var (recent, renamed) = await ReadsWhileAChangeWaits.Asked(TestStash.Hold(setAside), () => stash.Store.Rename(adopted, "fox"), stash.Store.Recent);

        var listed = recent.Value!.Single();
        Assert.AreEqual((cat.Id, stash.Folder.InRoot("cat.png")), (listed.Id, listed.FilePath), "listed on the restored picture on disk (State: A live failure)");
        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        Assert.AreEqual(stash.Folder.InRoot("fox.png"), renamed.Value!.FilePath);
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(renamed.Value.FilePath), "the Undo finished first, its previous original renamed");
        CollectionAssert.AreEqual(new[] { EditedItems.Hash(Rewrite) }, OpenedWith.Recycled(stash), "the adopted picture recycled once its hold went");
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(false, DisplayName = "unedited, adopted from another format, its step waiting on a held file")]
    [DataRow(true, DisplayName = "edited, while it renders the previous picture")]
    public async Task EditAndACopyAskedWhileTheUndoRunsAreAnsweredOnceItHasRun(bool edited)
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        var (_, adopted, _) = await OpenedWith.AdoptedCat(stash, edited ? Rewrite : MediaFixtures.Bytes("photo.jpg"), edited);
        using var rendering = new SemaphoreSlim(0);
        using var resume = new ManualResetEventSlim();
        if (edited)
        {
            stash.Renderer = (original, name, layers, format, work, retry) =>
            {
                rendering.Release();
                resume.Wait(TimeSpan.FromSeconds(10));
                return FinishedPicture.Render(original, name, layers, format, work, retry);
            };
        }

        Task<Outcome<StashItem?>> undoing;
        Task<Outcome<ItemForEdit?>> editing;
        Task<Confirmation> copying;
        bool answeredWhileRunning;
        using (edited ? null : TestStash.Hold(adopted.FilePath))
        {
            undoing = stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision);
            if (edited)
            {
                Assert.IsTrue(await rendering.WaitAsync(TimeSpan.FromSeconds(10)), "the Undo never rendered");
            }
            else
            {
                await EditorSessions.Until(() => EditedItems.Row(stash, adopted.Id).Steps.Count > 0, "the Undo never committed");
            }

            editing = stash.Store.ForEdit(adopted.Id);
            copying = stash.Store.Confirm([adopted], "Copy");
            await Task.Delay(300);
            answeredWhileRunning = editing.IsCompleted || copying.IsCompleted;
            resume.Set();
        }

        var undone = await undoing;
        var edit = (await editing).Value!;
        using var copy = await copying;

        Assert.IsTrue(undone.Succeeded, undone.Failure?.ToString());
        Assert.IsFalse(answeredWhileRunning, "neither answered while the Undo ran");
        var original = edited ? stash.Folder.InOriginals("cat.png") : stash.Folder.InRoot("cat.png");
        var layers = edited ? EditedItems.BlackBoxAndBox : LayerList.Empty;
        Assert.AreEqual((original, EditedItems.Hash(Still), layers.ToJson()), (edit.OriginalPath, Convert.ToHexString(edit.OriginalSha256), edit.Layers.ToJson()), "Edit opens once the Undo has run, on the restored original and layers (G20)");
        Assert.AreEqual(stash.Folder.InRoot("cat.png"), copy.Items.Single().Value?.FilePath, "the copy hands over the restored picture, never the adopted one the Undo set aside (G8)");
    }
}
