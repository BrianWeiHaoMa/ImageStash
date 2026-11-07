using System.Runtime.Versioning;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// The Undo of an Open with… adoption left unfinished by a refused step or a kill (State: Every change, A live failure,
/// Open with…; G27, G28, G29, G35): named, its steps kept and the item listed on the picture on disk; the next start
/// finishing it from every moment a kill can leave, or, while a step is still refused before the adopted picture has
/// gone to the Recycle Bin, putting the adopted picture back and rolling the row back to the adoption, whose record the
/// start then expires. Every other item, tag, use and file is as it was.
/// </summary>
[TestClass]
public sealed class ItemAdoptionUndoRecoveryTests
{
    private static readonly byte[] Still = MediaFixtures.Bytes("still.png");

    private static readonly byte[] Rewrite = MediaFixtures.DistinctPng(5, 12, 10);

    // The item imported first in every stash here, which nothing may touch.
    private const long DogId = 1;

    [TestMethod]
    public async Task WhileTheAdoptedPictureCannotBeSetAsideTheItemIsTheAdoptedOneAndTheStartRollsTheUndoBack()
    {
        using var stash = new TestStash();
        var (_, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited: false);
        var others = OpenedWith.Untouched(stash, DogId, "dog");
        Outcome<StashItem?> undoing;
        StashItem? whileHeld;
        int steps;
        StoreOpening opening;
        using (TestStash.Hold(adopted.FilePath))
        {
            undoing = await stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision);
            whileHeld = (await stash.Store.Item(adopted.Id)).Value;
            steps = EditedItems.Row(stash, adopted.Id).Steps.Count;
            opening = await ReopenHoldingTheCopy(stash, adopted.Id);
        }

