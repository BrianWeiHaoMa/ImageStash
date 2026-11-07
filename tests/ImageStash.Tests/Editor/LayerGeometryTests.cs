using System.Drawing;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// Where the editor's layers answer a press (G21, G23): the latest in the list first where they overlap; a hide over its
/// rectangle; a mark only on its ink, within the hit tolerance, never in the open space of its bounding box or inside a
/// box; the outline the view draws around a selection, grown by Media's ink, which grows with the picture; a handle
/// within the tolerance of its point, a hide's, a text layer's and a caption bar's corners resizing; a mark reshaped
/// over the same pixels; a text layer hit and handled as it is turned, and turned by its round handle to the pointer's
/// angle from its centre, Shift snapping and a double-click resetting; a caption bar hit on its band; inside a layer's box
/// a handle taken only nearer than its centre, so a small one moves from its middle; a turned text moved and kept by its
/// turned bounds, its box free to start left of or above the grid.
/// </summary>
[TestClass]
public sealed class LayerGeometryTests
{
    private static readonly PixelSize Cat = new(8, 6);

    [TestMethod]
    public async Task WhereTwoMarksOverlapAClickSelectsTheLaterInTheList()
    {
        using var editing = await CatInTheEditor.Open(List(Box(0, 0, 5, 4), Box(2, 1, 5, 4)));
        var session = editing.Session;
        session.Tool = session.Draw();

        // Where the first box's right edge crosses the second's top edge.
        session.Click(5, 1);

        Assert.AreEqual(1, session.Selected);
    }

    [TestMethod]
    public async Task AClickOutsideAThinStrokesInkCountsOnlyWithinTheTolerance()
    {
        using var editing = await CatInTheEditor.Open(List(Pen(Thickness.Thin, new(1, 2), new(5, 2))));
        var session = editing.Session;
        session.Tool = session.Draw();

        // Half a pixel below the stroke's bounding box grown by its ink, which ends at 3.5.
        session.HitTolerance = 0.25f;
        session.Click(3, 4);
        Assert.IsNull(session.Selected);

        session.HitTolerance = 1;
        session.Click(3, 4);
        Assert.AreEqual(0, session.Selected);
    }

    [TestMethod]
    public async Task AClickBesideAStrokeInItsBoundingBoxSelectsNothingAndADragThereDrawsANewMark()
    {
        using var editing = await CatInTheEditor.Open(List(Pen(Thickness.Thin, new(0, 0), new(7, 5))));
        var session = editing.Session;
        session.Tool = session.Draw();
        session.HitTolerance = 0;

        session.Click(6.5f, 0.5f);
        Assert.IsNull(session.Selected);

        session.Click(4, 3);
        Assert.AreEqual(0, session.Selected, "on the stroke");
        session.DragThrough((6.5f, 0.5f), (6.5f, 2.5f));
        Assert.HasCount(2, session.Layers.Layers, "a drag starting beside the selected stroke draws a new mark");
        Assert.AreEqual(Pen(Thickness.Thin, new(0, 0), new(7, 5)), session.Layers.Layers[0]);
    }

    [TestMethod]
    public void AMarkIsUnderAPressOnlyOnItsInk()
    {
        var box = Box(1, 1, 6, 4);
        var arrow = new DrawMarkLayer(MarkKind.Arrow, [new(0, 2), new(799, 2)], null, ColorChoices.Red, Thickness.Thin, null);
        var wide = new PixelSize(800, 600);

        Assert.IsTrue(LayerGeometry.Hits(List(box), 0, new PointF(1.5f, 3), 0, Cat), "on its left edge");
        Assert.IsFalse(LayerGeometry.Hits(List(box), 0, new PointF(4, 3), 0, Cat), "inside, away from its edges");
        Assert.IsTrue(LayerGeometry.Hits(List(box), 0, new PointF(4, 3), 1, Cat), "inside, within the tolerance of its edge");
        Assert.IsTrue(LayerGeometry.Hits(List(arrow), 0, new PointF(400, 3.4f), 0, wide), "on its shaft");
        Assert.IsFalse(LayerGeometry.Hits(List(arrow), 0, new PointF(400, 4.5f), 0, wide), "beside its shaft");
        Assert.IsTrue(LayerGeometry.Hits(List(arrow), 0, new PointF(793, 5), 0, wide), "on its head, wider than its shaft");
        Assert.IsTrue(LayerGeometry.Hits(List(new HideLayer(new PixelRect(1, 1, 6, 4), HideKind.Blur, null)), 0, new PointF(4, 3), 0, Cat), "a hide is under a press anywhere over it");
    }

