using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Revert to original and Save as new item from the menus, headless (G26, G19, G35, R9): Revert at once, nothing opened,
/// its notice raised only once the store has published it, its Undo saving the removed layers again, rendered, an item
/// gone meanwhile refused by name, a refused step named with no notice to undo it; Save as new item making a second
/// item of the layers the store records, its tags, not favorite, and the editor opened on it, a refusal or a failure
/// after the new item's insert named with nothing opened.
/// </summary>
[TestClass]
public sealed class EditingFromMenusTests
{
    [TestMethod]
    public async Task RevertActsAtOnceAndItsNoticeComesOnlyOnceTheStoreHasPublishedIt()
    {
        using var listed = await ListedStash.Open();
        var fixture = MediaFixtures.Path("still.png");
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.BlackBoxAndBox)).Value!;
        await listed.Listing.Refresh();
        SavedRow? rowAtNotice = null;
        listed.Notices.Changed += () => rowAtNotice ??= listed.Notices.Shown.Count > 0 ? EditedItems.Row(listed.Stash, cat.Id) : null;

        var reverted = await listed.Editing.RevertToOriginal(cat);

        Assert.IsTrue(reverted.Succeeded, reverted.Failure?.Sentence);
        Assert.IsFalse(listed.Editing.IsOpen, "from a menu nothing opens (G26)");
        var notice = listed.Notices.Shown.Single();
        Assert.AreEqual("Reverted “cat” to its original.", notice.Text);
        Assert.AreEqual("Undo", notice.UndoLabel);
        Assert.AreEqual("[]", rowAtNotice!.Layers, "R9: raised after the transaction that removed the layers");
        Assert.IsEmpty(rowAtNotice.Steps, "and after the file steps that complete it");
        var item = (await listed.Stash.All()).Single();
        Assert.IsFalse(item.Edited);
        Assert.AreEqual(listed.Stash.Folder.InRoot("cat.png"), item.FilePath);
        CollectionAssert.AreEqual(File.ReadAllBytes(fixture), File.ReadAllBytes(item.FilePath), "one file in the root, the original's bytes (F7)");
        Assert.IsEmpty(Directory.GetFiles(listed.Stash.Folder.InOriginals("")), "nothing left in originals\\");
        Assert.IsFalse(listed.Listing.Items.Single().Edited, "the listing read again");
    }

    [TestMethod]
    public async Task RevertsUndoSavesTheRemovedLayersAgainRendered()
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.BlackBoxAndBox)).Value!;
        var render = File.ReadAllBytes(cat.FilePath);
        await listed.Listing.Refresh();
        await listed.Editing.RevertToOriginal(cat);

        await listed.Notices.Undo(listed.Notices.Shown.Single());

        Assert.IsEmpty(listed.Notices.Shown, "nothing to name");
        var edit = await EditedItems.ForEdit(listed.Stash.Store!, cat.Id);
        Assert.AreEqual(EditedItems.BlackBoxAndBox, edit.Layers, "the removed list back");
        Assert.AreEqual(edit.Layers, edit.RenderedLayers, "and rendered: the picture is the one the list makes, never layers its picture lacks");
        Assert.AreEqual(listed.Stash.Folder.InOriginals("cat.png"), edit.OriginalPath, "the original back in originals\\");
        CollectionAssert.AreEqual(render, File.ReadAllBytes(edit.Item.FilePath), "the render in the root, as the first Save made it");
        Assert.IsTrue(listed.Listing.Items.Single().Edited);
    }

    [TestMethod]
    public async Task RevertsUndoOnAnItemDeletedMeanwhileIsRefusedByName()
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.BlackBoxAndBox)).Value!;
        await listed.Listing.Refresh();
        await listed.Editing.RevertToOriginal(cat);
        await listed.Stash.Store!.Remove([cat.Id]);

        await listed.Notices.Undo(listed.Notices.Shown.Single());

        Assert.AreEqual("Undo Revert to original failed: cat, it is no longer in the stash", listed.Notices.Shown.Single().Text);
    }

    [TestMethod]
    public async Task RevertsUndoWhileTheEditorsSaveRendersLeavesThatSaveRefusedByNameNeverARenderOfARender()
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.Cropped)).Value!;
        await listed.Listing.Refresh();
        await listed.Editing.RevertToOriginal(cat);
        var revert = listed.Notices.Shown.Single();
        await listed.Editing.Open(listed.Listing.Items.Single(), EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((5, 3), (7, 5));
        var live = session.Layers;
        using var paused = new PausedRender(listed.Renderer);
        listed.Renderer = paused.Render;

        // The notices lie above the editor, so the Undo is the user's click while the Save's render runs.
        var saving = listed.Editing.Save();
        await paused.Reached();
        await listed.Notices.Undo(revert);
        paused.Release();
        var saved = await saving;

        Assert.AreEqual(new Failure("Save", "cat", ItemSave.ChangedWhileRendered), saved.Failure, "refused by name before its commit (G35), never published over a picture that is no longer the original");
        Assert.IsTrue(listed.Editing.IsOpen, "the edit kept open on its layers");
        Assert.AreEqual(live, session.Layers);
        var undone = await EditedItems.ForEdit(listed.Stash.Store!, cat.Id);
        Assert.AreEqual(EditedItems.Cropped, undone.Layers, "the item as the Undo left it");
        Assert.AreEqual(undone.Layers, undone.RenderedLayers);
        Assert.AreEqual((4, 3), (undone.Item.Facts.Width, undone.Item.Facts.Height));

        var again = await listed.Editing.Save();

        Assert.IsTrue(again.Succeeded, again.Failure?.Sentence);
        var edit = await EditedItems.ForEdit(listed.Stash.Store!, cat.Id);
        Assert.AreEqual(live, edit.Layers);
        Assert.AreEqual((8, 6), (edit.Item.Facts.Width, edit.Item.Facts.Height), "the render of the Hide over the whole original (G26)");
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(edit.OriginalPath), "the original untouched (G29)");
    }

    [TestMethod]
    public async Task TheEditorsSaveWhileRevertsUndoRendersLeavesTheUndoRefusedByNameAndTheSaveStanding()
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.Cropped)).Value!;
        await listed.Listing.Refresh();
        await listed.Editing.RevertToOriginal(cat);
        var revert = listed.Notices.Shown.Single();
        await listed.Editing.Open(listed.Listing.Items.Single(), EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((5, 3), (7, 5));
        var live = session.Layers;
        using var paused = new PausedRender(listed.Renderer);
        listed.Renderer = paused.Render;

        var undoing = listed.Notices.Undo(revert);
        await paused.Reached();
        var saved = await listed.Editing.Save();
        paused.Release();
        await undoing;

        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        Assert.AreEqual(new Failure("Save", "cat", ItemSave.ChangedWhileRendered).Sentence, listed.Notices.Shown.Single().Text, "the Undo's save refused by name (G35)");
        var edit = await EditedItems.ForEdit(listed.Stash.Store!, cat.Id);
        Assert.AreEqual(live, edit.Layers, "the editor's Save stands");
        Assert.AreEqual(edit.Layers, edit.RenderedLayers);
        Assert.AreEqual((8, 6), (edit.Item.Facts.Width, edit.Item.Facts.Height), "its render, never the Undo's crop drawn over it");
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(edit.OriginalPath), "the original untouched (G29)");
        Assert.IsEmpty(Directory.GetFiles(listed.Stash.Folder.Work), "the refused render discarded");
    }

    /// <summary>The window's renderer held at its first call until released, as a slow render is; later calls run at once.</summary>
    private sealed class PausedRender(FinishedPictureRenderer inner) : IDisposable
    {
        private readonly TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim release = new();
        private int calls;

        public Outcome<MediaRender> Render(string original, string name, LayerList layers, ImageFormat format, string work, TimeSpan retry)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                reached.SetResult();
                Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10)), "the paused render was released");
            }

            return inner(original, name, layers, format, work, retry);
        }

        public Task Reached() => reached.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Release() => release.Set();

        public void Dispose()
        {
            release.Set();
            release.Dispose();
        }
    }

    [TestMethod]
    public async Task ARevertWhoseOriginalIsHeldIsNamedWithNoNoticeToUndoIt()
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.BlackBoxAndBox)).Value!;
        await listed.Listing.Refresh();
        Outcome reverted;
        using (TestStash.Hold(listed.Stash.Folder.InOriginals("cat.png")))
        {
            reverted = await listed.Editing.RevertToOriginal(cat);
        }

        Assert.IsFalse(reverted.Succeeded);
        Assert.StartsWith("Revert to original failed: cat, ", reverted.Failure!.Sentence);
        Assert.AreEqual(reverted.Failure.Sentence, listed.Notices.Shown.Single().Text, "named, and no notice says it was reverted (G35, R9)");
        Assert.IsNull(listed.Notices.Shown.Single().UndoLabel);
        var item = listed.Listing.Items.Single();
        Assert.AreEqual(cat.FilePath, item.FilePath, "listed on the picture on disk, the render, until the step is done (State: A live failure)");
        Assert.IsEmpty(listed.Clipboard.WrittenAgain, "no clipboard refresh after a failure after the commit (State: A live failure)");
    }

    [TestMethod]
    public async Task SaveAsNewItemFromAMenuMakesASecondItemOfTheRecordedLayersAndOpensTheEditorOnIt()
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.BlackBoxAndBox)).Value!;
        listed.Stash.Tag(cat.Id, "funny");
        listed.Stash.SqlWhileOpen("UPDATE items SET favorite = 1 WHERE id = $id", ("$id", cat.Id));
        await listed.Listing.Refresh();
        var before = EditedItems.Row(listed.Stash, cat.Id);
        var continued = new List<StashItem>();
        listed.Editing.Continued += continued.Add;

        var made = await listed.Editing.SaveAsNewItem(listed.Listing.Items.Single(), EditOrigin.Preview);

        Assert.IsTrue(made.Succeeded, made.Failure?.Sentence);
        var copy = (await listed.Stash.All()).Single(item => item.Id != cat.Id);
        Assert.AreEqual("cat 2", copy.Name, "named with a counter (G26)");
        CollectionAssert.AreEqual(new[] { "funny" }, copy.Tags.ToList(), "the same tags");
        Assert.IsFalse(copy.Favorite, "not favorite");
        Assert.IsTrue(copy.Edited);
        Assert.AreEqual(EditedItems.BlackBoxAndBox, (await EditedItems.ForEdit(listed.Stash.Store!, copy.Id)).Layers, "the layers the store records");
        Assert.AreEqual(before, EditedItems.Row(listed.Stash, cat.Id), "the item it was made from left as it was");
        Assert.AreEqual(copy.Id, listed.Editing.Session?.Item.Id, "the editor open on the new item");
        Assert.AreEqual(EditedItems.BlackBoxAndBox, listed.Editing.Session!.Layers, "with its layers live");
        Assert.AreEqual(EditOrigin.Preview, listed.Editing.Origin);
        Assert.AreEqual(copy.Id, continued.Single().Id, "a preview beneath moves to it");
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task SaveAsNewItemFromAMenuRendersTheRecordedListNotTheOpenSessions()
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.BlackBoxAndBox)).Value!;
        await listed.Listing.Refresh();
        await listed.Editing.Open(cat, EditOrigin.Grid);
        listed.Editing.Session!.RevertToOriginal();

        await listed.Editing.SaveAsNewItem(cat, EditOrigin.Grid);

        var copy = (await listed.Stash.All()).Single(item => item.Name == "cat 2");
        Assert.AreEqual(EditedItems.BlackBoxAndBox, (await EditedItems.ForEdit(listed.Stash.Store!, copy.Id)).Layers, "the committed list, whatever an edit open on the item shows");
        Assert.IsTrue(copy.Edited);
    }

    [TestMethod]
    public async Task SaveAsNewItemWhoseRenderIsRefusedIsNamedAddsNothingAndOpensNothing()
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.BlackBoxAndBox)).Value!;
        await listed.Listing.Refresh();
        listed.Renderer = EditedItems.Refusing;

        var made = await listed.Editing.SaveAsNewItem(cat, EditOrigin.Grid);

        Assert.AreEqual(new Failure("Save the picture", "cat", EditedItems.RefusedReason), made.Failure);
        Assert.AreEqual(made.Failure!.Sentence, listed.Notices.Shown.Single().Text);
        Assert.HasCount(1, await listed.Stash.All(), "nothing added");
        Assert.IsFalse(listed.Editing.IsOpen, "nothing opened");
        Assert.IsNull(listed.Editing.Busy, "the busy state over");
    }

    [TestMethod]
    public async Task SaveAsNewItemWhoseNewFileCannotBePlacedIsNamedAndOpensNothing()
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.BlackBoxAndBox)).Value!;
        await listed.Listing.Refresh();
        var holds = new List<FileStream>();
        listed.Renderer = EditedItems.Holding(holds);
        try
        {
            var made = await listed.Editing.SaveAsNewItem(cat, EditOrigin.Grid);

            Assert.IsFalse(made.Succeeded);
            Assert.AreEqual(made.Failure!.Sentence, listed.Notices.Shown.Single().Text, "named (G35)");
            Assert.IsFalse(listed.Editing.IsOpen, "nothing opened on an item the stash does not list");
            Assert.AreEqual(cat.Id, (await listed.Stash.All()).Single().Id, "the new row unlisted until the next start finishes or undoes it");
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }
    }
}
