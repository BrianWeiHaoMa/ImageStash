using System.Drawing;
using System.Security.Cryptography;
using Avalonia.Input;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using SkiaSharp;

namespace ImageStash.Tests.Window;

/// <summary>
/// One transparent still through the window's editing, menu and handoff entries (G8, G19–G26, F6–F8):
/// every layer made by its tool, saved, selected again, changed, copied as a new item and reverted with Undo.
/// Saved pixels are read independently with SkiaSharp; no window or real clipboard is used.
/// </summary>
[TestClass]
public sealed class EditingAStillWithEveryLayerTests
{
    private static readonly PixelSize Size = new(640, 640);
    private static readonly PixelRect Blur = new(60, 440, 120, 80);
    private static readonly PixelRect Black = new(300, 440, 120, 80);
    private static readonly Rgba Fill = new(255, 220, 0, 255);
    private static readonly Rgba Edge = new(0, 80, 255, 255);

    [TestMethod]
    public async Task AllLayersSurviveSaveReopenMenuCopyRevertAndItsUndoWithTheOriginalAndTransparencyKept()
    {
        using var listed = await ListedStash.Open();
        var original = TransparentPng();
        var originalHash = SHA256.HashData(original);
        var first = await listed.Stash.ImportPicture("the audacity", "transparent.png", original);
        Require(await listed.Organising.AddTag([first], "reaction funny"));
        Require(await listed.Organising.SetFavorite([first], true));
        listed.Listing.Focus(first.Id);
        Require(await listed.Editing.OpenOnSelection(null));

        var layers = AddEveryLayer(listed.Editing.Session!);
        Require(await listed.Editing.Save());
        Assert.IsFalse(listed.Editing.IsOpen);
        first = listed.Listing.Items.Single();
        await AssertSaved(listed, first, layers, originalHash);
        using (var rendered = SKBitmap.Decode(first.FilePath))
        using (var source = SKBitmap.Decode(original))
        {
            AssertEveryLayerRendered(source, rendered);
            await AssertHandoff(listed, first, rendered);
        }

        var firstRender = File.ReadAllBytes(first.FilePath);
        var changed = await ReopenSelectEveryLayerAndChangeText(listed, first, layers);
        first = listed.Listing.Items.Single();
        await AssertSaved(listed, first, changed, originalHash);
        var secondRender = File.ReadAllBytes(first.FilePath);
        AssertTextRenderChanged(firstRender, secondRender);

        var menu = listed.ItemMenu;
        var second = await CopyFromMenu(listed, menu, first, changed, originalHash, secondRender);
        // The menu continues editing the copy; Save closes that session before reverting the first item.
        Require(await listed.Editing.Save());
        await RevertFromMenuAndUndo(listed, menu, first, second, changed, original, originalHash, secondRender);
        Assert.IsEmpty(listed.Failures);
    }

