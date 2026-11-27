using System.Drawing;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// What a new text layer takes (F8: "within one edit a new layer takes instead the font last chosen in that edit for a
/// layer of its preset"; G21: "Within one edit a new layer takes, in place of its preset's own fill and outline, the ones
/// last chosen in that edit for a layer of its preset"), through the session's clicks and the option row's choices:
/// preset by preset and property by property, a choice on a layer the edit opened with counting, only what a layer took
/// counting, and a new edit starting again from the presets' own.
/// </summary>
public sealed partial class TextToolTests
{
    [TestMethod]
    public async Task AFontAFillAndAnOutlineChosenOnDifferentMemesAllReachTheNextMemeAndNoOtherPreset()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        var text = session.Text();
        session.Click(10, 5);
        session.SetTypedText("one");
        text.Choose("Comic Sans MS");
        session.Deselect();
        session.Click(10, 40);
        session.SetTypedText("two");
        text.ChooseFill(Yellow);
        session.Deselect();
        session.Click(10, 30);
        session.SetTypedText("three");
        text.ChooseOutline(Outline.None);
        session.Deselect();

        session.Click(10, 20);

        var next = (TextLayer)session.Layers.Layers[3];
        Assert.AreEqual(("Comic Sans MS", Yellow, Outline.None), (next.Font, next.Fill, next.Outline), "each property as last chosen for a Meme, each on another Meme");
        Assert.AreEqual(("Comic Sans MS", Yellow, Outline.None), (text.FontShown, text.FillShown, text.OutlineShown), "F8: the menu marks the selected layer's font");
        var (first, second) = ((TextLayer)session.Layers.Layers[0], (TextLayer)session.Layers.Layers[1]);
        Assert.AreEqual(("Comic Sans MS", White, Outline.Auto, "Comic Sans MS", Yellow, Outline.Auto), (first.Font, first.Fill, first.Outline, second.Font, second.Fill, second.Outline), "each earlier Meme keeps what it took and was given");
        session.Deselect();

        text.Choose(TextKind.Label);
        session.Click(40, 24);
        var label = (TextLayer)session.Layers.Layers.Single(layer => layer is TextLayer { Preset: TextPreset.Label });
        Assert.AreEqual(("Segoe UI", White, Outline.Auto), (label.Font, label.Fill, label.Outline), "a Label takes none of the Memes' choices");
        session.SetTypedText("lab");
        text.ChooseFill(Blue);
        session.Deselect();

        text.Choose(TextKind.CaptionBar);
        session.Click(10, 24);
        var bar = (CaptionBarLayer)session.Layers.Layers.Single(layer => layer is CaptionBarLayer);
        Assert.AreEqual(("Segoe UI", NearBlack, Outline.None), (bar.Font, bar.Fill, bar.Outline), "a Caption bar takes its own: no yellow or blue letters on its white bar");
        session.SetTypedText("bar");
        session.Deselect();