    [TestMethod]
    public void AStrokesAreaIsItsPixelCentresGrownByItsInkWhichGrowsWithThePicture()
    {
        var thin = Pen(Thickness.Thin, new(1, 2), new(5, 2));
        var arrow = new DrawMarkLayer(MarkKind.Arrow, [new(0, 0), new(7, 5)], null, ColorChoices.Red, Thickness.Medium, null);
        var marker = thin with { Kind = MarkKind.Marker, Thickness = Thickness.Medium };

        Assert.AreEqual(RectangleF.FromLTRB(0.5f, 1.5f, 6.5f, 3.5f), LayerGeometry.Bounds(List(thin), 0, Cat));
        Assert.AreEqual(RectangleF.FromLTRB(-7.5f, -7.5f, 15.5f, 13.5f), LayerGeometry.Bounds(List(arrow), 0, Cat), "an arrow's head is wider than its shaft");
        Assert.AreEqual(RectangleF.FromLTRB(-4.5f, -3.5f, 11.5f, 8.5f), LayerGeometry.Bounds(List(marker), 0, Cat));
        Assert.AreEqual(RectangleF.FromLTRB(-0.5f, 0.5f, 7.5f, 4.5f), LayerGeometry.Bounds(List(thin), 0, new PixelSize(1600, 1200)));
        Assert.AreEqual(RectangleF.FromLTRB(-1, -1, 4, 4), LayerGeometry.Bounds(List(Box(0, 0, 3, 3)), 0, Cat));
        Assert.AreEqual(new RectangleF(1, 1, 3, 3), LayerGeometry.Bounds(List(new HideLayer(new PixelRect(1, 1, 3, 3), HideKind.Blur, null)), 0, Cat));
        Assert.IsNull(LayerGeometry.Bounds(List(new ShrinkLayer(Shrink.Half)), 0, Cat));
    }

    [TestMethod]
    public void AHandleIsHitWithinTheToleranceOfItsPoint()
    {
        var hide = new HideLayer(new PixelRect(1, 1, 4, 3), HideKind.Blur, null);
        var arrow = new DrawMarkLayer(MarkKind.Arrow, [new(0, 0), new(7, 5)], null, ColorChoices.Red, Thickness.Medium, null);

        Assert.AreEqual(Handle.BottomRight, LayerGeometry.HandleAt(List(hide), 0, new PointF(5.4f, 4), 0.5f, Cat));
        Assert.IsNull(LayerGeometry.HandleAt(List(hide), 0, new PointF(5.6f, 4), 0.5f, Cat));
        Assert.AreEqual(Handle.TopLeft, LayerGeometry.HandleAt(List(hide), 0, new PointF(1, 1.2f), 0.5f, Cat));
        Assert.AreEqual(Handle.End, LayerGeometry.HandleAt(List(arrow), 0, new PointF(7.5f, 5.5f), 0.5f, Cat));
        Assert.AreEqual(Handle.Start, LayerGeometry.HandleAt(List(arrow), 0, new PointF(0.2f, 0.3f), 0.5f, Cat));
        Assert.IsEmpty(LayerGeometry.Handles(List(Pen(Thickness.Thin, new(1, 2), new(5, 2))), 0, Cat), "a stroke shows its outline");
        Assert.IsTrue(LayerGeometry.Resizes(hide));
        Assert.IsFalse(LayerGeometry.Resizes(arrow), "a mark is only moved (G23); its handles show the selection");
        Assert.AreEqual(arrow, LayerGeometry.Resized(List(arrow), 0, Handle.End, 2, 2, Cat));
    }

    [TestMethod]
    public void AMarkReshapedKeepsItsPixels()
    {
        var pen = Pen(Thickness.Thin, new(1, 1), new(4, 3), new(2, 5));
        var box = Box(1, 1, 4, 3);

        Assert.AreEqual(new PixelRect(1, 1, 4, 5), LayerGeometry.Reshaped(pen, MarkKind.Box).Rect);
        Assert.IsEmpty(LayerGeometry.Reshaped(pen, MarkKind.Box).Points);
        CollectionAssert.AreEqual(new PixelPoint[] { new(1, 1), new(2, 5) }, LayerGeometry.Reshaped(pen, MarkKind.Arrow).Points.ToArray());
        CollectionAssert.AreEqual(pen.Points.ToArray(), LayerGeometry.Reshaped(pen, MarkKind.Marker).Points.ToArray());
        CollectionAssert.AreEqual(new PixelPoint[] { new(1, 1), new(4, 3) }, LayerGeometry.Reshaped(box, MarkKind.Arrow).Points.ToArray());
        CollectionAssert.AreEqual(new PixelPoint[] { new(1, 1), new(4, 1), new(4, 3), new(1, 3), new(1, 1) }, LayerGeometry.Reshaped(box, MarkKind.Pen).Points.ToArray());
        Assert.IsNull(LayerGeometry.Reshaped(box, MarkKind.Pen).Rect);
        foreach (var kind in Enum.GetValues<MarkKind>())
        {
            _ = new LayerList([LayerGeometry.Reshaped(pen, kind), LayerGeometry.Reshaped(box, kind)]);
        }
    }

