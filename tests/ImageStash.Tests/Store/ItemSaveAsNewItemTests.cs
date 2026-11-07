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
/// Save as new item on the store (G26, State: Save as new item, Every change, A live failure, File names; F6, F7, G12,
/// G28, G35, R9): a second item from a copy of the original that Store makes, carrying the layers and the same tags, not
/// favorite, named with a counter, both its files from one stem, the item it came from left exactly as it was; without
/// layers the copy alone; a refusal before the insert inserting nothing; a step refused after it leaving the new row
/// unlisted; every kill state reopened leaving the new item absent or whole, the renderer never called for it; the
/// summon's listing answered while the copy waits on a held original (G1).
/// </summary>
[TestClass]
public sealed class ItemSaveAsNewItemTests
{
    [TestMethod]
    public async Task WithLayersASecondItemIsMadeFromACopyOfTheOriginalWithTheSameTagsNotFavoriteAndACounterName()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var imported = await stash.ImportFixture("the audacity", "animated.webp");
        stash.Tag(imported.Id, "yes");
        stash.Tag(imported.Id, "reaction");
        stash.SqlWhileOpen("UPDATE items SET favorite = 1 WHERE id = $id", ("$id", imported.Id));
        Assert.IsEmpty((await store.RecordUses([imported], stash.Clock.Now)).Value!);
        var source = (await stash.SaveEdit(await Listed(store, imported.Id), BlackBoxAndBox)).Value!;
        var render = await stash.Render(source, Arrow);
        var rendered = File.ReadAllBytes(render.WorkPath);
        var sourceRow = AllColumns(stash, source.Id);
        var filesBefore = FilesWithHashes(stash).Where(file => !file.StartsWith(".work")).ToList();
        var before = await Commits(store, source.Id);
        stash.Clock.Now = stash.Clock.Now.AddMinutes(1);

        var made = await store.SaveAsNewItem(source, Arrow, render);

