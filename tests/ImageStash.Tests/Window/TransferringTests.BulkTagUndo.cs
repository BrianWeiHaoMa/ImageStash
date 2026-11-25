using Avalonia.Input;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

public sealed partial class TransferringTests
{
    private const string BulkTag = "reaction";
    private const string ExportWaitsForNotice = "Waiting for the Undo of a notice before exporting your stash…";
    private static readonly TimeSpan BulkTagTransferLimit = TimeSpan.FromSeconds(20);

    [TestMethod]
    [DataRow(false, DisplayName = "export copies the committed bulk tag Undo from Ctrl+Z")]
    [DataRow(true, DisplayName = "failed export publication leaves the bulk tag Undo committed")]
    public async Task ExportWaitsForTheShowingBulkTagUndoAndItsTransaction(bool refusePublication)
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(10));
        var (first, second) = await ImportBulkTagPair(listed);
        var before = listed.Stash.TagRows();
        var notice = await ApplyBulkTag(listed, first, second);
        var archive = listed.Stash.Outside("bulk-tags.zip");
        if (refusePublication)
        {
            // The sibling partial ZIP can be written, but its final rename cannot replace this directory.
            Directory.CreateDirectory(archive);
        }

        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(archive);
        var page = listed.Page();
        var copyBegan = false;
        listed.Transferring.Changed += () =>
        {
            if (listed.Listing.WholeStashActBegun)
            {
                copyBegan = true;
            }
        };
        var exporting = listed.Turns.Act(() => page.Export(null!));
        Outcome exported;
        try
        {
            await Handing.Eventually(() => listed.Turns.Act(() =>
                page.ExportLine == ExportWaitsForNotice || listed.Listing.WholeStashActBegun || exporting.IsCompleted));
            listed.Turns.Act(() =>
            {
                Assert.AreEqual(ExportWaitsForNotice, page.ExportLine, "export names the Undo it is waiting for (G35)");
                Assert.IsTrue(listed.Notices.OffersUndo(notice), "the original offer stands during the wait (G16)");
                Assert.AreEqual(StashExport.InProgress, listed.Listing.CannotStartReason, "new changes are barred during the wait (G36)");
                Assert.IsFalse(listed.Listing.WholeStashActBegun);
            });

            await RunBulkTagUndoWhileExportWaits(listed, notice, byKey: !refusePublication);
            CollectionAssert.AreEqual(before, listed.Stash.TagRows(), "the inverse restored each item's own tags (G28)");
        }
        finally
        {
            // A notice from the inverse can itself be waited for, as with Delete Undo.
            listed.Turns.Act(() =>
            {
                listed.Stash.Clock.Now += TimeSpan.FromMinutes(1);
                listed.Notices.Sweep();
            });
            exported = await exporting.WaitAsync(BulkTagTransferLimit);
        }

        Assert.IsTrue(copyBegan, "export reached copying after the inverse");
        CollectionAssert.AreEqual(before, listed.Stash.TagRows(), "the committed inverse stands after export ends (G36)");
        if (refusePublication)
        {
            Assert.AreEqual(StashExport.Action, exported.Failure?.Action, "the export failure names its action (G35)");
            Assert.AreEqual("bulk-tags.zip", exported.Failure!.Subject);
            Assert.AreEqual(exported.Failure.Sentence, page.WarningOf(SettingsPage.ExportTitle));
            Assert.IsTrue(Directory.Exists(archive), "the directory at the chosen path survived publication");
            Assert.IsFalse(File.Exists(archive), "no archive was published");
        }
        else
        {
            Assert.IsTrue(exported.Succeeded, exported.Failure?.Sentence);
            using var copied = new TestStash();
            Directory.CreateDirectory(copied.Folder.Root);
            File.WriteAllBytes(copied.Folder.StoreFile, ExportedStash.Entry(archive, "stash.db"));
            CollectionAssert.AreEqual(before, copied.TagRows(), "the exported store includes the whole inverse (G28, G36)");
        }
    }

    [TestMethod]
    [DataRow(false, DisplayName = "hidden window expires the bulk tag Undo before import copies")]
    [DataRow(true, DisplayName = "minimised window expires the bulk tag Undo before import copies")]
    public async Task ImportExpiresTheBulkTagUndoBeforeCopyingAndReusesItsItemIds(bool minimised)
    {
        using var listed = await ListedStash.Open();
        var (first, second) = await ImportBulkTagPair(listed);
        using var source = new TestStash();
        await source.OpenCleanly();
        var replacementFirst = await source.ImportPicture("tiger", "tiger.png", MediaFixtures.DistinctPng(48, 8, 6));
        var replacementSecond = await source.ImportPicture("wolf", "wolf.png", MediaFixtures.DistinctPng(64, 8, 6));
        Assert.AreEqual((first.Id, second.Id), (replacementFirst.Id, replacementSecond.Id), "the archive reuses both old IDs");
        source.Tag(replacementFirst.Id, "wild");
        source.Tag(replacementSecond.Id, BulkTag);
        var replacementTags = source.TagRows();
        var archive = await ExportedStash.Export(source, MovingStash.Keeper(source, []));
        var page = await listed.Offered(archive);
        var notice = await ApplyBulkTag(listed, first, second);

        listed.WindowShowing.Seen(visible: minimised, minimised: minimised);
        Assert.IsTrue(listed.Notices.OffersUndo(notice), "the hidden clock pauses the offer until a transfer is asked (G16)");
        (bool Ended, bool OffersUndo)? atCopyStart = null;
        listed.Transferring.Changed += () =>
        {
            if (listed.Listing.WholeStashActBegun)
            {
                atCopyStart ??= (notice.TimeEnded, listed.Notices.OffersUndo(notice));
            }
        };

        var importing = listed.Turns.Act(page.ConfirmImport);
        Outcome imported;
        try
        {
            imported = await importing.WaitAsync(BulkTagTransferLimit);
        }
        finally
        {
            listed.Turns.Act(() =>
            {
                listed.WindowShowing.Seen(visible: true, minimised: false);
                if (!importing.IsCompleted)
                {
                    listed.Stash.Clock.Now += TimeSpan.FromMinutes(1);
                    listed.Notices.Sweep();
                }
            });
            await importing.WaitAsync(BulkTagTransferLimit);
        }

        Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence);
        Assert.AreEqual<(bool Ended, bool OffersUndo)?>((true, false), atCopyStart, "the offer expired before copying (G16, State: Move)");
        CollectionAssert.AreEqual(new[] { (first.Id, "tiger"), (second.Id, "wolf") },
            (await listed.Stash.All()).OrderBy(item => item.Id).Select(item => (item.Id, item.Name)).ToArray(), "the archive replaced the old items (G36)");
        CollectionAssert.AreEqual(replacementTags, listed.Stash.TagRows());

        await listed.Turns.Act(() => listed.Notices.Undo(notice));
        await listed.Turns.Act(() => PressBulkTagUndo(listed));

        CollectionAssert.AreEqual(replacementTags, listed.Stash.TagRows(), "neither the old card nor Ctrl+Z can change the replacement rows (G7, G16, G36)");
    }

    private static async Task RunBulkTagUndoWhileExportWaits(ListedStash listed, Notice notice, bool byKey)
    {
        var undoing = Task.CompletedTask;
        try
        {
            using var held = listed.Stash.HoldStore();
            undoing = listed.Turns.Act(() => byKey ? PressBulkTagUndo(listed) : listed.Notices.Undo(notice));
            // Wait for transfer to read the new state after the original notice leaves, while SQLite still holds out the inverse.
            await Handing.Eventually(() => listed.Turns.Act(() =>
                listed.Transferring.ExportLine != ExportWaitsForNotice || undoing.IsCompleted));
            listed.Turns.Act(() =>
            {
                Assert.IsFalse(listed.Notices.OffersUndo(notice), "the offer was taken");
                Assert.IsFalse(undoing.IsCompleted, "the inverse is still held by SQLite");
                Assert.IsFalse(listed.Listing.WholeStashActBegun, "export cannot start copying while the inverse is held (G28, State: Move)");
                StringAssert.StartsWith(listed.Transferring.ExportLine, "Waiting for ", "the busy state names a wait for the change (G35)");
            });
        }
        finally
        {
            await undoing.WaitAsync(BulkTagTransferLimit);
        }
    }

    private static async Task PressBulkTagUndo(ListedStash listed)
    {
        Assert.AreEqual(KeyAction.UndoNotice, await listed.Press(Key.Z, KeyModifiers.Control, KeyPlace.Grid));
        listed.Keys.Released(Key.Z);
    }

    private static async Task<(StashItem First, StashItem Second)> ImportBulkTagPair(ListedStash listed)
    {
        var first = await listed.Stash.ImportFixture("cat", "still.png");
        var second = await listed.Stash.ImportFixture("dog", "photo.jpg");
        listed.Stash.Tag(first.Id, BulkTag);
        listed.Stash.Tag(second.Id, "keep");
        await listed.Listing.ShowScope(Scope.All);
        return (first, second);
    }

    private static async Task<Notice> ApplyBulkTag(ListedStash listed, StashItem first, StashItem second)
    {
        var changed = await listed.Organising.AddTag([first, second], BulkTag);
        Assert.IsTrue(changed.Succeeded, changed.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { (first.Id, BulkTag), (second.Id, "keep"), (second.Id, BulkTag) },
            listed.Stash.TagRows(), "the forward change committed before the notice (G28)");
        var offers = listed.Notices.Shown.Where(listed.Notices.OffersUndo).ToArray();
        Assert.HasCount(1, offers, "the bulk tag change must show a notice offering Undo (G16)");
        return offers[0];
    }
}
