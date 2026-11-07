using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Delete with Undo through the window's one entry over a real stash (G18): the selection leaves the listing at once,
/// one notice raised only after its files went to the Recycle Bin (R9), its Undo bringing every item back whole and
/// its leaving, by its six seconds or its ×, finishing the delete (G16); refusals and failures named by file with the
/// rest deleted (G35); a busy state naming the action while the caller stays free (G35, E4); no use recorded (G10);
/// the import batch's Undo and Delete's composed, each row removed once (G12).
/// </summary>
[TestClass]
public sealed class DeletionTests
{
    [TestMethod]
    public async Task ThreeSelectedItemsLeaveTogetherWithOneNoticeRaisedOnlyAfterTheirFilesWentToTheRecycleBin()
    {
        using var listed = await ListedStash.Open();
        var (cat, dance, wow) = (await listed.Stash.Import("cat"), await listed.Stash.Import("dance"), await listed.Stash.Import("wow"));
        var kept = await listed.Stash.Import("kept");
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.Select([cat.Id, dance.Id, wow.Id], wow.Id);
        var seenAtEachRecycle = new List<(int Notices, string? Busy, int Listed)>();
        listed.Stash.RecycleBin.BeforeEach = _ => seenAtEachRecycle.Add((listed.Notices.Shown.Count, listed.Deletion.Busy, listed.Listing.Items.Count));

        var deleted = await listed.Deletion.Delete(listed.Listing.SelectedItems);

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { wow.FilePath, dance.FilePath, cat.FilePath }, listed.Stash.RecycleBin.Asked, "once each, in the grid's order, newest added first");
        CollectionAssert.AreEqual(Enumerable.Repeat((0, (string?)"Deleting 3 items…", 4), 3).ToList(), seenAtEachRecycle,
            "while the files go, the busy state names the action and no notice or refresh has come yet (R9, G35)");
        CollectionAssert.AreEqual(new[] { kept.Id }, listed.Listing.Items.Select(item => item.Id).ToList());
        var notice = listed.Notices.Shown.Single();
        Assert.AreEqual("Deleted “wow”, “dance” and “cat”.", notice.Text);
        Assert.AreEqual("Undo", notice.UndoLabel);
        Assert.IsNull(listed.Deletion.Busy, "the busy state ends with the call");
        Assert.HasCount(3, listed.Stash.WorkFiles(), "a held copy of each");
        CollectionAssert.AreEquivalent(new[] { kept.FileName }, listed.Stash.RootFiles());
        Assert.IsEmpty(listed.Failures);
        Assert.AreEqual(0L, Uses(listed), "a Delete records no use (G10)");
    }

    [TestMethod]
    public async Task UndoBringsBackEveryItemWithItsNameTagsFavoriteFlagUsesAndBytesAndRemovesTheRecycleBinsEntries()
    {
        using var listed = await ListedStash.Open();
        var bytes = new[] { TestStash.Bytes(1), TestStash.Bytes(2), TestStash.Bytes(3) };
        var items = new List<StashItem> { await listed.Stash.Import("tagged", bytes[0]), await listed.Stash.Import("favorite", bytes[1]), await listed.Stash.Import("used", bytes[2]) };
        listed.Stash.Tag(items[0].Id, "reaction");
        listed.Stash.SqlWhileOpen("UPDATE items SET favorite = 1 WHERE id = $id", ("$id", items[1].Id));
        await listed.Stash.Store!.RecordUses([items[2]], listed.Stash.Clock.Now);
        await listed.Listing.ShowScope(Scope.All);
        var before = listed.Listing.Items.ToList();
        listed.Listing.SelectAll();
        await listed.Deletion.Delete(listed.Listing.SelectedItems);
        var notice = listed.Notices.Shown.Single();

        await listed.Notices.Undo(notice);

        Assert.HasCount(3, before);
        Assert.HasCount(3, listed.Listing.Items);
        foreach (var (expected, actual) in before.Zip(listed.Listing.Items))
        {
            TestStash.AssertSameItem(expected, actual);
        }

        CollectionAssert.AreEqual(new[] { "reaction" }, listed.Listing.Items.Single(item => item.Name == "tagged").Tags.ToList());
        Assert.IsTrue(listed.Listing.Items.Single(item => item.Name == "favorite").Favorite);
        await listed.Listing.ShowScope(Scope.Recent);
        CollectionAssert.AreEqual(new[] { items[2].Id }, listed.Listing.Items.Select(item => item.Id).ToList(), "its use is back, so Recent lists it");
        CollectionAssert.AreEqual(bytes, items.Select(item => File.ReadAllBytes(item.FilePath)).ToList(), "the same bytes, back in place");
        Assert.HasCount(3, listed.Stash.RecycleBin.Removed, "every entry the Delete made is removed");
        Assert.IsEmpty(Directory.GetFiles(listed.Stash.RecycleBin.Folder), "the Recycle Bin holds nothing of them");
        Assert.IsEmpty(listed.Stash.WorkFiles());
        Assert.IsEmpty(listed.Notices.Shown, "the notice left as its Undo ran, and nothing failed");

        await listed.Notices.Undo(notice);

        Assert.HasCount(3, listed.Stash.RecycleBin.Removed, "a second Undo does nothing");
        Assert.AreEqual(1L, Uses(listed), "neither the Delete nor its Undo records a use (G10)");
    }

    [TestMethod]
    public async Task AnItemTheStoreSavedLeavesWithBothItsFilesAndItsUndoBringsBothBackAsTheListingShowedIt()
    {
        using var listed = await ListedStash.Open();
        var saved = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.webp"), EditedItems.BlackBoxAndBox)).Value!;
        var render = File.ReadAllBytes(saved.FilePath);
        await listed.Listing.ShowScope(Scope.All);
        var shown = listed.Listing.Items.Single();
        listed.Listing.Select([shown.Id], shown.Id);

        var deleted = await listed.Deletion.Delete(listed.Listing.SelectedItems);

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { listed.Stash.Folder.InRoot("cat.png"), listed.Stash.Folder.InOriginals("cat.webp") }, listed.Stash.RecycleBin.Asked, "its render and its original");
        Assert.IsEmpty(listed.Listing.Items);

        await listed.Notices.Undo(listed.Notices.Shown.Single());

        TestStash.AssertSameItem(shown, listed.Listing.Items.Single());
        CollectionAssert.AreEqual(render, File.ReadAllBytes(listed.Stash.Folder.InRoot("cat.png")));
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.webp"), File.ReadAllBytes(listed.Stash.Folder.InOriginals("cat.webp")));
        Assert.HasCount(2, listed.Stash.RecycleBin.Removed);
        Assert.IsEmpty(listed.Stash.WorkFiles());
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task TheNoticesSixSecondsOrItsCloseFinishTheDeleteLeavingTheFilesInTheRecycleBin()
    {
        foreach (var leave in new Action<ListedStash, Notice>[]
        {
            (listed, _) => { listed.Stash.Clock.Now += Notices.Lasting; listed.Notices.Sweep(); },
            (listed, notice) => listed.Notices.Close(notice),
        })
        {
            using var listed = await ListedStash.Open();
            var items = new[] { await listed.Stash.Import("one"), await listed.Stash.Import("two") };
            await listed.Listing.ShowScope(Scope.All);
            listed.Listing.SelectAll();
            await listed.Deletion.Delete(listed.Listing.SelectedItems);

            leave(listed, listed.Notices.Shown.Single());
            await listed.Stash.All();

            Assert.IsEmpty(listed.Stash.WorkFiles(), "the held copies are deleted");
            Assert.HasCount(2, Directory.GetFiles(listed.Stash.RecycleBin.Folder), "the Recycle Bin keeps the files");
            Assert.IsEmpty(listed.Stash.RecycleBin.Removed);
            Assert.IsEmpty(listed.Notices.Shown, "nothing failed");
            listed.Stash.Close();
            Assert.IsEmpty(listed.Stash.SqlQuery("SELECT id FROM items", row => row.GetInt64(0)), "the rows are removed");
        }
    }

    [TestMethod]
    public async Task AnUndoneDeleteIsNeverFinishedAfterwards()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("kept");
        await listed.Listing.ShowScope(Scope.All);
        await listed.Deletion.Delete(listed.Listing.SelectedItems);
        await listed.Notices.Undo(listed.Notices.Shown.Single());

        listed.Stash.Clock.Now += Notices.Lasting * 2;
        listed.Notices.Sweep();
        await listed.Stash.All();

        CollectionAssert.AreEqual(new[] { item.Id }, listed.Listing.Items.Select(i => i.Id).ToList());
        Assert.IsTrue(File.Exists(item.FilePath));
        listed.Stash.Close();
        CollectionAssert.AreEqual(new[] { 0L }, listed.Stash.SqlQuery("SELECT deleted FROM items", row => row.GetInt64(0)));
    }

    [TestMethod]
    public async Task AHoveredNoticeOutlastsItsSixSecondsAndItsUndoStillRestores()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("hovered");
        await listed.Listing.ShowScope(Scope.All);
        await listed.Deletion.Delete(listed.Listing.SelectedItems);
        var notice = listed.Notices.Shown.Single();
        listed.Notices.Hover(notice, over: true);

        listed.Stash.Clock.Now += TimeSpan.FromSeconds(30);
        listed.Notices.Sweep();
        await listed.Notices.Undo(notice);

        CollectionAssert.AreEqual(new[] { item.Id }, listed.Listing.Items.Select(i => i.Id).ToList());
        Assert.IsTrue(File.Exists(item.FilePath));
        Assert.IsEmpty(listed.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task AFileHeldForReadingIsCopiedItsRecycleRefusedAndNamedTheOthersDeletedAndUndoBringsAllThreeBack()
    {
        using var listed = await ListedStash.Open();
        var (first, held, last) = (await listed.Stash.Import("first"), await listed.Stash.Import("held"), await listed.Stash.Import("last"));
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.SelectAll();
        using var reader = new FileStream(held.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var deleted = await listed.Deletion.Delete(listed.Listing.SelectedItems);

        var refusal = new Failure("Move to the Recycle Bin", held.FileName, HeldFileRetry.HeldReason);
        Assert.AreEqual(refusal, deleted.Failure);
        CollectionAssert.AreEqual(new[] { "Deleted “last” and “first”. “held” could not be deleted yet; Undo brings it back.", refusal.Sentence },
            listed.Notices.Shown.Select(notice => notice.Text).ToList(), "the Delete's notice, then a notice of its own naming the file and why");
        Assert.IsEmpty(listed.Listing.Items, "the refused item is hidden with the others until Undo or the next start");
        Assert.IsTrue(File.Exists(held.FilePath), "its file never left");

        await listed.Notices.Undo(listed.Notices.Shown[0]);

        CollectionAssert.AreEquivalent(new[] { first.Id, held.Id, last.Id }, listed.Listing.Items.Select(item => item.Id).ToList());
        Assert.IsEmpty(listed.Stash.WorkFiles());
        Assert.IsEmpty(Directory.GetFiles(listed.Stash.RecycleBin.Folder));
        CollectionAssert.AreEqual(new[] { refusal.Sentence }, listed.Notices.Shown.Select(notice => notice.Text).ToList(), "the Undo names nothing");
    }

    [TestMethod]
    public async Task ARefusedItemLeftAloneStaysDeletedWithItsCopyAndIsBackWholeAtTheNextStartWhileStillHeld()
    {
        using var listed = await ListedStash.Open();
        var bytes = TestStash.Bytes(7);
        var (held, other) = (await listed.Stash.Import("held", bytes), await listed.Stash.Import("other"));
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.SelectAll();
        using (var reader = new FileStream(held.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await listed.Deletion.Delete(listed.Listing.SelectedItems);
            Assert.AreEqual("Deleted “other”. “held” could not be deleted yet; Undo brings it back.", listed.Notices.Shown[0].Text);

            listed.Notices.Close(listed.Notices.Shown[0]);
            await listed.Stash.All();

            Assert.IsTrue(File.Exists(listed.Stash.HeldCopy(held.Id)), "the refused item keeps its copy for the next start");
            Assert.HasCount(1, listed.Stash.WorkFiles(), "the finished one's copy is gone");
            Assert.HasCount(1, listed.Notices.Shown, "the finish names nothing more");

            var opening = await listed.Reopen();

            Assert.AreEqual(HeldFileRetry.HeldReason, opening.Failures.Single().Reason, "the start names the refusal it undid");
        }

        CollectionAssert.AreEqual(new[] { held.Id }, listed.Listing.Items.Select(item => item.Id).ToList(), "back whole; the finished one is gone");
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(held.FilePath));
        Assert.IsEmpty(listed.Stash.WorkFiles());
        Assert.HasCount(1, Directory.GetFiles(listed.Stash.RecycleBin.Folder), "only the finished item is in the Recycle Bin");
        Assert.IsFalse(File.Exists(other.FilePath));
    }

    [TestMethod]
    public async Task ACopyThatCannotBeMadeIsNamedItsItemStaysListedAndTheOtherTwoAreDeleted()
    {
        using var listed = await ListedStash.Open();
        var (first, locked, last) = (await listed.Stash.Import("first"), await listed.Stash.Import("locked"), await listed.Stash.Import("last"));
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.SelectAll();

        Outcome deleted;
        using (new FileStream(locked.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            deleted = await listed.Deletion.Delete(listed.Listing.SelectedItems);
        }

        Assert.AreEqual((locked.FileName, HeldFileRetry.HeldReason), (deleted.Failure?.Subject, deleted.Failure?.Reason), deleted.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { "Deleted “last” and “first”.", deleted.Failure!.Sentence }, listed.Notices.Shown.Select(notice => notice.Text).ToList());
        CollectionAssert.AreEqual(new[] { locked.Id }, listed.Listing.Items.Select(item => item.Id).ToList(), "it stays, and can be selected and deleted again");
        CollectionAssert.AreEquivalent(new[] { first.FilePath, last.FilePath }, listed.Stash.RecycleBin.Asked);
        Assert.HasCount(2, listed.Stash.WorkFiles(), "no copy of it is left behind");

        Assert.IsTrue((await listed.Deletion.Delete(listed.Listing.SelectedItems)).Succeeded);
        Assert.IsEmpty(listed.Listing.Items);
    }

    [TestMethod]
    public async Task NothingSelectedOrAStashNotOpenAsksNothingOfTheStore()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("one");

        Assert.IsTrue((await listed.Deletion.Delete([])).Succeeded);

        var opening = new Listing(_ => { });
        var notices = new Notices(listed.Stash.Clock);
        var refused = await new Deletion(opening, notices).Delete([item]);
        Assert.AreEqual(new Failure(Deletion.Action, "“one”", "your stash is still opening"), refused.Failure);
        Assert.AreEqual(refused.Failure!.Sentence, notices.Shown.Single().Text);
        Assert.IsEmpty(opening.SelectedItems, "while the stash opens nothing is selected, so Del has nothing to delete");
        Assert.IsEmpty(listed.Stash.RecycleBin.Asked);
        Assert.IsEmpty(listed.Notices.Shown);
        Assert.IsTrue(File.Exists(item.FilePath));
    }

    [TestMethod]
    public async Task UndoNamesAnItemThatCannotComeBackAndBringsBackTheOthers()
    {
        using var listed = await ListedStash.Open();
        var (kept, lost) = (await listed.Stash.Import("kept"), await listed.Stash.Import("lost"));
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.SelectAll();
        await listed.Deletion.Delete(listed.Listing.SelectedItems);
        File.Delete(listed.Stash.HeldCopy(lost.Id));

        await listed.Notices.Undo(listed.Notices.Shown.Single());

        CollectionAssert.AreEqual(new[] { kept.Id }, listed.Listing.Items.Select(item => item.Id).ToList());
        Assert.AreEqual(new Failure("Undo the delete", lost.FileName, "its held copy is gone").Sentence, listed.Notices.Shown.Single().Text);
    }

    [TestMethod]
    public async Task AnUndoWhoseRecycleBinEntryCannotBeRemovedBringsEveryItemBackAndNamesThatEntry()
    {
        using var listed = await ListedStash.Open();
        var items = new[] { await listed.Stash.Import("one"), await listed.Stash.Import("stuck"), await listed.Stash.Import("three") };
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.SelectAll();
        await listed.Deletion.Delete(listed.Listing.SelectedItems);
        listed.Stash.RecycleBin.RefusingRemoval = identity => identity.EndsWith("-" + items[1].FileName);

        await listed.Notices.Undo(listed.Notices.Shown.Single());

        CollectionAssert.AreEquivalent(items.Select(item => item.Id).ToList(), listed.Listing.Items.Select(item => item.Id).ToList());
        var entry = Path.GetFileName(listed.Stash.RecycleBin.Removed.Single(identity => identity.EndsWith("-" + items[1].FileName)));
        Assert.AreEqual(new Failure("Remove from the Recycle Bin", entry, "the test Recycle Bin refuses to remove it").Sentence, listed.Notices.Shown.Single().Text);
        Assert.IsEmpty(listed.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task FiveThousandItemsDeleteWhileTheCallerStaysFreeAndUndoBringsThemAllBack()
    {
        using var listed = await ListedStash.Open();
        await listed.Reopen(stash => stash.AddPictures(5000));
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.SelectAll();
        using var release = new ManualResetEventSlim();
        listed.Stash.RecycleBin.BeforeEach = _ => release.Wait(TimeSpan.FromSeconds(60));

        var deleting = listed.Deletion.Delete(listed.Listing.SelectedItems);

        Assert.IsFalse(deleting.IsCompleted, "the call returned to its caller while the store works, so the window answers (G35)");
        Assert.AreEqual("Deleting 5000 items…", listed.Deletion.Busy);
        release.Set();
        Assert.IsTrue((await deleting).Succeeded);
        Assert.IsEmpty(listed.Listing.Items);
        Assert.AreEqual("Deleted 5000 items.", listed.Notices.Shown.Single().Text);
        Assert.HasCount(5000, listed.Stash.WorkFiles());

        await listed.Notices.Undo(listed.Notices.Shown.Single());

        Assert.HasCount(5000, listed.Listing.Items);
        Assert.IsEmpty(listed.Stash.WorkFiles());
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task DelPressedAgainWhileItsDeleteRunsAsksForNothingMore()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("one");
        await listed.Stash.Import("two");
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.SelectAll();
        using var release = new ManualResetEventSlim();
        listed.Stash.RecycleBin.BeforeEach = _ => release.Wait(TimeSpan.FromSeconds(10));

        var first = listed.Deletion.Delete(listed.Listing.SelectedItems);
        var again = listed.Deletion.Delete(listed.Listing.SelectedItems);
        release.Set();

        Assert.IsTrue((await again).Succeeded);
        Assert.IsTrue((await first).Succeeded);
        Assert.HasCount(2, listed.Stash.RecycleBin.Asked, "each file once");
        Assert.AreEqual("Deleted “two” and “one”.", listed.Notices.Shown.Single().Text, "and no item named as no longer in the stash");
    }

    [TestMethod]
    public async Task TheImportsUndoAfterADeleteLeavesItToTheDeleteWhoseNoticeStillBringsItBackWhole()
    {
        using var listed = await ListedStash.Open();
        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));
        var imported = listed.Notices.Shown.Single();
        var item = listed.Listing.SelectedItems.Single();
        await listed.Deletion.Delete(listed.Listing.SelectedItems);
        var deletion = listed.Notices.Shown[^1];

        await listed.Notices.Undo(imported);

        Assert.HasCount(1, listed.Stash.RecycleBin.Asked, "the Delete's recycle only: no file touched twice");
        Assert.HasCount(1, listed.Stash.WorkFiles(), "its held copy waits for the Delete's notice");
        Assert.IsEmpty(listed.Listing.Items);
        CollectionAssert.AreEqual(new[] { deletion }, listed.Notices.Shown.ToList(), "the Delete's notice still shows, and nothing failed");

        await listed.Notices.Undo(deletion);

        Assert.AreEqual(item.Id, listed.Listing.Items.Single().Id, "while its notice shows, the Delete's Undo restores all of it (G18)");
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(item.FilePath));
        Assert.IsEmpty(Directory.GetFiles(listed.Stash.RecycleBin.Folder), "its entry is removed");
        Assert.IsEmpty(listed.Stash.WorkFiles());
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task TheImportsUndoAfterADeleteAndThenTheDeletesLeavingEndTheItemOnce()
    {
        using var listed = await ListedStash.Open();
        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));
        var imported = listed.Notices.Shown.Single();
        var item = listed.Listing.SelectedItems.Single();
        await listed.Deletion.Delete(listed.Listing.SelectedItems);
        await listed.Notices.Undo(imported);

        listed.Notices.Close(listed.Notices.Shown.Single());
        await listed.Stash.All();

        CollectionAssert.AreEqual(new[] { item.FilePath }, listed.Stash.RecycleBin.Asked);
        Assert.HasCount(1, Directory.GetFiles(listed.Stash.RecycleBin.Folder));
        Assert.IsEmpty(listed.Stash.WorkFiles());
        Assert.IsEmpty(listed.Stash.RootFiles());
        Assert.IsEmpty(listed.Notices.Shown, "nothing failed");
        listed.Stash.Close();
        Assert.IsEmpty(listed.Stash.SqlQuery("SELECT id FROM items", row => row.GetInt64(0)), "the row is removed once");
    }

    [TestMethod]
    public async Task DelPressedWhileItsDeleteRunsNeverDeletesTheItemItsRefreshSelectsNext()
    {
        using var listed = await ListedStash.Open();
        var kept = await listed.Stash.Import("kept");
        var pressed = await listed.Stash.Import("pressed");
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.Select([pressed.Id], pressed.Id);
        using var release = new ManualResetEventSlim();
        listed.Stash.RecycleBin.BeforeEach = _ => release.Wait(TimeSpan.FromSeconds(10));

        var first = listed.Deletion.DeleteSelection();
        // A load waiting behind the Delete, as typing or any reload leaves one, so the second press waits for the listing.
        var loading = listed.Listing.Refresh();
        var again = listed.Deletion.DeleteSelection();
        release.Set();
        await Task.WhenAll(first, loading, again);

        CollectionAssert.AreEqual(new[] { pressed.FilePath }, listed.Stash.RecycleBin.Asked, "the second press, made while the deleted item was still the selection, asks for nothing");
        CollectionAssert.AreEqual(new[] { kept.Id }, listed.Listing.Items.Select(item => item.Id).ToList());
        Assert.IsTrue(File.Exists(kept.FilePath));
        Assert.AreEqual("Deleted “pressed”.", listed.Notices.Shown.Single().Text);
    }

    [TestMethod]
    public async Task DelDeletesTheSelectionOfWhatWasTypedOnceItHasArrived()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Stash.Import("dog");
        await listed.Listing.ShowScope(Scope.All);

        var typing = listed.Listing.SetQuery("ca");
        var deleted = await listed.Deletion.DeleteSelection();
        await typing;

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { cat.FilePath }, listed.Stash.RecycleBin.Asked, "the first match of what was typed, not what showed before it (G5)");
    }

    [TestMethod]
    public async Task TheImportsUndoAfterTheDeletesUndoRecyclesTheItemAgain()
    {
        using var listed = await ListedStash.Open();
        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));
        var imported = listed.Notices.Shown.Single();
        var item = listed.Listing.SelectedItems.Single();
        await listed.Deletion.Delete(listed.Listing.SelectedItems);

        await listed.Notices.Undo(listed.Notices.Shown[^1]);
        await listed.Notices.Undo(imported);

        CollectionAssert.AreEqual(new[] { item.FilePath, item.FilePath }, listed.Stash.RecycleBin.Asked, "recycled again by the import's Undo");
        Assert.HasCount(1, Directory.GetFiles(listed.Stash.RecycleBin.Folder), "one entry for it: the Delete's was removed by its Undo");
        Assert.IsEmpty(listed.Listing.Items);
        Assert.IsEmpty(listed.Stash.RootFiles());
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task ADeleteOfAnItemTheImportsUndoRemovedNamesItByItsName()
    {
        using var listed = await ListedStash.Open();
        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));
        var item = listed.Listing.SelectedItems.Single();
        await listed.Notices.Undo(listed.Notices.Shown.Single());

        var deleted = await listed.Deletion.Delete([item]);

        Assert.AreEqual(new Failure(Deletion.Action, "still", "it is no longer in the stash"), deleted.Failure);
        Assert.AreEqual(deleted.Failure!.Sentence, listed.Notices.Shown.Single().Text, "the one failure, and no Delete notice with nothing to undo");
    }

    /// <summary>The uses the stash has recorded; closes the store to read its file, where <see cref="ListedStash.Uses"/> reads it open.</summary>
    private static long Uses(ListedStash listed)
    {
        listed.Stash.Close();
        return listed.Stash.SqlQuery("SELECT COUNT(*) FROM uses", row => row.GetInt64(0)).Single();
    }
}