    private static LayerList AddEveryLayer(EditorSession session)
    {
        session.HandleRoom = new RectangleF(0, 0, Size.Width, Size.Height);
        session.Tool = session.Text();
        session.Text().Choose(TextKind.Meme);
        session.Click(100, 50);
        Assert.IsTrue(session.Typing);
        session.SetTypedText("HELLO");
        // Choose is the callback the font menu hands to the Text tool.
        session.Text().Choose(FontMenu.Listed.Single(font => font == "Comic Sans MS"));
        session.Text().ChooseFill(Fill);
        session.Text().ChooseOutline(Outline.Of(Edge));
        var text = (TextLayer)session.SelectedLayer!;
        var handle = LayerGeometry.Handles(session.Layers, session.Selected!.Value, Size, session.HandleRoom)
            .Single(grip => grip.Handle == Handle.Rotate).At;
        var centre = Centre(text.Box);
        var radius = MathF.Sqrt(MathF.Pow(handle.X - centre.X, 2) + MathF.Pow(handle.Y - centre.Y, 2));
        var angle = 22 * Math.PI / 180;
        var target = new PointF(centre.X + radius * (float)Math.Sin(angle), centre.Y - radius * (float)Math.Cos(angle));
        session.ShiftHeld = true;
        session.DragThrough((handle.X, handle.Y), (target.X, target.Y));
        session.ShiftHeld = false;
        text = (TextLayer)session.SelectedLayer!;
        Assert.AreEqual(15d, text.Rotation, "a non-multiple 22° pointer angle snaps to 15° with Shift");
        Assert.AreEqual((TextPreset.Meme, "Comic Sans MS", Fill, Outline.Of(Edge)),
            (text.Preset, text.Font, text.Fill, text.Outline));

        session.Deselect();
        session.Drawing(MarkKind.Arrow);
        session.Draw().Choose(new Rgba(255, 0, 0, 255));
        session.DragThrough((50, 300), (170, 340));
        session.Deselect();
        session.Draw().Choose(MarkKind.Box);
        session.Draw().Choose(new Rgba(0, 0, 255, 255));
        session.DragThrough((240, 290), (330, 360));
        session.Deselect();
        session.Draw().Choose(MarkKind.Marker);
        session.Draw().Choose(new Rgba(0, 255, 0, 255));
        session.DragThrough((420, 310), (450, 330), (500, 310));
        session.Tool = session.Hide();
        session.Hide().Choose(HideKind.Blur);
        session.DragThrough((Blur.X, Blur.Y), (Blur.X + Blur.Width, Blur.Y + Blur.Height));
        session.Deselect();
        session.Hide().Choose(HideKind.Black);
        session.DragThrough((Black.X, Black.Y), (Black.X + Black.Width, Black.Y + Black.Height));

        Assert.HasCount(6, session.Layers.Layers);
        CollectionAssert.AreEqual(new[] { MarkKind.Arrow, MarkKind.Box, MarkKind.Marker },
            session.Layers.Layers.OfType<DrawMarkLayer>().Select(mark => mark.Kind).ToArray());
        CollectionAssert.AreEqual(new[] { HideKind.Blur, HideKind.Black },
            session.Layers.Layers.OfType<HideLayer>().Select(hide => hide.Kind).ToArray());
        return session.Layers;
    }

    private static async Task AssertSaved(ListedStash listed, StashItem item, LayerList layers, byte[] originalHash)
    {
        Assert.IsTrue(item.Edited);
        Assert.AreEqual(listed.Stash.Folder.InRoot(item.Name + ".png"), item.FilePath,
            "the listing points at the root render under the item's name");
        var edit = await listed.Stash.Store!.ForEdit(item.Id);
        Assert.IsTrue(edit.Succeeded, edit.Failure?.Sentence);
        Assert.IsNotNull(edit.Value);
        Assert.AreEqual(listed.Stash.Folder.InOriginals(item.Name + ".png"), edit.Value.OriginalPath);
        CollectionAssert.AreEqual(originalHash, SHA256.HashData(File.ReadAllBytes(edit.Value.OriginalPath)),
            "the original's SHA-256 is unchanged");
        Assert.AreEqual(layers, edit.Value.Layers, "the complete live list, including font, colors, turn and geometry");
        AssertRoot(listed);
        var previewLayers = await PreviewFacts.EditedLayers(listed.Stash.Store, item);
        Assert.IsTrue(previewLayers.Succeeded, previewLayers.Failure?.Sentence);
        var uses = await listed.Stash.Store.UseHistory(item.Id);
        Assert.IsTrue(uses.Succeeded, uses.Failure?.Sentence);
        var facts = PreviewFacts.Lines(item, PreviewFacts.FileLength(item.FilePath), uses.Value!, previewLayers.Value!,
            listed.Stash.Clock.GetUtcNow(), listed.Stash.Clock.LocalTimeZone);
        Assert.AreEqual("Edited here: text, draw mark, hide · the original is kept", facts[2]);
    }

