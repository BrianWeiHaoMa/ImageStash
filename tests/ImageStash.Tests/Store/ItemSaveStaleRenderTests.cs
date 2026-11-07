using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditedItems;

namespace ImageStash.Tests.Store;

/// <summary>
/// A render that no longer goes with its item, at Save and Save as new item (Approach: a finished picture is a render of
/// the original; G26, G35): one decoded from any file but the item's original is refused by name and discarded, whether
/// it read another Save's render, even when a Revert has since put the original back at the path it read, or another
/// item's picture renamed onto that path; one decoded from the original is saved, whatever was renamed, committed or
/// finished meanwhile, or made fresh over a change still pending.
/// </summary>
[TestClass]
public sealed class ItemSaveStaleRenderTests
{
    [TestMethod]
    public async Task TwoRevertNoticesAndAMenuRevertMustNotPublishARenderOfARender()
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), Arrow)).Value!;
        await listed.Listing.Refresh();
        Assert.IsTrue((await listed.Editing.RevertToOriginal(cat)).Succeeded);
        var first = listed.Notices.Shown.Single();
        cat = (await listed.Stash.SaveEdit(listed.Listing.Items.Single(), Cropped)).Value!;
        await listed.Listing.Refresh();
        Assert.IsTrue((await listed.Editing.RevertToOriginal(cat)).Succeeded);
        var second = listed.Notices.Shown.Single(n => n != first);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowRead = new ManualResetEventSlim();
        using var allowReturn = new ManualResetEventSlim();
        var calls = 0;
        listed.Renderer = (original, name, layers, format, work, retry) =>
        {
            if (Interlocked.Increment(ref calls) != 1)
                return FinishedPicture.Render(original, name, layers, format, work, retry);
            entered.SetResult();
            if (!allowRead.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException();
            var rendered = FinishedPicture.Render(original, name, layers, format, work, retry);
            read.SetResult();
            if (!allowReturn.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException();
            return rendered;
        };
        var undoing = listed.Notices.Undo(first);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await listed.Notices.Undo(second);
            Assert.AreEqual(4, listed.Listing.Items.Single().Facts.Width);
            allowRead.Set();
            await read.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.IsTrue((await listed.Editing.RevertToOriginal(listed.Listing.Items.Single())).Succeeded);
        }
        finally
        {
            allowRead.Set();
            allowReturn.Set();
        }
        await undoing;
        CollectionAssert.Contains(listed.Notices.Shown.Select(notice => notice.Text).ToList(), new Failure("Save", "cat", ItemSave.ChangedWhileRendered).Sentence, "the first Undo refused by name (G35), its render read from the other Undo's");
        var edit = (await listed.Stash.Store!.ForEdit(cat.Id)).Value!;
        Assert.IsTrue(edit.Layers.IsEmpty, "the item as the menu's Revert, the last change, left it");
        Assert.AreEqual(listed.Stash.Folder.InRoot("cat.png"), edit.Item.FilePath);
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(edit.Item.FilePath), "one file, the original's bytes, never a render of a render");
        Assert.IsEmpty(Directory.GetFiles(listed.Stash.Folder.Originals));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RenameAfterValidRenderShouldStillSave(bool asNew)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var render = await stash.Render(cat, Arrow);
        var renamed = await store.Rename(cat, "dog");
        Assert.IsTrue(renamed.Succeeded);
        var saved = asNew ? await store.SaveAsNewItem(cat, Arrow, render) : await store.Save(cat, Arrow, render);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AReusedPathMustNotAdmitARenderOfARender(bool asNew)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var edit = (await store.ForEdit(cat.Id)).Value!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowRead = new ManualResetEventSlim();
        using var allowReturn = new ManualResetEventSlim();
        Outcome<MediaRender> Renderer(string original, string name, LayerList layers, ImageFormat format, string work, TimeSpan retry)
        {
            entered.SetResult();
            if (!allowRead.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException();
            var rendered = FinishedPicture.Render(original, name, layers, format, work, retry);
            read.SetResult();
            if (!allowReturn.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException();
            return rendered;
        }

        var saving = asNew
            ? SavedEdit.SaveAsNew(store, cat, edit, Arrow, Renderer, TestStash.ShortRetry)
            : SavedEdit.Save(store, cat, edit, Arrow, Renderer, TestStash.ShortRetry);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var other = await stash.SaveEdit(cat, Cropped);
            Assert.IsTrue(other.Succeeded, other.Failure?.Sentence);
            allowRead.Set();
            await read.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var reverted = await store.Save(other.Value!, LayerList.Empty, null);
            Assert.IsTrue(reverted.Succeeded, reverted.Failure?.Sentence);
        }
        finally
        {
            allowRead.Set();
            allowReturn.Set();
        }

        var saved = await saving;
        Assert.AreEqual(new Failure(asNew ? "Save as new item" : "Save", "cat", ItemSave.ChangedWhileRendered), saved.Failure, "refused by name, its render read from the other Save's although the original is back where it read");
        var items = (await store.All()).Value!;
        Assert.HasCount(1, items, "no item made of it");
        Assert.IsFalse(items.Single().Edited, "the item as the Revert left it");
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(items.Single().FilePath));
        Assert.IsEmpty(stash.WorkFiles(), "the refused render discarded");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ARenderOfAnotherItemsPictureRenamedOntoTheOriginalsPathIsRefused(bool asNew)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var other = await stash.ImportPicture("other", "other.png", MediaFixtures.DistinctPng(91, 12, 10));
        other = (await stash.SaveEdit(other, Cropped)).Value!;
        var edit = (await store.ForEdit(cat.Id)).Value!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        // Held before it opens the original, the few milliseconds between Edit's read and the render's open.
        Outcome<MediaRender> Renderer(string original, string name, LayerList layers, ImageFormat format, string work, TimeSpan retry)
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException();
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        }

        var saving = asNew
            ? SavedEdit.SaveAsNew(store, cat, edit, Arrow, Renderer, TestStash.ShortRetry)
            : SavedEdit.Save(store, cat, edit, Arrow, Renderer, TestStash.ShortRetry);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.IsTrue((await store.Rename(cat, "moved")).Succeeded);
            Assert.IsTrue((await store.Rename(other, "cat")).Succeeded);
            Assert.AreEqual(stash.Folder.InRoot("cat.png"), edit.OriginalPath, "the path the render opens now holds the other item's render");
        }
        finally
        {
            release.Set();
        }

        var saved = await saving;
        Assert.AreEqual(new Failure(asNew ? "Save as new item" : "Save", "moved", ItemSave.ChangedWhileRendered), saved.Failure, "refused by name before any commit");
        Assert.IsNull(saved.Value);
        var items = (await store.All()).Value!.OrderBy(item => item.Id).ToList();
        Assert.HasCount(2, items, "no item made of it");
        Assert.AreEqual(("moved", false, 8, 6, stash.Folder.InRoot("moved.png")), (items[0].Name, items[0].Edited, items[0].Facts.Width, items[0].Facts.Height, items[0].FilePath), "the item as the rename left it");
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(items[0].FilePath), "its one file, its original");
        Assert.AreEqual(("cat", true, 4, 3, stash.Folder.InRoot("cat.png")), (items[1].Name, items[1].Edited, items[1].Facts.Width, items[1].Facts.Height, items[1].FilePath), "the other item untouched");
        Assert.AreEqual((4, 3), (MediaFixtures.FirstFrame(items[1].FilePath).Width, MediaFixtures.FirstFrame(items[1].FilePath).Height));
        Assert.IsEmpty(stash.WorkFiles(), "the refused render discarded");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ARenderOfTheOriginalMadeBeforeAPendingSaveMovedItIsSaved(bool asNew)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await FirstSaveCommitted(stash);
        var render = await stash.Render(cat, Arrow);
        var revision = Row(stash, cat.Id).LayerRevision;
        // A plain copy finishes the pending Save, moving the original into originals\, and raises no revision.
        Assert.IsTrue((await store.SaveAsNewItem(cat, LayerList.Empty, null)).Succeeded);
        Assert.AreEqual((revision, true), (Row(stash, cat.Id).LayerRevision, Row(stash, cat.Id).Steps.Count == 0));
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(stash.Folder.InOriginals("cat.png")), "the original moved, its bytes unchanged");

        var saved = asNew ? await store.SaveAsNewItem(cat, Arrow, render) : await store.Save(cat, Arrow, render);

        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        Assert.AreEqual((8, 6), (saved.Value!.Facts.Width, saved.Value.Facts.Height));
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(stash.Folder.InOriginals("cat.png")), "the original untouched (G29)");
    }

    [TestMethod]
    [DataRow("save-before-move", false)]
    [DataRow("save-before-move", true)]
    [DataRow("save-after-move", false)]
    [DataRow("save-after-move", true)]
    [DataRow("revert", false)]
    [DataRow("revert", true)]
    [DataRow("rename", false)]
    [DataRow("rename", true)]
    public async Task FreshRenderOverPendingChangeShouldSave(string pending, bool asNew)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        StashItem cat;
        if (pending == "save-before-move") cat = await FirstSaveCommitted(stash);
        else if (pending == "save-after-move") cat = await FirstSaveMoved(stash);
        else
        {
            cat = (await stash.SaveEdit(await stash.ImportFixture("cat", "still.png"), Cropped)).Value!;
            using (TestStash.Hold(cat.FilePath))
            {
                var failed = pending == "revert" ? await store.Save(cat, LayerList.Empty, null) : await store.Rename(cat, "dog");
                Assert.IsFalse(failed.Succeeded);
            }
        }
        var render = await stash.Render(cat, Arrow);
        var saved = asNew ? await store.SaveAsNewItem(cat, Arrow, render) : await store.Save(cat, Arrow, render);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        Assert.AreEqual(8, saved.Value!.Facts.Width);
    }
}
