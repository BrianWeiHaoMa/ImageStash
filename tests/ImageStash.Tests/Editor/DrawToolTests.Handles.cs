using System.Drawing;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// A selected mark's handles (G23, G25, G21's "as shown"; What crosses every boundary), through the session's press, drag
/// and release: a drag from either end of a selected arrow moves that end alone, its head kept on the end it was drawn to;
/// a drag from a corner of a selected box resizes it, the opposite corner kept; the body still moves the mark, a small
/// mark from its middle; a click on a handle is a click; a pen or marker stroke has no handle; every result whole pixels
/// inside the original; one step each; and Save draws what the editor drew.
/// </summary>
public sealed partial class DrawToolTests
{
    private const string ArrowHint = "Drag an end to re-aim, the arrow to move, or elsewhere to draw.";
    private const string BoxHint = "Drag a corner to resize, the box to move, or elsewhere to draw.";

    [TestMethod]
    public async Task EitherEndOfASelectedArrowMovesThatEndAloneItsHeadKeptAsOneStepEach()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Drawing(MarkKind.Arrow).Choose(Blue);
        session.Draw().Choose(Thickness.Thick);
        session.DragThrough((10.5f, 10.5f), (30.5f, 20.5f));
        session.HitTolerance = 3;
        Assert.AreEqual(ArrowHint, session.Tool.Hint);

        session.DragThrough((30.5f, 20.5f), (35, 25), (40.5f, 30.5f));
        Assert.AreEqual(Arrow((10, 10), (40, 30)), Mark(session), "the head moved, the tail exactly where it was");
        Assert.AreEqual(0, session.Selected);

        session.DragThrough((10.5f, 10.5f), (5.5f, 25.5f));
        Assert.AreEqual(Arrow((5, 25), (40, 30)), Mark(session), "the tail moved; the head is still the end drawn to");

        session.Undo();
        Assert.AreEqual(Arrow((10, 10), (40, 30)), Mark(session), "one step for the tail");
        session.Undo();
        Assert.AreEqual(Arrow((10, 10), (30, 20)), Mark(session), "one step for the head");
        session.Redo();
        session.Redo();
        Assert.AreEqual(Arrow((5, 25), (40, 30)), Mark(session));
        Assert.IsFalse(session.CanRedo);

