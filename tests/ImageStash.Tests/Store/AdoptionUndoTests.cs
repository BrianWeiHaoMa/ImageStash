using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// An Open with… adoption's undo record (State: Open with…; G27, G29, G18): its expiry once the notification has gone,
/// the previous original sent to the Recycle Bin, never deleted, and the record cleared, once, for the adoption it names
/// alone; a refused recycle named with the record kept; an expiry asked while the adoption's steps wait finishing them
/// first; a second adoption expiring the first's record so one stands; a Delete's mark keeping the record for its Undo;
/// and a row removed for good expiring it before its session ends, the summon's listing answered while it waits on a
/// held file (G1).
/// </summary>
[TestClass]
public sealed class AdoptionUndoTests
{
    private static readonly byte[] Still = MediaFixtures.Bytes("still.png");

    [TestMethod]
    public async Task ExpiringRecyclesTheHeldPreviousOriginalAndClearsTheRecordOnce()
    {
        using var stash = new TestStash();
        var (cat, _) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        var held = stash.Folder.InWork(stash.WorkFiles().Single());

        var another = await stash.Store!.ExpireAdoption(cat.Id, adopted.RenderedRevision + 1);
        Assert.IsTrue(another.Succeeded);
        Assert.IsEmpty(stash.RecycleBin.Asked, "another revision's adoption: nothing to do");
        var expired = await stash.Store.ExpireAdoption(cat.Id, adopted.RenderedRevision);
        var again = await stash.Store.ExpireAdoption(cat.Id, adopted.RenderedRevision);

        Assert.IsTrue(expired.Succeeded && again.Succeeded, expired.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { held }, stash.RecycleBin.Asked, "to the Recycle Bin, never deleted (G29), and once");
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
        TestStash.AssertSameItem(adopted, await EditedItems.Listed(stash.Store, cat.Id));
    }

    [TestMethod]
    public async Task ExpiringAnEditedItemsAdoptionRecyclesItsPreviousOriginalFromOriginals()
    {
        using var stash = new TestStash();
        var (_, cat) = await EditedItems.SavedCat(stash);
        await OpenedWith.AwaitingRewrite(stash, cat, MediaFixtures.DistinctPng(5, 12, 10));
        var adopted = await OpenedWith.Adopt(stash, cat.Id);

        Assert.IsTrue((await stash.Store!.ExpireAdoption(cat.Id, adopted.RenderedRevision)).Succeeded);

        CollectionAssert.AreEqual(new[] { stash.Folder.InOriginals("cat.png") }, stash.RecycleBin.Asked);
        Assert.IsEmpty(EditedItems.OriginalFiles(stash));
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
    }

    [TestMethod]
    public async Task ARefusedRecycleIsNamedAndKeepsTheRecordForALaterExpiry()
    {
        using var stash = new TestStash();
        var (cat, _) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        var record = OpenedWith.Undo(stash, cat.Id);
        stash.RecycleBin.Refusing = _ => true;

        var refused = await stash.Store!.ExpireAdoption(cat.Id, adopted.RenderedRevision);

        Assert.AreEqual(new Failure(AdoptionUndo.ExpireAction, "cat", "the test Recycle Bin refuses it"), refused.Failure);
        Assert.AreEqual(record, OpenedWith.Undo(stash, cat.Id));
        Assert.IsTrue(File.Exists(stash.Folder.InWork(record!.HeldCopy!)));
        stash.RecycleBin.Refusing = _ => false;
        Assert.IsTrue((await stash.Store.ExpireAdoption(cat.Id, adopted.RenderedRevision)).Succeeded);
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
    }

    [TestMethod]
    public async Task APreviousOriginalWindowsRemovesForGoodAtTheExpiryClearsTheRecordAndFreesItsName()
    {
        // G29: a removal for good completes the expiry that asked for it, so the record no longer keeps the name.
        using var stash = new TestStash();
        var (_, cat) = await EditedItems.SavedCat(stash);
        await OpenedWith.AwaitingRewrite(stash, cat, MediaFixtures.DistinctPng(5, 12, 10));
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        stash.RecycleBin.RemovingForGood = _ => true;

        var expired = await stash.Store!.ExpireAdoption(cat.Id, adopted.RenderedRevision);

        Assert.IsTrue(expired.Succeeded, expired.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { stash.Folder.InOriginals("cat.png") }, stash.RecycleBin.Asked);
        Assert.IsEmpty(EditedItems.OriginalFiles(stash));
        Assert.IsEmpty(OpenedWith.Recycled(stash));
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
        Assert.IsEmpty(stash.RecycleBin.Removed);
    }

