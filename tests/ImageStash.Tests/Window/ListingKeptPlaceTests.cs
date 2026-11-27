using Avalonia.Input;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The person's place when their act takes the selected items out of the list, through the window's keys and acts over
/// a real stash: the item now standing where the first of them stood is selected, the last item past the end, so a
/// second Del deletes the item the person is on; each act that can take the selection out (Delete, un-favoriting in
/// Favorites, the lit chip's tag taken off, a rename out of a search, an import's Undo) does so; items still listed stay
/// selected, an emptied selection stays empty, and a scope, a search and a summon select as G1, G4 and G5 say.
/// </summary>
[TestClass]
public sealed class ListingKeptPlaceTests
{
    [TestMethod]
    [DataRow(new[] { "c" }, "d", DisplayName = "c deleted: d, which slid into its place")]
    [DataRow(new[] { "b", "d" }, "c", DisplayName = "b and d deleted: c, which stands where b stood")]
    [DataRow(new[] { "e" }, "d", DisplayName = "the last deleted: the new last")]
    public async Task DelSelectsTheItemNowStandingWhereTheFirstDeletedItemStood(string[] deleted, string selected)
    {
        using var listed = await ListedStash.Open();
        var items = await AToE(listed);
        listed.Listing.Select(deleted.Select(name => items[name].Id), null);

        await Del(listed);

        CollectionAssert.AreEqual(new[] { items[selected].Id }, listed.Listing.Selection.ToArray());
        Assert.AreEqual(items[selected].Id, listed.Listing.Focused);
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task DeletingTheOnlyItemLeavesNothingSelected()
    {
        using var listed = await ListedStash.Open();
        var only = await listed.Stash.Import("only");
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.Focus(only.Id);

        await Del(listed);

        Assert.IsEmpty(listed.Listing.Items);
        Assert.IsEmpty(listed.Listing.Selection);
        Assert.IsNull(listed.Listing.Focused);
    }

    [TestMethod]
    public async Task ASecondDelOnceTheFirstHasLandedDeletesTheNeighbourAndOnePressedMeanwhileDeletesNothingElse()
    {
        using var listed = await ListedStash.Open();
        var items = await AToE(listed);
        listed.Listing.Focus(items["c"].Id);
        var pressedMeanwhile = false;
        Task? pressed = null;
        listed.Stash.RecycleBin.BeforeEach = _ =>
        {
            if (!pressedMeanwhile)
            {
                pressedMeanwhile = true;
                listed.Keys.Released(Key.Delete);
                pressed = listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);
            }
        };

        await Del(listed);
        await pressed!;
        listed.Stash.RecycleBin.BeforeEach = null;
        CollectionAssert.AreEqual(Names(items, "a", "b", "d", "e"), Listed(listed), "the Del pressed while c was going asked for c again, which is already going");
        Assert.AreEqual(items["d"].Id, listed.Listing.Focused);

        await Del(listed);

        CollectionAssert.AreEqual(Names(items, "a", "b", "e"), Listed(listed), "the second Del took d, the item the person was on, never a");
        Assert.AreEqual(items["e"].Id, listed.Listing.Focused);
    }

    [TestMethod]
    public async Task ASummonWhoseLoadOvertakesTheRefreshKeepsThePlaceToo()
    {
        using var listed = await ListedStash.Open();
        var items = await AToE(listed);
        listed.Listing.Focus(items["c"].Id);
        Assert.IsTrue((await listed.Stash.Store!.Delete([items["c"].Id])).Single().Succeeded);

        var refreshing = listed.Listing.Refresh();
        var summoning = listed.Summon(pinned: true);
        await Task.WhenAll(refreshing, summoning);
        await listed.Listing.Settle();

        CollectionAssert.AreEqual(Names(items, "a", "b", "d", "e"), Listed(listed));
        CollectionAssert.AreEqual(new[] { items["d"].Id }, listed.Listing.Selection.ToArray(), "the summon's load, not the first item");
    }

    [TestMethod]
    public async Task UnFavoritingTheSelectedItemInFavoritesSelectsTheItemThatTookItsPlace()
    {
        using var listed = await ListedStash.Open();
        var items = await AToE(listed);
        await listed.Organising.SetFavorite(items.Values.ToList(), favorite: true);
        await listed.Listing.ShowScope(Scope.Favorites);
        listed.Listing.Focus(items["c"].Id);

        await listed.Press(Key.D, KeyModifiers.Control, KeyPlace.Grid);

        CollectionAssert.AreEqual(Names(items, "a", "b", "d", "e"), Listed(listed));
        CollectionAssert.AreEqual(new[] { items["d"].Id }, listed.Listing.Selection.ToArray());
    }

