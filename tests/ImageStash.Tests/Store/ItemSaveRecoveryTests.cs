using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditedItems;

namespace ImageStash.Tests.Store;

/// <summary>
/// A Save or a Revert a kill or a refused step left pending, at the next start (State: Save, Every change, A live
/// failure, Files; G28, G29, G35): a Save rendered again once through the renderer, the fresh render recorded on its step
/// before it lands, and finished; while it cannot finish, undone where its files can go back, the original first, else
/// left listed on the picture on disk; a Revert finished or undone the same way; every failure named and every other
/// item open. Each kill state is built as a kill leaves it, then reopened once.
/// </summary>
[TestClass]
public sealed class ItemSaveRecoveryTests
{
    private static readonly MediaFacts StillPng = new(ImageFormat.Png, 8, 6, false, 1, 0);

    [TestMethod]
    [DataRow("a: committed, nothing moved")]
    [DataRow("b: the original moved, the render in .work")]
    [DataRow("c: both steps done, the last transaction not run")]
    [DataRow("d: a second Save committed")]
    [DataRow("e: a second Save placed, the last transaction not run")]
    public async Task EveryKillStateOfASaveIsFinishedAtStartByRenderingItAgainOnce(string state)
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var other = await stash.ImportFixture("dog", "still.gif");
        var cat = state[0] switch
        {
            'a' => await FirstSaveCommitted(stash),
            'b' => await FirstSaveMoved(stash),
            'c' => await FirstSavePlaced(stash),
            'd' => await SecondSaveCommitted(stash),
            _ => await SecondSavePlaced(stash),
        };
        stash.Close();
        var committed = LayerList.FromJson(Row(stash, cat.Id).Layers, "cat").Value!;
        var revision = Row(stash, cat.Id).LayerRevision;
        var placedBefore = File.Exists(stash.Folder.InRoot("cat.png")) ? File.GetLastWriteTimeUtc(stash.Folder.InRoot("cat.png")) : DateTime.MinValue;
        await Task.Delay(20);