    [TestMethod]
    public async Task AMarkChosenWithAStrokeSelectedReshapesItAsOneStep()
    {
        using var editing = await CatInTheEditor.Open();
        var session = editing.Session;
        session.Drawing(MarkKind.Pen);
        session.DragThrough((1.5f, 1.5f), (4.5f, 3.5f));

        session.Draw().Choose(MarkKind.Box);

        Assert.AreEqual(new PixelRect(1, 1, 4, 3), ((DrawMarkLayer)session.Layers.Layers.Single()).Rect);
        session.Undo();
        Assert.AreEqual(MarkKind.Pen, ((DrawMarkLayer)session.Layers.Layers.Single()).Kind);
    }

    [TestMethod]
    public void AMovedStrokeStaysOnThePicture()
    {
        var pen = Pen(Thickness.Thin, new(1, 1), new(4, 3));

        CollectionAssert.AreEqual(new PixelPoint[] { new(4, 3), new(7, 5) }, ((DrawMarkLayer)LayerGeometry.Moved(pen, 10, 10, Cat)).Points.ToArray());
        CollectionAssert.AreEqual(new PixelPoint[] { new(0, 0), new(3, 2) }, ((DrawMarkLayer)LayerGeometry.Moved(pen, -10, -10, Cat)).Points.ToArray());
    }

    [TestMethod]
    public void ATurnedTextLayerIsHitWhereItIsDrawnAndItsHandlesTurnWithIt()
    {
        var still = new PixelSize(64, 48);
        var label = new TextLayer(TextPreset.Label, "turned", "Segoe UI", 4, ColorChoices.Swatches[0], Outline.Auto, new PixelRect(20, 10, 30, 6), 90, null);
        var list = List(label);

        Assert.IsFalse(LayerGeometry.Hits(list, 0, new PointF(49, 13), 0, still), "the unrotated right end is not where it is drawn");
        Assert.IsTrue(LayerGeometry.Hits(list, 0, new PointF(35, 25), 0, still), "inside the turned box");
        Assert.AreEqual(new RectangleF(20, 10, 30, 6), LayerGeometry.Bounds(list, 0, still), "the box before its rotation, which the view turns");
        Assert.AreEqual(90, LayerGeometry.Rotation(label));
        Assert.IsTrue(LayerGeometry.Resizes(label));

        var handles = LayerGeometry.Handles(list, 0, still);
        Assert.HasCount(5, handles);
        var turned = handles.Single(handle => handle.Handle == Handle.Rotate).At;
        Assert.IsTrue(turned.X > 35 + 3 && Math.Abs(turned.Y - 13) < 1e-3, $"right of the centre at 90°: {turned}");
        var upright = LayerGeometry.Handles(List(label with { Rotation = 0 }), 0, still).Single(handle => handle.Handle == Handle.Rotate).At;
        Assert.IsTrue(upright.Y < 10 && Math.Abs(upright.X - 35) < 1e-3, $"above the top's centre at 0°: {upright}");
        var corner = handles.Single(handle => handle.Handle == Handle.TopLeft).At;
        Assert.IsTrue(Math.Abs(corner.X - 38) < 1e-3 && Math.Abs(corner.Y - (-2)) < 1e-3, $"the top-left corner turned about the centre: {corner}");
        Assert.AreEqual(Handle.Rotate, LayerGeometry.HandleAt(list, 0, turned, 0.5f, still));
        Assert.AreEqual(0, LayerGeometry.Rotation(new HideLayer(new PixelRect(1, 1, 2, 2), HideKind.Blur, null)));
    }

    [TestMethod]
    public void TextIsUnderAPressOnlyOverThePictureMediaClipsItTo()
    {
        var still = new PixelSize(64, 48);
        var bar = new CaptionBarLayer("me", "Segoe UI", 6, ColorChoices.Swatches[1], Outline.None);
        var turned = new TextLayer(TextPreset.Label, "turned", "Segoe UI", 4, ColorChoices.Swatches[0], Outline.Auto, new PixelRect(20, 10, 30, 6), 90, null);
        var list = List(bar, turned);

        Assert.IsFalse(LayerGeometry.Hits(list, 1, new PointF(35, -1), 0, still), "its turned box reaches above the picture, where nothing of it is drawn");
        Assert.AreEqual(0, LayerGeometry.TopmostAt(list, new PointF(35, -1), 0, still), "the caption's band there is the caption's");
        Assert.IsTrue(LayerGeometry.Hits(list, 1, new PointF(35, 1), 0, still), "on the picture it is drawn");
    }

