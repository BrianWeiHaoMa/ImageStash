using System.Drawing;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Look;
using ImageStash.Media;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using PixelRect = ImageStash.Layers.PixelRect;
using Point = Avalonia.Point;

namespace ImageStash.Windows.Tests.Editor;

[TestClass]
public sealed class EditorOptionLayoutTests
{
    [TestMethod]
    [DataRow(458, 508, 200, 1200)]
    [DataRow(460, 640, 200, 1200)]
    [DataRow(460, 640, 100, 80)]
    [DataRow(748, 620, 200, 1200)]
    [DataRow(880, 620, 200, 1200)]
    public async Task AddingAndSelectingATextLayerOrSwitchingToolsKeepsThePictureAndThePointUnderThePointer(
        int width, int height, int pictureWidth, int pictureHeight)
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("still.png", pictureWidth, pictureHeight, (_, _) => TextFieldTests.Grey))).Single();
        await Resize(shown, width, height);
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));

        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var pointer = editor.Canvas.TranslatePoint(editor.At(0.5, 0.25), editor.Overlay)!.Value;
            var initial = Geometry(shown, editor, pointer);
            AssertOptionsFit(shown, editor);

            editor.Click(0.5, 0.25);
            Assert.IsTrue(editor.Session!.SelectedLayer is TextLayer { Preset: TextPreset.Meme });
            Assert.AreEqual(initial, Geometry(shown, editor, pointer), "adding a Meme");
            ShownWindow.Type(editor.Field!, "hello");
            ShownWindow.Tap(editor.Field!, Key.Enter, KeyModifiers.Control);
            ShownWindow.Press(editor.Canvas, Key.Escape);
            Assert.IsNull(editor.Session.Selected);
            Assert.AreEqual(initial, Geometry(shown, editor, pointer), "deselecting the Meme");
            var text = (TextLayer)editor.Session.Layers.Layers.Single();
            var centre = editor.Canvas.Mapping()!.ToCanvas(text.Box.X + text.Box.Width / 2f, text.Box.Y + text.Box.Height / 2f);
            editor.Press(centre);
            editor.Release(centre);
            Assert.IsNotNull(editor.Session.Selected);
            Assert.AreEqual(initial, Geometry(shown, editor, pointer), "reselecting the Meme");
            AssertOptionsFit(shown, editor);

            foreach (var tool in new[] { "Draw", "Hide", "Crop", "Text" })
            {
                editor.Choose(tool);
                Assert.AreEqual(tool, editor.Session.Tool.Name);
                Assert.AreEqual(initial, Geometry(shown, editor, pointer), tool);
                AssertOptionsFit(shown, editor);
            }

            editor.Choose("Draw");
            editor.Choose("Box");
            editor.Choose("Thick");
            editor.Drag((0.2, 0.5), (0.8, 0.8));
            Assert.IsTrue(editor.Session.SelectedLayer is DrawMarkLayer { Kind: MarkKind.Box, Thickness: ImageStash.Layers.Thickness.Thick });
            Assert.AreEqual(initial, Geometry(shown, editor, pointer), "selecting the drawn mark");
            Assert.Contains("Drag a corner to resize, the box to move, or elsewhere to draw.", editor.Texts());
            AssertOptionsFit(shown, editor);

            // A selected mark's handles reshape it (G23), and neither its hint nor a reshape moves the picture (S13).
            var box = ((DrawMarkLayer)editor.Session.SelectedLayer!).Rect!.Value;
            editor.Drag(editor.Canvas.Mapping()!.ToCanvas(box.X + box.Width, box.Y + box.Height), editor.Canvas.Mapping()!.ToCanvas(box.X + box.Width / 2f, box.Y + box.Height / 2f));
            var resized = ((DrawMarkLayer)editor.Session.SelectedLayer!).Rect!.Value;
            Assert.IsTrue(resized.X == box.X && resized.Y == box.Y && resized.Width < box.Width && resized.Height < box.Height, $"its corner resized {box} to {resized}, the opposite corner kept");
            Assert.AreEqual(initial, Geometry(shown, editor, pointer), "resizing the box");
            editor.Choose("Arrow");
            Assert.Contains("Drag an end to re-aim, the arrow to move, or elsewhere to draw.", editor.Texts());
            Assert.AreEqual(initial, Geometry(shown, editor, pointer), "the selected arrow");
            AssertOptionsFit(shown, editor);
        });
    }

    [TestMethod]
    public async Task ALongFontLabelHasItsFullNameAndThePickerActsOnTheOpenLayerAtTheMinimum()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("still.png", 400, 900, (_, _) => TextFieldTests.Grey))).Single();
        await Resize(shown, 458, 508);
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        const string family = "Uninstalled Font With A Family Name Wider Than The Font Button";
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var pointer = editor.Canvas.TranslatePoint(editor.At(0.5, 0.25), editor.Overlay)!.Value;
            var initial = Geometry(shown, editor, pointer);
            editor.Click(0.5, 0.25);
            var opening = editor.Session!.TypingOpening;
            ((TextTool)editor.Session!.Tool).Choose(family);
            Assert.AreEqual(initial, Geometry(shown, editor, pointer), "the long family does not refit the picture");
            AssertOptionsFit(shown, editor);
            var font = editor.Button("Font");
            var label = ((Panel)font.Content!).Children.OfType<TextBlock>().First();
            Assert.AreEqual(family, label.Text);
            Assert.IsTrue(label.TextLayout.TextLines.Single().HasCollapsed, "the label trims inside the button");
            Assert.AreEqual(family, AutomationProperties.GetHelpText(font), "the full name is available to a screen reader");
            Assert.AreEqual(family, ToolTip.GetTip(font));
            font.Focus(NavigationMethod.Tab);
            ShownWindow.Tap(font, Key.Enter);
            Assert.IsNotNull(editor.OpenMenu, "the keyboard opens the font menu");
            var selected = editor.OpenMenu.Items.OfType<MenuItem>().Single(entry => (string?)entry.Header == family);
            Assert.IsTrue(selected.IsChecked, "the menu shows and marks the entire family name");
            editor.OpenMenu.Close();

            editor.Choose("Outline color");
            shown.Window.UpdateLayout();
            TextOptionRowTests.AssertInside(shown, (Control)Picker(editor).GetVisualParent()!, "floating outline picker");
            editor.Choose("None");
            Assert.AreEqual(opening, editor.Session.TypingOpening, "choosing a colour keeps the empty layer's field open");
            var field = editor.Field!;
            Assert.IsTrue(field.Focus(), "the typing field is still reachable");
            ShownWindow.Type(field, "Still typing");
            ShownWindow.Tap(field, Key.Enter, KeyModifiers.Control);
            var text = (TextLayer)editor.Session.SelectedLayer!;
            Assert.AreEqual(("STILL TYPING", Outline.None), (text.Text, text.Outline));

            // Draw once to deliver the real renderer's missing-font report without waiting for a desktop frame.
            using var image = new RenderTargetBitmap(Avalonia.PixelSize.FromSize(editor.Overlay.Bounds.Size, 1), new Vector(96, 96));
            image.Render(editor.Overlay);
        });

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Canvas.Fallbacks.Contains(family)));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var note = editor.Overlay.GetVisualDescendants().OfType<FontFallbackNote>().Single();
            TextOptionRowTests.AssertInside(shown, note, "missing-font note");
            Assert.Contains($"{family} is not installed on this PC, so its text shows in a fallback font.", editor.Texts());
            foreach (var words in new[] { EditorOverlay.OutputSize, "Cancel", "Save" })
            {
                TextOptionRowTests.AssertInside(shown, editor.Button(words), words);
            }

            Assert.IsGreaterThanOrEqualTo(100.0, editor.Canvas.Bounds.Height, "room for the picture while the note shows");
            AssertOptionsFit(shown, editor);
        });
    }

    [TestMethod]
    public async Task ResizeAndTheDockedPickerChangeTheAvailableCanvas()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("still.png", 1400, 500, (_, _) => TextFieldTests.Grey))).Single();
        await Resize(shown, 458, 508);
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        var narrow = TrayDesktop.Get(() =>
        {
            shown.Window.UpdateLayout();
            var box = editor.Box;
            editor.Click(0.5, 0.25);
            editor.Choose("More colors");
            shown.Window.UpdateLayout();
            Assert.IsFalse(Picker(editor).GetVisualAncestors().OfType<ScrollViewer>().Any(), "the picker floats at the minimum");
            Assert.AreEqual(box, editor.Box, "floating leaves the fit alone");
            return (Canvas: BoundsInOverlay(editor.Canvas, editor.Overlay), Fitted: editor.Box);
        });

        await Resize(shown, 880, 620);
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var picker = Picker(editor);
            Assert.IsTrue(picker.GetVisualAncestors().OfType<ScrollViewer>().Any(), "the opened picker docks after resizing");
            TextOptionRowTests.AssertInside(shown, (Control)picker.GetVisualParent()!, "docked picker");
            var docked = BoundsInOverlay(editor.Canvas, editor.Overlay);
            Assert.IsGreaterThan(narrow.Canvas.Width, docked.Width, "resizing made room");
            var box = editor.Box;
            Assert.IsGreaterThan(narrow.Fitted.Width, box.Width, "the picture scales up on resize");
            ShownWindow.Press(editor.Canvas, Key.Escape);
            shown.Window.UpdateLayout();
            Assert.IsGreaterThan(docked.Width, editor.Canvas.Bounds.Width, "closing the dock gave the canvas its width");
            Assert.IsGreaterThan(box.Width, editor.Box.Width, "the picture scales up into the wider canvas");
        });
    }

    [TestMethod]
    public async Task TrimKeepsTheSamePictureAndFilmstripAtTheMinimum()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Fixture("animated.gif", "clip.gif"))).Single();
        await Resize(shown, 458, 508);
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.TimelineComplete));

        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var pointer = editor.Canvas.TranslatePoint(editor.At(0.5, 0.5), editor.Overlay)!.Value;
            var initial = Geometry(shown, editor, pointer);
            var strip = BoundsInOverlay(editor.Filmstrip, editor.Overlay);
            editor.Click(0.5, 0.25);
            Assert.IsNotNull(editor.Session!.SelectedLayer);
            Assert.AreEqual(initial, Geometry(shown, editor, pointer), "adding a Meme on an animation");
            editor.Choose("Trim");
            Assert.AreEqual("Trim", editor.Session!.Tool.Name);
            Assert.AreEqual(initial, Geometry(shown, editor, pointer), "switching to Trim");
            Assert.AreEqual(strip, BoundsInOverlay(editor.Filmstrip, editor.Overlay));
            TextOptionRowTests.AssertInside(shown, editor.Filmstrip, "filmstrip");
            var readouts = editor.Overlay.GetVisualDescendants().OfType<SteadyReadout>().ToArray();
            Assert.HasCount(2, readouts);
            foreach (var readout in readouts)
            {
                TextOptionRowTests.AssertInside(shown, readout, "Trim readout");
            }
            AssertOptionsFit(shown, editor);

            editor.DragOnStrip(editor.Edge(0), editor.Edge(1));
            Assert.AreEqual(new FrameRange(1, 2), editor.Session!.Layers.Trim?.Range, "the start handle dragged on the strip");
            Assert.IsFalse(editor.Session.Playing, "the drag paused the play");
            Assert.AreEqual(initial, Geometry(shown, editor, pointer), "a paused handle drag leaves the picture where it was");
            AssertOptionsFit(shown, editor);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.FrameIndex == 1));
    }

    [TestMethod]
    public async Task ALongTrimRangeWrapsBesideResetWithoutTakingAnotherControlLine()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var path = Path.ChangeExtension(shown.Picture("long-delay.png"), ".gif");
        WriteLongClip(path);
        var item = (await shown.OpenStash(path)).Single();
        await Resize(shown, 458, 508);
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.TimelineComplete));

        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var canvas = BoundsInOverlay(editor.Canvas, editor.Overlay);
            editor.Choose("Trim");
            shown.Window.UpdateLayout();
            editor.DragOnStrip(editor.Edge(0), editor.Edge(6_000));
            shown.Window.UpdateLayout();
            Assert.AreEqual(canvas, BoundsInOverlay(editor.Canvas, editor.Overlay), "the full range fits in the shared space");
            var kept = editor.Overlay.GetVisualDescendants().OfType<SteadyReadout>()
                .Single(text => text.Text?.StartsWith("Keeps ", StringComparison.Ordinal) == true);
            Assert.HasCount(2, kept.TextLayout.TextLines, $"{kept.Text} at {kept.Bounds.Size}");
            Assert.IsFalse(kept.TextLayout.TextLines.Any(line => line.HasCollapsed));
            Assert.IsLessThanOrEqualTo(kept.Bounds.Height + 0.5, kept.TextLayout.Height, "both lines are visible");
            TextOptionRowTests.AssertInside(shown, kept, "kept range");
            var reset = editor.Button("Reset");
            TextOptionRowTests.AssertInside(shown, reset, "Reset");
            var keptBox = BoundsInOverlay(kept, editor.Overlay);
            var resetBox = BoundsInOverlay(reset, editor.Overlay);
            Assert.IsGreaterThanOrEqualTo(keptBox.Right, resetBox.Left, "Reset stays after the range");
            Assert.AreEqual(keptBox.Center.Y, resetBox.Center.Y, 0.5, "Reset stays on the same control line");
            AssertOptionsFit(shown, editor);
        });
    }

    private static void WriteLongClip(string path)
    {
        const int frameCount = 20_000;
        const long delayMs = 655_350;
        using var file = File.Create(path);
        var clip = GifEncoder.Open(file, 1, 1, "long clip").Value!;
        var red = new MediaFrame(1, 1, 0, [0, 0, 255, 255]);
        var blue = new MediaFrame(1, 1, 0, [255, 0, 0, 255]);
        for (var frame = 0; frame < frameCount; frame++)
        {
            Assert.IsTrue(clip.Append(frame % 2 == 0 ? red : blue, frame * delayMs).Succeeded);
        }

        Assert.IsTrue(clip.Stop(frameCount * delayMs).Succeeded);
    }

    [TestMethod]
    public async Task ReopenedCropShowsTheWholeOriginalForWideningAndCaptionAndCompareRefit()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("tall.png", 400, 900, (_, _) => TextFieldTests.Grey))).Single();
        await Resize(shown, 458, 508);
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        TrayDesktop.Run(() =>
        {
            editor.Choose("Crop");
            shown.Window.UpdateLayout();
            editor.Drag((1, 1), (0.75, 0.75));
            Assert.IsNotNull(editor.Session!.Layers.Crop);
            editor.Choose("Save");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));
        var reopened = (await shown.Listing.Store!.All()).Value!.Single();
        await TrayDesktop.Get(() => shown.Editing.Open(reopened, EditOrigin.Grid));

        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var cropped = editor.Canvas.Mapping()!;
            var canvas = BoundsInOverlay(editor.Canvas, editor.Overlay);
            var original = new PixelRect(0, 0, 400, 900);
            Assert.AreNotEqual(original, cropped.Shown);

            editor.Choose("Crop");
            shown.Window.UpdateLayout();
            var inCrop = editor.Canvas.Mapping()!;
            Assert.AreEqual(original, inCrop.Shown, "pixels outside the saved crop are visible");
            Assert.AreEqual(canvas, BoundsInOverlay(editor.Canvas, editor.Overlay));
            var (corner, wider) = (inCrop.ToCanvas(cropped.Shown.Width, cropped.Shown.Height),
                inCrop.ToCanvas(360, 810));
            editor.Press(corner);
            editor.Move(wider);
            editor.Release(wider);
            var (side, edge) = (inCrop.ToCanvas(360, 405), inCrop.ToCanvas(420, 405));
            editor.Press(side);
            editor.Move(edge);
            editor.Release(edge);
            Assert.AreEqual(new PixelRect(0, 0, 400, 810), editor.Session!.Layers.Crop!.Rect, "the right side alone, widened to the original's edge (G22)");
            Assert.AreEqual(inCrop, editor.Canvas.Mapping(), "a side's drag leaves the picture where it was");
            editor.Choose("Text");
            shown.Window.UpdateLayout();
            var widened = editor.Canvas.Mapping()!;
            Assert.IsGreaterThan(cropped.Shown.Width, widened.Shown.Width);
            Assert.IsGreaterThan(cropped.Shown.Height, widened.Shown.Height);
            Assert.AreEqual(canvas, BoundsInOverlay(editor.Canvas, editor.Overlay));

            editor.Choose("Caption bar");
            editor.Click(0.5, 0.25);
            shown.Window.UpdateLayout();
            ShownWindow.Type(editor.Field!, "caption");
            ShownWindow.Tap(editor.Field!, Key.Enter, KeyModifiers.Control);
            shown.Window.UpdateLayout();
            var captioned = editor.Canvas.Mapping()!;
            Assert.IsGreaterThan(widened.Composition.Height, captioned.Composition.Height);
            Assert.AreNotEqual(widened.Box, captioned.Box);
            editor.PressButton("Compare");
            shown.Window.UpdateLayout();
            Assert.AreEqual(inCrop, editor.Canvas.Mapping(), "Compare fits the original");
            editor.ReleaseButton("Compare");
            shown.Window.UpdateLayout();
            Assert.AreEqual(captioned, editor.Canvas.Mapping());
        });
    }

    private static async Task Resize(ShownWindow shown, int width, int height)
    {
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = width;
            shown.Window.Height = height;
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() =>
            Math.Abs(shown.Window.Bounds.Width - width) < 1 && Math.Abs(shown.Window.Bounds.Height - height) < 1));
    }

    private static (Rect Canvas, Rect Fitted, PointF OriginalUnderPointer) Geometry(ShownWindow shown, ShownEditor editor, Point pointer)
    {
        shown.Window.UpdateLayout();
        var canvas = BoundsInOverlay(editor.Canvas, editor.Overlay);
        var mapping = editor.Canvas.Mapping()!;
        var local = editor.Overlay.TranslatePoint(pointer, editor.Canvas)!.Value;
        var start = new Vector(canvas.X, canvas.Y);
        return (canvas, mapping.Box.Translate(start), mapping.ToOriginal(local));
    }

    private static Rect BoundsInOverlay(Control control, EditorOverlay overlay) =>
        new(control.TranslatePoint(default, overlay)!.Value, control.Bounds.Size);

    private static Control Picker(ShownEditor editor) =>
        editor.Overlay.GetVisualDescendants().OfType<Control>()
            .Single(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Color picker");

    private static void AssertOptionsFit(ShownWindow shown, ShownEditor editor)
    {
        var hint = editor.Overlay.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == editor.Session!.Tool.Hint);
        TextOptionRowTests.AssertInside(shown, hint, "gesture hint");
        Assert.IsFalse(hint.TextLayout.TextLines.Any(line => line.HasCollapsed), hint.Text);
        Assert.IsLessThanOrEqualTo(hint.Bounds.Width + 0.5, hint.TextLayout.Width, hint.Text);
        var row = editor.Overlay.GetVisualDescendants().OfType<Control>().Single(control => control is IOptionRow);
        var rowBounds = BoundsInOverlay(row, editor.Overlay).Inflate(0.5);
        foreach (var button in row.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
        {
            TextOptionRowTests.AssertInside(shown, button, "tool choice");
            Assert.IsTrue(rowBounds.Contains(BoundsInOverlay(button, editor.Overlay)), $"tool choice {button.Content} inside its row");
        }
    }
}
