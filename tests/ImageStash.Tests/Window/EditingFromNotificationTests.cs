using ImageStash.Editor;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// A capture notification's Edit and Trim in the window, headless (G16, G20, G26, G35, G28, State): the editor on the
/// card's own item over All, Trim's tool selected before the editor first shows where the item lists it, Save leaving
/// the item selected; another item's edit left as Cancel would; an item gone refused by name; and the card's Undo while
/// that edit is open closing it without a notice, a Save still rendering abandoned and one already committed finished
/// before the removal takes both files.
/// </summary>
[TestClass]
public sealed class EditingFromNotificationTests
{
    [TestMethod]
    public async Task EditOpensTheCardsItemOverAllWithItSelectedAndSaveLeavesItSelectedThere()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        var dog = await listed.Stash.ImportFixture("dog", "transparent.png");
        await listed.Handoff.Copy(null!, [dog]);
        await listed.Listing.ShowScope(Scope.Recent);
        CollectionAssert.AreEqual(new[] { dog.Id }, listed.Listing.Items.Select(item => item.Id).ToList(), "Recent, without the capture");
        // The capture's own copy on the clipboard, as "Also copy" puts it there, which records no use.
        listed.Clipboard.LastWritten = [cat.Id];
        var returned = new List<EditReturn>();
        listed.Editing.Returned += back => returned.Add(back);

        Assert.IsTrue((await listed.Editing.OpenFromNotification(new CaptureEdit(cat, Trim: false))).Succeeded);

        var session = listed.Editing.Session!;
        Assert.AreEqual(cat.Id, session.Item.Id, "the card's own item, not the grid's selection (G20)");
        Assert.AreEqual(EditOrigin.Notification, listed.Editing.Origin);
        Assert.IsInstanceOfType<TextTool>(session.Tool, "Edit opens on the first tool");
        Assert.AreEqual(Scope.All, listed.Listing.Scope, "All beneath, where every item is listed");
        CollectionAssert.AreEqual(new[] { cat.Id }, listed.Listing.Selection.ToList(), "the item listed and selected");

        session.Tool = session.Hide();
        session.DragThrough((2, 2), (5, 4));
        Assert.IsTrue((await listed.Editing.Save()).Succeeded);

        Assert.IsFalse(listed.Editing.IsOpen);
        Assert.AreEqual((EditOrigin.Notification, cat.Id), (returned.Single().Origin, returned.Single().Item!.Id), "back where the notification opened it");
        CollectionAssert.AreEqual(new[] { cat.Id }, listed.Listing.Selection.ToList(), "G26: a window a notification opened is left with the item selected");
        Assert.AreEqual(1, listed.Listing.Items.Single(item => item.Id == cat.Id).RenderedRevision, "listed as saved");
        CollectionAssert.AreEqual(new[] { cat.Id }, listed.Clipboard.WrittenAgain.Select(again => again.SavedItem).ToList(), "G26: ImageStash's copy of the item replaced");
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task TrimOpensAClipOnTheTrimToolAndARecordingOfOneImageOnText()
    {
        using var listed = await ListedStash.Open();
        var clip = await listed.Stash.ImportFixture("clip", "animated.gif");
        var single = await listed.Stash.ImportFixture("single", "still.gif");
        var shown = new List<EditorTool>();
        listed.Editing.Changed += () =>
        {
            if (listed.Editing.Session is { } open)
            {
                shown.Add(open.Tool);
            }
        };

        await listed.Editing.OpenFromNotification(new CaptureEdit(clip, Trim: true));

        var session = listed.Editing.Session!;
        Assert.IsInstanceOfType<TrimTool>(session.Tool, "G16: Trim opens the editor with the Trim tool selected");
        Assert.IsInstanceOfType<TrimTool>(shown.First(), "selected before the editor first shows");
        Assert.IsTrue(session.Playing, "the clip playing from the opening");
        Assert.AreEqual(EditOrigin.Notification, listed.Editing.Origin);

        await listed.Editing.OpenFromNotification(new CaptureEdit(single, Trim: true));

        session = listed.Editing.Session!;
        Assert.AreEqual(single.Id, session.Item.Id);
        Assert.IsFalse(session.Tools.OfType<TrimTool>().Any(), "a recording of one image is a still: no Trim listed (G20)");
        Assert.IsInstanceOfType<TextTool>(session.Tool, "so it opens on Text");

        await listed.Editing.OpenFromNotification(new CaptureEdit(clip, Trim: false));
        var edit = listed.Editing.Session!;
        Assert.IsInstanceOfType<TextTool>(edit.Tool, "Edit on a clip opens on Text");

        await listed.Editing.OpenFromNotification(new CaptureEdit(clip, Trim: true));
        Assert.AreSame(edit, listed.Editing.Session, "the edit open on that item stays as it is");
        Assert.IsInstanceOfType<TrimTool>(edit.Tool, "now on Trim");
    }

