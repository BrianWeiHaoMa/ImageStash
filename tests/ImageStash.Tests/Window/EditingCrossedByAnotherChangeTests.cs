using ImageStash.Editor;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// An open edit crossed by another change of its item, headless (G16, G20, G26, G29, P1): a rename from the capture card
/// before Save, an import's Undo removing the item, a Revert's Undo before the edit ends, an earlier Cancel's Undo
/// replacing the open edit, a Delete with its Undo or expiry, Save as new item over an earlier Save failed after its
/// commit, the footer's size after a rename or a Revert's Undo moved the original, Edit opened while a held original
/// waits through two renames, Edit asked while its item's own rename waits on that held original, and an Open with…
/// adoption's Undo while the adopted item is open in the editor, and a Revert's Undo after an adoption replaced the
/// original its layers were made over; each leaves the edit and the item consistent.
/// </summary>
[TestClass]
public sealed class EditingCrossedByAnotherChangeTests
{
    [TestMethod]
    public async Task RenameBeforeSaveUsesTheCurrentNameAndOriginal()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        listed.CaptureNotification.Raise(cat, CaptureKind.Screenshot, false, null);
        await listed.Editing.OpenFromNotification(new CaptureEdit(cat, false));
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        // CaptureCard's committed field calls this same public Renaming path, with notification notices.
        var cardRename = new Renaming(listed.Listing, listed.Notifications, listed.Handoff);
        Assert.IsTrue((await cardRename.Rename(cat, "new name")).Succeeded);
        var saved = await listed.Editing.Save();
        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        var edit = (await listed.Stash.Store!.ForEdit(cat.Id)).Value!;
        Assert.AreEqual("new name", edit.Item.Name);
        Assert.AreEqual("new name.png", edit.Item.FileName);
        Assert.AreEqual(listed.Stash.Folder.InOriginals("new name.png"), edit.OriginalPath);
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(edit.OriginalPath));
        Assert.AreEqual(session.Layers, edit.Layers);
    }

    [TestMethod]
    public async Task ImportUndoClosesItsOpenEditWithoutSaving()
    {
        using var listed = await ListedStash.Open();
        var imported = await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));
        var cat = imported.Single().Item!;
        var notice = listed.Notices.Shown.Single();
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        await listed.Notices.Undo(notice);
        await EditorSessions.Until(() => !listed.Editing.IsOpen, "the removed import's editor remained open");
        Assert.IsFalse(session.IsOpen);
        Assert.IsEmpty(await listed.Stash.All());
        Assert.IsEmpty(listed.Notices.Shown);
        Assert.IsEmpty(Directory.GetFiles(listed.Stash.Folder.Work));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RevertUndoBeforeEditorEndingKeepsTheOriginalAndHonorsTheChosenEnding(bool save)
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.Cropped)).Value!;
        await listed.Editing.RevertToOriginal(cat);
        var revert = listed.Notices.Shown.Single();
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((5, 3), (7, 5));
        var live = session.Layers;
        await listed.Notices.Undo(revert);
        if (save)
        {
            var saved = await listed.Editing.Save();
            Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        }
        else
        {
            Assert.IsTrue(listed.Editing.Cancel());
        }
        var edit = (await listed.Stash.Store!.ForEdit(cat.Id)).Value!;
        Assert.AreEqual(save ? live : EditedItems.Cropped, edit.Layers);
        Assert.AreEqual(save ? 8 : 4, edit.Item.Facts.Width);
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(edit.OriginalPath));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EarlierCancelUndoReplacesAnotherOpenEditAndItsUndoCanBringThatEditBack(bool sameItem)
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        var dog = sameItem ? cat : await listed.Stash.ImportFixture("dog", "transparent.png");
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var first = listed.Editing.Session!;
        first.Tool = first.Hide();
        first.DragThrough((1, 1), (4, 3));
        var discarded = first.Layers;
        Assert.IsTrue(listed.Editing.Cancel());
        var undoFirst = listed.Notices.Shown.Single();
        await listed.Editing.Open(dog, EditOrigin.Preview);
        var second = listed.Editing.Session!;
        second.Tool = second.Hide();
        second.DragThrough((5, 3), (7, 5));
        var replaced = second.Layers;
        await listed.Notices.Undo(undoFirst);
        Assert.IsFalse(second.IsOpen);
        Assert.AreEqual(cat.Id, listed.Editing.Session!.Item.Id);
        Assert.AreEqual(discarded, listed.Editing.Session.Layers);
        await listed.Notices.Undo(listed.Notices.Shown.Single());
        Assert.AreEqual(dog.Id, listed.Editing.Session!.Item.Id);
        Assert.AreEqual(replaced, listed.Editing.Session.Layers);
        Assert.IsTrue((await listed.Stash.Store!.ForEdit(cat.Id)).Value!.Layers.IsEmpty);
        listed.Editing.Cancel();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeleteClosesTheOpenEditAndUndoOrExpiryKeepsTheDiscardedLayersUnsaved(bool undo)
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.Cropped)).Value!;
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (3, 3));
        Assert.IsTrue((await listed.Deletion.Delete([cat])).Succeeded);
        await EditorSessions.Until(() => !listed.Editing.IsOpen, "Delete did not close the edit");
        Assert.IsFalse(session.IsOpen);
        var notice = listed.Notices.Shown.Single();
        if (undo)
        {
            await listed.Notices.Undo(notice);
            var edit = (await listed.Stash.Store!.ForEdit(cat.Id)).Value!;
            Assert.AreEqual(EditedItems.Cropped, edit.Layers);
            Assert.IsFalse(listed.Editing.IsOpen);
            await listed.Editing.Open(edit.Item, EditOrigin.Grid);
            Assert.AreEqual(EditedItems.Cropped, listed.Editing.Session!.Layers);
            listed.Editing.Cancel();
        }
        else
        {
            listed.Stash.Clock.Now += Notices.Lasting;
            listed.Notices.Sweep();
            await EditorSessions.Until(() => listed.Stash.SqlQueryWhileOpen("SELECT COUNT(*) FROM items", r => r.GetInt64(0)).Single() == 0, "Delete did not expire");
            Assert.IsEmpty(await listed.Stash.All());
            Assert.IsEmpty(Directory.GetFiles(listed.Stash.Folder.Work));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SaveAsNewWithAnEarlierFailedCommittedSaveCopiesTheOriginalAndLeavesSourceComplete(bool moved)
    {
        using var listed = await ListedStash.Open();
        var cat = moved ? await EditedItems.FirstSaveMoved(listed.Stash) : await EditedItems.FirstSaveCommitted(listed.Stash);
        await listed.Listing.Refresh();
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((5, 3), (7, 5));
        var live = session.Layers;
        var made = await listed.Editing.SaveAsNewItem();
        Assert.IsTrue(made.Succeeded, made.Failure?.Sentence);
        Assert.AreNotEqual(cat.Id, session.Item.Id);
        var source = (await listed.Stash.Store!.ForEdit(cat.Id)).Value!;
        var copy = (await listed.Stash.Store.ForEdit(session.Item.Id)).Value!;
        Assert.AreEqual(EditedItems.BlackBoxAndBox, source.Layers);
        Assert.AreEqual(live, copy.Layers);
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(copy.OriginalPath));
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(source.OriginalPath));
        Assert.IsEmpty(Directory.GetFiles(listed.Stash.Folder.Work));
        listed.Editing.Cancel();
    }

    [TestMethod]
    [DataRow("renamed")]
    [DataRow("renamed-onto")]
    [DataRow("revert-undone")]
    public async Task TheFootersSizeIsTheSavedPicturesAfterTheItemsFilesMoveUnderTheEdit(string moved)
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        var other = await listed.Stash.ImportPicture("other", "other.png", MediaFixtures.DistinctPng(91, 12, 10));
        if (moved == "revert-undone")
        {
            cat = (await listed.Stash.SaveEdit(cat, EditedItems.Cropped)).Value!;
            await listed.Listing.Refresh();
            Assert.IsTrue((await listed.Editing.RevertToOriginal(cat)).Succeeded);
        }

        await listed.Listing.Refresh();
        var revert = listed.Notices.Shown.SingleOrDefault();
        Assert.IsTrue((await listed.Editing.Open(cat, EditOrigin.Grid)).Succeeded);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (3, 2));
        await EditorSessions.Until(() => !session.Estimating && session.EstimatedBytes.HasValue, "the first estimate never came");
        // The card's name field and a Revert notice's Undo both stay in reach above an open editor.
        var cardRename = new Renaming(listed.Listing, listed.Notifications, listed.Handoff);
        if (moved == "revert-undone")
        {
            await listed.Notices.Undo(revert!);
        }
        else
        {
            Assert.IsTrue((await cardRename.Rename(cat, "moved")).Succeeded);
            if (moved == "renamed-onto")
            {
                Assert.IsTrue((await cardRename.Rename(other, "cat")).Succeeded);
            }
        }

        session.DragThrough((5, 3), (7, 5));
        await EditorSessions.Until(() => !session.Estimating, "the estimate never ended");
        var estimate = session.EstimatedBytes;
        var saved = await listed.Editing.Save();
        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        var finished = (await listed.Stash.Store!.ForEdit(cat.Id)).Value!.Item;
        Assert.AreEqual(new FileInfo(finished.FilePath).Length, estimate, "the footer's size is the saved still's bytes (G24)");
    }

    [TestMethod]
    public async Task EditOpenedWhileAnotherProgramHoldsTheOriginalNeverShowsAPictureRenamesPutAtItsPath()
    {
        using var listed = await ListedStash.Open();
        var store = listed.Stash.Store!;
        // Three rounds: a retry that falls between the two renames finds no file there, and is refused for that instead.
        for (var round = 0; round < 3; round++)
        {
            var cat = await listed.Stash.ImportPicture($"cat {round}", $"cat {round}.png", MediaFixtures.DistinctPng(10 + round, 8, 6));
            var other = await listed.Stash.ImportPicture($"other {round}", $"other {round}.png", MediaFixtures.DistinctPng(90 + round, 12, 10));
            await listed.Listing.Refresh();
            Task<Outcome> opening;
            // Held as a scanner holds it (E3), reading barred and renaming allowed: Edit's open waits in its retry while two
            // renames, as the cards' fields ask them, queued behind Edit's read of the item, put the other picture at its path.
            using (new FileStream(cat.FilePath, FileMode.Open, FileAccess.Read, FileShare.Delete))
            {
                opening = listed.Editing.Open(cat, EditOrigin.Grid);
                var renames = await Task.WhenAll(store.Rename(cat, $"moved {round}"), store.Rename(other, $"cat {round}"));
                Assert.IsTrue(renames.All(renamed => renamed.Succeeded));
            }

            var opened = await opening;
            Assert.IsNull(listed.Editing.Session, "the other item's picture never shown as this one's original (G20)");
            Assert.AreEqual(("Read the picture", $"cat {round}"), (opened.Failure?.Action, opened.Failure?.Subject), "refused by name (G35)");

            var askedAgain = await listed.Editing.Open(cat, EditOrigin.Grid);
            Assert.IsTrue(askedAgain.Succeeded, askedAgain.Failure?.Sentence);
            Assert.AreEqual(listed.Stash.Folder.InRoot($"moved {round}.png"), listed.Editing.Session!.Edit.OriginalPath, "asked again, it opens on the original where the rename put it (G20)");
            Assert.IsTrue(listed.Editing.Cancel());
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task EditAskedWhileItsItemsRenameWaitsOnTheHeldOriginalOpensOnTheOriginalWhereTheRenamePutIt()
    {
        using var listed = await ListedStash.Open(HeldFileRetry.Duration);
        var store = listed.Stash.Store!;
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        Task<Outcome> opening;
        bool openedWhileHeld;
        // Held as a scanner holds a fresh file (E3), reading barred: the rename the card's field asks waits on it, and Edit,
        // asked meanwhile, must not be handed the path that rename is about to move.
        using (new FileStream(cat.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var renaming = store.Rename(cat, "dog");
            await Task.Delay(ReadsWhileAChangeWaits.AskedAfter);
            opening = listed.Editing.Open(cat, EditOrigin.Grid);
            await Task.Delay(ReadsWhileAChangeWaits.HeldFor - ReadsWhileAChangeWaits.AskedAfter);
            openedWhileHeld = opening.IsCompleted;
            Assert.IsFalse(renaming.IsCompleted, "the rename still waits on the held original");
        }

        var opened = await opening;

        Assert.IsFalse(openedWhileHeld, "Edit's read waits for the change asked before it");
        Assert.IsTrue(opened.Succeeded, opened.Failure?.Sentence);
        var edit = listed.Editing.Session!.Edit;
        Assert.AreEqual(("dog", listed.Stash.Folder.InRoot("dog.png")), (edit.Item.Name, edit.OriginalPath), "opened on the original where the rename put it (G20)");
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(edit.OriginalPath));
    }

    [TestMethod]
    public async Task AnAdoptionsUndoLeavesTheOpenEditOfItsItemAsCancelWouldSoNoEditShowsAnotherPicture()
    {
        using var listed = await ListedStash.Open();
        var still = MediaFixtures.Bytes("still.png");
        var cat = await listed.Stash.ImportPicture("cat", "cat.png", still);
        await listed.Listing.Refresh();
        var replaced = await OpenedWith.ReplacedThroughTheMenu(listed, cat, MediaFixtures.DistinctPng(92, 7, 5));
        Assert.IsTrue((await listed.Editing.Open(listed.Listing.Items.Single(), EditOrigin.Grid)).Succeeded);
        var session = listed.Editing.Session!;
        Assert.AreEqual(new PixelSize(7, 5), session.OutputSize, "the edit opens on the adopted picture");
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));

        await listed.Notifications.Undo(replaced);

        Assert.IsFalse(listed.Editing.IsOpen, "the edit over the replaced picture is left as Cancel would (G20)");
        Assert.IsFalse(session.IsOpen);
        Assert.AreEqual(EditedItems.Hash(still), OpenedWith.OriginalHash(listed.Stash, cat.Id), "Undo restored the previous original (G27)");
        Assert.IsTrue((await EditedItems.ForEdit(listed.Stash.Store!, cat.Id)).Layers.IsEmpty, "nothing of the discarded edit was saved");
        var discarded = listed.Notices.Shown.Single();
        Assert.AreEqual(("Discarded the edit of “cat”.", "Undo"), (discarded.Text, discarded.UndoLabel), "the Cancel notice, whose Undo brings the edit back (G20)");
        await listed.Notices.Undo(discarded);
        Assert.AreEqual(new PixelSize(8, 6), listed.Editing.Session!.OutputSize, "brought back over the original the item now has");
        Assert.HasCount(1, listed.Editing.Session.Layers.Layers);
    }

    [TestMethod]
    public async Task ASaveIsRefusedByNameWhenTheOriginalWasReplacedUnderTheOpenEdit()
    {
        using var listed = await ListedStash.Open();
        var still = MediaFixtures.Bytes("still.png");
        var cat = await listed.Stash.ImportPicture("cat", "cat.png", still);
        await OpenedWith.AwaitingRewrite(listed.Stash, cat, MediaFixtures.DistinctPng(92, 7, 5));
        var adopted = await OpenedWith.Adopt(listed.Stash, cat.Id);
        await listed.Listing.Refresh();
        Assert.IsTrue((await listed.Editing.Open(listed.Listing.Items.Single(), EditOrigin.Grid)).Succeeded);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        // As an Undo that ran while Edit's open was still reading the adopted picture leaves the edit.
        await OpenedWith.UndoAdoption(listed.Stash, cat.Id, adopted.RenderedRevision);
        session.DragThrough((2, 2), (5, 4));
        await EditorSessions.Until(() => !session.Estimating, "the footer's estimate, which reads the item again, has run");

        var saved = await listed.Editing.Save();

        Assert.AreEqual(new Failure(ItemSave.Action, "cat", EditorSession.OriginalReplaced), saved.Failure, "refused by name, never rendered over a picture the editor did not show (G21, G35)");
        Assert.IsTrue(listed.Editing.IsOpen, "the edit stays open on its layers");
        Assert.AreEqual(EditedItems.Hash(still), OpenedWith.OriginalHash(listed.Stash, cat.Id));
        Assert.IsTrue((await EditedItems.ForEdit(listed.Stash.Store!, cat.Id)).Layers.IsEmpty, "nothing saved");
    }

    [TestMethod]
    public async Task ARevertsUndoAfterAnAdoptionReplacedTheOriginalIsRefusedByNameAndSavesNothing()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportPicture("cat", "cat.png", MediaFixtures.Bytes("still.png"));
        var edited = await listed.Stash.SaveEdit(cat, EditedItems.BlackBoxAndBox);
        Assert.IsTrue(edited.Succeeded, edited.Failure?.Sentence);
        await listed.Listing.Refresh();
        Assert.IsTrue((await listed.Editing.RevertToOriginal(listed.Listing.Items.Single())).Succeeded);
        var reverted = listed.Notices.Shown.Single();
        var rewrite = MediaFixtures.DistinctPng(96, 7, 5);
        await OpenedWith.ReplacedThroughTheMenu(listed, listed.Listing.Items.Single(), rewrite);

        await listed.Notices.Undo(reverted);

        CollectionAssert.Contains(listed.Notices.Shown.Select(notice => notice.Text).ToList(), new Failure(Editing.UndoRevertAction, "cat", Editing.OriginalReplacedSinceRevert).Sentence, "refused by name (G35)");
        Assert.IsTrue((await EditedItems.ForEdit(listed.Stash.Store!, cat.Id)).Layers.IsEmpty, "no layer made over the earlier picture lands on the adopted one (G21)");
        Assert.AreEqual(EditedItems.Hash(rewrite), OpenedWith.OriginalHash(listed.Stash, cat.Id));
    }

    [TestMethod]
    [DataRow(EditOrigin.Grid)]
    [DataRow(EditOrigin.Preview)]
    [DataRow(EditOrigin.Notification)]
    public async Task EditAskedWhileARevertsUndoRendersItsItemOpensOnceTheUndoHasRunOnTheLayersItBroughtBack(EditOrigin from)
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "animated.gif"), EditedItems.Cropped)).Value!;
        await listed.Listing.Refresh();
        Assert.IsTrue((await listed.Editing.RevertToOriginal(cat)).Succeeded);
        using var rendering = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        // A render as long as a large picture's: the Undo's change runs while Edit is asked.
        listed.Renderer = (original, name, layers, format, work, retry) =>
        {
            rendering.Set();
            release.Wait(TimeSpan.FromSeconds(15));
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        };
        var undoing = listed.Notices.Undo(listed.Notices.Shown.Single());
        Assert.IsTrue(rendering.Wait(TimeSpan.FromSeconds(10)));

        listed.Listing.Focus(cat.Id);
        var opening = from switch
        {
            EditOrigin.Grid => listed.Editing.OpenOnSelection(null),
            EditOrigin.Preview => listed.Editing.Open(cat, EditOrigin.Preview),
            _ => listed.Editing.OpenFromNotification(new CaptureEdit(cat, true)),
        };
        await Task.WhenAny(opening, Task.Delay(500));
        var openedMeanwhile = listed.Editing.IsOpen;
        release.Set();
        await undoing;
        var opened = await opening;

        Assert.IsFalse(openedMeanwhile, "Edit opens once the change of its item has run (G20)");
        Assert.IsTrue(opened.Succeeded, opened.Failure?.Sentence);
        Assert.AreEqual(EditedItems.Cropped, listed.Editing.Session!.Layers, "on the layers the Undo brought back, live (G20)");
        Assert.IsTrue((await listed.Editing.Save()).Succeeded);
        Assert.AreEqual(EditedItems.Cropped, (await EditedItems.ForEdit(listed.Stash.Store!, cat.Id)).Layers, "a Save of the edit as shown keeps them");
    }

    [TestMethod]
    public async Task ARevertsUndoBegunWhileEditStillReadsAHeldOriginalIsWaitedForAndEditOpensOnTheLayersItBroughtBack()
    {
        using var listed = await ListedStash.Open(editRetry: TimeSpan.FromSeconds(10));
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.Cropped)).Value!;
        await listed.Listing.Refresh();
        Assert.IsTrue((await listed.Editing.RevertToOriginal(cat)).Succeeded);
        var reverted = (await listed.Stash.Store!.ForEdit(cat.Id)).Value!;
        using var rendering = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        listed.Renderer = (original, name, layers, format, work, retry) =>
        {
            rendering.Set();
            release.Wait(TimeSpan.FromSeconds(15));
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        };

        bool openedMeanwhile;
        Task<Outcome> opening;
        Task undoing;
        // Held as a scanner holds it (E3): Edit's open retries its read, and the Revert's Undo begins meanwhile.
        using (var held = new FileStream(reverted.OriginalPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            opening = listed.Editing.Open(reverted.Item, EditOrigin.Grid);
            await Task.Delay(300);
            Assert.IsFalse(opening.IsCompleted, "Edit still reads the held original");
            undoing = listed.Notices.Undo(listed.Notices.Shown.Single());
            Assert.IsTrue(rendering.Wait(TimeSpan.FromSeconds(10)));
        }

        await Task.WhenAny(opening, Task.Delay(1000));
        openedMeanwhile = listed.Editing.IsOpen;
        release.Set();
        await undoing;
        var opened = await opening;

        Assert.IsFalse(openedMeanwhile, "Edit opens once the change that ran during its read has run (G20)");
        Assert.IsTrue(opened.Succeeded, opened.Failure?.Sentence);
        Assert.AreEqual(EditedItems.Cropped, listed.Editing.Session!.Layers, "on the layers the Undo brought back, live (G20)");
    }
}
