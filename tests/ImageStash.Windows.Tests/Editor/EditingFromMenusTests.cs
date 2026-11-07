using Avalonia.Input;
using ImageStash.Store;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// An edited item's menus and preview on the real desktop (G19, G26, G20): the item menu and the preview's ⋯ offering
/// Revert to original and Save as new item on an edited item only; Revert acting at once with a notice whose Undo brings
/// the layers back; the preview saying "Edited here" with its layer kinds and losing it on Revert, staying open; Save as
/// new item from the ⋯ opening the editor on the new item with the preview beneath moved to it, and Save returning there.
/// </summary>
[TestClass]
public sealed class EditingFromMenusTests
{
    [TestMethod]
    public async Task TheItemMenuOffersRevertAndSaveAsNewItemOnAnEditedItemAndRevertActsAtOnceWithItsUndo()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("checker.png", 64, 48, EditorCanvasTests.Checker))).Single();
        TrayDesktop.Run(() =>
        {
            var unedited = shown.RightClick(item.Id);
            Assert.IsFalse(unedited.Contains("Revert to original") || unedited.Contains("Save as new item"), string.Join(", ", unedited));
            shown.CloseMenu();
        });
        await SaveABlackBox(shown, editor, item);

        TrayDesktop.Run(() =>
        {
            var edited = shown.RightClick(item.Id);
            CollectionAssert.AreEqual(new[] { "Copy", "Preview", "Edit", "Revert to original", "Save as new item", "Add to Favorites" }, edited.Take(6).ToList(), "G19, G26");
            shown.ClickMenuEntry("Revert to original");
            shown.CloseMenu();
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Said() == "Reverted “checker” to its original."));

        var folder = TrayDesktop.Get(() => shown.Listing.Store!.Folder);
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Editing.IsOpen, "from a menu nothing opens (G26)");
            Assert.IsFalse(shown.Listing.Items.Single().Edited);
        });
        Assert.IsEmpty(Directory.GetFiles(folder.InOriginals("")), "the original back in the root");

        await TrayDesktop.Get(() => shown.Notices.Undo(shown.Notices.Shown.Single(notice => notice.Text.StartsWith("Reverted", StringComparison.Ordinal))));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Single().Edited));
        Assert.IsTrue(File.Exists(folder.InOriginals("checker.png")), "Undo saved the layers again, the original kept in originals\\");
    }

    [TestMethod]
    public async Task ThePreviewOfAnEditedItemSaysEditedHereRevertsInPlaceAndSavesAsANewItemBeneathTheEditor()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("checker.png", 64, 48, EditorCanvasTests.Checker))).Single();
        await SaveABlackBox(shown, editor, item);
        TrayDesktop.Run(() => shown.OpenPreview(shown.Listing.Items.Single()));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count == 3));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("Edited here: hide · the original is kept", shown.Preview.Facts[2], "G19: its layer kinds");
            CollectionAssert.AreEqual(new[] { "Edit", "Rename", "—", "Revert to original", "Save as new item", "—", "Save a copy…", "Open with…", "Reveal in File Explorer", "—", "Delete" }, shown.ClickMore());
            shown.ClickMenuEntry("Revert to original");
            shown.CloseMenu();
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count == 2));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Preview.IsOpen, "the preview stays open on the item");
            Assert.AreEqual(item.Id, shown.Preview.Item?.Id);
            CollectionAssert.AreEqual(new[] { "Edit", "Rename", "—", "Save a copy…", "Open with…", "Reveal in File Explorer", "—", "Delete" }, shown.ClickMore(), "an unedited item's ⋯");
            shown.CloseMenu();
        });

        await TrayDesktop.Get(() => shown.Notices.Undo(shown.Notices.Shown.Single(notice => notice.Text.StartsWith("Reverted", StringComparison.Ordinal))));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count == 3));
        TrayDesktop.Run(() =>
        {
            shown.ClickMore();
            shown.ClickMenuEntry("Save as new item");
            shown.CloseMenu();
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.Session?.Item.Name == "checker 2" && shown.Preview.Item?.Name == "checker 2"));

        var copy = TrayDesktop.Get(() => shown.Editing.Session!.Item);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Preview.Covered, "the preview beneath the editor, moved to the new item (G26)");
            Assert.HasCount(1, shown.Editing.Session!.Layers.Layers, "the editor on the new item with its layer live");
            editor.Choose("Save");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen && !shown.Preview.Covered && shown.Preview.Facts.Count == 3));
        TrayDesktop.Run(() => Assert.AreEqual(copy.Id, shown.Preview.Item?.Id, "Save returns to the preview, on the new item"));
    }

    /// <summary>The editor on the item from the grid, a black box over its middle, saved, and the window back on the grid.</summary>
    private static async Task SaveABlackBox(ShownWindow shown, ShownEditor editor, StashItem item)
    {
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Choose("Hide");
            editor.Choose("Black box");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
            ShownWindow.Press(editor.Canvas, Key.S, KeyModifiers.Control);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen && shown.Grid.IsFocused));
        await TrayDesktop.Get(() => shown.Listing.Settle());
    }
}