    [TestMethod]
    public async Task EditWithAnotherItemsEditOpenLeavesThatEditAsCancelWouldAndOpensTheCardsItem()
    {
        using var listed = await ListedStash.Open();
        var (cat, dog) = (await listed.Stash.ImportFixture("cat", "still.png"), await listed.Stash.ImportFixture("dog", "transparent.png"));
        await listed.Listing.Refresh();
        await listed.Editing.Open(dog, EditOrigin.Preview);
        var left = listed.Editing.Session!;
        left.Tool = left.Hide();
        left.DragThrough((1, 1), (4, 3));

        await listed.Editing.OpenFromNotification(new CaptureEdit(cat, Trim: false));

        Assert.IsFalse(left.IsOpen, "one edit at a time (Approach)");
        Assert.AreEqual(cat.Id, listed.Editing.Session!.Item.Id);
        Assert.AreEqual(EditOrigin.Notification, listed.Editing.Origin);
        Assert.AreEqual("Discarded the edit of “dog”.", listed.Notices.Shown.Single().Text, "left as Cancel would, notice and Undo included (G20)");
        Assert.IsTrue((await listed.Stash.Store!.ForEdit(dog.Id)).Value!.Layers.IsEmpty, "nothing of it saved");
    }

    [TestMethod]
    public async Task ACardsItemGoneFromTheStashIsRefusedByNameInTheWindowAndNothingOpens()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        Assert.IsTrue((await listed.Deletion.Delete([cat])).Succeeded);
        listed.Notices.Close(listed.Notices.Shown.Single());

        var refused = await listed.Editing.OpenFromNotification(new CaptureEdit(cat, Trim: false));

