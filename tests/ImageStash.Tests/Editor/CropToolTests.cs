using System.Drawing;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// Crop (G20, G22, G24, G25; What crosses every boundary): second on the tool row of every item; the frame's corners
/// resizing the rectangle and a drag on it moving it, on the original's grid, inside the picture and at least 1 × 1,
/// each one step; 1:1 squaring it and keeping it square; Reset giving the whole picture back; a rectangle covering the
/// whole original never stored; the crop never selected, by a click under any tool, nor removed by Del; the footer's
/// size, the saved file and a later widening following the crop; while Crop is current the whole original shown round
/// the frame, the original alone while Compare is held; a Meme placed and grown on the cropped picture staying on it.
/// </summary>
[TestClass]
public sealed class CropToolTests
{
    private static readonly PixelRect Whole = new(0, 0, StillInTheEditor.Size.Width, StillInTheEditor.Size.Height);

    [TestMethod]
    public async Task CropIsSecondOnTheToolRowOfAStillAndOfAnAnimation()
    {
        using var still = await StillInTheEditor.Open();
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var clip = await stash.ImportFixture("clip", "animated.gif");
        using var animated = await Open(stash, clip);

        CollectionAssert.AreEqual(new[] { "Text", "Crop", "Hide", "Draw" }, still.Session.Tools.Select(tool => tool.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Text", "Crop", "Trim", "Hide", "Draw" }, animated.Tools.Select(tool => tool.Name).ToArray());
        Assert.AreEqual("Drag the corners, or the picture", still.Session.Crop().Hint, "what the mouse does (G20)");
    }

    [TestMethod]
    public async Task ChosenAndSavedAtOnceItStoresNothingAndTheItemStaysUnedited()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        var commits = await EditedItems.Commits(editing.Stash.Store!, editing.Still.Id);

        session.Tool = session.Crop();
        Assert.AreEqual(Whole, session.Crop().Frame, "the frame round the whole picture");
        Assert.IsTrue(session.Layers.IsEmpty);
        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        Assert.AreEqual(commits, await EditedItems.Commits(editing.Stash.Store!, editing.Still.Id), "nothing committed");
        Assert.IsTrue((await EditedItems.ForEdit(editing.Stash.Store!, editing.Still.Id)).Layers.IsEmpty);
        Assert.IsEmpty(EditedItems.OriginalFiles(editing.Stash), "one file, the original");
    }

    [TestMethod]
    public async Task TheTopLeftCornerDraggedInCropsOnTheOriginalsGridTheFooterFollowsAndOneUndoGivesTheWholePictureBack()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Crop();

        session.DragThrough((0, 0), (4, 3), (7.6f, 6.2f), (10, 8));

        Assert.AreEqual(new CropLayer(new PixelRect(10, 8, 54, 40)), session.Layers.Layers.Single());
        Assert.AreEqual(new PixelSize(54, 40), session.OutputSize, "the footer's size (G24)");
        Assert.AreEqual(new PixelRect(10, 8, 54, 40), session.Crop().Frame);
        Assert.IsNull(session.Selected, "the crop is never selected");
        session.Undo();
        Assert.IsTrue(session.Layers.IsEmpty, "the drag was one step (G25)");
        Assert.AreEqual(new PixelSize(64, 48), session.OutputSize);
        Assert.IsFalse(session.CanUndo, "and only one");
    }

    [TestMethod]
    public async Task ADragOnTheRectangleMovesItOverTheFixedPictureALayerStaysAndAtTheEdgeItStopsItsSizeKept()
    {
        var hide = new HideLayer(new PixelRect(20, 20, 10, 10), HideKind.Blur, null);
        using var editing = await StillInTheEditor.Open(List(hide, new CropLayer(new PixelRect(10, 8, 54, 40))));
        var session = editing.Session;
        session.Tool = session.Crop();

        session.DragThrough((40, 30), (37, 28), (35, 26));
        Assert.AreEqual(List(hide, new CropLayer(new PixelRect(5, 4, 54, 40))), session.Layers, "moved by the drag, the hide where it was (G22)");
        session.DragThrough((40, 30), (20, 30), (-30, 30));
        Assert.AreEqual(List(hide, new CropLayer(new PixelRect(0, 4, 54, 40))), session.Layers, "stopped at the left edge, its size kept");

        session.Undo();
        Assert.AreEqual(new PixelRect(5, 4, 54, 40), session.Layers.Crop!.Rect, "each drag one step");
    }