        static DrawMarkLayer Arrow((int X, int Y) tail, (int X, int Y) head) =>
            new(MarkKind.Arrow, [new(tail.X, tail.Y), new(head.X, head.Y)], null, Blue, Thickness.Thick, null);
    }

    [TestMethod]
    [DataRow(10f, 10f, -4, -3, 6, 7, 24, 17, DisplayName = "top left")]
    [DataRow(30f, 10f, 5, -2, 10, 8, 25, 16, DisplayName = "top right")]
    [DataRow(10f, 24f, -2, 3, 8, 10, 22, 17, DisplayName = "bottom left")]
    [DataRow(30f, 24f, 3, 4, 10, 10, 23, 18, DisplayName = "bottom right")]
    public async Task EachCornerOfASelectedBoxResizesItWithTheOppositeCornerKept(float x, float y, int dx, int dy, int left, int top, int width, int height)
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Drawing(MarkKind.Box).Choose(Blue);
        session.DragThrough((10.5f, 10.5f), (29.5f, 23.5f));
        var drawn = Mark(session);
        Assert.AreEqual(new PixelRect(10, 10, 20, 14), drawn.Rect);
        session.HitTolerance = 3;
        Assert.AreEqual(BoxHint, session.Tool.Hint);

        session.DragThrough((x, y), (x + dx, y + dy));

        Assert.AreEqual(drawn with { Rect = new PixelRect(left, top, width, height) }, Mark(session), "only the rectangle changed");
        session.Undo();
        Assert.AreEqual(drawn, Mark(session), "one step");
        session.Undo();
        Assert.IsEmpty(session.Layers.Layers);
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public async Task ABoxCornerPastTheOppositeOneTurnsItRoundAndOffThePictureStopsAtItsEdge()
    {
        using var editing = await StillInTheEditor.Open(List(Box(10, 10, 20, 14)));
        var session = editing.Session;
        session.Tool = session.Draw();
        session.HitTolerance = 3;
        session.Click(20, 10);
        Assert.AreEqual(0, session.Selected);

        session.DragThrough((30, 24), (0, 4));
        Assert.AreEqual(new PixelRect(0, 4, 10, 6), Mark(session).Rect, "turned round about the kept corner");

        session.DragThrough((0, 4), (10, 10));
        Assert.AreEqual(new PixelRect(9, 9, 1, 1), Mark(session).Rect, "onto the kept corner, a pixel stays on the side it came from");

        session.DragThrough((9, 9), (200, -100));
        Assert.AreEqual(new PixelRect(10, 0, 54, 10), Mark(session).Rect, "kept inside the original");
    }

    [TestMethod]
    public async Task AnArrowEndStaysOnTheOriginalAndOneDraggedOntoTheOtherStopsAPixelShortAndStillReshapes()
    {
        var size = StillInTheEditor.Size;
        using var editing = await StillInTheEditor.Open(List(new DrawMarkLayer(MarkKind.Arrow, [new(10, 10), new(30, 20)], null, ColorChoices.Red, Thickness.Thin, null)));
        var session = editing.Session;
        session.Tool = session.Draw();
        session.HitTolerance = 3;
        session.Click(20.5f, 15.5f);
        Assert.AreEqual(0, session.Selected);

        session.DragThrough((30.5f, 20.5f), (300, 200));
        CollectionAssert.AreEqual(new PixelPoint[] { new(10, 10), new(size.Width - 1, size.Height - 1) }, Mark(session).Points.ToArray());
        session.DragThrough((10.5f, 10.5f), (-50, -50));
        CollectionAssert.AreEqual(new PixelPoint[] { new(0, 0), new(size.Width - 1, size.Height - 1) }, Mark(session).Points.ToArray());

        session.Undo();
        session.Undo();
        session.DragThrough((30.5f, 20.5f), (10.5f, 10.5f));
        CollectionAssert.AreEqual(new PixelPoint[] { new(10, 10), new(11, 10) }, Mark(session).Points.ToArray(), "a pixel short, on the side the head came from");
        Assert.AreEqual(0, session.Selected);
        Assert.IsTrue(LayerList.FromJson(session.Layers.ToJson(), "still").Succeeded, "the model keeps it");

        session.DragThrough((11.5f, 10.5f), (21.5f, 15.5f));
        CollectionAssert.AreEqual(new PixelPoint[] { new(10, 10), new(21, 15) }, Mark(session).Points.ToArray(), "its head still takes a drag");
        session.Undo();
        session.DragThrough((10.5f, 10.5f), (10.5f, 30.5f));
        CollectionAssert.AreEqual(new PixelPoint[] { new(10, 30), new(11, 10) }, Mark(session).Points.ToArray(), "and so does its tail");
        session.DeleteSelected();
        Assert.IsEmpty(session.Layers.Layers);
    }

    [TestMethod]
    public async Task TheBodyStillMovesTheMarkASmallMarkMovesFromItsMiddleAndADragBesideDrawsANewOne()
    {
        var box = Box(10, 10, 20, 14);
        var arrow = new DrawMarkLayer(MarkKind.Arrow, [new(10, 30), new(30, 40)], null, ColorChoices.Red, Thickness.Thin, null);
        var small = Box(44, 10, 2, 2);
        var shortArrow = new DrawMarkLayer(MarkKind.Arrow, [new(50, 30), new(52, 30)], null, ColorChoices.Red, Thickness.Thin, null);
        using var editing = await StillInTheEditor.Open(List(box, arrow, small, shortArrow));
        var session = editing.Session;
        session.Tool = session.Draw();
        session.HitTolerance = 3;

        session.Click(20, 10);
        session.DragThrough((20, 10), (22, 13));
        Assert.AreEqual(new PixelRect(12, 13, 20, 14), ((DrawMarkLayer)session.Layers.Layers[0]).Rect, "from a box's edge between its corners, moved whole");

        session.Click(20.5f, 35.5f);
        session.DragThrough((20.5f, 35.5f), (22.5f, 33.5f));
        CollectionAssert.AreEqual(new PixelPoint[] { new(12, 28), new(32, 38) }, ((DrawMarkLayer)session.Layers.Layers[1]).Points.ToArray(), "from an arrow's shaft, moved whole");

        session.Click(45, 11);
        Assert.AreEqual(2, session.Selected);
        session.DragThrough((45, 11), (48, 13));
        Assert.AreEqual(new PixelRect(47, 12, 2, 2), ((DrawMarkLayer)session.Layers.Layers[2]).Rect, "a box smaller than the reach moves from its middle");

        session.Click(51.5f, 30.5f);
        Assert.AreEqual(3, session.Selected);
        session.DragThrough((51.5f, 30.5f), (52.5f, 32.5f));
        CollectionAssert.AreEqual(new PixelPoint[] { new(51, 32), new(53, 32) }, ((DrawMarkLayer)session.Layers.Layers[3]).Points.ToArray(), "a short arrow moves from its middle");

        var before = session.Layers.Layers.ToArray();
        session.DragThrough((58.5f, 4.5f), (62.5f, 8.5f));
        Assert.HasCount(5, session.Layers.Layers, "beside the selected mark, beyond its handles, a drag draws");
        CollectionAssert.AreEqual(before, session.Layers.Layers.Take(4).ToArray(), "and changes no other mark");
    }

    [TestMethod]
    public async Task AClickOnAHandleReshapesNothingAndAddsNoStep()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Drawing(MarkKind.Box);
        session.DragThrough((10.5f, 10.5f), (29.5f, 23.5f));
        session.HitTolerance = 3;
        var drawn = session.Layers;

        session.Click(30, 24);
        session.Press(new PointF(10, 10));
        session.Drag(new PointF(10.5f, 10.5f));
        session.Release(new PointF(10.5f, 10.5f));

        Assert.AreEqual(drawn, session.Layers);
        Assert.AreEqual(0, session.Selected);
        session.Undo();
        Assert.IsEmpty(session.Layers.Layers, "the drawing was the only step");
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public async Task AnUnselectedArrowsEndReshapesNothing()
    {
        var arrow = new DrawMarkLayer(MarkKind.Arrow, [new(10, 10), new(30, 20)], null, ColorChoices.Red, Thickness.Thin, null);
        using var editing = await StillInTheEditor.Open(List(arrow));
        var session = editing.Session;
        session.Drawing(MarkKind.Arrow);
        session.HitTolerance = 3;

        session.DragThrough((30.5f, 20.5f), (40.5f, 30.5f));

        Assert.HasCount(2, session.Layers.Layers);
        Assert.AreEqual(arrow, session.Layers.Layers[0], "its end did not move");
        Assert.AreEqual(1, session.Selected, "the drag drew a new arrow");
    }

    [TestMethod]
    public async Task ASelectedStrokeHasNoHandleAndADragFromItsEndMovesItWhole()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Drawing(MarkKind.Marker);
        session.DragThrough((10.5f, 10.5f), (20.5f, 10.5f), (30.5f, 20.5f));
        session.HitTolerance = 3;
        Assert.IsEmpty(LayerGeometry.Handles(session.Layers, 0, StillInTheEditor.Size));
        Assert.AreEqual("Drag the selected mark to move it, or drag elsewhere to draw.", session.Tool.Hint);

        session.DragThrough((30.5f, 20.5f), (32.5f, 22.5f));

        CollectionAssert.AreEqual(new PixelPoint[] { new(12, 12), new(22, 12), new(32, 22) }, Mark(session).Points.ToArray());
    }

    [TestMethod]
    public async Task OnAnAnimationAReshapedMarkKeepsItsColourThicknessKindAndTimeRange()
    {
        var range = new FrameRange(1, 3);
        var arrow = new DrawMarkLayer(MarkKind.Arrow, [new(10, 10), new(30, 20)], null, Blue, Thickness.Thick, range);
        var box = new DrawMarkLayer(MarkKind.Box, [], new PixelRect(36, 10, 20, 14), Blue, Thickness.Thin, range);
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportPicture("clip", "clip.gif", SyntheticClips.Gif(6, 64, 48)), List(arrow, box));
        var session = clip.Session;
        session.Tool = session.Draw();
        session.HitTolerance = 3;
        session.Seek(2);
        await Until(() => session.FrameIndex == 2, "the picture shows a frame both marks are on");

        session.Click(20.5f, 15.5f);
        session.DragThrough((30.5f, 20.5f), (34.5f, 40.5f));
        session.Click(46, 10);
        session.DragThrough((56, 24), (60, 30));

        Assert.AreEqual(arrow with { Points = [new(10, 10), new(34, 40)] }, session.Layers.Layers[0]);
        Assert.AreEqual(box with { Rect = new PixelRect(36, 10, 24, 20) }, session.Layers.Layers[1]);
    }

    [TestMethod]
    public async Task SavedTheReAimedArrowAndResizedBoxAreDrawnAsTheEditorDrewThemAndReopenLive()
    {
        using var editing = await StillInTheEditor.Open(png: StillInTheEditor.Gradient());
        var session = editing.Session;
        var original = MediaFixtures.FirstFrame(editing.Still.FilePath);
        session.Drawing(MarkKind.Arrow).Choose(Blue);
        session.DragThrough((10.5f, 10.5f), (30.5f, 20.5f));
        session.Deselect();
        session.Drawing(MarkKind.Box);
        session.DragThrough((36.5f, 26.5f), (49.5f, 37.5f));
        var drawn = session.Layers;
        session.HitTolerance = 3;

        session.DragThrough((50, 38), (58, 44));
        session.Click(20.5f, 15.5f);
        session.DragThrough((30.5f, 20.5f), (40.5f, 8.5f));
        var reshaped = session.Layers;
        Assert.AreEqual(new PixelRect(36, 26, 22, 18), ((DrawMarkLayer)reshaped.Layers[1]).Rect);
        CollectionAssert.AreEqual(new PixelPoint[] { new(10, 10), new(40, 8) }, ((DrawMarkLayer)reshaped.Layers[0]).Points.ToArray());

        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        var finished = MediaFixtures.FirstFrame(saved.Item!.FilePath);
        CollectionAssert.AreEqual(Compositions.Compose(original, reshaped).Frame.Pixels, finished.Pixels, "the finished picture is the editor's drawing (R4)");
        CollectionAssert.AreNotEqual(Compositions.Compose(original, drawn).Frame.Pixels, finished.Pixels, "and not the marks as first drawn");

        using var again = await Open(editing.Stash, saved.Item);
        Assert.AreEqual(reshaped, again.Layers);
        Assert.IsFalse(again.CanUndo);
        again.Tool = again.Draw();
        again.HitTolerance = 3;
        again.Click(25.5f, 9.5f);
        Assert.AreEqual(0, again.Selected);
        again.DragThrough((10.5f, 10.5f), (12.5f, 30.5f));
        CollectionAssert.AreEqual(new PixelPoint[] { new(12, 30), new(40, 8) }, ((DrawMarkLayer)again.Layers.Layers[0]).Points.ToArray(), "live: its end still re-aims");
    }

    private static DrawMarkLayer Mark(EditorSession session) => (DrawMarkLayer)session.SelectedLayer!;
}
