using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The preview's ⋯ menu through the real stash window (G19, G9, G10, G18, G7, G27, F4): Save a copy…, Open with…,
/// Reveal in File Explorer and, after a separator, Delete, and nothing more; built on the previewed item whatever the
/// grid selects, Save a copy…, Open with… and Reveal acting as the item menu's do and recording no use, Delete as Del
/// does, the preview closing
/// once its item is gone; a menu of the stash window, never a window of its own; its keys its own while it is open, so
/// Down and Enter choose an entry and hand nothing off, and Esc closes the menu alone. Its dialogs are the check's own;
/// Delete sends a small picture of the check's to the Recycle Bin and its Undo removes that entry again.
/// </summary>
[TestClass]
public sealed class ItemMenuTests
{
    [TestMethod]
    public async Task ThePreviewsMenuActsOnItsItemWhateverTheGridSelectsRecordsNoUseAndIsAMenuOfTheWindow()
    {
        var dialogs = new PresetDialogs();
        using var shown = ShownWindow.Open(dialogs: dialogs);
        var items = await shown.OpenStash(shown.Picture("menu one.png"), shown.Picture("menu two.png"), shown.Picture("menu three.png"));
        var previewed = items[1];
        TrayDesktop.Run(() => shown.OpenPreview(previewed));
        TrayDesktop.Run(() => shown.Listing.Select(items.Select(item => item.Id), items[0].Id));

        var labels = TrayDesktop.Get(shown.ClickMore);

        CollectionAssert.AreEqual(new[] { "Edit", "Rename", "—", "Save a copy…", "Open with…", "Reveal in File Explorer", "—", "Delete" }, labels, "G19's ⋯ menu on an unedited item: no Revert or Save as new item");
        TrayDesktop.Run(() =>
        {
            var menu = TopLevel.GetTopLevel(shown.OpenMenu!);
            Assert.IsInstanceOfType<PopupRoot>(menu, "a popup of the window's, not a window (F4)");
            var handle = menu.TryGetPlatformHandle()!.Handle;
            Assert.AreEqual(shown.Window.TryGetPlatformHandle()!.Handle, GetWindow(handle, OwnerOf), "owned by the stash window");
            Assert.AreNotEqual(0, GetWindowLongPtr(handle, ExtendedStyle) & ToolWindow, "with no taskbar button of its own");
        });

        TrayDesktop.Run(() => shown.ClickMenuEntry("Reveal in File Explorer"));
        CollectionAssert.AreEqual(new[] { previewed.FilePath }, dialogs.Revealed, "the previewed item's file, not the grid's focused item's (G9)");

        TrayDesktop.Run(() =>
        {
            shown.ClickMore();
            shown.ClickMenuEntry("Open with…");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => dialogs.OpenedWith.Count == 1));
        var copy = TrayDesktop.Get(() => dialogs.OpenedWith.Single());
        Assert.AreEqual(previewed.FileName, Path.GetFileName(copy), "the dialog on a copy of the previewed item's finished picture, not the grid's focused item's (G27, G9)");
        Assert.IsFalse(copy.StartsWith(shown.Listing.Store!.Folder.Root, StringComparison.OrdinalIgnoreCase), "kept outside the stash folder (G27)");
        // The window reads the copy without sharing once the dialog has closed; it is read here once that has let go.
        byte[]? copied = null;
        await ShownWindow.Eventually(() =>
        {
            try
            {
                copied = File.ReadAllBytes(copy);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        });
        CollectionAssert.AreEqual(File.ReadAllBytes(previewed.FilePath), copied, "the finished picture as it is");
        TrayDesktop.Run(() => Assert.AreEqual("", shown.Said(), "nothing refused"));

        var inside = Path.Combine(shown.Listing.Store!.Folder.Root, "menu copy.png");
        dialogs.Saves = inside;
        TrayDesktop.Run(() =>
        {
            shown.ClickMore();
            shown.ClickMenuEntry("Save a copy…");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown.Any(notice => notice.Text.Contains("menu copy.png"))));
        TrayDesktop.Run(() => Assert.AreEqual("Save a copy failed: menu copy.png, it would be inside the stash folder, which holds only ImageStash's own files",
            shown.Notices.Shown.Single(notice => notice.Text.Contains("menu copy.png")).Text, "refused by the item menu's own rule (G9, G35)"));
        Assert.IsFalse(File.Exists(inside), "and nothing written there (G29)");

        var outside = Path.Combine(Path.GetTempPath(), "imagestash-menu-copy-" + Guid.NewGuid().ToString("N") + ".png");
        dialogs.Saves = outside;
        try
        {
            TrayDesktop.Run(() =>
            {
                shown.ClickMore();
                shown.ClickMenuEntry("Save a copy…");
            });
            // The copy is there before it is written; read it once nothing holds it, so the read is of the finished copy.
            await ShownWindow.Eventually(() => File.Exists(outside) && !ShownWindow.Held(outside));
            CollectionAssert.AreEqual(File.ReadAllBytes(previewed.FilePath), File.ReadAllBytes(outside), "the previewed item's finished picture, as it is (G9)");
        }
        finally
        {
            File.Delete(outside);
        }

        Assert.AreEqual(0L, shown.Uses(), "neither Save a copy… nor Reveal is a use (G10)");
        TrayDesktop.Run(() => Assert.AreEqual(previewed.Id, shown.Preview.Item?.Id, "the preview still on its item"));
    }

    [TestMethod]
    public async Task WithTheMenuOpenItsKeysAreItsOwnDownAndEnterDeleteWithoutAHandoffAndEscClosesTheMenuAlone()
    {
        var target = new PresetTarget();
        using var shown = ShownWindow.Open(target: target);
        var items = await shown.OpenStash(shown.Picture("menu keys one.png"), shown.Picture("menu keys two.png"));
        var previewed = items[0];
        // With a target, Enter reaching the window would paste into it: what the menu's own Enter must not do.
        TrayDesktop.Run(() => shown.Window.PasteTarget.FromPress(new ForegroundReading(false, PresetTarget.App("Relay"))));
        TrayDesktop.Run(() => shown.OpenPreview(previewed));
        TrayDesktop.Run(() => shown.MoreButton.Focus(NavigationMethod.Tab));

        TrayDesktop.Run(() => PressSpace(shown.MoreButton));
        TrayDesktop.Run(() => Assert.IsTrue(shown.OpenMenu is { IsOpen: true }, "Space on the focused ⋯ button opens its menu (G7)"));
        TrayDesktop.Run(() => shown.SendKey(Key.Escape));
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.OpenMenu!.IsOpen, "Esc closes the menu");
            Assert.IsTrue(shown.Preview.IsOpen && shown.Window.IsVisible, "and only the menu: the preview stays open (G2)");
            Assert.AreSame(shown.MoreButton, shown.Focused, "focus back on the ⋯ button");
        });

        TrayDesktop.Run(() => PressSpace(shown.MoreButton));
        TrayDesktop.Run(() =>
        {
            shown.SendKey(Key.Down);
            shown.SendKey(Key.Down);
            shown.SendKey(Key.Down);
            shown.SendKey(Key.Down);
            shown.SendKey(Key.Down);
            shown.SendKey(Key.Down);
        });
        TrayDesktop.Run(() => Assert.AreEqual("Delete", (shown.Focused as MenuItem)?.Header as string, "Down goes through the menu's entries, past its separators"));
        TrayDesktop.Run(() => shown.SendKey(Key.Enter));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Preview.IsOpen && shown.Notices.Shown.Any(notice => notice.Text == "Deleted “menu keys one”.")));

        Assert.IsEmpty(target.Pasted, "Enter chose the entry and handed nothing off (G7, G8)");
        Assert.AreEqual(0L, shown.Uses());
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible);
            CollectionAssert.AreEqual(new[] { items[1].Id }, shown.Listing.Items.Select(item => item.Id).ToList(), "deleted as Del deletes (G18)");
        });
        Assert.HasCount(1, RecycleBinEntries.Of(previewed.FilePath), "its file in the Recycle Bin");

        var notice = TrayDesktop.Get(() => shown.Notices.Shown.Single(notice => notice.Text == "Deleted “menu keys one”."));
        TrayDesktop.Run(() => _ = shown.Notices.Undo(notice));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 2));
        Assert.IsEmpty(RecycleBinEntries.Of(previewed.FilePath), "the check leaves nothing in the bin");
    }

    private const int OwnerOf = 4;
    private const int ExtendedStyle = -20;
    private const nint ToolWindow = 0x80;

    /// <summary>Space pressed and released on a focused button, as the keyboard presses it.</summary>
    private static void PressSpace(Button button)
    {
        ShownWindow.Press(button, Key.Space);
        ShownWindow.Release(button, Key.Space);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);
}
