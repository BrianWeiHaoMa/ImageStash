using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// Trim and the time ranges as the window draws and drives them (G20, G21, G22, G24, G25, F3): Trim
/// third on the tool row of an animation; at the window's minimum its row, wrapped, with play and pause, the position,
/// the kept range, Reset, Cancel, the size and Save inside the window; a drag of the left handle on the filmstrip changing
/// the trim, the row's words and the size chip, the frame cut away dimmed and the handles drawn under Trim alone; the
/// selected label's bar over the filmstrip under Text, its start dragged; Space, Right and Del under Trim; the saved GIF
/// holding the kept frames at their own timing with the label in its frame only.
/// </summary>
[TestClass]
public sealed class TrimToolTests
{
    [TestMethod]
    public async Task AtTheMinimumTrimsRowFitsAndADragOfTheLeftHandleChangesTheTrimTheWordsTheSizeAndDimsTheFrameCutAway()
    {
        using var shown = ShownWindow.Open();
        var handle = AppliedLook.SelectionMark(shown);
        using var editor = new ShownEditor(shown);
        // Frames of many colours, so the saved file's size, and the size chip with it, follows the frames kept.
        var item = (await shown.OpenStash(Clip(shown, "trimmed", (x, y, frame) => ((byte)(x * 7 + y * 3 + frame * 50), (byte)(x * y + frame * 90), (byte)(y * 11 - x + frame * 30))))).Single();
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = 1;
            shown.Window.Height = 1;
        });
        await EditorOverlayTests.Open(shown, item);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.TimelineComplete && editor.Filmstrip.Cells.Count == 3));
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "Text", "Crop", "Trim", "Hide", "Draw" }, editor.Tools(), "Trim third, on an animation (G20)");
            editor.Choose("Trim");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Texts().Contains("Keeps 0:00.0 – 0:00.6 · 0.6 s of 0.6 s")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !editor.Size().EndsWith("…", StringComparison.Ordinal)));
        var sizeBefore = TrayDesktop.Get(editor.Size);
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            WindowMinimum.AssertAt(shown.Window.Bounds.Size, "the window at its minimum");
            var words = editor.Overlay.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToList();
            var position = words.Single(text => (text.Text ?? "").EndsWith(" / 0:00.6", StringComparison.Ordinal));
            var kept = words.Single(text => text.Text == "Keeps 0:00.0 – 0:00.6 · 0.6 s of 0.6 s");
            var hint = words.Single(text => text.Text == "Drag the handles under the picture");
            foreach (var control in new Control[] { editor.Button(editor.Shows("Pause") ? "Pause" : "Play"), position, kept, editor.Button("Reset"), hint, editor.Button("Cancel"), editor.Button(EditorOverlay.OutputSize), editor.Button("Save") })
            {
                TextOptionRowTests.AssertInside(shown, control, $"{(control as TextBlock)?.Text ?? Avalonia.Automation.AutomationProperties.GetName(control)} at the minimum (G20, G21)");
            }

            Assert.IsGreaterThan(0.0, hint.TranslatePoint(default, kept)!.Value.Y, "the row wrapped: what the mouse does on a line below the kept range");
        });
        var before = await RenderedWindow.Of(shown);

        TrayDesktop.Run(() => editor.DragOnStrip(editor.Edge(0), editor.Edge(1)));

        TrayDesktop.Run(() => Assert.AreEqual(new LayerList([new TrimLayer(new FrameRange(1, 2))]), editor.Session!.Layers, "the trim on the original's frames (G22)"));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Texts().Contains("Keeps 0:00.1 – 0:00.6 · 0.5 s of 0.6 s")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Size() != sizeBefore && !editor.Size().EndsWith("…", StringComparison.Ordinal)));
        TrayDesktop.Run(() => StringAssert.StartsWith(editor.Size(), "160 × 120 · ", "the size chip follows the kept frames (G24)"));
        var after = await RenderedWindow.Of(shown);
        var strip = TrayDesktop.Get(editor.FilmstripInRendering);
        var s = after.Scaling;
        var middle = (int)(strip.Y + (Filmstrip.LaneHeight + Filmstrip.CellsHeight / 2) * s);
        var (first, second, end) = TrayDesktop.Get(() => (editor.Filmstrip.CellLeft(0), editor.Filmstrip.CellLeft(1), editor.Filmstrip.CellRight(2)));
        var (inFirst, inSecond) = ((int)(strip.X + (first + (second - first) / 3) * s), (int)(strip.X + (second + (end - second) / 6) * s));
        Assert.AreNotEqual(before.At(inFirst, middle), after.At(inFirst, middle), "the first frame dimmed once the trim cuts it away (G22)");
        Assert.AreEqual(before.At(inSecond, middle), after.At(inSecond, middle), "the second frame, kept, as it was");
        Assert.Contains(handle, after.Colors(strip.X + second * s - 4, middle - 4, 8, 8), "the left handle at the kept frames' start");
        Assert.Contains(handle, after.Colors(strip.X + end * s - 8, middle - 4, 8, 8), "the right handle at their end");

        TrayDesktop.Run(() => editor.Choose("Text"));
        var underText = await RenderedWindow.Of(shown);
        // Text's row is not Trim's height, so the strip may stand elsewhere.
        var moved = TrayDesktop.Get(editor.FilmstripInRendering).Y - strip.Y;
        Assert.DoesNotContain(handle, underText.Colors(strip.X + second * s - 4, middle + moved - 4, 8, 8), "the handles are Trim's");
        Assert.AreEqual(after.At(inFirst, middle), underText.At(inFirst, (int)(middle + moved)), "the frame cut away stays dimmed under every tool");
    }

    [TestMethod]
    public async Task TheSelectedLabelsBarShowsUnderTextItsStartDragsAndTheTrimmedClipSavesItsKeptFramesAtTheirTimingWithTheLabelInItsFrameOnly()
    {
        using var shown = ShownWindow.Open();
        var handle = AppliedLook.SelectionMark(shown);
        using var editor = new ShownEditor(shown);
        // Flat red, green and blue frames, so the label's white letters are the only white.
        var item = (await shown.OpenStash(Clip(shown, "label clip", (_, _, frame) => frame switch { 0 => (0, 0, 255), 1 => (0, 255, 0), _ => (255, 0, 0) }))).Single();
        await EditorOverlayTests.Open(shown, item);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.TimelineComplete && editor.Filmstrip.Cells.Count == 3));
        TrayDesktop.Run(() =>
        {
            editor.Choose("Label");
            editor.Click(0.3, 0.3);
            ShownWindow.Type(editor.Field!, "W");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            Assert.AreEqual(new FrameRange(0, 2), editor.Session!.SelectedRange, "the whole clip by default (G22)");
        });
        var labelled = await RenderedWindow.Of(shown);
        var strip = TrayDesktop.Get(editor.FilmstripInRendering);
        var s = labelled.Scaling;
        Assert.Contains(handle, labelled.Colors(strip.X, strip.Y + 1 * s, 4 * s, (Filmstrip.LaneHeight - 2) * s), "the bar's start in the lane over the first frame");

        TrayDesktop.Run(() => editor.DragOnStrip(editor.Edge(0, inLane: true), editor.Edge(2, inLane: true)));

        TrayDesktop.Run(() => Assert.AreEqual(new FrameRange(2, 2), ((TextLayer)editor.Session!.SelectedLayer!).Range, "its start dragged to the last frame"));
        var ranged = await RenderedWindow.Of(shown);
        var third = TrayDesktop.Get(() => editor.Filmstrip.CellLeft(2));
        Assert.DoesNotContain(handle, ranged.Colors(strip.X, strip.Y + 1 * s, 4 * s, (Filmstrip.LaneHeight - 2) * s), "no bar over the first frame now");
        Assert.Contains(handle, ranged.Colors(strip.X + third * s, strip.Y + 1 * s, 4 * s, (Filmstrip.LaneHeight - 2) * s), "its start at the third");

        TrayDesktop.Run(() =>
        {
            editor.Choose("Trim");
            editor.DragOnStrip(editor.Edge(0), editor.Edge(1));
            Assert.AreEqual(new FrameRange(2, 2), ((TextLayer)editor.Session!.Layers.Layers[0]).Range, "a trim moved later does not move it (G22)");
            Assert.IsTrue(editor.Session.Playing);
            editor.Canvas.Focus();
            ShownWindow.Press(editor.Canvas, Key.Space);
            ShownWindow.Release(editor.Canvas, Key.Space);
            Assert.IsFalse(editor.Session.Playing, "Space pauses under Trim (G25)");
        });
        var paused = TrayDesktop.Get(() => editor.Session!.FrameIndex);
        TrayDesktop.Run(() => ShownWindow.Press(editor.Canvas, paused == 2 ? Key.Left : Key.Right));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.FrameIndex == (paused == 2 ? 1 : paused + 1)));
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(editor.Canvas, Key.Delete);
            Assert.HasCount(2, editor.Session!.Layers.Layers, "Del under Trim removes neither the label nor the trim");
        });

        TrayDesktop.Run(() => ShownWindow.Press(editor.Canvas, Key.S, KeyModifiers.Control));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));
        await TrayDesktop.Get(() => shown.Listing.Settle());

        var saved = TrayDesktop.Get(() => shown.Listing.Items.Single());
        Assert.IsTrue(saved.FilePath.EndsWith(".gif", StringComparison.Ordinal), saved.FilePath);
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 160, 120, true, 2, 500), saved.Facts, "two frames lasting 500 ms (G19)");
        var frames = CaptureDesk.Frames(saved.FilePath);
        CollectionAssert.AreEqual(new[] { 200, 300 }, frames.Select(frame => frame.DurationMs).ToList(), "the kept frames at their own timing, delays of 20 and 30 cs (F3)");
        Assert.IsEmpty(White(frames[0]), "the first kept frame without the label ranged on the next");
        Assert.IsNotEmpty(White(frames[1]), "the second with its white letters");
        var folder = TrayDesktop.Get(() => shown.Listing.Store!.Folder);
        Assert.HasCount(3, CaptureDesk.Frames(folder.InOriginals("label clip.gif")), "the frame cut away still in the original (G22)");
    }

    /// <summary>
    /// A GIF of three 160 × 120 frames lasting 100, 200 and 300 ms, each pixel's blue, green and red from
    /// <paramref name="colorAt"/>, written by the product's encoder into the check's folder.
    /// </summary>
    private static string Clip(ShownWindow shown, string name, Func<int, int, int, (byte B, byte G, byte R)> colorAt)
    {
        var path = Path.ChangeExtension(shown.Picture(name + ".png", 1, 1, (_, _) => SKColors.Black), ".gif");
        var encoder = GifEncoder.Open(path, 160, 120, name, HeldFileRetry.Duration).Value!;
        for (var frame = 0; frame < 3; frame++)
        {
            var pixels = new byte[160 * 120 * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var (b, g, r) = colorAt(i / 4 % 160, i / 4 / 160, frame);
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (b, g, r, 255);
            }

            Assert.IsTrue(encoder.Append(new MediaFrame(160, 120, 0, pixels), new[] { 0L, 100, 300 }[frame]).Succeeded);
        }

        Assert.IsTrue(encoder.Stop(600).Succeeded);
        return path;
    }

    /// <summary>The pixels of <paramref name="frame"/> that are white, as the label's letters are.</summary>
    private static List<int> White(MediaFrame frame) =>
        Enumerable.Range(0, frame.Width * frame.Height).Where(i => frame.Pixels[i * 4] == 255 && frame.Pixels[i * 4 + 1] == 255 && frame.Pixels[i * 4 + 2] == 255).ToList();
}
