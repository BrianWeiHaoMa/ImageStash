using System.Drawing;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// A Meme's size while it is typed (G21: "Meme is uppercase with an automatic outline, placed at the top or bottom", "a
/// corner handle resizes it"; L2; G25; F7), through the session's click, typing, corner drag, Undo and Redo on a picture
/// 500 pixels wide: a Meme a click added shrinks to stay on two lines as Media lays them out, down to half the tenth of
/// the width it was added at, and grows back; its edge stays; a Label and a caption bar keep their size; a corner drag
/// that changed the size ends the fitting, as long as that drag stands; the fitting follows its layer; and it is one
/// step with the click, stored nowhere, and drawn by Save as the editor drew it.
/// </summary>
[TestClass]
public sealed class EditorSessionFittingTests
{
    private const string Long = "ME WHEN THE BUILD PASSES ON THE FIRST TRY";
    private const int Tenth = 50, Floor = 25;

    [TestMethod]
    public async Task ALongMemeLineShrinksToTwoLinesAndGrowsBackWhenLettersAreTakenAway()
    {
        using var stash = new TestStash();
        using var session = await OpenWide(stash);
        session.Click(250, 10);
        var top = Meme(session).Box.Y;
        session.SetTypedText("SHORT");
        var shortLine = Meme(session);
        Assert.AreEqual(Tenth, shortLine.FontSize, "a short line keeps the tenth of the width");
        Assert.AreEqual(3, LayerComposer.TextLines(Meme(session) with { Text = Long }), "the long line takes three lines at the tenth");

        session.SetTypedText(Long);

        var fitted = Meme(session);
        Assert.IsTrue(fitted.FontSize is < Tenth and >= Floor, $"a smaller whole size not below half: {fitted.FontSize}");
        Assert.AreEqual(2, LayerComposer.TextLines(fitted), "on two lines as Media lays them out");
        Assert.IsGreaterThan(2, LayerComposer.TextLines(fitted with { FontSize = fitted.FontSize + 1 }), "the largest size that keeps two lines");
        Assert.AreEqual(LayerComposer.TextSize(fitted).Height, fitted.Box.Height, "the box follows the letters");
        Assert.AreEqual(top, fitted.Box.Y, "a top Meme keeps its top edge");

        session.SetTypedText("SHORT");
        Assert.AreEqual(shortLine, Meme(session), "deleting back gives what typing it fresh gives");
    }

    [TestMethod]
    public async Task ABottomMemeShrinksAndGrowsUpwardFromItsBottomEdge()
    {
        using var stash = new TestStash();
        using var session = await OpenWide(stash);
        session.Click(250, 290);
        var bottom = Meme(session).Box.Y + Meme(session).Box.Height;

        session.SetTypedText(Long);

        var fitted = Meme(session);
        Assert.IsLessThan(Tenth, fitted.FontSize);
        Assert.AreEqual(bottom, fitted.Box.Y + fitted.Box.Height, "the bottom edge stays where it was put");
        Assert.IsTrue(fitted.Box.Y >= 0 && fitted.Box.X >= 0 && fitted.Box.X + fitted.Box.Width <= 500 && fitted.Box.Y + fitted.Box.Height <= 300, "inside the original");
    }

    [TestMethod]
    public async Task ATopMemeKeepsItsTopEdgeOnACropWhoseBottomIsNearer()
    {
        using var stash = new TestStash();
        using var cropped = await OpenWide(stash, startOn: List(new CropLayer(new PixelRect(5, 10, 490, 75))));
        cropped.Click(250, 12);
        var top = Meme(cropped).Box.Y;

        cropped.SetTypedText(Long);
        Assert.IsLessThan(Tenth, Meme(cropped).FontSize);
        Assert.AreEqual(top, Meme(cropped).Box.Y, "a top Meme keeps its top edge on a crop nearer its bottom");
    }

