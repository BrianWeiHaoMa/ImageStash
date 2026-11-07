using System.Diagnostics;
using System.Security.Cryptography;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// Rename on the store (G17, P1, State: the Rename bullet, File names, A live failure): the name and the files' names
/// committed with a step per file, the files renamed, the step cleared; " 2" while a name is taken, ignoring case, a
/// deleted item's name kept taken; nothing required of a name; a refusal named (G35); a row whose rename is unfinished
/// listed with its file where it is, other changes going on over it, a second rename finishing the first or refused;
/// while it waits on a held file, the summon's listing answered and every change and confirmation asked after it waiting
/// for it (G1, G8).
/// </summary>
[TestClass]
public sealed class ItemRenameTests
{
    [TestMethod]
    public async Task ARenameCommitsTheNameAndRenamesTheFileInItsPlace()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(1));
        stash.Clock.Now = stash.Clock.Now.AddSeconds(1);
        var later = await stash.Import("later");

        var renamed = await store.Rename(cat, "dog");

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        TestStash.AssertSameItem(cat with { Name = "dog", FileName = "dog.png", FilePath = stash.Folder.InRoot("dog.png") }, renamed.Value);
        CollectionAssert.AreEquivalent(new[] { "dog.png", "later.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(TestStash.Bytes(1), File.ReadAllBytes(stash.Folder.InRoot("dog.png")));
        Assert.IsEmpty(stash.WorkFiles());
        CollectionAssert.AreEqual(new[] { later.Id, cat.Id }, (await stash.All()).Select(i => i.Id).ToList(), "the added time still orders it");
        Assert.AreEqual(cat.Id, (await store.Search("dog")).Value!.Single().Id);
        Assert.IsEmpty((await store.Search("cat")).Value!);
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT pending_steps IS NULL AND original_file = 'dog.png' FROM items WHERE id = $id", r => r.GetBoolean(0), ("$id", cat.Id)).Single());
    }

    [TestMethod]
    public async Task TheRenamedItemCarriesTheStoresCommitsAfterItsLastSoAHeldStoreHandsItOverAndNotTheItemAsItWasBefore()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat");

        var dog = (await store.Rename(cat, "dog")).Value!;

        Assert.AreEqual((await store.Item(cat.Id)).Value!.CommitsWhenRead, dog.CommitsWhenRead);
        using (stash.HoldStore())
        {
            Assert.AreSame(dog, (await store.Confirm([dog], "Copy")).Items.Single().Value, "its file is under the name it carries");
            Assert.AreEqual(
                $"Copy failed: cat, the stash could not be read ({StashFolder.StoreFileName}: {HeldFileRetry.HeldReason})",
                (await store.Confirm([cat], "Copy")).Items.Single().Failure?.Sentence,
                "as read before the rename, its old file name could be another item's by now");
        }
    }

    [TestMethod]
    public async Task TheSameNameAgainWritesNothing()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat");
        var written = File.GetLastWriteTimeUtc(cat.FilePath);
        var storeBefore = StoreBytes(stash);

        var renamed = await store.Rename(cat, "cat");

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        TestStash.AssertSameItem(cat, renamed.Value);
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(cat.FilePath));
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(storeBefore, StoreBytes(stash), "no transaction wrote the store");
    }

    [TestMethod]
    public async Task AChangeOfCaseRenamesTheFileAndAnUnchangedFileNameTakesNoStep()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("Cat");
        var question = await stash.Import("Cat?");
        var written = File.GetLastWriteTimeUtc(question.FilePath);

        var lower = await store.Rename(cat, "cat");
        var underscore = await store.Rename(question, "Cat_");

        Assert.IsTrue(lower.Succeeded, lower.Failure?.ToString());
        Assert.AreEqual(("cat", "cat.png"), (lower.Value!.Name, lower.Value.FileName));
        Assert.AreEqual("Cat_.png", question.FileName);
        Assert.IsTrue(underscore.Succeeded, underscore.Failure?.ToString());
        Assert.AreEqual(("Cat_", "Cat_.png"), (underscore.Value!.Name, underscore.Value.FileName));
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(question.FilePath));
        CollectionAssert.AreEquivalent(new[] { "cat.png", "Cat_.png" }, stash.RootFiles(), "listed as Windows now spells them");
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT pending_steps IS NULL FROM items", r => r.GetBoolean(0)).All(cleared => cleared));
    }

    [TestMethod]
    public async Task ATakenNameGetsACounterIgnoringCaseAndADeletedItemKeepsItsNameTakenUntilItEnds()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        var c = await stash.Import("c");
        var d = await stash.Import("d");

        Assert.AreEqual("a 2.png", (await store.Rename(b, "a")).Value!.FileName);
        Assert.AreEqual("A 2.png", (await store.Rename(b, "A")).Value!.FileName, "its own file is not taken from it");
        Assert.IsTrue((await store.Delete([a.Id])).Single().Succeeded);
        Assert.AreEqual("a 3.png", (await store.Rename(c, "a")).Value!.FileName, "a deleted item's name stays taken inside its notice");
        Assert.IsTrue((await store.FinishDelete([a.Id])).Single().Succeeded);
        Assert.AreEqual("a.png", (await store.Rename(d, "a")).Value!.FileName);
        CollectionAssert.AreEquivalent(new[] { "a.png", "A 2.png", "a 3.png" }, stash.RootFiles());
    }

    [TestMethod]
    public async Task NothingIsRequiredOfANameAndANameMadeSafeKeepsItsLimits()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var first = await stash.Import("first");
        var second = await stash.Import("second");
        var device = await stash.Import("device");
        var longName = await stash.Import("long");

        var empty = await store.Rename(first, "");
        var blank = await store.Rename(second, "   ");
        var con = await store.Rename(device, "con");
        var long300 = await store.Rename(longName, new string('x', 300));

        Assert.AreEqual(("", "Untitled.png"), (empty.Value!.Name, empty.Value.FileName));
        Assert.AreEqual(("   ", "Untitled 2.png"), (blank.Value!.Name, blank.Value.FileName));
        Assert.AreEqual(("con", "con_.png"), (con.Value!.Name, con.Value.FileName));
        Assert.AreEqual(new string('x', 300), long300.Value!.Name);
        Assert.AreEqual(new string('x', 200) + ".png", long300.Value.FileName);
        CollectionAssert.AreEquivalent(new[] { "Untitled.png", "Untitled 2.png", "con_.png", new string('x', 200) + ".png" }, stash.RootFiles());
    }

    [TestMethod]
    public async Task AnEditedItemsOriginalUnderOriginalsTakesTheNameToo()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(2));
        stash.Close();
        stash.MakeEdited(cat, TestStash.Bytes(3));
        var store = await stash.OpenCleanly();

        var renamed = await store.Rename(cat, "dog");

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        Assert.AreEqual(stash.Folder.InRoot("dog.png"), renamed.Value!.FilePath);
        CollectionAssert.AreEqual(new[] { "dog.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(new[] { "dog.png" }, Directory.GetFiles(stash.Folder.Originals).Select(Path.GetFileName).ToArray());
        CollectionAssert.AreEqual(TestStash.Bytes(3), File.ReadAllBytes(stash.Folder.InRoot("dog.png")));
        CollectionAssert.AreEqual(TestStash.Bytes(2), File.ReadAllBytes(stash.Folder.InOriginals("dog.png")));
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT pending_steps IS NULL AND original_file = 'dog.png' AND original_in_originals = 1 FROM items", r => r.GetBoolean(0)).Single());
    }

    [TestMethod]
    public async Task AnEditedWebPKeepsEachFilesOwnExtensionThroughARenameAndItsRevert()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var clip = await stash.ImportFixture("clip", "still.webp");
        var saved = await stash.SaveEdit(clip, EditedItems.Arrow);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());

        var renamed = await store.Rename(saved.Value!, "dog");

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        Assert.AreEqual(stash.Folder.InRoot("dog.png"), renamed.Value!.FilePath, "the finished picture a PNG under the new name (F3, P1)");
        CollectionAssert.AreEqual(new[] { "dog.png" }, stash.RootFiles());
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.webp"), File.ReadAllBytes(stash.Folder.InOriginals("dog.webp")), "the original keeps its own extension (State: File names)");
        var reverted = await store.Save(renamed.Value, LayerList.Empty, null);
        Assert.IsTrue(reverted.Succeeded, reverted.Failure?.ToString());
        Assert.AreEqual(stash.Folder.InRoot("dog.webp"), reverted.Value!.FilePath, "one file again, the original under the new name (F7)");
        CollectionAssert.AreEqual(new[] { "dog.webp" }, stash.RootFiles());
    }

    [TestMethod]
    public async Task AnEditedItemWhoseOriginalsNameIsTakenTakesACounterForBothFiles()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("cat");
        stash.Close();
        stash.MakeEdited(cat, TestStash.Bytes(4));
        // An original under originals\ already named dog, as no rule of this build leaves one, with no root file of that name.
        File.WriteAllBytes(stash.Folder.InOriginals("dog.png"), TestStash.Bytes(5));
        var store = await stash.OpenCleanly();

        var renamed = await store.Rename(cat, "dog");

        Assert.AreEqual("dog 2.png", renamed.Value!.FileName);
        Assert.IsTrue(File.Exists(stash.Folder.InOriginals("dog 2.png")));
        CollectionAssert.AreEqual(TestStash.Bytes(5), File.ReadAllBytes(stash.Folder.InOriginals("dog.png")));
    }

    [TestMethod]
    public async Task AnItemNoLongerInTheStashIsRefusedByNameWithNothingWritten()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var (gone, marked, listed) = await stash.GoneMarkedAndListed();
        var waiting = await stash.Import("waiting");
        // An import whose file never reached the root, as the next start finds it: listed by no query.
        stash.SqlWhileOpen("UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", waiting.Id), ("$steps", PendingStep.Encode([PendingStep.Place("staged.png", "elsewhere.png")])));

        var outcomes = new[] { await store.Rename(gone, "x"), await store.Rename(marked, "x"), await store.Rename(waiting, "x") };

        CollectionAssert.AreEqual(
            new[] { new Failure("Rename", "gone", ItemFactChange.NotInStash), new Failure("Rename", "marked", ItemFactChange.NotInStash), new Failure("Rename", "waiting", ItemFactChange.NotInStash) },
            outcomes.Select(o => o.Failure).ToList());
        Assert.IsTrue((await store.Rename(listed, "x")).Succeeded);
        stash.Close();
        CollectionAssert.AreEqual(
            new[] { "marked", "waiting" },
            stash.SqlQuery("SELECT name FROM items WHERE id IN ($m, $w) ORDER BY id", r => r.GetString(0), ("$m", marked.Id), ("$w", waiting.Id)));
    }

    [TestMethod]
    public async Task ARenameRefusedLiveKeepsItsStepListsTheFileWhereItIsAndLetsOtherChangesThrough()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(6));

        Outcome<StashItem> renamed;
        using (TestStash.Hold(cat.FilePath))
        {
            renamed = await store.Rename(cat, "dog");
        }

        Assert.AreEqual(new Failure("Rename", "cat.png", HeldFileRetry.HeldReason), renamed.Failure);
        Assert.AreEqual(("dog", "dog.png", cat.FilePath), (renamed.Value!.Name, renamed.Value.FileName, renamed.Value.FilePath), "the item as committed, its file where it is");
        var listed = (await stash.All()).Single();
        Assert.AreEqual(("dog", cat.FilePath), (listed.Name, listed.FilePath));
        Assert.AreEqual(cat.FilePath, (await store.Item(cat.Id)).Value!.FilePath);
        Assert.IsTrue((await store.AddTag([listed], "mood")).Single().Succeeded);
        Assert.IsTrue((await store.SetFavorite([listed], true)).Single().Succeeded);
        Assert.IsTrue((await store.RecordUses([cat], stash.Clock.Now)).Succeeded);
        Assert.AreEqual(new UseHistory(1, stash.Clock.Now), (await store.UseHistory(cat.Id)).Value);
        var after = (await stash.All()).Single();
        Assert.AreEqual(("dog", true, "mood"), (after.Name, after.Favorite, after.Tags.Single()));
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT pending_steps IS NOT NULL FROM items", r => r.GetBoolean(0)).Single(), "the step waits for the next start");
    }

    [TestMethod]
    public async Task ASecondRenameIsRefusedWhileTheFirstCannotFinishAndFinishesItOnceItCan()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat");
        Outcome<StashItem> second;
        using (TestStash.Hold(cat.FilePath))
        {
            await store.Rename(cat, "dog");
            second = await store.Rename(cat, "emu");
        }

        Assert.AreEqual(new Failure("Rename", "dog", $"its earlier rename cannot finish yet: {HeldFileRetry.HeldReason}"), second.Failure);
        Assert.IsNull(second.Value);
        Assert.AreEqual("dog", (await stash.All()).Single().Name, "nothing written");

        var third = await store.Rename(cat, "emu");

        Assert.IsTrue(third.Succeeded, third.Failure?.ToString());
        Assert.AreEqual(("emu", "emu.png", stash.Folder.InRoot("emu.png")), (third.Value!.Name, third.Value.FileName, third.Value.FilePath));
        CollectionAssert.AreEqual(new[] { "emu.png" }, stash.RootFiles());
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT pending_steps IS NULL FROM items", r => r.GetBoolean(0)).Single());
    }

    [TestMethod]
    public async Task TheSameNameWhileItsRenameIsUnfinishedIsRefusedUntilTheRenameCanFinish()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat");
        Outcome<StashItem> again;
        using (TestStash.Hold(cat.FilePath))
        {
            var first = await store.Rename(cat, "dog");
            again = await store.Rename(first.Value!, "dog");
        }

        Assert.AreEqual(new Failure("Rename", "dog", $"its earlier rename cannot finish yet: {HeldFileRetry.HeldReason}"), again.Failure, "no success before the file carries the name");

        var finished = await store.Rename(cat, "dog");

        Assert.IsTrue(finished.Succeeded, finished.Failure?.ToString());
        Assert.AreEqual(("dog", stash.Folder.InRoot("dog.png")), (finished.Value!.Name, finished.Value.FilePath));
        CollectionAssert.AreEqual(new[] { "dog.png" }, stash.RootFiles());
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT pending_steps IS NULL FROM items", r => r.GetBoolean(0)).Single());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task TheSummonsListingIsAnsweredWhileARenameWaitsOnItsHeldFileAndTheRenameThenCompletes()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(21));
        Assert.IsTrue((await store.RecordUses([cat], stash.Clock.Now)).Succeeded);

        var (recent, renamed) = await ReadsWhileAChangeWaits.Asked(TestStash.Hold(cat.FilePath), () => store.Rename(cat, "dog"), store.Recent);

        var listed = recent.Value!.Single();
        Assert.AreEqual((cat.Id, "dog", cat.FilePath), (listed.Id, listed.Name, listed.FilePath), "the name committed, the file where it is (State: A live failure)");
        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        var now = (await store.Item(cat.Id)).Value!;
        Assert.AreEqual(("dog", stash.Folder.InRoot("dog.png")), (now.Name, now.FilePath));
        CollectionAssert.AreEqual(new[] { "dog.png" }, stash.RootFiles());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task TheListingIsAnsweredWhileADeleteWaitsToFinishAPendingRenameAndTheRenamedFileIsThenRecycled()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(23));
        Assert.IsTrue((await store.RecordUses([cat], stash.Clock.Now)).Succeeded);
        using (TestStash.Hold(cat.FilePath))
        {
            Assert.AreEqual(new Failure("Rename", "cat.png", HeldFileRetry.HeldReason), (await store.Rename(cat, "dog")).Failure, "refused live, its step kept");
        }

        var (recent, deleted) = await ReadsWhileAChangeWaits.Asked(TestStash.Hold(cat.FilePath), () => store.Delete([cat.Id]), store.Recent);

        var listed = recent.Value!.Single();
        Assert.AreEqual((cat.Id, "dog", cat.FilePath), (listed.Id, listed.Name, listed.FilePath), "under the file where it is (State: A live failure)");
        Assert.IsTrue(deleted.Single().Succeeded, deleted.Single().Failure?.ToString());
        CollectionAssert.AreEqual(new[] { stash.Folder.InRoot("dog.png") }, stash.RecycleBin.Asked, "the rename finished first, the file recycled under its new name");
        Assert.IsEmpty(stash.RootFiles());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task EveryListingAskedWhileARenameRetriesPastItsDurationIsAnsweredAndTheRenameIsThenNamedAsHeld()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(1) };
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(22));
        Assert.IsTrue((await store.RecordUses([cat], stash.Clock.Now)).Succeeded);

        var answered = new List<(long Asked, long Took, bool RenameEnded)>();
        Outcome<StashItem> renamed;
        using (TestStash.Hold(cat.FilePath))
        {
            var renaming = store.Rename(cat, "dog");
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < ReadsWhileAChangeWaits.HeldFor)
            {
                await Task.Delay(300);
                var asked = clock.ElapsedMilliseconds;
                var reading = store.Recent();
                Assert.AreSame(reading, await Task.WhenAny(reading, Task.Delay(ReadsWhileAChangeWaits.AnsweredWithin)), $"the listing asked at {asked} ms (G1)");
                Assert.IsTrue(reading.Result.Succeeded, reading.Result.Failure?.ToString());
                answered.Add((asked, clock.ElapsedMilliseconds - asked, renaming.IsCompleted));
            }

            renamed = await renaming;
        }

        Assert.IsFalse(answered[0].RenameEnded, "the first listings were answered during the retry");
        Assert.IsGreaterThanOrEqualTo(5, answered.Count);
        Assert.AreEqual(new Failure("Rename", "cat.png", HeldFileRetry.HeldReason), renamed.Failure, "named once the retry is spent, as before (G35)");
        Assert.AreEqual(("dog", cat.FilePath), (renamed.Value!.Name, renamed.Value.FilePath));
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT pending_steps IS NOT NULL FROM items", r => r.GetBoolean(0)).Single(), "the step waits for the next start");
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AChangeAskedWhileARenameWaitsRunsOnlyOnceTheRenameEndsWhileTheListingIsAnsweredMeanwhile()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(23));
        var other = await stash.Import("other", TestStash.Bytes(24));
        Assert.IsTrue((await store.RecordUses([other], stash.Clock.Now)).Succeeded);

        Task<IReadOnlyList<Outcome>> tagging;
        bool listedInTime, taggedWhileHeld;
        Task<Outcome<StashItem>> renaming;
        using (TestStash.Hold(cat.FilePath))
        {
            renaming = store.Rename(cat, "dog");
            await Task.Delay(ReadsWhileAChangeWaits.AskedAfter);
            tagging = store.AddTag([other], "mood");
            var listing = store.Recent();
            listedInTime = await Task.WhenAny(listing, Task.Delay(ReadsWhileAChangeWaits.AnsweredWithin)) == listing;
            await Task.Delay(ReadsWhileAChangeWaits.HeldFor - ReadsWhileAChangeWaits.AskedAfter);
            taggedWhileHeld = tagging.IsCompleted;
        }

        var renamed = await renaming;
        var tagged = await tagging;

        Assert.IsTrue(listedInTime, "the read asked after the queued change is answered during the wait (G1)");
        Assert.IsFalse(taggedWhileHeld, "a change never runs inside another (State: Every change)");
        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        Assert.IsTrue(tagged.Single().Succeeded, TestStash.Failures(tagged));
        Assert.AreEqual("mood", (await store.Item(other.Id)).Value!.Tags.Single());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AHandoffsConfirmationAskedWhileARenameWaitsIsAnsweredOnceTheRenameEndsWithTheFileWhereItIsNow()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(25));
        Assert.IsTrue((await store.RecordUses([cat], stash.Clock.Now)).Succeeded);

        Task<Confirmation> confirming;
        bool listedInTime, confirmedWhileHeld;
        using (TestStash.Hold(cat.FilePath))
        {
            var renaming = store.Rename(cat, "dog");
            await Task.Delay(ReadsWhileAChangeWaits.AskedAfter);
            confirming = store.Confirm([cat], "Copy");
            var listing = store.Recent();
            listedInTime = await Task.WhenAny(listing, Task.Delay(ReadsWhileAChangeWaits.AnsweredWithin)) == listing;
            await Task.Delay(ReadsWhileAChangeWaits.HeldFor - ReadsWhileAChangeWaits.AskedAfter);
            confirmedWhileHeld = confirming.IsCompleted;
        }

        using var confirmed = await confirming;

        Assert.IsTrue(listedInTime, "the summon's listing is still answered during the wait (G1)");
        // The files it answers leave the store on the clipboard, and a paste's keystroke follows the rename: the path the
        // rename is moving would be gone by then (G8).
        Assert.IsFalse(confirmedWhileHeld, "a confirmation waits for the change asked before it");
        var handed = confirmed.Items.Single();
        Assert.IsTrue(handed.Succeeded, handed.Failure?.ToString());
        Assert.AreEqual(stash.Folder.InRoot("dog.png"), handed.Value!.FilePath);
        Assert.IsTrue(File.Exists(handed.Value.FilePath));
    }

    // Read between the store's calls, which hold no transaction, with sharing the store's own connection allows.
    private static byte[] StoreBytes(TestStash stash)
    {
        using var stream = new FileStream(stash.Folder.StoreFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(stream);
    }
}
