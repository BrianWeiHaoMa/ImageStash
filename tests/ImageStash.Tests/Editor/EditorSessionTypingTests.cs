using System.Drawing;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// Typing into a text or caption-bar layer (G21, G25, G26, G20): what is typed shown at once with its box following its
/// letters, the whole run one step with the click that added the layer, an emptied layer gone, half a surrogate pair
/// never handed to the model; a double-click opening a layer's field; every other operation of the session closing the
/// field first, so Undo takes back what was typed, Save saves it and Cancel discards it; a Meme keeping the uppercase
/// letters it shows, a Label its letters as typed.
/// </summary>
[TestClass]
public sealed class EditorSessionTypingTests
{
    private static readonly PixelSize Still = StillInTheEditor.Size;

    [TestMethod]
    public async Task TheAddAndWhatIsTypedAreOneStepWhenTheFieldCloses()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Click(10, 5);

        session.SetTypedText("h");
        session.SetTypedText("hi");

        var typed = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual("HI", typed.Text, "shown as it is typed, a Meme in the uppercase it shows (G21)");
        Assert.AreEqual(LayerComposer.TextSize(typed).Height, typed.Box.Height);
        Assert.IsFalse(session.CanUndo, "no step per keystroke");

        session.EndTyping();
        Assert.IsTrue(session.CanUndo);
        Assert.IsFalse(session.Typing);
        Assert.AreEqual(0, session.Selected);