    [TestMethod]
    public void ACaptionBarIsHitOnItsBandWhichItsFourCornersResize()
    {
        var still = new PixelSize(64, 48);
        var list = List(new HideLayer(new PixelRect(1, 1, 2, 2), HideKind.Blur, null), new CaptionBarLayer("me, later", "Segoe UI", 6, ColorChoices.Swatches[1], Outline.None));
        var band = LayerComposer.Bands(still.Width, still.Height, list).Single().Band;
        Assert.AreEqual((0, 64, 0), (band.X, band.Width, band.Y + band.Height), "Media's band, above the picture");

        Assert.IsTrue(LayerGeometry.Hits(list, 1, new PointF(10, -band.Height / 2f), 0, still));
        Assert.IsFalse(LayerGeometry.Hits(list, 1, new PointF(10, 1), 0, still), "the picture below it is not the bar");
        Assert.AreEqual(1, LayerGeometry.TopmostAt(list, new PointF(10, -band.Height / 2f), 0, still));
        Assert.AreEqual(new RectangleF(band.X, band.Y, band.Width, band.Height), LayerGeometry.Bounds(list, 1, still));
        CollectionAssert.AreEqual(
            new[] { (Handle.TopLeft, new PointF(0, band.Y)), (Handle.TopRight, new PointF(64, band.Y)), (Handle.BottomLeft, new PointF(0, 0)), (Handle.BottomRight, new PointF(64, 0)) },
            LayerGeometry.Handles(list, 1, still).ToArray(),
            "four corners and no round handle");
        Assert.IsTrue(LayerGeometry.Resizes(list.Layers[1]));
        Assert.AreSame(list.Layers[1], LayerGeometry.Moved(list.Layers[1], 5, 5, still), "a caption bar is never moved");
        Assert.AreSame(list.Layers[1], LayerGeometry.Turned(list.Layers[1], new PointF(60, 0), snapped: false), "nor turned");
    }


    [TestMethod]
    public async Task TheRoundHandleTurnsTheLayerToThePointersAngleFromItsCentre()
    {
        using var editing = await StillInTheEditor.Open(List(TextToolTests.Meme("hello", 10, new PixelRect(4, 10, 56, 15))));
        var session = editing.Session;
        session.Click(30, 17);
        var centre = new PointF(32, 17.5f);
        var handle = RotateHandle(session);
        Assert.AreEqual(centre.X, handle.X, 1e-4, "above the centre");
        Assert.IsLessThan(10, handle.Y, "above the box");

        // Pressed beside the handle's point, within the tolerance: the angle is the pointer's from the centre, not the drag's turn from its start (3° less here).
        session.DragThrough((handle.X + 0.8f, handle.Y), (handle.X + 10, handle.Y + 2), (60, 17.5f));
        Assert.AreEqual(90, Rotation(session), 1);

        Through(session, RotateHandle(session), At(centre, 30, 20), At(centre, 40, 20));
        Assert.AreEqual(40, Rotation(session), 1, "without Shift, as the pointer is");

        session.ShiftHeld = true;
        Through(session, RotateHandle(session), At(centre, 20, 20), At(centre, 40, 20));
        Assert.AreEqual(45, Rotation(session), 1e-9, "with Shift, the nearest 15° step");
    }

    [TestMethod]
    public async Task ARoundHandleBeyondTheViewsRoomIsPulledBackAlongItsLineAndStillTurnsAndResetsTheText()
    {
        using var editing = await StillInTheEditor.Open(List(new TextLayer(TextPreset.Label, "hi", "Segoe UI", 20, ColorChoices.Swatches[0], Outline.Auto, new PixelRect(20, 0, 24, 30), 0, null)));
        var session = editing.Session;
        var room = RectangleF.FromLTRB(-4, -4, 68, 52);
        session.HitTolerance = 1;
        session.HandleRoom = room;
        session.Click(32, 15);

        var free = LayerGeometry.Handles(session.Layers, 0, StillInTheEditor.Size).Single(handle => handle.Handle == Handle.Rotate).At;
        var pulled = LayerGeometry.Handles(session.Layers, 0, StillInTheEditor.Size, room).Single(handle => handle.Handle == Handle.Rotate).At;
        Assert.AreEqual(new PointF(32, -15), free, "three quarters of the font size above the box");
        Assert.AreEqual(new PointF(32, -4), pulled, "pulled down its line from the centre to the room's top, still above the box (G21)");
        Assert.AreEqual(Handle.Rotate, LayerGeometry.HandleAt(session.Layers, 0, pulled, 0.5f, StillInTheEditor.Size, room));
        CollectionAssert.AreEqual(LayerGeometry.Handles(session.Layers, 0, StillInTheEditor.Size).ToArray(), LayerGeometry.Handles(session.Layers, 0, StillInTheEditor.Size, RectangleF.FromLTRB(-20, -20, 84, 68)).ToArray(), "a handle inside the room stays where it is");

        session.DragThrough((pulled.X, pulled.Y), (50, 15), (60, 15));
        Assert.AreEqual(90, Rotation(session), 1e-9, "pressed where the view shows it, it turns the text");
        var turned = LayerGeometry.Handles(session.Layers, 0, StillInTheEditor.Size, room).Single(handle => handle.Handle == Handle.Rotate).At;
        Assert.IsTrue(room.Contains(turned), $"the turned handle in the room: {turned}");
        session.DoubleClick(turned);
        Assert.AreEqual(0, Rotation(session), "and a double-click there resets it");

        var outside = LayerGeometry.Handles(List(new TextLayer(TextPreset.Label, "hi", "Segoe UI", 20, ColorChoices.Swatches[0], Outline.Auto, new PixelRect(20, 0, 24, 30), 0, null)), 0, StillInTheEditor.Size, RectangleF.FromLTRB(0, 40, 64, 48)).Single(handle => handle.Handle == Handle.Rotate).At;
        Assert.AreEqual(new PointF(32, 40), outside, "a room the box's centre lies outside of takes the handle to its nearest point");
    }

