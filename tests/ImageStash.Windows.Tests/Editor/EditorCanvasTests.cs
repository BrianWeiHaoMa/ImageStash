using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using PixelRect = ImageStash.Layers.PixelRect;
using Thickness = ImageStash.Layers.Thickness;
using Rect = Avalonia.Rect;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// The editor's canvas as the real renderer draws it (G23, G24, G20, G25, R4, S10): Hide's dragged rectangle blurring
/// or, with Black box, covering what is under it, moved and resized by its handles; the selected layer's handles at its
/// corners and none with nothing selected; a drag landing on the original's pixels it covers whatever the Shrink
/// choice; Compare, held by its button or C, showing the original alone; the operation the canvas hands the
/// compositor still drawing the picture as it was when the compositor renders it after the session ended or changed; a
/// gesture read through the place the picture had at its press, so a caption's corner held still keeps the size it gave.
/// </summary>
[TestClass]
public sealed class EditorCanvasTests
{
    /// <summary>A checker of four-pixel squares in two colours, which a blur changes; neither is black nor a handle's colour.</summary>
    internal static readonly SKColor Light = new(200, 60, 60), Dark = new(60, 200, 60);

    /// <summary>A handle's inside.</summary>
    private const uint White = 0xFFFFFFFF;

    internal static SKColor Checker(int x, int y) => (x / 4 + y / 4) % 2 == 0 ? Light : Dark;

    internal static uint Argb(SKColor color) => 0xFF000000u | ((uint)color.Red << 16) | ((uint)color.Green << 8) | color.Blue;

