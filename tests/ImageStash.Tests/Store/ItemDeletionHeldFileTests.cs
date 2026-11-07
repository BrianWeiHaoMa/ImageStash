using System.Diagnostics;
using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// Delete, its Undo and its finish while a file they need is briefly held (E3), with the product's held-file duration:
/// the summon's listing answered between their attempts (G1); the Recycle Bin asked again for a held file, and only for
/// it, until the duration is spent before a failure is named (G35); then each change completing, or named, as State's
/// Delete bullet says (G18).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ItemDeletionHeldFileTests
{
    [TestMethod]
    public async Task TheListingIsAnsweredWhileADeletesCopyWaitsOnItsUnsharedPictureAndTheItemIsThenDeleted()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var cat = await UsedItem(stash, "cat", 50);

        var (recent, deleted) = await ReadsWhileAChangeWaits.Asked(Unshared(cat.FilePath), () => store.Delete([cat.Id]), store.Recent);

        Assert.AreEqual(cat.Id, recent.Value!.Single().Id, "nothing is marked before its copy is made");
        var item = deleted.Single();
        Assert.IsTrue(item.Succeeded, item.Failure?.ToString());
        Assert.IsEmpty(await stash.All());
        CollectionAssert.AreEqual(new[] { cat.FilePath }, stash.RecycleBin.Asked);
        Assert.IsTrue(File.Exists(item.Value!.RecycledIdentities.Single()));
        CollectionAssert.AreEqual(TestStash.Bytes(50), File.ReadAllBytes(stash.HeldCopy(cat.Id)), "held for Undo");
    }

    [TestMethod]
    public async Task TheListingIsAnsweredWhileADeletesRecycleWaitsOnItsHeldPictureAndTheFileIsThenRecycled()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var cat = await UsedItem(stash, "cat", 51);

        var (recent, deleted) = await ReadsWhileAChangeWaits.Asked(TestStash.Hold(cat.FilePath), () => store.Delete([cat.Id]), store.Recent);

        Assert.IsEmpty(recent.Value!, "marked before its recycle, so returned by no query");
        var item = deleted.Single();
        Assert.IsTrue(item.Succeeded, item.Failure?.ToString());
        Assert.IsTrue(File.Exists(item.Value!.RecycledIdentities.Single()));
        Assert.IsGreaterThan(1, stash.RecycleBin.Asked.Count, "asked again while held");
        Assert.IsTrue(stash.RecycleBin.Asked.All(path => path == cat.FilePath));
        Assert.IsEmpty(stash.RootFiles());
    }

    [TestMethod]
    public async Task ARecycleStillHeldAfterTheDurationFailsTheDeleteAfterItsMarkAndUndoBringsTheItemBack()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var cat = await UsedItem(stash, "cat", 52);

        Outcome<DeletedItem> deleted;
        var clock = Stopwatch.StartNew();
        using (TestStash.Hold(cat.FilePath))
        {
            deleted = (await store.Delete([cat.Id])).Single();
        }

        Assert.AreEqual(new Failure("Move to the Recycle Bin", "cat.png", HeldFileRetry.HeldReason), deleted.Failure);
        Assert.IsGreaterThanOrEqualTo(HeldFileRetry.Duration, clock.Elapsed, "retried for the held-file duration before it is named (G35)");
        Assert.IsEmpty(deleted.Value!.RecycledIdentities);
        Assert.IsEmpty(await stash.All(), "the mark stands");

        var restored = (await store.UndoDelete([deleted.Value])).Single();

        Assert.IsTrue(restored.Succeeded && restored.Value!.Named.Count == 0, restored.Failure?.ToString());
        Assert.AreEqual(cat.Id, (await stash.All()).Single().Id);
        CollectionAssert.AreEqual(TestStash.Bytes(52), File.ReadAllBytes(cat.FilePath));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task TheListingIsAnsweredWhileAnUndoWaitsOnItsHeldCopyAndTheItemIsThenBack()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var cat = await UsedItem(stash, "cat", 53);
        var deleted = (await store.Delete([cat.Id])).Single().Value!;

        var (recent, restored) = await ReadsWhileAChangeWaits.Asked(TestStash.Hold(stash.HeldCopy(cat.Id)), () => store.UndoDelete([deleted]), store.Recent);

        Assert.IsEmpty(recent.Value!, "still marked while its file comes back");
        Assert.IsTrue(restored.Single().Succeeded && restored.Single().Value!.Named.Count == 0, TestStash.Failures(restored));
        Assert.AreEqual(cat.Id, (await store.Recent()).Value!.Single().Id);
        CollectionAssert.AreEqual(TestStash.Bytes(53), File.ReadAllBytes(cat.FilePath));
        Assert.IsFalse(File.Exists(deleted.RecycledIdentities.Single()), "the bin's entry removed");
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    [DataRow(false, DisplayName = "an unedited item's one entry held")]
    [DataRow(true, DisplayName = "an edited item's finished picture's entry held, its original's free")]
    public async Task TheListingIsAnsweredWhileAnUndoWaitsOnAHeldBinEntryAndOnlyThatEntryIsAskedAgain(bool edited)
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat", TestStash.Bytes(54));
        if (edited)
        {
            stash.Close();
            stash.MakeEdited(cat, TestStash.Bytes(55));
            store = await stash.OpenCleanly();
        }

        Assert.IsTrue((await store.RecordUses([cat], stash.Clock.Now)).Succeeded);
        var deleted = (await store.Delete([cat.Id])).Single().Value!;
        var held = deleted.RecycledIdentities[0];

        var (recent, restored) = await ReadsWhileAChangeWaits.Asked(TestStash.Hold(held), () => store.UndoDelete([deleted]), store.Recent);

        Assert.IsEmpty(recent.Value!, "the mark is cleared only after the bin's entries go");
        Assert.IsTrue(restored.Single().Succeeded, restored.Single().Failure?.ToString());
        Assert.IsEmpty(restored.Single().Value!.Named, "nothing named: the held entry went once its hold did");
        Assert.IsFalse(deleted.RecycledIdentities.Any(File.Exists), "every entry removed");
        Assert.IsGreaterThan(1, stash.RecycleBin.Removed.Count(identity => identity == held), "the held entry asked again");
        foreach (var free in deleted.RecycledIdentities.Skip(1))
        {
            Assert.AreEqual(1, stash.RecycleBin.Removed.Count(identity => identity == free), "a free entry of the same removal asked once");
        }

        Assert.AreEqual(cat.Id, (await store.Recent()).Value!.Single().Id);
        Assert.HasCount(edited ? 2 : 1, deleted.RecycledIdentities);
    }

    [TestMethod]
    public async Task TheListingIsAnsweredWhileAFinishWaitsOnAHeldCopyAndTheRowIsThenRemoved()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var dog = await UsedItem(stash, "dog", 56);
        var cat = await UsedItem(stash, "cat", 57);
        Assert.IsTrue((await store.Delete([cat.Id])).Single().Succeeded);

        var (recent, finished) = await ReadsWhileAChangeWaits.Asked(TestStash.Hold(stash.HeldCopy(cat.Id)), () => store.FinishDelete([cat.Id]), store.Recent);

        Assert.AreEqual(dog.Id, recent.Value!.Single().Id);
        Assert.IsTrue(finished.Single().Succeeded, finished.Single().Failure?.ToString());
        Assert.IsEmpty(stash.WorkFiles(), "the held copy deleted once its hold went");
        stash.Close();
        CollectionAssert.AreEqual(new[] { dog.Id }, stash.SqlQuery("SELECT id FROM items", r => r.GetInt64(0)));
    }

    [TestMethod]
    public async Task AHeldFileOfADeletesBatchIsAskedAgainWhileTheOtherIsRecycledOnce()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var first = await stash.Import("first");
        var second = await stash.Import("second");

        bool secondRecycledWhileFirstHeld;
        Task<IReadOnlyList<Outcome<DeletedItem>>> deleting;
        using (TestStash.Hold(first.FilePath))
        {
            deleting = store.Delete([first.Id, second.Id]);
            await Task.Delay(ReadsWhileAChangeWaits.HeldFor / 2);
            secondRecycledWhileFirstHeld = !File.Exists(second.FilePath) && File.Exists(first.FilePath) && !deleting.IsCompleted;
            await Task.Delay(ReadsWhileAChangeWaits.HeldFor / 2);
        }

        var deleted = await deleting;

        Assert.IsTrue(secondRecycledWhileFirstHeld, "the free file goes while the held one waits");
        Assert.IsTrue(deleted.All(o => o.Succeeded), TestStash.Failures(deleted));
        Assert.AreEqual(1, stash.RecycleBin.Asked.Count(path => path == second.FilePath), "the free file is asked once");
        Assert.IsGreaterThan(1, stash.RecycleBin.Asked.Count(path => path == first.FilePath), "the held file asked again until its hold went");
        Assert.IsEmpty(stash.RootFiles());
        Assert.IsEmpty(await stash.All());
    }

    [TestMethod]
    public async Task AFileHeldEarlyInABatchWhoseFirstPassOutlastsTheDurationIsStillAskedAgain()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var first = await stash.Import("first");
        var second = await stash.Import("second");
        using var hold = TestStash.Hold(first.FilePath);
        stash.RecycleBin.BeforeEach = path =>
        {
            if (path == second.FilePath)
            {
                // The batch's first pass outlasts the held-file duration, and the first file is let go meanwhile.
                Thread.Sleep(stash.Retry + TimeSpan.FromMilliseconds(200));
                hold.Dispose();
            }
        };

        var deleted = await store.Delete([first.Id, second.Id]);

        Assert.IsTrue(deleted.All(o => o.Succeeded), TestStash.Failures(deleted));
        Assert.AreEqual(2, stash.RecycleBin.Asked.Count(path => path == first.FilePath), "owed the whole duration from its first refusal (G35)");
        Assert.IsEmpty(stash.RootFiles());
    }

    /// <summary>Imports an item of <paramref name="seed"/>'s bytes and records one use of it, so Recent lists it.</summary>
    private static async Task<StashItem> UsedItem(TestStash stash, string name, int seed)
    {
        var item = await stash.Import(name, TestStash.Bytes(seed));
        Assert.IsTrue((await stash.Store!.RecordUses([item], stash.Clock.Now)).Succeeded);
        return item;
    }

    /// <summary>Holds a file without sharing, as a scanner may (E3): it cannot be read, so a copy of it is refused.</summary>
    private static FileStream Unshared(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.None);
}
