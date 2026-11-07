using Avalonia.Controls;
using Avalonia.VisualTree;
using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownSettings;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The Settings page's transfers on the desktop: Move… beside Open folder waits, saying why, with the page disabled, and
/// the stash window then lists from the new folder (G30); Export stash… and Import stash… follow the stash location's
/// row, and the import's offer of a chosen file shows inside the page with no window of its own, its Cancel changing
/// nothing (G32, G36, F4).
/// </summary>
[TestClass]
public sealed class SettingsWindowTransferTests
{
    [TestMethod]
    public async Task MoveBesideOpenFolderWaitsSayingWhyWithThePageDisabledThenTheStashWindowListsFromTheNewFolder()
    {
        using var settings = new SettingsFolder();
        var dialogs = new PresetDialogs();
        var shown = ShownWindow.Open(dialogs: dialogs);
        var chosen = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "imagestash-move-check-" + Guid.NewGuid().ToString("N"))).FullName;
        SettingsWindow? window = null;
        try
        {
            await shown.OpenStash(shown.Picture("move check.png"));
            var old = TrayDesktop.Get(() => shown.Listing.Store!.Folder.Root);
            dialogs.PickedFolder = chosen;
            TransferredStash.CloseNotices(shown);
            // A notice whose Undo still stands, as a Delete's does: the Move waits for it (State: Move).
            var standing = TrayDesktop.Get(() => shown.Notices.Raise("Deleted “move check”.", "Undo", () => Task.FromResult(Outcome.Done)));
            var opened = window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page(moving: shown.Moving)));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => opened.IsVisible));

            TrayDesktop.Run(() =>
            {
                var names = Named(opened);
                Assert.AreEqual(names.IndexOf("Open folder") + 1, names.IndexOf(SettingsPage.MoveWords), "Move… beside Open folder on the stash location's row (G32)");
                ShownWindow.RaiseClick((Button)Control(opened, SettingsPage.MoveWords));
            });
            const string waiting = "Waiting for the Undo of a notice before moving your stash…";
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => opened.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == waiting && text.IsEffectivelyVisible)));
            TrayDesktop.Run(() =>
            {
                Assert.IsFalse(Control(opened, SettingsPage.MoveWords).IsEffectivelyEnabled, "Move… disabled while it is asked");
                Assert.IsFalse(Control(opened, SettingsPage.AlsoCopyTitle).IsEffectivelyEnabled, "and every other control of the page");
                Assert.IsFalse(Control(opened, "Open folder").IsEffectivelyEnabled);
                Assert.AreEqual(old, ((TextBox)Control(opened, SettingsPage.StashFolderTitle)).Text, "the folder the Move started from (R9)");
                shown.Notices.Close(standing);
            });

            await ShownWindow.Eventually(() => TrayDesktop.Get(() => !opened.Page.MoveAsked));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled));
            TrayDesktop.Run(() =>
            {
                var said = string.Join(" | ", shown.Notices.Shown.Select(notice => notice.Text));
                Assert.AreEqual(Path.Combine(chosen, "move check.png"), shown.Listing.Items.Single().FilePath, true, "the stash window lists from the new folder (G30): " + said);
                Assert.IsTrue(Control(opened, SettingsPage.AlsoCopyTitle).IsEffectivelyEnabled, "the page takes changes again");
                Assert.IsTrue(opened.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == $"Moved your stash to {chosen}." && text.IsEffectivelyVisible));
            });
            Assert.IsFalse(Directory.Exists(old), "the old folder removed (G30)");
        }
        finally
        {
            if (window is not null)
            {
                TrayDesktop.Run(window.Close);
            }

            shown.Dispose();
            Directory.Delete(chosen, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExportAndImportFollowTheStashLocationAndTheImportsOfferIsPartOfThePageWithNoWindowOfItsOwn()
    {
        using var settings = new SettingsFolder();
        var shown = ShownWindow.Open();
        SettingsWindow? window = null;
        string? archive = null;
        try
        {
            await shown.OpenStash(shown.Picture("offer check.png", 8, 6, (_, _) => SkiaSharp.SKColors.Red));
            TransferredStash.CloseNotices(shown);
            archive = await TransferredStash.ExportOf(shown, ("one", SkiaSharp.SKColors.Blue), ("two", SkiaSharp.SKColors.Green));
            settings.Dialogs.PickedFile = archive;
            var transferring = TrayDesktop.Get(() => new Transferring(shown.Listing, shown.Notices, shown.Settings, settings.Dialogs, new WindowsRecycleBin(), shown.Handoff,
                shown.OpenWith, shown.Moving, _ => Outcome.Done, TimeProvider.System, HeldFileRetry.Duration));
            var opened = window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page(moving: shown.Moving, transferring: transferring)));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => opened.IsVisible));
            var windows = ProcessWindows.Visible();

            TrayDesktop.Run(() =>
            {
                var names = Named(opened);
                Assert.AreEqual(names.IndexOf(SettingsPage.MoveWords) + 1, names.IndexOf(SettingsPage.ExportWords), "Export stash… right after the stash location's row (G32)");
                Assert.AreEqual(names.IndexOf(SettingsPage.ExportWords) + 1, names.IndexOf(SettingsPage.ImportWords), "then Import stash…");
                Assert.IsFalse(Control(opened, SettingsPage.ConfirmImportWords).IsEffectivelyVisible, "no offer before a file is chosen");
                ShownWindow.RaiseClick((Button)Control(opened, SettingsPage.ImportWords));
            });
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => Control(opened, SettingsPage.ConfirmImportWords).IsEffectivelyVisible));

            Assert.AreEqual(windows, ProcessWindows.Visible(), "choosing the file and the offer change the count of the application's windows by nothing (F4)");
            TrayDesktop.Run(() =>
            {
                CollectionAssert.AreEqual(new[] { Transferring.ImportPickerTitle }, settings.Dialogs.FilePickers, "the one-file picker, once");
                var offer = opened.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text?.StartsWith("export.zip holds 2 items.") == true);
                Assert.IsTrue(offer.IsEffectivelyVisible, "the count and what it replaces, inside the page (G36, F4)");
                var importRow = Control(opened, SettingsPage.ImportWords).GetVisualAncestors().OfType<Grid>().First();
                Assert.IsTrue(offer.GetVisualAncestors().Contains(importRow), "under the Import row's words");
                Assert.IsTrue(Control(opened, SettingsPage.CancelImportWords).IsEffectivelyVisible, "with Import and Cancel");
                Assert.AreEqual(StashState.Open, shown.Listing.State, "nothing replaced before the confirmation");
                ShownWindow.RaiseClick((Button)Control(opened, SettingsPage.CancelImportWords));
            });
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => !Control(opened, SettingsPage.ConfirmImportWords).IsEffectivelyVisible));
            TrayDesktop.Run(() => Assert.AreEqual("offer check", shown.Listing.Items.Single().Name, "its Cancel changed nothing"));
        }
        finally
        {
            if (window is not null)
            {
                TrayDesktop.Run(window.Close);
            }

            shown.Dispose();
            if (archive is not null)
            {
                Directory.Delete(Path.GetDirectoryName(archive)!, recursive: true);
            }
        }
    }
}