    [TestMethod]
    public async Task HidesDraggedRectangleBlursWhatIsUnderItOnlyBlackBoxCoversItAndItsHandlesMoveAndResizeIt()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("checker.png", 64, 48, Checker));
        await EditorOverlayTests.Open(shown, items[0]);
        var before = await RenderedWindow.Of(shown);
        var picture = TrayDesktop.Get(editor.PictureInRendering);
        var scale = picture.Width / 64;

        TrayDesktop.Run(() =>
        {
            editor.Choose("Hide");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
        });
        var blurred = await RenderedWindow.Of(shown);

        TrayDesktop.Run(() => Assert.AreEqual(new HideLayer(new PixelRect(16, 12, 32, 24), HideKind.Blur, null), editor.Session!.Layers.Layers.Single(), "one blur over the pixels the drag covered (G23)"));
        Assert.IsGreaterThan(0, Differences(before, blurred, Inside(picture, scale, 18, 14, 28, 20)), "the pixels inside are blurred");
        var (above, right) = TrayDesktop.Get(() => (
            editor.InRendering(new Rect(editor.OnCanvas(0, 0), new Point(editor.OnCanvas(64, 0).X, editor.OnCanvas(0, 12).Y - ShownEditor.BeyondHandle))),
            editor.InRendering(new Rect(new Point(editor.OnCanvas(48, 0).X + ShownEditor.BeyondHandle, editor.OnCanvas(0, 0).Y), editor.OnCanvas(64, 48)))));
        Assert.AreEqual(0, Differences(before, blurred, above), "the pixels above it, beyond its handles, as they were");
        Assert.AreEqual(0, Differences(before, blurred, right), "and right of it");
        var handle = AppliedLook.SelectionMark(shown);
        foreach (var (x, y) in new[] { (16, 12), (48, 12), (16, 36), (48, 36) })
        {
            CollectionAssert.AreEquivalent(new[] { White }, blurred.Colors(TrayDesktop.Get(() => editor.InsideHandle(editor.OnCanvas(x, y)))).ToList(), $"a handle drawn at the corner ({x}, {y}): its white");
        }

        // Nothing selected, no handle; Black box chosen, a second drag covers wholly.
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(editor.Canvas, Key.Escape);
            editor.Choose("Hide");
            editor.Choose("Black box");
            editor.Drag((2 / 64d, 2 / 48d), (10 / 64d, 10 / 48d));
            Assert.AreEqual(new HideLayer(new PixelRect(2, 2, 8, 8), HideKind.Black, null), editor.Session!.Layers.Layers[1]);
            Assert.AreEqual(HideKind.Blur, ((HideLayer)editor.Session.Layers.Layers[0]).Kind, "the blur not selected when Black box was chosen stays a blur");
        });
        TrayDesktop.Run(() => ShownWindow.Press(editor.Canvas, Key.Escape));
        var deselected = await RenderedWindow.Of(shown);
        CollectionAssert.AreEquivalent(new[] { 0xFF000000u }, deselected.Colors(picture.X + 2 * scale, picture.Y + 2 * scale, 8 * scale, 8 * scale).ToList(), "one colour inside: black (G23)");
        Assert.DoesNotContain(handle, deselected.Colors(TrayDesktop.Get(() => editor.InRendering(editor.Box.Inflate(ShownEditor.BeyondHandle)))), "no handle with nothing selected");

        // A click selects the blur; its bottom-right handle resizes it; a drag inside, away from the handles, moves it.
        TrayDesktop.Run(() =>
        {
            editor.Click(32 / 64d, 24 / 48d);
            Assert.AreEqual(0, editor.Session!.Selected);
            // Each drag goes far enough on the canvas to pass the drag threshold, six units, whatever a picture pixel spans.
            var by = (int)Math.Ceiling(editor.OnOriginal(6));
            editor.Drag(editor.OnCanvas(48, 36), editor.OnCanvas(48 + by, 36 + by));
            Assert.AreEqual(new PixelRect(16, 12, 32 + by, 24 + by), ((HideLayer)editor.Session.Layers.Layers[0]).Rect, "the corner dragged out");
            // Up and left, where the picture has room: the grown blur already reaches its right and bottom edges.
            editor.Drag(editor.OnCanvas(30, 24), editor.OnCanvas(30 - by, 24 - by));
            Assert.AreEqual(new PixelRect(16 - by, 12 - by, 32 + by, 24 + by), ((HideLayer)editor.Session.Layers.Layers[0]).Rect, "moved by the drag's offset");
        });
    }

    [TestMethod]
    public async Task AtHalfSizeADragFromTheShownQuarterToTheShownThreeQuarterPointHidesTheSamePixelsOfTheOriginalAsAtFullSize()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("checker.png", 64, 48, Checker));
        await EditorOverlayTests.Open(shown, items[0]);

        TrayDesktop.Run(() =>
        {
            editor.ChooseShrink("Half size");
            editor.Choose("Hide");
            editor.Choose("Black box");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Size().StartsWith("32 × 24 · ")));
        TrayDesktop.Run(() =>
        {
            var scaling = shown.Window.RenderScaling;
            Assert.AreEqual(new Rect(editor.Box.X, editor.Box.Y, 32 / scaling, 24 / scaling), editor.Box, "the 32 × 24 output shown at its own size: one of its pixels to one of the display's");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
            Assert.AreEqual(new PixelRect(16, 12, 32, 24), ((HideLayer)editor.Session!.Layers.Layers.OfType<HideLayer>().Single()).Rect, "on the original's grid, not the output's (G23)");
            ShownWindow.Press(editor.Canvas, Key.Escape);
        });
        var rendered = await RenderedWindow.Of(shown);
        var picture = TrayDesktop.Get(editor.PictureInRendering);
        CollectionAssert.AreEquivalent(new[] { 0xFF000000u }, rendered.Colors(picture.X + 9, picture.Y + 7, 14, 10).ToList(), "black inside the dragged rectangle on screen");
        Assert.DoesNotContain(0xFF000000u, rendered.Colors(picture.X + 1, picture.Y + 1, 6, 4), "the picture's own colours outside it");

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(editor.Canvas, Key.Z, KeyModifiers.Control);
            editor.ChooseShrink("Full size");
            Assert.IsTrue(editor.Session!.Layers.IsEmpty, "the hide undone and the Shrink back at full size");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
            Assert.AreEqual(new PixelRect(16, 12, 32, 24), ((HideLayer)editor.Session.Layers.Layers.Single()).Rect, "the same pixels at full size");
        });
    }

    [TestMethod]
    public async Task DrawsBoxInASwatchAtAThicknessIsItsOwnLayerAClickOnItsInkSelectsItAndDelRemovesIt()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("checker.png", 64, 48, Checker));
        await EditorOverlayTests.Open(shown, items[0]);
        var blue = new Rgba(0x2F, 0x6B, 0xFF, 0xFF);

        TrayDesktop.Run(() =>
        {
            editor.Choose("Draw");
            Assert.IsTrue(editor.Shows("Pen") && editor.Shows("Marker") && editor.Shows("Arrow") && editor.Shows("Box") && editor.Shows("More colors") && editor.Shows("Thin") && editor.Shows("Medium") && editor.Shows("Thick"), "Draw's option row (G23)");
            Assert.Contains("Drag to draw a mark; click a mark to select it", editor.Texts(), "and what the mouse does (G20)");
            editor.Choose("Box");
            editor.Choose("#2F6BFF");
            editor.Choose("Thick");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
            Assert.AreEqual(new DrawMarkLayer(MarkKind.Box, [], new PixelRect(16, 12, 32, 24), blue, Thickness.Thick, null), editor.Session!.Layers.Layers.Single());
            ShownWindow.Press(editor.Canvas, Key.Escape);
            editor.Choose("Pen");
            editor.Drag((0.1, 0.9), (0.9, 0.9));
            Assert.IsTrue(editor.Session.Layers.Layers[1] is DrawMarkLayer { Kind: MarkKind.Pen, Points.Count: > 1 }, "a stroke, a layer of its own");
            ShownWindow.Press(editor.Canvas, Key.Escape);
        });
        var drawn = await RenderedWindow.Of(shown);
        var picture = TrayDesktop.Get(editor.PictureInRendering);
        var scale = picture.Width / 64;
        Assert.Contains(0xFF2F6BFFu, drawn.Colors(picture.X + 15 * scale, picture.Y + 20 * scale, 2 * scale, 8 * scale), "blue along the box's left edge");

        TrayDesktop.Run(() =>
        {
            editor.Click(16 / 64d, 24 / 48d);
            Assert.AreEqual(0, editor.Session!.Selected, "a click on the box's ink selects it (G23)");
        });
        var selected = await RenderedWindow.Of(shown);
        CollectionAssert.AreEquivalent(new[] { White }, selected.Colors(TrayDesktop.Get(() => editor.InsideHandle(editor.OnCanvas(48, 36)))).ToList(), "its corner handle's white");

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(editor.Canvas, Key.Delete);
            Assert.IsTrue(editor.Session!.Layers.Layers.Single() is DrawMarkLayer { Kind: MarkKind.Pen }, "Del removes the selected mark alone (G23)");
        });
    }

    [TestMethod]
    public async Task CompareHeldByItsButtonOrCShowsTheOriginalAloneAndLetGoTheLayersAgain()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("checker.png", 64, 48, Checker));
        await EditorOverlayTests.Open(shown, items[0]);
        var original = await RenderedWindow.Of(shown);
        TrayDesktop.Run(() =>
        {
            editor.Choose("Hide");
            editor.Choose("Black box");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
            ShownWindow.Press(editor.Canvas, Key.Escape);
        });
        var picture = TrayDesktop.Get(editor.PictureInRendering);
        var inside = Inside(picture, picture.Width / 64, 18, 14, 28, 20);

        TrayDesktop.Run(() => editor.PressButton("Compare"));
        var held = await RenderedWindow.Of(shown);
        TrayDesktop.Run(() => editor.ReleaseButton("Compare"));
        var letGo = await RenderedWindow.Of(shown);
        TrayDesktop.Run(() => ShownWindow.Press(editor.Canvas, Key.C));
        var heldByKey = await RenderedWindow.Of(shown);
        TrayDesktop.Run(() => ShownWindow.Release(editor.Canvas, Key.C));
        var letGoByKey = await RenderedWindow.Of(shown);

        Assert.AreEqual(0, Differences(original, held, Inside(picture, 1, 0, 0, picture.Width, picture.Height)), "G20: the original alone while held");
        Assert.DoesNotContain(0xFF000000u, held.Colors(inside.X, inside.Y, inside.Width, inside.Height));
        CollectionAssert.AreEquivalent(new[] { 0xFF000000u }, letGo.Colors(inside.X, inside.Y, inside.Width, inside.Height).ToList(), "the hide back once let go");
        Assert.AreEqual(0, Differences(original, heldByKey, Inside(picture, 1, 0, 0, picture.Width, picture.Height)), "G25: C holds Compare");
        CollectionAssert.AreEquivalent(new[] { 0xFF000000u }, letGoByKey.Colors(inside.X, inside.Y, inside.Width, inside.Height).ToList());
    }

    [TestMethod]
    public async Task TheOperationTheCanvasHandsTheCompositorDrawsThePictureAsItWasAfterItsSessionEndedOrChanged()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var endings = new (string Name, Func<StashItem, Task> End)[]
        {
            ("Esc with nothing selected", async _ => await Keys(shown, editor, Key.Escape)),
            ("Save", async _ => await Keys(shown, editor, Key.S)),
            ("the item removed", async item =>
            {
                await TrayDesktop.Get(() => shown.Listing.Store!.Remove([item.Id]));
                await TrayDesktop.Get(() => shown.Listing.Refresh());
                foreach (var entry in RecycleBinEntries.Of(item.FilePath))
                {
                    File.Delete(entry.Recycled);
                    File.Delete(entry.Record);
                }
            }),
            ("Save as new item", async _ =>
            {
                TrayDesktop.Run(() =>
                {
                    editor.ClickMore();
                    editor.ClickMenuEntry("Save as new item");
                });
                await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.Session?.Item.Name.EndsWith(" 2") == true));
            }),
            ("Ctrl+Z", _ =>
            {
                TrayDesktop.Run(() => ShownWindow.Press(editor.Canvas, Key.Z, KeyModifiers.Control));
                return Task.CompletedTask;
            }),
        };

        // Each picture its own, by one pixel, so none is a duplicate of another.
        var items = await shown.OpenStash([.. Enumerable.Range(0, endings.Length + 1).Select(i =>
            shown.Picture($"checker {i}.png", 64, 48, (x, y) => x == 63 && y == 47 ? new SKColor((byte)(10 + i), 0, 0) : Checker(x, y)))]);
        StashItem Numbered(int i) => items.Single(item => item.Name == $"checker {i}");
        for (var i = 0; i < endings.Length; i++)
        {
            var item = Numbered(i);
            await EditorOverlayTests.Open(shown, item);
            var (operation, size, inside, outside, corner) = TrayDesktop.Get(() =>
            {
                editor.Choose("Hide");
                editor.Choose("Black box");
                editor.Drag((0.25, 0.25), (0.75, 0.75));
                ShownWindow.Press(editor.Canvas, Key.Escape);
                // Its own renderer draws at 96 DPI, so canvas units are its pixels: sampled through the canvas's mapping, at
                // the first of its pixels wholly inside a checker square (a square spans fewer pixels than four at a scale above 1),
                // and in the far corner at the pixel holding a square's centre, the square beside the one holding the pixel
                // that sets each picture apart.
                return (editor.Canvas.DrawOperation()!, editor.Canvas.Bounds.Size, editor.OnCanvas(32, 24), editor.OnCanvas(0, 0), editor.OnCanvas(58, 42));
            });

            await endings[i].End(item);
            await Task.Delay(100);

            // The pixels are the observation: a render through RenderTargetBitmap swallows what the operation throws.
            var pixels = TrayDesktop.Get(() => RenderedAlone(operation, size));
            var name = endings[i].Name;
            Assert.AreEqual(0xFF000000u, pixels.At((int)inside.X, (int)inside.Y), $"after {name}: black inside the hide, as it was when taken");
            Assert.AreEqual(Argb(Checker(2, 2)), pixels.At((int)Math.Ceiling(outside.X), (int)Math.Ceiling(outside.Y)), $"after {name}: the picture's own colour outside it");
            Assert.AreEqual(Argb(Checker(58, 42)), pixels.At((int)corner.X, (int)corner.Y), $"after {name}: and in its far corner");
            if (TrayDesktop.Get(() => shown.Editing.IsOpen))
            {
                TrayDesktop.Run(() => editor.Session!.Deselect());
                await Keys(shown, editor, Key.Escape);
            }
        }

        // An operation the compositor has disposed takes nothing from the canvas, which still draws the picture and the hide.
        await EditorOverlayTests.Open(shown, Numbered(endings.Length));
        TrayDesktop.Run(() =>
        {
            editor.Choose("Hide");
            editor.Choose("Black box");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
            ShownWindow.Press(editor.Canvas, Key.Escape);
            editor.Canvas.DrawOperation()!.Dispose();
        });
        var rendered = await RenderedWindow.Of(shown);
        var picture = TrayDesktop.Get(editor.PictureInRendering);
        CollectionAssert.AreEquivalent(new[] { 0xFF000000u }, rendered.Colors(picture.X + 20, picture.Y + 16, 24, 16).ToList(), "the hide still drawn");
        Assert.AreEqual(Argb(Checker(10, 10)), rendered.At((int)(picture.X + 10), (int)(picture.Y + 10)), "and the picture round it");
    }

    [TestMethod]
    public async Task ADoubleClickEditsATextItsRoundHandleTurnsItSnappingWithShiftAndADoubleClickOnTheHandleResetsIt()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey));
        await EditorOverlayTests.Open(shown, items[0]);
        var handle = AppliedLook.SelectionMark(shown);
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            ShownWindow.Press(editor.Canvas, Key.Escape);
            editor.DoubleClick(editor.OnCanvas(100, Text(editor).Box.Y + 5));
            Assert.IsTrue(editor.Session!.Typing && editor.Field?.Text == "HI", "a double-click on a text edits it (G21)");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
        });
        var level = await RenderedWindow.Of(shown);
        var (rotate, centre) = TrayDesktop.Get(() => (RotateHandle(editor), Centre(editor)));
        CollectionAssert.AreEquivalent(new[] { White }, level.Colors(TrayDesktop.Get(() => editor.InsideRoundHandle(rotate))).ToList(), "the round handle above the box's top centre, its white (G21)");
        // The handles are the accent's, which the window's accent buttons show too: only the canvas is read.
        var canvas = TrayDesktop.Get(() => RenderedWindow.Place(shown.Window, editor.Canvas));
        var flat = level.Bounds(handle, canvas);
        Assert.IsGreaterThan(flat.Bottom - flat.Top, flat.Right - flat.Left, "the outline of a level text lies wide");

        TrayDesktop.Run(() =>
        {
            Assert.IsLessThan(centre.Y, rotate.Y, "above the box");
            editor.Drag(rotate, centre + new Vector(60, 0));
            Assert.AreEqual(90, Text(editor).Rotation, 1, "the pointer right of the centre turns it a quarter clockwise (G21)");
        });
        var turned = await RenderedWindow.Of(shown);
        var upright = turned.Bounds(handle, canvas);
        Assert.IsGreaterThan(upright.Right - upright.Left, upright.Bottom - upright.Top, "and its outline turns with it, standing tall");

        TrayDesktop.Run(() =>
        {
            var at40 = centre + new Vector(60 * Math.Sin(40 * Math.PI / 180), -60 * Math.Cos(40 * Math.PI / 180));
            editor.Drag(RotateHandle(editor), at40, KeyModifiers.Shift);
            Assert.AreEqual(45, Text(editor).Rotation, "Shift held snaps to 15° steps (G21)");
            editor.DoubleClick(RotateHandle(editor));
            Assert.AreEqual(0, Text(editor).Rotation, "a double-click on the round handle resets it");
            Assert.IsFalse(editor.Session!.Typing, "and edits nothing");
        });
    }

    [TestMethod]
    public async Task OnAPictureFillingTheCanvasATopTextsRoundHandleShowsInsideTheCanvasAndTurnsIt()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("square.png", 600, 600, (_, _) => TextFieldTests.Grey));
        await EditorOverlayTests.Open(shown, items[0]);
        TrayDesktop.Run(() =>
        {
            Assert.IsLessThan(600, editor.Box.Height, "the picture scaled down to the canvas");
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
        });
        // Read once the frame showing it has rendered: the committed text's box is measured again on the way there.
        var rendered = await RenderedWindow.Of(shown);
        var rotate = TrayDesktop.Get(() =>
        {
            var at = RotateHandle(editor);
            var canvas = editor.Canvas.Bounds;
            Assert.IsTrue(at.X >= 8 && at.Y >= 8 && at.X <= canvas.Width - 8 && at.Y <= canvas.Height - 8, $"a top text's round handle at {at} lies inside the {canvas.Size} canvas, where it can be seen and pressed (G21)");
            return at;
        });
        CollectionAssert.AreEquivalent(new[] { White }, rendered.Colors(TrayDesktop.Get(() => editor.InsideRoundHandle(rotate))).ToList(), "drawn there, its white");
        TrayDesktop.Run(() =>
        {
            editor.Drag(rotate, Centre(editor) + new Vector(80, 0));
            Assert.AreEqual(90, Text(editor).Rotation, 1, "and turns the text");
        });
    }

    [TestMethod]
    public async Task ATextAtThePicturesTopGrownByItsCornerKeepsItsRoundHandleInsideTheCanvas()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("square.png", 600, 600, (_, _) => TextFieldTests.Grey));
        await EditorOverlayTests.Open(shown, items[0]);
        TrayDesktop.Run(() =>
        {
            editor.Choose("Label");
            editor.Click(0.5, 0.02);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            var (size, box) = (Text(editor).FontSize, Text(editor).Box);
            var corner = editor.OnCanvas(box.X + box.Width, box.Y + box.Height);
            editor.Drag(corner, corner + new Vector(40, 40));
            Assert.IsGreaterThan(size * 3 / 2, Text(editor).FontSize, "the corner grew the text by half again or more (G21)");
            var at = RotateHandle(editor);
            var canvas = editor.Canvas.Bounds;
            Assert.IsTrue(at.X >= 8 && at.Y >= 8 && at.X <= canvas.Width - 8 && at.Y <= canvas.Height - 8, $"the grown text's round handle at {at} lies inside the {canvas.Size} canvas, where it can be pressed (G21)");
            editor.Drag(at, Centre(editor) + new Vector(80, 0));
            Assert.AreEqual(90, Text(editor).Rotation, 1, "and turns the text");
        });
    }

    [TestMethod]
    public async Task ACaptionsCornerHeldStillKeepsTheSizeItsMoveGaveThroughAnotherMoveThereAndTheRelease()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("grey.png", 192, 144, (_, _) => TextFieldTests.Grey));
        await EditorOverlayTests.Open(shown, items[0]);
        TrayDesktop.Run(() =>
        {
            editor.Choose("Caption bar");
            editor.Click(0.5, 0.5);
            ShownWindow.Type(editor.Field!, "one");
            foreach (var line in new[] { "two", "three", "four", "five", "six" })
            {
                // The field's box takes Enter once it is laid out, as it is on screen.
                shown.Window.UpdateLayout();
                ShownWindow.Press(editor.Field!, Key.Enter);
                ShownWindow.Type(editor.Field!, line);
            }

            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            Assert.AreEqual("one\ntwo\nthree\nfour\nfive\nsix", ((CaptionBarLayer)editor.Session!.Layers.Layers[0]).Text, "six lines, so the band is tall beside the picture");
            var band = LayerGeometry.Bounds(editor.Session.Layers, 0, new ImageStash.Layers.PixelSize(192, 144))!.Value;
            var (size, box) = (FontSize(editor), editor.Box);
            var corner = editor.OnCanvas(band.Right, band.Bottom);
            var to = editor.OnCanvas(band.Right + band.Width, band.Bottom + band.Height);

            editor.Press(corner);
            editor.Move(to);
            var first = FontSize(editor);
            Assert.IsGreaterThan(size, first, "the corner grows the caption (G21)");
            Assert.AreNotEqual(box, editor.Box, "and the taller picture shows elsewhere on the canvas");
            editor.Move(to);
            Assert.AreEqual(first, FontSize(editor), "another move to the same place resizes nothing");
            editor.Release(to);
            Assert.AreEqual(first, FontSize(editor), "nor does the release there: the size is where the pointer stopped");
        });

        static int FontSize(ShownEditor editor) => ((CaptionBarLayer)editor.Session!.Layers.Layers[0]).FontSize;
    }

    [TestMethod]
    public async Task ASelectedCaptionBarShowsItsBandsFourCornersAndACornerDraggedOutGrowsItsFontAndBand()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey));
        await EditorOverlayTests.Open(shown, items[0]);
        var band = TrayDesktop.Get(() =>
        {
            editor.Choose("Caption bar");
            editor.Click(0.5, 0.5);
            ShownWindow.Type(editor.Field!, "me");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            Assert.IsTrue(editor.Session!.SelectedLayer is CaptionBarLayer { Text: "me" }, "the bar selected");
            return LayerGeometry.Bounds(editor.Session.Layers, 0, new ImageStash.Layers.PixelSize(200, 150))!.Value;
        });
        var rendered = await RenderedWindow.Of(shown);
        foreach (var (x, y) in new[] { (band.Left, band.Top), (band.Right, band.Top), (band.Left, band.Bottom), (band.Right, band.Bottom) })
        {
            CollectionAssert.AreEquivalent(new[] { White }, rendered.Colors(TrayDesktop.Get(() => editor.InsideHandle(editor.OnCanvas(x, y)))).ToList(), $"a handle at the band's corner ({x}, {y}), its white over the band and beyond it (G21)");
        }

        var (size, height) = TrayDesktop.Get(() => (((CaptionBarLayer)editor.Session!.Layers.Layers[0]).FontSize, editor.Box.Height));
        TrayDesktop.Run(() =>
        {
            var corner = editor.OnCanvas(band.Right, band.Bottom);
            editor.Drag(corner, corner + new Vector(30, 30));
            Assert.IsGreaterThan(size, ((CaptionBarLayer)editor.Session!.Layers.Layers[0]).FontSize, "the corner scales its font size (G21)");
            Assert.IsGreaterThan(height, editor.Box.Height, "and the band, so the picture shown, grows taller");
            var layers = editor.Session.Layers;
            var inside = editor.OnCanvas(band.Left + band.Width / 3, band.Top + band.Height / 2);
            editor.Drag(inside, inside + new Vector(20, 15));
            Assert.AreEqual(layers, editor.Session.Layers, "a drag inside the band, away from its corners, changes nothing: a bar is not moved");
        });
    }

    private static TextLayer Text(ShownEditor editor) => editor.Session!.Layers.Layers.OfType<TextLayer>().Single();

    /// <summary>The selected text's round handle on the canvas.</summary>
    internal static Point RotateHandle(ShownEditor editor)
    {
        var original = new ImageStash.Layers.PixelSize(editor.Session!.Frame.Width, editor.Session.Frame.Height);
        var at = LayerGeometry.Handles(editor.Session.Layers, editor.Session.Selected!.Value, original, editor.Canvas.HandleRoom()).Single(each => each.Handle == Handle.Rotate).At;
        return editor.OnCanvas(at.X, at.Y);
    }

    /// <summary>The selected text's box's centre on the canvas.</summary>
    internal static Point Centre(ShownEditor editor)
    {
        var box = Text(editor).Box;
        return editor.OnCanvas(box.X + box.Width / 2f, box.Y + box.Height / 2f);
    }

    /// <summary>Esc, or Ctrl with S, pressed on the canvas; waits for the editor to close and the window to return to the grid.</summary>
    private static async Task Keys(ShownWindow shown, ShownEditor editor, Key key)
    {
        TrayDesktop.Run(() => ShownWindow.Press(editor.Canvas, key, key == Key.S ? KeyModifiers.Control : KeyModifiers.None));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen && shown.Grid.IsFocused));
    }

    /// <summary>
    /// <paramref name="operation"/> rendered into a bitmap of the canvas's size through a control of the check's own,
    /// whose render hands it to the drawing context as the canvas hands it to the compositor.
    /// </summary>
    private static RenderedWindow RenderedAlone(ICustomDrawOperation operation, Size size)
    {
        var host = new OperationHost(operation) { Width = size.Width, Height = size.Height };
        host.Measure(size);
        host.Arrange(new Rect(size));
        var pixelSize = new Avalonia.PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height));
        using var target = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        target.Render(host);
        var pixels = new uint[pixelSize.Width * pixelSize.Height];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            target.CopyPixels(new Avalonia.PixelRect(pixelSize), pinned.AddrOfPinnedObject(), pixels.Length * 4, pixelSize.Width * 4);
        }
        finally
        {
            pinned.Free();
        }

        return new RenderedWindow([.. pixels.Select(pixel => pixel | 0xFF000000)], pixelSize.Width, 1, default, default);
    }

    private sealed class OperationHost(ICustomDrawOperation operation) : Control
    {
        public override void Render(DrawingContext context) => context.Custom(operation);
    }

    /// <summary>A rectangle of the original's grid as the rendering shows it, in physical pixels.</summary>
    internal static Rect Inside(Rect picture, double scale, double x, double y, double width, double height) =>
        new(picture.X + x * scale, picture.Y + y * scale, width * scale, height * scale);

    /// <summary>How many pixels inside <paramref name="area"/> differ between two renderings.</summary>
    internal static int Differences(RenderedWindow before, RenderedWindow after, Rect area)
    {
        var count = 0;
        for (var y = (int)Math.Ceiling(area.Y); y < (int)(area.Y + area.Height); y++)
        {
            for (var x = (int)Math.Ceiling(area.X); x < (int)(area.X + area.Width); x++)
            {
                count += before.At(x, y) == after.At(x, y) ? 0 : 1;
            }
        }

        return count;
    }
}