        Assert.IsTrue(made.Succeeded, made.Failure?.ToString());
        var copy = made.Value!;
        Assert.AreEqual(("the audacity 2", false, stash.Clock.Now), (copy.Name, copy.Favorite, copy.AddedAt));
        CollectionAssert.AreEqual(new[] { "yes", "reaction" }, copy.Tags.ToList());
        Assert.AreEqual((render.Facts, ImageFormat.Gif, true), (copy.Facts, copy.Facts.Format, copy.Facts.Animated), "the render's facts (F3)");
        Assert.AreEqual(("the audacity 2.gif", stash.Folder.InRoot("the audacity 2.gif")), (copy.FileName, copy.FilePath));
        CollectionAssert.AreEqual(rendered, File.ReadAllBytes(copy.FilePath), "the render is its finished picture");
        Assert.AreEqual(Hash(MediaFixtures.Bytes("animated.webp")), Hash(stash.Folder.InOriginals("the audacity 2.webp")), "a byte copy of the original");
        CollectionAssert.AreEqual(
            filesBefore.Concat([$@"originals\the audacity 2.webp {Hash(MediaFixtures.Bytes("animated.webp"))}", $"the audacity 2.gif {Hash(rendered)}"]).Order().ToList(),
            FilesWithHashes(stash),
            "every file of the item it came from as it was, and the two new ones");
        Assert.IsEmpty(stash.WorkFiles());
        Assert.AreEqual(sourceRow, AllColumns(stash, source.Id), "the item it came from is left exactly as it was");
        Assert.AreEqual(1, (await store.UseHistory(source.Id)).Value!.Count);
        Assert.AreEqual(0, (await store.UseHistory(copy.Id)).Value!.Count, "no use is copied");
        var row = Row(stash, copy.Id);
        Assert.AreEqual(
            (Arrow.ToJson(), (string?)null, row.LayerRevision, true, "the audacity 2.gif", "the audacity 2.webp", render.Facts, 0),
            (row.Layers, row.PreviousLayers, row.RenderedRevision, row.OriginalInOriginals, row.FinishedFile, row.OriginalFile, row.Finished, row.Steps.Count));
        Assert.AreEqual(Row(stash, source.Id).Original, row.Original, "the original's record is copied");
        Assert.AreEqual(OriginalHash(stash, source.Id), OriginalHash(stash, copy.Id));
        var edit = await ForEdit(store, copy.Id);
        Assert.AreEqual((Arrow, Arrow, ImageFormat.Gif, stash.Folder.InOriginals("the audacity 2.webp")), (edit.Layers, edit.RenderedLayers, edit.FinishedFormat, edit.OriginalPath));
        Assert.AreEqual(copy.Id, (await stash.All()).First().Id, "newest added first");
        TestStash.AssertSameItem(copy, await Listed(store, copy.Id));
        Assert.AreEqual(before + 2, await Commits(store, copy.Id), "the insert and the clearing, nothing more");
        Assert.IsEmpty(stash.RenderCalls, "the caller rendered");
    }

    [TestMethod]
    public async Task EachFurtherCopyTakesTheFirstCounterNoRowsNameHoldsCountingFromTheItemsOwnName()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var audacity = await stash.ImportFixture("the audacity", "still.png");

        var second = (await store.SaveAsNewItem(audacity, LayerList.Empty, null)).Value!;
        var third = (await store.SaveAsNewItem(audacity, LayerList.Empty, null)).Value!;
        var ofSecond = (await store.SaveAsNewItem(second, LayerList.Empty, null)).Value!;

        Assert.AreEqual(("the audacity 2", "the audacity 3", "the audacity 2 2"), (second.Name, third.Name, ofSecond.Name));
        Assert.AreEqual(("the audacity 2.png", "the audacity 3.png", "the audacity 2 2.png"), (second.FileName, third.FileName, ofSecond.FileName));
    }

    [TestMethod]
    public async Task ANameARowADeleteMarkedStillHoldsIsTakenIgnoringCaseAndAnUnnamedItemsCopyIsTheCounterAlone()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var audacity = await stash.ImportFixture("the audacity", "still.png");
        var marked = await stash.Import("THE AUDACITY 2");
        Assert.IsTrue((await store.Delete([marked.Id])).Single().Succeeded, "its notice's Undo can still bring the name back");
        var unnamed = await stash.Import("");

        var copy = (await store.SaveAsNewItem(audacity, LayerList.Empty, null)).Value!;
        var unnamedCopy = (await store.SaveAsNewItem(unnamed, LayerList.Empty, null)).Value!;

        Assert.AreEqual(("the audacity 3", "the audacity 3.png"), (copy.Name, copy.FileName));
        Assert.AreEqual(("2", "2.png"), (unnamedCopy.Name, unnamedCopy.FileName));
    }

    [TestMethod]
    public async Task AFileNameTakenInTheRootGivesBothFilesASecondCounterWhileTheNameKeepsItsOwn()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var audacity = await stash.ImportFixture("the audacity", "animated.webp");
        var other = await stash.ImportFixture("the audacity 2", "still.gif");
        // Renamed while another program held its file: named "other", its file still the audacity 2.gif, its step pending.
        using (TestStash.Hold(other.FilePath))
        {
            Assert.IsFalse((await store.Rename(other, "other")).Succeeded);
        }

        var made = await store.SaveAsNewItem(audacity, BlackBoxAndBox, await stash.Render(audacity, BlackBoxAndBox));

        Assert.IsTrue(made.Succeeded, made.Failure?.ToString());
        Assert.AreEqual(("the audacity 2", "the audacity 2 2.gif"), (made.Value!.Name, made.Value.FileName));
        CollectionAssert.AreEqual(new[] { "the audacity 2 2.webp" }, OriginalFiles(stash), "one stem for both files (State: File names)");
        Assert.AreEqual(stash.Folder.InRoot("the audacity 2.gif"), (await Listed(store, other.Id)).FilePath, "the other item's file is where it was");
    }

    [TestMethod]
    public async Task WithoutLayersTheCopyAloneIsPlacedInTheRootAsTheNewItemsOneFile()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var dog = (await stash.SaveEdit(await stash.ImportFixture("dog", "still.webp"), BlackBoxAndBox)).Value!;

        var plainCat = await store.SaveAsNewItem(cat, LayerList.Empty, null);
        var plainDog = await store.SaveAsNewItem(dog, LayerList.Empty, null);

        Assert.IsTrue(plainCat.Succeeded && plainDog.Succeeded, $"{plainCat.Failure} {plainDog.Failure}");
        Assert.AreEqual(("cat 2", "cat 2.png", stash.Folder.InRoot("cat 2.png"), cat.Facts), (plainCat.Value!.Name, plainCat.Value.FileName, plainCat.Value.FilePath, plainCat.Value.Facts));
        Assert.AreEqual(("dog 2", "dog 2.webp", Row(stash, dog.Id).Original), (plainDog.Value!.Name, plainDog.Value.FileName, plainDog.Value.Facts), "a copy of the original alone, its own format (F7)");
        Assert.IsNull(TileBadge.Of(plainDog.Value));
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(plainCat.Value.FilePath));
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.webp"), File.ReadAllBytes(plainDog.Value.FilePath));
        CollectionAssert.AreEquivalent(new[] { "cat.png", "dog.png", "cat 2.png", "dog 2.webp" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "dog.webp" }, OriginalFiles(stash), "nothing new in originals\\");
        foreach (var made in new[] { plainCat.Value, plainDog.Value })
        {
            var row = Row(stash, made.Id);
            Assert.AreEqual(("[]", false, made.FileName, made.FileName, null, 0), (row.Layers, row.OriginalInOriginals, row.FinishedFile, row.OriginalFile, row.Finished, row.Steps.Count));
            Assert.AreEqual((LayerList.Empty, LayerList.Empty), ((await ForEdit(store, made.Id)).Layers, (await ForEdit(store, made.Id)).RenderedLayers));
        }

        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ARefusalBeforeTheInsertInsertsNothingAndLeavesTheRenderTheCallers()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var render = await stash.Render(cat, BlackBoxAndBox);
        var outside = stash.Outside("render.png");
        File.Copy(render.WorkPath, outside);
        Outcome<StashItem> held;
        using (stash.HoldStore())
        {
            held = await store.SaveAsNewItem(cat, BlackBoxAndBox, render);
        }

        Assert.AreEqual(new Failure(ItemSaveAsNewItem.Action, StashFolder.StoreFileName, HeldFileRetry.HeldReason), held.Failure);
        Assert.AreEqual(new Failure("Save as new item", "cat", "its layers came without the picture rendered from them"), (await store.SaveAsNewItem(cat, BlackBoxAndBox, null)).Failure);
        Assert.AreEqual(new Failure("Save as new item", "cat", "a copy without layers takes no rendered picture"), (await store.SaveAsNewItem(cat, LayerList.Empty, render)).Failure);
        Assert.AreEqual(new Failure("Save as new item", "cat", "its rendered picture is not in the stash's work folder"), (await store.SaveAsNewItem(cat, BlackBoxAndBox, render with { WorkPath = outside })).Failure);
        Assert.AreEqual(new Failure("Save as new item", "cat", "its rendered picture is gone"), (await store.SaveAsNewItem(cat, BlackBoxAndBox, render with { WorkPath = stash.Folder.InWork("gone.png") })).Failure);
        Assert.AreEqual(new Failure("Save as new item", "cat", ItemSave.ChangedWhileRendered), (await store.SaveAsNewItem(cat, BlackBoxAndBox, render with { OriginalSha256 = Convert.FromHexString(Hash(MediaFixtures.Bytes("still.webp"))) })).Failure, "decoded from bytes that are not the item's original");
        CollectionAssert.AreEqual(new[] { Path.GetFileName(render.WorkPath) }, stash.WorkFiles(), "no copy is left, and the render stays the caller's");
        Assert.AreEqual(1L, stash.SqlQueryWhileOpen("SELECT COUNT(*) FROM items", r => r.GetInt64(0)).Single());

        Assert.IsTrue((await store.Delete([cat.Id])).Single().Succeeded);

        Assert.AreEqual(new Failure("Save as new item", "cat", ItemFactChange.NotInStash), (await store.SaveAsNewItem(cat, BlackBoxAndBox, render)).Failure);
        Assert.AreEqual(1L, stash.SqlQueryWhileOpen("SELECT COUNT(*) FROM items", r => r.GetInt64(0)).Single());
        Assert.IsTrue(File.Exists(render.WorkPath));
    }

    [TestMethod]
    public async Task AnOriginalHeldWithoutReadSharingIsRetriedThenNamedAndNothingIsInserted()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = (await stash.SaveEdit(await stash.ImportFixture("cat", "still.webp"), BlackBoxAndBox)).Value!;
        var render = await stash.Render(cat, Arrow);
        var clock = Stopwatch.StartNew();
        Outcome<StashItem> made;
        // Read sharing would let the copy read it, as a scanner's hold does; this one shares nothing.
        using (new FileStream(stash.Folder.InOriginals("cat.webp"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            made = await store.SaveAsNewItem(cat, Arrow, render);
        }

        Assert.AreEqual(new Failure("Save as new item", "cat", HeldFileRetry.HeldReason), made.Failure);
        Assert.IsGreaterThanOrEqualTo(TestStash.ShortRetry, clock.Elapsed, "retried before it is named (G35)");
        CollectionAssert.AreEqual(new[] { Path.GetFileName(render.WorkPath) }, stash.WorkFiles());
        Assert.AreEqual(cat.Id, (await stash.All()).Single().Id);
        Assert.AreEqual(1L, stash.SqlQueryWhileOpen("SELECT COUNT(*) FROM items", r => r.GetInt64(0)).Single());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task TheSummonsListingIsAnsweredWhileTheCopyWaitsOnAHeldOriginalAndTheNewItemIsThenMade()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var cat = (await stash.SaveEdit(await stash.ImportFixture("cat", "still.webp"), BlackBoxAndBox)).Value!;
        Assert.IsTrue((await store.RecordUses([cat], stash.Clock.Now)).Succeeded);
        var render = await stash.Render(cat, Arrow);
        IReadOnlyList<StashItem>? allMeanwhile = null;

        var (recent, made) = await ReadsWhileAChangeWaits.Asked(
            new FileStream(stash.Folder.InOriginals("cat.webp"), FileMode.Open, FileAccess.Read, FileShare.None),
            () => store.SaveAsNewItem(cat, Arrow, render),
            async () =>
            {
                var listed = await store.Recent();
                allMeanwhile = (await store.All()).Value;
                return listed;
            });

        Assert.AreEqual(cat.Id, recent.Value!.Single().Id);
        Assert.AreEqual(cat.Id, allMeanwhile!.Single().Id, "no new row is listed before its files are in place");
        Assert.IsTrue(made.Succeeded, made.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { made.Value!.Id, cat.Id }, (await stash.All()).Select(item => item.Id).ToList());
    }

    [TestMethod]
    public async Task AnItemsPendingSaveIsFinishedFirstAndOneThatCannotFinishRefusesTheCopyByName()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await Listed(store, (await FirstSaveMoved(stash)).Id);
        var render = await stash.Render(cat, Arrow);
        stash.Renderer = Refusing;

        var refused = await store.SaveAsNewItem(cat, Arrow, render);

        Assert.AreEqual(new Failure("Save as new item", "cat", $"its earlier save cannot finish yet: {RefusedReason}"), refused.Failure);
        Assert.AreEqual(1L, stash.SqlQueryWhileOpen("SELECT COUNT(*) FROM items", r => r.GetInt64(0)).Single());
        Assert.IsTrue(File.Exists(render.WorkPath));

        stash.Renderer = FinishedPicture.Render;
        var made = await store.SaveAsNewItem(cat, Arrow, render);

        Assert.IsTrue(made.Succeeded, made.Failure?.ToString());
        Assert.IsEmpty(Row(stash, cat.Id).Steps, "the item's own Save finished first");
        Assert.AreEqual((1L, 1L), (Row(stash, cat.Id).LayerRevision, Row(stash, cat.Id).RenderedRevision));
        Assert.AreEqual(("cat 2", "cat 2.png"), (made.Value!.Name, made.Value.FileName));
        CollectionAssert.AreEquivalent(new[] { "cat.png", "cat 2.png" }, stash.RootFiles());
        CollectionAssert.AreEquivalent(new[] { "cat.png", "cat 2.png" }, OriginalFiles(stash));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ARenderHeldThroughItsPlaceFailsAfterTheInsertUnlistedAndTheNextStartRemovesTheRowWithEveryFile()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.webp");
        stash.Tag(cat.Id, "yes");
        cat = await Listed(store, cat.Id);
        var render = await stash.Render(cat, BlackBoxAndBox);
        Outcome<StashItem> made;
        using (TestStash.Hold(render.WorkPath))
        {
            made = await store.SaveAsNewItem(cat, BlackBoxAndBox, render);
        }

        Assert.AreEqual(new Failure("Save as new item", "cat 2", HeldFileRetry.HeldReason), made.Failure);
        Assert.AreEqual(("cat 2", "cat 2.png"), (made.Value!.Name, made.Value.FileName), "a failure after the insert carries the new item as committed");
        Assert.IsNull((await store.Item(made.Value.Id)).Value, "no query lists it before its files are in place");
        Assert.AreEqual(cat.Id, (await stash.All()).Single().Id);
        Assert.AreEqual(cat.Id, (await store.Search("cat")).Value!.Single().Id);
        Assert.AreEqual(new TagCount("yes", 1), (await store.TagCounts()).Value!.Single(), "its tag is not counted");
        CollectionAssert.AreEqual(new[] { PendingStep.PlaceInOriginals, PendingStep.PlaceInRoot }, Row(stash, made.Value.Id).Steps.Select(step => step.Step).ToList());
        CollectionAssert.AreEquivalent(new[] { (cat.Id, "yes"), (made.Value.Id, "yes") }, stash.TagRows(), "the tags came with the insert, before any file moved (G28)");
        Assert.AreEqual((render.Facts, "cat 2"), (Row(stash, made.Value.Id).Finished, made.Value.Name), "and so did the render's facts");
        Assert.AreEqual(Hash(MediaFixtures.Bytes("still.webp")), Hash(stash.Folder.InOriginals("cat 2.webp")), "the copy was placed first");
        Assert.IsTrue(File.Exists(render.WorkPath));
        TestStash.AssertSameItem(cat, await Listed(store, cat.Id));

        var opening = await stash.Reopen();

        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        Assert.AreEqual(cat.Id, (await stash.All()).Single().Id);
        CollectionAssert.AreEqual(new[] { (cat.Id, "yes") }, stash.TagRows());
        Assert.AreEqual(1L, stash.SqlQueryWhileOpen("SELECT COUNT(*) FROM items", r => r.GetInt64(0)).Single());
        CollectionAssert.AreEqual(new[] { "cat.webp" }, stash.RootFiles());
        Assert.IsEmpty(OriginalFiles(stash), "the published copy is deleted with the row");
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(stash.RecycleBin.Asked, "no item ever existed, so nothing is recycled");
        Assert.IsEmpty(stash.RenderCalls);
    }

    [TestMethod]
    [DataRow(false, false, DisplayName = "the insert committed, both files in .work")]
    [DataRow(true, false, DisplayName = "the copy placed, the render in .work")]
    public async Task AKillBeforeEveryFileIsPlacedLeavesARowTheStartRemovesWithEveryFile(bool copyPlaced, bool renderPlaced)
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var (cat, copy) = await Killed(stash, copyPlaced, renderPlaced);

        var opening = await stash.Open();

        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        Assert.IsNull((await opening.Store!.Item(copy.Id)).Value);
        TestStash.AssertSameItem(cat, (await stash.All()).Single());
        Assert.AreEqual(Hash(MediaFixtures.Bytes("still.webp")), Hash(stash.Folder.InRoot("cat.webp")), "the item it came from is untouched");
        CollectionAssert.AreEqual(new[] { "cat.webp" }, stash.RootFiles());
        Assert.IsEmpty(OriginalFiles(stash));
        Assert.IsEmpty(stash.WorkFiles());
        CollectionAssert.AreEqual(new[] { (cat.Id, "yes") }, stash.TagRows(), "the copy's tags go with its row");
        Assert.IsEmpty(stash.RenderCalls, "nothing is rendered for it at start");
    }

    [TestMethod]
    public async Task AKillAfterBothFilesArePlacedLeavesTheStartOnlyTheClearingAndTheItemWhole()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var (cat, copy) = await Killed(stash, copyPlaced: true, renderPlaced: true);

        var opening = await stash.Open();

        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        CollectionAssert.AreEqual(new[] { copy.Id, cat.Id }, (await stash.All()).Select(item => item.Id).ToList());
        TestStash.AssertSameItem(copy, await Listed(opening.Store!, copy.Id));
        CollectionAssert.AreEqual(new[] { "yes" }, copy.Tags.ToList());
        Assert.IsEmpty(Row(stash, copy.Id).Steps);
        CollectionAssert.AreEquivalent(new[] { "cat.webp", "cat 2.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "cat 2.webp" }, OriginalFiles(stash));
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(stash.RenderCalls);
    }

    [TestMethod]
    public async Task AKillBeforeALayerlessCopyIsPlacedLeavesARowTheStartRemovesAsAnImports()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.webp");
        var copy = (await store.SaveAsNewItem(cat, LayerList.Empty, null)).Value!;
        stash.Close();
        var work = Guid.NewGuid().ToString("N") + ".webp";
        File.Move(copy.FilePath, stash.Folder.InWork(work));
        stash.Sql("UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", copy.Id), ("$steps", PendingStep.Encode([PendingStep.Place(work, copy.FileName)])));

        var opening = await stash.Open();

        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        TestStash.AssertSameItem(cat, (await stash.All()).Single());
        Assert.AreEqual(1L, stash.SqlQueryWhileOpen("SELECT COUNT(*) FROM items", r => r.GetInt64(0)).Single());
        CollectionAssert.AreEqual(new[] { "cat.webp" }, stash.RootFiles());
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task AWorkFileThatCannotBeDeletedAtStartLeavesTheRowUnlistedNamedAndBothItsFilesForTheNextStart()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var (cat, copy) = await Killed(stash, copyPlaced: false, renderPlaced: false);
        var steps = stash.SqlQuery("SELECT pending_steps FROM items WHERE id = $id", r => PendingStep.Decode(r.GetString(0)), ("$id", copy.Id)).Single();
        StoreOpening opening;
        using (TestStash.Hold(stash.Folder.InWork(steps[0].From)))
        {
            opening = await stash.Open();
        }

        Assert.AreEqual(new Failure("Undo an unfinished save as new item", "cat 2", HeldFileRetry.HeldReason), opening.Failures.Single());
        Assert.IsNull((await opening.Store!.Item(copy.Id)).Value, "unlisted");
        TestStash.AssertSameItem(cat, (await stash.All()).Single());
        CollectionAssert.AreEquivalent(steps.Select(step => step.From).ToList(), stash.WorkFiles(), "the clearing keeps both files its steps name");

        opening = await stash.Reopen();

        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        Assert.AreEqual(1L, stash.SqlQueryWhileOpen("SELECT COUNT(*) FROM items", r => r.GetInt64(0)).Single());
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(OriginalFiles(stash));
        CollectionAssert.AreEqual(new[] { "cat.webp" }, stash.RootFiles());
    }

    [TestMethod]
    public async Task AnImportOfTheSameBytesAfterASaveAsNewItemIsAlreadyInTheStashAsTheItemItCameFrom()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var copy = (await store.SaveAsNewItem(cat, BlackBoxAndBox, await stash.Render(cat, BlackBoxAndBox))).Value!;
        Assert.AreEqual(OriginalHash(stash, cat.Id), OriginalHash(stash, copy.Id), "the copy carries the original's hash (G12)");
        var staged = await stash.Stage("again.png", MediaFixtures.Bytes("still.png"));
        var read = MediaDecoder.ReadFacts(staged, "again.png", TestStash.ShortRetry).Value!;

        var again = await store.CommitImport(new ImportCandidate(staged, "again", read.Facts, read.Sha256));

        Assert.IsTrue(again.Succeeded && again.Value!.AlreadyInStash, again.Failure?.ToString());
        Assert.AreEqual(cat.Id, again.Value.Item.Id, "the lower identity is met first");
    }

    [TestMethod]
    public async Task ACopyWhoseClearingWasRefusedIsListedAndADeleteClearsItsStepsBeforeItHoldsItsFiles()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.webp");
        var copy = (await store.SaveAsNewItem(cat, BlackBoxAndBox, await stash.Render(cat, BlackBoxAndBox))).Value!;
        // As a refused clearing leaves it: both files in place, both steps on the row.
        stash.SqlWhileOpen("UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", copy.Id), ("$steps", PendingStep.Encode(
            [new PendingStep(PendingStep.PlaceInOriginals, Guid.NewGuid().ToString("N") + ".webp", "cat 2.webp"), PendingStep.Place(Guid.NewGuid().ToString("N") + ".png", "cat 2.png")])));
        TestStash.AssertSameItem(copy, await Listed(store, copy.Id));

        var deleted = (await store.Delete([copy.Id])).Single();

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { copy.FilePath, stash.Folder.InOriginals("cat 2.webp") }, stash.RecycleBin.Asked);
        Assert.IsEmpty(Row(stash, copy.Id).Steps, "cleared before the mark, which keeps no step but a done import's");
        Assert.AreEqual(cat.Id, (await stash.All()).Single().Id);
    }

    // A Save as new item of a WebP "cat" tagged "yes" with a black box and a box, made whole, then put back on the closed
    // store as a kill leaves it: each file not placed back in .work\, both steps on the row.
    private static async Task<(StashItem Cat, StashItem Copy)> Killed(TestStash stash, bool copyPlaced, bool renderPlaced)
    {
        var imported = await stash.ImportFixture("cat", "still.webp");
        stash.Tag(imported.Id, "yes");
        var cat = await Listed(stash.Store!, imported.Id);
        var made = await stash.Store!.SaveAsNewItem(cat, BlackBoxAndBox, await stash.Render(cat, BlackBoxAndBox));
        Assert.IsTrue(made.Succeeded, made.Failure?.ToString());
        var copy = made.Value!;
        stash.Close();
        var (copyWork, renderWork) = (Guid.NewGuid().ToString("N") + ".webp", Guid.NewGuid().ToString("N") + ".png");
        if (!copyPlaced)
        {
            File.Move(stash.Folder.InOriginals("cat 2.webp"), stash.Folder.InWork(copyWork));
        }

        if (!renderPlaced)
        {
            File.Move(stash.Folder.InRoot("cat 2.png"), stash.Folder.InWork(renderWork));
        }

        stash.Sql("UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", copy.Id), ("$steps", PendingStep.Encode(
            [new PendingStep(PendingStep.PlaceInOriginals, copyWork, "cat 2.webp"), PendingStep.Place(renderWork, "cat 2.png")])));
        return (cat, copy);
    }

    private static string OriginalHash(TestStash stash, long id) =>
        stash.SqlQueryWhileOpen("SELECT hex(original_sha256) FROM items WHERE id = $id", r => r.GetString(0), ("$id", id)).Single();

    // Every column of the item's row, as text, read between the store's calls.
    private static string AllColumns(TestStash stash, long id) =>
        stash.SqlQueryWhileOpen("SELECT * FROM items WHERE id = $id", r => string.Join("|", Enumerable.Range(0, r.FieldCount)
            .Select(i => $"{r.GetName(i)}={(r.IsDBNull(i) ? "NULL" : r.GetValue(i) is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(r.GetValue(i)))}")), ("$id", id)).Single();
}
