using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.TagMenuControls;

namespace ImageStash.Tests.Window;

/// <summary>
/// Rename tag…'s box left (G17): focus moving to another part of the window renames as Enter does; Esc, focus going with
/// no element taking it (another app, the flyout's hide), or leaving with the name unchanged write nothing and say nothing.
/// In a list of every tag, a press elsewhere in the window closes the list before focus moves, and keeps the name.
/// </summary>
public sealed partial class TagRenameTests
{
    [TestMethod]
    public async Task TheRenameBoxKeepsTheTypedNameOnlyWhenFocusMovesToAnotherPartOfTheWindow()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("picture");
        listed.Stash.Tag(item.Id, "cat");
        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);

            var unchanged = OpenRename(Chip(Chips(listed), "cat"));
            unchanged.RaiseEvent(Leaving(toElement: true));
            Assert.IsFalse(listed.Organising.Changing, "the name as it was: nothing asked");

            var gone = OpenRename(Chip(Chips(listed), "cat"));
            gone.Text = "dropped";
            gone.RaiseEvent(Leaving(toElement: false));
            Assert.IsFalse(listed.Organising.Changing, "focus gone with no element taking it writes nothing");

            var cancelled = OpenRename(Chip(Chips(listed), "cat"));
            cancelled.Text = "abandoned";
            Press(cancelled, Key.Escape);
            listed.Keys.Released(Key.Escape);
            cancelled.RaiseEvent(Leaving(toElement: true));
            Assert.IsFalse(listed.Organising.Changing, "Esc ended the draft before the focus it gives back moved");
            Assert.AreEqual("cat", (await Stored(listed, item)).Tags.Single());
            Assert.IsEmpty(listed.Notices.Shown, "nothing said");

            var kept = OpenRename(Chip(Chips(listed), "cat"));
            kept.Text = "kitty";
            await listed.WaitForOrganising(() => kept.RaiseEvent(Leaving(toElement: true)));

            Assert.AreEqual("kitty", (await Stored(listed, item)).Tags.Single(), "renamed as Enter renames");
            Assert.AreEqual("Renamed #cat to #kitty", listed.Notices.Shown.Single().Text);
        });
    }

    [TestMethod]
    public async Task ARenameInAListOfEveryTagIsKeptWhenAPressElsewhereInTheWindowClosesTheList()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("picture");
        listed.Stash.Tag(item.Id, "cat");
        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var field = new TagField();
            field.Picker.ConfigureTagManagement(listed.Listing, listed.Organising, listed.Keys, listed.Paste);
            var tile = new Button();
            // The window the list's opener shows in, with another part of it to press.
            _ = new StackPanel { Children = { field, tile } };
            field.Show([], listed.Listing.Tags, []);

            Click(EveryTag(field));
            var cancelled = OpenRename(PickerRow(field.Picker, "cat"));
            cancelled.Text = "abandoned";
            Press(cancelled, Key.Escape);
            listed.Keys.Released(Key.Escape);
            tile.RaiseEvent(PressOn(tile));
            Assert.IsFalse(listed.Organising.Changing, "Esc ended the draft before the press");

            var hidden = OpenRename(PickerRow(field.Picker, "cat"));
            hidden.Text = "dropped";
            field.Picker.Close();
            tile.RaiseEvent(PressOn(tile));
            Assert.IsFalse(listed.Organising.Changing, "the list closing with no press in the window (a hide, another app) writes nothing");
            Assert.AreEqual("cat", (await Stored(listed, item)).Tags.Single());
            Assert.IsEmpty(listed.Notices.Shown, "nothing said");

            Click(EveryTag(field));
            Dispatcher.UIThread.RunJobs();
            var kept = OpenRename(PickerRow(field.Picker, "cat"));
            kept.Text = "kitty";
            await listed.WaitForOrganising(() => tile.RaiseEvent(PressOn(tile)));

            Assert.AreEqual("kitty", (await Stored(listed, item)).Tags.Single(), "renamed as Enter renames, before the press acts");
            Assert.AreEqual("Renamed #cat to #kitty", listed.Notices.Shown.Single().Text);
        });
    }

    /// <summary>A left press on <paramref name="target"/>.</summary>
    private static PointerPressedEventArgs PressOn(Control target) => new(target,
        new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), target, default, 0,
        new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1);

    /// <summary>The box losing focus, to another element of the window, or to none.</summary>
    private static FocusChangedEventArgs Leaving(bool toElement) =>
        new(InputElement.LostFocusEvent) { NewFocusedElement = toElement ? new Button() : null };
}
