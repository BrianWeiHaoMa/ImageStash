using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Leaving an edit (G20): "Cancel, the back arrow, and Esc with nothing selected leave the item as it was and, when the
/// edit's layers differ from the ones the item's finished picture was rendered from, put up a notice with Undo that
/// brings the discarded edit back." Cancel, the back arrow and Discard changes all raise the editor's Cancel, which the
/// window answers with <see cref="Editing.Cancel"/>; Esc goes through the editor's <see cref="EditorEscape"/> first.
/// </summary>
[TestClass]
public sealed class EditingLeavingTests
{
    private const string Discarded = "Discarded the edit of “cat”.";

    [TestMethod]
    public async Task AnEditOpenedAndLeftUntouchedRaisesNoNoticeByCancelOrByOneEsc()
    {
        using var listed = await ListedStash.Open();
        var cat = await Cat(listed);

        await listed.Editing.Open(cat, EditOrigin.Grid);
        Assert.IsTrue(listed.Editing.Cancel());
        Assert.IsFalse(listed.Editing.IsOpen, "left at once");
        Assert.IsFalse(listed.Notices.Shown.Any(), "Cancel, the back arrow and Discard changes say nothing when nothing would be lost");

        await listed.Editing.Open(cat, EditOrigin.Grid);
        Assert.IsTrue(LeftByEsc(listed), "one Esc leaves an unchanged edit");
        Assert.IsFalse(listed.Editing.IsOpen);
        Assert.IsFalse(listed.Notices.Shown.Any(), "nor does Esc");
    }

    [TestMethod]
    public async Task AChangedEditLeavesAtOnceByCancelWithTheNoticeWhoseUndoBringsExactlyItBack()
    {
        using var listed = await ListedStash.Open();
        var cat = await Cat(listed);
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        session.Deselect();
        var changed = session.Layers;

        Assert.IsTrue(listed.Editing.Cancel(), "Cancel and the back arrow leave it at once, asking nothing");

        Assert.IsFalse(listed.Editing.IsOpen);
        var notice = listed.Notices.Shown.Single();
        Assert.AreEqual((Discarded, "Undo"), (notice.Text, notice.UndoLabel));
        await listed.Notices.Undo(notice);
        Assert.AreEqual(changed, listed.Editing.Session!.Layers, "the Undo brings exactly that edit back");
    }

    [TestMethod]
    public async Task TheRestoredEditLeftUntouchedStillDiffersAndRaisesTheNoticeAgain()
    {
        using var listed = await ListedStash.Open();
        var cat = await Cat(listed);
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        var changed = session.Layers;
        listed.Editing.Cancel();
        await listed.Notices.Undo(listed.Notices.Shown.Single());
        Assert.IsFalse(listed.Editing.Session!.CanUndo, "the restored edit's history is empty");

        Assert.IsTrue(listed.Editing.Cancel());

        var again = listed.Notices.Shown.Single(each => each.Text == Discarded && listed.Notices.OffersUndo(each));
        await listed.Notices.Undo(again);
        Assert.AreEqual(changed, listed.Editing.Session!.Layers, "measured against the item, not the list the session opened on");
    }

    [TestMethod]
    public async Task AChangeTakenBackByUndoLeavesWithoutANotice()
    {
        using var listed = await ListedStash.Open();
        var cat = await Cat(listed);
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        session.Undo();
        Assert.IsTrue(session.CanRedo, "the history still holds the mark");

        Assert.IsTrue(LeftByEsc(listed), "the layers are the item's again, so one Esc leaves");

        Assert.IsFalse(listed.Notices.Shown.Any(), "the history's depth is no change");
    }

