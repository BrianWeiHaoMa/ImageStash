using Avalonia.Input;
using Avalonia.Threading;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.TagMenuControls;

namespace ImageStash.Tests.Window;

/// <summary>
/// One run through what holds still under the person's hand, each part driven through its own entry: an item menu's
/// Add tag ▸ keeps the order it opened with while its counts change (G17); a tag renamed through its lit chip keeps the
/// search on it, its chip lit in its place and every carrier listed, and Undo gives it back (G17, G5, G4); a Delete
/// selects the neighbour that took the deleted item's place, and its Undo selects what came back (G18).
/// </summary>
[TestClass]
public sealed class HeldInPlaceTests
{
    [TestMethod]
    public async Task TagsGivenRenamedUndoneAndADeleteUndoneEachLeaveThePersonWhereTheyWere()
    {
        using var listed = await ListedStash.Open();
        var plain = await listed.Stash.Import("plain");
        var c = await listed.Stash.Import("c");
        var b = await listed.Stash.Import("b");
        var a = await listed.Stash.Import("a");
        listed.Stash.Tag(a.Id, "alpha");
        listed.Stash.Tag(b.Id, "beta");
        listed.Stash.Tag(b.Id, "alpha");
        listed.Stash.Tag(c.Id, "beta");
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.Select([plain.Id], plain.Id);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var menu = new ItemMenuTagLists(listed.Listing, listed.Notices, listed.Organising, listed.Listing.SelectedItems);
            Assert.IsTrue(await menu.Read());
            CollectionAssert.AreEqual(new (string?, int?)[] { ("alpha", 2), ("beta", 2) }, Rows(menu), "tied in count, then by spelling, as the list opens");
            Assert.IsTrue((await menu.Click(ItemMenuAction.AddTag, "beta")).Succeeded);
            CollectionAssert.AreEqual(new (string?, int?)[] { ("alpha", 2), ("beta", 3) }, Rows(menu), "beta is the commonest now; its row holds its place, its count follows");
            Assert.IsTrue((await menu.Click(ItemMenuAction.AddTag, "alpha")).Succeeded);
            CollectionAssert.AreEqual(new (string?, int?)[] { ("alpha", 3), ("beta", 3) }, Rows(menu));

            var search = WindowLayout.SearchBox();
            SearchBoxQuery.Wire(search, listed.Listing, new SummonSelection());
            var row = Chips(listed);
            Click(Chip(row, "beta"));
            await listed.Listing.Settle();
            Assert.AreEqual("beta", listed.Listing.Query);
            var carriers = new[] { b.Id, c.Id, plain.Id };
            CollectionAssert.AreEquivalent(carriers, Listed(listed));
            var places = listed.Listing.Chips.Select(chip => chip.Tag).ToList();

            var box = OpenRename(Chip(row, "beta"));
            box.Text = "ace";
            await listed.WaitForOrganising(() => Press(box, Key.Enter));
            listed.Keys.Released(Key.Enter);
            await listed.Listing.Settle();
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual("ace", listed.Listing.Query, "the search follows the rename");
            Assert.AreEqual("ace", search.Text, "and the search box shows it");
            CollectionAssert.AreEquivalent(carriers, Listed(listed), "the grid still lists every item that carries it");
            CollectionAssert.AreEqual(places.Select(tag => tag == "beta" ? "ace" : tag).ToList(), listed.Listing.Chips.Select(chip => chip.Tag).ToList(),
                "the chip in its place under its new name, though a fresh order would put #ace first");
            Assert.IsTrue(Chip(row, "ace").Classes.Contains("accent"), "and lit");

            await listed.WaitForOrganising(() => _ = listed.Notices.Undo(listed.Notices.Shown.Single(notice => notice.Text == "Renamed #beta to #ace")));
            await listed.Listing.Settle();
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual("beta", listed.Listing.Query, "Undo puts the word back");
            Assert.AreEqual("beta", search.Text);
            CollectionAssert.AreEquivalent(carriers, Listed(listed));
            CollectionAssert.AreEqual(places, listed.Listing.Chips.Select(chip => chip.Tag).ToList());
            Assert.IsTrue(Chip(row, "beta").Classes.Contains("accent"));
            Assert.IsFalse(listed.Listing.Tags.Any(tag => tag.Tag == "ace"));

            var order = Listed(listed);
            var at = order.IndexOf(b.Id);
            Assert.IsLessThan(order.Count - 1, at, "b has a neighbour after it");
            var neighbour = order[at + 1];
            listed.Listing.Select([b.Id], b.Id);
            listed.Keys.Released(Key.Delete);
            await listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);

            CollectionAssert.DoesNotContain(Listed(listed), b.Id);
            CollectionAssert.AreEqual(new[] { neighbour }, listed.Listing.Selection.ToArray(), "the neighbour that took its place is selected");

            await listed.Notices.Undo(listed.Notices.Shown.Single(notice => notice.Text.StartsWith("Deleted", StringComparison.Ordinal)));

            CollectionAssert.AreEqual(order, Listed(listed), "the deleted item back in its place");
            CollectionAssert.AreEqual(new[] { b.Id }, listed.Listing.Selection.ToArray(), "the restored item selected");
            Assert.IsEmpty(listed.Failures);
        }));
    }

    private static (string?, int?)[] Rows(ItemMenuTagLists menu) =>
        [.. menu.AddTag.Where(entry => entry.Tag is not null).Select(entry => (entry.Tag, entry.Count))];

    private static List<long> Listed(ListedStash listed) => listed.Listing.Items.Select(item => item.Id).ToList();
}
