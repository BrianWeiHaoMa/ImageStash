using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// Crop's frame drawn by a drag and moved by one side (G22, G24, G25; What crosses every boundary), through the session's
/// press, drag and release: a drag outside the frame, or on a picture with no crop, draws the frame over what it covers in
/// place of the old one; a drag inside a crop moves it; a press on a side's handle or a corner's, inside or outside the
/// frame, takes that handle, uncropped too; a click changes nothing; every result whole pixels inside the original, at
/// least 1 × 1, square under 1:1; a small frame still moved from its middle and by its corners; a saved crop widened.
/// </summary>
public sealed partial class CropToolTests
{
    [TestMethod]
    public async Task ADragFromOutsideTheCropDrawsANewFrameInItsPlaceAsOneStepThatUndoAndRedoReverseAndRepeat()
    {
        var old = new CropLayer(new PixelRect(20, 20, 10, 10));
        var hide = new HideLayer(new PixelRect(2, 2, 4, 4), HideKind.Blur, null);
        using var editing = await StillInTheEditor.Open(List(old, hide));
        var session = editing.Session;
        session.Tool = session.Crop();

        session.DragThrough((5, 5), (10, 10), (15.5f, 12.5f));

        var drawn = List(new CropLayer(new PixelRect(5, 5, 11, 8)), hide);
        Assert.AreEqual(drawn, session.Layers, "the pixels the drag covered, in the old crop's place (G22)");
        Assert.AreEqual(new PixelSize(11, 8), session.OutputSize, "the footer's size follows the frame (G24)");
        session.Undo();
        Assert.AreEqual(List(old, hide), session.Layers, "one step gives the old crop back (G25)");
        Assert.IsFalse(session.CanUndo);
        session.Redo();
        Assert.AreEqual(drawn, session.Layers);
    }

    [TestMethod]
    public async Task OnAnUncroppedPictureADragEitherWayDrawsTheFrameWhileACornerStillCropsInward()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Crop();

        session.DragThrough((20.2f, 10.7f), (30, 20), (40.6f, 30.1f));
        Assert.AreEqual(new CropLayer(new PixelRect(20, 10, 21, 21)), session.Layers.Layers.Single(), "the whole pixels the drag covered");
        Assert.AreEqual(new PixelSize(21, 21), session.OutputSize);
        session.Undo();
        Assert.IsTrue(session.Layers.IsEmpty, "one step");

        session.DragThrough((40.6f, 30.1f), (20.2f, 10.7f));
        Assert.AreEqual(new PixelRect(20, 10, 21, 21), session.Layers.Crop!.Rect, "dragged up and left, the same pixels");
        session.Undo();