        session.Undo();
        Assert.IsEmpty(session.Layers.Layers, "the add and the text are one step");
        Assert.IsFalse(session.CanUndo);
        session.Redo();
        Assert.AreEqual("HI", ((TextLayer)session.Layers.Layers.Single()).Text);
    }

    [TestMethod]
    public async Task ALayerLeftWithNoTextIsAsIfNeverAdded()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;

        session.Click(10, 5);
        session.EndTyping();
        Assert.IsEmpty(session.Layers.Layers);
        Assert.IsFalse(session.CanUndo);
        Assert.IsNull(session.Selected);

        session.Click(10, 5);
        session.SetTypedText("a");
        session.SetTypedText("");
        session.EndTyping();
        Assert.IsEmpty(session.Layers.Layers);
        Assert.IsFalse(session.CanUndo);

        session.Click(10, 5);
        session.SetTypedText(" \n ");
        session.EndTyping();
        Assert.IsEmpty(session.Layers.Layers, "nothing but white space draws no letter");
    }

    [TestMethod]
    public async Task HalfAnEmojiTypedIsNotShownAndTheWholeOneIs()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Click(10, 5);
        session.SetTypedText("a");

        session.SetTypedText("a\uD83D");
        Assert.AreEqual("A", ((TextLayer)session.Layers.Layers.Single()).Text, "the model refuses half a surrogate pair; the last whole text stays");

        session.SetTypedText("a😀");
        Assert.AreEqual("A😀", ((TextLayer)session.Layers.Layers.Single()).Text);
        session.EndTyping();

        session.Undo();
        Assert.IsEmpty(session.Layers.Layers);
        session.Redo();
        Assert.AreEqual("A😀", ((TextLayer)session.Layers.Layers.Single()).Text, "one step holding the emoji");
    }

    [TestMethod]
    public async Task ADoubleClickOpensALayersFieldAndItsRunIsOneStep()
    {
        using var editing = await StillInTheEditor.Open(List(Hello()));
        var session = editing.Session;
        session.Tool = session.Hide();

        session.DoubleClick(new PointF(30, 6));

        Assert.IsTrue(session.Typing);
        Assert.AreEqual(0, session.Selected);
        Assert.AreSame(session.Text(), session.Tool, "the layer's own tool is current");
        session.SetTypedText("hello there");
        session.EndTyping();
        var reopened = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual("HELLO THERE", reopened.Text);
        Assert.AreEqual(LayerComposer.TextSize(reopened).Height, reopened.Box.Height, "re-measured");

        session.DoubleClick(new PointF(30, 6));
        session.SetTypedText("");
        session.EndTyping();
        Assert.IsEmpty(session.Layers.Layers, "emptied, it is removed as one step");
        Assert.IsNull(session.Selected);

        session.Undo();
        Assert.AreEqual("HELLO THERE", ((TextLayer)session.Layers.Layers.Single()).Text);
        session.Undo();
        Assert.AreEqual("hello", ((TextLayer)session.Layers.Layers.Single()).Text);
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public async Task ADoubleClickBesideAnyTextOpensNoField()
    {
        using var editing = await StillInTheEditor.Open(List(Hello(), new HideLayer(new PixelRect(40, 30, 10, 10), HideKind.Blur, null)));
        var session = editing.Session;

        session.DoubleClick(new PointF(45, 35));
        Assert.IsFalse(session.Typing, "a hide has no text");
        session.DoubleClick(new PointF(20, 40));
        Assert.IsFalse(session.Typing);
        Assert.HasCount(2, session.Layers.Layers);

        session.BeginTyping();
        Assert.IsFalse(session.Typing, "nothing selected to type into");
    }

    [TestMethod]
    public async Task OpeningAndClosingAFieldIsAChangeTheViewHears()
    {
        using var editing = await StillInTheEditor.Open(List(Hello()));
        var session = editing.Session;
        session.Click(30, 6);
        var heard = new List<bool>();
        session.Changed += () => heard.Add(session.Typing);

        session.BeginTyping();
        session.EndTyping();

        CollectionAssert.AreEqual(new[] { true, false }, heard);
        Assert.IsFalse(session.CanUndo, "a run that changed nothing is no step");
    }

    [TestMethod]
    public async Task ACaptionBarsTextGrowsItsBandAsItIsTyped()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Text().Choose(TextKind.CaptionBar);
        session.Click(10, 10);
        var one = session.OutputSize.Height;

        session.SetTypedText("me\nlater");

        Assert.AreEqual("me\nlater", ((CaptionBarLayer)session.Layers.Layers.Single()).Text);
        Assert.IsGreaterThan(one, session.OutputSize.Height, "a line more in the band");
        session.EndTyping();
        Assert.IsTrue(session.CanUndo);
    }

    [TestMethod]
    public async Task ABottomMemeGrowsAndShrinksUpwardAndStaysAtTheBottomAndALongLabelStaysAtThePicturesLeft()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Click(10, 40);
        var bottom = ((TextLayer)session.Layers.Layers.Single()).Box;

        session.SetTypedText("one\ntwo\nthree");

        var grown = ((TextLayer)session.Layers.Layers.Single()).Box;
        Assert.IsGreaterThan(bottom.Height, grown.Height);
        Assert.AreEqual(bottom.Y + bottom.Height, grown.Y + grown.Height, "its bottom edge kept: it grew upward");
        session.SetTypedText("one");
        Assert.AreEqual(bottom, ((TextLayer)session.Layers.Layers.Single()).Box, "shrunk back, still at the bottom (G21)");
        session.EndTyping();

        session.Deselect();
        session.Click(10, 5);
        var top = ((TextLayer)session.Layers.Layers[1]).Box;
        session.SetTypedText("one\ntwo");
        Assert.AreEqual((top.X, top.Y), (((TextLayer)session.Layers.Layers[1]).Box.X, ((TextLayer)session.Layers.Layers[1]).Box.Y), "a top Meme grows downward");
        session.EndTyping();

        session.Deselect();
        session.Text().Choose(TextKind.Label);
        session.Click(50, 20);
        session.SetTypedText("a label far longer than the picture is wide");
        var label = ((TextLayer)session.Layers.Layers[2]).Box;
        Assert.IsGreaterThan(Still.Width, label.Width);
        Assert.AreEqual((0, 20), (label.X, label.Y), "its origin at 0 on the axis it cannot fit, its width standing");
    }

    [TestMethod]
    public async Task UndoWhileTypingTakesBackWhatWasTypedAndRedoBringsItBack()
    {
        using var editing = await StillInTheEditor.Open(List(Hello()));
        var session = editing.Session;
        session.DoubleClick(new PointF(30, 6));
        session.SetTypedText("abc");

        session.Undo();

        Assert.IsFalse(session.Typing);
        Assert.AreEqual("hello", ((TextLayer)session.Layers.Layers.Single()).Text);
        session.Redo();
        Assert.AreEqual("ABC", ((TextLayer)session.Layers.Layers.Single()).Text);
    }

    [TestMethod]
    public async Task UndoWithAnEmptyNewFieldTakesBackOnlyThatLayer()
    {
        using var editing = await StillInTheEditor.Open(List(Hello()));
        var session = editing.Session;
        session.DoubleClick(new PointF(30, 6));
        session.SetTypedText("hello again");
        session.EndTyping();
        session.Click(10, 40);
        Assert.HasCount(2, session.Layers.Layers);

        session.Undo();

        Assert.AreEqual("HELLO AGAIN", ((TextLayer)session.Layers.Layers.Single()).Text, "the earlier step stands");
        Assert.IsTrue(session.CanUndo);
    }

    [TestMethod]
    public async Task DelWithAnEmptyNewFieldRemovesOnlyThatLayer()
    {
        using var editing = await StillInTheEditor.Open(List(Hello()));
        var session = editing.Session;
        session.Click(30, 6);
        session.Click(10, 40);
        Assert.AreEqual(1, session.Selected);

        session.DeleteSelected();

        Assert.AreEqual(List(Hello()), session.Layers, "the layer selected before the click stays");
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public async Task DelWhileTypingClosesTheFieldThenRemovesTheLayer()
    {
        using var editing = await StillInTheEditor.Open(List(Hello()));
        var session = editing.Session;
        session.DoubleClick(new PointF(30, 6));
        session.SetTypedText("abc");

        session.DeleteSelected();

        Assert.IsEmpty(session.Layers.Layers);
        Assert.IsFalse(session.Typing);
        session.Undo();
        Assert.AreEqual("ABC", ((TextLayer)session.Layers.Layers.Single()).Text, "the typed text was its own step");
    }

    [TestMethod]
    public async Task DeselectChoosingAToolShrinkAPressAndAnOptionEachCloseTheFieldFirst()
    {
        var closing = new (string Name, Action<EditorSession> Act, bool Selected)[]
        {
            ("Deselect", session => session.Deselect(), false),
            ("Tool = Hide", session => session.Tool = session.Hide(), false),
            ("Shrink", session => session.Shrink = Shrink.Half, true),
            ("Press", session => session.Press(new PointF(20, 40)), true),
            ("ChooseFill", session => session.Text().ChooseFill(ColorChoices.Swatches[4]), true),
            ("DoubleClick", session => session.DoubleClick(new PointF(20, 40)), true),
            ("RevertToOriginal", session => session.RevertToOriginal(), false),
        };
        foreach (var (name, act, selected) in closing)
        {
            using var editing = await StillInTheEditor.Open(List(Hello()));
            var session = editing.Session;
            session.DoubleClick(new PointF(30, 6));
            session.SetTypedText("abc");

            act(session);

            Assert.IsFalse(session.Typing, name);
            Assert.AreEqual(selected, session.Selected is not null, name);
            session.Undo();
            if (name != "RevertToOriginal" && name != "Shrink" && name != "ChooseFill")
            {
                Assert.AreEqual("hello", ((TextLayer)session.Layers.Layers.Single()).Text, $"{name}: the typed text was committed as one step");
            }
            else
            {
                Assert.AreEqual("ABC", ((TextLayer)session.Layers.Layers.Single(layer => layer is TextLayer)).Text, $"{name}: its own step after the typed one");
            }
        }
    }

    [TestMethod]
    public async Task AnOptionChosenWithAnEmptyNewFieldChangesItsLayerKeepsTheFieldOpenAndJoinsItsStep()
    {
        var (fill, outline) = (new Rgba(0x12, 0x34, 0x56, 0xFF), new Rgba(0x65, 0x43, 0x21, 0xFF));
        var choices = new (string Name, Action<TextTool> Choose, Func<Layer, bool> Took)[]
        {
            ("fill", text => text.ChooseFill(fill), layer => layer is TextLayer { Fill: var color } && color == fill),
            ("outline", text => text.ChooseOutline(Outline.Of(outline)), layer => layer is TextLayer { Outline: var line } && line == Outline.Of(outline)),
            ("font", text => text.Choose("Comic Sans MS"), layer => layer is TextLayer { Font: "Comic Sans MS" }),
            ("preset", text => text.Choose(TextKind.Label), layer => layer is TextLayer { Preset: TextPreset.Label }),
            ("caption", text => text.Choose(TextKind.CaptionBar), layer => layer is CaptionBarLayer),
        };
        foreach (var (name, choose, took) in choices)
        {
            using var editing = await StillInTheEditor.Open();
            var session = editing.Session;
            session.Click(10, 5);
            session.SetTypedText("first");
            session.EndTyping();
            var first = session.Layers;
            session.Click(10, 40);
            Assert.AreEqual(1, session.Selected, name);

            choose(session.Text());

            Assert.IsTrue(session.Typing, $"{name}: the field stays open, its layer holding no letter yet (G21)");
            Assert.AreEqual(1, session.Selected, name);
            Assert.IsTrue(took(session.Layers.Layers[1]), $"{name}: the new layer took it: {session.Layers.Layers[1]}");
            Assert.AreSame(first.Layers[0], session.Layers.Layers[0], $"{name}: the earlier layer is as it was");
            session.SetTypedText("x");
            Assert.IsTrue(took(session.Layers.Layers[1]), $"{name}: and keeps it as letters are typed: {session.Layers.Layers[1]}");
            session.EndTyping();
            Assert.HasCount(2, session.Layers.Layers, name);
            session.Undo();
            Assert.AreEqual(first, session.Layers, $"{name}: the add, the choice and the letters are one step");
        }
    }

    [TestMethod]
    public async Task AnEmptyNewFieldGivenOptionsLeavesWithItsLayerAsNoStepWhateverClosesIt()
    {
        var (dragged, kept) = (new Rgba(0x10, 0x20, 0x30, 0xFF), new Rgba(0x40, 0x50, 0x60, 0xFF));
        var closing = new (string Name, Action<EditorSession> Act)[]
        {
            ("EndTyping", session => session.EndTyping()),
            ("Undo", session => session.Undo()),
            ("Redo", session => session.Redo()),
            ("Deselect", session => session.Deselect()),
            ("Tool = Hide", session => session.Tool = session.Hide()),
            ("Del", session => session.DeleteSelected()),
            ("Press", session => session.Press(new PointF(20, 30))),
            ("DoubleClick on the other layer", session => session.DoubleClick(new PointF(30, 6))),
        };
        foreach (var (name, act) in closing)
        {
            foreach (var dragging in new[] { false, true })
            {
                var what = dragging ? $"{name}, a colour still dragged" : name;
                using var editing = await StillInTheEditor.Open(List(Hello()));
                var session = editing.Session;
                var text = session.Text();
                session.Click(10, 40);
                text.Choose("Georgia");
                text.Choose(TextKind.Label);
                text.ChooseFill(ColorChoices.Swatches[4]);
                Assert.IsTrue(session.Typing && session.Layers.Layers[1] is TextLayer { Font: "Georgia", Preset: TextPreset.Label }, $"{what}: the field and its layer kept through the options");
                if (dragging)
                {
                    text.BeginColor();
                    Assert.IsTrue(text.PreviewFill(dragged), what);
                }

                act(session);

                Assert.AreEqual(List(Hello()), session.Layers, $"{what}: a layer left with no text goes, whatever was chosen for it, and nothing else changes");
                Assert.IsFalse(session.CanUndo, $"{what}: no step");
                Assert.IsFalse(text.LetGoFill(kept), $"{what}: the colour dragged ended with the field");
                Assert.AreEqual(List(Hello()), session.Layers, $"{what}: and its let-go brings nothing back");
            }
        }
    }

    [TestMethod]
    public async Task AFieldEmptiedOnAnEarlierLayerStaysOpenForAnOptionAndItsRunIsOneStep()
    {
        using var editing = await StillInTheEditor.Open(List(Hello()));
        var session = editing.Session;
        session.DoubleClick(new PointF(30, 6));
        session.SetTypedText("");

        session.Text().Choose("Georgia");

        Assert.IsTrue(session.Typing, "the layer emptied in its field is not removed by an option");
        Assert.AreEqual("Georgia", ((TextLayer)session.Layers.Layers.Single()).Font);
        session.SetTypedText("bye");
        session.EndTyping();
        var typed = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual(("BYE", "Georgia"), (typed.Text, typed.Font));
        session.Undo();
        Assert.AreEqual(Hello(), session.Layers.Layers.Single(), "one step, the font with the letters");
    }

    [TestMethod]
    public async Task AColourDraggedWithAnEmptyNewFieldShowsOnItsLayerAndItsLetGoJoinsTheFieldsStep()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        var text = session.Text();
        session.Click(10, 5);
        var white = ((TextLayer)session.Layers.Layers.Single()).Fill;
        var (dragged, kept) = (new Rgba(0x10, 0x20, 0x30, 0xFF), new Rgba(0x40, 0x50, 0x60, 0xFF));

        text.BeginColor();
        Assert.IsTrue(session.Typing, "the drag begins with the field open");
        Assert.IsTrue(text.PreviewFill(dragged));
        Assert.AreEqual(dragged, ((TextLayer)session.Layers.Layers.Single()).Fill, "shown as it is dragged");
        Assert.IsTrue(text.LetGoFill(kept));

        Assert.IsTrue(session.Typing, "the field stays open");
        Assert.AreEqual(kept, ((TextLayer)session.Layers.Layers.Single()).Fill);
        Assert.IsFalse(session.CanUndo, "no step of its own");
        CollectionAssert.Contains(session.Colors.Recent.ToList(), kept, "a colour that went on the layer is a recent one");
        session.SetTypedText("hi");
        Assert.AreEqual(kept, ((TextLayer)session.Layers.Layers.Single()).Fill, "kept as letters are typed");
        session.EndTyping();
        session.Undo();
        Assert.IsEmpty(session.Layers.Layers, "the add, the colour and the letters are one step");

        // Letters typed while a colour is still dragged put the drag back, so its let-go takes back no letter.
        session.Click(10, 5);
        text.BeginColor();
        text.PreviewFill(dragged);
        session.SetTypedText("a");
        var lettered = (TextLayer)session.Layers.Layers.Single();
        Assert.AreEqual(("A", white), (lettered.Text, lettered.Fill));
        Assert.IsFalse(text.LetGoFill(kept), "the drag was ended by the letter");
        Assert.AreSame(lettered, session.Layers.Layers.Single());
        session.EndTyping();

        // The field closed while a colour is still dragged ends the drag with it, so its let-go brings back no layer.
        session.Click(10, 40);
        text.BeginColor();
        text.PreviewFill(dragged);
        session.EndTyping();
        Assert.HasCount(1, session.Layers.Layers, "the new layer left with its empty field");
        Assert.IsFalse(text.LetGoFill(kept));
        Assert.HasCount(1, session.Layers.Layers);
    }

    [TestMethod]
    public async Task AFieldOpenedPartWayThroughADragPutsTheDragBackAndSaveSavesWhatWasTyped()
    {
        using var editing = await StillInTheEditor.Open(List(TextToolTests.Label("old", 8, 10, 10)));
        var session = editing.Session;
        session.Click(11, 11);
        session.Press(new PointF(12, 12));
        session.Drag(new PointF(22, 22));

        session.BeginTyping();
        session.SetTypedText("new");
        session.EndTyping();
        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        var stored = (TextLayer)(await EditedItems.ForEdit(editing.Stash.Store!, editing.Still.Id)).Layers.Layers.Single();
        Assert.AreEqual(("new", 10, 10), (stored.Text, stored.Box.X, stored.Box.Y), "the drag was put back as the field opened, and the typed text kept");
    }

    [TestMethod]
    public async Task ADoubleClickOnACaptionsBandOpensItsFieldThoughLaterTextTurnedReachesOverIt()
    {
        var bar = new CaptionBarLayer("me", "Segoe UI", 6, ColorChoices.Swatches[1], Outline.None);
        var turned = new TextLayer(TextPreset.Label, "turned", "Segoe UI", 4, ColorChoices.Swatches[0], Outline.Auto, new PixelRect(20, 10, 30, 6), 90, null);
        using var editing = await StillInTheEditor.Open(List(bar, turned));
        var session = editing.Session;

        // The label's turned box reaches from y −2 to 28, where Media clips it to the picture; the band is above y 0.
        session.DoubleClick(new PointF(35, -1));

        Assert.AreEqual(0, session.Selected);
        Assert.IsTrue(session.Typing);
    }

    [TestMethod]
    public async Task SaveWhileTypingSavesWhatWasTyped()
    {
        using var editing = await StillInTheEditor.Open(List(Hello()));
        var session = editing.Session;
        session.DoubleClick(new PointF(30, 6));
        session.SetTypedText("saved as typed");

        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        Assert.AreEqual("SAVED AS TYPED", ((TextLayer)(await EditedItems.ForEdit(editing.Stash.Store!, editing.Still.Id)).Layers.Layers.Single()).Text);
    }

    [TestMethod]
    public async Task CancelWhileTypingDiscardsWhatWasTyped()
    {
        using var editing = await StillInTheEditor.Open(List(Hello()));
        var session = editing.Session;
        session.DoubleClick(new PointF(30, 6));
        session.SetTypedText("discarded");

        var discarded = session.Cancel();

        Assert.AreEqual("DISCARDED", ((TextLayer)discarded!.Layers.Single()).Text, "the notice's Undo brings back what was on screen");
        Assert.IsFalse(session.Typing);
    }

    [TestMethod]
    public async Task AnEndedSessionTakesNoTyping()
    {
        using var editing = await StillInTheEditor.Open(List(Hello()));
        var session = editing.Session;
        session.DoubleClick(new PointF(30, 6));
        session.Dispose();

        session.SetTypedText("late");
        session.EndTyping();
        session.BeginTyping();

        Assert.AreEqual("hello", ((TextLayer)session.Layers.Layers.Single()).Text);
        Assert.IsFalse(session.Typing);
    }

    [TestMethod]
    public async Task TypingIntoAMemeKeepsTheUppercaseLettersItShowsWhichItKeepsAsALabelAndALabelKeepsWhatWasTyped()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        session.Click(10, 5);
        Assert.AreEqual(TextPreset.Meme, ((TextLayer)session.SelectedLayer!).Preset);

        session.SetTypedText("minimum");
        Assert.AreEqual("MINIMUM", ((TextLayer)session.SelectedLayer!).Text, "the letters Media draws for it (G21: Meme is uppercase)");
        session.SetTypedText("minßimum");
        Assert.AreEqual("MINSSIMUM", ((TextLayer)session.SelectedLayer!).Text, "a character whose capital is two letters keeps both");
        session.SetTypedText("minimum");
        session.EndTyping();

        session.Text().Choose(TextKind.Label);
        var label = (TextLayer)session.SelectedLayer!;
        Assert.AreEqual((TextPreset.Label, "MINIMUM"), (label.Preset, label.Text), "switched to Label, it keeps the letters it showed");

        session.Deselect();
        session.Click(10, 30);
        session.SetTypedText("minimum");
        Assert.AreEqual((TextPreset.Label, "minimum"), (((TextLayer)session.SelectedLayer!).Preset, ((TextLayer)session.SelectedLayer!).Text), "a Label keeps what was typed");
    }

    /// <summary>A Meme "hello" at the top of the still, its box its letters'.</summary>
    private static TextLayer Hello() =>
        LayerGeometry.Measured(TextToolTests.Meme("hello", 6, new PixelRect(2, 2, 60, 1)), LayerList.Empty, Still);
}