        var opening = await stash.Open();

        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        Assert.HasCount(1, stash.RenderCalls);
        var original = state[0] == 'a' ? stash.Folder.InRoot("cat.png") : stash.Folder.InOriginals("cat.png");
        Assert.AreEqual((original, committed, ImageFormat.Png), stash.RenderCalls[0], "the committed list over the original where it is, in F3's format");
        var row = Row(stash, cat.Id);
        Assert.AreEqual(new SavedRow(committed.ToJson(), null, revision, revision, true, "cat.png", "cat.png", row.Steps, StillPng, StillPng), row, "a completed Save");
        Assert.IsEmpty(row.Steps);
        Assert.IsEmpty(stash.WorkFiles());
        CollectionAssert.AreEqual(new[] { "cat.png", "dog.gif" }, stash.RootFiles().Order().ToList());
        Assert.IsGreaterThan(placedBefore, File.GetLastWriteTimeUtc(stash.Folder.InRoot("cat.png")), "the fresh render is the finished picture");
        Assert.AreEqual(Hash(MediaFixtures.Bytes("still.png")), Hash(stash.Folder.InOriginals("cat.png")), "the original's bytes unchanged");
        Assert.AreEqual((StillPng, revision), ((await Listed(stash.Store!, cat.Id)).Facts, (await Listed(stash.Store!, cat.Id)).RenderedRevision));
        TestStash.AssertSameItem(other, await Listed(stash.Store!, other.Id));
    }

    [TestMethod]
    public async Task ASameNamedFirstSaveHeldAtStartAfterItsPlaceStaysListedAsSavedWithItsOriginalInOriginals()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await FirstSavePlaced(stash);
        var render = stash.Folder.InRoot("cat.png");

        StoreOpening opening;
        using (TestStash.Hold(render))
        {
            opening = await stash.Open();
            var store = opening.Store!;

            Assert.HasCount(2, opening.Failures, string.Join("; ", opening.Failures));
            Assert.AreEqual(new Failure("Finish an unfinished save", "cat", HeldFileRetry.HeldReason), opening.Failures[0]);
            Assert.AreEqual(new Failure("Undo an unfinished save", "cat", HeldFileRetry.HeldReason), opening.Failures[1], "the original cannot go back over the held render");
            var listed = await Listed(store, cat.Id);
            Assert.AreEqual((render, StillPng, 1L), (listed.FilePath, listed.Facts, listed.RenderedRevision), "listed as saved on that render");
            var edit = await ForEdit(store, cat.Id);
            Assert.AreEqual((stash.Folder.InOriginals("cat.png"), BlackBoxAndBox), (edit.OriginalPath, edit.RenderedLayers), "the move reads done though the root has cat.png again");
            CollectionAssert.AreEqual(new[] { render, stash.Folder.InOriginals("cat.png") }, StoredPaths(stash, cat.Id).ToList());
            var again = await store.CommitImport(TestStash.Candidate(await stash.Stage("again.png", MediaFixtures.Bytes("still.png")), "again", MediaFixtures.Bytes("still.png")));
            Assert.IsTrue(again.Value!.AlreadyInStash, "G12 finds the original where it is");
        }

        stash.Close();
        var reopened = await stash.Open();
        Assert.IsEmpty(reopened.Failures, string.Join("; ", reopened.Failures));
        Assert.IsEmpty(Row(stash, cat.Id).Steps);
    }

    [TestMethod]
    public async Task ASecondSaveWhoseLastTransactionCannotRunIsListedAsSavedAndTheNextStartFinishesIt()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await SecondSavePlaced(stash);
        stash.Renderer = Refusing;

        var opening = await stash.Open();

        Assert.AreEqual(new Failure("Finish an unfinished save", "cat", RefusedReason), opening.Failures.Single(), "nothing to put back: it stays, named (State: Every change)");
        var listed = await Listed(stash.Store!, cat.Id);
        Assert.AreEqual((stash.Folder.InRoot("cat.png"), "cat.png", StillPng, 2L), (listed.FilePath, listed.FileName, listed.Facts, listed.RenderedRevision), "the placed render, with its step's facts, not the 4 × 3 columns");
        Assert.AreEqual(listed.Facts, (await stash.All()).Single().Facts);
        var edit = await ForEdit(stash.Store!, cat.Id);
        Assert.AreEqual((Arrow, Arrow, stash.Folder.InOriginals("cat.png")), (edit.Layers, edit.RenderedLayers, edit.OriginalPath));
        Assert.HasCount(1, Row(stash, cat.Id).Steps);

        stash.Renderer = FinishedPicture.Render;
        var reopened = await stash.Reopen();

        Assert.IsEmpty(reopened.Failures, string.Join("; ", reopened.Failures));
        Assert.HasCount(2, stash.RenderCalls);
        var row = Row(stash, cat.Id);
        Assert.AreEqual(new SavedRow(Arrow.ToJson(), null, 2, 2, true, "cat.png", "cat.png", row.Steps, StillPng, StillPng), row);
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    [DataRow('a')]
    [DataRow('d')]
    public async Task TheStartRecordsItsFreshRenderOnTheStepBeforeItLands(char state)
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = state == 'a' ? await FirstSaveCommitted(stash) : await SecondSaveCommitted(stash);
        stash.Close();
        var superseded = Row(stash, cat.Id).Steps.Single(s => s.Step == PendingStep.PlaceRender).From;
        var seen = new List<IReadOnlyList<PendingStep>>();
        using var watcher = new FileSystemWatcher(stash.Folder.Root, "cat.png") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        watcher.Created += (_, _) => Observe();
        watcher.Renamed += (_, _) => Observe();
        watcher.Changed += (_, _) => Observe();
        watcher.EnableRaisingEvents = true;

        var opening = await stash.Open();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Count() == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        watcher.EnableRaisingEvents = false;
        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        lock (seen)
        {
            Assert.IsNotEmpty(seen, "the render's arrival was seen");
            foreach (var steps in seen.Where(steps => steps.Count > 0))
            {
                var place = steps.Single(s => s.Step == PendingStep.PlaceRender);
                Assert.AreNotEqual(superseded, place.From, "never the superseded render");
                Assert.AreEqual(StillPng, place.Facts);
            }
        }

        Assert.IsEmpty(stash.WorkFiles());

        // On the watcher's thread, where an exception would end the test run: a failed read is an observation that fails the check.
        void Observe()
        {
            IReadOnlyList<PendingStep> steps;
            try
            {
                steps = stash.SqlQueryWhileOpen("SELECT pending_steps FROM items WHERE id = $id", r => r.IsDBNull(0) ? [] : PendingStep.Decode(r.GetString(0)), ("$id", cat.Id)).Single();
            }
            catch (Exception e)
            {
                steps = [new PendingStep(PendingStep.PlaceRender, superseded, e.Message)];
            }

            lock (seen)
            {
                seen.Add(steps);
            }
        }

        int Count()
        {
            lock (seen)
            {
                return seen.Count;
            }
        }
    }

    [TestMethod]
    public async Task AFreshRenderRefusedAtStartLeavesAPlacedSecondSaveListedAsSaved()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await SecondSavePlaced(stash);
        var placed = Row(stash, cat.Id).Steps.Single();
        var holds = new List<FileStream>();
        stash.Renderer = Holding(holds);

        try
        {
            var opening = await stash.Open();

            Assert.AreEqual(new Failure("Finish an unfinished save", "cat", HeldFileRetry.HeldReason), opening.Failures[0], string.Join("; ", opening.Failures));
            Assert.AreEqual(placed, Row(stash, cat.Id).Steps.Single(), "a done step is not recorded again");
            var listed = await Listed(stash.Store!, cat.Id);
            Assert.AreEqual((stash.Folder.InRoot("cat.png"), StillPng, 2L), (listed.FilePath, listed.Facts, listed.RenderedRevision));
            Assert.AreEqual(Arrow, (await ForEdit(stash.Store!, cat.Id)).RenderedLayers);
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }
    }

    [TestMethod]
    public async Task AFreshRenderRefusedAtStartUndoesAPlacedFirstSaveOriginalFirst()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await FirstSavePlaced(stash, "still.webp");
        var holds = new List<FileStream>();
        stash.Renderer = Holding(holds);

        try
        {
            var opening = await stash.Open();

            Assert.AreEqual(new Failure("Finish an unfinished save", "cat", HeldFileRetry.HeldReason), opening.Failures[0], string.Join("; ", opening.Failures));
            Assert.IsFalse(File.Exists(stash.Folder.InRoot("cat.png")), "the placed render is deleted");
            Assert.AreEqual(Hash(MediaFixtures.Bytes("still.webp")), Hash(stash.Folder.InRoot("cat.webp")));
            AssertBeforeTheSave(stash, cat);
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }
    }

    [TestMethod]
    public async Task ARenderRefusedAtStartUndoesASaveWithNothingMoved()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var other = await stash.ImportFixture("dog", "still.gif");
        var cat = await FirstSaveCommitted(stash);
        stash.Close();
        stash.Renderer = Refusing;

        var opening = await stash.Open();

        Assert.AreEqual(new Failure("Finish an unfinished save", "cat", RefusedReason), opening.Failures.Single());
        AssertBeforeTheSave(stash, cat);
        Assert.IsEmpty(stash.WorkFiles(), "the Save's render no row names any more is cleared");
        TestStash.AssertSameItem(cat, await Listed(stash.Store!, cat.Id));
        TestStash.AssertSameItem(other, await Listed(stash.Store!, other.Id));
    }

    [TestMethod]
    public async Task AnOriginalHeldThroughTheStartUndoesASaveWithNothingMoved()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await FirstSaveCommitted(stash);
        stash.Close();

        StoreOpening opening;
        using (TestStash.Hold(stash.Folder.InRoot("cat.png")))
        {
            opening = await stash.Open();
        }

        Assert.AreEqual(new Failure("Finish an unfinished save", "cat", HeldFileRetry.HeldReason), opening.Failures.Single());
        AssertBeforeTheSave(stash, cat);
    }

    [TestMethod]
    public async Task ARenderRefusedAtStartMovesTheOriginalBackAndUndoesTheSave()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await FirstSaveMoved(stash);
        stash.Close();
        stash.Renderer = Refusing;

        var opening = await stash.Open();

        Assert.AreEqual(new Failure("Finish an unfinished save", "cat", RefusedReason), opening.Failures.Single());
        Assert.AreEqual(Hash(MediaFixtures.Bytes("still.png")), Hash(stash.Folder.InRoot("cat.png")));
        AssertBeforeTheSave(stash, cat);
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task AnOriginalThatCannotMoveBackLeavesTheSaveListedOnItAndItsRenderKept()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await FirstSaveMoved(stash);
        stash.Close();
        var render = Row(stash, cat.Id).Steps.Single(s => s.Step == PendingStep.PlaceRender).From;
        stash.Renderer = Refusing;

        using (TestStash.Hold(stash.Folder.InOriginals("cat.png")))
        {
            var opening = await stash.Open();

            Assert.AreEqual(new Failure("Finish an unfinished save", "cat", RefusedReason), opening.Failures[0], string.Join("; ", opening.Failures));
            Assert.AreEqual(new Failure("Undo an unfinished save", "cat", HeldFileRetry.HeldReason), opening.Failures[1]);
            var listed = await Listed(stash.Store!, cat.Id);
            Assert.AreEqual((stash.Folder.InOriginals("cat.png"), StillPng, 0L), (listed.FilePath, listed.Facts, listed.RenderedRevision), "the original, where it is");
            Assert.AreEqual(LayerList.Empty, (await ForEdit(stash.Store!, cat.Id)).RenderedLayers);
            CollectionAssert.AreEqual(new[] { render }, stash.WorkFiles(), "the render its step names survives the clearing");
            stash.Close();
        }

        stash.Renderer = FinishedPicture.Render;
        var reopened = await stash.Open();

        Assert.IsEmpty(reopened.Failures, string.Join("; ", reopened.Failures));
        Assert.AreEqual((1L, 1L), (Row(stash, cat.Id).LayerRevision, Row(stash, cat.Id).RenderedRevision));
        Assert.IsEmpty(stash.WorkFiles(), "gone once the Save finished");
    }

    [TestMethod]
    [DataRow("still.png")]
    [DataRow("still.webp")]
    public async Task ARenderRefusedAtStartUndoesAPlacedFirstSaveAndDeletesItsRender(string fixture)
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await FirstSavePlaced(stash, fixture);
        stash.Renderer = Refusing;

        var opening = await stash.Open();

        Assert.AreEqual(new Failure("Finish an unfinished save", "cat", RefusedReason), opening.Failures.Single());
        CollectionAssert.AreEqual(new[] { cat.FileName }, stash.RootFiles(), "the original back, over a render of its name or with the render deleted");
        Assert.AreEqual(Hash(MediaFixtures.Bytes(fixture)), Hash(stash.Folder.InRoot(cat.FileName)));
        Assert.IsEmpty(OriginalFiles(stash));
        Assert.IsEmpty(stash.RecycleBin.Asked, "a render is deleted, never recycled (G29)");
        AssertBeforeTheSave(stash, cat);
    }

    [TestMethod]
    public async Task AHeldRenderOfAnotherNameLeavesTheOriginalBackAndTheNextStartFinishesTheSave()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await FirstSavePlaced(stash, "still.webp");
        stash.Renderer = Refusing;

        using (TestStash.Hold(stash.Folder.InRoot("cat.png")))
        {
            var opening = await stash.Open();

            Assert.AreEqual(new Failure("Undo an unfinished save", "cat", HeldFileRetry.HeldReason), opening.Failures[1], string.Join("; ", opening.Failures));
            var listed = await Listed(stash.Store!, cat.Id);
            Assert.AreEqual((stash.Folder.InRoot("cat.webp"), "cat.webp", cat.Facts, 0L), (listed.FilePath, listed.FileName, listed.Facts, listed.RenderedRevision), "listed on the original, back in the root");
            Assert.AreEqual((LayerList.Empty, stash.Folder.InRoot("cat.webp")), ((await ForEdit(stash.Store!, cat.Id)).RenderedLayers, (await ForEdit(stash.Store!, cat.Id)).OriginalPath));
            CollectionAssert.AreEqual(new[] { stash.Folder.InRoot("cat.webp") }, StoredPaths(stash, cat.Id).ToList(), "the render awaiting its delete is not the item's");
            stash.Close();
        }

        stash.Renderer = FinishedPicture.Render;
        var reopened = await stash.Open();

        Assert.IsEmpty(reopened.Failures, string.Join("; ", reopened.Failures));
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "cat.webp" }, OriginalFiles(stash));
        Assert.AreEqual((1L, 1L, true), (Row(stash, cat.Id).LayerRevision, Row(stash, cat.Id).RenderedRevision, Row(stash, cat.Id).OriginalInOriginals));
    }

    [TestMethod]
    public async Task AStartAfterAnUndoPutTheOriginalBackNeverListsTheSaveOnARenderThatIsGone()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await FirstSavePlaced(stash);
        // An earlier start's undo moved the original back over its render, then was killed before rolling the row back:
        // the place step reads done, though its render is gone.
        File.Move(stash.Folder.InOriginals("cat.png"), stash.Folder.InRoot("cat.png"), overwrite: true);
        var holds = new List<FileStream>();
        stash.Renderer = Holding(holds);
        // Once this start moves the original into originals\ again, another program holds it, so the undo of the place
        // the held render refuses cannot move it back: the start ends with the original in originals\ and nothing placed.
        using var watcher = new FileSystemWatcher(stash.Folder.Originals, "cat.png") { NotifyFilter = NotifyFilters.FileName };
        watcher.Created += (_, created) =>
        {
            lock (holds)
            {
                holds.Add(new FileStream(created.FullPath, FileMode.Open, FileAccess.Read, FileShare.None));
            }
        };
        watcher.EnableRaisingEvents = true;
        try
        {
            var opening = await stash.Open();
            watcher.EnableRaisingEvents = false;

            Assert.AreEqual(new Failure("Undo an unfinished save", "cat", HeldFileRetry.HeldReason), opening.Failures[1], string.Join("; ", opening.Failures));
            var listed = await Listed(stash.Store!, cat.Id);
            Assert.IsTrue(File.Exists(listed.FilePath), $"listed on {listed.FilePath}, which is not on disk");
            Assert.AreEqual((stash.Folder.InOriginals("cat.png"), StillPng, 0L), (listed.FilePath, listed.Facts, listed.RenderedRevision), "the original, where it is");
            Assert.AreEqual(LayerList.Empty, (await ForEdit(stash.Store!, cat.Id)).RenderedLayers);
            stash.Close();
        }
        finally
        {
            lock (holds)
            {
                holds.ForEach(hold => hold.Dispose());
            }
        }

        stash.Renderer = FinishedPicture.Render;
        var reopened = await stash.Open();

        Assert.IsEmpty(reopened.Failures, string.Join("; ", reopened.Failures));
        Assert.AreEqual((1L, 1L), (Row(stash, cat.Id).LayerRevision, Row(stash, cat.Id).RenderedRevision));
        Assert.AreEqual(Hash(MediaFixtures.Bytes("still.png")), Hash(stash.Folder.InOriginals("cat.png")));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task AnUndoDeletesARenderAnEarlierUndoCouldNotAndLeavesNoFileNoItemOwns()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await FirstSavePlaced(stash, "still.webp");
        // An earlier start's undo moved the original back and could not delete the render, held then.
        File.Move(stash.Folder.InOriginals("cat.webp"), stash.Folder.InRoot("cat.webp"));
        var holds = new List<FileStream>();
        stash.Renderer = Holding(holds);

        try
        {
            var opening = await stash.Open();

            Assert.AreEqual(new Failure("Finish an unfinished save", "cat", HeldFileRetry.HeldReason), opening.Failures[0], string.Join("; ", opening.Failures));
            CollectionAssert.AreEqual(new[] { "cat.webp" }, stash.RootFiles(), "the render left in the root is deleted with the undo");
            AssertBeforeTheSave(stash, cat);
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }
    }

    [TestMethod]
    public async Task AnOriginalThatCannotGoBackLeavesAPlacedFirstSaveListedAsSavedAndItsRenderInPlace()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await FirstSavePlaced(stash, "still.webp");
        stash.Renderer = Refusing;

        using (TestStash.Hold(stash.Folder.InOriginals("cat.webp")))
        {
            var opening = await stash.Open();

            Assert.AreEqual(new Failure("Undo an unfinished save", "cat", HeldFileRetry.HeldReason), opening.Failures[1], string.Join("; ", opening.Failures));
            Assert.IsTrue(File.Exists(stash.Folder.InRoot("cat.png")), "the render is not deleted before the original is back");
            var listed = await Listed(stash.Store!, cat.Id);
            Assert.AreEqual((stash.Folder.InRoot("cat.png"), "cat.png", 1L), (listed.FilePath, listed.FileName, listed.RenderedRevision));
        }
    }

    /// <summary>The row of "cat" exactly as before its first Save: unedited, its one file in the root, nothing pending.</summary>
    private static void AssertBeforeTheSave(TestStash stash, StashItem cat)
    {
        var row = Row(stash, cat.Id);
        Assert.AreEqual(new SavedRow("[]", null, 0, 0, false, cat.FileName, cat.FileName, row.Steps, cat.Facts, null), row);
        Assert.IsEmpty(row.Steps);
        Assert.IsTrue(File.Exists(cat.FilePath));
    }
}
