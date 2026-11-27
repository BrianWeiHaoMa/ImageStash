using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.TagMenuControls;

namespace ImageStash.Tests.Window;

/// <summary>
/// Rename tag…'s box left (G17): focus moving to another part of the window renames as Enter does; Esc, focus going with
/// no element taking it (another app, the flyout's hide), or leaving with the name unchanged write nothing and say nothing.
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

    /// <summary>The box losing focus, to another element of the window, or to none.</summary>
    private static FocusChangedEventArgs Leaving(bool toElement) =>
        new(InputElement.LostFocusEvent) { NewFocusedElement = toElement ? new Button() : null };
}
