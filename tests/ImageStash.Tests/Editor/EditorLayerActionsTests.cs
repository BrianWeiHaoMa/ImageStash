using Avalonia.Controls;
using Avalonia.LogicalTree;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// The editor's Undo, Redo and Compare, and the layer's Delete, as the editor shows them (G20): the three absent on a
/// fresh item, appearing together once the edit holds a layer or a change, so an item opened with layers shows them at
/// once, and from then in their places for the rest of that edit, Undo and Redo dimmed in place while they cannot act;
/// Delete shown only while a layer is selected, in the place the option row keeps for it, so nothing in the row moves.
/// </summary>
[TestClass]
public sealed class EditorLayerActionsTests
{
    private static readonly string?[] Fresh = ["Back", "More options"];
    private static readonly string?[] Held = ["Back", "Undo", "Redo", "Compare", "More options"];

    [TestMethod]
    public async Task AFreshItemOpensWithoutUndoRedoOrCompare()
    {
        using var editing = await StillInTheEditor.Open();

        ControlThread.Run(() =>
        {
            var shown = EditorShown.Over(editing.Session);

            CollectionAssert.AreEqual(Fresh, shown.HeadShown.ToArray());
            Assert.AreEqual((false, false), shown.State("Compare"), "Compare would show the same picture");
        });
    }

    [TestMethod]
    public async Task AfterOneChangeAndItsUndoBothStepsKeepTheirPlacesAndTheGroupStaysUndoneToNothing()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;

        ControlThread.Run(() =>
        {
            var shown = EditorShown.Over(session);
            session.Drawing(MarkKind.Box).Choose(MarkKind.Box);
            session.DragThrough((5.5f, 5.5f), (30.5f, 20.5f));

            CollectionAssert.AreEqual(Held, shown.HeadShown.ToArray(), "the three appear together, in this order");
            Assert.AreEqual(((true, true), (true, false), (true, true)), (shown.State("Undo"), shown.State("Redo"), shown.State("Compare")));

            // A button off any window ignores a click, so the step is the session's, as Ctrl+Z's is.
            session.Undo();

            Assert.IsTrue(session.Layers.IsEmpty, "undone back to nothing");
            CollectionAssert.AreEqual(Held, shown.HeadShown.ToArray(), "Redo never takes Undo's place, so a second click there cannot redo");
            Assert.AreEqual(((true, false), (true, true)), (shown.State("Undo"), shown.State("Redo")), "Undo dimmed in its place, Redo able beside it");
        });
    }

    [TestMethod]
    public async Task TheGroupStaysWhileTheEditGoesOnWithACopyHoldingNoLayerAndNoHistory()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        var shown = default(EditorShown);
        ControlThread.Run(() =>
        {
            shown = EditorShown.Over(session);
            session.Drawing(MarkKind.Box).Choose(MarkKind.Box);
            session.DragThrough((5.5f, 5.5f), (30.5f, 20.5f));
            session.DeleteSelected();
            Assert.IsTrue(session.Layers.IsEmpty);
        });

        var continued = await session.SaveAsNewItem();
        Assert.AreEqual(EditEnding.Continued, continued.Ending, continued.Failure?.Sentence);
        Assert.IsTrue(session.Layers.IsEmpty && session.Edit.RenderedLayers.IsEmpty && !session.CanUndo && !session.CanRedo, "the copy holds no layer and no history");

        ControlThread.Run(() =>
        {
            // The window shows the edit going on with the copy in the same editor.
            shown!.Overlay.Show(session);

            CollectionAssert.AreEqual(Held, shown.HeadShown.ToArray(), "the three keep their places for the rest of the edit");
            Assert.AreEqual(((true, false), (true, false)), (shown.State("Undo"), shown.State("Redo")));
        });
    }

    [TestMethod]
    public async Task AnotherFreshItemsEditOpenedAfterwardsStartsWithoutTheGroup()
    {
        using var first = await StillInTheEditor.Open();
        using var second = await StillInTheEditor.Open();

        ControlThread.Run(() =>
        {
            var shown = EditorShown.Over(first.Session);
            first.Session.Drawing(MarkKind.Box).Choose(MarkKind.Box);
            first.Session.DragThrough((5.5f, 5.5f), (30.5f, 20.5f));
            CollectionAssert.AreEqual(Held, shown.HeadShown.ToArray());

            // The window keeps one editor and shows each edit in it.
            shown.Overlay.Show(second.Session);

            CollectionAssert.AreEqual(Fresh, shown.HeadShown.ToArray());
        });
    }

    [TestMethod]
    public async Task AnItemSavedWithLayersOpensWithAllThreeUndoAndRedoDimmed()
    {
        using var editing = await StillInTheEditor.Open();
        editing.Session.Drawing(MarkKind.Box).Choose(MarkKind.Box);
        editing.Session.DragThrough((5.5f, 5.5f), (30.5f, 20.5f));
        Assert.AreEqual(EditEnding.Saved, (await editing.Session.Save()).Ending);
        using var reopened = await Open(editing.Stash, editing.Still);
        Assert.IsFalse(reopened.Layers.IsEmpty);

        ControlThread.Run(() =>
        {
            var shown = EditorShown.Over(reopened);

            CollectionAssert.AreEqual(Held, shown.HeadShown.ToArray());
            Assert.AreEqual(((true, false), (true, false), (true, true)), (shown.State("Undo"), shown.State("Redo"), shown.State("Compare")));
        });
    }

    [TestMethod]
    public async Task DeleteShowsOnlyWhileALayerIsSelectedAndNothingInTheRowMoves()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;

        ControlThread.Run(() =>
        {
            var shown = EditorShown.Over(session);
            var delete = shown.Button("Delete");
            var slot = (Control)delete.GetLogicalParent()!;
            var options = (Grid)slot.GetLogicalParent()!;
            var places = Places(options);
            Assert.AreEqual((false, false), shown.State("Delete"), "nothing selected");

            session.Drawing(MarkKind.Box).Choose(MarkKind.Box);
            session.DragThrough((5.5f, 5.5f), (30.5f, 20.5f));
            Assert.AreEqual(0, session.Selected);
            Assert.AreEqual((true, true), shown.State("Delete"));
            var withSelection = Places(options);

            session.Deselect();

            Assert.AreEqual((false, false), shown.State("Delete"));
            Assert.IsTrue(slot.IsVisible, "the slot stays");
            Assert.AreEqual(1, Grid.GetColumn(slot));
            Assert.AreEqual(new GridLength(44), options.ColumnDefinitions[1].Width, "its room kept whether or not Delete shows");
            CollectionAssert.AreEqual(places, withSelection, "every control of the row in its place with a selection");
            CollectionAssert.AreEqual(places, Places(options), "and without");

            session.Click(5.5f, 10.5f);
            Assert.AreEqual(0, session.Selected);
            session.Tool = session.Crop();
            Assert.AreEqual((false, false), shown.State("Delete"), "nothing can be selected in Crop");
        });
    }

    /// <summary>Each child of the option row's grid with its column, row and whether it shows.</summary>
    private static string[] Places(Grid options) =>
        [.. options.Children.Select(child => $"{child.GetType().Name} {Grid.GetColumn(child)} {Grid.GetRow(child)} {child.IsVisible}")];
}
