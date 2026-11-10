using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using Rect = Avalonia.Rect;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// Text saved, end to end on the real window (G21, G26, G29, G8, G19, F3, N7, R4): a Meme in Comic Sans MS with a yellow
/// fill, a blue outline of its own and a turn snapped with Shift, shown on the canvas pixel for pixel as Save writes it;
/// the saved file in the grid, the preview and the handoff, the original untouched, and the layer live at the next Edit,
/// its text changed and saved again; a Label on a still WebP with transparency saved as a PNG whose handoff bitmap keeps
/// the transparency.
/// </summary>
[TestClass]
public sealed class TextSavedTests
{
    private const string Picture = "caption me.png";
    private static readonly PasteTarget Relay = PresetTarget.App("Relay");
    private static readonly Rgba Yellow = ColorChoices.Swatches[2];
    private static readonly Rgba Blue = new(0x20, 0x40, 0xFF, 0xFF);

    [TestMethod]
    public async Task TheCanvasShowsATurnedMemeInComicSansPixelForPixelAsSaveWritesIt()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(Marked(shown))).Single();
        await TurnedMeme(shown, editor, item);
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(editor.Canvas, Key.Escape);
            Assert.IsTrue(editor.Session!.Selected is null && !editor.Session.Compare, "deselected, Compare not held: no outline or handle over the picture");
            var scaling = shown.Window.RenderScaling;
            Assert.AreEqual(new Size(160 / scaling, 120 / scaling), editor.Box.Size, "the picture shown 1 : 1, at its output size: one of its pixels to one of the display's");
            Assert.IsEmpty(editor.Canvas.Fallbacks, "Comic Sans MS is installed here: nothing drawn in a fallback");
        });
        var canvas = await RenderedWindow.Of(shown);
        var place = TrayDesktop.Get(editor.PictureInRendering);
        var layers = TrayDesktop.Get(() => editor.Session!.Layers);

        await Save(shown);

        var saved = TrayDesktop.Get(() => shown.Listing.Items.Single());
        var file = CaptureDesk.FirstFrame(saved.FilePath);
        Assert.AreEqual((160, 120), (file.Width, file.Height));
        var differences = Differences(canvas, place, file);
        Assert.IsEmpty(differences, $"G21: the finished picture is the canvas as shown, pixel for pixel: {string.Join("; ", differences.Take(5))}");

        // What was chosen is in the file: the fill and the outline exactly, the lettering turned, and in its own face.
        Assert.IsGreaterThan(0, Count(file, Yellow), "the fill's yellow");
        Assert.IsGreaterThan(0, Count(file, Blue), "the outline's blue");
        var slope = Slope(Points(file, Yellow).ToList());
        Assert.AreEqual(45, slope, 10, $"one line of letters reads along a line turned 45° clockwise, down to the right: {slope:F1}°");
        var original = TrayDesktop.Get(() => shown.Listing.Store!.Folder).InOriginals(Picture);
        Assert.IsEmpty(Differences(Composed(original, layers, "Comic Sans MS"), file), "the file is Media's composition of the list");
        Assert.IsGreaterThan(50, Differences(Composed(original, layers, SKTypeface.Default.FamilyName), file).Count, "the lettering is Comic Sans MS's, not the default face's (F8)");
    }

    [TestMethod]
    public async Task TheSavedMemeShowsInTheGridThePreviewAndTheHandoffTheOriginalUntouchedAndIsLiveAtTheNextEdit()
    {
        var target = new PresetTarget { Before = null };
        using var shown = ShownWindow.Open(target: target);
        using var editor = new ShownEditor(shown);
        var imported = Marked(shown);
        var importedHash = SHA256.HashData(File.ReadAllBytes(imported));
        var item = (await shown.OpenStash(imported)).Single();
        await Summon(shown);
        var gridBefore = await RenderedWindow.Of(shown);
        var tileBefore = TrayDesktop.Get(() => EditingInTheWindowTests.Tile(shown, item.Id));
        var made = await TurnedMeme(shown, editor, item);

        await Save(shown);

        var saved = TrayDesktop.Get(() =>
        {
            Assert.IsTrue(shown.Grid.IsFocused, "back to the grid Edit was started from (G26)");
            CollectionAssert.AreEqual(new[] { item.Id }, shown.Listing.Selection.ToList(), "with the item selected");
            return shown.Listing.Items.Single();
        });
        Assert.IsTrue(saved.Edited && saved.RenderedRevision == 1, "saved once");
        var file = CaptureDesk.FirstFrame(saved.FilePath);
        var folder = TrayDesktop.Get(() => shown.Listing.Store!.Folder);
        CollectionAssert.AreEqual(importedHash, SHA256.HashData(File.ReadAllBytes(folder.InOriginals(Picture))), "the original's bytes unchanged in originals\\ (G29)");
        CollectionAssert.AreEqual(new[] { saved.FilePath }, Directory.GetFiles(folder.Root, "*.png").ToList(), "the root holds one file for the item, its finished picture");
        Assert.HasCount(1, Directory.GetFiles(folder.Originals));

        // The grid: the tile read again, showing the yellow lettering.
        var gridAfter = await RenderedWindow.Of(shown);
        var tileAfter = TrayDesktop.Get(() => EditingInTheWindowTests.Tile(shown, item.Id));
        Assert.AreEqual(tileBefore.Size, tileAfter.Size);
        Assert.IsFalse(Yellowish(gridBefore, tileBefore), "no yellow in the tile before");
        Assert.IsTrue(Yellowish(gridAfter, tileAfter), "the grid shows the finished picture's lettering (G26)");

        await PreviewShows(shown, saved, file);

        // The handoff: the finished picture on the real clipboard, as its file, its PNG and its bitmap.
        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.Enter));
        await ShownWindow.Eventually(() => target.Pasted.Count == 1 && shown.Uses() == 1);
        var read = TestClipboard.Read();
        CollectionAssert.AreEqual(new[] { saved.FilePath }, read.Files.ToList(), "the finished picture's file (G8)");
        Assert.IsEmpty(Differences(Decoded(read.Png!), file), "its PNG decodes to the finished picture");
        Assert.IsEmpty(DibDifferences(read.DibV5!, file), "and so does its bitmap");

        await Summon(shown);
        await ReopenAndChange(shown, editor, saved, made, file);
        await Save(shown);

        var again = TrayDesktop.Get(() => shown.Listing.Items.Single());
        Assert.AreEqual(2, again.RenderedRevision, "saved again");
        var changed = CaptureDesk.FirstFrame(again.FilePath);
        Assert.IsGreaterThan(Count(file, Yellow), Count(changed, Yellow), "the longer text in the finished picture");
        CollectionAssert.AreEqual(importedHash, SHA256.HashData(File.ReadAllBytes(folder.InOriginals(Picture))), "the original still unchanged (G29)");
        // ImageStash's own copy of the item, untouched since the handoff, is replaced by the picture as saved again (G26).
        await ShownWindow.Eventually(() => TestClipboard.Read().DibV5 is { } bitmap && DibDifferences(bitmap, changed).Count == 0);
        CollectionAssert.AreEqual(new[] { again.FilePath }, TestClipboard.Read().Files.ToList());
        Assert.AreEqual(1, shown.Uses(), "the refresh records no use (G10)");
    }

    [TestMethod]
    public async Task ALabelOnAStillWebPWithTransparencyIsSavedAsAPngWhoseHandoffBitmapKeepsTheTransparency()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        // Clear at the left, half clear beside it, opaque grey from there.
        var webp = WebP(shown, "clear.webp", (x, _) => x < 40 ? SKColors.Transparent : x < 60 ? TextFieldTests.Grey.WithAlpha(128) : TextFieldTests.Grey);
        var importedHash = SHA256.HashData(File.ReadAllBytes(webp));
        var item = (await shown.OpenStash(webp)).Single();
        Assert.AreEqual((ImageFormat.WebP, 160, 120, false), (item.Facts.Format, item.Facts.Width, item.Facts.Height, item.Facts.Animated), "a still WebP");
        var imported = CaptureDesk.FirstFrame(item.FilePath);
        Assert.AreEqual(0, Alpha(imported, 5, 5), "transparent at its left");
        var half = Alpha(imported, 50, 5);
        Assert.IsTrue(half is > 0 and < 255, $"and partly transparent beside it: {half}");
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Choose("Label");
            editor.Click(0.6, 0.4);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
        });

        await Save(shown);

        var saved = TrayDesktop.Get(() => shown.Listing.Items.Single());
        var label = (TextLayer)(await TrayDesktop.Get(() => shown.Listing.Store!.ForEdit(item.Id))).Value!.Layers.Layers.Single();
        Assert.IsTrue(saved.FilePath.EndsWith(".png", StringComparison.Ordinal) && saved.Facts.Format == ImageFormat.Png, $"an edited still WebP's finished picture is a PNG (F3, N7): {saved.FilePath}");
        var folder = TrayDesktop.Get(() => shown.Listing.Store!.Folder);
        CollectionAssert.AreEqual(importedHash, SHA256.HashData(File.ReadAllBytes(folder.InOriginals("clear.webp"))), "the WebP original kept as it came (G29)");
        var file = CaptureDesk.FirstFrame(saved.FilePath);
        Assert.AreEqual(0, Alpha(file, 5, 5), "transparent where the original is and no letter lies");
        Assert.AreEqual(half, Alpha(file, 50, 5), "and partly transparent where it is");
        var letters = Points(file, ColorChoices.Swatches[0]).Where(point => point.X >= label.Box.X && point.X < label.Box.X + label.Box.Width && point.Y >= label.Box.Y && point.Y < label.Box.Y + label.Box.Height).ToList();
        Assert.IsNotEmpty(letters, "the label's white letters in its box");

        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "Enter · Copy" }, shown.FooterHints(), "no paste target: Enter copies (G8)");
            ShownWindow.Press(shown.Search, Key.Enter);
        });
        await ShownWindow.Eventually(() => shown.Uses() == 1);
        var read = TestClipboard.Read();
        CollectionAssert.AreEqual(new[] { saved.FilePath }, read.Files.ToList(), "the finished PNG's file (G8)");
        var dib = read.DibV5!;
        Assert.AreEqual((124, 160, 120), (BitConverter.ToInt32(dib, 0), BitConverter.ToInt32(dib, 4), BitConverter.ToInt32(dib, 8)), "a BITMAPV5HEADER, bottom-up rows");
        Assert.AreEqual(((ushort)32, 3u), (BitConverter.ToUInt16(dib, 14), BitConverter.ToUInt32(dib, 16)), "32 bits a pixel, BI_BITFIELDS");
        CollectionAssert.AreEqual(new[] { 0x00FF0000u, 0x0000FF00u, 0x000000FFu, 0xFF000000u }, Enumerable.Range(0, 4).Select(i => BitConverter.ToUInt32(dib, 40 + i * 4)).ToList(),
            "red, green, blue and alpha masks: BGRA bytes, alpha read as alpha");
        Assert.AreEqual(0, DibPixel(dib, 160, 120, 5, 5)[3], "the bitmap keeps the transparency (G8)");
        Assert.AreEqual(half, DibPixel(dib, 160, 120, 50, 5)[3], "partial transparency too");
        Assert.IsTrue(letters.All(point => DibPixel(dib, 160, 120, point.X, point.Y).SequenceEqual(new byte[] { 255, 255, 255, 255 })), "and the letters' white");
        Assert.IsEmpty(DibDifferences(dib, file), "every pixel the finished picture's");
    }

    /// <summary>An opaque grey 160 × 120 PNG with two darker marks, written into the check's folder.</summary>
    private static string Marked(ShownWindow shown) => shown.Picture(Picture, 160, 120, (x, y) =>
        x is >= 20 and < 50 && y is >= 20 and < 40 ? new SKColor(60, 60, 60)
        : x is >= 110 and < 140 && y is >= 70 and < 110 ? new SKColor(160, 160, 160)
        : TextFieldTests.Grey);

    /// <summary>A 160 × 120 picture coloured by <paramref name="colorAt"/>, written as a WebP into the check's folder.</summary>
    private static string WebP(ShownWindow shown, string name, Func<int, int, SKColor> colorAt)
    {
        var png = shown.Picture(Path.ChangeExtension(name, ".png"), 160, 120, colorAt);
        using var bitmap = SKBitmap.Decode(png);
        using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 100);
        var path = Path.ChangeExtension(png, ".webp");
        File.WriteAllBytes(path, data.ToArray());
        File.Delete(png);
        return path;
    }

    /// <summary>The flyout summoned by the open shortcut over the app Relay, which becomes the paste target (G8).</summary>
    private static async Task Summon(ShownWindow shown)
    {
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, Relay), new SummonAnchor(null));
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());
    }

    /// <summary>
    /// The editor open on the item, Text current: a click in the picture's lower half adds a Meme, "Hello" typed and left;
    /// Comic Sans MS from the font menu; the yellow swatch; the outline swatch's picker given the blue's hex; the round
    /// handle dragged with Shift held to 40° from where it rests, which snaps to 45°. The layer made, still selected.
    /// </summary>
    private static async Task<TextLayer> TurnedMeme(ShownWindow shown, ShownEditor editor, StashItem item)
    {
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            Assert.IsInstanceOfType<TextTool>(editor.Session!.Tool, "Text current at open (G20)");
            editor.Click(0.5, 0.75);
            ShownWindow.Type(editor.Field!, "Hello");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            editor.ClickMenuButton("Font");
            editor.ClickMenuEntry("Comic Sans MS");
            editor.Choose(ColorChoices.Hex(Yellow));
        });
        // A click lands on what the window has drawn: the outline swatch once the row is.
        await Task.Delay(200);
        TrayDesktop.Run(() => shown.Click(editor.Button("Outline color")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Named(editor, "Hex") is not null));
        return TrayDesktop.Get(() =>
        {
            var hex = (TextBox)Named(editor, "Hex")!;
            hex.Focus();
            hex.SelectAll();
            ShownWindow.Type(hex, ColorChoices.Hex(Blue));
            ShownWindow.Press(hex, Key.Enter);
            ShownWindow.Press(hex, Key.Escape);
            Assert.IsNull(Named(editor, "Hex"), "the picker closed, the layer still selected");

            var (handle, centre) = (EditorCanvasTests.RotateHandle(editor), EditorCanvasTests.Centre(editor));
            var reach = centre.Y - handle.Y;
            var at40 = centre + new Vector(reach * Math.Sin(40 * Math.PI / 180), -reach * Math.Cos(40 * Math.PI / 180));
            editor.Drag(handle, at40, KeyModifiers.Shift);

            var turned = (TextLayer)editor.Session!.Layers.Layers.Single();
            Assert.AreEqual(new TextLayer(TextPreset.Meme, "HELLO", "Comic Sans MS", turned.FontSize, Yellow, Outline.Of(Blue), turned.Box, 45, null), turned,
                "a Meme in Comic Sans MS, yellow, outlined in blue, turned 45° (G21)");
            Assert.IsGreaterThan(60, turned.Box.Y, "placed at the bottom, the click being in the lower half");
            return turned;
        });
    }

    /// <summary>
    /// Space on the saved item: the preview reads and shows the finished picture pixel for pixel, its facts naming the
    /// text; then Esc closes it.
    /// </summary>
    private static async Task PreviewShows(ShownWindow shown, StashItem saved, MediaFrame file)
    {
        TrayDesktop.Run(() => shown.OpenPreview(saved));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count == 3 && shown.PreviewPicture.Image is not null));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("Edited here: text · the original is kept", shown.Preview.Facts[2], "G19");
            var image = shown.PreviewPicture.Image!;
            Assert.AreEqual(new Avalonia.PixelSize(file.Width, file.Height), image.PixelSize, "at full size");
            var pixels = new byte[file.Pixels.Length];
            var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                image.CopyPixels(new Avalonia.PixelRect(image.PixelSize), pinned.AddrOfPinnedObject(), pixels.Length, file.Width * 4);
            }
            finally
            {
                pinned.Free();
            }

            CollectionAssert.AreEqual(file.Pixels, pixels, "the preview reads the finished picture");
        });
        var preview = await RenderedWindow.Of(shown);
        Assert.AreEqual(new Size(file.Width, file.Height), preview.Picture.Size, "shown at its own size, never enlarged (G19)");
        Assert.IsEmpty(Differences(preview, preview.Picture, file), "the preview shows the finished picture's pixels");
        TrayDesktop.Run(() => ShownWindow.Press((Control)shown.Focused!, Key.Escape));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Preview.IsOpen));
    }

    /// <summary>
    /// The next Edit on the saved item: the layer back exactly as <paramref name="made"/>, drawn as <paramref name="file"/>
    /// shows it, selected by a click, its text reopened by a double-click and made "Hello again".
    /// </summary>
    private static async Task ReopenAndChange(ShownWindow shown, ShownEditor editor, StashItem saved, TextLayer made, MediaFrame file)
    {
        await EditorOverlayTests.Open(shown, saved);
        var centre = TrayDesktop.Get(() =>
        {
            var meme = (TextLayer)editor.Session!.Layers.Layers.Single();
            Assert.AreEqual(made, meme, "the layer as it was made, every value (G26)");
            Assert.IsNull(editor.Session.Selected);
            return editor.OnCanvas(meme.Box.X + meme.Box.Width / 2f, meme.Box.Y + meme.Box.Height / 2f);
        });
        var reopened = await RenderedWindow.Of(shown);
        Assert.IsEmpty(Differences(reopened, TrayDesktop.Get(editor.PictureInRendering), file), "drawn live as it was saved");
        TrayDesktop.Run(() =>
        {
            editor.Press(centre);
            editor.Release(centre);
            Assert.AreEqual(0, editor.Session!.Selected, "a click on the lettering selects the layer");
            editor.DoubleClick(centre);
            Assert.AreEqual("HELLO", editor.Field?.Text, "a double-click opens its text");
            editor.Field!.CaretIndex = editor.Field.Text!.Length;
            ShownWindow.Type(editor.Field, " again");
            ShownWindow.Press(editor.Field, Key.Enter, KeyModifiers.Control);
            Assert.AreEqual("HELLO AGAIN", ((TextLayer)editor.Session.Layers.Layers.Single()).Text);
        });
    }

    /// <summary>Ctrl+S on the canvas, and the window back from the editor with its listing read again.</summary>
    private static async Task Save(ShownWindow shown)
    {
        TrayDesktop.Run(() => ShownWindow.Press(shown.Window.GetVisualDescendants().OfType<EditorCanvas>().Single(), Key.S, KeyModifiers.Control));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen && shown.Grid.IsFocused));
        await TrayDesktop.Get(() => shown.Listing.Settle());
    }

    /// <summary>The overlay's control a screen reader reads by these words, while it shows.</summary>
    private static Control? Named(ShownEditor editor, string words) =>
        editor.Overlay.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == words);

    /// <summary>
    /// The opaque original at <paramref name="originalPath"/> composed with <paramref name="layers"/> by Media's routine, at
    /// the output size, every text in <paramref name="family"/>.
    /// </summary>
    private static MediaFrame Composed(string originalPath, LayerList layers, string family)
    {
        var refaced = new LayerList(layers.Layers.Select(layer => layer is TextLayer text ? text with { Font = family } : layer).ToList());
        var original = CaptureDesk.FirstFrame(originalPath);
        using var bitmap = new SKBitmap(new SKImageInfo(original.Width, original.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            Assert.IsTrue(LayerComposer.Compose(canvas, original, refaced, 0, Path.GetFileNameWithoutExtension(originalPath)).Succeeded);
        }

        return new MediaFrame(original.Width, original.Height, 0, bitmap.Bytes);
    }

    /// <summary>An opaque PNG's pixels, as BGRA.</summary>
    private static MediaFrame Decoded(byte[] png)
    {
        using var decoded = SKBitmap.Decode(png);
        using var bitmap = decoded.Copy(SKColorType.Bgra8888);
        return new MediaFrame(bitmap.Width, bitmap.Height, 0, bitmap.Bytes);
    }

    /// <summary>Every pixel of <paramref name="file"/> that the rendering shows otherwise inside <paramref name="place"/>, which shows it 1 : 1.</summary>
    private static List<string> Differences(RenderedWindow rendered, Rect place, MediaFrame file)
    {
        var differences = new List<string>();
        for (var y = 0; y < file.Height; y++)
        {
            for (var x = 0; x < file.Width; x++)
            {
                var i = (y * file.Width + x) * 4;
                var expected = 0xFF000000u | (uint)file.Pixels[i + 2] << 16 | (uint)file.Pixels[i + 1] << 8 | file.Pixels[i];
                // The place lies on whole display pixels; its product with a scaling such as 1.75 may fall a hair short of one.
                var shownThere = rendered.At((int)Math.Round(place.X) + x, (int)Math.Round(place.Y) + y);
                if (shownThere != expected)
                {
                    differences.Add($"({x}, {y}) shown {shownThere:X8}, saved {expected:X8}");
                }
            }
        }

        return differences;
    }

    /// <summary>The pixels at which two frames of one size differ.</summary>
    private static List<string> Differences(MediaFrame one, MediaFrame other) =>
        Enumerable.Range(0, one.Width * one.Height)
            .Where(i => !one.Pixels.AsSpan(i * 4, 4).SequenceEqual(other.Pixels.AsSpan(i * 4, 4)))
            .Select(i => $"({i % one.Width}, {i / one.Width})").ToList();

    /// <summary>
    /// The pixels at which a CF_DIBV5 bitmap of bottom-up rows differs from <paramref name="file"/>: its alpha anywhere,
    /// its colour where the file has any alpha.
    /// </summary>
    private static List<string> DibDifferences(byte[] dib, MediaFrame file)
    {
        var differences = new List<string>();
        for (var y = 0; y < file.Height; y++)
        {
            for (var x = 0; x < file.Width; x++)
            {
                var i = (y * file.Width + x) * 4;
                var pixel = DibPixel(dib, file.Width, file.Height, x, y);
                var alpha = file.Pixels[i + 3];
                if (pixel[3] != alpha || alpha > 0 && !pixel.AsSpan().SequenceEqual(file.Pixels.AsSpan(i, 4)))
                {
                    differences.Add($"({x}, {y})");
                }
            }
        }

        return differences;
    }

    private static byte Alpha(MediaFrame frame, int x, int y) => frame.Pixels[(y * frame.Width + x) * 4 + 3];

    /// <summary>The B, G, R and A bytes of pixel (<paramref name="x"/>, <paramref name="y"/>) of a CF_DIBV5 bitmap of bottom-up rows.</summary>
    private static byte[] DibPixel(byte[] dib, int width, int height, int x, int y) => dib.AsSpan(124 + ((height - 1 - y) * width + x) * 4, 4).ToArray();

    /// <summary>Every point of <paramref name="frame"/> of exactly <paramref name="color"/>, opaque.</summary>
    private static IEnumerable<(int X, int Y)> Points(MediaFrame frame, Rgba color) =>
        Enumerable.Range(0, frame.Width * frame.Height)
            .Where(i => frame.Pixels[i * 4] == color.B && frame.Pixels[i * 4 + 1] == color.G && frame.Pixels[i * 4 + 2] == color.R && frame.Pixels[i * 4 + 3] == 255)
            .Select(i => (i % frame.Width, i / frame.Width));

    private static int Count(MediaFrame frame, Rgba color) => Points(frame, color).Count();

    /// <summary>
    /// The angle of the line the points lie along, their principal axis, in degrees from the x axis towards the y axis,
    /// which points down: positive turns clockwise on screen. Independent of how the lettering was drawn.
    /// </summary>
    private static double Slope(List<(int X, int Y)> points)
    {
        var (meanX, meanY) = (points.Average(p => p.X), points.Average(p => p.Y));
        var xx = points.Sum(p => (p.X - meanX) * (p.X - meanX));
        var yy = points.Sum(p => (p.Y - meanY) * (p.Y - meanY));
        var xy = points.Sum(p => (p.X - meanX) * (p.Y - meanY));
        return Math.Atan2(2 * xy, xx - yy) / 2 * 180 / Math.PI;
    }

    /// <summary>Whether a pixel inside <paramref name="area"/> of the rendering is yellow: red and green high, blue low.</summary>
    private static bool Yellowish(RenderedWindow rendered, Rect area) =>
        rendered.Colors(area.X, area.Y, area.Width, area.Height).Any(pixel => (pixel >> 16 & 0xFF) > 180 && (pixel >> 8 & 0xFF) > 150 && (pixel & 0xFF) < 110);

}