    [TestMethod]
    public async Task ATurnedTextWithEveryCornerBeyondTheViewsRoomHasThemPulledIntoItWhereADragResizesIt()
    {
        var turned = new TextLayer(TextPreset.Label, "WWWWWWWWWW", "Segoe UI", 8, ColorChoices.Swatches[0], Outline.Auto, new PixelRect(0, 20, 75, 10), 90, null);
        using var editing = await StillInTheEditor.Open(List(turned));
        var session = editing.Session;
        var room = RectangleF.FromLTRB(-4, -4, 68, 52);
        session.HitTolerance = 1;
        session.HandleRoom = room;
        session.Click(37.5f, 25);
        Assert.AreEqual(0, session.Selected);
        bool InRoom(PointF at) => at.X >= room.Left && at.X <= room.Right && at.Y >= room.Top && at.Y <= room.Bottom;
        Assert.IsFalse(LayerGeometry.Handles(session.Layers, 0, StillInTheEditor.Size).Any(handle => handle.Handle < Handle.Start && InRoom(handle.At)), "turned, every corner lies beyond the room");

        var corners = LayerGeometry.Handles(session.Layers, 0, StillInTheEditor.Size, room).Where(handle => handle.Handle < Handle.Start).ToList();
        Assert.IsTrue(corners.All(corner => InRoom(corner.At)), "a corner handle resizes it (G21): pulled into the room, " + string.Join(", ", corners));
        var corner = corners.Single(each => each.Handle == Handle.BottomRight).At;
        session.DragThrough((corner.X, corner.Y), (corner.X, corner.Y + 3), (corner.X, corner.Y + 6));
        Assert.IsGreaterThan(8, ((TextLayer)session.Layers.Layers.Single()).FontSize, "dragged outward along its line, it grows the text");
    }

    [TestMethod]
    public async Task ARoundHandlePulledOntoACornerIsTheOnePressedAsItIsDrawnOverIt()
    {
        var turned = TextToolTests.Meme("big", 100, new PixelRect(0, 0, 200, 240)) with { Rotation = 45 };
        using var editing = await StillInTheEditor.Open(List(turned));
        var session = editing.Session;
        var room = RectangleF.FromLTRB(-4, -4, 68, 52);
        session.HitTolerance = 1;
        session.HandleRoom = room;
        session.Click(60, 45);
        Assert.AreEqual(0, session.Selected);
        var handles = LayerGeometry.Handles(session.Layers, 0, StillInTheEditor.Size, room);
        var round = handles.Single(handle => handle.Handle == Handle.Rotate).At;
        Assert.IsTrue(handles.Any(handle => handle.Handle != Handle.Rotate && handle.At == round), "a corner pulled to the round handle's place");

        Assert.AreEqual(Handle.Rotate, LayerGeometry.HandleAt(session.Layers, 0, round, 1, StillInTheEditor.Size, room), "the round handle, drawn over the corner, is the one pressed");
        session.DragThrough((round.X, round.Y), (round.X - 4, round.Y + 10), (round.X - 8, round.Y + 20));
        Assert.AreEqual(100, ((TextLayer)session.Layers.Layers.Single()).FontSize, "turned, not resized");
        Assert.AreNotEqual(45, Rotation(session));
        session.DoubleClick(LayerGeometry.Handles(session.Layers, 0, StillInTheEditor.Size, room).Single(handle => handle.Handle == Handle.Rotate).At);
        Assert.AreEqual(0, Rotation(session), "and a double-click there resets it (G21)");
    }