    [TestMethod]
    public async Task AClickThatAddedATextLeftBlankOrAToolsNextChoiceIsNoChange()
    {
        using var listed = await ListedStash.Open();
        var cat = await Cat(listed);
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Text();
        session.Click(4, 3);
        Assert.IsTrue(session.Typing, "the click added a layer and opened its field");
        Assert.IsFalse(session.Layers.IsEmpty, "the blank layer shows while its field is open");

        Assert.IsTrue(listed.Editing.Cancel());
        Assert.IsFalse(listed.Notices.Shown.Any(), "a blank new text, its field still open, is no change");

        await listed.Editing.Open(cat, EditOrigin.Grid);
        session = listed.Editing.Session!;
        session.Drawing(MarkKind.Arrow).Choose(new Rgba(200, 30, 30, 255));
        session.Hide().Choose(HideKind.Black);
        Assert.IsTrue(listed.Editing.Cancel());
        Assert.IsFalse(listed.Notices.Shown.Any(), "what tools remember for their next layer is no layer");
    }

    [TestMethod]
    public async Task LettersTypedInAFieldStillOpenAreAChangeAndTheUndoBringsThemBack()
    {
        using var listed = await ListedStash.Open();
        var cat = await Cat(listed);
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Text();
        session.Click(4, 3);
        session.SetTypedText("hi");
        Assert.IsTrue(session.Typing);

        Assert.IsTrue(listed.Editing.Cancel());

        var notice = listed.Notices.Shown.Single();
        Assert.AreEqual(Discarded, notice.Text);
        await listed.Notices.Undo(notice);
        Assert.AreEqual("HI", ((TextLayer)listed.Editing.Session!.Layers.Layers.Single()).Text);
    }

    [TestMethod]
    public async Task AColourOnlyBeingTriedOnASavedMarkIsPutBackAndIsNoChange()
    {
        using var listed = await ListedStash.Open();
        var cat = await Cat(listed);
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Drawing(MarkKind.Box);
        session.DragThrough((1, 1), (5, 4));
        Assert.IsTrue((await listed.Editing.Save()).Succeeded);
        var saved = (await listed.Stash.Store!.Item(cat.Id)).Value!;

        await listed.Editing.Open(saved, EditOrigin.Grid);
        session = listed.Editing.Session!;
        var draw = session.Drawing(MarkKind.Box);
        session.Click(1, 1);
        Assert.IsNotNull(session.Selected, "the saved box selected");
        draw.BeginColor();
        Assert.IsTrue(draw.Preview(new Rgba(10, 200, 10, 255)), "the colour shows on the box while it is tried");

        Assert.IsTrue(listed.Editing.Cancel());

        Assert.IsFalse(listed.Notices.Shown.Any(), "the colour being tried is put back before the layers are compared");
    }

    [TestMethod]
    public async Task ACropAndAShrinkChoiceAreChanges()
    {
        using var listed = await ListedStash.Open();
        var cat = await Cat(listed);

        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Crop();
        session.DragThrough((1, 1), (5, 4));
        Assert.IsNotNull(session.Layers.Crop);
        listed.Editing.Cancel();
        Assert.AreEqual(Discarded, listed.Notices.Shown.Single().Text, "a crop is a layer");

        listed.Notices.Close(listed.Notices.Shown.Single());
        await listed.Editing.Open(cat, EditOrigin.Grid);
        listed.Editing.Session!.Shrink = Shrink.Half;
        listed.Editing.Cancel();
        Assert.AreEqual(Discarded, listed.Notices.Shown.Single().Text, "so is Shrink");
    }

