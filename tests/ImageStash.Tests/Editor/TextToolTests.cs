using System.Drawing;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// The Text tool (G20, G21, G22, F7, F8): first on the tool row and current at open; a click on the picture adding a
/// layer of the current preset with that preset's own defaults, a Meme at the top or the bottom by the click's half, a
/// Label at the click, a caption bar above the picture, while a click on a layer still selects it; a drag moving a text
/// layer and never drawing one; a corner scaling the font with a Meme's width or a caption's band, the opposite corner
/// kept; the round handle turning a text layer from its centre, Shift snapping to 15° and a double-click resetting it; a
/// preset chosen converting the selected layer; the font, fill and outline changing the selected layer only.
/// </summary>
[TestClass]
public sealed class TextToolTests
{
    private static readonly PixelSize Still = StillInTheEditor.Size;
    private static readonly Rgba White = ColorChoices.Swatches[0];
    private static readonly Rgba NearBlack = ColorChoices.Swatches[1];
    private static readonly Rgba Yellow = ColorChoices.Swatches[2];
    private static readonly Rgba Blue = ColorChoices.Swatches[4];

    [TestMethod]
    public async Task OpensWithTextCurrentOnMemeAndNothingOfALayerShown()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        var text = session.Text();

        CollectionAssert.AreEqual(new[] { "Text", "Crop", "Hide", "Draw" }, session.Tools.Select(tool => tool.Name).ToArray());
        Assert.AreSame(text, session.Tool);
        Assert.AreEqual(TextKind.Meme, text.Kind);
        Assert.AreEqual(TextKind.Meme, text.KindShown);
        Assert.IsNull(text.FontShown);
        Assert.IsNull(text.FillShown);
        Assert.IsNull(text.OutlineShown);
        Assert.AreEqual("Click the picture to add text", text.Hint);
        Assert.IsFalse(session.Typing);
    }

    [TestMethod]
    public async Task AClickInTheUpperHalfAddsAMemeAtTheTopWithItsFieldOpen()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;

        session.Click(10, 5);

        var meme = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual(new TextLayer(TextPreset.Meme, "", "Impact", meme.FontSize, White, Outline.Auto, meme.Box, 0, null), meme);
        Assert.IsGreaterThan(0, meme.Box.X, "a margin at the left");
        Assert.AreEqual(Still.Width - 2 * meme.Box.X, meme.Box.Width, "the picture's width less a margin on each side");
        Assert.AreEqual(meme.Box.X, meme.Box.Y, "at the top, the same margin above it");
        Assert.IsLessThan(Still.Height / 2, meme.Box.Y + meme.Box.Height, "above the middle");
        Assert.AreEqual(LayerComposer.TextSize(meme).Height, meme.Box.Height);
        Assert.AreEqual(0, session.Selected);
        Assert.IsTrue(session.Typing);
        Assert.IsFalse(session.CanUndo, "the add is no step until its field closes");
        Assert.AreNotEqual("Click the picture to add text", session.Text().Hint, "with a text layer selected the row says what the mouse does with it");
    }

    [TestMethod]
    public async Task AClickInTheLowerHalfAddsAMemeWhoseBottomIsAtTheMarginAboveThePicturesBottom()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;

        session.Click(10, 40);

        var meme = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual(TextPreset.Meme, meme.Preset);
        Assert.AreEqual(meme.Box.X, Still.Height - (meme.Box.Y + meme.Box.Height), "the side margin below it, not at the top");
    }

    [TestMethod]
    public async Task ALabelIsAddedAtTheClickInSegoeUiAndACaptionBarAfterTheOtherLayersAboveThePicture()
    {
        var hide = new HideLayer(new PixelRect(40, 30, 5, 5), HideKind.Blur, null);
        using var editing = await StillInTheEditor.Open(List(hide));
        var session = editing.Session;
        var text = session.Text();

        text.Choose(TextKind.Label);
        session.Click(30, 20);

        var label = (TextLayer)session.Layers.Layers[1];
        Assert.AreEqual(new TextLayer(TextPreset.Label, "", "Segoe UI", label.FontSize, White, Outline.Auto, label.Box, 0, null), label);
        Assert.AreEqual(new PixelPoint(30, 20), new PixelPoint(label.Box.X, label.Box.Y));
        Assert.AreEqual(LayerComposer.TextSize(label), new PixelSize(label.Box.Width, label.Box.Height));
        Assert.AreEqual(TextKind.Label, text.KindShown);

        text.Choose(TextKind.CaptionBar);
        session.Click(1, 1);

        Assert.HasCount(2, session.Layers.Layers, "the empty label left with its field; the hide stays");
        var bar = (CaptionBarLayer)session.Layers.Layers[1];
        Assert.AreEqual(new CaptionBarLayer("", "Segoe UI", bar.FontSize, NearBlack, Outline.None), bar, "black text on the white bar, no outline (G21)");
        Assert.AreEqual(1, session.Selected);
        Assert.IsTrue(session.Typing);
        var band = LayerComposer.Bands(Still.Width, Still.Height, session.Layers).Single().Band;
        Assert.AreEqual(new PixelSize(Still.Width, Still.Height + band.Height), session.OutputSize, "the bar extends the picture's height");
        Assert.AreEqual(TextKind.CaptionBar, text.KindShown);
    }

    [TestMethod]
    public async Task EachPresetStartsFromItsOwnDefaultsNotFromTheLastChoices()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        var text = session.Text();
        session.Click(10, 5);
        session.SetTypedText("first");
        text.Choose("Comic Sans MS");
        text.ChooseFill(Blue);
        text.ChooseOutline(Outline.None);

        session.Deselect();
        session.Click(10, 40);

        var next = (TextLayer)session.Layers.Layers[1];
        Assert.AreEqual(("Impact", White, Outline.Auto), (next.Font, next.Fill, next.Outline), "F8: a Meme starts in Impact; G21: Meme defaults to Auto");
        Assert.AreEqual(("Comic Sans MS", Blue, Outline.None), (((TextLayer)session.Layers.Layers[0]).Font, ((TextLayer)session.Layers.Layers[0]).Fill, ((TextLayer)session.Layers.Layers[0]).Outline));
    }

    [TestMethod]
    public async Task AClickOnALayerSelectsItAndAClickBesideThePictureAddsNothing()
    {
        var hide = new HideLayer(new PixelRect(40, 30, 10, 10), HideKind.Blur, null);
        using var editing = await StillInTheEditor.Open(List(hide));
        var session = editing.Session;

        session.Click(45, 35);
        Assert.AreEqual(0, session.Selected);
        Assert.AreSame(session.Hide(), session.Tool, "a click on a layer selects it, as built");
        Assert.HasCount(1, session.Layers.Layers);

        session.Tool = session.Text();
        session.Click(70, 20);
        session.Click(20, -3);
        Assert.HasCount(1, session.Layers.Layers, "beside the picture nothing is added");
        Assert.IsFalse(session.Typing);
    }

    [TestMethod]
    public async Task ADragOnOpenPictureChangesNothingAndKeepsTheSelection()
    {
        using var editing = await StillInTheEditor.Open(List(Meme("hello", 10, new PixelRect(4, 10, 56, 15))));
        var session = editing.Session;
        session.Click(30, 17);
        var before = session.Layers;

        session.DragThrough((2, 40), (10, 45), (20, 46));

        Assert.AreEqual(before, session.Layers);
        Assert.AreEqual(0, session.Selected);
        Assert.IsFalse(session.CanUndo);
        Assert.IsFalse(session.Typing);
    }

    [TestMethod]
    public async Task ADragFromInsideASelectedTextLayerMovesItAsOneStep()
    {
        using var editing = await StillInTheEditor.Open(List(Meme("hello", 10, new PixelRect(4, 10, 56, 15))));
        var session = editing.Session;
        session.Click(30, 17);

        session.DragThrough((30, 17), (31.5f, 18), (33, 19));

        var moved = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual(new PixelRect(7, 12, 56, 15), moved.Box);
        Assert.AreEqual(10, moved.FontSize);
        session.Undo();
        Assert.AreEqual(new PixelRect(4, 10, 56, 15), ((TextLayer)session.Layers.Layers.Single()).Box, "one step");
    }

    [TestMethod]
    public async Task ADragStartingOnATextLayerNotSelectedSelectsAndMovesItAsOneStep()
    {
        using var editing = await StillInTheEditor.Open(List(Meme("hello", 10, new PixelRect(4, 10, 56, 15))));
        var session = editing.Session;
        Assert.IsNull(session.Selected);

        session.DragThrough((30, 17), (31.5f, 18), (33, 19));

        Assert.AreEqual(new PixelRect(7, 12, 56, 15), ((TextLayer)session.Layers.Layers.Single()).Box, "on a text layer a drag moves it (G21)");
        Assert.AreEqual(0, session.Selected);
        session.Undo();
        Assert.AreEqual(new PixelRect(4, 10, 56, 15), ((TextLayer)session.Layers.Layers.Single()).Box, "one step");
    }

    [TestMethod]
    public async Task AClickOnOpenPictureAddsThePresetTheRowShowsForTheSelectedLayer()
    {
        using var editing = await StillInTheEditor.Open(List(Label("oh no", 8, 10, 10)));
        var session = editing.Session;
        var text = session.Text();
        session.Click(11, 11);
        Assert.AreEqual(TextKind.Label, text.KindShown, "the row shows the selected Label's preset as current");

        session.Click(40, 40);

        Assert.AreEqual(TextPreset.Label, ((TextLayer)session.Layers.Layers[1]).Preset, "a click adds a layer of the current preset (G21)");
        Assert.AreEqual(TextKind.Label, text.Kind);
    }

    [TestMethod]
    public async Task ACornerScalesTheFontWithAMemesWidthTheOppositeCornerKept()
    {
        using var editing = await StillInTheEditor.Open(List(Meme("hello", 10, new PixelRect(4, 10, 40, 15))));
        var session = editing.Session;
        session.Click(30, 17);

        // Outward along the diagonal from the top-left corner, (40, 15).
        session.DragThrough((44, 25), (48, 26.5f), (52, 28));

        var grown = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual(12, grown.FontSize, "the font scaled by how far the corner went along the diagonal, 1.2");
        Assert.AreEqual(48, grown.Box.Width, "and the Meme's width with it");
        Assert.AreEqual((4, 10), (grown.Box.X, grown.Box.Y), "the opposite corner kept");
        Assert.AreEqual(LayerComposer.TextSize(grown).Height, grown.Box.Height, "the box follows the letters");
        session.Undo();
        Assert.AreEqual(new PixelRect(4, 10, 40, 15), ((TextLayer)session.Layers.Layers.Single()).Box, "one step");

        session.Click(30, 17);
        session.DragThrough((4, 10), (8, 11), (12, 13));

        var shrunk = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual(8, shrunk.FontSize);
        Assert.AreEqual(32, shrunk.Box.Width);
        Assert.AreEqual((44, 25), (shrunk.Box.X + shrunk.Box.Width, shrunk.Box.Y + shrunk.Box.Height), "the bottom-right corner where it was");
        Assert.AreEqual(LayerComposer.TextSize(shrunk).Height, shrunk.Box.Height);
    }

    [TestMethod]
    public async Task AMemeGrownPastThePicturesEdgeIsKeptInsideIt()
    {
        using var editing = await StillInTheEditor.Open(List(Meme("hello", 10, new PixelRect(4, 10, 56, 15))));
        var session = editing.Session;
        session.Click(30, 17);

        session.DragThrough((60, 25), (65, 26), (71.2f, 28));

        var grown = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual((12, 67), (grown.FontSize, grown.Box.Width));
        Assert.AreEqual((0, 10), (grown.Box.X, grown.Box.Y), "wider than the picture: at its left edge, the width standing");
    }

    [TestMethod]
    public async Task ACornerDraggedPastTheOppositeOneLeavesTheFontAtOne()
    {
        using var editing = await StillInTheEditor.Open(List(Meme("hello", 10, new PixelRect(4, 10, 56, 15))));
        var session = editing.Session;
        session.Click(30, 17);

        session.DragThrough((60, 25), (0, 0), (-100, -100));

        var least = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual(1, least.FontSize);
        Assert.IsTrue(least.Box is { Width: >= 1, Height: >= 1, X: >= 0, Y: >= 0 }, least.Box.ToString());
    }

    [TestMethod]
    public async Task AResizedLabelTakesTheSizeOfItsLettersAtTheNewSize()
    {
        var label = Label("oh no", 8, 10, 10);
        using var editing = await StillInTheEditor.Open(List(label));
        var session = editing.Session;
        session.Click(label.Box.X + 1, label.Box.Y + 1);
        var corner = new PointF(label.Box.X + label.Box.Width, label.Box.Y + label.Box.Height);

        session.DragThrough((corner.X, corner.Y), (corner.X + label.Box.Width / 2f, corner.Y + label.Box.Height / 2f));

        var resized = (TextLayer)session.Layers.Layers.Single();
        Assert.IsGreaterThan(8, resized.FontSize);
        Assert.AreEqual(LayerComposer.TextSize(resized), new PixelSize(resized.Box.Width, resized.Box.Height));
        Assert.AreEqual((10, 10), (resized.Box.X, resized.Box.Y));
    }

    [TestMethod]
    public async Task ACaptionBarsCornerScalesItsFontAndItsBandFollowsAboveThePicture()
    {
        var bar = new CaptionBarLayer("me, later", "Segoe UI", 6, NearBlack, Outline.None);
        using var editing = await StillInTheEditor.Open(List(bar));
        var session = editing.Session;
        var before = LayerComposer.Bands(Still.Width, Still.Height, session.Layers).Single().Band;
        session.Click(32, before.Y + before.Height / 2f);
        Assert.AreEqual(0, session.Selected);

        session.DragThrough((64, 0), (70, 1), (77, 2));

        var grown = (CaptionBarLayer)session.Layers.Layers.Single();
        Assert.IsGreaterThan(6, grown.FontSize);
        Assert.AreEqual(bar with { FontSize = grown.FontSize }, grown, "only its size changed");
        var band = LayerComposer.Bands(Still.Width, Still.Height, session.Layers).Single().Band;
        Assert.IsGreaterThan(before.Height, band.Height);
        Assert.AreEqual((0, Still.Width, 0), (band.X, band.Width, band.Y + band.Height), "the picture's width, directly above it");
        Assert.AreEqual(new PixelSize(Still.Width, Still.Height + band.Height), session.OutputSize);

        session.Undo();
        Assert.AreEqual(bar, session.Layers.Layers.Single(), "one step");
        session.Click(32, before.Y + before.Height / 2f);
        session.DragThrough((64, 0), (58, -1), (52, -2));
        Assert.IsLessThan(6, ((CaptionBarLayer)session.Layers.Layers.Single()).FontSize);

        session.DragThrough((64, 0), (-200, -400));
        Assert.AreEqual(1, ((CaptionBarLayer)session.Layers.Layers.Single()).FontSize, "never below 1");
    }

    [TestMethod]
    public async Task ACaptionBarIsNeitherMovedNorTurned()
    {
        var bar = new CaptionBarLayer("me, later", "Segoe UI", 6, NearBlack, Outline.None);
        using var editing = await StillInTheEditor.Open(List(bar));
        var session = editing.Session;
        var band = LayerComposer.Bands(Still.Width, Still.Height, session.Layers).Single().Band;
        session.Click(32, band.Y + band.Height / 2f);

        session.DragThrough((20, band.Y + band.Height / 2f), (30, band.Y + band.Height / 2f + 5), (40, 20));

        Assert.AreEqual(bar, session.Layers.Layers.Single());
        Assert.IsFalse(session.CanUndo);
        Assert.AreEqual("Drag a corner to resize it; double-click to edit its text", session.Text().Hint);
    }

    [TestMethod]
    public async Task APresetChosenConvertsTheSelectedLayerInPlaceAsOneStepEach()
    {
        var hide = new HideLayer(new PixelRect(40, 30, 5, 5), HideKind.Blur, null);
        var meme = Meme("x", 8, new PixelRect(4, 10, 56, 10)) with { Font = "Comic Sans MS", Fill = Yellow, Outline = Outline.None };
        using var editing = await StillInTheEditor.Open(List(hide, meme));
        var session = editing.Session;
        var text = session.Text();
        session.Click(30, 14);
        Assert.AreEqual(1, session.Selected);

        text.Choose(TextKind.Label);
        var label = (TextLayer)session.Layers.Layers[1];
        Assert.AreEqual((TextPreset.Label, "x", "Comic Sans MS", 8, Yellow, Outline.None, 4, 10), (label.Preset, label.Text, label.Font, label.FontSize, label.Fill, label.Outline, label.Box.X, label.Box.Y));
        Assert.AreEqual(LayerComposer.TextSize(label), new PixelSize(label.Box.Width, label.Box.Height));
        Assert.AreEqual(TextKind.Label, text.Kind);

        text.Choose(TextKind.CaptionBar);
        Assert.AreEqual(new CaptionBarLayer("x", "Comic Sans MS", 8, NearBlack, Outline.None), session.Layers.Layers[1]);
        Assert.AreEqual(TextKind.CaptionBar, text.KindShown);

        text.Choose(TextKind.Meme);
        var back = (TextLayer)session.Layers.Layers[1];
        Assert.AreEqual((TextPreset.Meme, "x", "Comic Sans MS", 8, White, Outline.Auto), (back.Preset, back.Text, back.Font, back.FontSize, back.Fill, back.Outline));
        Assert.IsLessThan(Still.Height / 2, back.Box.Y, "placed as a new Meme, at the top");
        Assert.AreEqual(TextKind.Meme, text.Kind);
        Assert.AreSame(hide, session.Layers.Layers[0]);

        session.Undo();
        Assert.IsInstanceOfType<CaptionBarLayer>(session.Layers.Layers[1]);
        session.Undo();
        Assert.AreEqual(TextPreset.Label, ((TextLayer)session.Layers.Layers[1]).Preset);
        session.Undo();
        Assert.AreEqual(meme, session.Layers.Layers[1]);
    }

    [TestMethod]
    public async Task ALabelMadeAMemeKeepsItsWordsOnOneLine()
    {
        var label = Label("oh no", 8, 10, 10);
        using var editing = await StillInTheEditor.Open(List(label));
        var session = editing.Session;
        session.Click(11, 11);

        session.Text().Choose(TextKind.Meme);

        var meme = (TextLayer)session.Layers.Layers.Single();
        Assert.IsGreaterThan(meme.Box.Height, LayerComposer.TextSize(label with { Preset = TextPreset.Meme }).Height, "at the label's own width its uppercase would wrap");
        Assert.AreEqual((TextPreset.Meme, 10, 10), (meme.Preset, meme.Box.X, meme.Box.Y));
        Assert.AreEqual(LayerComposer.TextSize(meme with { Text = "o" }).Height, meme.Box.Height, "\"OH NO\" in uppercase is wider than \"oh no\", and still one line");
    }

    [TestMethod]
    public async Task ALabelWithNoLetterYetMadeAMemeKeepsItsPlaceAndTurnAndTakesTheRoomToThePicturesRight()
    {
        var turned = Label("old", 4, 20, 20) with { Rotation = 30 };
        using var editing = await StillInTheEditor.Open(List(turned));
        var session = editing.Session;
        var text = session.Text();
        session.DoubleClick(new PointF(turned.Box.X + turned.Box.Width / 2f, turned.Box.Y + turned.Box.Height / 2f));
        session.SetTypedText("");
        text.ChooseFill(Yellow);

        text.Choose(TextKind.Meme);

        var meme = (TextLayer)session.Layers.Layers.Single();
        Assert.IsTrue(session.Typing, "its field still open");
        Assert.AreEqual((TextPreset.Meme, Yellow, 30d), (meme.Preset, meme.Fill, meme.Rotation), "its colours and turn kept, as between Meme and Label");
        Assert.AreEqual(Still.Width - turned.Box.X, meme.Box.Width, "as wide as the room to the picture's right, not as no letters");
        session.SetTypedText("oh no");
        var typed = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual(LayerComposer.TextSize(typed with { Text = "O" }).Height, typed.Box.Height, "its words on one line");
        Assert.AreEqual(30d, typed.Rotation);
        session.EndTyping();
        session.Undo();
        Assert.AreEqual(turned, session.Layers.Layers.Single(), "one step");
    }

    [TestMethod]
    public async Task ALabelMadeAMemeOnACroppedPictureReachesNoFurtherThanTheShownPicturesRight()
    {
        var crop = new CropLayer(new PixelRect(0, 0, 16, Still.Height));
        var label = Label("old", 4, 4, 8);
        using var editing = await StillInTheEditor.Open(List(crop, label));
        var session = editing.Session;
        var text = session.Text();
        session.DoubleClick(new PointF(label.Box.X + label.Box.Width / 2f, label.Box.Y + label.Box.Height / 2f));
        session.SetTypedText("");

        text.Choose(TextKind.Meme);

        var room = 16 - label.Box.X;
        Assert.AreEqual(room, ((TextLayer)session.Layers.Layers[1]).Box.Width, "with no letter yet, the room to the cropped picture's right, where letters typed still show");
        session.SetTypedText("oh no oh no oh no");
        session.EndTyping();
        text.Choose(TextKind.Label);
        text.Choose(TextKind.Meme);
        Assert.IsLessThanOrEqualTo(room, ((TextLayer)session.Layers.Layers[1]).Box.Width, "with letters, no wider than that room either");
    }

    [TestMethod]
    public async Task TheFontFillAndOutlineChangeTheSelectedLayerEachAsOneStep()
    {

        using var editing = await StillInTheEditor.Open(List(Label("oh no", 8, 10, 10)));
        var session = editing.Session;
        var text = session.Text();
        session.Click(11, 11);
        var recent = session.Colors.Recent.ToList();

        text.Choose("Comic Sans MS");
        var lettered = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual("Comic Sans MS", lettered.Font);
        Assert.AreEqual("Comic Sans MS", text.FontShown);
        Assert.AreEqual(LayerComposer.TextSize(lettered), new PixelSize(lettered.Box.Width, lettered.Box.Height), "the box follows the new face");

        text.ChooseFill(Blue);
        Assert.AreEqual(Blue, ((TextLayer)session.Layers.Layers.Single()).Fill);
        Assert.AreEqual(Blue, text.FillShown);
        CollectionAssert.AreEqual(recent, session.Colors.Recent.ToList(), "a swatch is no recent color");
        var picked = new Rgba(0x12, 0x34, 0x56, 0xFF);
        text.ChooseFill(picked);
        Assert.AreEqual(picked, session.Colors.Recent[0]);

        text.ChooseOutline(Outline.None);
        Assert.AreEqual(Outline.None, ((TextLayer)session.Layers.Layers.Single()).Outline);
        var red = new Rgba(0xE0, 0x10, 0x10, 0xFF);
        text.ChooseOutline(Outline.Of(red));
        Assert.AreEqual(Outline.Of(red), text.OutlineShown);
        Assert.AreEqual(red, session.Colors.Recent[0]);
        text.ChooseOutline(Outline.Auto);
        Assert.AreEqual(Outline.Auto, ((TextLayer)session.Layers.Layers.Single()).Outline);

        for (var steps = 6; steps > 0; steps--)
        {
            Assert.IsTrue(session.CanUndo, $"{steps} steps left");
            session.Undo();
        }

        Assert.IsFalse(session.CanUndo);
        Assert.AreEqual("Segoe UI", ((TextLayer)session.Layers.Layers.Single()).Font);
    }

    [TestMethod]
    public async Task AFillDraggedOnThePickerShowsLiveAsNoStepAndItsLetGoIsOneStepAndOneRecentColour()
    {
        using var editing = await StillInTheEditor.Open(List(Label("oh no", 8, 10, 10)));
        var session = editing.Session;
        var text = session.Text();
        session.Click(11, 11);
        var before = (TextLayer)session.Layers.Layers.Single();
        var passed = new[] { new Rgba(0x20, 0x40, 0x60, 0xFF), new Rgba(0x30, 0x50, 0x70, 0xFF), new Rgba(0x40, 0x60, 0x80, 0xFF) };

        text.BeginColor();
        foreach (var color in passed)
        {
            Assert.IsTrue(text.PreviewFill(color));
            Assert.AreEqual(color, ((TextLayer)session.Layers.Layers.Single()).Fill, "the fill shows each colour the drag passes");
            Assert.AreEqual(color, text.FillShown);
            Assert.IsFalse(session.CanUndo, "and records no step while it runs (G25)");
        }

        Assert.IsEmpty(session.Colors.Recent, "no recent colour while it runs");
        Assert.IsTrue(text.LetGoFill(passed[^1]));

        Assert.AreEqual(passed[^1], ((TextLayer)session.Layers.Layers.Single()).Fill);
        CollectionAssert.AreEqual(new[] { passed[^1] }, session.Colors.Recent.ToArray(), "one recent colour, the one let go on (G21)");
        Assert.AreEqual(0, session.Selected);
        session.Undo();
        Assert.AreEqual(before, session.Layers.Layers.Single(), "one Undo puts back the fill before the drag");
        Assert.IsFalse(session.CanUndo, "the drag was one step");
    }

    [TestMethod]
    public async Task AnOutlineDraggedOnThePickerShowsLiveAndCommitsOnceWhileAutoAndNoneStayOneStepEach()
    {
        using var editing = await StillInTheEditor.Open(List(Label("oh no", 8, 10, 10)));
        var session = editing.Session;
        var text = session.Text();
        session.Click(11, 11);
        var red = new Rgba(0xE0, 0x10, 0x10, 0xFF);
        var darker = new Rgba(0xA0, 0x10, 0x10, 0xFF);

        text.BeginColor();
        text.PreviewOutline(red);
        Assert.AreEqual(Outline.Of(red), ((TextLayer)session.Layers.Layers.Single()).Outline);
        text.PreviewOutline(darker);
        Assert.AreEqual(Outline.Of(darker), text.OutlineShown);
        Assert.IsFalse(session.CanUndo);
        Assert.IsTrue(text.LetGoOutline(darker));
        text.ChooseOutline(Outline.None);
        text.ChooseOutline(Outline.Auto);

        CollectionAssert.AreEqual(new[] { darker }, session.Colors.Recent.ToArray());
        session.Undo();
        Assert.AreEqual(Outline.None, text.OutlineShown, "Auto was one step");
        session.Undo();
        Assert.AreEqual(Outline.Of(darker), text.OutlineShown, "None was one step");
        session.Undo();
        Assert.AreEqual(Outline.Auto, text.OutlineShown, "the drag was one step, back to the outline before it");
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public async Task UndoDuringAFillDragPutsTheFillBackThenUndoesTheChangeBeforeAndTheLateLetGoAppliesNothing()
    {
        using var editing = await StillInTheEditor.Open(List(Label("oh no", 8, 10, 10)));
        var session = editing.Session;
        var text = session.Text();
        session.Click(11, 11);
        text.Choose("Comic Sans MS");
        var dragged = new Rgba(0x20, 0x40, 0x60, 0xFF);
        text.BeginColor();
        text.PreviewFill(dragged);

        session.Undo();

        var back = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual("Segoe UI", back.Font, "one Undo stepped back the change before the drag (G25)");
        Assert.AreNotEqual(dragged, back.Fill, "the drag's colour put back");
        Assert.IsFalse(text.LetGoFill(dragged), "the let-go after it applies nothing");
        Assert.AreEqual(back, session.Layers.Layers.Single());
        Assert.IsFalse(session.CanUndo);
        Assert.IsEmpty(session.Colors.Recent);
    }

    [TestMethod]
    public async Task AColourChosenAtOnceDuringAFillDragIsItsOwnStepFromWhatWasCommitted()
    {
        using var editing = await StillInTheEditor.Open(List(Label("oh no", 8, 10, 10)));
        var session = editing.Session;
        var text = session.Text();
        session.Click(11, 11);
        var before = (TextLayer)session.Layers.Layers.Single();
        var dragged = new Rgba(0x20, 0x40, 0x60, 0xFF);
        var typed = new Rgba(0xFF, 0x7A, 0x1A, 0xFF);
        text.BeginColor();
        text.PreviewFill(dragged);

        text.ChooseFill(typed);

        Assert.AreEqual(typed, ((TextLayer)session.Layers.Layers.Single()).Fill);
        CollectionAssert.AreEqual(new[] { typed }, session.Colors.Recent.ToArray(), "the dragged colour was never recorded");
        Assert.IsFalse(text.LetGoFill(dragged), "the drag ended with the choice");
        session.Undo();
        Assert.AreEqual(before, session.Layers.Layers.Single(), "one step, from the fill before the drag");
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public async Task AShrinkChosenDuringAFillDragPutsTheFillBackAndIsItsOwnStepFromWhatWasCommitted()
    {
        using var editing = await StillInTheEditor.Open(List(Label("oh no", 8, 10, 10)));
        var session = editing.Session;
        var text = session.Text();
        session.Click(11, 11);
        var before = (TextLayer)session.Layers.Layers.Single();
        var dragged = new Rgba(0x20, 0x40, 0x60, 0xFF);
        text.BeginColor();
        text.PreviewFill(dragged);

        session.Shrink = Shrink.Half;

        Assert.AreEqual(Shrink.Half, session.Shrink);
        Assert.AreEqual(before, session.Layers.Layers.OfType<TextLayer>().Single(), "the dragged fill put back, never carried into the Shrink's step (G25)");
        Assert.IsFalse(text.LetGoFill(dragged), "the drag ended with the Shrink: its late let-go applies nothing");
        Assert.AreEqual(before, session.Layers.Layers.OfType<TextLayer>().Single());
        Assert.IsEmpty(session.Colors.Recent, "no colour was used, so none is recent (G21)");
        session.Undo();
        Assert.AreEqual(Shrink.Full, session.Shrink);
        Assert.AreEqual(before, session.Layers.Layers.Single(), "one Undo takes back the Shrink alone");
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public async Task AFillDragUndoEndedStaysEndedThroughLaterMovesSoItsLetGoAppliesNothing()
    {
        using var editing = await StillInTheEditor.Open(List(Label("oh no", 8, 10, 10)));
        var session = editing.Session;
        var text = session.Text();
        session.Click(11, 11);
        text.Choose("Comic Sans MS");
        var dragged = new Rgba(0x20, 0x40, 0x60, 0xFF);
        text.BeginColor();
        text.PreviewFill(dragged);
        session.Undo();
        var back = session.Layers;

        Assert.IsFalse(text.PreviewFill(new Rgba(0x30, 0x50, 0x70, 0xFF)), "a move after the drag was ended continues nothing");
        Assert.IsFalse(text.LetGoFill(dragged), "and its let-go applies nothing");
        Assert.AreEqual(back, session.Layers);
        Assert.IsEmpty(session.Colors.Recent);
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public async Task UndoOrRedoWithNothingToStepStillEndsAFillDrag()
    {
        using var editing = await StillInTheEditor.Open(List(Label("oh no", 8, 10, 10)));
        var session = editing.Session;
        var text = session.Text();
        session.Click(11, 11);
        var before = session.Layers;
        var dragged = new Rgba(0x20, 0x40, 0x60, 0xFF);

        foreach (var key in new Action[] { session.Undo, session.Redo })
        {
            text.BeginColor();
            text.PreviewFill(dragged);
            key();
            Assert.AreEqual(before, session.Layers, "the drag's colour put back though there was no step to take (G25)");
            Assert.IsFalse(text.LetGoFill(dragged), "its let-go applies nothing");
        }

        Assert.IsFalse(session.CanUndo);
        Assert.IsEmpty(session.Colors.Recent);
    }

    [TestMethod]
    public async Task AColourDragWithNoTextSelectedChangesNothingAndRecordsNoRecentColour()
    {
        using var editing = await StillInTheEditor.Open(List(Label("oh no", 8, 10, 10)));
        var session = editing.Session;
        var text = session.Text();
        var before = session.Layers;
        var dragged = new Rgba(0x20, 0x40, 0x60, 0xFF);

        text.BeginColor();
        text.PreviewFill(dragged);
        text.LetGoFill(dragged);
        text.BeginColor();
        text.PreviewOutline(dragged);
        text.LetGoOutline(dragged);

        Assert.AreEqual(before, session.Layers);
        Assert.IsFalse(session.CanUndo);
        Assert.IsEmpty(session.Colors.Recent, "a colour no layer took is no colour used (G21)");
    }

    [TestMethod]
    public async Task AFillDragEndedWithoutItsLetGoOrByDeselectingPutsTheFillBackAsNoStep()
    {
        using var editing = await StillInTheEditor.Open(List(Label("oh no", 8, 10, 10)));
        var session = editing.Session;
        var text = session.Text();
        session.Click(11, 11);
        var before = session.Layers;
        var dragged = new Rgba(0x20, 0x40, 0x60, 0xFF);

        text.BeginColor();
        text.PreviewFill(dragged);
        text.AbandonColor();
        Assert.AreEqual(before, session.Layers, "the picker closed or its pointer lost: the fill back");
        Assert.IsFalse(text.LetGoFill(dragged));

        text.BeginColor();
        text.PreviewFill(dragged);
        session.Deselect();
        Assert.AreEqual(before, session.Layers, "deselected: the fill back");
        Assert.IsNull(session.Selected);
        Assert.IsFalse(text.LetGoFill(dragged));
        Assert.IsFalse(session.CanUndo);
        Assert.IsEmpty(session.Colors.Recent);
    }

    [TestMethod]
    public async Task ASaveDuringAFillDragSavesTheLayersAsCommitted()
    {
        using var editing = await StillInTheEditor.Open(List(Label("oh no", 8, 10, 10)));
        var session = editing.Session;
        var text = session.Text();
        session.Click(11, 11);
        text.ChooseFill(Blue);
        var committed = session.Layers;
        text.BeginColor();
        text.PreviewFill(new Rgba(0x20, 0x40, 0x60, 0xFF));

        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        Assert.AreEqual(committed, (await EditedItems.ForEdit(editing.Stash.Store!, editing.Still.Id)).Layers, "the drag's colour put back before the render (G26)");
    }

    [TestMethod]
    public async Task WithNothingSelectedTheOptionsChangeNothingAndThrowNothing()
    {
        using var editing = await StillInTheEditor.Open(List(Label("oh no", 8, 10, 10)));
        var session = editing.Session;
        var text = session.Text();
        var before = session.Layers;

        text.Choose("Comic Sans MS");
        text.ChooseFill(new Rgba(1, 2, 3, 255));
        text.ChooseOutline(Outline.None);
        session.Click(11, 11);
        text.Choose("");
        text.Choose("  ");
        text.Choose("A\uD83D");

        Assert.AreEqual(before, session.Layers);
        Assert.IsFalse(session.CanUndo);
        Assert.IsEmpty(session.Colors.Recent);
    }

    [TestMethod]
    public async Task ANewMemeOnAnAnimationSpansTheWholeClip()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        using var session = await Open(stash, await stash.ImportFixture("clip", "animated.gif"));

        session.Click(1, 1);

        Assert.IsNull(((TextLayer)session.Layers.Layers.Single()).Range, "null is the whole clip (G22), not (0, last)");
    }

    /// <summary>A Meme with <paramref name="text"/> at <paramref name="box"/> as a stored list may hold it, not re-measured.</summary>
    internal static TextLayer Meme(string text, int size, PixelRect box) =>
        new(TextPreset.Meme, text, "Impact", size, White, Outline.Auto, box, 0, null);

    /// <summary>A Label with <paramref name="text"/> whose box's top-left is (<paramref name="x"/>, <paramref name="y"/>), its size its letters'.</summary>
    internal static TextLayer Label(string text, int size, int x, int y) =>
        LayerGeometry.Measured(new TextLayer(TextPreset.Label, text, "Segoe UI", size, White, Outline.Auto, new PixelRect(x, y, 1, 1), 0, null), LayerList.Empty, Still);
}
