using System.Drawing;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// Draw (G23; G21's recent colors; What crosses every boundary): Pen, Marker, Arrow and Box, each its own layer in whole
/// pixels with the chosen color and thickness; a click selects the mark under it and on empty picture changes nothing; a
/// drag starting on the selected mark moves it, from its handles too; a drag starting inside a box or on an unselected
/// mark draws a new one; Del removes the selected mark; an option chosen with a mark selected changes it and the next.
/// </summary>
[TestClass]
public sealed class DrawToolTests
{
    private static readonly Rgba Blue = ColorChoices.Swatches[4];

    [TestMethod]
    public async Task APenDragThroughFivePointsIsOneSelectedMark()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        var draw = session.Drawing(MarkKind.Pen);
        draw.Choose(ColorChoices.Red);
        draw.Choose(Thickness.Medium);

        session.DragThrough((0.5f, 0.5f), (2.5f, 1.5f), (4.5f, 2.5f), (6.5f, 3.5f), (7.5f, 5.5f));

        var mark = (DrawMarkLayer)session.Layers.Layers.Single();
        Assert.AreEqual((MarkKind.Pen, ColorChoices.Red, Thickness.Medium), (mark.Kind, mark.Color, mark.Thickness));
        Assert.AreEqual(new PixelPoint(0, 0), mark.Points[0]);
        Assert.AreEqual(new PixelPoint(7, 5), mark.Points[^1]);
        Assert.IsNull(mark.Rect);
        Assert.AreEqual(0, session.Selected);
    }

    [TestMethod]
    public async Task AMarkerDragIsAMarkerMark()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Drawing(MarkKind.Marker);

        session.DragThrough((0.5f, 0.5f), (2.5f, 1.5f), (7.5f, 5.5f));

        var mark = (DrawMarkLayer)session.Layers.Layers.Single();
        Assert.AreEqual(MarkKind.Marker, mark.Kind);
        CollectionAssert.AreEqual(new PixelPoint[] { new(0, 0), new(2, 1), new(7, 5) }, mark.Points.ToArray());
    }

    [TestMethod]
    public async Task AnArrowRunsFromThePressToTheReleaseItsHeadThere()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Drawing(MarkKind.Arrow);

        session.Press(new PointF(0, 0));
        session.Drag(new PointF(3, 3));
        session.Release(new PointF(7, 5));

        CollectionAssert.AreEqual(new PixelPoint[] { new(0, 0), new(7, 5) }, ((DrawMarkLayer)session.Layers.Layers.Single()).Points.ToArray());
    }

    [TestMethod]
    public async Task ABoxIsTheDraggedRectangleEitherWayRound()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Drawing(MarkKind.Box);

        session.DragThrough((6.5f, 4.5f), (1.5f, 1.5f));

        var box = (DrawMarkLayer)session.Layers.Layers.Single();
        Assert.AreEqual(new PixelRect(1, 1, 6, 4), box.Rect);
        Assert.IsEmpty(box.Points);
    }

    [TestMethod]
    public async Task AClickOnEmptyPictureDrawsNothingAndAClickOnAMarkSelectsIt()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Drawing(MarkKind.Box).Choose(Thickness.Thin);
        session.DragThrough((1, 1), (3, 3));

        session.Click(7.5f, 5.5f);
        Assert.HasCount(1, session.Layers.Layers);
        Assert.AreEqual(0, session.Selected, "a click on empty picture is no deselection");

        session.Deselect();
        session.Click(7.5f, 5.5f);
        Assert.IsNull(session.Selected);
        session.Click(2, 2);
        Assert.AreEqual(0, session.Selected);
        Assert.HasCount(1, session.Layers.Layers);
    }

    [TestMethod]
    public async Task ADragStartingOnTheSelectedBoxMovesItAndOneInsideItDrawsANewMark()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Drawing(MarkKind.Box).Choose(Thickness.Thin);
        session.DragThrough((1, 1), (6, 5));

        session.DragThrough((1, 3), (3, 4));
        Assert.AreEqual(new PixelRect(3, 2, 5, 4), ((DrawMarkLayer)session.Layers.Layers.Single()).Rect, "moved, kept on the picture");

        session.DragThrough((8, 6), (9, 7));
        Assert.AreEqual(new PixelRect(3, 2, 5, 4), ((DrawMarkLayer)session.Layers.Layers.Single()).Rect, "a corner moves a box; it does not resize it");

        session.HitTolerance = 0.25f;
        session.DragThrough((5.5f, 4), (6.5f, 5));
        Assert.HasCount(2, session.Layers.Layers, "inside the box, away from its edges, is open picture");
    }

    [TestMethod]
    public async Task ADragStartingOnAnUnselectedArrowDrawsANewMarkOverIt()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Drawing(MarkKind.Arrow);
        session.DragThrough((0, 0), (7, 5));
        var arrow = session.Layers.Layers.Single();
        session.Deselect();

        session.DragThrough((3.5f, 2.5f), (5.5f, 0.5f));

        Assert.HasCount(2, session.Layers.Layers);
        Assert.AreEqual(arrow, session.Layers.Layers[0], "the arrow did not move");
        CollectionAssert.AreEqual(new PixelPoint[] { new(3, 2), new(5, 0) }, ((DrawMarkLayer)session.Layers.Layers[1]).Points.ToArray());
        Assert.AreEqual(1, session.Selected);
    }

    [TestMethod]
    public async Task ADragFromTheSelectedArrowsEndMovesTheWholeArrow()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Drawing(MarkKind.Arrow);
        session.DragThrough((2, 2), (5, 3));
        session.HitTolerance = 0;

        session.DragThrough((5.5f, 3.5f), (6.5f, 4.5f));

        CollectionAssert.AreEqual(new PixelPoint[] { new(3, 3), new(6, 4) }, ((DrawMarkLayer)session.Layers.Layers.Single()).Points.ToArray());
    }

    [TestMethod]
    public async Task DeleteRemovesTheSelectedMarkAndSelectsNothing()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Drawing(MarkKind.Box).Choose(Thickness.Thin);
        session.DragThrough((0, 0), (2, 2));
        session.DragThrough((5, 3), (7, 5));

        session.DeleteSelected();

        Assert.HasCount(1, session.Layers.Layers);
        Assert.AreEqual(new PixelRect(0, 0, 2, 2), ((DrawMarkLayer)session.Layers.Layers.Single()).Rect);
        Assert.IsNull(session.Selected);
        session.Undo();
        Assert.HasCount(2, session.Layers.Layers);
        Assert.AreEqual(1, session.Selected);
    }

    [TestMethod]
    public async Task ASwatchChosenWithAMarkSelectedColorsItAndTheNextMark()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        var draw = session.Drawing(MarkKind.Box);
        draw.Choose(Thickness.Thin);
        session.DragThrough((1, 1), (3, 3));

        draw.Choose(Blue);
        Assert.AreEqual(Blue, ((DrawMarkLayer)session.Layers.Layers[0]).Color);
        Assert.AreEqual(Blue, draw.ColorShown);
        session.DragThrough((5.5f, 3.5f), (7, 5));

        Assert.AreEqual(Blue, ((DrawMarkLayer)session.Layers.Layers[1]).Color);
        session.Undo();
        session.Undo();
        Assert.AreEqual(ColorChoices.Red, ((DrawMarkLayer)session.Layers.Layers.Single()).Color, "the color was one step");
    }

    [TestMethod]
    public async Task TheOptionRowShowsTheSelectedMarksValuesAndOtherwiseTheNextMarks()
    {
        using var editing = await CatInTheEditor.Open(List(new DrawMarkLayer(MarkKind.Arrow, [new(0, 0), new(7, 5)], null, Blue, Thickness.Thick, null)));
        var session = editing.Session;
        var draw = session.Drawing(MarkKind.Pen);

        session.Click(4, 3);
        Assert.AreEqual((MarkKind.Arrow, Blue, Thickness.Thick), (draw.MarkShown, draw.ColorShown, draw.ThicknessShown));
        session.Deselect();
        Assert.AreEqual((MarkKind.Pen, ColorChoices.Red, Thickness.Medium), (draw.MarkShown, draw.ColorShown, draw.ThicknessShown));
    }

    [TestMethod]
    public async Task AClickOnAHideWhileDrawingSelectsItAndMakesHideCurrent()
    {
        using var editing = await CatInTheEditor.Open(List(new HideLayer(new PixelRect(1, 1, 3, 3), HideKind.Black, null)));
        var session = editing.Session;
        session.Drawing(MarkKind.Pen);

        session.Click(2, 2);

        Assert.AreEqual(0, session.Selected);
        Assert.IsInstanceOfType<HideTool>(session.Tool);
        Assert.AreEqual(HideKind.Black, session.Hide().KindShown);

        session.Tool = session.Draw();
        Assert.IsNull(session.Selected, "Draw does not own the hide, so choosing it deselects the hide");
    }

    [TestMethod]
    public async Task AColourDraggedOnThePickerShowsOnTheSelectedMarkAsNoStepAndItsLetGoIsOneStepAndTheNextMarksColour()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        var draw = session.Drawing(MarkKind.Box);
        session.DragThrough((1, 1), (3, 3));
        var (passed, last) = (new Rgba(0x20, 0x40, 0x60, 0xFF), new Rgba(0x40, 0x60, 0x80, 0xFF));

        draw.BeginColor();
        draw.Preview(passed);
        Assert.AreEqual(passed, ((DrawMarkLayer)session.Layers.Layers.Single()).Color, "the mark shows the colour the drag passes");
        draw.Preview(last);
        Assert.AreEqual(last, draw.ColorShown);
        Assert.IsEmpty(session.Colors.Recent);
        Assert.IsTrue(draw.LetGo(last));

        CollectionAssert.AreEqual(new[] { last }, session.Colors.Recent.ToArray(), "one recent colour (G21)");
        Assert.AreEqual(last, draw.Color, "and the next mark's colour");
        session.Undo();
        Assert.AreEqual(ColorChoices.Red, ((DrawMarkLayer)session.Layers.Layers.Single()).Color, "the drag was one step (G25)");
        session.Undo();
        Assert.IsFalse(session.CanUndo, "then the box's drawing");
    }

    [TestMethod]
    public async Task WithNoMarkSelectedAColourDraggedOnThePickerChangesNoLayerAndItsLetGoSetsTheNextMarksColour()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        var draw = session.Drawing(MarkKind.Pen);
        var before = session.Layers;
        var dragged = new Rgba(0x20, 0x40, 0x60, 0xFF);

        draw.BeginColor();
        draw.Preview(dragged);
        Assert.AreEqual(before, session.Layers);
        Assert.AreEqual(ColorChoices.Red, draw.Color, "nothing chosen until the let-go");
        Assert.IsTrue(draw.LetGo(dragged));

        Assert.AreEqual(dragged, draw.Color, "the next mark's colour (G23)");
        Assert.AreEqual(before, session.Layers);
        Assert.IsFalse(session.CanUndo, "no step");
        CollectionAssert.AreEqual(new[] { dragged }, session.Colors.Recent.ToArray());

        session.DragThrough((1, 1), (3, 3));
        session.Deselect();
        draw.BeginColor();
        draw.Preview(Blue);
        session.Undo();
        Assert.IsEmpty(session.Layers.Layers, "Undo stepped back the mark drawn before the drag");
        Assert.IsFalse(draw.LetGo(Blue), "a let-go after Undo abandoned the drag applies nothing");
        Assert.AreEqual(dragged, draw.Color);
    }

    [TestMethod]
    public async Task APickedColorBecomesARecentOneAndASwatchDoesNot()
    {
        var colors = new ColorChoices();
        using var stash = new TestStash();
        await stash.OpenCleanly();
        using var session = await EditorSessions.Open(stash, await stash.ImportFixture("cat", "still.png"), colors: colors);
        var orange = new Rgba(0xFF, 0x7A, 0x1A, 0xFF);

        session.Draw().Choose(orange);
        session.Draw().Choose(Blue);

        CollectionAssert.AreEqual(new[] { orange }, colors.Recent.ToArray());
        Assert.AreEqual(Blue, session.Draw().Color);
    }

    [TestMethod]
    public async Task DrawingOnEmptyPictureWithAMarkSelectedTakesTheOptionsTheRowShowsAndUndoGivesTheMarkBackSelected()
    {
        var arrow = new DrawMarkLayer(MarkKind.Arrow, [new(2, 2), new(20, 2)], null, ColorChoices.Swatches[4], Thickness.Thick, null);
        using var editing = await StillInTheEditor.Open(List(arrow));
        var session = editing.Session;
        var draw = session.Drawing(MarkKind.Pen);
        session.Click(10, 2);
        var shown = (draw.MarkShown, draw.ColorShown, draw.ThicknessShown);
        Assert.AreEqual((MarkKind.Arrow, ColorChoices.Swatches[4], Thickness.Thick), shown);

        session.DragThrough((40, 30), (55, 40));

        var added = (DrawMarkLayer)session.SelectedLayer!;
        Assert.AreEqual(1, session.Selected);
        Assert.AreEqual(shown, (added.Kind, added.Color, added.Thickness), "the new mark is what the row showed (G20, G23)");
        CollectionAssert.AreEqual(new PixelPoint[] { new(40, 30), new(55, 40) }, added.Points.ToArray());
        Assert.AreEqual((MarkKind.Pen, ColorChoices.Red, Thickness.Medium), (draw.Mark, draw.Color, draw.Thickness), "the tool's own next mark is unchanged");

        session.Undo();
        Assert.AreEqual(arrow, session.Layers.Layers.Single());
        Assert.AreEqual(0, session.Selected, "the earlier mark selected again");
        Assert.AreEqual(shown, (draw.MarkShown, draw.ColorShown, draw.ThicknessShown), "the row showing its values");
    }
}
