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
        Assert.AreEqual(("Comic Sans MS", (Rgba?)Yellow, Outline.None), (text.FontShown, text.FillShown, text.OutlineShown), "F8: the menu marks the selected layer's font");
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
