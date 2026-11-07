using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditedItems;

namespace ImageStash.Tests.Store;

/// <summary>
/// A change to an item whose Save or Revert is pending while the store runs (State: A live failure, Every change; G18,
/// G28, G35): Rename, Delete, an import's Undo and Save finish it first, a Save by rendering it again through the store's
/// renderer, its fresh render recorded on the step before it lands, then act on the row as it is after; while it cannot
/// finish, the change is refused by name and the row, its steps and its files stay as they were, for the next start.
/// While the original is held the render is asked again, and while the fresh render's readback is held that file is read
/// back again, never rendered again, and a written file that does not decode is made again at a path of its own, never
/// placed; the summon's listing is answered meanwhile (G1, G35).
/// </summary>
[TestClass]
public sealed class ItemSaveRecoveryBeforeChangeTests
{
    private static readonly MediaFacts StillPng = new(ImageFormat.Png, 8, 6, false, 1, 0);

    [TestMethod]
    public async Task ASecondSaveRefusedLiveStaysOnTheEarlierRenderAndEveryLaterChangeFinishesItOrIsRefused()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var first = (await stash.SaveEdit(cat, Cropped)).Value!;
        var render = await stash.Render(first, Arrow);
        var held = TestStash.Hold(render.WorkPath);
        try
        {
            var saved = await store.Save(first, Arrow, render);

            Assert.AreEqual(new Failure("Save", "cat", HeldFileRetry.HeldReason), saved.Failure);
            var listed = await Listed(store, cat.Id);
            Assert.AreEqual((first.FilePath, first.Facts, 1L), (listed.FilePath, listed.Facts, listed.RenderedRevision), "the earlier render, 4 × 3");
            var steps = Row(stash, cat.Id).Steps;

            stash.Renderer = Refusing;
            var unfinished = $"its earlier save cannot finish yet: {RefusedReason}";
            Assert.AreEqual(new Failure("Rename", "cat", unfinished), (await store.Rename(listed, "dog")).Failure);
            Assert.AreEqual(new Failure("Delete", "cat", unfinished), (await store.Delete([cat.Id])).Single().Failure);
            var third = await stash.Render(listed, BlackBoxAndBox);
            Assert.AreEqual(new Failure("Save", "cat", unfinished), (await store.Save(listed, BlackBoxAndBox, third)).Failure);
            Assert.IsTrue(File.Exists(third.WorkPath), "the third render stays the caller's");
            Assert.IsTrue((await store.Discard(third.WorkPath)).Succeeded);
            CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles(), "nothing renamed");
            CollectionAssert.AreEqual(new[] { Path.GetFileName(render.WorkPath) }, stash.WorkFiles(), "no held copy left behind");
            CollectionAssert.AreEqual(steps.ToList(), Row(stash, cat.Id).Steps.ToList(), "the row and its steps as they were");
            Assert.IsFalse(stash.SqlQueryWhileOpen("SELECT deleted FROM items WHERE id = $id", r => r.GetBoolean(0), ("$id", cat.Id)).Single(), "nothing marked");

            stash.Renderer = FinishedPicture.Render;
            var deleted = (await store.Delete([cat.Id])).Single();

            Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
            Assert.HasCount(4, stash.RenderCalls, "the three refused finishes and the one that finished");
            Assert.AreEqual((stash.Folder.InOriginals("cat.png"), Arrow, ImageFormat.Png), stash.RenderCalls[^1]);
            Assert.HasCount(2, deleted.Value!.RecycledIdentities, "the fresh render and the original, both recycled");
            Assert.IsEmpty(stash.RootFiles());
            Assert.IsEmpty(OriginalFiles(stash));
            Assert.Contains(Path.GetFileName(render.WorkPath), stash.WorkFiles(), "the held render is left, and no failure named for it");

            var restored = (await store.UndoDelete([deleted.Value])).Single();

