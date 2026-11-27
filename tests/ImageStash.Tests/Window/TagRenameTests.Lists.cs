using Avalonia.Input;
using Avalonia.Threading;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.TagMenuControls;

namespace ImageStash.Tests.Window;

/// <summary>
/// A rename made from a row of an open list of every tag (G17): the row keeps its place under the new name, and a merge
/// onto a tag the list also holds leaves no two rows for one tag.
/// </summary>
public sealed partial class TagRenameTests
{
    [TestMethod]
    public async Task ARenameFromTheListOfEveryTagKeepsItsRowInPlace()
    {
        using var listed = await ListedStash.Open();
        var (a, b, c) = (await listed.Stash.Import("a", TestStash.Bytes(1)), await listed.Stash.Import("b", TestStash.Bytes(2)), await listed.Stash.Import("c", TestStash.Bytes(3)));
        listed.Stash.Tag(a.Id, "cat");
        listed.Stash.Tag(b.Id, "cat");
        listed.Stash.Tag(b.Id, "dog");
        listed.Stash.Tag(c.Id, "dog");
        listed.Stash.Tag(c.Id, "emu");
        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var field = new TagField();
            field.Picker.ConfigureTagManagement(listed.Listing, listed.Organising, listed.Keys, listed.Paste);
            field.Show([], listed.Listing.Tags, []);
            Click(EveryTag(field));
            var opened = field.Picker.Entries.Select(entry => entry.Tag).ToList();

            var box = OpenRename(PickerRow(field.Picker, "cat"));
            box.Text = "kitty";
            await listed.WaitForOrganising(() => Press(box, Key.Enter));
            listed.Keys.Released(Key.Enter);
            field.Show([], listed.Listing.Tags, []);
            Dispatcher.UIThread.RunJobs();

            CollectionAssert.AreEqual(opened.Select(tag => tag == "cat" ? "kitty" : tag).ToList(), field.Picker.Entries.Select(entry => entry.Tag).ToList(),
                "the renamed row keeps its place under its new name");

            var merge = OpenRename(PickerRow(field.Picker, "emu"));
            merge.Text = "dog";
            await listed.WaitForOrganising(() => Press(merge, Key.Enter));
            listed.Keys.Released(Key.Enter);
            field.Show([], listed.Listing.Tags, []);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(1, field.Picker.Entries.Count(entry => entry.Tag == "dog"), "no two rows for one tag");
            Assert.HasCount(opened.Count, field.Picker.Entries, "the merged tag's row stays, reading 0, for the list's open time");
        });
    }

    [TestMethod]
    public async Task ARenameFromTheChipRowsListOfEveryTagKeepsItsRowInPlaceAndAClickOnItPicksTheNewName()
    {
        using var listed = await ListedStash.Open();
        var (a, b, c) = (await listed.Stash.Import("a", TestStash.Bytes(1)), await listed.Stash.Import("b", TestStash.Bytes(2)), await listed.Stash.Import("c", TestStash.Bytes(3)));
        listed.Stash.Tag(a.Id, "cat");
        listed.Stash.Tag(b.Id, "cat");
        listed.Stash.Tag(b.Id, "dog");
        listed.Stash.Tag(c.Id, "dog");
        listed.Stash.Tag(c.Id, "emu");
        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var row = Chips(listed);
            Click(EveryTag(row));
            await listed.Listing.Settle();
            Dispatcher.UIThread.RunJobs();
            var opened = row.Picker.Entries.Select(entry => entry.Tag).ToList();
            CollectionAssert.AreEqual(new[] { "cat", "dog", "emu" }, opened, "the commonest first, then by spelling, as it opens");

            var box = OpenRename(PickerRow(row.Picker, "cat"));
            box.Text = "kitty";
            await listed.WaitForOrganising(() => Press(box, Key.Enter));
            listed.Keys.Released(Key.Enter);
            await listed.Listing.Settle();
            Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(row.Picker.IsOpen);
            CollectionAssert.AreEqual(new[] { "kitty", "dog", "emu" }, row.Picker.Entries.Select(entry => entry.Tag).ToList(),
                "the renamed row keeps its place under its new name, not re-sorted by spelling nor joining the end");
            Assert.AreEqual(2, row.Picker.Entries.Single(entry => entry.Tag == "kitty").Count);

            Click(PickerRow(row.Picker, "kitty"));
            await listed.Listing.Settle();

            Assert.AreEqual("kitty", listed.Listing.Query, "a click on the row acts on the tag it shows");
            CollectionAssert.AreEquivalent(new[] { a.Id, b.Id }, listed.Listing.Items.Select(item => item.Id).ToArray());
        });
    }
}