    [TestMethod]
    public async Task TakingTheLitChipsTagOffTheSelectedItemSelectsTheItemThatTookItsPlace()
    {
        using var listed = await ListedStash.Open();
        var items = await AToE(listed);
        foreach (var item in items.Values)
        {
            listed.Stash.Tag(item.Id, "cat");
        }

        await listed.Listing.ShowScope(Scope.All);
        await listed.Listing.ChooseTag("cat");
        listed.Listing.Focus(items["b"].Id);

        await listed.Organising.RemoveTag(listed.Listing.SelectedItems, "cat");

        Assert.AreEqual("cat", listed.Listing.Query.Trim(), "the chip is still lit");
        CollectionAssert.AreEqual(Names(items, "a", "c", "d", "e"), Listed(listed));
        CollectionAssert.AreEqual(new[] { items["c"].Id }, listed.Listing.Selection.ToArray());
    }

    [TestMethod]
    public async Task ARenameOutOfTheSearchSelectsTheItemThatTookItsPlace()
    {
        using var listed = await ListedStash.Open();
        await AToE(listed, prefix: "fox ");
        await listed.Listing.SetQuery("fox");
        var order = Listed(listed);
        var renamed = listed.Listing.Items[2];
        listed.Listing.Focus(renamed.Id);

        await listed.Renaming.Rename(renamed, "badger");

        Assert.AreEqual("fox", listed.Listing.Query);
        CollectionAssert.AreEqual(order.Where(id => id != renamed.Id).ToList(), Listed(listed));
        CollectionAssert.AreEqual(new[] { order[3] }, listed.Listing.Selection.ToArray());
    }

    [TestMethod]
    public async Task AnImportsUndoSelectsTheItemThatTookTheSelectedOnesPlace()
    {
        using var listed = await ListedStash.Open();
        await AToE(listed);
        var dropped = new HeldBackFile(MediaFixtures.Path("photo.jpg"));
        dropped.Release();
        var added = (await listed.Import.Drop(dropped.Drop)).Single().Item!;
        Assert.AreEqual(added.Id, listed.Listing.Items[0].Id, "All, the new item first and selected");
        CollectionAssert.AreEqual(new[] { added.Id }, listed.Listing.Selection.ToArray());
        var second = listed.Listing.Items[1].Id;

        await listed.Notices.Undo(listed.Notices.Shown.Single());

        CollectionAssert.AreEqual(new[] { second }, listed.Listing.Selection.ToArray(), "a, which now stands first");
    }

    [TestMethod]
    public async Task ItemsStillListedStayTheSelectionAndAnEmptiedSelectionStaysEmpty()
    {
        using var listed = await ListedStash.Open();
        await AToE(listed, prefix: "fox ");
        await listed.Listing.SetQuery("fox");
        var (b, c) = (Item(listed, "fox b"), Item(listed, "fox c"));
        listed.Listing.Select([b.Id, c.Id], c.Id);

        await listed.Renaming.Rename(b, "badger");

        CollectionAssert.AreEqual(new[] { c.Id }, listed.Listing.Selection.ToArray(), "c is still listed and stays the selection");
        Assert.AreEqual(c.Id, listed.Listing.Focused);

        listed.Listing.Toggle(c.Id);
        await listed.Renaming.Rename(Item(listed, "fox d"), "dingo");

        Assert.IsEmpty(listed.Listing.Selection, "the person emptied it");
    }

    [TestMethod]
    public async Task AScopeASearchAndASummonSelectAsBeforeTheFirstItemOrWhatWasLeft()
    {
        using var listed = await ListedStash.Open();
        var items = await AToE(listed);
        listed.Listing.Focus(items["c"].Id);

        await listed.Summon();
        CollectionAssert.AreEqual(new[] { items["c"].Id }, listed.Listing.Selection.ToArray(), "a summon keeps the selection (G1)");

        await listed.Listing.ShowScope(Scope.All);
        CollectionAssert.AreEqual(new[] { items["a"].Id }, listed.Listing.Selection.ToArray(), "a scope click selects the first item (G4)");

        listed.Listing.Focus(items["c"].Id);
        await listed.Listing.SetQuery("d");
        CollectionAssert.AreEqual(new[] { items["d"].Id }, listed.Listing.Selection.ToArray(), "a search selects the first match (G5)");
    }

    /// <summary>Five items that All lists as a b c d e (newest added first), each named <paramref name="prefix"/> and its letter, All shown.</summary>
    internal static async Task<Dictionary<string, StashItem>> AToE(ListedStash listed, string prefix = "")
    {
        var items = new Dictionary<string, StashItem>();
        foreach (var letter in new[] { "e", "d", "c", "b", "a" })
        {
            items[letter] = await listed.Stash.Import(prefix + letter);
        }

        await listed.Listing.ShowScope(Scope.All);
        CollectionAssert.AreEqual(Names(items, "a", "b", "c", "d", "e"), Listed(listed));
        return items;
    }

    private static async Task Del(ListedStash listed)
    {
        listed.Keys.Released(Key.Delete);
        await listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);
    }

    private static StashItem Item(ListedStash listed, string name) => listed.Listing.Items.Single(item => item.Name == name);

    private static List<long> Listed(ListedStash listed) => listed.Listing.Items.Select(item => item.Id).ToList();

    private static List<long> Names(Dictionary<string, StashItem> items, params string[] letters) => letters.Select(letter => items[letter].Id).ToList();
}
