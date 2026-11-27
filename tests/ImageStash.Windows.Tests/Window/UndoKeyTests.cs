using Avalonia.Input;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// <c>Ctrl+Z</c> on the real window (G7): after Del in the grid or in the preview it brings the item back, from the grid
/// and from the preview open on another item; in the search box it undoes the typed text and leaves the notice. These
/// checks show the stash window and delete pictures of their own into the Recycle Bin, whose entries the Undo removes.
/// </summary>
[TestClass]
public sealed class UndoKeyTests
{
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task DelThenCtrlZInTheGridBringsTheItemBack()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("undo key one.png"), shown.Picture("undo key kept.png"));
        TrayDesktop.Run(() =>
        {
            shown.Notices.Close(shown.Notices.Shown.Single());
            shown.Listing.Focus(items[0].Id);
            shown.Grid.Focus();
            Tap(shown.Grid, Key.Delete);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 1 && shown.Notices.Shown.Count == 1));

        TrayDesktop.Run(() => Tap(shown.Grid, Key.Z, KeyModifiers.Control));

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 2 && shown.Notices.Shown.Count == 0));
        Assert.IsTrue(File.Exists(items[0].FilePath));
        Assert.IsEmpty(RecycleBinEntries.Of(items[0].FilePath), "its entry removed with the Undo (G18)");
    }

    [TestMethod]
    public async Task DelInThePreviewThenCtrlZWhereFocusLandsBringsTheItemBack()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("undo key preview.png"), shown.Picture("undo key other.png"));
        TrayDesktop.Run(() =>
        {
            shown.Notices.Close(shown.Notices.Shown.Single());
            shown.OpenPreview(items[0]);
            Tap(shown.PreviewPicture, Key.Delete);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Preview.Item?.Id == items[1].Id && shown.Notices.Shown.Count == 1));

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Preview.IsOpen, "the preview shows the item that took the deleted one's place (G19)");
            Tap(shown.PreviewPicture, Key.Z, KeyModifiers.Control);
        });

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 2 && shown.Notices.Shown.Count == 0));
    }

    [TestMethod]
    public async Task CtrlZInThePreviewOpenOnAnotherItemBringsBackTheItemDeletedFromTheGrid()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("undo key gone.png"), shown.Picture("undo key shown.png"));
        TrayDesktop.Run(() =>
        {
            shown.Notices.Close(shown.Notices.Shown.Single());
            shown.Listing.Focus(items[0].Id);
            shown.Grid.Focus();
            Tap(shown.Grid, Key.Delete);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 1 && shown.Notices.Shown.Count == 1));

        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(items[1]);
            Tap(shown.PreviewPicture, Key.Z, KeyModifiers.Control);
        });

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 2 && shown.Notices.Shown.Count == 0));
        TrayDesktop.Run(() => Assert.IsTrue(shown.Preview.IsOpen, "the preview stays on its item"));
    }

    [TestMethod]
    public async Task CtrlZInTheSearchBoxUndoesTheTypedTextAndLeavesTheNotice()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("undo key boxed.png"), shown.Picture("undo key left.png"));
        TrayDesktop.Run(() =>
        {
            shown.Notices.Close(shown.Notices.Shown.Single());
            shown.Listing.Focus(items[0].Id);
            shown.Grid.Focus();
            Tap(shown.Grid, Key.Delete);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 1 && shown.Notices.Shown.Count == 1));

        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            Type(shown.Search, "zzz");
            Assert.AreEqual("zzz", shown.Search.Text, "typed into the box");
            Tap(shown.Search, Key.Z, KeyModifiers.Control);
        });
        await Task.Delay(500);

        TrayDesktop.Run(() =>
        {
            Assert.AreNotEqual("zzz", shown.Search.Text ?? "", "the box's own Ctrl+Z undid its typing (G7)");
            Assert.HasCount(1, shown.Notices.Shown, "the Delete's notice still there");
        });
        Assert.IsFalse(File.Exists(items[0].FilePath), "and the item still deleted");
    }
}