    [TestMethod]
    public async Task ShiftHeldPartWayThroughATurnSnapsFromThenOn()
    {
        using var editing = await StillInTheEditor.Open(List(TextToolTests.Meme("hello", 10, new PixelRect(4, 10, 56, 15))));
        var session = editing.Session;
        session.Click(30, 17);
        var centre = new PointF(32, 17.5f);
        var handle = RotateHandle(session);

        session.Press(handle);
        session.Drag(At(centre, 20, 20));
        session.Drag(At(centre, 40, 20));
        Assert.AreEqual(40, Rotation(session), 1, "shown live");
        session.ShiftHeld = true;
        session.Drag(At(centre, 41, 20));
        Assert.AreEqual(45, Rotation(session), 1e-9);
        session.Release(At(centre, 41, 20));

        Assert.AreEqual(45, Rotation(session), 1e-9);
        session.Undo();
        Assert.AreEqual(0, Rotation(session), "the turn is one step");
    }

    [TestMethod]
    public async Task ATurnEndingOnTheCentreKeepsTheAngleItHad()
    {
        using var editing = await StillInTheEditor.Open(List(TextToolTests.Meme("hello", 10, new PixelRect(4, 10, 56, 15)) with { Rotation = 30 }));
        var session = editing.Session;
        session.Click(32, 17.5f);
        var handle = RotateHandle(session);

        session.DragThrough((handle.X, handle.Y), (32, 17.5f));

        Assert.AreEqual(30, Rotation(session), "no direction from the centre itself, never NaN");
    }

    [TestMethod]
    public async Task AClickOnTheRoundHandleOverAnotherLayerKeepsTheTextSelectedSoADoubleClickThereResetsIt()
    {
        var turned = TextToolTests.Meme("hello", 10, new PixelRect(4, 10, 56, 15)) with { Rotation = 45 };
        using var editing = await StillInTheEditor.Open(List(turned, new HideLayer(new PixelRect(38, 2, 10, 10), HideKind.Blur, null)));
        var session = editing.Session;
        session.Click(32, 17.5f);
        var handle = RotateHandle(session);
        Assert.AreEqual(1, LayerGeometry.TopmostAt(session.Layers, handle, 0, StillInTheEditor.Size), "the hide lies under the round handle");

        session.Click(handle.X, handle.Y);
        Assert.AreEqual(0, session.Selected, "a click on the round handle is the handle's, not a click on what lies beneath");
        session.DoubleClick(handle);

        Assert.AreEqual(0, Rotation(session), "resetting on double-click (G21)");
    }

    [TestMethod]
    public async Task ADoubleClickOnTheRoundHandleTurnsTheLayerBackAsOneStep()
    {
        using var editing = await StillInTheEditor.Open(List(TextToolTests.Meme("hello", 10, new PixelRect(4, 10, 56, 15)) with { Rotation = 45 }));
        var session = editing.Session;
        session.Click(32, 17.5f);
        var handle = RotateHandle(session);

        session.Click(handle.X, handle.Y);
        session.DoubleClick(handle);

        Assert.AreEqual(0, Rotation(session));
        Assert.IsFalse(session.Typing, "the handle's double-click resets, it does not edit");
        Assert.HasCount(1, session.Layers.Layers, "its first click added nothing");
        session.Undo();
        Assert.AreEqual(45, Rotation(session));
    }
    [TestMethod]
    public async Task TheMiddleOfAHideSmallerThanThePressReachMovesItWhileItsCornersStillResizeIt()
    {
        using var editing = await StillInTheEditor.Open(List(new HideLayer(new PixelRect(20, 20, 8, 8), HideKind.Black, null)));
        var session = editing.Session;
        session.Tool = session.Hide();
        // The canvas's own reach, eight of its units, on a canvas showing the picture at its size.
        session.HitTolerance = CanvasMapping.Of(64, 48, session.ShownLayers, new Avalonia.Size(64, 48), 1)!.ToOriginal(EditorCanvas.PressReach);
        Assert.AreEqual(8f, session.HitTolerance);
        session.Click(24, 24);

        session.DragThrough((24, 24), (34, 29));
        Assert.AreEqual(new PixelRect(30, 25, 8, 8), ((HideLayer)session.SelectedLayer!).Rect, "its middle moves it (G23)");

        session.Undo();
        session.DragThrough((29, 29), (33, 33));
        Assert.AreEqual(new PixelRect(20, 20, 12, 12), ((HideLayer)session.SelectedLayer!).Rect, "a press beside its corner, within the reach, resizes it");
        session.Undo();
        session.DragThrough((21, 21), (17, 17));
        Assert.AreEqual(new PixelRect(16, 16, 12, 12), ((HideLayer)session.SelectedLayer!).Rect, "and so does one inside it nearer its corner than its middle");
    }

