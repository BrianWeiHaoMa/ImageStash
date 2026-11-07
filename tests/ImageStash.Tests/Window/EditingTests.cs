using ImageStash.Editor;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Editing in the window, headless (G20, G26, G35, G16, R9): the one path that opens the editor, on one item only and
/// nothing with several; every refusal named with nothing open; Save closing the edit only once the store has
/// published it, back where Edit was started; a refused Save named with the edit kept, a failure after the commit
/// named with it closed; Cancel's notice and the Undo that brings the discarded edit back; one edit at a time; the item
/// leaving the stash ending the edit without a notice.
/// </summary>
[TestClass]
public sealed class EditingTests
{
    [TestMethod]
    public async Task CtrlEOpensTheEditorOnTheOneSelectedItemOrThePreviewsAndOnNothingWithSeveralSelected()
    {
        using var listed = await ListedStash.Open();
        var (cat, dog) = (await listed.Stash.ImportFixture("cat", "still.png"), await listed.Stash.ImportFixture("dog", "transparent.png"));
        await listed.Listing.Refresh();
        listed.Listing.Select([cat.Id, dog.Id], dog.Id);

        Assert.IsTrue((await listed.Editing.OpenOnSelection(null)).Succeeded);
        Assert.IsFalse(listed.Editing.IsOpen, "G20: unavailable with several selected, on the focused one too");
        Assert.IsEmpty(listed.Notices.Shown, "and named in no notice");

        await listed.Editing.OpenOnSelection(cat);
        Assert.AreEqual(cat.Id, listed.Editing.Session?.Item.Id, "the preview's item, whatever the grid beneath selects");
        Assert.AreEqual(EditOrigin.Preview, listed.Editing.Origin);
        CollectionAssert.AreEqual(new[] { cat.Id }, listed.Listing.Selection.ToList(), "the item alone selected");
        listed.Editing.Cancel();

        listed.Listing.Focus(dog.Id);
        await listed.Editing.OpenOnSelection(null);
        Assert.AreEqual(dog.Id, listed.Editing.Session?.Item.Id);
        Assert.AreEqual(EditOrigin.Grid, listed.Editing.Origin);
    }

    [TestMethod]
    public async Task AListTheStoreCannotReadIsRefusedByNameNothingOpensAndTheItemStaysListed()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        listed.Stash.SqlWhileOpen("UPDATE items SET layers = '{' WHERE id = $id", ("$id", cat.Id));
        await listed.Listing.Refresh();

        var refused = await listed.Editing.Open(cat, EditOrigin.Grid);

