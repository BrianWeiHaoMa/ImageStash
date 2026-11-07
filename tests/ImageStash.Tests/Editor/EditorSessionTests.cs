using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// The editor's session (Composition: Editor; G20, G24, G25, G26, R4, R7, G35, State: A live failure): opened on the
/// original's first frame with the layers the picture on disk was rendered from, or on a list it is given; a held
/// original or an unreadable list refused by name; undo and redo restoring the layers and the selection together;
/// Shrink as one layer; the eyedropper's sample of the picture as shown.
/// </summary>
[TestClass]
public sealed class EditorSessionTests
{
    [TestMethod]
    public async Task OpensOnTheOriginalsFirstFrameWithNoLayersAndTextThenHideThenDraw()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");

        using var session = await Open(stash, cat);

        Assert.AreEqual((8, 6), (session.Frame.Width, session.Frame.Height));
        CollectionAssert.AreEqual(MediaFixtures.FirstFrame(cat.FilePath).Pixels, session.Frame.Pixels);
        Assert.AreEqual(LayerList.Empty, session.Layers);
        CollectionAssert.AreEqual(new[] { "Text", "Crop", "Hide", "Draw" }, session.Tools.Select(tool => tool.Name).ToArray());
        Assert.AreEqual("Text", session.Tool.Name);
        Assert.AreEqual(new PixelSize(8, 6), session.OutputSize);
        Assert.IsNull(session.Selected);
        Assert.IsFalse(session.CanUndo);
        Assert.IsTrue(session.Tools.All(tool => tool.Hint.Length > 0), "each option row states what the mouse does (G20)");
    }

    [TestMethod]
    public async Task OpensASavedItemOnItsLayers()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = (await stash.SaveEdit(await stash.ImportFixture("cat", "still.png"), EditedItems.BlackBoxAndBox)).Value!;

        using var session = await Open(stash, cat);

        Assert.AreEqual(EditedItems.BlackBoxAndBox, session.Layers);
    }

    [TestMethod]
    public async Task DuringALiveFailureOpensOnTheLayersThePictureOnDiskWasRenderedFromAndItsSaveWaitsForTheEarlierOne()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await EditedItems.FirstSaveCommitted(stash);
        Assert.AreEqual(EditedItems.BlackBoxAndBox, (await EditedItems.ForEdit(store, cat.Id)).Layers, "the committed list is the new one");

        using var session = await Open(stash, cat);
        session.Tool = session.Hide();
        Assert.AreEqual(LayerList.Empty, session.Layers, "the picture on disk is the original's");
        session.DragThrough((1, 1), (3, 3));

        EditResult refused;
        using (TestStash.Hold(cat.FilePath))
        {
            refused = await session.Save();
        }

        Assert.AreEqual(EditEnding.Refused, refused.Ending);
        Assert.AreEqual("cat", refused.Failure!.Subject, refused.Failure.Sentence);
        Assert.IsTrue(session.IsOpen);
        Assert.HasCount(1, session.Layers.Layers);

        var saved = await session.Save();
        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        Assert.AreEqual(session.Layers, (await EditedItems.ForEdit(store, cat.Id)).Layers);
    }

    [TestMethod]
    public async Task OpensAnAnimationOnItsFirstFrameWithTextHideAndDraw()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var clip = await stash.ImportFixture("clip", "animated.gif");

        // A clock only the check moves, so the play stays on the first frame.
        using var session = await Open(stash, clip, time: new SteppedClock());

        CollectionAssert.AreEqual(MediaFixtures.FirstFrame(clip.FilePath).Pixels, session.Frame.Pixels);
        Assert.AreEqual(0, session.FrameIndex);
        CollectionAssert.AreEqual(new[] { "Text", "Crop", "Trim", "Hide", "Draw" }, session.Tools.Select(tool => tool.Name).ToArray());
    }

    [TestMethod]
    public async Task OpensOnAListItIsGivenWithNothingSelectedAndNothingToUndo()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var discarded = List(Box(1, 1, 3, 3));

        using var session = await Open(stash, cat, startOn: discarded);

        Assert.AreEqual(discarded, session.Layers);
        Assert.IsNull(session.Selected);
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public async Task AnOriginalHeldWithoutSharingIsRefusedByName()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var edit = await EditedItems.ForEdit(store, cat.Id);

        Outcome<EditorSession> opened;
        using (new FileStream(cat.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            opened = await EditorSession.Open(store, edit, FinishedPicture.Render, new ColorChoices(), TestStash.ShortRetry);
        }

        Assert.IsFalse(opened.Succeeded);
        Assert.AreEqual("cat", opened.Failure.Subject, opened.Failure.Sentence);
    }

    [TestMethod]
    public async Task AListTheStoreCannotReadRefusesEditByNameAndTheItemStaysListed()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        stash.SqlWhileOpen("""UPDATE items SET layers = '[{"kind":"nothing"}]' WHERE id = $id""", ("$id", cat.Id));

        var edit = await store.ForEdit(cat.Id);

        Assert.IsFalse(edit.Succeeded);
        Assert.AreEqual("cat", edit.Failure.Subject, edit.Failure.Sentence);
        Assert.AreEqual(cat.Id, (await stash.All()).Single().Id);
    }

    [TestMethod]
    public async Task UndoAndRedoPutBackTheLayersAndTheSelectionTogether()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        using var session = await Open(stash, await stash.ImportFixture("cat", "still.png"));
        session.Tool = session.Hide();
        session.DragThrough((0, 0), (2, 2));
        var first = (session.Layers, session.Selected);
        session.DragThrough((4, 0), (6, 2));
        var second = (session.Layers, session.Selected);
        session.DragThrough((0, 4), (2, 6));
        var third = (session.Layers, session.Selected);

        session.Undo();
        session.Undo();
        Assert.AreEqual(first, (session.Layers, session.Selected));
        session.Redo();
        Assert.AreEqual(second, (session.Layers, session.Selected));
        Assert.IsTrue(session.CanRedo);
        Assert.AreNotEqual(third.Layers, session.Layers);

        session.DragThrough((1, 4), (3, 6));
        Assert.IsFalse(session.CanRedo, "a new change after Undo drops what Redo would have made");

        session.Undo();
        session.Undo();
        session.Undo();
        Assert.IsFalse(session.CanUndo);
        var start = (session.Layers, session.Selected);
        session.Undo();
        Assert.AreEqual(start, (session.Layers, session.Selected), "an empty stack changes nothing");
    }

    [TestMethod]
    public async Task UndoBringsBackTheSelectionAChangeStartedFromAndAClickIsNoStep()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        using var session = await Open(stash, await stash.ImportFixture("cat", "still.png"));
        session.Tool = session.Hide();
        session.DragThrough((0, 0), (2, 2));
        session.DragThrough((4, 0), (6, 2));
        session.Click(1, 1);
        Assert.AreEqual(0, session.Selected);
        var clicked = (session.Layers, session.Selected);

        session.DragThrough((1, 1), (2, 4));
        session.Undo();

        Assert.AreEqual(clicked, (session.Layers, session.Selected));
        session.Undo();
        Assert.HasCount(1, session.Layers.Layers);
        Assert.AreEqual(0, session.Selected);
    }

    [TestMethod]
    public async Task RevertToOriginalEmptiesTheListAsOneStep()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = (await stash.SaveEdit(await stash.ImportFixture("cat", "still.png"), EditedItems.BlackBoxAndBox)).Value!;
        using var session = await Open(stash, cat);
        session.Click(3, 3);
        var before = (session.Layers, session.Selected);
        Assert.IsNotNull(before.Selected);

        session.RevertToOriginal();
        Assert.AreEqual(LayerList.Empty, session.Layers);
        Assert.IsNull(session.Selected);

        session.Undo();
        Assert.AreEqual(before, (session.Layers, session.Selected));
    }

    [TestMethod]
    public async Task ShrinkIsOneLayerEachChoiceAStep()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        using var session = await Open(stash, await stash.ImportFixture("cat", "still.png"));
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (3, 3));

        session.Shrink = Shrink.Half;
        Assert.AreEqual(new PixelSize(4, 3), session.OutputSize);
        Assert.HasCount(1, session.Layers.Layers.OfType<ShrinkLayer>());
        Assert.AreEqual(0, session.Selected, "the hide stays selected");

        session.Shrink = Shrink.ThreeQuarter;
        Assert.AreEqual(new ShrinkLayer(Shrink.ThreeQuarter), session.Layers.Layers.OfType<ShrinkLayer>().Single());
        Assert.AreEqual(new PixelSize(6, 5), session.OutputSize);

        session.Shrink = Shrink.Full;
        Assert.IsEmpty(session.Layers.Layers.OfType<ShrinkLayer>());
        Assert.AreEqual(new PixelSize(8, 6), session.OutputSize);

        session.Undo();
        Assert.AreEqual(Shrink.ThreeQuarter, session.Shrink);
        session.Undo();
        Assert.AreEqual(Shrink.Half, session.Shrink);
        session.Undo();
        Assert.AreEqual(Shrink.Full, session.Shrink);
        Assert.HasCount(1, session.Layers.Layers);
    }

    [TestMethod]
    public async Task TheEyedropperSamplesACaptionsWhiteBandAsPartOfThePicture()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        // A caption's band, 8 pixels tall at size 4, lies above the 8 × 6 picture, from y = -8 to y = 0 on its grid.
        using var session = await Open(stash, cat, startOn: new LayerList([new CaptionBarLayer("hi", "Segoe UI", 4, new Rgba(20, 20, 20, 255), Outline.None)]));

        Assert.AreEqual(new Rgba(255, 255, 255, 255), session.SampleColor(new PixelPoint(0, -8)), "the band's white, which G21 says extends the picture");
        Assert.IsNull(session.SampleColor(new PixelPoint(0, -9)), "above the band is off the picture");
    }

    [TestMethod]
    public async Task TheEyedropperSamplesThePictureAsComposed()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        using var session = await Open(stash, cat, startOn: EditedItems.BlackBoxAndBox);
        var original = session.Frame;
        Rgba Bare(int x, int y) => new(original.Pixels[(y * 8 + x) * 4 + 2], original.Pixels[(y * 8 + x) * 4 + 1], original.Pixels[(y * 8 + x) * 4], original.Pixels[(y * 8 + x) * 4 + 3]);

        Assert.AreEqual(new Rgba(0, 0, 0, 255), session.SampleColor(new PixelPoint(3, 3)), "the black box");
        Assert.AreEqual(Bare(1, 4), session.SampleColor(new PixelPoint(1, 4)), "a pixel no layer touches");
        Assert.AreEqual(new Rgba(255, 0, 0, 255), session.SampleColor(new PixelPoint(0, 3)), "the red box's edge");
        Assert.IsNull(session.SampleColor(new PixelPoint(8, 0)));
        Assert.IsNull(session.SampleColor(new PixelPoint(0, -1)));

        session.Shrink = Shrink.Half;
        Assert.AreEqual(new Rgba(0, 0, 0, 255), session.SampleColor(new PixelPoint(3, 3)), "sampled before Shrink scales the picture");

        session.Compare = true;
        Assert.AreEqual(Bare(3, 3), session.SampleColor(new PixelPoint(3, 3)), "Compare shows the original alone");
        session.Compare = false;
        Assert.AreEqual(new Rgba(0, 0, 0, 255), session.SampleColor(new PixelPoint(3, 3)));
    }

    [TestMethod]
    public async Task TheEyedropperFindsThePictureUnderACropAndBelowACaptionBand()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var black = new Rgba(0, 0, 0, 255);
        var cropped = List(new CropLayer(new PixelRect(2, 1, 4, 3)), new HideLayer(new PixelRect(3, 2, 1, 1), HideKind.Black, null));
        var captioned = List(new CaptionBarLayer("hi", "Segoe UI", 10, new Rgba(0, 0, 0, 255), Outline.None), new HideLayer(new PixelRect(0, 0, 1, 1), HideKind.Black, null));

        using (var session = await Open(stash, cat, startOn: cropped))
        {
            Assert.AreEqual(black, session.SampleColor(new PixelPoint(3, 2)));
            Assert.AreNotEqual(black, session.SampleColor(new PixelPoint(2, 1)));
            Assert.IsNull(session.SampleColor(new PixelPoint(1, 1)), "outside the crop nothing is shown");
        }

        using (var session = await Open(stash, cat, startOn: captioned))
        {
            Assert.AreEqual(black, session.SampleColor(new PixelPoint(0, 0)), "the picture's pixel, not the white band above it");
            Assert.AreNotEqual(new Rgba(255, 255, 255, 255), session.SampleColor(new PixelPoint(1, 0)));
        }
    }
}
