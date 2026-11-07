using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using PixelRect = ImageStash.Layers.PixelRect;
using Rect = Avalonia.Rect;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// Editing in the stash window on the real desktop (G26, G24, G20, G1, G2, G3, G11, G12, R9): Save inside a busy state
/// naming the item, back to the grid or the preview Edit was started from, the tile read again and no edit marker; the
/// editor's ⋯ menu; the window's rules with an edit open (the flyout stays, the shortcut brings it forward, a summon
/// and a capture bring the edit back as left); a drop beneath the editor; one edit at a time.
/// </summary>
[TestClass]
public sealed class EditingInTheWindowTests
{
    [TestMethod]
    public async Task SaveRunsInABusyStateNamingTheItemAndReturnsToTheGridWithItsTileReadAgainAndNoMarker()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var fixture = shown.Fixture("transparent.png", "transparent.png");
        var item = (await shown.OpenStash(fixture)).Single();
        // No tile selected, so no ring tells one tile from another.
        TrayDesktop.Run(() => shown.Listing.Select([], null));
        var before = await RenderedWindow.Of(shown);
        var tileBefore = TrayDesktop.Get(() => Tile(shown, item.Id));
        await EditorOverlayTests.Open(shown, item);
        var busy = new List<string?>();
        TrayDesktop.Run(() =>
        {
            // A hide and a box, each dragged farther than a click on a picture shown at its own eight by six pixels.
            editor.Choose("Hide");
            editor.Choose("Black box");
            editor.Drag((0.5 / 8, 0.5 / 6), (5.5 / 8, 5.5 / 6));
            ShownWindow.Press(editor.Canvas, Key.Escape);
            editor.Choose("Draw");
            editor.Choose("Box");
            editor.Drag((7.5 / 8, 0.5 / 6), (2.5 / 8, 5.5 / 6));
            Assert.AreEqual(new PixelRect(0, 0, 6, 6), ((HideLayer)editor.Session!.Layers.Layers[0]).Rect);
            Assert.AreEqual(new PixelRect(2, 0, 6, 6), ((DrawMarkLayer)editor.Session.Layers.Layers[1]).Rect);
            shown.Editing.Changed += () => busy.Add(shown.Editing.Busy);
            ShownWindow.Press(editor.Canvas, Key.S, KeyModifiers.Control);
            // Pressed again while it runs, by key and by button: nothing more (G35).
            ShownWindow.Press(editor.Canvas, Key.S, KeyModifiers.Control);
            editor.Choose("Save");
        });
        // The edit closes once the store has published it; the return follows once the listing is read again.
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen && shown.Grid.IsFocused));
        await TrayDesktop.Get(() => shown.Listing.Settle());

        var stash = TrayDesktop.Get(() => shown.Listing.Store!.Folder);
        TrayDesktop.Run(() =>
        {
            CollectionAssert.Contains(busy, "Saving “transparent”…", "G35: the busy state names the Save while it runs");
            Assert.IsTrue(shown.Grid.IsFocused, "back to the grid Edit was started from (G26)");
            CollectionAssert.AreEqual(new[] { item.Id }, shown.Listing.Selection.ToList(), "with the item selected");
            Assert.AreEqual(1, shown.Listing.Items.Single().RenderedRevision, "saved once, published before the edit closed (R9)");
        });
        CollectionAssert.AreEqual(File.ReadAllBytes(fixture), File.ReadAllBytes(stash.InOriginals("transparent.png")), "the original kept, byte for byte (G29)");
        CollectionAssert.AreNotEqual(SHA256.HashData(File.ReadAllBytes(fixture)), SHA256.HashData(File.ReadAllBytes(stash.InRoot("transparent.png"))), "the finished picture rendered in its place");

        // The tile is read again, and is the finished picture's tile and nothing more: an item imported from that
        // finished picture, which never had layers, shows the same pixels in its tile.
        var copy = Path.Combine(Path.GetTempPath(), "imagestash-finished-" + Guid.NewGuid().ToString("N") + ".png");
        File.Copy(stash.InRoot("transparent.png"), copy);
        try
        {
            var plain = await Dispatcher.UIThread.InvokeAsync(async () => (await shown.Import.Drop(ListedDrop(copy))).Single().Item!);
            TrayDesktop.Run(() => shown.Listing.Select([], null));
            await Task.Delay(400);
            var after = await RenderedWindow.Of(shown);
            var (edited, unedited) = TrayDesktop.Get(() => (Tile(shown, item.Id), Tile(shown, plain.Id)));
            Assert.AreEqual((edited.Width, edited.Height), (unedited.Width, unedited.Height));
            Assert.IsGreaterThan(0, Differences(before, tileBefore, after, edited), "the tile read again: the black box shows");
            Assert.AreEqual(0, Differences(after, edited, after, unedited), "G26: the grid shows no edit marker");
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [TestMethod]
    public async Task SaveFromThePreviewReturnsToThePreviewWithItsFactsReadAgain()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        // Noise, which a black box over most of it makes a much smaller file, so the facts' size changes in words.
        var item = (await shown.OpenStash(shown.Picture("noise.png", 200, 150, (x, y) => new SKColor((byte)(x * 2654435761u >> 13 ^ y * 40503u), (byte)(y * 2654435761u >> 11 ^ x * 97u), (byte)((x + 7) * (y + 3) * 2246822519u >> 17))))).Single();
        TrayDesktop.Run(() => shown.OpenPreview(item));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count > 0));
        var facts = TrayDesktop.Get(() => shown.Preview.Facts[0]);
        TrayDesktop.Run(() => ShownWindow.RaiseClick(shown.EditButton));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.IsOpen));

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Preview.IsOpen && shown.Preview.Covered, "the preview stays beneath, covered");
            editor.Choose("Hide");
            editor.Choose("Black box");
            editor.Drag((0.1, 0.1), (0.9, 0.9));
            editor.Choose("Save");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count > 0 && shown.Preview.Facts[0] != facts));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(item.Id, shown.Preview.Item?.Id, "the preview shown again on the item (G26)");
            Assert.IsFalse(shown.Preview.Covered);
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "with focus in it");
        });
    }

    [TestMethod]
    public async Task TheMoreMenuOffersRevertOnlyOnAnEditedItemAndRevertDiscardAndSaveAsNewItemDoWhatTheySay()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("checker.png", 64, 48, EditorCanvasTests.Checker))).Single();
        await shown.Tag([item], "funny");
        var original = await RenderedWindowWhileEditing(shown, item);
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "Save as new item", "—", "Discard changes" }, editor.ClickMore(), "no Revert on an unedited item (G20)");
            editor.Choose("Hide");
            editor.Choose("Black box");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
            ShownWindow.Press(editor.Canvas, Key.S, KeyModifiers.Control);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));

        // On the saved item: Revert to original empties the list, the item unchanged until Save, and Ctrl+Z brings it back.
        await EditorOverlayTests.Open(shown, item);
        var saved = TrayDesktop.Get(() => editor.Session!.Layers);
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "Save as new item", "Revert to original", "—", "Discard changes" }, editor.ClickMore());
            editor.ClickMenuEntry("Revert to original");
            Assert.IsTrue(editor.Session!.Layers.IsEmpty);
        });
        var reverted = await RenderedWindow.Of(shown);
        var picture = TrayDesktop.Get(editor.PictureInRendering);
        Assert.AreEqual(0, EditorCanvasTests.Differences(original, reverted, picture), "the canvas shows the original alone");
        Assert.AreEqual(saved, (await TrayDesktop.Get(() => shown.Listing.Store!.ForEdit(item.Id))).Value!.Layers, "the item as it was until Save (G26)");
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(editor.Canvas, Key.Z, KeyModifiers.Control);
            Assert.AreEqual(saved, editor.Session!.Layers, "Ctrl+Z brings the layers back");
            editor.ClickMore();
            editor.ClickMenuEntry("Discard changes");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));
        Assert.Contains("Discarded the edit of “checker”.", TrayDesktop.Get(shown.Said), "Discard changes leaves it as Cancel does");

        // Save as new item from an edit started in the preview: the new item beneath, the editor on it, Save back to it.
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(item);
            ShownWindow.RaiseClick(shown.EditButton);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.IsOpen));
        TrayDesktop.Run(() =>
        {
            editor.Choose("Draw");
            editor.Drag((0.1, 0.9), (0.9, 0.9));
            editor.ClickMore();
            editor.ClickMenuEntry("Save as new item");
        });
        // The session takes the new item before the listing refreshes, and the preview follows only after that refresh.
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.Session?.Item.Name == "checker 2" && shown.Preview.Item?.Name == "checker 2"));
        var (made, live) = TrayDesktop.Get(() => (shown.Editing.Session!.Item, shown.Editing.Session.Layers));
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(editor.Session!.CanUndo, "the new item's edit starts with no history");
            Assert.AreEqual(made.Id, shown.Preview.Item?.Id, "the preview beneath moved to the new item");
            Assert.IsFalse(made.Favorite);
            Assert.IsTrue(editor.Overlay.IsEffectivelyVisible, "the editor open on it");
        });
        Assert.AreEqual(live, (await TrayDesktop.Get(() => shown.Listing.Store!.ForEdit(made.Id))).Value!.Layers, "with the session's layers");
        CollectionAssert.AreEqual(new[] { "funny" }, shown.TagsOf(made.Id), "and the same tags (G26)");
        Assert.AreEqual(saved, (await TrayDesktop.Get(() => shown.Listing.Store!.ForEdit(item.Id))).Value!.Layers, "the item edited until now as it was");
        TrayDesktop.Run(() => editor.Choose("Save"));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));
        TrayDesktop.Run(() => Assert.AreEqual(made.Id, shown.Preview.Item?.Id, "Save returns to the preview, on the new item"));
    }

    [TestMethod]
    public async Task WithAnEditOpenTheFlyoutStaysTheShortcutBringsItForwardAndASummonOrACaptureBringsTheEditBackAsLeft()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("checker.png", 64, 48, EditorCanvasTests.Checker))).Single();
        await EditorOverlayTests.Open(shown, item);
        var (session, layers) = TrayDesktop.Get(() =>
        {
            editor.Choose("Hide");
            editor.Choose("Black box");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
            return (editor.Session!, editor.Session!.Layers);
        });

        TrayDesktop.Run(shown.Deactivate);
        await Task.Delay(300);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible, "G2: losing focus to another app leaves the flyout while an edit is open"));

        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(new ForegroundReading(false, null), new SummonAnchor(null)));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.IsActive));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "G2: the shortcut brings it forward instead of hiding it");
            AssertAsLeft(shown, editor, session, layers);
        });

        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromTray();
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());
        TrayDesktop.Run(() => AssertAsLeft(shown, editor, session, layers));

        TrayDesktop.Run(() => shown.Window.CaptureHiding.HideForCapture());
        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible, "G3: hidden for a capture"));
        TrayDesktop.Run(() => shown.Window.CaptureHiding.ShowAfterCapture());
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "and back after it");
            Assert.AreSame(session, shown.Editing.Session, "the edit kept through it (G3)");
            Assert.AreEqual(layers, session.Layers);
            Assert.IsTrue(session.CanUndo);
        });
    }

    [TestMethod]
    public async Task AFileDroppedOnTheWindowUnderAnEditOpenedFromThePreviewIsAddedBeneathAndSaveStillReturnsToThatPreview()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("checker.png", 64, 48, EditorCanvasTests.Checker))).Single();
        var dropped = shown.Picture("dropped.png");
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(item);
            ShownWindow.RaiseClick(shown.EditButton);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.IsOpen));
        var data = await shown.Files(dropped);

        TrayDesktop.Run(() =>
        {
            ShownWindow.RaiseDrag(editor.Canvas, DragDrop.DragEnterEvent, data);
            ShownWindow.RaiseDrag(editor.Canvas, DragDrop.DragOverEvent, data);
            ShownWindow.RaiseDrag(editor.Canvas, DragDrop.DropEvent, data);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Any(each => each.Name == "dropped")));
        await TrayDesktop.Get(() => shown.Listing.Settle());

        TrayDesktop.Run(() =>
        {
            var added = shown.Listing.Items.Single(each => each.Name == "dropped");
            CollectionAssert.AreEqual(new[] { added.Id }, shown.Listing.Selection.ToList(), "G11, G12: added and selected beneath the editor");
            Assert.IsTrue(shown.Editing.IsOpen, "the edit stays open");
            Assert.AreEqual(item.Id, shown.Preview.Item?.Id, "the preview beneath still on the edited item");
            editor.Choose("Save");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Preview.IsOpen, "Save returns to the preview Edit was started from (G26)");
            Assert.AreEqual(item.Id, shown.Preview.Item?.Id);
        });
    }

    [TestMethod]
    public async Task AnEditStartedInTheGridReturnsToTheGridThoughItsUndoReopenedItOverAnotherItemsPreview()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("first.png"), shown.Picture("second.png"));
        var (first, second) = (items.Single(each => each.Name == "first"), items.Single(each => each.Name == "second"));
        await EditorOverlayTests.Open(shown, first);
        await EditorOverlayTests.Cancelled(shown);
        var notice = TrayDesktop.Get(() => shown.Notices.Shown.Single(each => each.Text == "Discarded the edit of “first”."));
        TrayDesktop.Run(() => shown.OpenPreview(second));

        await TrayDesktop.Get(() => shown.Notices.Undo(notice));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.Session?.Item.Id == first.Id));
        TrayDesktop.Run(() => editor.Choose("Save"));
        // The edit closes once the store has published it; the return follows once the listing is read again.
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen && shown.Grid.IsFocused));

        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Preview.IsOpen, "G26: back to the grid Edit was started from, not to the other item's preview");
            Assert.IsTrue(shown.Grid.IsFocused);
            CollectionAssert.AreEqual(new[] { first.Id }, shown.Listing.Selection.ToList());
        });
    }

    [TestMethod]
    public async Task AFlyoutAnEditKeptUpAfterFocusWentToAnotherAppHidesWhenTheEditEnds()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("kept.png"))).Single();
        await EditorOverlayTests.Open(shown, item);

        TrayDesktop.Run(shown.Deactivate);
        await Task.Delay(300);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible, "kept up while the edit is open (G2)"));
        TrayDesktop.Run(() => shown.Editing.Cancel());

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Window.IsVisible));
    }

    [TestMethod]
    public async Task OpeningTheEditorOnAnotherItemFirstLeavesTheOpenEditAsCancelWould()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("first.png"), shown.Picture("second.png"));
        var (first, second) = (items.Single(each => each.Name == "first"), items.Single(each => each.Name == "second"));
        await EditorOverlayTests.Open(shown, first);
        var earlier = TrayDesktop.Get(() => shown.Editing.Session!);

        await TrayDesktop.Get(() => shown.Editing.Open(second, EditOrigin.Grid));

        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(earlier.IsOpen, "one edit at a time (Approach)");
            Assert.AreEqual(second.Id, shown.Editing.Session!.Item.Id);
            Assert.AreSame(shown.Editing.Session, editor.Overlay.Session, "the editor shows the other item");
            Assert.Contains("Discarded the edit of “first”.", shown.Said(), "the first left as Cancel would, with its notice (G20)");
        });
    }

    private static void AssertAsLeft(ShownWindow shown, ShownEditor editor, EditorSession session, LayerList layers)
    {
        Assert.AreSame(session, shown.Editing.Session, "G1: the edit as it was left");
        Assert.AreEqual(layers, session.Layers);
        Assert.IsTrue(session.CanUndo, "its history kept");
        Assert.IsTrue(editor.Overlay.IsEffectivelyVisible);
        Assert.IsTrue(shown.Focused is Visual focus && editor.Overlay.IsVisualAncestorOf(focus), "focus inside the editor");
    }

    /// <summary>The window rendered with the editor open on <paramref name="item"/> before anything is drawn.</summary>
    private static async Task<RenderedWindow> RenderedWindowWhileEditing(ShownWindow shown, StashItem item)
    {
        await EditorOverlayTests.Open(shown, item);
        return await RenderedWindow.Of(shown);
    }

    private static DropContents ListedDrop(string path) => new([new DroppedItem(Path.GetFileName(path), path, null, IsFolder: false)], false, false, []);

    /// <summary>Where the item's tile lies in the rendered window, in physical pixels.</summary>
    internal static Rect Tile(ShownWindow shown, long id)
    {
        var tile = shown.Grid.Rows.SelectMany(row => row.Tiles).Single(each => each.Id == id);
        var grid = RenderedWindow.Place(shown.Window, shown.Grid);
        var scaling = shown.Window.RenderScaling;
        return new Rect(grid.X + tile.X * scaling, grid.Y + tile.Y * scaling, tile.Width * scaling, tile.Height * scaling);
    }

    /// <summary>How many pixels of one rendering's area differ from the same-sized area of another's.</summary>
    private static int Differences(RenderedWindow one, Rect inOne, RenderedWindow other, Rect inOther)
    {
        var count = 0;
        for (var y = 0; y < (int)inOne.Height; y++)
        {
            for (var x = 0; x < (int)inOne.Width; x++)
            {
                count += one.At((int)inOne.X + x, (int)inOne.Y + y) == other.At((int)inOther.X + x, (int)inOther.Y + y) ? 0 : 1;
            }
        }

        return count;
    }
}