    [TestMethod]
    public void ACropFramesCornerIsTakenInsideItOnlyNearerThanItsMiddle()
    {
        var frame = new PixelRect(20, 20, 8, 8);
        Assert.IsNull(LayerGeometry.CornerAt(frame, new PointF(24, 24), 8), "a small frame's middle is for moving it");
        Assert.AreEqual(Handle.TopLeft, LayerGeometry.CornerAt(frame, new PointF(21, 21), 8));
        Assert.AreEqual(Handle.BottomRight, LayerGeometry.CornerAt(frame, new PointF(29, 29), 8), "outside it, the whole reach");
        Assert.AreEqual(Handle.BottomRight, LayerGeometry.CornerAt(new PixelRect(0, 0, 60, 40), new PointF(55, 35), 8), "a large frame's corners reach inside as before");
    }

    [TestMethod]
    public async Task ATurnedTextMovesIntoTheSpaceItVisiblyHasItsBoxThenStartingLeftOfTheGridAndASaveRendersIt()
    {
        var label = TextToolTests.Label("abcdef", 8, 0, 19) with { Rotation = 90 };
        Assert.AreEqual(new PixelRect(0, 19, 24, 10), label.Box, "Segoe UI's measure, as the case was set up");
        Assert.AreEqual(RectangleF.FromLTRB(7, 12, 17, 36), TextPlacement.Bounds(label.Box, label.Rotation), "turned, it shows from x 7 to 17");
        using var editing = await StillInTheEditor.Open(List(label));
        var session = editing.Session;
        session.HitTolerance = 8;
        session.Click(12, 24);

        session.DragThrough((12, 24), (7, 24));
        var moved = (TextLayer)session.SelectedLayer!;
        Assert.AreEqual(label.Box with { X = -5 }, moved.Box, "five pixels left, still wholly on the picture as it shows (G21)");
        var list = new LayerList(session.Layers.Layers);
        Assert.AreEqual(list, LayerList.FromJson(list.ToJson(), "still").Value, "the list holding it is one the model admits, and stores");

        session.Undo();
        session.DragThrough((12, 24), (-18, 24));
        Assert.AreEqual(label.Box with { X = -7 }, ((TextLayer)session.SelectedLayer!).Box, "no further than its turned bounds reach the picture's left edge");
        session.Undo();
        session.DragThrough((12, 24), (7, 24));

        var saved = await session.Save();
        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        var picture = MediaFixtures.FirstFrame(saved.Item!.FilePath);
        int Lettered(int left, int right)
        {
            var count = 0;
            for (var y = 12; y < 36; y++)
            {
                for (var x = left; x < right; x++)
                {
                    var at = (y * picture.Width + x) * 4;
                    count += picture.Pixels[at] != 0x80 || picture.Pixels[at + 1] != 0x80 || picture.Pixels[at + 2] != 0x80 ? 1 : 0;
                }
            }

            return count;
        }

        Assert.IsGreaterThan(0, Lettered(2, 12), "its letters drawn where it now shows");
        Assert.AreEqual(0, Lettered(13, 18), "and none where it showed before the move");
    }

    [TestMethod]
    public async Task ATurnedTextLongerThanThePictureMovesWithoutJumpingAndAReturnToNoTurnKeepsItOnThePicture()
    {
        // Turned a quarter, its letters run from above the picture to below it, its centre on it.
        var tall = new TextLayer(TextPreset.Label, "abcdefghijklmnopqrstuvwxyz", "Segoe UI", 8, ColorChoices.Swatches[0], Outline.Auto, new PixelRect(-68, 0, 200, 10), 90, null);
        using var editing = await StillInTheEditor.Open(List(tall));
        var session = editing.Session;
        session.HitTolerance = 8;
        session.Click(32, 20);

        session.DragThrough((32, 20), (12, 20));
        Assert.AreEqual(new PixelRect(-88, 0, 200, 10), ((TextLayer)session.SelectedLayer!).Box, "moved as far as asked, and not along the axis it covers whole (G21)");

        session.DoubleClick(RotateHandle(session));
        var reset = (TextLayer)session.SelectedLayer!;
        Assert.AreEqual(0, reset.Rotation, "the double-click resets the turn (G21)");
        Assert.AreEqual(new PixelRect(-88, 0, 200, 10), reset.Box, "unturned, it still covers the picture's width, so it stays where it was");
        Assert.AreEqual(0, LayerGeometry.TopmostAt(session.Layers, new PointF(10, 5), 0, StillInTheEditor.Size), "and its letters are still on the picture to be pressed");

        session.Undo();
        session.Undo();
        Assert.AreEqual(tall, session.Layers.Layers.Single());
        var handle = RotateHandle(session);
        session.DragThrough((handle.X, handle.Y), (32, -40));
        var upright = (TextLayer)session.SelectedLayer!;
        Assert.AreEqual(0, upright.Rotation);
        Assert.AreEqual(new PixelRect(-68, 0, 200, 10), upright.Box, "turned upright by its handle, it covers the picture's width as it was");
    }

