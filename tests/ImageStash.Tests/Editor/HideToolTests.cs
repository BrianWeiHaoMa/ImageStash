using System.Drawing;
using ImageStash.Layers;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// Hide (G23; What crosses every boundary): a dragged rectangle becomes a hide in whole pixels covering the drag, either
/// way round, kept inside the picture, blurred until Black box is chosen; a press closer than the drag threshold draws
/// nothing; the selected hide moved by a drag inside it and resized by its corners, never below 1 × 1 nor off the
/// picture; Black box applied to the selected hide as one step and to the next; a new hide takes the style the option
/// row shows, the selected hide's (G20).
/// </summary>
[TestClass]
public sealed class HideToolTests
{
    [TestMethod]
    public async Task ADragBecomesASelectedBlurCoveringItInWholePixels()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Hide();

        session.Press(new PointF(1.2f, 1.4f));
        session.Drag(new PointF(4.6f, 3.9f));
        session.Release(new PointF(4.6f, 3.9f));

        Assert.AreEqual(new HideLayer(new PixelRect(1, 1, 4, 3), HideKind.Blur, null), session.Layers.Layers.Single());
        Assert.AreEqual(0, session.Selected);
    }

    [TestMethod]
    public async Task TheSameDragTheOtherWayRoundGivesTheSameRectangle()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Hide();

        session.DragThrough((4.6f, 3.9f), (1.2f, 1.4f));

        Assert.AreEqual(new PixelRect(1, 1, 4, 3), ((HideLayer)session.Layers.Layers.Single()).Rect);
    }

    [TestMethod]
    public async Task ADragLeavingThePictureEndsAtItsEdge()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Hide();

        session.DragThrough((1.2f, 1.4f), (20, 20));
        session.DragThrough((-3, -2), (0.5f, 0.5f));
        session.DragThrough((9, 1), (12, 2));

        CollectionAssert.AreEqual(
            new[] { new PixelRect(1, 1, 7, 5), new PixelRect(0, 0, 1, 1), new PixelRect(7, 1, 1, 1) },
            session.Layers.Layers.Cast<HideLayer>().Select(hide => hide.Rect).ToArray());
    }

    [TestMethod]
    public async Task APressReleasedWithinTheThresholdOnEmptyPictureDrawsNothingAndKeepsTheSelection()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (3, 3));

        session.Press(new PointF(6.1f, 5.1f));
        session.Release(new PointF(6.3f, 5.1f));

        Assert.HasCount(1, session.Layers.Layers);
        Assert.AreEqual(0, session.Selected);
    }

    [TestMethod]
    public async Task BlackBoxMakesTheNextHideBlack()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Hide();

        session.Hide().Choose(HideKind.Black);
        session.DragThrough((1, 1), (3, 3));

        Assert.AreEqual(HideKind.Black, ((HideLayer)session.Layers.Layers.Single()).Kind);
    }

    [TestMethod]
    public async Task ADragInsideTheSelectedHideMovesItItsSizeKept()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Hide();
        session.DragThrough((1.2f, 1.4f), (4.6f, 3.9f));

        session.DragThrough((2.5f, 2.5f), (3.5f, 3), (4.5f, 3.5f));

        Assert.AreEqual(new PixelRect(3, 2, 4, 3), ((HideLayer)session.Layers.Layers.Single()).Rect);
        Assert.AreEqual(0, session.Selected);
    }

    [TestMethod]
    public async Task AMovedHideStaysOnThePicture()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));

        session.DragThrough((2, 2), (30, -30));

        Assert.AreEqual(new PixelRect(5, 0, 3, 2), ((HideLayer)session.Layers.Layers.Single()).Rect);
    }

    [TestMethod]
    public async Task ItsBottomRightHandleGrowsItTheOriginKept()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Hide();
        session.DragThrough((1.2f, 1.4f), (4.6f, 3.9f));

        session.DragThrough((5.2f, 4.1f), (6.2f, 5.1f));

        Assert.AreEqual(new PixelRect(1, 1, 5, 4), ((HideLayer)session.Layers.Layers.Single()).Rect);
    }

    [TestMethod]
    public async Task ItsTopLeftHandleDraggedPastTheFarCornerNeverGivesASizeBelowOneOrAPlaceOffThePicture()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (5, 4));

        session.DragThrough((1, 1), (3, 3), (5, 4), (5.4f, 4.4f));
        var onTheCorner = ((HideLayer)session.Layers.Layers.Single()).Rect;
        session.Undo();
        session.DragThrough((1, 1), (40, 40));
        var beyond = ((HideLayer)session.Layers.Layers.Single()).Rect;
        session.Undo();
        session.DragThrough((1, 1), (-9, -9));
        var outside = ((HideLayer)session.Layers.Layers.Single()).Rect;

        foreach (var rect in new[] { onTheCorner, beyond, outside })
        {
            Assert.IsTrue(rect.Width >= 1 && rect.Height >= 1 && rect.X >= 0 && rect.Y >= 0, rect.ToString());
            Assert.IsTrue(rect.X + rect.Width <= 8 && rect.Y + rect.Height <= 6, rect.ToString());
        }

        Assert.AreEqual(new PixelRect(4, 3, 1, 1), onTheCorner, "the corner on the far one leaves the pixel beside it");
        Assert.AreEqual(new PixelRect(5, 4, 3, 2), beyond, "turned round, the dragged corner at the picture's edge");
        Assert.AreEqual(new PixelRect(0, 0, 5, 4), outside);
    }

    [TestMethod]
    public async Task BlackBoxChosenWithAHideSelectedChangesItAsOneStepAndTheNext()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (3, 3));

        session.Hide().Choose(HideKind.Black);
        Assert.AreEqual(HideKind.Black, ((HideLayer)session.Layers.Layers.Single()).Kind);
        Assert.AreEqual(HideKind.Black, session.Hide().KindShown);
        session.DragThrough((5, 4), (7, 6));
        Assert.AreEqual(HideKind.Black, ((HideLayer)session.Layers.Layers[1]).Kind);

        session.Undo();
        session.Undo();
        Assert.AreEqual(HideKind.Blur, ((HideLayer)session.Layers.Layers.Single()).Kind);
    }

    [TestMethod]
    public async Task ANewHideTakesTheBlackBoxTheRowShowsForTheSelectedHideAndWithNoneSelectedTheToolsOwnStyle()
    {
        using var editing = await StillInTheEditor.Open(List(new HideLayer(new PixelRect(2, 2, 20, 15), HideKind.Black, null)));
        var session = editing.Session;
        session.Tool = session.Hide();
        session.Click(12, 9);
        Assert.AreEqual(HideKind.Black, session.Hide().KindShown, "the row shows the selected hide's Black box");

        session.DragThrough((40, 30), (55, 40));

        Assert.AreEqual(new HideLayer(new PixelRect(40, 30, 15, 10), HideKind.Black, null), session.SelectedLayer, "the new hide is what the row showed, and selected");
        Assert.AreEqual(HideKind.Blur, session.Hide().Kind, "choosing nothing left the tool's own style as it was");

        session.Deselect();
        Assert.AreEqual(HideKind.Blur, session.Hide().KindShown);
        session.DragThrough((30, 2), (36, 8));
        Assert.AreEqual(HideKind.Blur, ((HideLayer)session.SelectedLayer!).Kind, "with nothing selected, the row and the drag are the tool's own style");
    }
}
