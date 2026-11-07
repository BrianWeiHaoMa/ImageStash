using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// The editor playing an animated item in the window (G22, G25, G6, G19, G26): the canvas drawing its frames as they
/// play; Space pausing and playing, Left and Right stepping, from the canvas or a tool button, while inside a text or
/// the picker's hex Space and the arrows stay the field's; on a still those keys play nothing; the preview beneath and a
/// hovered tile playing nothing while the editor plays, the preview playing again once it closes, and a Save while it
/// plays returning to the preview on the finished GIF.
/// </summary>
[TestClass]
public sealed class PlayingInTheEditorTests
{
    // animated.gif's frames, as the renderer draws them.
    private static readonly uint[] Frames = [0xFFFF0000, 0xFF00FF00, 0xFF0000FF];

    [TestMethod]
    public async Task TheCanvasDrawsTheFramesAsTheyPlayAndSpaceLeftAndRightDriveThePlay()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Fixture("animated.gif", "playing.gif"));
        await EditorOverlayTests.Open(shown, items[0]);

        var drawn = new HashSet<uint>();
        for (var sample = 0; sample < 8 && drawn.Count < 2; sample++)
        {
            drawn.Add(await PictureColour(shown, editor));
        }

        Assert.IsGreaterThanOrEqualTo(2, drawn.Count, "the canvas draws the frames as they play");
        Assert.IsTrue(drawn.IsSubsetOf(Frames));

        TrayDesktop.Run(() =>
        {
            editor.Canvas.Focus();
            ShownWindow.Press(editor.Canvas, Key.Space);
            Assert.IsFalse(editor.Session!.Playing, "Space pauses");
        });
        var paused = TrayDesktop.Get(() => editor.Session!.FrameIndex);
        await Task.Delay(700);
        Assert.AreEqual(paused, TrayDesktop.Get(() => editor.Session!.FrameIndex), "on the frame showing");
        Assert.AreEqual(Frames[paused], await PictureColour(shown, editor), "which the canvas draws");

        var next = Math.Min(paused + 1, 2);
        TrayDesktop.Run(() => ShownWindow.Press(editor.Canvas, Key.Right));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.FrameIndex == next));
        TrayDesktop.Run(() => ShownWindow.Press(editor.Canvas, Key.Left));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.FrameIndex == next - 1));
        Assert.AreEqual(Frames[next - 1], await PictureColour(shown, editor), "Left steps back one frame");

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(editor.Canvas, Key.Space);
            Assert.IsTrue(editor.Session!.Playing, "Space plays again");
        });
    }

    [TestMethod]
    public async Task InsideATextOrTheHexSpaceAndTheArrowsAreTheFieldsAndOnAToolButtonSpacePlaysAndKeepsTheTool()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Fixture("animated.gif", "typing.gif"));
        await EditorOverlayTests.Open(shown, items[0]);
        TrayDesktop.Run(() =>
        {
            editor.Session!.Pause();
            editor.Click(0.5, 0.5);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Field is not null));

        TrayDesktop.Run(() =>
        {
            var field = editor.Field!;
            field.Focus();
            ShownWindow.Type(field, "ab");
            var frame = editor.Session!.FrameIndex;
            Assert.IsFalse(KeyDown(field, Key.Space).Handled, "Space reaches the field");
            ShownWindow.Type(field, " ");
            Assert.IsTrue(field.Text?.EndsWith(' ') == true, $"and types a space: {field.Text}");
            var caret = field.CaretIndex;
            KeyDown(field, Key.Left);
            Assert.AreEqual(caret - 1, field.CaretIndex, "Left moves the caret");
            Assert.IsFalse(editor.Session.Playing, "neither plays");
            Assert.AreEqual(frame, editor.Session.FrameIndex, "nor steps");
            ShownWindow.Press(field, Key.Enter, KeyModifiers.Control);
        });

        TrayDesktop.Run(() =>
        {
            editor.Choose("Draw");
            editor.Choose("Box");
            editor.Drag((0.2, 0.2), (0.6, 0.6));
        });
        await Task.Delay(200);
        TrayDesktop.Run(() => shown.Click(editor.Button("More colors")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Hex(editor) is not null));
        var before = TrayDesktop.Get(() =>
        {
            var hex = Hex(editor)!;
            hex.Focus();
            Assert.IsFalse(KeyDown(hex, Key.Space).Handled, "Space reaches the hex");
            KeyDown(hex, Key.Left);
            KeyDown(hex, Key.Right);
            return editor.Session!.FrameIndex;
        });
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(editor.Session!.Playing, "Space in the hex plays nothing");
            Assert.AreEqual(before, editor.Session.FrameIndex, "and the arrows in it step nothing");
            ShownWindow.Press(Hex(editor)!, Key.Escape);
        });

        TrayDesktop.Run(() =>
        {
            var hide = editor.Button("Hide");
            hide.Focus(NavigationMethod.Tab);
            var tool = editor.Session!.Tool;
            ShownWindow.Press(hide, Key.Space);
            ShownWindow.Release(hide, Key.Space);
            Assert.IsTrue(editor.Session.Playing, "Space on a tool button plays");
            Assert.AreSame(tool, editor.Session.Tool, "and the tool stays");
        });
    }

    [TestMethod]
    public async Task WhileTheWindowIsHiddenThePlayHoldsItsFrameAndShownAgainGoesOnPlayingOrStaysPaused()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Fixture("animated.gif", "hidden playing.gif"));
        await EditorOverlayTests.Open(shown, items[0]);
        Assert.IsTrue(TrayDesktop.Get(() => editor.Session!.Playing));

        TrayDesktop.Run(shown.Window.Hide);
        var heldOn = TrayDesktop.Get(() => editor.Session!.FrameIndex);
        await Task.Delay(700);
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(editor.Session!.Playing, "hidden, the editor plays nothing nobody sees (G6)");
            Assert.AreEqual(heldOn, editor.Session.FrameIndex, "and holds its frame");
            Assert.IsTrue(shown.Editing.IsOpen, "the edit kept as it was left (G1, G3)");
        });

        TrayDesktop.Run(shown.Window.Show);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.Playing && editor.Session.FrameIndex != heldOn));

        TrayDesktop.Run(() =>
        {
            editor.Canvas.Focus();
            ShownWindow.Press(editor.Canvas, Key.Space);
            ShownWindow.Release(editor.Canvas, Key.Space);
            shown.Window.Hide();
        });
        await Task.Delay(300);
        TrayDesktop.Run(shown.Window.Show);
        await Task.Delay(500);
        TrayDesktop.Run(() => Assert.IsFalse(editor.Session!.Playing, "paused before the hide, still paused after the show"));
    }

    [TestMethod]
    public async Task OnAStillSpaceLeftAndRightPlayNothing()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("still keys.png", 64, 48, EditorCanvasTests.Checker));
        await EditorOverlayTests.Open(shown, items[0]);

        TrayDesktop.Run(() =>
        {
            editor.Canvas.Focus();
            var frame = editor.Session!.Frame;
            foreach (var key in new[] { Key.Space, Key.Right, Key.Left })
            {
                Assert.IsFalse(KeyDown(editor.Canvas, key).Handled, $"{key} is not the editor's on a still");
            }

            Assert.IsFalse(editor.Session.Playing);
            Assert.AreSame(frame, editor.Session.Frame);
            Assert.AreEqual(0, editor.Session.FrameIndex);
        });
    }

    [TestMethod]
    public async Task ThePreviewAndAHoveredTileBeneathPlayNothingWhileTheEditorPlaysAndThePreviewPlaysAgainOnceItCloses()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Fixture("long.gif", "beneath.gif"), shown.Fixture("animated.gif", "hovered beneath.gif"));
        var (clip, other) = (items.Single(item => item.Name == "beneath"), items.Single(item => item.Name == "hovered beneath"));
        TrayDesktop.Run(() => shown.MovePointer(other.Id, 0, 0));
        await ShownWindow.Eventually(() => ShownWindow.Held(other.FilePath));
        TrayDesktop.Run(() => shown.OpenPreview(clip));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewPicture.Image is not null && !shown.PreviewPicture.Paused));

        TrayDesktop.Run(() => ShownWindow.RaiseClick(shown.EditButton));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.Session?.Item.Id == clip.Id));
        await ShownWindow.Eventually(() => !ShownWindow.Held(other.FilePath));
        await Task.Delay(500);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(editor.Session!.Playing, "the editor's canvas is the one playing (G6)");
            Assert.IsTrue(shown.PreviewPicture.Paused, "the preview beneath plays nothing");
        });
        Assert.IsFalse(ShownWindow.Held(other.FilePath), "nor does the hovered tile beneath");

        await EditorOverlayTests.Cancelled(shown);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.IsOpen && !shown.PreviewPicture.Paused));
    }

    [TestMethod]
    public async Task ASaveWhileItPlaysReturnsToThePreviewPlayingTheFinishedGif()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Fixture("animated.gif", "saved playing.gif"));
        TrayDesktop.Run(() => shown.OpenPreview(items[0]));
        TrayDesktop.Run(() => ShownWindow.RaiseClick(shown.EditButton));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.Session is { Playing: true }));
        TrayDesktop.Run(() =>
        {
            editor.Choose("Hide");
            editor.Drag((0.1, 0.1), (0.5, 0.5));
            Assert.IsTrue(editor.Session!.Playing);
            ShownWindow.Press(editor.Canvas, Key.S, KeyModifiers.Control);
        });

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen && shown.Preview.IsOpen));
        var saved = TrayDesktop.Get(() => shown.Preview.Item!);
        Assert.AreEqual(".gif", Path.GetExtension(saved.FilePath));
        Assert.IsTrue(saved.Facts.Animated, "the finished picture is the clip, every frame");
        var first = TrayDesktop.Get(() => shown.PreviewPicture.Image);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewPicture.Image is { } image && !ReferenceEquals(image, first)));
    }

    private static async Task<uint> PictureColour(ShownWindow shown, ShownEditor editor)
    {
        var rendered = await RenderedWindow.Of(shown);
        var picture = TrayDesktop.Get(editor.PictureInRendering);
        return rendered.At((int)picture.Center.X, (int)picture.Center.Y);
    }

    private static KeyEventArgs KeyDown(Control target, Key key)
    {
        var pressed = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target };
        target.RaiseEvent(pressed);
        return pressed;
    }

    private static TextBox? Hex(ShownEditor editor) =>
        editor.Overlay.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(box) == "Hex");
}