    private static void AssertEveryLayerRendered(SKBitmap original, SKBitmap saved)
    {
        Assert.IsNotNull(saved, "SkiaSharp directly decodes the root render");
        Assert.AreEqual((Size.Width, Size.Height), (saved.Width, saved.Height));
        var blackPixels = Pixels(Black).Select(p => saved.GetPixel(p.X, p.Y)).ToArray();
        Assert.IsTrue(blackPixels.All(pixel => pixel == new SKColor(0, 0, 0, 255)),
            $"black box: required one opaque black color; observed {string.Join(", ", blackPixels.Distinct())}");
        Assert.IsTrue(Pixels(Black).All(p => saved.GetPixel(p.X, p.Y) != original.GetPixel(p.X, p.Y)),
            "no pixel in the black box retains the original's patterned pixels");
        Assert.IsTrue(Pixels(Blur).Any(p => saved.GetPixel(p.X, p.Y) != original.GetPixel(p.X, p.Y)),
            "the blurred patterned region changes in the actual saved file");
        var letters = Pixels(new PixelRect(0, 0, 640, 240)).Select(p => saved.GetPixel(p.X, p.Y)).ToArray();
        CollectionAssert.Contains(letters, new SKColor(Fill.R, Fill.G, Fill.B, Fill.A), "the chosen text fill is rendered");
        CollectionAssert.Contains(letters, new SKColor(Edge.R, Edge.G, Edge.B, Edge.A), "the chosen text outline is rendered");
        Assert.IsTrue(Pixels(new PixelRect(45, 290, 140, 65)).Any(p => saved.GetPixel(p.X, p.Y) == new SKColor(255, 0, 0)),
            "the arrow is rendered in its separate region");
        Assert.AreEqual(new SKColor(0, 0, 255), saved.GetPixel(240, 325), "the outlined box's edge is rendered");
        Assert.AreEqual((byte)0, saved.GetPixel(285, 325).Alpha, "the box stays outlined, with its transparent middle");
        var marker = saved.GetPixel(450, 330);
        Assert.IsTrue(marker.Alpha > 0 && marker.Alpha < 255 && marker.Green == 255,
            $"the marker renders a translucent green stroke; observed {marker}");
        AssertUntouchedTransparency(original, saved);
    }