    [TestMethod]
    public async Task ATrimIsAChange()
    {
        using var listed = await ListedStash.Open();
        var clip = await listed.Stash.ImportFixture("clip", "animated.gif");
        await listed.Listing.Refresh();
        await listed.Editing.Open(clip, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        var trim = session.Trim();
        session.Tool = trim;
        await EditorSessions.Until(() => session.TimelineComplete, "the clip's frames read");
        trim.DragHandle(RangeEnd.First, 1);
        Assert.IsNotNull(session.Layers.Trim);

        listed.Editing.Cancel();

        Assert.AreEqual("Discarded the edit of “clip”.", listed.Notices.Shown.Single().Text);
    }

    [TestMethod]
    public async Task AnEditSaveAsNewItemMovedOnToAndLeftUntouchedRaisesNone()
    {
        using var listed = await ListedStash.Open();
        var cat = await Cat(listed);
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        Assert.IsTrue((await listed.Editing.SaveAsNewItem()).Succeeded);
        Assert.AreNotEqual(cat.Id, listed.Editing.Session!.Item.Id, "going on with the new item");

        Assert.IsTrue(listed.Editing.Cancel());

        Assert.IsFalse(listed.Notices.Shown.Any(each => each.Text.StartsWith("Discarded", StringComparison.Ordinal)), "the new item holds exactly these layers");
    }

    [TestMethod]
    public async Task LeavingForAnotherItemOrAnAdoptionsUndoFollowsTheOneRule()
    {
        using var listed = await ListedStash.Open();
        var (cat, dog) = (await listed.Stash.ImportFixture("cat", "still.png"), await listed.Stash.ImportFixture("dog", "transparent.png"));
        await listed.Listing.Refresh();

        await listed.Editing.Open(dog, EditOrigin.Grid);
        await listed.Editing.OpenFromNotification(new CaptureEdit(cat, Trim: false));
        Assert.AreEqual(cat.Id, listed.Editing.Session!.Item.Id);
        Assert.IsFalse(listed.Notices.Shown.Any(), "a notification's Edit over an untouched edit leaves it without a notice");

        await listed.Editing.LeaveEditOf(cat.Id);
        Assert.IsFalse(listed.Editing.IsOpen);
        Assert.IsFalse(listed.Notices.Shown.Any(), "nor does an adoption's Undo leaving an untouched edit");

        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        await listed.Editing.LeaveEditOf(cat.Id);
        Assert.AreEqual(Discarded, listed.Notices.Shown.Single().Text, "an adoption's Undo leaving a changed edit leaves the notice with its Undo");
    }

    [TestMethod]
    public async Task AMenuChangeOfTheItemWhileItsEditIsOpenMovesWhatTheEditIsComparedWith()
    {
        using var listed = await ListedStash.Open();
        var cat = await Cat(listed);
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        Assert.IsTrue((await listed.Editing.Save()).Succeeded);
        var saved = (await listed.Stash.Store!.Item(cat.Id)).Value!;
        Assert.IsTrue((await listed.Editing.RevertToOriginal(saved)).Succeeded);
        var revertNotice = listed.Notices.Shown.Single();
        await listed.Editing.Open((await listed.Stash.Store.Item(cat.Id)).Value!, EditOrigin.Grid);
        session = listed.Editing.Session!;
        Assert.IsTrue(session.Layers.IsEmpty, "opened on the reverted item");
        // The footer's estimate reads the item too: finished first, so only the listing's change can bring the news.
        await EditorSessions.Until(() => !session.Estimating, "the estimate done");

        await listed.Notices.Undo(revertNotice);
        await EditorSessions.Until(() => session.Differs, "the open edit compared with the layers the Undo put back");

        Assert.AreEqual(EscapeAct.Taken, new EditorEscape().Pressed(session, pickerOpen: false, inField: false, inMenu: false), "Esc asks first: leaving would discard the empty list");
        Assert.IsTrue(listed.Editing.Cancel());
        Assert.IsTrue(listed.Notices.Shown.Any(each => each.Text == Discarded), "and Cancel offers it back");
    }

    private static async Task<StashItem> Cat(ListedStash listed)
    {
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        return cat;
    }

    /// <summary>One Esc with nothing selected, through the editor's Esc as the overlay presses it, and the window's Cancel when it leaves.</summary>
    private static bool LeftByEsc(ListedStash listed)
    {
        var escape = new EditorEscape();
        var act = escape.Pressed(listed.Editing.Session!, pickerOpen: false, inField: false, inMenu: false);
        escape.Released();
        return act == EscapeAct.Leave && listed.Editing.Cancel();
    }
}