            Assert.IsTrue(restored.Succeeded, restored.Failure?.ToString());
            var back = await Listed(store, cat.Id);
            Assert.AreEqual((stash.Folder.InRoot("cat.png"), StillPng, 2L), (back.FilePath, back.Facts, back.RenderedRevision), "back as saved with the second list");
            Assert.AreEqual(Hash(MediaFixtures.Bytes("still.png")), Hash(stash.Folder.InOriginals("cat.png")));
        }
        finally
        {
            held.Dispose();
        }

        var reopened = await stash.Reopen();
        Assert.IsEmpty(reopened.Failures, string.Join("; ", reopened.Failures));
        Assert.IsEmpty(stash.WorkFiles(), "the held render is cleared once released");
    }

    // A tag, the favorite and a use touch no file, so they commit at once and leave a refused Save's steps to the item's
    // next change of its files or the next start ("a change of its tags, favorite or uses ... leaves them").
    [TestMethod]
    public async Task ATagTheFavoriteAndAUseAfterASaveRefusedLiveCommitAndLeaveItsStepsToTheNextStart()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var first = (await stash.SaveEdit(cat, Cropped)).Value!;
        var render = await stash.Render(first, Arrow);
        using (TestStash.Hold(render.WorkPath))
        {
            Assert.AreEqual(new Failure("Save", "cat", HeldFileRetry.HeldReason), (await store.Save(first, Arrow, render)).Failure);
        }

        var listed = await Listed(store, cat.Id);
        var steps = Row(stash, cat.Id).Steps;
        Assert.IsNotEmpty(steps, "the refused Save's steps are pending");
        var renders = stash.RenderCalls.Count;

        Assert.IsTrue((await store.AddTag([listed], "smug")).Single().Succeeded);
        Assert.IsTrue((await store.SetFavorite([listed], true)).Single().Succeeded);
        Assert.IsTrue((await store.RecordUses([listed], stash.Clock.Now)).Succeeded);

        CollectionAssert.AreEqual(steps.ToList(), Row(stash, cat.Id).Steps.ToList(), "the steps left as they were");
        Assert.AreEqual(renders, stash.RenderCalls.Count, "nothing rendered for them");
        var after = await Listed(store, cat.Id);
        Assert.AreEqual((first.FilePath, first.Facts, 1L), (after.FilePath, after.Facts, after.RenderedRevision), "still the picture on disk");
        Assert.AreEqual((true, "smug", 1), (after.Favorite, after.Tags.Single(), (await store.UseHistory(cat.Id)).Value!.Count));

        var reopened = await stash.Reopen();

        Assert.IsEmpty(reopened.Failures, string.Join("; ", reopened.Failures));
        Assert.IsEmpty(Row(stash, cat.Id).Steps, "the start finished the Save");
        var finished = await Listed(reopened.Store!, cat.Id);
        Assert.AreEqual((StillPng, 2L, true, "smug", 1), (finished.Facts, finished.RenderedRevision, finished.Favorite, finished.Tags.Single(), (await reopened.Store!.UseHistory(cat.Id)).Value!.Count));
    }

    [TestMethod]
    public async Task ADeleteFinishesAFirstSaveThenDeletesBothFilesAndUndoBringsBothBackAsSaved()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await FirstSaveMoved(stash);

        var deleted = (await store.Delete([cat.Id])).Single();

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        Assert.HasCount(1, stash.RenderCalls);
        Assert.HasCount(2, deleted.Value!.RecycledIdentities, "the row read again after the finish has two files");
        Assert.IsEmpty(stash.RootFiles());

        var restored = (await store.UndoDelete([deleted.Value])).Single();

        Assert.IsTrue(restored.Succeeded, restored.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        Assert.AreEqual(Hash(MediaFixtures.Bytes("still.png")), Hash(stash.Folder.InOriginals("cat.png")));
        var row = Row(stash, cat.Id);
        Assert.AreEqual(new SavedRow(BlackBoxAndBox.ToJson(), null, 1, 1, true, "cat.png", "cat.png", row.Steps, StillPng, StillPng), row);
        Assert.IsEmpty(row.Steps);
        Assert.AreEqual((StillPng, 1L), ((await Listed(store, cat.Id)).Facts, (await Listed(store, cat.Id)).RenderedRevision));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ADeleteRefusedOverASaveThatCannotFinishLeavesItForTheStartToUndo()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await FirstSaveMoved(stash);
        var steps = Row(stash, cat.Id).Steps;
        stash.Renderer = Refusing;

        var refused = (await store.Delete([cat.Id])).Single();

        Assert.AreEqual(new Failure("Delete", "cat", $"its earlier save cannot finish yet: {RefusedReason}"), refused.Failure);
        Assert.IsEmpty(stash.RecycleBin.Asked);
        CollectionAssert.AreEqual(steps.ToList(), Row(stash, cat.Id).Steps.ToList(), "the steps kept");
        Assert.IsFalse(stash.SqlQueryWhileOpen("SELECT deleted FROM items WHERE id = $id", r => r.GetBoolean(0), ("$id", cat.Id)).Single());

        var reopened = await stash.Reopen();

        Assert.AreEqual(new Failure("Finish an unfinished save", "cat", RefusedReason), reopened.Failures.Single());
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles(), "the original back in the root");
        Assert.AreEqual((0L, false), (Row(stash, cat.Id).LayerRevision, Row(stash, cat.Id).OriginalInOriginals));

        var deleted = (await stash.Store!.Delete([cat.Id])).Single();

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        Assert.HasCount(1, deleted.Value!.RecycledIdentities, "the unedited item's one file");
    }

    [TestMethod]
    public async Task ALiveFinishRecordsItsFreshRenderOnTheStepBeforePlacingIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await SecondSaveCommitted(stash);
        var superseded = Row(stash, cat.Id).Steps.Single().From;
        var holds = new List<FileStream>();
        stash.Renderer = Holding(holds);
        try
        {
            var renamed = await store.Rename(cat, "dog");

            Assert.AreEqual(new Failure("Rename", "cat", $"its earlier save cannot finish yet: {HeldFileRetry.HeldReason}"), renamed.Failure);
            var fresh = stash.WorkFiles().Single();
            Assert.AreNotEqual(superseded, fresh, "the superseded render is discarded");
            Assert.AreEqual(new PendingStep(PendingStep.PlaceRender, fresh, "cat.png", Facts: StillPng), Row(stash, cat.Id).Steps.Single(), "the step names and describes the render that will land");
            Assert.AreEqual((1L, ImageFormat.Png), ((await Listed(store, cat.Id)).RenderedRevision, (await Listed(store, cat.Id)).Facts.Format));
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }

        stash.Renderer = FinishedPicture.Render;
        var finished = await store.Rename(cat, "dog");

        Assert.IsTrue(finished.Succeeded, finished.Failure?.ToString());
        Assert.AreEqual(("dog.png", StillPng, 2L), (finished.Value!.FileName, finished.Value.Facts, finished.Value.RenderedRevision));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task TheListingIsAnsweredWhileARenameWaitsToRenderAPendingSaveOverItsHeldOriginalAndTheSaveThenFinishesFirst()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var first = await SecondSaveCommitted(stash);
        Assert.IsTrue((await store.RecordUses([first], stash.Clock.Now)).Succeeded);
        var answers = new List<string?>();
        stash.Renderer = (original, name, layers, format, work, retry) =>
        {
            var rendered = FinishedPicture.Render(original, name, layers, format, work, retry);
            answers.Add(rendered.Failure?.Reason);
            return rendered;
        };
        var unshared = new FileStream(stash.Folder.InOriginals("cat.png"), FileMode.Open, FileAccess.Read, FileShare.None);

        var (recent, renamed) = await ReadsWhileAChangeWaits.Asked(unshared, () => store.Rename(first, "dog"), store.Recent);

        var listed = recent.Value!.Single();
        Assert.AreEqual((first.FilePath, first.Facts), (listed.FilePath, listed.Facts), "listed on the picture on disk, the earlier render (State: A live failure)");
        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        Assert.IsGreaterThan(1, answers.Count(reason => reason == HeldFileRetry.HeldReason), "asked again while the original is held (G35)");
        CollectionAssert.AreEqual(new string?[] { null }, answers.Where(reason => reason != HeldFileRetry.HeldReason).ToList(), "rendered once the hold went");
        Assert.AreEqual(("dog.png", StillPng, 2L), (renamed.Value!.FileName, renamed.Value.Facts, renamed.Value.RenderedRevision), "the Save finished first, then renamed");
        CollectionAssert.AreEqual(new[] { "dog.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "dog.png" }, OriginalFiles(stash));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task ARenderWhoseReadbackIsHeldHasItsFileReadBackAgainWhileTheListingIsAnswered()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var first = await SecondSaveCommitted(stash);
        Assert.IsTrue((await store.RecordUses([first], stash.Clock.Now)).Succeeded);
        var scanner = new Scanner();
        stash.Renderer = (original, name, layers, format, work, retry) =>
        {
            var rendered = FinishedPicture.Render(original, name, layers, format, work, retry);
            // The scanner takes the fresh render as it closes, so Media's readback of it answers a held file (E3).
            return rendered.Succeeded && scanner.Takes(work) ? new Failure("Save the picture", name, HeldFileRetry.HeldReason) : rendered;
        };

        var (recent, renamed) = await ReadsWhileAChangeWaits.Asked(scanner, () => store.Rename(first, "dog"), store.Recent);

        Assert.AreEqual(first.FilePath, recent.Value!.Single().FilePath, "listed on the picture on disk (State: A live failure)");
        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        Assert.HasCount(1, stash.RenderCalls, "rendered once: another fresh file would be one the scanner takes again");
        Assert.IsGreaterThan(1, stash.ReadCalls.Count, "its file read back again while held (G35)");
        Assert.AreEqual(("dog.png", StillPng, 2L), (renamed.Value!.FileName, renamed.Value.Facts, renamed.Value.RenderedRevision), "the Save finished first, then renamed");
        CollectionAssert.AreEqual(new[] { "dog.png" }, stash.RootFiles());
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task ARenderThatOutlastsTheHeldFileDurationStillHasItsHeldReadbackAskedAgain()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(1) };
        var store = await stash.OpenCleanly();
        var first = await SecondSaveCommitted(stash);
        var lettingGo = new List<Task>();
        stash.Renderer = (original, name, layers, format, work, retry) =>
        {
            // A render longer than the held-file duration, whose fresh file a scanner then holds for a moment (E3).
            Thread.Sleep(stash.Retry + TimeSpan.FromMilliseconds(200));
            var rendered = FinishedPicture.Render(original, name, layers, format, work, retry);
            var scan = new FileStream(work, FileMode.Open, FileAccess.Read, FileShare.None);
            lettingGo.Add(Task.Run(async () =>
            {
                await Task.Delay(300);
                await scan.DisposeAsync();
            }));
            return rendered.Succeeded ? new Failure("Save the picture", name, HeldFileRetry.HeldReason) : rendered;
        };

        var renamed = await store.Rename(first, "dog");
        await Task.WhenAll(lettingGo);

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        Assert.HasCount(1, stash.RenderCalls);
        Assert.AreEqual(("dog.png", StillPng, 2L), (renamed.Value!.FileName, renamed.Value.Facts, renamed.Value.RenderedRevision), "the held file owed the whole duration from its first refusal (G35)");
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ARenderAnsweredHeldWhoseWrittenBytesDoNotDecodeIsMadeAgainAtAPathOfItsOwnAndNeverPlaced()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var first = await SecondSaveCommitted(stash);
        var partial = new List<string>();
        stash.Renderer = (original, name, layers, format, work, retry) =>
        {
            var rendered = FinishedPicture.Render(original, name, layers, format, work, retry);
            if (partial.Count > 0 || !rendered.Succeeded)
            {
                return rendered;
            }

            // A write stopped part way and answered held: no holder E3 admits does this, so the branch is driven here.
            var bytes = File.ReadAllBytes(work);
            File.WriteAllBytes(work, bytes[..(bytes.Length / 2)]);
            partial.Add(work);
            return new Failure("Save the picture", name, HeldFileRetry.HeldReason);
        };

        var renamed = await store.Rename(first, "dog");

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        Assert.HasCount(2, stash.RenderCalls, "made again once its written bytes did not decode");
        Assert.IsFalse(File.Exists(partial.Single()), "the partial render discarded, never placed");
        Assert.AreEqual(("dog.png", StillPng, 2L), (renamed.Value!.FileName, renamed.Value.Facts, renamed.Value.RenderedRevision));
        Assert.AreEqual(StillPng, MediaDecoder.ReadPicture(renamed.Value.FilePath, "dog", TimeSpan.Zero).Value!.Facts, "the placed picture decodes whole");
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task AnImportsUndoFinishesAPendingSaveThenRecyclesBothFiles()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await FirstSaveMoved(stash);

        var removed = (await store.Remove([cat.Id])).Single();

        Assert.IsTrue(removed.Succeeded, removed.Failure?.ToString());
        Assert.HasCount(1, stash.RenderCalls);
        Assert.HasCount(2, stash.RecycleBin.Asked);
        Assert.IsEmpty(stash.RootFiles());
        Assert.IsEmpty(OriginalFiles(stash));
    }

    [TestMethod]
    public async Task ASaveOverAPendingSaveFinishesItFirstThenSavesItsOwnList()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await SecondSaveCommitted(stash);
        var third = await stash.Render(cat, BlackBoxAndBox);
        var bytes = File.ReadAllBytes(third.WorkPath);

        var saved = await store.Save(cat, BlackBoxAndBox, third);

        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        Assert.HasCount(1, stash.RenderCalls, "the pending Save rendered again, the new one not");
        Assert.AreEqual(Arrow, stash.RenderCalls[0].Layers);
        Assert.AreEqual(3L, saved.Value!.RenderedRevision);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(stash.Folder.InRoot("cat.png")));
        Assert.AreEqual(BlackBoxAndBox.ToJson(), Row(stash, cat.Id).Layers);
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ARenameFinishesAPendingRevertFirstOrIsRefusedWhileItCannot()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await RevertMoved(stash);

        Outcome<StashItem> refused;
        using (TestStash.Hold(stash.Folder.InRoot("cat.png")))
        {
            refused = await store.Rename(cat, "dog");
        }

        Assert.AreEqual(new Failure("Rename", "cat", $"its earlier revert to the original cannot finish yet: {HeldFileRetry.HeldReason}"), refused.Failure);
        CollectionAssert.AreEquivalent(new[] { "cat.png", "cat.webp" }, stash.RootFiles());

        var renamed = await store.Rename(cat, "dog");

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { "dog.webp" }, stash.RootFiles(), "the render deleted, then the one file renamed");
        Assert.IsEmpty(OriginalFiles(stash));
        Assert.IsEmpty(stash.RenderCalls);
        Assert.AreEqual(("[]", 2L, 2L, false), (Row(stash, cat.Id).Layers, Row(stash, cat.Id).LayerRevision, Row(stash, cat.Id).RenderedRevision, Row(stash, cat.Id).OriginalInOriginals));
    }

    /// <summary>
    /// A scanner that takes every fresh render as it closes and holds it without sharing, as a virus scanner may (E3),
    /// until the check lets go of it all.
    /// </summary>
    private sealed class Scanner : IDisposable
    {
        private readonly List<FileStream> holds = [];
        private bool lettingGo;

        /// <summary>Holds <paramref name="path"/> and answers true, or false once the check has let go.</summary>
        public bool Takes(string path)
        {
            lock (holds)
            {
                if (lettingGo)
                {
                    return false;
                }

                holds.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None));
                return true;
            }
        }

        public void Dispose()
        {
            lock (holds)
            {
                lettingGo = true;
                holds.ForEach(hold => hold.Dispose());
            }
        }
    }
}
