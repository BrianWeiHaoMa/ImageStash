using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// An Open with… adoption left unfinished by a kill or a refused step (State: Every change, A live failure, Open with…;
/// G27, G28, G29, G35): the next start finishes it and then expires its record, from every moment a kill can leave;
/// while a step is still refused there, it puts the previous picture back (the held original renamed in, or the render
/// made again) and rolls the row back with the rewrite awaiting adoption again; and a record the start cannot expire
/// or read is named, its held original kept. Every other item, tag, use and file is as it was.
/// </summary>
[TestClass]
public sealed class ItemAdoptionRecoveryTests
{
    private static readonly byte[] Still = MediaFixtures.Bytes("still.png");

    private static readonly byte[] Rewrite = MediaFixtures.DistinctPng(5, 12, 10);

    // The item imported first in every stash here, which nothing may touch.
    private const long DogId = 1;

    [TestMethod]
    [DataRow("committed")]
    [DataRow("held")]
    [DataRow("placed")]
    public async Task AtStartAnUneditedItemsAdoptionKilledAtAnyMomentIsFinishedAndItsRecordExpired(string moment)
    {
        using var stash = new TestStash();
        var (cat, others) = await CommittedWithNothingRun(stash, edited: false);
        var steps = EditedItems.Row(stash, cat.Id).Steps;
        var held = stash.Folder.InWork(steps[0].To);
        if (moment is "held" or "placed")
        {
            File.Move(cat.FilePath, held);
        }

        if (moment == "placed")
        {
            File.Move(stash.Folder.InWork(steps[1].From), cat.FilePath);
        }

        await stash.OpenCleanly();

        await AssertAdoptedAndExpired(stash, cat, others, previous: held);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AtStartAnEditedItemsAdoptionKilledBeforeOrAfterItsRendersRemovalIsFinished(bool removed)
    {
        using var stash = new TestStash();
        var (cat, others) = await CommittedWithNothingRun(stash, edited: true);
        if (removed)
        {
            File.Delete(cat.FilePath);
        }

        await stash.OpenCleanly();

        await AssertAdoptedAndExpired(stash, cat, others, previous: stash.Folder.InOriginals("cat.png"));
        Assert.IsEmpty(EditedItems.OriginalFiles(stash));
    }

    [TestMethod]
    public async Task AtStartAnAdoptionDoneButForItsRecordHasTheRecordExpired()
    {
        using var stash = new TestStash();
        var (cat, others) = await Stash(stash, edited: false);
        await OpenedWith.Adopt(stash, cat.Id);
        var held = stash.Folder.InWork(OpenedWith.Undo(stash, cat.Id)!.HeldCopy!);
        stash.Close();

        await stash.OpenCleanly();

        await AssertAdoptedAndExpired(stash, cat, others, previous: held);
    }

    [TestMethod]
    public async Task AtStartARecordWhosePreviousOriginalIsAlreadyGoneIsClearedWithoutTheRecycleBin()
    {
        using var stash = new TestStash();
        var (cat, _) = await Stash(stash, edited: false);
        await OpenedWith.Adopt(stash, cat.Id);
        var held = stash.Folder.InWork(OpenedWith.Undo(stash, cat.Id)!.HeldCopy!);
        stash.Close();
        // As a kill after the expiry's recycle and before its clearing leaves it.
        File.Move(held, stash.Outside("recycled.png"));

        await stash.OpenCleanly();

        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
        Assert.IsEmpty(stash.RecycleBin.Asked);
    }

    [TestMethod]
    public async Task AtStartAnUneditedItemsAdoptedFileStillHeldIsUndoneWithTheRewriteAwaitingAgain()
    {
        using var stash = new TestStash();
        var (cat, others) = await Stash(stash, edited: false);
        var row = OpenedWith.WholeRow(stash, cat.Id);
        var files = EditedItems.FilesWithHashes(stash);
        List<FileStream> holds = [];
        stash.Reader = OpenedWith.Holding(holds);

        var adopting = await stash.Store!.AdoptRewrite(cat.Id);
        var whileHeld = await stash.Store.Item(cat.Id);
        var adopted = stash.WorkFiles().Single(name => name != OpenedWith.Undo(stash, cat.Id)!.HeldCopy);
        var opening = await ReopenHoldingTheCopy(stash, cat);
        holds.ForEach(hold => hold.Dispose());

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", HeldFileRetry.HeldReason), adopting.Failure);
        Assert.IsNull(whileHeld.Value, "not listed while its picture is in .work\\");
        Assert.AreEqual("Finish an unfinished adoption", opening.Failures[0].Action);
        Assert.AreEqual("cat", opening.Failures[0].Subject);
        Assert.IsTrue(opening.Failures.Any(failure => failure.Action == "Clear the work folder" && failure.Subject == adopted), "the held adopted file is named by the clearing");
        TestStash.AssertSameItem(cat, await EditedItems.Listed(stash.Store!, cat.Id));
        Assert.AreEqual(row, OpenedWith.WholeRow(stash, cat.Id), "exactly as before: the record cleared, the rewrite awaiting adoption again");
        CollectionAssert.AreEquivalent(files.Append($".work\\{adopted} {EditedItems.Hash(Rewrite)}").ToList(), EditedItems.FilesWithHashes(stash));
        Assert.AreEqual(others, Others(stash));
        stash.Reader = MediaDecoder.ReadPicture;
        await OpenedWith.Adopt(stash, cat.Id);
        CollectionAssert.AreEqual(Rewrite, File.ReadAllBytes(cat.FilePath), "adopted once let go");
    }

    [TestMethod]
    public async Task AtStartAnEditedItemsRenderStillHeldRollsTheAdoptionBackWithNothingMoved()
    {
        using var stash = new TestStash();
        var (cat, others) = await Stash(stash, edited: true);
        var row = OpenedWith.WholeRow(stash, cat.Id);
        var files = EditedItems.FilesWithHashes(stash);
        StoreOpening opening;
        using (TestStash.Hold(cat.FilePath))
        {
            Assert.AreEqual(HeldFileRetry.HeldReason, (await stash.Store!.AdoptRewrite(cat.Id)).Failure?.Reason);
            opening = await ReopenHoldingTheCopy(stash, cat);
        }

        Assert.AreEqual(("Finish an unfinished adoption", "cat"), (opening.Failures[0].Action, opening.Failures[0].Subject));
        Assert.HasCount(1, opening.Failures, string.Join("; ", opening.Failures));
        Assert.AreEqual(row, OpenedWith.WholeRow(stash, cat.Id));
        CollectionAssert.AreEquivalent(files, EditedItems.FilesWithHashes(stash), "nothing moved, the adopted file discarded");
        Assert.AreEqual(others, Others(stash));
        await OpenedWith.Adopt(stash, cat.Id);
    }

    [TestMethod]
    public async Task AtStartAnEditedItemsAdoptedFileStillHeldAfterItsRenderWentIsUndoneByRenderingAgain()
    {
        using var stash = new TestStash();
        var (cat, others) = await Stash(stash, edited: true);
        var row = OpenedWith.WholeRow(stash, cat.Id);
        var files = EditedItems.FilesWithHashes(stash);
        var render = EditedItems.Hash(cat.FilePath);
        List<FileStream> holds = [];
        stash.Reader = OpenedWith.Holding(holds);

        Assert.AreEqual(HeldFileRetry.HeldReason, (await stash.Store!.AdoptRewrite(cat.Id)).Failure?.Reason);
        var whileHeld = await stash.Store.Item(cat.Id);
        var adopted = stash.WorkFiles().Single();
        stash.RenderCalls.Clear();
        var opening = await ReopenHoldingTheCopy(stash, cat);
        holds.ForEach(hold => hold.Dispose());

        Assert.IsNull(whileHeld.Value, "its render deleted, its adopted file not placed");
        Assert.AreEqual(("Finish an unfinished adoption", "cat"), (opening.Failures[0].Action, opening.Failures[0].Subject));
        Assert.HasCount(1, stash.RenderCalls, "the render made again from the previous layers");
        Assert.AreEqual((stash.Folder.InOriginals("cat.png"), EditedItems.BlackBoxAndBox), (stash.RenderCalls[0].OriginalPath, stash.RenderCalls[0].Layers));
        Assert.AreEqual(render, EditedItems.Hash(cat.FilePath), "the same list over the same original");
        Assert.AreEqual(row, OpenedWith.WholeRow(stash, cat.Id));
        CollectionAssert.AreEquivalent(files.Append($".work\\{adopted} {EditedItems.Hash(Rewrite)}").ToList(), EditedItems.FilesWithHashes(stash));
        Assert.AreEqual(others, Others(stash));
        stash.Reader = MediaDecoder.ReadPicture;
        await OpenedWith.Adopt(stash, cat.Id);
    }

    [TestMethod]
    public async Task AtStartAnAdoptionThatCannotBePutBackIsLeftUnlistedNamedForTheNextStart()
    {
        using var stash = new TestStash();
        var (cat, others) = await Stash(stash, edited: true);
        List<FileStream> holds = [];
        stash.Reader = OpenedWith.Holding(holds);
        Assert.AreEqual(HeldFileRetry.HeldReason, (await stash.Store!.AdoptRewrite(cat.Id)).Failure?.Reason);
        var row = OpenedWith.WholeRow(stash, cat.Id);
        stash.Renderer = EditedItems.Refusing;

        var opening = await ReopenHoldingTheCopy(stash, cat);
        var stillUnlisted = await stash.Store!.Item(cat.Id);
        holds.ForEach(hold => hold.Dispose());

        CollectionAssert.AreEqual(new[] { "Finish an unfinished adoption", "Undo an unfinished adoption" }, opening.Failures.Take(2).Select(failure => failure.Action).ToList());
        Assert.AreEqual(EditedItems.RefusedReason, opening.Failures[1].Reason);
        Assert.IsNull(stillUnlisted.Value);
        Assert.AreEqual(row, OpenedWith.WholeRow(stash, cat.Id), "its steps and record kept");
        Assert.AreEqual(others, Others(stash));
        (stash.Renderer, stash.Reader) = (FinishedPicture.Render, MediaDecoder.ReadPicture);
        await stash.Reopen();
        CollectionAssert.AreEqual(Rewrite, File.ReadAllBytes(cat.FilePath), "finished by the next start");
    }

    [TestMethod]
    public async Task AtStartARecordThatCannotBeReadIsNamedAndNothingInTheWorkFolderIsCleared()
    {
        using var stash = new TestStash();
        var (cat, others) = await Stash(stash, edited: false);
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        var held = OpenedWith.Undo(stash, cat.Id)!.HeldCopy!;
        stash.Close();
        stash.Sql("UPDATE items SET adoption_undo = '{\"revision\":' WHERE id = $id", ("$id", cat.Id));
        File.WriteAllBytes(stash.Folder.InWork("left by a kill.png"), [1]);

        var opening = await stash.Open();

        Assert.IsNotNull(opening.Store);
        CollectionAssert.AreEquivalent(
            new[] { new Failure(AdoptionUndo.ExpireAction, "cat", AdoptionUndo.Unreadable), new Failure("Clear the work folder", "cat", AdoptionUndo.Unreadable) },
            opening.Failures.ToList());
        CollectionAssert.AreEquivalent(new[] { held, "left by a kill.png" }, stash.WorkFiles());
        TestStash.AssertSameItem(adopted, await EditedItems.Listed(opening.Store, cat.Id));
        Assert.IsEmpty(stash.RecycleBin.Asked);
        Assert.AreEqual(others, Others(stash));
    }

    /// <summary>
    /// An open stash holding "cat" from still.png, edited or not, whose Open with… copy awaits adoption, and "dog" with a
    /// tag and a use, which nothing here may touch; with what of dog the checks compare.
    /// </summary>
    private static async Task<(StashItem Cat, string Others)> Stash(TestStash stash, bool edited)
    {
        await stash.OpenCleanly();
        var dog = await stash.ImportFixture("dog", "transparent.png");
        stash.Tag(dog.Id, "mood");
        Assert.IsEmpty((await stash.Store!.RecordUses([dog], DateTimeOffset.FromUnixTimeMilliseconds(1_000))).Value!);
        var cat = await stash.ImportFixture("cat", "still.png");
        if (edited)
        {
            var saved = await stash.SaveEdit(cat, EditedItems.BlackBoxAndBox);
            Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
            cat = saved.Value!;
        }

        await OpenedWith.AwaitingRewrite(stash, cat, Rewrite);
        return (await EditedItems.Listed(stash.Store, cat.Id), Others(stash));
    }

    /// <summary>
    /// <see cref="Stash"/>, then the adoption committed with nothing run, as a kill right after its commit leaves it:
    /// here, made by holding the file its first step moves or deletes, then the store closed.
    /// </summary>
    private static async Task<(StashItem Cat, string Others)> CommittedWithNothingRun(TestStash stash, bool edited)
    {
        var (cat, others) = await Stash(stash, edited);
        using (TestStash.Hold(cat.FilePath))
        {
            Assert.AreEqual(HeldFileRetry.HeldReason, (await stash.Store!.AdoptRewrite(cat.Id)).Failure?.Reason);
        }

        stash.Close();
        Assert.HasCount(2, EditedItems.Row(stash, cat.Id).Steps);
        return (cat, others);
    }

    /// <summary>The item fully adopted as "cat.png" holding the rewrite, its record expired at that start, its previous original in the Recycle Bin.</summary>
    private static async Task AssertAdoptedAndExpired(TestStash stash, StashItem cat, string others, string previous)
    {
        var listed = await EditedItems.Listed(stash.Store!, cat.Id);
        Assert.AreEqual((stash.Folder.InRoot("cat.png"), new MediaFacts(ImageFormat.Png, 12, 10, false, 1, 0), false), (listed.FilePath, listed.Facts, listed.Edited));
        CollectionAssert.AreEqual(Rewrite, File.ReadAllBytes(listed.FilePath));
        var row = EditedItems.Row(stash, cat.Id);
        Assert.AreEqual(("[]", (string?)null, row.LayerRevision, 0), (row.Layers, row.PreviousLayers, row.RenderedRevision, row.Steps.Count));
        Assert.AreEqual(EditedItems.Hash(Rewrite), OpenedWith.OriginalHash(stash, cat.Id), "the adopted bytes under the adopted facts and hash");
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id), "expired at the same start, after the finish");
        CollectionAssert.AreEqual(new[] { previous }, stash.RecycleBin.Asked);
        Assert.IsEmpty(stash.WorkFiles());
        Assert.AreEqual(others, Others(stash));
    }

    private static async Task<StoreOpening> ReopenHoldingTheCopy(TestStash stash, StashItem cat)
    {
        // The session's copy is held too, so the start's own classification leaves it for later.
        var copy = OpenedWith.Record(stash, cat.Id)!.CopyPath(stash.OpenWithRoot!);
        using var hold = TestStash.Hold(copy);
        return await stash.Reopen();
    }

    /// <summary>The other item's row, tags, uses and files, read between the store's calls or with it closed.</summary>
    private static string Others(TestStash stash) =>
        string.Join(
            "\n",
            [
                OpenedWith.WholeRow(stash, DogId),
                .. stash.TagRows().Where(tag => tag.Item == DogId).Select(tag => tag.Tag),
                .. stash.SqlQueryWhileOpen("SELECT used_at FROM uses WHERE item_id = $id", r => r.GetInt64(0).ToString(), ("$id", DogId)),
                .. EditedItems.FilesWithHashes(stash).Where(line => line.StartsWith("dog")),
            ]);
}