        Assert.AreEqual(new Failure(ItemAdoptionUndo.Action, "cat", HeldFileRetry.HeldReason), undoing.Failure);
        TestStash.AssertSameItem(adopted, whileHeld);
        Assert.AreEqual(3, steps, "the steps on the row");
        Assert.AreEqual(new Failure("Finish an unfinished undo of an adoption", "cat", HeldFileRetry.HeldReason), opening.Failures.Single());
        await AssertAdoptedWithItsRecordExpired(stash, adopted, others);
        Assert.IsEmpty((await stash.Reopen()).Failures, "released, nothing is left");
        TestStash.AssertSameItem(adopted, await EditedItems.Listed(stash.Store!, adopted.Id));
    }

    [TestMethod]
    public async Task WhileTheHeldOriginalCannotBeRestoredTheItemIsUnlistedAndTheStartPutsTheAdoptedPictureBack()
    {
        using var stash = new TestStash();
        var (_, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited: false);
        var others = OpenedWith.Untouched(stash, DogId, "dog");
        var held = stash.Folder.InWork(OpenedWith.Undo(stash, adopted.Id)!.HeldCopy!);
        Outcome<StashItem?> undoing;
        StashItem? whileHeld;
        StoreOpening opening;
        using (TestStash.Hold(held))
        {
            undoing = await stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision);
            whileHeld = (await stash.Store.Item(adopted.Id)).Value;
            opening = await ReopenHoldingTheCopy(stash, adopted.Id);
        }

        Assert.AreEqual(new Failure(ItemAdoptionUndo.Action, "cat", HeldFileRetry.HeldReason), undoing.Failure);
        Assert.IsNull(whileHeld, "the adopted picture in .work\\, the previous one not back");
        CollectionAssert.AreEqual(
            new[] { new Failure("Finish an unfinished undo of an adoption", "cat", HeldFileRetry.HeldReason), new Failure(AdoptionUndo.ExpireAction, "cat", HeldFileRetry.HeldReason) },
            opening.Failures.ToList());
        TestStash.AssertSameItem(adopted, await EditedItems.Listed(stash.Store!, adopted.Id));
        CollectionAssert.AreEqual(Rewrite, File.ReadAllBytes(adopted.FilePath), "the adopted picture back in the root");
        Assert.IsEmpty(OpenedWith.Recycled(stash), "nothing in the Recycle Bin");
        Assert.IsNotNull(OpenedWith.Undo(stash, adopted.Id), "the record kept while its held original cannot be recycled");
        Assert.AreEqual(others, OpenedWith.Untouched(stash, DogId, "dog"));

        Assert.IsEmpty((await stash.Reopen()).Failures);
        await AssertAdoptedWithItsRecordExpired(stash, adopted, others);
    }

    [TestMethod]
    public async Task WhileAnEditedPreviousRenderCannotBePlacedTheItemIsUnlistedAndTheStartPutsTheAdoptedPictureBack()
    {
        using var stash = new TestStash();
        var (_, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited: true);
        var others = OpenedWith.Untouched(stash, DogId, "dog");
        List<FileStream> holds = [];
        stash.Renderer = EditedItems.Holding(holds);

        var undoing = await stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision);
        var whileHeld = (await stash.Store.Item(adopted.Id)).Value;
        stash.RenderCalls.Clear();
        var opening = await ReopenHoldingTheCopy(stash, adopted.Id);
        holds.ForEach(hold => hold.Dispose());
        stash.Renderer = FinishedPicture.Render;

        Assert.AreEqual(new Failure(ItemAdoptionUndo.Action, "cat", HeldFileRetry.HeldReason), undoing.Failure);
        Assert.IsNull(whileHeld, "the adopted picture in .work\\, the render not placed");
        Assert.AreEqual(new Failure("Finish an unfinished undo of an adoption", "cat", HeldFileRetry.HeldReason), opening.Failures[0]);
        Assert.HasCount(1, stash.RenderCalls, "rendered again at the start, held again");
        Assert.IsTrue(opening.Failures.Skip(1).All(failure => failure.Action == "Clear the work folder"), string.Join("; ", opening.Failures));
        await AssertAdoptedWithItsRecordExpired(stash, adopted, others, previous: Still);
        Assert.IsEmpty((await stash.Reopen()).Failures);
        Assert.IsEmpty(stash.WorkFiles(), "the held renders cleared once let go");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WhileTheRecycleIsRefusedTheItemIsRestoredAndTheNextStartRecyclesTheAdoptedPicture(bool edited)
    {
        using var stash = new TestStash();
        var (before, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited);
        var others = OpenedWith.Untouched(stash, DogId, "dog");
        stash.RecycleBin.Refusing = path => OpenedWith.Holds(path, Rewrite);

        var undoing = await stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision);
        var listed = await EditedItems.Listed(stash.Store, adopted.Id);
        var steps = EditedItems.Row(stash, adopted.Id).Steps;
        stash.RecycleBin.Refusing = _ => false;
        var opening = await ReopenHoldingTheCopy(stash, adopted.Id);

        Assert.AreEqual(new Failure(ItemAdoptionUndo.Action, "cat", "the test Recycle Bin refuses it"), undoing.Failure);
        TestStash.AssertSameItem(before with { RenderedRevision = adopted.RenderedRevision + 1 }, listed);
        Assert.AreEqual(PendingStep.RecycleSetAside, steps[^1].Step, "the recycle owed");
        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        await AssertRestored(stash, before, adopted, others);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ARecycleStillRefusedAtStartRollsTheUndoBackAndTheStartExpiresTheRecord(bool edited)
    {
        using var stash = new TestStash();
        var (_, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited);
        var others = OpenedWith.Untouched(stash, DogId, "dog");
        stash.RecycleBin.Refusing = path => OpenedWith.Holds(path, Rewrite);

        Assert.IsNotNull((await stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision)).Failure);
        var opening = await ReopenHoldingTheCopy(stash, adopted.Id);

        Assert.AreEqual(new Failure("Finish an unfinished undo of an adoption", "cat", "the test Recycle Bin refuses it"), opening.Failures.Single());
        await AssertAdoptedWithItsRecordExpired(stash, adopted, others);
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ARollbackAtStartAfterARenamePutsThePreviousOriginalBackUnderItsRecordsName()
    {
        using var stash = new TestStash();
        var (_, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited: true);
        var renamed = (await stash.Store!.Rename(adopted, "fox")).Value!;
        stash.RecycleBin.Refusing = path => OpenedWith.Holds(path, Rewrite);
        Assert.IsNotNull((await stash.Store.UndoAdoption(adopted.Id, adopted.RenderedRevision)).Failure);
        CollectionAssert.AreEqual(new[] { "fox.png" }, EditedItems.OriginalFiles(stash), "renamed under the item's stem");

        var opening = await ReopenHoldingTheCopy(stash, adopted.Id);

        Assert.AreEqual("Finish an unfinished undo of an adoption", opening.Failures.Single().Action);
        TestStash.AssertSameItem(renamed, await EditedItems.Listed(stash.Store!, adopted.Id));
        CollectionAssert.AreEqual(new[] { EditedItems.Hash(Still) }, OpenedWith.Recycled(stash), "renamed back, so the expiry found it");
        Assert.IsEmpty(EditedItems.OriginalFiles(stash));
    }

    [TestMethod]
    public async Task AnUndoTheStartCanNeitherFinishNorPutBackIsLeftListedOnItsPictureForTheNextStart()
    {
        using var stash = new TestStash();
        var (before, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited: false);
        var others = OpenedWith.Untouched(stash, DogId, "dog");
        stash.RecycleBin.Refusing = path => OpenedWith.Holds(path, Rewrite);
        Assert.IsNotNull((await stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision)).Failure);
        var row = OpenedWith.WholeRow(stash, adopted.Id);

        StoreOpening opening;
        using (TestStash.Hold(stash.Folder.InRoot("cat.png")))
        {
            opening = await ReopenHoldingTheCopy(stash, adopted.Id);
        }

        CollectionAssert.AreEqual(new[] { "Finish an unfinished undo of an adoption", "Undo an unfinished undo of an adoption" }, opening.Failures.Select(failure => failure.Action).ToList());
        Assert.AreEqual(row, OpenedWith.WholeRow(stash, adopted.Id), "its steps and record kept");
        TestStash.AssertSameItem(before with { RenderedRevision = adopted.RenderedRevision + 1 }, await EditedItems.Listed(stash.Store!, adopted.Id));
        Assert.IsTrue(stash.WorkFiles().Any(name => OpenedWith.Holds(stash.Folder.InWork(name), Rewrite)), "the set-aside picture kept from the clearing");
        stash.RecycleBin.Refusing = _ => false;
        Assert.IsEmpty((await stash.Reopen()).Failures);
        await AssertRestored(stash, before, adopted, others);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AnUndoWhoseSetAsidePictureCannotGoBackStaysListedOnTheRestoredPictureForTheNextStart(bool edited)
    {
        using var stash = new TestStash();
        var (before, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited);
        var others = OpenedWith.Untouched(stash, DogId, "dog");
        stash.RecycleBin.Refusing = path => OpenedWith.Holds(path, Rewrite);
        Assert.IsNotNull((await stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision)).Failure);
        stash.RecycleBin.Refusing = _ => false;
        var row = OpenedWith.WholeRow(stash, adopted.Id);
        var aside = stash.Folder.InWork(stash.WorkFiles().Single(name => OpenedWith.Holds(stash.Folder.InWork(name), Rewrite)));

        // The set-aside picture held at the start: its recycle refused, and it cannot go back into the root either.
        StoreOpening opening;
        using (TestStash.Hold(aside))
        {
            opening = await ReopenHoldingTheCopy(stash, adopted.Id);
        }

        CollectionAssert.AreEqual(new[] { "Finish an unfinished undo of an adoption", "Undo an unfinished undo of an adoption" }, opening.Failures.Select(failure => failure.Action).ToList());
        Assert.AreEqual(row, OpenedWith.WholeRow(stash, adopted.Id), "its steps and record kept");
        TestStash.AssertSameItem(before with { RenderedRevision = adopted.RenderedRevision + 1 }, await EditedItems.Listed(stash.Store!, adopted.Id));
        CollectionAssert.AreEqual(Rewrite, File.ReadAllBytes(aside), "the set-aside picture kept for its recycle");
        Assert.IsEmpty((await stash.Reopen()).Failures);
        await AssertRestored(stash, before, adopted, others);
    }

    [TestMethod]
    [DoNotParallelize]
    [SupportedOSPlatform("windows")]
    public async Task APutBackKeepsEveryFileItCheckedFromOtherProgramsUntilTheRowIsRolledBack()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(1.5) };
        var (_, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited: false);
        var others = OpenedWith.Untouched(stash, DogId, "dog");
        stash.RecycleBin.Refusing = path => OpenedWith.Holds(path, Rewrite);
        Assert.IsNotNull((await stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision)).Failure);
        stash.RecycleBin.Refusing = _ => false;
        var aside = stash.Folder.InWork(stash.WorkFiles().Single(name => OpenedWith.Holds(stash.Folder.InWork(name), Rewrite)));

        // The set-aside picture held through its recycle's retry and into the put-back's check of it, which follows the
        // check of the restored picture; meanwhile another program asks to read the restored picture.
        var held = HeldFiles.HeldFor(aside, TimeSpan.FromSeconds(2.6));
        var opening = ReopenHoldingTheCopy(stash, adopted.Id);
        await Task.Delay(TimeSpan.FromSeconds(2));
        var readable = Readable(stash.Folder.InRoot("cat.png"));
        await held;
        var opened = await opening;

        Assert.IsFalse(readable, "held from its check to its move, so no other program can come to hold it in between");
        Assert.AreEqual(new Failure("Finish an unfinished undo of an adoption", "cat", HeldFileRetry.HeldReason), opened.Failures.Single());
        await AssertAdoptedWithItsRecordExpired(stash, adopted, others);
    }

    [TestMethod]
    public async Task APutBackRefusedAfterTheRestoredPictureMovedPutsItBackInTheRoot()
    {
        using var stash = new TestStash();
        var jpeg = MediaFixtures.Bytes("photo.jpg");
        var (before, adopted, _) = await OpenedWith.AdoptedCat(stash, jpeg, edited: false);
        stash.RecycleBin.Refusing = path => OpenedWith.Holds(path, jpeg);
        Assert.IsNotNull((await stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision)).Failure);
        var row = OpenedWith.WholeRow(stash, adopted.Id);
        // A file where the adopted cat.jpg would go back, so its move is refused once the restored cat.png has moved.
        var inTheWay = stash.Folder.InRoot("cat.jpg");
        File.WriteAllBytes(inTheWay, [1]);

        var opening = await ReopenHoldingTheCopy(stash, adopted.Id);

        CollectionAssert.AreEqual(new[] { "Finish an unfinished undo of an adoption", "Undo an unfinished undo of an adoption" }, opening.Failures.Select(failure => failure.Action).ToList());
        Assert.AreEqual(row, OpenedWith.WholeRow(stash, adopted.Id), "its steps and record kept");
        TestStash.AssertSameItem(before with { RenderedRevision = adopted.RenderedRevision + 1 }, await EditedItems.Listed(stash.Store!, adopted.Id));
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(stash.Folder.InRoot("cat.png")), "the restored picture back in the root");
        File.Delete(inTheWay);
        stash.RecycleBin.Refusing = _ => false;
        Assert.IsEmpty((await stash.Reopen()).Failures);
        TestStash.AssertSameItem(before with { RenderedRevision = adopted.RenderedRevision + 1 }, await EditedItems.Listed(stash.Store!, adopted.Id));
        CollectionAssert.AreEqual(new[] { EditedItems.Hash(jpeg) }, OpenedWith.Recycled(stash), "the adopted picture, and only it");
    }

    [TestMethod]
    [DataRow("committed", false)]
    [DataRow("committed", true)]
    [DataRow("set aside", false)]
    [DataRow("set aside", true)]
    [DataRow("restored", false)]
    [DataRow("restored", true)]
    [DataRow("recycled", false)]
    [DataRow("recycled", true)]
    public async Task AtStartAnUndoKilledAtAnyMomentIsFinished(string moment, bool edited)
    {
        using var stash = new TestStash();
        var (before, adopted, _) = await OpenedWith.AdoptedCat(stash, Rewrite, edited);
        var others = OpenedWith.Untouched(stash, DogId, "dog");
        // The Undo committed with nothing run, as a kill right after its commit leaves it: made by holding the adopted file.
        using (TestStash.Hold(adopted.FilePath))
        {
            Assert.IsNotNull((await stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision)).Failure);
        }

        stash.Close();
        var steps = EditedItems.Row(stash, adopted.Id).Steps;
        var aside = steps.Single(step => step.Step == PendingStep.SetAsideAdopted);
        var landing = steps.Single(step => step.Step is PendingStep.RestoreHeld or PendingStep.PlaceRender);
        var committedRender = edited ? File.ReadAllBytes(landing.FromPath(stash.Folder)) : null;
        if (moment is "set aside" or "restored" or "recycled")
        {
            File.Move(aside.FromPath(stash.Folder), aside.ToPath(stash.Folder));
        }

        if (moment is "restored" or "recycled")
        {
            File.Move(landing.FromPath(stash.Folder), landing.ToPath(stash.Folder));
        }

        if (moment == "recycled")
        {
            Assert.IsTrue(stash.RecycleBin.Recycle([aside.ToPath(stash.Folder)], TestStash.ShortRetry)[0].Succeeded);
        }

        stash.RenderCalls.Clear();
        await stash.OpenCleanly();

        await AssertRestored(stash, before, adopted, others);
        Assert.HasCount(edited && moment is "committed" or "set aside" ? 1 : 0, stash.RenderCalls, "an unplaced render made again at start");
        if (committedRender is not null)
        {
            CollectionAssert.AreEqual(committedRender, File.ReadAllBytes(stash.Folder.InRoot("cat.png")), "the same list over the same original");
        }
    }

    /// <summary>
    /// The item fully restored: listed as it was before the adoption at the Undo's revision, its picture back under
    /// "cat.png", its row's facts, hash, layers and names as before, the adopted picture in the Recycle Bin, the record
    /// cleared, no step left, <c>.work\</c> empty, and dog as it was.
    /// </summary>
    private static async Task AssertRestored(TestStash stash, StashItem before, StashItem adopted, string others)
    {
        var listed = await EditedItems.Listed(stash.Store!, adopted.Id);
        TestStash.AssertSameItem(before with { RenderedRevision = adopted.RenderedRevision + 1 }, listed);
        var row = EditedItems.Row(stash, adopted.Id);
        Assert.AreEqual(((string?)null, row.LayerRevision, 0, before.Edited), (row.PreviousLayers, row.RenderedRevision, row.Steps.Count, row.OriginalInOriginals));
        Assert.AreEqual((TestStash.StillPng, "cat.png", "cat.png"), (row.Original, row.FinishedFile, row.OriginalFile));
        Assert.AreEqual(EditedItems.Hash(Still), OpenedWith.OriginalHash(stash, adopted.Id), "the previous original's hash with its facts");
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(before.Edited ? stash.Folder.InOriginals("cat.png") : stash.Folder.InRoot("cat.png")));
        CollectionAssert.AreEqual(new[] { EditedItems.Hash(Rewrite) }, OpenedWith.Recycled(stash), "the adopted picture, and only it");
        Assert.IsNull(OpenedWith.Undo(stash, adopted.Id));
        Assert.IsEmpty(stash.WorkFiles());
        Assert.AreEqual(others, OpenedWith.Untouched(stash, DogId, "dog"));
    }

    /// <summary>
    /// The item rolled back to the adoption: listed as adopted, its adopted picture in the root, no step left and its
    /// layers at the adoption's revision, its record expired by the same start, the previous original (still.png's
    /// bytes) in the Recycle Bin, and dog as it was.
    /// </summary>
    private static async Task AssertAdoptedWithItsRecordExpired(TestStash stash, StashItem adopted, string others, byte[]? previous = null)
    {
        TestStash.AssertSameItem(adopted, await EditedItems.Listed(stash.Store!, adopted.Id));
        CollectionAssert.AreEqual(Rewrite, File.ReadAllBytes(adopted.FilePath));
        var row = EditedItems.Row(stash, adopted.Id);
        Assert.AreEqual(("[]", adopted.RenderedRevision, adopted.RenderedRevision, 0), (row.Layers, row.LayerRevision, row.RenderedRevision, row.Steps.Count));
        Assert.IsNull(OpenedWith.Undo(stash, adopted.Id));
        CollectionAssert.AreEqual(new[] { EditedItems.Hash(previous ?? Still) }, OpenedWith.Recycled(stash), "the previous original, and not the adopted picture");
        Assert.AreEqual(others, OpenedWith.Untouched(stash, DogId, "dog"));
    }

    /// <summary>Whether another program could open the file at <paramref name="path"/> for reading now, as a scanner's open asks.</summary>
    private static bool Readable(string path)
    {
        try
        {
            using var other = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static async Task<StoreOpening> ReopenHoldingTheCopy(TestStash stash, long id)
    {
        // The session's copy is held too, so the start's own classification leaves it for later.
        var copy = OpenedWith.Record(stash, id)!.CopyPath(stash.OpenWithRoot!);
        using var hold = TestStash.Hold(copy);
        return await stash.Reopen();
    }
}