        Assert.AreEqual(new Failure("Edit", "cat", "it is no longer in the stash"), refused.Failure);
        Assert.AreEqual(refused.Failure!.Sentence, listed.Notices.Shown.Single().Text, "named in the window (G35)");
        Assert.IsFalse(listed.Editing.IsOpen);
    }

    [TestMethod]
    public async Task TheCardsUndoWhileItsEditIsOpenClosesItWithoutANoticeAndRemovesTheItem()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        var notice = Captured(listed, cat);
        await listed.Handoff.Copy(null!, [cat]);
        await listed.Editing.OpenFromNotification(new CaptureEdit(cat, Trim: false));
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        var returned = Returns(listed);

        await listed.Notifications.Undo(notice);
        await EditorSessions.Until(() => !returned.IsEmpty, "the edit never returned");

        Assert.IsFalse(session.IsOpen, "G16: the editor closes without saving");
        Assert.AreEqual(new EditReturn(EditOrigin.Grid, null), returned.Single(), "back to the grid, nothing to select");
        Assert.IsEmpty(listed.Notices.Shown, "without a notice");
        Assert.IsEmpty(await listed.Stash.All(), "the capture removed");
        CollectionAssert.AreEqual(new[] { cat.FilePath }, listed.Stash.RecycleBin.Asked, "its one file to the Recycle Bin (G29)");
        Assert.IsEmpty(listed.Clipboard.WrittenAgain, "no clipboard refresh");
    }

    [TestMethod]
    public async Task TheCardsUndoWhileTheSaveRendersAbandonsTheSaveUnnamedAndRemovesTheItemWithItsOneFile()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        var notice = Captured(listed, cat);
        await listed.Handoff.Copy(null!, [cat]);
        using var rendering = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        listed.Renderer = (original, name, layers, format, work, retry) =>
        {
            rendering.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        };
        await listed.Editing.OpenFromNotification(new CaptureEdit(cat, Trim: false));
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        var returned = Returns(listed);
        var saving = listed.Editing.Save();
        Assert.IsTrue(rendering.Wait(TimeSpan.FromSeconds(10)), "the render started");

        await listed.Notifications.Undo(notice);
        Assert.IsTrue(listed.Editing.IsOpen, "the Save running decides the edit's end itself");
        release.Set();
        Assert.IsTrue((await saving).Succeeded);

        Assert.IsFalse(session.IsOpen);
        Assert.AreEqual(new EditReturn(EditOrigin.Grid, null), returned.Single(), "ended as gone: back to the grid, nothing to select");
        Assert.IsEmpty(listed.Notices.Shown, "nothing named (G16)");
        Assert.IsEmpty(await listed.Stash.All(), "the capture removed");
        CollectionAssert.AreEqual(new[] { cat.FilePath }, listed.Stash.RecycleBin.Asked, "its one file, the render never placed");
        Assert.IsEmpty(Directory.GetFiles(listed.Stash.Folder.Work), "the render discarded (G28)");
        Assert.IsEmpty(listed.Clipboard.WrittenAgain, "no clipboard refresh");
    }

    [TestMethod]
    public async Task TheCardsUndoAfterTheSaveCommittedLetsTheSaveFinishThenRemovesBothFiles()
    {
        // The store retries the held render long enough for the Undo to wait behind the Save's steps.
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(5));
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        var notice = Captured(listed, cat);
        await listed.Handoff.Copy(null!, [cat]);
        var holds = new List<FileStream>();
        listed.Renderer = EditedItems.Holding(holds);
        await listed.Editing.OpenFromNotification(new CaptureEdit(cat, Trim: false));
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        var returned = Returns(listed);
        Task<Outcome> saving;
        Task undoing;
        try
        {
            saving = listed.Editing.Save();
            // Committed, the original moved into originals\, the render's place waiting on the hold.
            await EditorSessions.Until(() => File.Exists(listed.Stash.Folder.InOriginals("cat.png")), "the Save never committed");
            undoing = listed.Notifications.Undo(notice);
            await Task.Delay(300);
            Assert.IsFalse(undoing.IsCompleted, "the removal waits behind the Save's steps (Store serialises)");
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }

        Assert.IsTrue((await saving).Succeeded, "the Save finishes its steps");
        await undoing;
        await EditorSessions.Until(() => !returned.IsEmpty, "the edit never returned");

        Assert.IsFalse(session.IsOpen);
        Assert.IsEmpty(listed.Notices.Shown, "nothing named");
        Assert.IsEmpty(await listed.Stash.All(), "then removed");
        CollectionAssert.AreEquivalent(new[] { listed.Stash.Folder.InOriginals("cat.png"), listed.Stash.Folder.InRoot("cat.png") }, listed.Stash.RecycleBin.Asked,
            "both files, the original and the placed render, to the Recycle Bin (G28: complete, then removed)");
        Assert.IsEmpty(Directory.GetFiles(listed.Stash.Folder.Work), ".work\\ empty");
        Assert.IsEmpty(listed.Clipboard.WrittenAgain, "no clipboard refresh for an item gone since");
    }

    /// <summary>The item's capture notification raised as a screenshot's, returned as it shows above the tray.</summary>
    private static Notice Captured(ListedStash listed, StashItem item)
    {
        listed.CaptureNotification.Raise(item, CaptureKind.Screenshot, onClipboard: false, lengthMs: null);
        return listed.Notifications.Shown.Single();
    }

    /// <summary>Where the window shows the item at each close of an edit, collected from the store's thread too.</summary>
    private static System.Collections.Concurrent.ConcurrentQueue<EditReturn> Returns(ListedStash listed)
    {
        var returned = new System.Collections.Concurrent.ConcurrentQueue<EditReturn>();
        listed.Editing.Returned += returned.Enqueue;
        return returned;
    }
}
