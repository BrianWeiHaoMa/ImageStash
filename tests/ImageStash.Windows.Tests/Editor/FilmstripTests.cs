using Avalonia;
using ImageStash.Editor;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// The editor's filmstrip as the window draws it (G22, G25, F4): on an animated item, between the
/// picture and the tool row whatever the tool, every frame's thumbnail filling a cell of its own, the red playhead on the
/// frame showing; a click on a cell, or a drag along the cells, pausing the play on the frame under the pointer; on a
/// still, no strip and no room taken.
/// </summary>
[TestClass]
public sealed class FilmstripTests
{
    // animated.gif's frames, as the renderer draws them.
    private const uint Red = 0xFFFF0000, Green = 0xFF00FF00, Blue = 0xFF0000FF;

    private static readonly uint Playhead = 0xFF000000u | ((uint)Filmstrip.PlayheadColor.R << 16) | ((uint)Filmstrip.PlayheadColor.G << 8) | Filmstrip.PlayheadColor.B;

    [TestMethod]
    public async Task EveryFrameFillsItsCellBetweenThePictureAndTheToolsUnderEveryToolWithThePlayheadOnTheFrameShowing()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Fixture("animated.gif", "strip.gif"));
        await EditorOverlayTests.Open(shown, items[0]);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Filmstrip.Cells.Count == 3));
        TrayDesktop.Run(() => editor.Session!.Seek(1));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.FrameIndex == 1));

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(editor.Filmstrip.IsEffectivelyVisible);
            Assert.AreEqual(3, editor.Filmstrip.CellCount);
            var canvasBottom = editor.Canvas.TranslatePoint(new Point(0, editor.Canvas.Bounds.Height), editor.Overlay)!.Value.Y;
            var stripTop = editor.Filmstrip.TranslatePoint(default, editor.Overlay)!.Value.Y;
            var toolsTop = editor.Button("Text").TranslatePoint(default, editor.Overlay)!.Value.Y;
            Assert.IsTrue(canvasBottom <= stripTop && stripTop + editor.Filmstrip.Bounds.Height <= toolsTop, $"under the picture ({canvasBottom}), over the tool row ({toolsTop}): {stripTop}");
        });
        var rendered = await RenderedWindow.Of(shown);
        var strip = TrayDesktop.Get(editor.FilmstripInRendering);
        var (s, middle) = (rendered.Scaling, strip.Y + (Filmstrip.LaneHeight + Filmstrip.CellsHeight / 2) * rendered.Scaling);
        foreach (var (frame, colour) in new[] { (0, Red), (1, Green), (2, Blue) })
        {
            var (left, right) = TrayDesktop.Get(() => (editor.Filmstrip.CellLeft(frame), editor.Filmstrip.CellRight(frame)));
            // A quarter into the cell, clear of the playhead in the middle of frame 1's.
            Assert.AreEqual(colour, rendered.At((int)(strip.X + (left + (right - left) / 4) * s), (int)middle), $"frame {frame}'s thumbnail fills its cell");
        }

        var (playhead, first) = TrayDesktop.Get(() => (editor.Cell(1).X, editor.Cell(0).X));
        Assert.AreEqual(Playhead, rendered.At((int)Math.Round(strip.X + playhead * s), (int)middle), "the playhead on the frame showing");
        Assert.AreEqual(Red, rendered.At((int)Math.Round(strip.X + first * s), (int)middle), "and on no other");

        foreach (var tool in new[] { "Crop", "Hide", "Draw", "Text" })
        {
            TrayDesktop.Run(() => editor.Choose(tool));
            await Task.Delay(50);
            TrayDesktop.Run(() => Assert.IsTrue(editor.Filmstrip.IsEffectivelyVisible, $"the filmstrip stays under {tool}"));
        }
    }

    [TestMethod]
    public async Task AClickOnTheThirdThumbnailPausesOnTheThirdFrameAndADragAlongTheCellsFollowsThePointer()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Fixture("animated.gif", "seek.gif"));
        await EditorOverlayTests.Open(shown, items[0]);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Filmstrip.Cells.Count == 3));
        Assert.IsTrue(TrayDesktop.Get(() => editor.Session!.Playing), "it plays from the opening");

        TrayDesktop.Run(() => editor.ClickCell(2));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.FrameIndex == 2));
        await Task.Delay(700);
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(editor.Session!.Playing, "a click on the filmstrip pauses");
            Assert.AreEqual(2, editor.Session.FrameIndex, "on the frame clicked");
        });
        var rendered = await RenderedWindow.Of(shown);
        var picture = TrayDesktop.Get(editor.PictureInRendering);
        Assert.AreEqual(Blue, rendered.At((int)picture.Center.X, (int)picture.Center.Y), "the canvas draws the third frame");

        TrayDesktop.Run(() =>
        {
            editor.PressCell(2);
            editor.MoveToCell(1);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.FrameIndex == 1));
        TrayDesktop.Run(() =>
        {
            editor.MoveToCell(0);
            editor.ReleaseOnCell(0);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.FrameIndex == 0));
        TrayDesktop.Run(() => Assert.IsFalse(editor.Session!.Playing));
    }

    [TestMethod]
    public async Task AClipWithMoreFramesThanTheStripHasPixelsLeavesNoPixelOfTheStripWithoutAFrame()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var folder = Path.Combine(Path.GetTempPath(), $"imagestash-strip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            // A thousand frames of 16 × 12, red and blue in turn.
            var path = Path.Combine(folder, "thousand frames.gif");
            var clip = GifEncoder.Open(path, 16, 12, "thousand frames", HeldFileRetry.Duration).Value!;
            for (var frame = 0; frame < 1000; frame++)
            {
                var colour = frame % 2 == 0 ? new byte[] { 0, 0, 255, 255 } : [255, 0, 0, 255];
                Assert.IsTrue(clip.Append(new MediaFrame(16, 12, 0, Enumerable.Repeat(colour, 16 * 12).SelectMany(pixel => pixel).ToArray()), 20L * frame).Succeeded);
            }

            Assert.IsNotNull(clip.Stop(20_000).Value);
            var items = await shown.OpenStash(path);
            await EditorOverlayTests.Open(shown, items[0]);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.TimelineComplete && editor.Filmstrip.Cells.Count == 1000));
            TrayDesktop.Run(() => editor.Session!.Seek(0));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.FrameIndex == 0 && !editor.Session.Playing));

            var rendered = await RenderedWindow.Of(shown);
            var strip = TrayDesktop.Get(editor.FilmstripInRendering);
            var playhead = strip.X + TrayDesktop.Get(() => editor.Cell(0).X) * rendered.Scaling;
            var middle = (int)(strip.Y + (Filmstrip.LaneHeight + Filmstrip.CellsHeight / 2) * rendered.Scaling);
            var stray = Enumerable.Range((int)Math.Ceiling(strip.X), (int)Math.Floor(strip.Width))
                .Where(x => Math.Abs(x - playhead) > 3)
                .Select(x => rendered.At(x, middle))
                .Where(colour => colour != Red && colour != Blue)
                .ToList();
            Assert.IsEmpty(stray, $"every pixel across the strip shows a frame, none the strip's ground or a blend with it: {string.Join(", ", stray.Distinct().Select(colour => colour.ToString("X8")))}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task ADragBackToTheFrameShowingBeforeTheFramePressedOnHasArrivedEndsOnTheFrameShowing()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Fixture("animated.gif", "scrub back.gif"));
        await EditorOverlayTests.Open(shown, items[0]);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Filmstrip.Cells.Count == 3));
        TrayDesktop.Run(() => editor.Session!.Seek(0));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Session!.FrameIndex == 0 && !editor.Session.Playing));

        // In one turn of the UI thread, so the third frame, asked for by the press, cannot have shown before the drag ends.
        TrayDesktop.Run(() =>
        {
            editor.PressCell(2);
            editor.MoveToCell(0);
            editor.ReleaseOnCell(0);
        });
        await Task.Delay(700);

        TrayDesktop.Run(() => Assert.AreEqual(0, editor.Session!.FrameIndex, "the drag ended on the first frame, which stays"));
    }

    [TestMethod]
    public async Task OnAStillThereIsNoFilmstripAndTheToolRowFollowsThePicture()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("still strip.png", 64, 48, EditorCanvasTests.Checker));
        await EditorOverlayTests.Open(shown, items[0]);

        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(editor.Filmstrip.IsVisible);
            var canvasBottom = editor.Canvas.TranslatePoint(new Point(0, editor.Canvas.Bounds.Height), editor.Overlay)!.Value.Y;
            var toolsTop = editor.Button("Text").TranslatePoint(default, editor.Overlay)!.Value.Y;
            Assert.AreEqual(8, toolsTop - canvasBottom, 0.5, "the tool row's own margin, and no room for a strip");
        });
    }
}