    [TestMethod]
    public async Task ACornerDraggedAcrossItsOppositeOneTurnsRoundNeverBelowOneByOneAndOneDraggedOffThePictureStopsAtItsEdges()
    {
        using var editing = await StillInTheEditor.Open(List(new CropLayer(new PixelRect(10, 8, 20, 20))));
        var session = editing.Session;
        session.Tool = session.Crop();

        session.DragThrough((10, 8), (40, 38));
        var across = session.Layers.Crop!.Rect;
        session.Undo();
        session.DragThrough((10, 8), (30, 28));
        var onTheCorner = session.Layers.Crop!.Rect;
        session.Undo();
        session.DragThrough((30, 28), (100, 90));
        var beyond = session.Layers.Crop!.Rect;
        session.Undo();
        session.DragThrough((10, 8), (-20, -15));
        var outside = session.Layers.Crop!.Rect;

        Assert.AreEqual(new PixelRect(30, 28, 10, 10), across, "turned round about the kept corner");
        Assert.AreEqual(new PixelRect(29, 27, 1, 1), onTheCorner, "on the far corner, one pixel left");
        Assert.AreEqual(new PixelRect(10, 8, 54, 40), beyond, "the dragged corner at the picture's bottom right");
        Assert.AreEqual(new PixelRect(0, 0, 30, 28), outside, "and at its top left");
    }

    [TestMethod]
    public async Task ACornerDraggedBackToThePicturesEdgesLeavesNoCropAtAll()
    {
        using var editing = await StillInTheEditor.Open(List(new CropLayer(new PixelRect(10, 8, 54, 40))));
        var session = editing.Session;
        session.Tool = session.Crop();

        session.DragThrough((10, 8), (5, 4), (0, 0));

        Assert.IsTrue(session.Layers.IsEmpty, "a crop of the whole original is never stored");
        Assert.AreEqual(Whole, session.Crop().Frame);
    }

    [TestMethod]
    public async Task OneToOneSquaresTheRectangleAboutItsCentreAsOneStepAndKeepsItSquareAsACornerDrags()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Crop();

        session.Crop().ChooseSquare(true);
        Assert.IsTrue(session.Crop().Square);
        Assert.AreEqual(new CropLayer(new PixelRect(8, 0, 48, 48)), session.Layers.Layers.Single(), "the square of the shorter side about the centre, inside the picture");
        session.Undo();
        Assert.IsTrue(session.Layers.IsEmpty, "one step");
        session.Redo();

        session.DragThrough((56, 48), (50, 46), (46, 44));
        Assert.AreEqual(new PixelRect(8, 0, 38, 38), session.Layers.Crop!.Rect, "the shorter side the drag gave, from the kept top-left corner");
        session.DragThrough((20, 20), (30, 22), (38, 24));
        Assert.AreEqual(new PixelRect(26, 4, 38, 38), session.Layers.Crop!.Rect, "a move keeps it square, stopped at the right edge");
        session.DragThrough((26, 4), (60, 60), (100, 100));
        var crossed = session.Layers.Crop!.Rect;
        Assert.AreEqual(crossed.Width, crossed.Height, $"a corner dragged across the far one keeps it square: {crossed}");
        session.Undo();