        Assert.IsFalse(refused.Succeeded);
        Assert.StartsWith("Read the item's layers failed: cat, ", refused.Failure!.Sentence);
        Assert.AreEqual(refused.Failure.Sentence, listed.Notices.Shown.Single().Text, "named in the window (G35)");
        Assert.IsFalse(listed.Editing.IsOpen);
        Assert.AreEqual(cat.Id, listed.Listing.Items.Single().Id, "the item still listed");
    }

    [TestMethod]
    public async Task AnOriginalHeldWithoutSharingPastTheRetryIsRefusedByNameAndNothingOpens()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        Outcome refused;
        using (new FileStream(cat.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            refused = await listed.Editing.Open(cat, EditOrigin.Grid);
        }

        Assert.AreEqual(new Failure("Read the picture", "cat", HeldFileRetry.HeldReason), refused.Failure);
        Assert.AreEqual(refused.Failure!.Sentence, listed.Notices.Shown.Single().Text);
        Assert.IsFalse(listed.Editing.IsOpen);
    }

    [TestMethod]
    public async Task AnItemNoLongerInTheStashIsRefusedByName()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Stash.Store!.Remove([cat.Id]);

        var refused = await listed.Editing.Open(cat, EditOrigin.Grid);

        Assert.AreEqual(new Failure("Edit", "cat", "it is no longer in the stash"), refused.Failure);
        Assert.IsFalse(listed.Editing.IsOpen);
    }

    [TestMethod]
    public async Task SaveClosesTheEditOnlyOnceTheStoreHasPublishedItAndReturnsWhereEditStarted()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        await listed.Editing.Open(cat, EditOrigin.Preview);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.Hide().Choose(HideKind.Black);
        session.DragThrough((2, 2), (5, 4));
        var busy = new List<string?>();
        var returned = new List<EditReturn>();
        listed.Editing.Changed += () => busy.Add(listed.Editing.Busy);
        listed.Editing.Returned += back => returned.Add(back);

        var saved = await listed.Editing.Save();

        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        CollectionAssert.Contains(busy, "Saving “cat”…", "G35: the busy state names the Save while it runs");
        Assert.IsFalse(listed.Editing.IsOpen);
        Assert.IsFalse(session.IsOpen);
        var back = returned.Single();
        Assert.AreEqual(EditOrigin.Preview, back.Origin, "G26: back to the preview Edit was started from");
        Assert.AreEqual(1, back.Item!.RenderedRevision, "R9: returned only once the store has published the render");
        Assert.AreEqual(1, listed.Listing.Items.Single(item => item.Id == cat.Id).RenderedRevision, "the listing read again");
        CollectionAssert.AreEqual(new[] { cat.Id }, listed.Listing.Selection.ToList(), "the item selected");
        Assert.IsEmpty(listed.Notices.Shown, "nothing to name");
        Assert.HasCount(1, (await listed.Stash.Store!.ForEdit(cat.Id)).Value!.Layers.Layers);
    }

    [TestMethod]
    public async Task ARefusedSaveIsNamedAndTheEditStaysOpenOnItsLayers()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        listed.Renderer = EditedItems.Refusing;
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        var live = session.Layers;
        var returned = 0;
        listed.Editing.Returned += _ => returned++;

        var refused = await listed.Editing.Save();

        Assert.IsFalse(refused.Succeeded);
        StringAssert.Contains(listed.Notices.Shown.Single().Text, EditedItems.RefusedReason, "named in the window (G35)");
        Assert.IsTrue(listed.Editing.IsOpen, "the edit kept");
        Assert.AreEqual(live, session.Layers);
        Assert.AreEqual(0, returned);
        Assert.IsNull(listed.Editing.Busy);
    }

    [TestMethod]
    public async Task AFailureAfterTheCommitIsNamedTheEditClosesTheItemIsListedOnThePictureOnDiskAndNothingIsCopied()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        var holds = new List<FileStream>();
        listed.Renderer = EditedItems.Holding(holds);
        try
        {
            await listed.Editing.Open(cat, EditOrigin.Grid);
            listed.Editing.Session!.Tool = listed.Editing.Session!.Hide();
            listed.Editing.Session!.DragThrough((1, 1), (4, 3));
            var returned = new List<EditReturn>();
            listed.Editing.Returned += back => returned.Add(back);

            var failed = await listed.Editing.Save();

            Assert.IsFalse(failed.Succeeded);
            Assert.AreEqual(failed.Failure!.Sentence, listed.Notices.Shown.Single().Text, "named (G35)");
            Assert.IsFalse(listed.Editing.IsOpen, "closed on the item as committed");
            Assert.AreEqual(EditOrigin.Grid, returned.Single().Origin);
            var edit = await EditedItems.ForEdit(listed.Stash.Store!, cat.Id);
            Assert.AreEqual(edit.OriginalPath, listed.Listing.Items.Single().FilePath, "listed on the picture on disk, the original (State: A live failure)");
            Assert.IsEmpty(listed.Clipboard.Written, "nothing written to the clipboard");
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }
    }

    [TestMethod]
    public async Task TheItemLeavingTheStashWhileTheEditorIsOpenEndsTheEditWithoutANotice()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        await listed.Editing.Open(cat, EditOrigin.Preview);
        var session = listed.Editing.Session!;
        // The return is raised from the store's read once the edit has closed: waited for itself, not for the closing before it.
        var returned = new System.Collections.Concurrent.ConcurrentQueue<(EditReturn Back, bool Open)>();
        listed.Editing.Returned += back => returned.Enqueue((back, listed.Editing.IsOpen));

        await listed.Stash.Store!.Remove([cat.Id]);
        await listed.Listing.Refresh();
        await EditorSessions.Until(() => !returned.IsEmpty, "the edit never returned");

        Assert.IsFalse(session.IsOpen);
        Assert.IsEmpty(listed.Notices.Shown, "G16: closed without a notice");
        Assert.AreEqual((new EditReturn(EditOrigin.Grid, null), false), returned.Single(), "back to the grid, with nothing to select, the edit already closed");
    }

    [TestMethod]
    public async Task AnItemMerelyOutOfTheQueryKeepsItsEditOpen()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        await listed.Editing.Open(cat, EditOrigin.Grid);

        await listed.Listing.SetQuery("nothing matches this");
        await Task.Delay(200);

        Assert.IsTrue(listed.Editing.IsOpen);
    }

    [TestMethod]
    public async Task CancelLeavesTheItemAsItWasWithANoticeWhoseUndoOpensTheDiscardedEditAgain()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        await listed.Editing.Open(cat, EditOrigin.Preview);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        var discarded = session.Layers;
        var returned = new List<EditReturn>();
        listed.Editing.Returned += back => returned.Add(back);

        Assert.IsTrue(listed.Editing.Cancel());

        Assert.IsFalse(listed.Editing.IsOpen);
        Assert.AreEqual(EditOrigin.Preview, returned.Single().Origin, "back where Edit was started");
        Assert.AreEqual(cat.Id, returned.Single().Item!.Id);
        Assert.IsTrue((await listed.Stash.Store!.ForEdit(cat.Id)).Value!.Layers.IsEmpty, "the item as it was");
        Assert.AreEqual(cat.FilePath, (await listed.Stash.Store.Item(cat.Id)).Value!.FilePath);
        var notice = listed.Notices.Shown.Single();
        Assert.AreEqual("Discarded the edit of “cat”.", notice.Text);
        Assert.AreEqual("Undo", notice.UndoLabel);

        await listed.Notices.Undo(notice);

        Assert.IsTrue(listed.Editing.IsOpen, "G20: Undo brings the discarded edit back");
        Assert.AreEqual(discarded, listed.Editing.Session!.Layers, "on the discarded list, not the committed one");
        Assert.AreEqual(EditOrigin.Preview, listed.Editing.Origin);
    }

    [TestMethod]
    public async Task CancelWithNothingChangedStillPutsUpTheNoticeAndUndoOnAnItemGoneSinceIsRefusedByName()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        await listed.Editing.Open(cat, EditOrigin.Grid);

        listed.Editing.Cancel();
        var notice = listed.Notices.Shown.Single();
        Assert.AreEqual("Discarded the edit of “cat”.", notice.Text, "G20 makes no exception for an unchanged list");

        await listed.Stash.Store!.Remove([cat.Id]);
        await listed.Notices.Undo(notice);

        Assert.IsFalse(listed.Editing.IsOpen);
        Assert.AreEqual(new Failure("Edit", "cat", "it is no longer in the stash").Sentence, listed.Notices.Shown.Single().Text, "named once");
    }

    [TestMethod]
    public async Task OpeningAnotherItemFirstLeavesTheOpenEditAsCancelWould()
    {
        using var listed = await ListedStash.Open();
        var (cat, dog) = (await listed.Stash.ImportFixture("cat", "still.png"), await listed.Stash.ImportFixture("dog", "transparent.png"));
        await listed.Listing.Refresh();
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var first = listed.Editing.Session!;

        await listed.Editing.Open(dog, EditOrigin.Grid);

        Assert.IsFalse(first.IsOpen, "one edit at a time (Approach)");
        Assert.AreEqual(dog.Id, listed.Editing.Session!.Item.Id);
        Assert.AreEqual("Discarded the edit of “cat”.", listed.Notices.Shown.Single().Text, "left as Cancel would, notice and Undo included");
    }

    [TestMethod]
    public async Task SaveAsNewItemGoesOnWithTheNewItemAndTellsTheWindow()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        await listed.Editing.Open(cat, EditOrigin.Preview);
        listed.Editing.Session!.Tool = listed.Editing.Session!.Hide();
        listed.Editing.Session!.DragThrough((1, 1), (4, 3));
        var continued = new List<StashItem>();
        listed.Editing.Continued += item => continued.Add(item);

        var saved = await listed.Editing.SaveAsNewItem();

        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        var made = continued.Single();
        Assert.AreEqual("cat 2", made.Name);
        Assert.IsTrue(listed.Editing.IsOpen, "G26: editing goes on with the new item");
        Assert.AreEqual(made.Id, listed.Editing.Session!.Item.Id);
        Assert.IsFalse(listed.Editing.Session.CanUndo);
        Assert.AreEqual(EditOrigin.Preview, listed.Editing.Origin, "where Edit was started follows it");
        CollectionAssert.AreEqual(new[] { made.Id }, listed.Listing.Selection.ToList());
        Assert.IsTrue((await listed.Stash.Store!.ForEdit(cat.Id)).Value!.Layers.IsEmpty, "the item edited until now as it was");
    }

    [TestMethod]
    public async Task ASecondSaveWhileOneRunsAsksForNothingMoreAndCancelMeanwhileChangesNothing()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        var release = new ManualResetEventSlim();
        listed.Renderer = (original, name, layers, format, work, retry) =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        };
        await listed.Editing.Open(cat, EditOrigin.Grid);
        listed.Editing.Session!.Tool = listed.Editing.Session!.Hide();
        listed.Editing.Session!.DragThrough((1, 1), (4, 3));
        var returned = 0;
        listed.Editing.Returned += _ => returned++;

        var first = listed.Editing.Save();
        var second = listed.Editing.Save();
        var cancelled = listed.Editing.Cancel();
        release.Set();
        await Task.WhenAll(first, second);

        Assert.AreSame(first, second, "the running Save, asked again");
        Assert.IsFalse(cancelled, "a Save running is not cancelled");
        Assert.AreEqual(1, returned, "one ending, one return");
        Assert.IsEmpty(listed.Notices.Shown);
        Assert.AreEqual(1, (await listed.Stash.Store!.Item(cat.Id)).Value!.RenderedRevision);
    }
}