        session.DragThrough((63.5f, 47.5f), (50, 40));
        Assert.AreEqual(new PixelRect(0, 0, 50, 40), session.Layers.Crop!.Rect, "a press on the bottom-right corner moves that corner");
    }

    [TestMethod]
    public async Task AClickOrAPressThatStaysWithinTheThresholdChangesNothingAndIsNoStep()
    {
        using var uncropped = await StillInTheEditor.Open();
        var session = uncropped.Session;
        session.Tool = session.Crop();
        session.Click(30, 20);
        session.DragThrough((30, 20), (30.3f, 20.2f));
        session.DragThrough((30, 20), (45, 35), (30.2f, 20.1f));
        Assert.IsTrue(session.Layers.IsEmpty, "no one-pixel crop on an uncropped picture, nor after a drag let go back at its press");
        Assert.IsFalse(session.CanUndo);

        var layers = List(new CropLayer(new PixelRect(20, 20, 10, 10)));
        using var cropped = await StillInTheEditor.Open(layers);
        session = cropped.Session;
        session.Tool = session.Crop();
        session.Click(5, 5);
        session.Click(25, 25);
        Assert.AreEqual(layers, session.Layers, "outside the frame and inside it");
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public async Task EachSidesHandleMovesThatSideAloneFromInsideOrOutsideTheFrameOnAnUncroppedPictureToo()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Crop();
        session.HitTolerance = 3;
        var whole = new PixelRect(0, 0, 64, 48);

        foreach (var (from, to, expected, side) in new[]
        {
            ((32f, 1f), (37f, 9f), new PixelRect(0, 8, 64, 40), "top, pressed inside, sideways ignored"),
            ((32f, 49.5f), (32f, 40f), new PixelRect(0, 0, 64, 38), "bottom, pressed outside"),
            ((-1f, 24f), (10f, 30f), new PixelRect(11, 0, 53, 48), "left, pressed outside"),
            ((63f, 24f), (50f, 24f), new PixelRect(0, 0, 51, 48), "right, pressed inside"),
        })
        {
            session.DragThrough(from, to);
            Assert.AreEqual(expected, session.Layers.Crop!.Rect, side);
            session.Undo();
            Assert.IsTrue(session.Layers.IsEmpty, $"{side}: one step");
            session.Redo();
            Assert.AreEqual(expected, session.Layers.Crop!.Rect, $"{side}: redone");
            session.Crop().Reset();
        }

        Assert.AreEqual(whole, session.Crop().Frame);
    }

    [TestMethod]
    public async Task ASideDraggedPastTheOppositeOneTurnsRoundAndOneDraggedOffThePictureStopsAtItsEdge()
    {
        using var editing = await StillInTheEditor.Open(List(new CropLayer(new PixelRect(10, 8, 20, 20))));
        var session = editing.Session;
        session.Tool = session.Crop();

        PixelRect After((float X, float Y) from, (float X, float Y) to)
        {
            session.DragThrough(from, to);
            var rect = session.Layers.Crop!.Rect;
            session.Undo();
            return rect;
        }

        Assert.AreEqual(new PixelRect(30, 8, 10, 20), After((10, 18), (40, 18)), "the left side past the right turns round about it");
        Assert.AreEqual(new PixelRect(29, 8, 1, 20), After((10, 18), (30, 18)), "on the right side, one pixel left");
        Assert.AreEqual(new PixelRect(10, 8, 54, 20), After((30, 18), (130, 18)), "the right side stops at the picture's edge");
        Assert.AreEqual(new PixelRect(10, 0, 20, 28), After((20, 8), (20, -42)), "the top side at the picture's top");
        Assert.AreEqual(new PixelRect(10, 28, 20, 20), After((20, 8), (20, 48)), "the top side past the bottom, at the picture's foot");
    }

    [TestMethod]
    public async Task ADragThatStartsOrRunsBeyondThePictureKeepsTheFrameOnItAndOneCoveringItAllLeavesNoCrop()
    {
        using var editing = await StillInTheEditor.Open(List(new CropLayer(new PixelRect(20, 20, 10, 10))));
        var session = editing.Session;
        session.Tool = session.Crop();

        session.DragThrough((-10, -5), (20.5f, 70));
        Assert.AreEqual(new PixelRect(0, 0, 21, 48), session.Layers.Crop!.Rect, "inside the original");
        session.DragThrough((-10, -10), (-5, -3));
        Assert.AreEqual(new PixelRect(0, 0, 1, 1), session.Layers.Crop!.Rect, "a drag wholly off the picture keeps one pixel of it");
        session.DragThrough((70, 60), (-5, -5));
        Assert.IsTrue(session.Layers.IsEmpty, "a frame round the whole original is no crop");
        session.Undo();
        Assert.AreEqual(new PixelRect(0, 0, 1, 1), session.Layers.Crop!.Rect, "each drag one step");
    }

    [TestMethod]
    public async Task WithOneToOneOnADrawnFrameAndEachSideDragGiveASquareInsideTheOriginal()
    {
        using var editing = await StillInTheEditor.Open(List(new CropLayer(new PixelRect(10, 10, 20, 20))));
        var session = editing.Session;
        session.Tool = session.Crop();
        session.Crop().ChooseSquare(true);
        Assert.IsFalse(session.CanUndo, "the square crop was square already");

        foreach (var (from, to, expected, gesture) in new[]
        {
            ((40f, 2f), (50.5f, 30.5f), new PixelRect(40, 2, 11, 11), "a frame drawn down and right, from its press"),
            ((60.5f, 40.5f), (45.2f, 20.2f), new PixelRect(45, 25, 16, 16), "a frame drawn up and left, from its press"),
            ((30f, 20f), (40f, 25f), new PixelRect(10, 5, 30, 30), "the right side out, centred down the frame"),
            ((20f, 10f), (20f, 0f), new PixelRect(5, 0, 30, 30), "the top side up, centred across it"),
            ((20f, 30f), (20f, 70f), new PixelRect(1, 10, 38, 38), "the bottom side to the picture's foot"),
            ((30f, 20f), (90f, 20f), new PixelRect(10, 0, 48, 48), "the right side as far as the picture is tall"),
            ((10f, 20f), (60f, 20f), new PixelRect(30, 5, 30, 30), "the left side past the right, turned round about it"),
        })
        {
            session.DragThrough(from, to);
            var rect = session.Layers.Crop!.Rect;
            Assert.AreEqual(expected, rect, gesture);
            Assert.IsTrue(rect.X >= 0 && rect.Y >= 0 && rect.X + rect.Width <= 64 && rect.Y + rect.Height <= 48, $"{gesture}: inside the original");
            session.Undo();
        }
    }

    [TestMethod]
    public async Task OnAFrameAFewPixelsAcrossAtALargeScaleTheMiddleStillMovesItAndACornerTwoSides()
    {
        var small = new PixelRect(30, 20, 4, 4);
        using var editing = await StillInTheEditor.Open(List(new CropLayer(small)));
        var session = editing.Session;
        session.Tool = session.Crop();
        session.HitTolerance = 12;

        PixelRect After((float X, float Y) from, (float X, float Y) to)
        {
            session.DragThrough(from, to);
            var rect = session.Layers.Crop!.Rect;
            session.Undo();
            Assert.AreEqual(small, session.Layers.Crop!.Rect, "one step");
            return rect;
        }

        Assert.AreEqual(new PixelRect(35, 25, 4, 4), After((32, 22), (37, 27)), "the middle moves the frame");
        Assert.AreEqual(new PixelRect(20, 10, 14, 14), After((30.5f, 20.5f), (20.5f, 10.5f)), "the top-left corner, inside, moves two sides");
        Assert.AreEqual(new PixelRect(30, 20, 9, 9), After((35, 25), (40, 30)), "the bottom-right corner, outside");
        Assert.AreEqual(new PixelRect(30, 10, 4, 14), After((32, 20.5f), (32, 10.5f)), "the top side, between the corners");
        Assert.AreEqual(new PixelRect(5, 5, 5, 5), After((5, 5), (9.5f, 9.5f)), "beyond every handle's reach a drag draws");
    }

    [TestMethod]
    public async Task APressAtEachHandleTheToolListsTakesThatHandle()
    {
        var frame = new PixelRect(10, 8, 40, 30);
        using var editing = await StillInTheEditor.Open(List(new CropLayer(frame)));
        var session = editing.Session;
        session.Tool = session.Crop();
        session.HitTolerance = 3;

        var handles = session.Crop().Handles;
        CollectionAssert.AreEquivalent(Enum.GetValues<CropHandle>(), handles.Select(handle => handle.Handle).ToArray(), "a handle at each corner and each side");
        foreach (var (handle, at) in handles)
        {
            session.DragThrough((at.X, at.Y), (at.X + 3, at.Y + 2));
            var rect = session.Layers.Crop!.Rect;
            session.Undo();
            var (left, top, right, bottom) = (rect.X - frame.X, rect.Y - frame.Y, rect.X + rect.Width - frame.X - frame.Width, rect.Y + rect.Height - frame.Y - frame.Height);
            var name = handle.ToString();
            Assert.AreEqual(name.Contains("Left", StringComparison.Ordinal) ? 3 : 0, left, $"{handle} at {at}: its left side");
            Assert.AreEqual(name.Contains("Right", StringComparison.Ordinal) ? 3 : 0, right, $"{handle} at {at}: its right side");
            Assert.AreEqual(name.StartsWith("Top", StringComparison.Ordinal) ? 2 : 0, top, $"{handle} at {at}: its top side");
            Assert.AreEqual(name.StartsWith("Bottom", StringComparison.Ordinal) ? 2 : 0, bottom, $"{handle} at {at}: its bottom side");
        }
    }

    [TestMethod]
    public async Task ADrawnFrameSavesAtItsSizeAndReopenedASideWidensIntoTheOriginalANewFrameTakesPixelsItLeftOutAndResetGivesItAllBack()
    {
        using var editing = await StillInTheEditor.Open(png: StillInTheEditor.Gradient());
        var session = editing.Session;
        var original = MediaFixtures.FirstFrame(editing.Still.FilePath);
        session.Tool = session.Crop();
        session.DragThrough((20.2f, 10.2f), (50.7f, 40.7f));
        Assert.AreEqual(new PixelSize(31, 31), session.OutputSize, "the footer states the frame's size (G24)");

        var first = await session.Save();

        Assert.AreEqual(EditEnding.Saved, first.Ending, first.Failure?.Sentence);
        var cropped = MediaFixtures.FirstFrame(first.Item!.FilePath);
        Assert.AreEqual((31, 31), (cropped.Width, cropped.Height), "the finished picture has the size the footer stated (G26)");
        Assert.AreEqual(SyntheticFrames.Pixel(original, 20, 10), SyntheticFrames.Pixel(cropped, 0, 0));

        using var again = await Open(editing.Stash, first.Item);
        again.Tool = again.Crop();
        Assert.AreEqual(new PixelRect(20, 10, 31, 31), again.Crop().Frame);
        again.DragThrough((20, 25.5f), (-30, 25.5f));
        Assert.AreEqual(new PixelRect(0, 10, 51, 31), again.Layers.Crop!.Rect, "the left side widened to the original's edge (G22)");
        again.DragThrough((5, 2), (12.5f, 8.5f));
        Assert.AreEqual(new PixelRect(5, 2, 8, 7), again.Layers.Crop!.Rect, "a new frame over pixels the saved crop left out");
        var second = await again.Save();

        Assert.AreEqual(EditEnding.Saved, second.Ending, second.Failure?.Sentence);
        var redrawn = MediaFixtures.FirstFrame(second.Item!.FilePath);
        Assert.AreEqual((8, 7), (redrawn.Width, redrawn.Height));
        Assert.AreEqual(SyntheticFrames.Pixel(original, 5, 2), SyntheticFrames.Pixel(redrawn, 0, 0));

        using var last = await Open(editing.Stash, second.Item);
        last.Tool = last.Crop();
        last.Crop().Reset();
        var whole = await last.Save();

        Assert.AreEqual(EditEnding.Saved, whole.Ending, whole.Failure?.Sentence);
        CollectionAssert.AreEqual(original.Pixels, MediaFixtures.FirstFrame(whole.Item!.FilePath).Pixels, "the whole original again");
    }
}
