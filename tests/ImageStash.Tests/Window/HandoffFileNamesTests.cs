using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// A handoff's file names are its items' until it is done (G8, G9; a file name is never identity): a copy, a paste and a
/// drag-out over a real stash while an import's Undo and a new import of the same name, or the finish of a waiting
/// rename, run on the store's thread between the store's confirmation and the write, between the write and the
/// keystroke, or during the drag; and a drag of an item whose file name went to another since it was shown.
/// </summary>
[TestClass]
public sealed class HandoffFileNamesTests
{
    [TestMethod]
    public async Task ANewItemThatWouldTakeTheFileNameOfAnItemBeingCopiedNeverGoesInItsPlace()
    {
        using var handing = await Handing.Open("transparent.png");
        var selected = handing.Items.Single();
        var bytes = MediaFixtures.Bytes("still.png");
        var candidate = TestStash.Candidate(await handing.Stash.Stage("replacement.png", bytes), selected.Name, bytes);
        // The still's read waits on its file, which a scanner holds without letting it be read (E3), retrying every
        // 100 ms, so the store has confirmed the item and the write has not begun while the import's Undo and a new
        // import of its name run on the store's thread, back to back behind the confirmation.
        var handoff = new Handoff(handing.Listing, handing.Notices, handing.Notifications, handing.Clipboard, handing.Target, handing.Stash.Clock, TimeSpan.FromSeconds(10), TestStash.ShortRetry);
        Task<Outcome> copying;
        StashItem replacement;
        using (new FileStream(selected.FilePath, FileMode.Open, FileAccess.Read, FileShare.Delete))
        {
            copying = handoff.Copy(null!, [selected]);
            Assert.IsFalse(copying.IsCompleted, "its confirmation is queued on the store's thread before the removal");
            // A moment before the file leaves, so the read has met the held file first and waits its spacing out.
            handing.Stash.RecycleBin.BeforeEach = _ => Thread.Sleep(40);
            var removing = handing.Stash.Store!.Remove([selected.Id]);
            var importing = handing.Stash.Store!.CommitImport(candidate);
            Assert.IsTrue((await removing).Single().Succeeded);
            replacement = (await importing).Value!.Item;
            Assert.AreEqual(0, handing.Clipboard.Begun);
        }

        var copied = await copying;

        var written = handing.Clipboard.Written.Select(write => write.Content).ToList();
        Assert.IsFalse(written.Any(content => content.FilePaths.Contains(replacement.FilePath) || content.Png is { } png && png.SequenceEqual(bytes)),
            "nothing of the new item goes in the copied one's place (G8): " + string.Join(", ", written.SelectMany(content => content.FilePaths)));
        Assert.IsFalse(copied.Succeeded, "the copied item is no longer in the stash");
        StringAssert.Contains(copied.Failure!.Sentence, selected.Name);
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    public async Task ANewItemThatWouldTakeTheFileNameOfAnItemBeingPastedIsNeverWhatThePasteHandsOver()
    {
        using var handing = await Handing.Open("animated.gif");
        var selected = handing.Items.Single();
        StashItem? replacement = null;
        // On the clipboard and not yet pasted: the import's Undo takes the item, and a new file comes in under its file name.
        handing.Clipboard.AtWrite = async () =>
        {
            Assert.IsTrue((await handing.Stash.Store!.Remove([selected.Id])).Single().Succeeded);
            var source = handing.Stash.Source(selected.FileName, MediaFixtures.Bytes("still.gif"));
            replacement = (await handing.Import.Drop(new DropContents([Handing.File(source)], false, false, []))).Single().Item!;
        };

        await handing.Paste([selected], RecordingTarget.Relay);

        Assert.Contains("activate and paste Relay", handing.Steps);
        var pastedPath = handing.Clipboard.Written.Single().Content.FilePaths.Single();
        Assert.AreNotEqual(replacement!.FilePath, pastedPath, "the keystroke never pastes a new item under the pasted one's file name (G8)");
    }

    [TestMethod]
    public async Task AnItemTheStoreSavedHoldsItsRendersNameWhileItIsPastedNotItsOriginals()
    {
        using var handing = await Handing.Open("still.webp");
        var saved = (await handing.Stash.SaveEdit(handing.Items.Single(), EditedItems.BlackBoxAndBox)).Value!;
        var (png, webp) = ("", "");
        // On the clipboard and not yet pasted: the import's Undo takes the item, and new items come in under its name.
        handing.Clipboard.AtWrite = async () =>
        {
            Assert.IsTrue((await handing.Stash.Store!.Remove([saved.Id])).Single().Succeeded);
            png = (await handing.Stash.Import("still", extension: ".png")).FileName;
            webp = (await handing.Stash.Import("still", extension: ".webp")).FileName;
        };

        await handing.Paste([saved], RecordingTarget.Relay);

        Assert.AreEqual(handing.Stash.Folder.InRoot("still.png"), handing.Clipboard.Written.Single().Content.FilePaths.Single(), "the render is handed over (G8)");
        Assert.AreEqual(("still 2.png", "still.webp"), (png, webp), "the render's name is in hand until the paste is done; the original's, never handed over, is free");
    }

    [TestMethod]
    public async Task ANewItemThatWouldTakeTheFileNameOfAnItemBeingDraggedIsNeverWhatTheDropHandsOver()
    {
        using var handing = await Handing.Open("animated.gif");
        var selected = handing.Items.Single();
        StashItem? replacement = null;
        // While the drag runs: the import's Undo, queued before it, takes the item, and a new file comes in under its file name.
        handing.Clipboard.DuringDrag = () =>
        {
            Assert.IsTrue(handing.Stash.Store!.Remove([selected.Id]).Result.Single().Succeeded);
            var source = handing.Stash.Source(selected.FileName, MediaFixtures.Bytes("still.gif"));
            replacement = handing.Import.Drop(new DropContents([Handing.File(source)], false, false, [])).Result.Single().Item!;
        };

        await handing.Handoff.DragOut([selected], handing.Drag, _ => { }, () => { });

        Assert.AreNotEqual(replacement!.FilePath, handing.Clipboard.Dragged.Single().Single(), "the drop never hands over a new item under the dragged one's file name (G9)");
    }

    [TestMethod]
    public async Task ADragOfAnItemWhoseFileNameWentToANewItemSinceItWasShownIsNamedAndDragsNothing()
    {
        using var handing = await Handing.Open("animated.gif");
        var shown = handing.Items.Single();
        Assert.IsTrue((await handing.Stash.Store!.Remove([shown.Id])).Single().Succeeded);
        var source = handing.Stash.Source(shown.FileName, MediaFixtures.Bytes("still.gif"));
        var replacement = (await handing.Import.Drop(new DropContents([Handing.File(source)], false, false, []))).Single().Item!;
        Assert.AreEqual(shown.FilePath, replacement.FilePath);
        foreach (var notice in handing.Notices.Shown.ToList())
        {
            handing.Notices.Close(notice);
        }

        var dragging = 0;
        var dragged = await handing.Handoff.DragOut([shown], handing.Drag, _ => dragging++, () => dragging++);

        Assert.AreEqual("Drag out failed: animated, it changed in the stash since it was shown", dragged.Failure?.Sentence);
        Assert.AreEqual(dragged.Failure!.Sentence, handing.Notices.Shown.Single().Text);
        Assert.IsEmpty(handing.Clipboard.Dragged, "nothing dragged (G9)");
        Assert.AreEqual(0, dragging);
        CollectionAssert.AreEqual(new[] { replacement.Id }, handing.Listing.Items.Select(item => item.Id).ToList(), "the grid shows the stash as it is");
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task AnItemWhoseRenameWaitsIsHandedOverUnderTheFileItsPathNamesAndNoNewItemComesUnderIt(bool paste, bool storeHeld)
    {
        using var handing = await Handing.Open("animated.gif");
        var original = handing.Items.Single();
        var store = handing.Stash.Store!;
        // Its file held as it is renamed, the rename's step waits: listed under its new name, its file where it was.
        using (TestStash.Hold(original.FilePath))
        {
            Assert.IsFalse((await store.Rename(original, "renamed")).Succeeded);
        }

        var waiting = (await store.Item(original.Id)).Value!;
        Assert.AreEqual(("renamed.gif", original.FilePath), (waiting.FileName, waiting.FilePath));
        StashItem? replacement = null;
        using var heldStore = storeHeld ? handing.Stash.HoldStore() : null;
        // Written: a rename finishes the step, which frees the old name, and a new file comes in under it.
        handing.Clipboard.AtWrite = async () =>
        {
            heldStore?.Dispose();
            Assert.IsTrue((await store.Rename(waiting, "renamed")).Succeeded);
            var source = handing.Stash.Source(original.FileName, MediaFixtures.Bytes("still.gif"));
            replacement = (await handing.Import.Drop(new DropContents([Handing.File(source)], false, false, []))).Single().Item!;
        };

        var handedOver = paste ? await handing.Paste([waiting], RecordingTarget.Relay) : await handing.Copy([waiting]);

        Assert.IsTrue(handedOver.Succeeded, handedOver.Failure?.Sentence);
        Assert.AreNotEqual(replacement!.FilePath, handing.Clipboard.Written.Single().Content.FilePaths.Single(), "the path handed over never names the new item's file (G8)");
    }

    [TestMethod]
    public async Task ADragOfAnItemWhoseRenameWaitsHoldsTheFileItsPathNames()
    {
        using var handing = await Handing.Open("animated.gif");
        var original = handing.Items.Single();
        var store = handing.Stash.Store!;
        using (TestStash.Hold(original.FilePath))
        {
            Assert.IsFalse((await store.Rename(original, "renamed")).Succeeded);
        }

        var waiting = (await store.Item(original.Id)).Value!;
        StashItem? replacement = null;
        handing.Clipboard.DuringDrag = () =>
        {
            Assert.IsTrue(store.Rename(waiting, "renamed").Result.Succeeded);
            var source = handing.Stash.Source(original.FileName, MediaFixtures.Bytes("still.gif"));
            replacement = handing.Import.Drop(new DropContents([Handing.File(source)], false, false, [])).Result.Single().Item!;
        };

        await handing.Handoff.DragOut([waiting], handing.Drag, _ => { }, () => { });

        Assert.AreNotEqual(replacement!.FilePath, handing.Clipboard.Dragged.Single().Single(), "the drop never hands over the new item's file (G9)");
    }
}