    [TestMethod]
    public async Task AnExpiryAskedWhileTheAdoptionsStepsWaitFinishesThemFirstOrIsRefused()
    {
        using var stash = new TestStash();
        var (cat, _) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var hold = TestStash.Hold(cat.FilePath);
        var adopting = await stash.Store!.AdoptRewrite(cat.Id);
        var revision = OpenedWith.Undo(stash, cat.Id)!.Revision;

        var whileHeld = await stash.Store.ExpireAdoption(cat.Id, revision);
        var stepsWhileHeld = EditedItems.Row(stash, cat.Id).Steps.Count;
        hold.Dispose();
        var released = await stash.Store.ExpireAdoption(cat.Id, revision);

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", HeldFileRetry.HeldReason), adopting.Failure);
        Assert.AreEqual(new Failure(AdoptionUndo.ExpireAction, "cat", "its earlier adoption cannot finish yet: " + HeldFileRetry.HeldReason), whileHeld.Failure);
        Assert.AreEqual(2, stepsWhileHeld, "the record is never cleared while the steps wait");
        Assert.IsTrue(released.Succeeded, released.Failure?.ToString());
        Assert.IsEmpty(EditedItems.Row(stash, cat.Id).Steps, "the adoption finished first");
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(Directory.GetFiles(stash.RecycleBin.Folder).Single()), "the held previous original");
    }

    [TestMethod]
    public async Task ASecondAdoptionExpiresTheFirstsRecordSoOneStands()
    {
        using var stash = new TestStash();
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var first = File.ReadAllBytes(copy);
        await OpenedWith.Adopt(stash, cat.Id);
        var firstHeld = stash.Folder.InWork(stash.WorkFiles().Single());
        OpenedWith.Rewrite(copy, MediaFixtures.DistinctPng(6, 9, 7));
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id));

        var second = await OpenedWith.Adopt(stash, cat.Id);

        CollectionAssert.AreEqual(new[] { firstHeld }, stash.RecycleBin.Asked, "the first previous original to the Recycle Bin");
        var record = OpenedWith.Undo(stash, cat.Id)!;
        Assert.AreEqual((second.RenderedRevision, record.HeldCopy), (record.Revision, stash.WorkFiles().Single()));
        CollectionAssert.AreEqual(first, File.ReadAllBytes(stash.Folder.InWork(record.HeldCopy!)), "the first adopted picture is now the one held");
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task TheListingIsAnsweredWhileAnExpirysRecycleWaitsOnTheHeldPreviousOriginalAndTheRecordIsThenCleared()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var (cat, _) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        Assert.IsTrue((await stash.Store!.RecordUses([adopted], stash.Clock.Now)).Succeeded);
        var held = stash.Folder.InWork(stash.WorkFiles().Single());

        var (recent, expired) = await ReadsWhileAChangeWaits.Asked(TestStash.Hold(held), () => stash.Store.ExpireAdoption(cat.Id, adopted.RenderedRevision), stash.Store.Recent);

        var listed = recent.Value!.Single();
        Assert.AreEqual((cat.Id, adopted.FilePath), (listed.Id, listed.FilePath), "listed on the adopted picture throughout (G1)");
        Assert.IsTrue(expired.Succeeded, expired.Failure?.ToString());
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
        Assert.IsEmpty(stash.WorkFiles());
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(Directory.GetFiles(stash.RecycleBin.Folder).Single()), "the previous original to the Recycle Bin (G29)");
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AnExpiryWhosePreviousOriginalStaysHeldPastTheDurationIsNamedAndKeepsTheRecord()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var (cat, _) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        var record = OpenedWith.Undo(stash, cat.Id);
        var held = stash.Folder.InWork(record!.HeldCopy!);

        Outcome refused;
        using (TestStash.Hold(held))
        {
            refused = await stash.Store!.ExpireAdoption(cat.Id, adopted.RenderedRevision);
        }

        Assert.AreEqual(new Failure(AdoptionUndo.ExpireAction, "cat", HeldFileRetry.HeldReason), refused.Failure);
        Assert.IsGreaterThan(1, stash.RecycleBin.Asked.Count, "asked again while held (G35)");
        Assert.AreEqual(record, OpenedWith.Undo(stash, cat.Id), "never cleared while its recycle is refused (G29)");
        Assert.IsTrue(File.Exists(held));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task TheListingIsAnsweredWhileASecondAdoptionWaitsToExpireTheFirstsRecordAndOneRecordThenStands()
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var first = File.ReadAllBytes(copy);
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        Assert.IsTrue((await stash.Store!.RecordUses([adopted], stash.Clock.Now)).Succeeded);
        var firstHeld = stash.Folder.InWork(stash.WorkFiles().Single());
        OpenedWith.Rewrite(copy, MediaFixtures.DistinctPng(6, 9, 7));
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id));

        var (recent, second) = await ReadsWhileAChangeWaits.Asked(TestStash.Hold(firstHeld), () => stash.Store.AdoptRewrite(cat.Id), stash.Store.Recent);

        var listed = recent.Value!.Single();
        Assert.AreEqual((cat.Id, adopted.FilePath), (listed.Id, listed.FilePath), "still the first adopted picture while the earlier record expires (G1)");
        Assert.IsTrue(second.Succeeded, second.Failure?.ToString());
        Assert.IsFalse(File.Exists(firstHeld), "the first previous original recycled once its hold went");
        var record = OpenedWith.Undo(stash, cat.Id)!;
        Assert.AreEqual((second.Value!.RenderedRevision, record.HeldCopy), (record.Revision, stash.WorkFiles().Single()));
        CollectionAssert.AreEqual(first, File.ReadAllBytes(stash.Folder.InWork(record.HeldCopy!)), "one record stands, holding the first adopted picture");
    }

    [TestMethod]
    public async Task ADeleteOfAnAdoptedItemKeepsItsRecordAndItsUndoBringsTheItemBackAsAdopted()
    {
        using var stash = new TestStash();
        var (cat, _) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        var record = OpenedWith.Undo(stash, cat.Id)!;

        var deleted = (await stash.Store!.Delete([cat.Id])).Single();

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        Assert.IsNull((await stash.Store.Item(cat.Id)).Value);
        CollectionAssert.AreEquivalent(new[] { record.HeldCopy!, Path.GetFileName(stash.HeldCopy(cat.Id)) }, stash.WorkFiles(), "its held original and the Delete's copy");
        Assert.AreEqual(record, OpenedWith.Undo(stash, cat.Id), "a Delete's mark leaves the record");
        Assert.IsTrue((await stash.Store.UndoDelete([deleted.Value!])).Single().Succeeded);
        TestStash.AssertSameItem(adopted, await EditedItems.Listed(stash.Store, cat.Id));
        Assert.AreEqual(record, OpenedWith.Undo(stash, cat.Id));
        Assert.IsTrue((await stash.Store.ExpireAdoption(cat.Id, adopted.RenderedRevision)).Succeeded);
        Assert.AreEqual(stash.Folder.InWork(record.HeldCopy!), stash.RecycleBin.Asked.Last());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ARowRemovedForGoodExpiresItsRecordAndEndsItsSession(bool byImportsUndo)
    {
        using var stash = new TestStash();
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        await OpenedWith.Adopt(stash, cat.Id);
        var held = stash.Folder.InWork(OpenedWith.Undo(stash, cat.Id)!.HeldCopy!);

        if (byImportsUndo)
        {
            Assert.IsTrue((await stash.Store!.Remove([cat.Id])).Single().Succeeded);
        }
        else
        {
            Assert.IsTrue((await stash.Store!.Delete([cat.Id])).Single().Succeeded);
            Assert.IsTrue((await stash.Store.FinishDelete([cat.Id])).Single().Succeeded);
        }

        CollectionAssert.Contains(stash.RecycleBin.Asked, held, "the previous original to the Recycle Bin (G29)");
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(stash.SqlQueryWhileOpen("SELECT id FROM items WHERE id = $id", r => r.GetInt64(0), ("$id", cat.Id)));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(copy)), "the copy and its folder");
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(false, DisplayName = "its previous original held")]
    [DataRow(true, DisplayName = "its Open with… copy held")]
    public async Task TheListingIsAnsweredWhileARowRemovedForGoodWaitsOnAHeldFileAndTheRowIsThenRemoved(bool copyHeld)
    {
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var (cat, copy) = await OpenedWith.AwaitingCat(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        var dog = await stash.Import("dog");
        Assert.IsTrue((await stash.Store!.RecordUses([adopted, dog], stash.Clock.Now)).Succeeded);
        var held = stash.Folder.InWork(OpenedWith.Undo(stash, cat.Id)!.HeldCopy!);
        Assert.IsTrue((await stash.Store.Delete([cat.Id])).Single().Succeeded);

        var (recent, finished) = await ReadsWhileAChangeWaits.Asked(TestStash.Hold(copyHeld ? copy : held), () => stash.Store.FinishDelete([cat.Id]), stash.Store.Recent);

        Assert.AreEqual(dog.Id, recent.Value!.Single().Id, "the marked row returned by no query meanwhile");
        Assert.IsTrue(finished.Single().Succeeded, finished.Single().Failure?.ToString());
        CollectionAssert.Contains(stash.RecycleBin.Asked, held, "the previous original to the Recycle Bin (G29)");
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(stash.SqlQueryWhileOpen("SELECT id FROM items WHERE id = $id", r => r.GetInt64(0), ("$id", cat.Id)));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(copy)), "the copy and its folder");
    }
}
