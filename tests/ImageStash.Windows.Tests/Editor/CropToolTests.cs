using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using static ImageStash.Windows.Tests.Editor.EditorCanvasTests;
using PixelRect = ImageStash.Layers.PixelRect;
using PixelSize = ImageStash.Layers.PixelSize;
using Rect = Avalonia.Rect;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// Crop as the real renderer draws it (G20, G22, G24): while it is current the whole original shows where it
/// showed, the outside of the rectangle dimmed and the inside as it was, with a handle at each corner; a drag from the
/// top-left corner's handle crops on the original's grid and changes the size chip; Compare shows the original alone,
/// without the frame; another tool shows the cropped picture; at the window's minimum, Crop's row, Cancel, the size and
/// Save lie inside the window.
/// </summary>
[TestClass]
public sealed class CropToolTests
{
    [TestMethod]
    public async Task WhileCropIsCurrentTheWholeOriginalShowsTheOutsideDimmedACornerDragChangesTheSizeAndCompareShowsTheOriginalAlone()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("checker.png", 256, 192, Checker));
        await EditorOverlayTests.Open(shown, items[0]);
        TrayDesktop.Run(() =>
        {
            editor.Choose("Crop");
            Assert.IsTrue(editor.Texts().Contains("Drag the corners, or the picture") && editor.Shows("1:1") && editor.Shows("Reset"), "Crop's row (G20)");
        });
        var framed = await RenderedWindow.Of(shown);
        var picture = TrayDesktop.Get(editor.PictureInRendering);
        var scale = picture.Width / 256;

        TrayDesktop.Run(() =>
        {
            editor.Drag(editor.OnCanvas(0, 0), editor.OnCanvas(64, 48));
            Assert.AreEqual(new CropLayer(new PixelRect(64, 48, 192, 144)), editor.Session!.Layers.Layers.Single(), "on the original's grid (G22)");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Size().StartsWith("192 × 144 · ", StringComparison.Ordinal)));
        var cropping = await RenderedWindow.Of(shown);

        TrayDesktop.Run(() => Assert.AreEqual(picture, editor.PictureInRendering(), "the whole original still, where it showed"));
        Assert.AreEqual(0, Differences(framed, cropping, Inside(picture, scale, 80, 64, 160, 64)), "inside the rectangle as it was");
        AssertDimmed(framed, cropping, Inside(picture, scale, 8, 8, 50, 34), "above and left of it");
        AssertDimmed(framed, cropping, Inside(picture, scale, 8, 60, 50, 110), "left of it");
        var handle = AppliedLook.SelectionMark(shown);
        foreach (var (x, y) in new[] { (64, 48), (256, 48), (64, 192), (256, 192) })
        {
            Assert.Contains(handle, cropping.Colors(picture.X + x * scale - 6, picture.Y + y * scale - 6, 12, 12), $"a handle at the corner ({x}, {y})");
        }

        TrayDesktop.Run(() => editor.PressButton("Compare"));
        var comparing = await RenderedWindow.Of(shown);
        TrayDesktop.Run(() => editor.ReleaseButton("Compare"));
        Assert.AreEqual(0, Differences(framed, comparing, Inside(picture, scale, 8, 8, 240, 140)), "while Compare is held, the original alone, nothing dimmed (G20)");
        Assert.DoesNotContain(handle, comparing.Colors(picture.X - 6, picture.Y - 6, picture.Width + 12, picture.Height + 12), "and no frame");

        TrayDesktop.Run(() => editor.Choose("Hide"));
        var hiding = await RenderedWindow.Of(shown);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(new PixelSize(192, 144), editor.Canvas.Mapping()!.Output, "another tool shows the cropped picture");
            var cropped = editor.PictureInRendering();
            Assert.AreEqual(Argb(Checker(66, 50)), hiding.At((int)(cropped.X + 2 * scale), (int)(cropped.Y + 2 * scale)), "starting at the crop's top-left");
            Assert.DoesNotContain(handle, hiding.Colors(cropped.X - 6, cropped.Y - 6, 12, 12), "with no frame");
        });
    }

    [TestMethod]
    public async Task AtTheWindowsMinimumCropsRowCancelTheSizeAndSaveLieInsideIt()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("wide.png", 600, 200, Checker))).Single();
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = 1;
            shown.Window.Height = 1;
        });
        await EditorOverlayTests.Open(shown, item);

        TrayDesktop.Run(() =>
        {
            editor.Choose("Crop");
            editor.Choose("1:1");
            Assert.AreEqual(new CropLayer(new PixelRect(200, 0, 200, 200)), editor.Session!.Layers.Layers.Single(), "1:1 squares the picture about its centre");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Size().StartsWith("200 × 200 · ", StringComparison.Ordinal)));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            Assert.AreEqual(WindowSize.Minimum, new WindowSize(shown.Window.Bounds.Width, shown.Window.Bounds.Height), "the window at its minimum");
            var hint = editor.Overlay.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Text == "Drag the corners, or the picture");
            foreach (var control in new Control[] { hint, editor.Button("1:1"), editor.Button("Reset"), editor.Button("Cancel"), editor.Button(EditorOverlay.OutputSize), editor.Button("Save") })
            {
                TextOptionRowTests.AssertInside(shown, control, $"{(control as TextBlock)?.Text ?? Avalonia.Automation.AutomationProperties.GetName(control)} at the minimum (G20)");
            }

            Assert.IsGreaterThanOrEqualTo(100.0, editor.Canvas.Bounds.Height, "room for the picture");
        });
    }

    /// <summary>Checks that every pixel inside <paramref name="area"/> is darker in every channel after than before.</summary>
    private static void AssertDimmed(RenderedWindow before, RenderedWindow after, Rect area, string where)
    {
        for (var y = (int)Math.Ceiling(area.Y); y < (int)(area.Y + area.Height); y++)
        {
            for (var x = (int)Math.Ceiling(area.X); x < (int)(area.X + area.Width); x++)
            {
                var (was, now) = (before.At(x, y), after.At(x, y));
                Assert.IsTrue(Enumerable.Range(0, 3).All(channel => (now >> (8 * channel) & 0xFF) < (was >> (8 * channel) & 0xFF)), $"{where}, dimmed at ({x}, {y}): {was:X8} then {now:X8}");
            }
        }
    }
}