        var undone = session.CanRedo;
        session.Crop().ChooseSquare(true);
        Assert.AreEqual(new PixelRect(26, 4, 38, 38), session.Layers.Crop!.Rect, "squaring a square changes nothing");
        Assert.AreEqual(undone, session.CanRedo, "and is no step");
        session.Crop().ChooseSquare(false);
        session.DragThrough((64, 42), (62, 42), (61, 42));
        Assert.AreEqual(new PixelRect(26, 4, 35, 38), session.Layers.Crop!.Rect, "1:1 off, the corners free again");
    }

    [TestMethod]
    public async Task ResetAfterOneToOneLeavesNoCropWithOneToOneOffAndTheFooterAtTheWholePicture()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Tool = session.Crop();
        session.Crop().ChooseSquare(true);

        session.Crop().Reset();

        Assert.IsTrue(session.Layers.IsEmpty);
        Assert.IsFalse(session.Crop().Square);
        Assert.AreEqual(new PixelSize(64, 48), session.OutputSize);
        session.Undo();
        Assert.AreEqual(new PixelRect(8, 0, 48, 48), session.Layers.Crop!.Rect, "Reset was one step");
    }

    [TestMethod]
    public async Task UnderDrawAClickInsideTheCropSelectsNothingAndUnderHideADragThereDrawsAHide()
    {
        var crop = new CropLayer(new PixelRect(10, 8, 54, 40));
        using var editing = await StillInTheEditor.Open(List(crop));
        var session = editing.Session;

        session.Tool = session.Draw();
        session.Click(30, 30);
        Assert.IsNull(session.Selected, "the crop is no mark under the click (G23)");
        Assert.AreSame(session.Draw(), session.Tool, "and Draw stays current");
        Assert.AreEqual(List(crop), session.Layers);

        session.Tool = session.Hide();
        session.DragThrough((20, 20), (25, 25), (30, 30));
        Assert.AreEqual(List(crop, new HideLayer(new PixelRect(20, 20, 10, 10), HideKind.Blur, null)), session.Layers, "a new hide, the crop not moved");

        session.Tool = session.Text();
        session.Text().Choose(TextKind.Label);
        session.Deselect();
        session.Click(12, 40);
        Assert.IsTrue(session.SelectedLayer is TextLayer, "a click on the cropped picture adds a label, never selects the crop");
    }

    [TestMethod]
    public async Task UnderCropAClickOnALayerSelectsNothingAndDelChangesNothing()
    {
        var layers = List(new HideLayer(new PixelRect(20, 20, 10, 10), HideKind.Blur, null), new CropLayer(new PixelRect(10, 8, 54, 40)));
        using var editing = await StillInTheEditor.Open(layers);
        var session = editing.Session;
        session.Tool = session.Hide();
        session.Click(25, 25);
        Assert.AreEqual(0, session.Selected);

        session.Tool = session.Crop();
        Assert.IsNull(session.Selected, "choosing Crop leaves nothing selected");
        session.Click(25, 25);
        session.Click(2, 2);
        session.DeleteSelected();

        Assert.IsNull(session.Selected);
        Assert.AreSame(session.Crop(), session.Tool, "Crop stays current");
        Assert.AreEqual(layers, session.Layers, "nothing removed (G25)");
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public async Task AClickBesideTheCroppedPictureSelectsNothingItCutAwayAndOneOnWhatShowsStillSelects()
    {
        var mark = Box(17, 16, 1, 4);
        var hide = new HideLayer(new PixelRect(2, 2, 5, 5), HideKind.Black, null);
        var across = Box(15, 24, 10, 3);
        using var editing = await StillInTheEditor.Open(List(mark, hide, across, new CropLayer(new PixelRect(20, 10, 20, 20))));
        var session = editing.Session;
        session.Tool = session.Draw();

        session.Click(18, 18);
        Assert.IsNull(session.Selected, "the box the crop cut away is no mark under the click (G23)");
        session.Click(4, 4);
        Assert.IsNull(session.Selected, "nor the hide");
        session.DeleteSelected();
        Assert.HasCount(4, session.Layers.Layers, "so Del removes nothing unseen");
        session.Click(24.5f, 24.5f);
        Assert.AreEqual(2, session.Selected, "the box across the crop's edge selects where it shows");
    }

    [TestMethod]
    public async Task AMarkOrHideTheCropCutAwayWholeIsNotSelectedByAClickOnThePictureWithinReachOfIt()
    {
        var layers = List(Box(14, 18, 1, 8), new HideLayer(new PixelRect(42, 12, 4, 4), HideKind.Blur, null), new CropLayer(new PixelRect(20, 10, 20, 20)));
        using var editing = await StillInTheEditor.Open(layers);
        var session = editing.Session;
        session.Tool = session.Draw();
        session.HitTolerance = 6;

        session.Click(20, 22);
        Assert.IsNull(session.Selected, "the box draws nothing on the picture, so it is under no click there (G23)");
        session.Click(39.5f, 14);
        Assert.IsNull(session.Selected, "nor the hide");
        session.DeleteSelected();
        Assert.AreEqual(layers, session.Layers, "and Del removes neither");
    }

    [TestMethod]
    public async Task ATextTheCropCutAwayWholeIsUnderNoClickOnThePictureWithinReachOfItSoTheClickAddsText()
    {
        var cut = new TextLayer(TextPreset.Label, "cut", "Segoe UI", 6, ColorChoices.Swatches[0], Outline.Auto, new PixelRect(2, 10, 16, 8), 0, null);
        // Turned a quarter, its box's corners reach past the crop's top edge though the box before the turn does not.
        var turned = new TextLayer(TextPreset.Label, "turned", "Segoe UI", 4, ColorChoices.Swatches[0], Outline.Auto, new PixelRect(22, 2, 16, 4), 90, null);
        var layers = List(cut, turned, new CropLayer(new PixelRect(20, 10, 20, 20)));
        using var editing = await StillInTheEditor.Open(layers);
        var session = editing.Session;
        session.HitTolerance = 6;

        session.Tool = session.Draw();
        session.Click(20.5f, 14);
        Assert.IsNull(session.Selected, "nothing of the label shows, so it is under no click on the picture");
        Assert.AreSame(session.Draw(), session.Tool, "and Draw stays current");
        session.DeleteSelected();
        Assert.AreEqual(layers, session.Layers, "so Del removes nothing unseen (G25)");
        session.Click(30, 10.5f);
        Assert.AreEqual(1, session.Selected, "a turned text across the crop's edge selects where it shows");

        session.Deselect();
        session.Tool = session.Text();
        session.Text().Choose(TextKind.Label);
        session.Click(20.5f, 14);
        Assert.HasCount(4, session.Layers.Layers, "the click on the cropped picture adds a label (G21)");
        Assert.AreEqual(3, session.Selected);
    }

    [TestMethod]
    public async Task OnAPictureCroppedShorterThanAMemesMarginTheMemeStillLiesAtTheEdgeItWasPutAt()
    {
        var thin = List(new CropLayer(new PixelRect(0, 20, 64, 2)));
        foreach (var (y, top) in new[] { (20.25f, true), (21.75f, false) })
        {
            using var editing = await StillInTheEditor.Open(thin);
            var session = editing.Session;

            session.Click(32, y);
            session.SetTypedText("HELLO");
            session.EndTyping();

            var box = ((TextLayer)session.Layers.Layers[1]).Box;
            Assert.AreEqual(top ? 20 : 22, top ? box.Y : box.Y + box.Height, $"at the {(top ? "top" : "bottom")} edge of the two rows shown (G21): {box}");
        }
    }

    [TestMethod]
    public async Task ADragFromBesideTheCroppedPictureDrawsOnIt()
    {
        var crop = new CropLayer(new PixelRect(20, 10, 20, 20));
        using var editing = await StillInTheEditor.Open(List(crop));
        var session = editing.Session;

        session.Tool = session.Hide();
        session.DragThrough((5, 15), (15, 20), (25, 25));
        Assert.AreEqual(new PixelRect(20, 15, 5, 10), ((HideLayer)session.Layers.Layers[1]).Rect, "the hide begins at the picture's edge, where it shows");
        session.Drawing(MarkKind.Box);
        session.DragThrough((25, 2), (50, 40));
        Assert.AreEqual(new PixelRect(25, 10, 15, 20), ((DrawMarkLayer)session.Layers.Layers[2]).Rect!.Value, "a box inside the picture");
        session.Deselect();
        session.Drawing(MarkKind.Pen);
        session.DragThrough((5, 12), (30, 12), (60, 12));
        CollectionAssert.AreEqual(new[] { new PixelPoint(20, 12), new PixelPoint(30, 12), new PixelPoint(39, 12) }, ((DrawMarkLayer)session.Layers.Layers[3]).Points.ToArray(), "a stroke's points on the picture");
        session.Deselect();
        session.Drawing(MarkKind.Arrow);
        session.DragThrough((0, 0), (63, 47));
        CollectionAssert.AreEqual(new[] { new PixelPoint(20, 10), new PixelPoint(39, 29) }, ((DrawMarkLayer)session.Layers.Layers[4]).Points.ToArray(), "an arrow's ends on the picture");
    }

    [TestMethod]
    public async Task ADragOutsideTheRectangleChangesNothing()
    {
        var layers = List(new CropLayer(new PixelRect(20, 20, 10, 10)));
        using var editing = await StillInTheEditor.Open(layers);
        var session = editing.Session;
        session.Tool = session.Crop();

        session.DragThrough((5, 5), (10, 10), (15, 12));

        Assert.AreEqual(layers, session.Layers);
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public async Task WhileCropIsCurrentTheWholeOriginalShowsRoundTheFrameAndWhileCompareIsHeldTheOriginalAlone()
    {
        var hide = new HideLayer(new PixelRect(20, 20, 10, 10), HideKind.Black, null);
        var layers = List(hide, new CropLayer(new PixelRect(10, 8, 54, 40)));
        using var editing = await StillInTheEditor.Open(layers, StillInTheEditor.Gradient());
        var session = editing.Session;

        Assert.AreEqual(layers, session.ShownLayers, "under Text, the cropped picture");
        Assert.IsNull(session.SampleColor(new(2, 2)), "outside the crop is off the picture");
        session.Tool = session.Crop();
        Assert.AreEqual(List(hide), session.ShownLayers, "under Crop, the whole original with the layers but the crop (G22)");
        Assert.AreEqual(new Rgba(8, 10, 90, 255), session.SampleColor(new(2, 2)), "the picture as the canvas shows it");
        session.Compare = true;
        Assert.AreEqual(LayerList.Empty, session.ShownLayers, "the original alone (G20)");
    }

    [TestMethod]
    public async Task AMemeOnACroppedPictureLandsOnItAndGrownAndShrunkStaysOnIt()
    {
        using var editing = await StillInTheEditor.Open(List(new CropLayer(new PixelRect(0, 20, 64, 20))));
        var session = editing.Session;

        session.Click(10, 36);
        var placed = ((TextLayer)session.SelectedLayer!).Box;
        Assert.IsTrue(placed.Y >= 20 && placed.Y + placed.Height <= 40, $"a bottom Meme placed on the cropped picture: {placed}");
        session.SetTypedText("one\ntwo\nthree\nfour");
        var grown = ((TextLayer)session.SelectedLayer!).Box;
        Assert.IsLessThan(20, grown.Y, $"grown above the cropped picture's top: {grown}");
        Assert.AreEqual(placed.Y + placed.Height, grown.Y + grown.Height, "upward, its bottom kept");
        session.SetTypedText("one");
        Assert.AreEqual(placed, ((TextLayer)session.SelectedLayer!).Box, "shrunk back where it was (G21)");

        session.SetTypedText("one\ntwo\nthree\nfour");
        session.EndTyping();
        session.BeginTyping();
        session.SetTypedText("one");
        Assert.AreEqual(placed, ((TextLayer)session.SelectedLayer!).Box, "shrunk in a later field, still at the cropped picture's bottom");
        session.EndTyping();

        session.Deselect();
        session.Click(10, 22);
        var top = ((TextLayer)session.SelectedLayer!).Box;
        Assert.IsTrue(top.Y >= 20 && top.Y + top.Height <= 40, $"a top Meme placed on the cropped picture: {top}");
        session.SetTypedText("one\ntwo\nthree");
        var below = ((TextLayer)session.SelectedLayer!).Box;
        Assert.IsGreaterThan(40, below.Y + below.Height, $"grown past the cropped picture's bottom: {below}");
        Assert.AreEqual(top.Y, below.Y, "downward, its top kept");
        session.EndTyping();
        session.BeginTyping();
        session.SetTypedText("one");
        Assert.AreEqual(top, ((TextLayer)session.SelectedLayer!).Box, "a top Meme grown past the bottom and shrunk stays at the top");
    }

    [TestMethod]
    public async Task SavedTheCropIsItsRectangleOfTheOriginalAndAWiderCropLaterShowsWhatItHadCut()
    {
        using var editing = await StillInTheEditor.Open(png: StillInTheEditor.Gradient());
        var session = editing.Session;
        var original = MediaFixtures.FirstFrame(editing.Still.FilePath);
        session.Tool = session.Crop();
        session.DragThrough((0, 0), (5, 4), (10, 8));

        var first = await session.Save();

        Assert.AreEqual(EditEnding.Saved, first.Ending, first.Failure?.Sentence);
        var cropped = MediaFixtures.FirstFrame(first.Item!.FilePath);
        Assert.AreEqual((54, 40), (cropped.Width, cropped.Height));
        Assert.AreEqual(SyntheticFrames.Pixel(original, 10, 8), SyntheticFrames.Pixel(cropped, 0, 0));
        Assert.AreEqual(SyntheticFrames.Pixel(original, 63, 47), SyntheticFrames.Pixel(cropped, 53, 39));

        using var again = await Open(editing.Stash, first.Item);
        again.Tool = again.Crop();
        Assert.AreEqual(new PixelRect(10, 8, 54, 40), again.Crop().Frame, "the crop as saved");
        again.DragThrough((10, 8), (7, 5), (4, 2));
        var widened = await again.Save();

        Assert.AreEqual(EditEnding.Saved, widened.Ending, widened.Failure?.Sentence);
        var wider = MediaFixtures.FirstFrame(widened.Item!.FilePath);
        Assert.AreEqual((60, 46), (wider.Width, wider.Height));
        Assert.AreEqual(SyntheticFrames.Pixel(original, 4, 2), SyntheticFrames.Pixel(wider, 0, 0), "a pixel the first crop had cut (G22)");

        using var last = await Open(editing.Stash, widened.Item);
        last.Tool = last.Crop();
        last.Crop().Reset();
        var whole = await last.Save();

        Assert.AreEqual(EditEnding.Saved, whole.Ending, whole.Failure?.Sentence);
        CollectionAssert.AreEqual(original.Pixels, MediaFixtures.FirstFrame(whole.Item!.FilePath).Pixels, "the whole original again");
    }
}