    [TestMethod]
    public async Task ATurnLeavesATextsBoxWhereItIsOnItsCentreAndTheNextMoveBringsItsTurnedBoxInside()
    {
        // At the left edge, a label turned upright by its handle sticks out on the left, its centre still on the picture.
        var label = TextToolTests.Label("abcdef", 8, 0, 19) with { Box = new PixelRect(-7, 19, 24, 10), Rotation = 90 };
        using var editing = await StillInTheEditor.Open(List(label));
        var session = editing.Session;
        session.HitTolerance = 8;
        session.Click(5, 24);
        var handle = RotateHandle(session);

        session.DragThrough((handle.X, handle.Y), (5, -10));
        var turned = (TextLayer)session.SelectedLayer!;
        Assert.AreEqual((0d, new PixelRect(-7, 19, 24, 10)), (turned.Rotation, turned.Box), "a turn moves nothing (G21)");
        Assert.AreEqual(0, LayerGeometry.TopmostAt(session.Layers, new PointF(5, 24), 0, StillInTheEditor.Size), "and the text is still under its centre");

        session.DragThrough((5, 24), (10, 24));
        Assert.AreEqual(new PixelRect(0, 19, 24, 10), ((TextLayer)session.SelectedLayer!).Box, "a move keeps its box, now unturned, inside");
    }

    [TestMethod]
    public async Task ATurnedTextLongerThanThePictureIsMovedNoFurtherThanKeepsItsCentreOnItSoATurnBackKeepsItInReach()
    {
        var tall = new TextLayer(TextPreset.Label, "abcdefghijklmnopqrstuvwxyz", "Segoe UI", 8, ColorChoices.Swatches[0], Outline.Auto, new PixelRect(-68, 0, 200, 10), 90, null);
        using var editing = await StillInTheEditor.Open(List(tall));
        var session = editing.Session;
        session.HitTolerance = 8;
        session.Click(32, 20);

        session.DragThrough((32, 20), (32, 220));
        Assert.AreEqual(new PixelRect(-68, 43, 200, 10), ((TextLayer)session.SelectedLayer!).Box, "down only until its centre reaches the picture's bottom edge");

        session.DoubleClick(RotateHandle(session));
        Assert.AreEqual(0, ((TextLayer)session.SelectedLayer!).Rotation);
        Assert.AreEqual(0, LayerGeometry.TopmostAt(session.Layers, new PointF(32, 45), 0, StillInTheEditor.Size), "turned back, its letters are still on the picture");
    }

    [TestMethod]
    public async Task AnUnturnedTextAHideAndABoxMovedLeftStopAtThePicturesEdge()
    {
        var label = TextToolTests.Label("abcdef", 8, 0, 19);
        using var editing = await StillInTheEditor.Open(List(label, new HideLayer(new PixelRect(0, 2, 10, 10), HideKind.Blur, null), Box(0, 40, 10, 6)));
        var session = editing.Session;
        session.Click(12, 24);
        session.DragThrough((12, 24), (7, 24));
        session.Click(5, 7);
        session.DragThrough((5, 7), (0, 2));
        session.Click(5, 40);
        session.DragThrough((5, 40), (0, 40));

        Assert.AreEqual(label.Box, ((TextLayer)session.Layers.Layers[0]).Box, "unturned, its bounds are its box, which stays at 0");
        Assert.AreEqual(new PixelRect(0, 0, 10, 10), ((HideLayer)session.Layers.Layers[1]).Rect, "a hide covers only the picture");
        Assert.AreEqual(new PixelRect(0, 40, 10, 6), ((DrawMarkLayer)session.Layers.Layers[2]).Rect, "and so does a box");
    }

    private static double Rotation(EditorSession session) => LayerGeometry.Rotation(session.SelectedLayer!);

    private static PointF RotateHandle(EditorSession session) =>
        LayerGeometry.Handles(session.Layers, session.Selected!.Value, StillInTheEditor.Size).Single(handle => handle.Handle == Handle.Rotate).At;

    /// <summary>A press at the first point, moved through the others, released at the last.</summary>
    private static void Through(EditorSession session, params PointF[] points) => session.DragThrough([.. points.Select(point => (point.X, point.Y))]);

    /// <summary>The point <paramref name="radius"/> from <paramref name="centre"/> at <paramref name="degrees"/> clockwise from straight up.</summary>
    private static PointF At(PointF centre, double degrees, float radius) =>
        new((float)(centre.X + radius * Math.Sin(degrees * Math.PI / 180)), (float)(centre.Y - radius * Math.Cos(degrees * Math.PI / 180)));

    private static DrawMarkLayer Pen(Thickness thickness, params PixelPoint[] points) => new(MarkKind.Pen, points, null, ColorChoices.Red, thickness, null);
}