    private static async Task AssertHandoff(ListedStash listed, StashItem item, SKBitmap render)
    {
        var firstEntry = ItemMenu.Entries([item], null)[0];
        Assert.AreEqual((ItemMenuAction.Copy, new KeyGesture(Key.Enter)), (firstEntry.Action, firstEntry.Gesture));
        // HandOver is Enter's path; no target means Copy, with only the recording seam receiving it.
        Require(await listed.Handoff.HandOver(null!, [item], null, () => Assert.Fail("Copy must leave the flyout open")));
        var content = listed.Clipboard.Written.Single().Content;
        CollectionAssert.AreEqual(new[] { item.FilePath }, content.FilePaths.ToArray(), "handoff uses the root render");
        CollectionAssert.AreEqual(new[] { item.Id }, content.Items.ToArray());
        Assert.IsNotNull(content.Png);
        CollectionAssert.AreEqual(File.ReadAllBytes(item.FilePath), content.Png, "the render's own PNG bytes");
        using var png = SKBitmap.Decode(content.Png);
        Assert.IsNotNull(png);
        Assert.IsNotNull(content.Bitmap);
        Assert.AreEqual((render.Width, render.Height), (content.Bitmap.Width, content.Bitmap.Height));
        // MediaFrame's boundary is straight-alpha BGRA; Skia's default decode is premultiplied.
        using var straight = SKBitmap.Decode(content.Png,
            new SKImageInfo(render.Width, render.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        CollectionAssert.AreEqual(straight.Bytes, content.Bitmap.Pixels, "the bitmap carries every rendered pixel and its alpha");
        using var original = SKBitmap.Decode(TransparentPng());
        AssertUntouchedTransparency(original, png);
        Assert.AreEqual(0, MediaFixtures.Alpha(content.Bitmap, 20, 600));
        Assert.AreEqual(128, MediaFixtures.Alpha(content.Bitmap, 600, 600));
    }

    private static async Task<LayerList> ReopenSelectEveryLayerAndChangeText(ListedStash listed, StashItem first, LayerList layers)
    {
        Require(await listed.Editing.Open(first, EditOrigin.Grid));
        var session = listed.Editing.Session!;
        Assert.AreEqual(layers, session.Layers, "all six saved layers reopen live");
        var text = (TextLayer)layers.Layers[0];
        PointF[] clicks = [Centre(text.Box), new(110.5f, 320.5f), new(240, 325), new(450.5f, 330.5f), new(120, 480), new(360, 480)];
        for (var i = 0; i < clicks.Length; i++)
        {
            session.Deselect();
            session.Click(clicks[i].X, clicks[i].Y);
            Assert.AreEqual(i, session.Selected, $"a click selects saved layer {i}: {layers.Layers[i]}");
            Assert.AreEqual(layers.Layers[i], session.SelectedLayer);
            Assert.AreEqual(i == 0 ? "Text" : i < 4 ? "Draw" : "Hide", session.Tool.Name);
        }

        session.Deselect();
        session.DoubleClick(Centre(text.Box));
        Assert.IsTrue(session.Typing, "double-click the saved text to edit its live field");
        session.SetTypedText("WORLD");
        session.EndTyping();
        var changed = session.Layers;
        Assert.AreEqual("WORLD", ((TextLayer)changed.Layers[0]).Text);
        CollectionAssert.AreEqual(layers.Layers.Skip(1).ToArray(), changed.Layers.Skip(1).ToArray(), "all other layers stay live and unchanged");
        Require(await listed.Editing.Save());
        Assert.IsFalse(listed.Editing.IsOpen);
        return changed;
    }

    private static async Task<StashItem> CopyFromMenu(ListedStash listed, ItemMenu menu, StashItem first,
        LayerList layers, byte[] originalHash, byte[] render)
    {
        var before = EditedItems.Row(listed.Stash, first.Id);
        var entry = ItemMenu.Entries([first], null).Single(e => e.Action == ItemMenuAction.SaveAsNewItem);
        Assert.AreEqual("Save as new item", entry.Label);
        Require(await menu.Act(entry.Action, null!, [first], null, () => Assert.Fail("no handoff"), _ => Assert.Fail("no dialog")));
        Assert.HasCount(2, listed.Listing.Items);
        var second = listed.Listing.Items.Single(item => item.Id != first.Id);
        Assert.AreEqual(first.Name + " 2", second.Name);
        CollectionAssert.AreEqual(new[] { "funny", "reaction" }, second.Tags.Order().ToArray());
        Assert.IsTrue(first.Favorite, "the source is favorite, so not copying that flag is observable");
        Assert.IsFalse(second.Favorite);
        var unchanged = listed.Listing.Items.Single(item => item.Id == first.Id);
        Assert.AreEqual((first.Name, first.Favorite, first.AddedAt, first.FileName, first.FilePath, first.Facts, first.RenderedRevision, first.Edited),
            (unchanged.Name, unchanged.Favorite, unchanged.AddedAt, unchanged.FileName, unchanged.FilePath, unchanged.Facts, unchanged.RenderedRevision, unchanged.Edited),
            "the first item's listed metadata stays unchanged");
        CollectionAssert.AreEqual(first.Tags.ToArray(), unchanged.Tags.ToArray(), "the first item's tags stay unchanged");
        Assert.AreEqual(before, EditedItems.Row(listed.Stash, first.Id), "the first item's entire edit record stays unchanged");
        CollectionAssert.AreEqual(render, File.ReadAllBytes(first.FilePath), "the first render is unchanged");
        await AssertSaved(listed, unchanged, layers, originalHash);
        Assert.AreEqual(second.Id, listed.Editing.Session?.Item.Id, "editing continues on the second item");
        Assert.AreEqual(layers, listed.Editing.Session!.Layers);
        await AssertSaved(listed, second, layers, originalHash);
        using var sourceRender = SKBitmap.Decode(render);
        using var copyRender = SKBitmap.Decode(second.FilePath);
        CollectionAssert.AreEqual(sourceRender.Bytes, copyRender.Bytes, "the copy renders all the same layers");
        return second;
    }

    private static async Task RevertFromMenuAndUndo(ListedStash listed, ItemMenu menu, StashItem first, StashItem second,
        LayerList layers, byte[] original, byte[] originalHash, byte[] render)
    {
        var entry = ItemMenu.Entries([first], null).Single(e => e.Action == ItemMenuAction.RevertToOriginal);
        Assert.AreEqual("Revert to original", entry.Label);
        Require(await menu.Act(entry.Action, null!, [first], null, () => Assert.Fail("no handoff"), _ => Assert.Fail("no dialog")));
        Assert.IsFalse(listed.Editing.IsOpen, "a menu Revert acts immediately, opening no edit");
        var reverted = listed.Listing.Items.Single(item => item.Id == first.Id);
        Assert.IsFalse(reverted.Edited);
        Assert.AreEqual(listed.Stash.Folder.InRoot(first.Name + ".png"), reverted.FilePath);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(reverted.FilePath), "the original bytes are restored under the item's name");
        Assert.IsTrue((await listed.Stash.Store!.ForEdit(first.Id)).Value!.Layers.IsEmpty);
        CollectionAssert.AreEqual(new[] { second.Name + ".png" }, Directory.GetFiles(listed.Stash.Folder.Originals).Select(Path.GetFileName).ToArray(),
            "originals holds only the second item's original, nothing of the reverted first");
        AssertRoot(listed);
        var notice = listed.Notices.Shown.Single();
        Assert.AreEqual($"Reverted “{first.Name}” to its original.", notice.Text);
        Assert.AreEqual("Undo", notice.UndoLabel);
        await listed.Notices.Undo(notice);

        var restored = listed.Listing.Items.Single(item => item.Id == first.Id);
        await AssertSaved(listed, restored, layers, originalHash);
        using var before = SKBitmap.Decode(render);
        using var after = SKBitmap.Decode(restored.FilePath);
        CollectionAssert.AreEqual(before.Bytes, after.Bytes, "the notice's Undo restores every layer's render");
        CollectionAssert.AreEquivalent(new[] { first.Name + ".png", second.Name + ".png" },
            Directory.GetFiles(listed.Stash.Folder.Originals).Select(Path.GetFileName).ToArray(),
            "Undo keeps the originals of both edited items");
    }

    private static void AssertTextRenderChanged(byte[] firstRender, byte[] secondRender)
    {
        using var before = SKBitmap.Decode(firstRender);
        using var after = SKBitmap.Decode(secondRender);
        Assert.IsTrue(Pixels(new PixelRect(0, 0, 640, 240)).Any(p => before.GetPixel(p.X, p.Y) != after.GetPixel(p.X, p.Y)),
            "changing the saved text changes the text's pixels in the root render");
        Assert.IsTrue(Pixels(new PixelRect(0, 240, 640, 400)).All(p => before.GetPixel(p.X, p.Y) == after.GetPixel(p.X, p.Y)),
            "the other layers and untouched pixels stay the same");
    }

    private static void AssertRoot(ListedStash listed) =>
        CollectionAssert.AreEquivalent(listed.Listing.Items.Select(item => item.FileName).Append(StashFolder.StoreFileName).ToArray(),
            Directory.GetFiles(listed.Stash.Folder.Root).Select(Path.GetFileName).ToArray(), "exactly one root picture per item plus the store");

    private static void AssertUntouchedTransparency(SKBitmap original, SKBitmap saved)
    {
        Assert.IsTrue(Pixels(new PixelRect(0, 560, 640, 80)).All(p => saved.GetPixel(p.X, p.Y) == original.GetPixel(p.X, p.Y)),
            "outside every layer the transparent strip and half-transparent patch retain every pixel");
        Assert.AreEqual((byte)0, saved.GetPixel(20, 600).Alpha);
        Assert.AreEqual((byte)128, saved.GetPixel(600, 600).Alpha);
    }

    private static byte[] TransparentPng()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Size.Width, Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        foreach (var region in new[] { Blur, Black })
        {
            foreach (var p in Pixels(region))
            {
                bitmap.SetPixel(p.X, p.Y, (p.X + p.Y) % 2 == 0 ? new SKColor(240, 60, 30) : new SKColor(30, 210, 240));
            }
        }

        foreach (var p in Pixels(new PixelRect(580, 580, 40, 40)))
        {
            bitmap.SetPixel(p.X, p.Y, new SKColor(0, 0, 255, 128));
        }

        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static IEnumerable<PixelPoint> Pixels(PixelRect rect)
    {
        for (var y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (var x = rect.X; x < rect.X + rect.Width; x++)
            {
                yield return new PixelPoint(x, y);
            }
        }
    }

    private static PointF Centre(PixelRect rect) => new(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);

    private static void Require(Outcome outcome) => Assert.IsTrue(outcome.Succeeded, outcome.Failure?.Sentence);
}
