using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The clipboard refresh after a Save, headless over the recording clipboard (G26, How G26 holds, G10, G35, State: A
/// live failure): after a published Save of the item ImageStash's latest write put there, the same items written again
/// as the handoff would write them now, any deleted since left out, under the handoff's one-write-at-a-time gate, with
/// a busy state naming it and no use recorded; nothing asked after a failure after the commit, a Cancel, or a latest
/// write of other items; a changed clipboard saying nothing, and a refused write and a stash that cannot be read named
/// as the refresh's, writing nothing. Also after Revert to original
/// from a menu and its Undo, which publish a Save of the item.
/// </summary>
[TestClass]
public sealed class HandoffRefreshTests
{
    [TestMethod]
    public async Task ASaveOfTheItemOnTheClipboardWritesItAgainAsTheHandoffWouldNowAndRecordsNoUse()
    {
        using var listed = await ListedStash.Open();
        var cat = await Listed(listed, "cat", "transparent.png");
        await listed.Handoff.Copy(null!, [cat]);
        var uses = listed.Uses();
        var busy = new List<string?>();
        listed.Handoff.Changed += () => busy.Add(listed.Handoff.Busy);

        var saved = await SaveABlackBox(listed, cat);

        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        var (savedItem, content, _) = listed.Clipboard.WrittenAgain.Single();
        var now = listed.Listing.Items.Single();
        var still = ClipboardStill.Read(now.FilePath, "cat", TestStash.ShortRetry).Value!;
        Assert.AreEqual(cat.Id, savedItem);
        Assert.AreEqual(1, now.RenderedRevision, "asked once the Save was published");
        CollectionAssert.AreEqual(new[] { now.FilePath }, content.FilePaths.ToList(), "the finished picture as it is now (G26)");
        CollectionAssert.AreEqual(still.Png, content.Png, "a still's PNG as the handoff reads it");
        CollectionAssert.AreEqual(still.Frame.Pixels, content.Bitmap!.Pixels, "and its bitmap, the black box in it");
        CollectionAssert.AreEqual(new[] { cat.Id }, content.Items.ToList());
        Assert.AreEqual(uses, listed.Uses(), "G10: the refresh records no use");
        CollectionAssert.Contains(busy, "Updating cat on the clipboard…", "G35: its busy state names it while it runs");
        Assert.IsNull(listed.Handoff.Busy);
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task AClipboardChangedSinceSaysNothingAndARefusedWriteIsNamedWithTheSaveStanding()
    {
        using var listed = await ListedStash.Open();
        var cat = await Listed(listed, "cat", "still.png");
        await listed.Handoff.Copy(null!, [cat]);
        listed.Clipboard.WritesAgain = Outcome<bool>.Of(false);

        await SaveABlackBox(listed, cat);

        Assert.HasCount(1, listed.Clipboard.WrittenAgain);
        Assert.IsEmpty(listed.Notices.Shown, "a changed clipboard is no failure (How G26 holds)");

        await listed.Handoff.Copy(null!, [listed.Listing.Items.Single()]);
        listed.Clipboard.WritesAgain = Outcome<bool>.Fail(RecordingClipboard.Held);
        var saved = await SaveABlackBox(listed, listed.Listing.Items.Single());

        Assert.IsTrue(saved.Succeeded, "the Save stands");
        Assert.AreEqual(2, listed.Listing.Items.Single().RenderedRevision);
        Assert.AreEqual("Update the clipboard failed: cat, another program is holding the clipboard open", listed.Notices.Shown.Single().Text, "G35");
    }

    [TestMethod]
    public async Task NothingIsWrittenAgainWhenTheLatestWriteWasOfAnotherItem()
    {
        using var listed = await ListedStash.Open();
        var cat = await Listed(listed, "cat", "still.png");
        var dog = await Listed(listed, "dog", "transparent.png");
        await listed.Handoff.Copy(null!, [dog]);

        await SaveABlackBox(listed, cat);

        Assert.IsEmpty(listed.Clipboard.WrittenAgain, "the clipboard holds dog, not the item saved");
    }

    [TestMethod]
    public async Task TheSameItemsAreWrittenAgainLeavingOutOneDeletedSince()
    {
        using var listed = await ListedStash.Open();
        var cat = await Listed(listed, "cat", "still.png");
        var dog = await Listed(listed, "dog", "transparent.png");
        var fish = await Listed(listed, "fish", "photo.jpg");
        await listed.Handoff.Copy(null!, [cat, dog, fish]);
        await listed.Stash.Store!.Remove([dog.Id]);
        await listed.Listing.Refresh();

        await SaveABlackBox(listed, cat);

        var content = listed.Clipboard.WrittenAgain.Single().Content;
        var items = listed.Listing.Items.ToDictionary(item => item.Id);
        CollectionAssert.AreEqual(new[] { items[cat.Id].FilePath, items[fish.Id].FilePath }, content.FilePaths.ToList(), "cat as saved and fish, in the order written; dog left out");
        CollectionAssert.AreEqual(new[] { cat.Id, fish.Id }, content.Items.ToList());
        Assert.IsNull(content.Png, "two items go as files only (G8)");
    }

    [TestMethod]
    public async Task AStashThatCannotBeReadStopsTheRefreshNamedAndLeavesTheClipboardAsItWas()
    {
        using var listed = await ListedStash.Open();
        var cat = await Listed(listed, "cat", "still.png");
        var dog = await Listed(listed, "dog", "transparent.png");
        await listed.Handoff.Copy(null!, [cat, dog]);
        await SaveABlackBox(listed, cat);
        var saved = listed.Listing.Items.Single(item => item.Id == cat.Id);

        Outcome refreshed;
        using (listed.Stash.HoldStore())
        {
            refreshed = await listed.Handoff.RefreshAfterSave(saved);
        }

        Assert.IsFalse(refreshed.Succeeded);
        Assert.HasCount(1, listed.Clipboard.WrittenAgain, "only the Save's own refresh: no item the stash could not confirm is dropped from the clipboard");
        Assert.AreEqual(
            $"Update the clipboard failed: cat, the stash could not be read ({StashFolder.StoreFileName}: {HeldFileRetry.HeldReason})",
            listed.Notices.Shown.Single().Text,
            "G35: the refresh is the action that failed, so the clipboard is known to hold the picture as it was");
    }

    [TestMethod]
    public async Task NoRefreshAfterAFailureAfterTheCommitOrACancel()
    {
        using var listed = await ListedStash.Open();
        var cat = await Listed(listed, "cat", "still.png");
        await listed.Handoff.Copy(null!, [cat]);
        var holds = new List<FileStream>();
        listed.Renderer = EditedItems.Holding(holds);
        try
        {
            var failed = await SaveABlackBox(listed, cat);
            Assert.IsFalse(failed.Succeeded, "the render's place refused after the commit");
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }

        await listed.Editing.Open(listed.Listing.Items.Single(), EditOrigin.Grid);
        listed.Editing.Cancel();

        Assert.IsEmpty(listed.Clipboard.WrittenAgain, "State: A live failure: no clipboard refresh; and a Cancel saves nothing");
    }

    [TestMethod]
    public async Task ACopyRunningWhenTheSaveIsPublishedWritesFirstAndTheRefreshAfterIt()
    {
        using var listed = await ListedStash.Open();
        var cat = await Listed(listed, "cat", "still.png");
        await listed.Handoff.Copy(null!, [cat]);
        await OpenWithABlackBox(listed, cat);
        var hold = new TaskCompletionSource();
        listed.Clipboard.Hold = hold.Task;
        var copying = listed.Handoff.Copy(null!, [cat]);

        var saving = listed.Editing.Save();
        await EditorSessions.Until(() => listed.Listing.Items.Single().RenderedRevision == 1 && !listed.Editing.IsOpen, "the Save was never published");
        await Task.Delay(100);
        Assert.IsEmpty(listed.Clipboard.WrittenAgain, "the refresh waits behind the copy's write");
        hold.SetResult();
        await copying;
        await saving;

        Assert.AreEqual(2, listed.Clipboard.WrittenAgain.Single().WrittenBefore, "never inside another write, never between a write and its paste (G8)");
    }

    [TestMethod]
    public async Task RevertToOriginalFromAMenuAndItsUndoReplaceTheClipboardsCopyToo()
    {
        using var listed = await ListedStash.Open();
        var cat = (await listed.Stash.SaveEdit(await listed.Stash.ImportFixture("cat", "still.png"), EditedItems.BlackBoxAndBox)).Value!;
        await listed.Listing.Refresh();
        await listed.Handoff.Copy(null!, [cat]);

        await listed.Editing.RevertToOriginal(cat);
        await listed.Notices.Undo(listed.Notices.Shown.Single());

        var (reverted, undone) = (listed.Clipboard.WrittenAgain[0].Content, listed.Clipboard.WrittenAgain[1].Content);
        Assert.HasCount(2, listed.Clipboard.WrittenAgain);
        CollectionAssert.AreEqual(File.ReadAllBytes(MediaFixtures.Path("still.png")), reverted.Png, "after the Revert, the original itself");
        CollectionAssert.AreEqual(File.ReadAllBytes(listed.Listing.Items.Single().FilePath), undone.Png, "after its Undo, the render again");
    }

    [TestMethod]
    public async Task AfterAnImportTheClipboardKeepsItsContentAndNoSaveRewritesItFromTheReplacedStashsWriteUntilTheImportedItemIsCopied()
    {
        using var listed = await ListedStash.Open();
        var cat = await Listed(listed, "cat", "still.png");
        await listed.Handoff.Copy(null!, [cat]);
        var written = listed.Clipboard.Written.Count;
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "one");

        Assert.IsTrue((await listed.ImportStash(archive)).Succeeded);
        await listed.Listing.ShowScope(Scope.All);
        var one = listed.Listing.Items.Single();
        Assert.AreEqual(cat.Id, one.Id, "the imported store gives the replaced item's identity to another");
        Assert.HasCount(written, listed.Clipboard.Written, "the clipboard's content left as it is");

        Assert.IsTrue((await SaveABlackBox(listed, one)).Succeeded);
        Assert.IsEmpty(listed.Clipboard.WrittenAgain, "its Save writes no picture ImageStash never put there (G26)");

        one = listed.Listing.Items.Single();
        await listed.Handoff.Copy(null!, [one]);
        Assert.IsTrue((await SaveABlackBox(listed, one)).Succeeded);
        Assert.AreEqual(one.Id, listed.Clipboard.WrittenAgain.Single().SavedItem, "once it is copied, its Save refreshes the clipboard as usual");
    }

    private static async Task<StashItem> Listed(ListedStash listed, string name, string fixture)
    {
        var item = await listed.Stash.ImportFixture(name, fixture);
        await listed.Listing.Refresh();
        return listed.Listing.Items.Single(listedItem => listedItem.Id == item.Id);
    }

    private static async Task OpenWithABlackBox(ListedStash listed, StashItem item)
    {
        await listed.Editing.Open(item, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.Hide().Choose(HideKind.Black);
        session.DragThrough((2, 2), (5, 4));
    }

    private static async Task<Outcome> SaveABlackBox(ListedStash listed, StashItem item)
    {
        await OpenWithABlackBox(listed, item);
        return await listed.Editing.Save();
    }
}