        text.Choose(TextKind.Label);
        session.Click(25, 24);
        var nextLabel = (TextLayer)session.SelectedLayer!;
        Assert.AreEqual((TextPreset.Label, "Segoe UI", Blue, Outline.Auto), (nextLabel.Preset, nextLabel.Font, nextLabel.Fill, nextLabel.Outline), "a Label takes what a Label was given");
    }

    [TestMethod]
    public async Task AChoiceOnALayerTheEditOpenedWithReachesTheNextLayerOfItsPreset()
    {
        var opened = new TextLayer(TextPreset.Meme, "OLD", "Georgia", 6, White, Outline.Auto, new PixelRect(3, 3, 58, 8), 0, null);
        using var editing = await StillInTheEditor.Open(List(opened));
        var session = editing.Session;
        var text = session.Text();
        session.Click(10, 6);
        Assert.AreEqual(0, session.Selected);

        text.ChooseFill(Blue);
        session.Deselect();
        session.Click(10, 40);

        var next = (TextLayer)session.Layers.Layers[1];
        Assert.AreEqual(("Impact", Blue, Outline.Auto), (next.Font, next.Fill, next.Outline), "the fill chosen on the opened layer; the font it only carried is no choice");
    }

    [TestMethod]
    public async Task AFillAndAFontChosenWithNoneSelectedAreWhatTheNextClickGives()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        var text = session.Text();

        text.ChooseFill(Blue);
        text.Choose("Georgia");
        Assert.IsTrue(session.Layers.IsEmpty, "a choice with none selected adds nothing");
        Assert.AreEqual(("Georgia", Blue), (text.FontShown, text.FillShown), "the row shows them as the next Meme's");
        session.Click(10, 5);

        var meme = (TextLayer)session.SelectedLayer!;
        Assert.AreEqual((TextPreset.Meme, "Georgia", Blue, Outline.Auto), (meme.Preset, meme.Font, meme.Fill, meme.Outline));
    }

    [TestMethod]
    public async Task AChoiceWithNoneSelectedStaysWithItsPresetAndAnotherPresetShowsItsOwnNextValues()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        var text = session.Text();
        text.ChooseFill(Blue);

        text.Choose(TextKind.Label);
        Assert.AreEqual(("Segoe UI", White, Outline.Auto), (text.FontShown, text.FillShown, text.OutlineShown), "Label's next values, not the Meme's blue");
        session.Click(30, 24);
        var label = (TextLayer)session.SelectedLayer!;
        Assert.AreEqual((TextPreset.Label, White), (label.Preset, label.Fill), "a Label takes Label's");
        session.SetTypedText("lab");
        session.Deselect();

        text.Choose(TextKind.Meme);
        Assert.AreEqual(Blue, text.FillShown, "the Meme's next fill kept");
    }

    [TestMethod]
    public async Task ForEachPresetWithNoneSelectedTheRowShowsWhatTheNextClickGives()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        var text = session.Text();
        var own = new Dictionary<TextKind, (string, Rgba, Outline)>
        {
            [TextKind.Meme] = ("Impact", White, Outline.Auto),
            [TextKind.CaptionBar] = ("Segoe UI", NearBlack, Outline.None),
            [TextKind.Label] = ("Segoe UI", White, Outline.Auto),
        };

        foreach (var (kind, values) in own)
        {
            text.Choose(kind);
            var shown = (text.FontShown, text.FillShown, text.OutlineShown);
            Assert.AreEqual(values, shown, $"{kind}: the preset's own (F8, G21)");
            session.Click(30, 30);
            Assert.AreEqual(shown, Values(session.SelectedLayer!), $"{kind}: the layer the click adds takes what was shown");
            session.SetTypedText("x");
            text.Choose("Comic Sans MS");
            text.ChooseOutline(Outline.Of(Blue));
            session.Deselect();

            Assert.AreEqual(("Comic Sans MS", values.Item2, Outline.Of(Blue)), (text.FontShown, text.FillShown, text.OutlineShown), $"{kind}: a choice on a selected layer of the preset replaces them");
            while (session.CanUndo)
            {
                session.Undo();
            }

            session.Click(30, 30);
            Assert.AreEqual(("Comic Sans MS", values.Item2, Outline.Of(Blue)), Values(session.SelectedLayer!), $"{kind}: and the next click gives them");
            session.SetTypedText("y");
            session.Deselect();
            while (session.CanUndo)
            {
                session.Undo();
            }
        }

        static (string, Rgba, Outline) Values(Layer layer) => layer switch
        {
            TextLayer text => (text.Font, text.Fill, text.Outline),
            CaptionBarLayer bar => (bar.Font, bar.Fill, bar.Outline),
            _ => throw new AssertFailedException($"not a text layer: {layer}"),
        };
    }

    [TestMethod]
    public async Task ThePickersColourAutoAndNoneChosenWithNoneSelectedEachBecomeTheNextTexts()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        var text = session.Text();
        var picked = new Rgba(10, 120, 200, 255);

        text.BeginColor();
        Assert.IsTrue(text.PreviewFill(Yellow));
        Assert.IsTrue(text.LetGoFill(picked));
        Assert.AreEqual(picked, text.FillShown, "the picker's drag let go");
        Assert.AreEqual(picked, session.Colors.Recent.FirstOrDefault(), "a picked colour becomes a recent one");
        text.ChooseOutline(Outline.None);
        Assert.AreEqual(Outline.None, text.OutlineShown, "None");
        text.ChooseOutline(Outline.Auto);
        Assert.AreEqual(Outline.Auto, text.OutlineShown, "Auto");
        text.BeginColor();
        Assert.IsTrue(text.LetGoOutline(Blue));
        Assert.AreEqual(Outline.Of(Blue), text.OutlineShown, "the outline picker's drag let go");
        // The eyedropper and the hex hand the picker's colour to ChooseFill, as a recent colour's click does.
        text.ChooseFill(Yellow);
        Assert.IsTrue(session.Layers.IsEmpty);

        session.Click(10, 5);
        var meme = (TextLayer)session.SelectedLayer!;
        Assert.AreEqual((Yellow, Outline.Of(Blue)), (meme.Fill, meme.Outline));
    }

    [TestMethod]
    public async Task AChoiceWithNoneSelectedWhileASaveRunsIsNotTaken()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var still = await stash.ImportFixture("still", "still.png");
        using var rendering = new SemaphoreSlim(0);
        using var go = new SemaphoreSlim(0);
        FinishedPictureRenderer waiting = (original, name, layers, format, work, retry) =>
        {
            rendering.Release();
            go.Wait();
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        };
        using var session = await Open(stash, still, waiting);
        var text = session.Text();
        session.Click(2, 1);
        session.SetTypedText("one");
        session.Deselect();

        var saving = session.SaveAsNewItem();
        Assert.IsTrue(await rendering.WaitAsync(TimeSpan.FromSeconds(10)));
        text.ChooseFill(Blue);
        text.Choose("Georgia");
        text.ChooseOutline(Outline.None);
        go.Release();
        Assert.AreEqual(EditEnding.Continued, (await saving).Ending);

        Assert.AreEqual(("Impact", White, Outline.Auto), (text.FontShown, text.FillShown, text.OutlineShown));
    }

    [TestMethod]
    public async Task ANewEditStartsAgainFromThePresetsOwn()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        var text = session.Text();
        session.Click(10, 5);
        session.SetTypedText("one");
        text.Choose("Comic Sans MS");
        text.ChooseFill(Blue);
        text.ChooseOutline(Outline.None);
        Assert.IsNotNull(session.Cancel());

        using var again = await Open(editing.Stash, editing.Still);
        again.Click(10, 40);

        var fresh = (TextLayer)again.Layers.Layers.Single();
        Assert.AreEqual(("Impact", White, Outline.Auto), (fresh.Font, fresh.Fill, fresh.Outline), "nothing is kept across edits");
    }

    [TestMethod]
    public async Task OnlyAColourALayerTookIsTakenByTheNextLayer()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        var text = session.Text();
        session.Click(10, 5);
        session.SetTypedText("one");
        text.BeginColor();
        Assert.IsTrue(text.PreviewFill(Blue));
        text.AbandonColor();
        session.Deselect();
        session.Click(10, 40);
        Assert.AreEqual(White, ((TextLayer)session.Layers.Layers[1]).Fill, "a colour only shown while the picker was dragged, then abandoned, is no choice");

        session.SetTypedText("two");
        text.BeginColor();
        Assert.IsTrue(text.PreviewFill(Blue));
        Assert.IsTrue(text.LetGoFill(Yellow));
        text.BeginColor();
        Assert.IsTrue(text.LetGoOutline(Blue));
        session.Deselect();
        session.Click(10, 24);

        var next = (TextLayer)session.Layers.Layers[2];
        Assert.AreEqual((Yellow, Outline.Of(Blue)), (next.Fill, next.Outline), "the picker's drag let go is a choice, for the fill and the outline");
    }

    [TestMethod]
    public async Task AChoiceMadeWhileASaveRunsIsNotTakenAndSaveAsNewItemGoesOnWithTheEarlierOnes()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var still = await stash.ImportFixture("still", "still.png");
        using var rendering = new SemaphoreSlim(0);
        using var go = new SemaphoreSlim(0);
        FinishedPictureRenderer waiting = (original, name, layers, format, work, retry) =>
        {
            rendering.Release();
            go.Wait();
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        };
        using var session = await Open(stash, still, waiting);
        var text = session.Text();
        session.Click(2, 1);
        session.SetTypedText("one");
        text.Choose("Comic Sans MS");

        var saving = session.SaveAsNewItem();
        Assert.IsTrue(await rendering.WaitAsync(TimeSpan.FromSeconds(10)));
        text.ChooseFill(Blue);
        go.Release();
        Assert.AreEqual(EditEnding.Continued, (await saving).Ending);
        session.Deselect();
        session.Click(2, session.OriginalSize.Height - 1);

        var next = (TextLayer)session.SelectedLayer!;
        Assert.AreEqual(("Comic Sans MS", White), (next.Font, next.Fill), "the font chosen before goes on with the edit; the fill no layer took is no choice");
    }
}