    [TestMethod]
    public async Task ATextTooLongAtTheFloorStaysThereAndTypedLinesAreNotShrunkForNothing()
    {
        using var stash = new TestStash();
        using var session = await OpenWide(stash);
        session.Click(250, 10);

        session.SetTypedText(string.Join(' ', Enumerable.Repeat(Long, 4)));
        var floored = Meme(session);
        Assert.AreEqual(Floor, floored.FontSize, "stays at half the size it started at");
        Assert.IsGreaterThan(2, LayerComposer.TextLines(floored), "and wraps on");

        session.SetTypedText("ONE\nTWO\nTHREE");
        Assert.AreEqual(Tenth, Meme(session).FontSize, "three short typed lines already fit their lines at the tenth");
    }

    [TestMethod]
    public async Task ALabelAndACaptionBarTypedLongKeepTheirSize()
    {
        using var stash = new TestStash();
        using var session = await OpenWide(stash);
        var text = session.Text();
        text.Choose(TextKind.Label);
        session.Click(10, 150);
        var size = ((TextLayer)session.SelectedLayer!).FontSize;
        session.SetTypedText(Long + " " + Long);
        Assert.AreEqual(size, ((TextLayer)session.SelectedLayer!).FontSize);
        session.Deselect();

        text.Choose(TextKind.CaptionBar);
        session.Click(250, 30);
        var barSize = ((CaptionBarLayer)session.SelectedLayer!).FontSize;
        session.SetTypedText(Long + " " + Long + " " + Long);
        Assert.AreEqual(barSize, ((CaptionBarLayer)session.SelectedLayer!).FontSize);
    }

    [TestMethod]
    public async Task ACornerDragThatChangedTheSizeEndsTheFittingWhileItStands()
    {
        using var stash = new TestStash();
        using var session = await OpenWide(stash);
        session.Click(250, 10);
        session.SetTypedText("SHORT");
        session.EndTyping();
        var corner = Corner(session);

        session.DragThrough((corner.X, corner.Y), (corner.X + 15, corner.Y + 15), (corner.X, corner.Y));
        Assert.IsFalse(session.CanRedo);
        TypeAgain(session, Long);
        Assert.IsLessThan(Tenth, Meme(session).FontSize, "a corner drag that changed nothing fixes nothing");
        session.Undo();
        Assert.AreEqual("SHORT", Meme(session).Text);

        session.DragThrough((corner.X, corner.Y), (corner.X - 40, corner.Y - 5));
        var set = Meme(session).FontSize;
        Assert.IsLessThan(Tenth, set);
        TypeAgain(session, Long + " AGAIN");
        Assert.AreEqual(set, Meme(session).FontSize, "after a corner set the size, typing wraps at it");
        Assert.IsGreaterThan(2, LayerComposer.TextLines(Meme(session)));

        session.Undo();
        session.Undo();
        Assert.AreEqual(Tenth, Meme(session).FontSize, "Ctrl+Z took the drag back");
        session.Redo();
        TypeAgain(session, Long + " AGAIN");
        Assert.AreEqual(set, Meme(session).FontSize, "Ctrl+Y brought the drag back, and its size with it");

        session.Undo();
        session.Undo();
        TypeAgain(session, Long + " AGAIN");
        Assert.AreEqual(2, LayerComposer.TextLines(Meme(session)), "with the drag undone, typing fits again");
    }

    [TestMethod]
    public async Task TheFittingFollowsItsLayerWhenAnotherIsDeletedOrRestored()
    {
        using var stash = new TestStash();
        using var session = await OpenWide(stash);
        session.Click(250, 10);
        session.SetTypedText("TOP");
        session.Deselect();
        session.Click(250, 290);
        session.SetTypedText("BOTTOM");
        session.EndTyping();
        var corner = Corner(session);
        session.DragThrough((corner.X, corner.Y), (corner.X - 40, corner.Y - 5));
        var set = ((TextLayer)session.Layers.Layers[1]).FontSize;

        session.Click(250, 20);
        Assert.AreEqual(0, session.Selected);
        session.DeleteSelected();
        session.Undo();

        session.Click(250, 20);
        TypeAgain(session, Long);
        Assert.AreEqual(2, LayerComposer.TextLines((TextLayer)session.Layers.Layers[0]), "the top Meme still fits");
        var box = ((TextLayer)session.Layers.Layers[1]).Box;
        session.Click(box.X + box.Width / 2f, box.Y + box.Height / 2f);
        Assert.AreEqual(1, session.Selected);
        TypeAgain(session, Long);
        Assert.AreEqual(set, ((TextLayer)session.Layers.Layers[1]).FontSize, "the bottom Meme keeps the size its corner set");
    }

    [TestMethod]
    public async Task AFittedMemeOpenedAgainStillFitsAndItsTypingIsOneStepWithTheClick()
    {
        using var stash = new TestStash();
        using var session = await OpenWide(stash);
        session.Click(250, 10);
        session.SetTypedText(Long);
        session.EndTyping();
        var fitted = session.Layers;

        session.Undo();
        Assert.IsEmpty(session.Layers.Layers, "one Ctrl+Z returns the list to before the click");
        session.Redo();
        Assert.AreEqual(fitted, session.Layers, "one Ctrl+Y brings the text and the size back together");

        TypeAgain(session, "SHORT");
        Assert.AreEqual(Tenth, Meme(session).FontSize, "opened again in the same edit, it grows back up to the tenth");
    }

    [TestMethod]
    public async Task WordsPastedIntoTheFieldFitAsTypedOnesDo()
    {
        using var stash = new TestStash();
        using var session = await OpenWide(stash);
        session.Click(250, 10);
        session.SetTypedText(Long);
        var typed = Meme(session);
        session.SetTypedText("ME WHEN");

        var (pasted, _) = LayerTextPaste.Insert("ME WHEN", 7, 7, " the build passes on the first try", meme: true)!.Value;
        session.SetTypedText(pasted);

        Assert.AreEqual(typed, Meme(session));
    }

    [TestMethod]
    public async Task AFittedMemeIsStoredAsAnyLayerAndSaveDrawsWhatTheEditorDrew()
    {
        using var stash = new TestStash();
        using var session = await OpenWide(stash);
        var text = session.Text();
        session.Click(250, 10);
        session.SetTypedText(Long);
        text.Choose("Comic Sans MS");
        text.ChooseFill(ColorChoices.Swatches[2]);
        session.Deselect();
        session.Click(250, 290);
        session.SetTypedText("SECOND");
        session.EndTyping();
        var layers = session.Layers;
        var (first, second) = ((TextLayer)layers.Layers[0], (TextLayer)layers.Layers[1]);
        Assert.AreEqual(2, LayerComposer.TextLines(first), "fitted again in its new font");
        Assert.AreEqual(("Comic Sans MS", ColorChoices.Swatches[2]), (second.Font, second.Fill), "the second Meme took the first's font and fill");
        CollectionAssert.AreEqual(layers.Layers.ToList(), LayerListJson.Read(LayerListJson.Write(layers.Layers)).Layers, "the layer list's JSON round-trips as before");
        var original = MediaFixtures.FirstFrame(session.Edit.OriginalPath);

        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        CollectionAssert.AreEqual(Compositions.Compose(original, layers).Frame.Pixels, MediaFixtures.FirstFrame(saved.Item!.FilePath).Pixels, "the finished picture is the editor's drawing (R4)");
        using var again = await Open(stash, saved.Item);
        Assert.AreEqual(layers, again.Layers, "both layers live at their sizes and choices");
    }

    private static async Task<EditorSession> OpenWide(TestStash stash, LayerList? startOn = null)
    {
        await stash.OpenCleanly();
        var wide = await stash.ImportPicture("wide", "wide.png", MediaFixtures.DistinctPng(0x808080, 500, 300));
        return await Open(stash, wide, startOn: startOn);
    }

    private static TextLayer Meme(EditorSession session) => (TextLayer)session.SelectedLayer!;

    /// <summary>The selected Meme's bottom-right corner handle, where a press grips it.</summary>
    private static PointF Corner(EditorSession session) =>
        LayerGeometry.Handles(session.Layers, session.Selected!.Value, session.OriginalSize).Single(handle => handle.Handle == Handle.BottomRight).At;

    /// <summary>Opens the selected layer's field again, types <paramref name="text"/> into it and closes it.</summary>
    private static void TypeAgain(EditorSession session, string text)
    {
        session.BeginTyping();
        session.SetTypedText(text);
        session.EndTyping();
    }
}
