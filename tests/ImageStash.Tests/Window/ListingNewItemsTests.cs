using Avalonia.Input;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The copy Save as new item makes is listed and selected (G26, G4): from Recent or Favorites, neither of which can list
/// it, the window shows All with no search and the copy selected, as an import shows what it added (G12), and the store
/// records nothing that would list it in Recent; from All, or a search it matches, the scope and the search stay. The
/// editor's button and the item menu's entry alike, a Cancel after the menu's leaving the copy selected, and Enter after
/// the edit's Save handing over the copy.
/// </summary>
[TestClass]
public sealed class ListingNewItemsTests
{
    [TestMethod]
    [DataRow(Scope.Recent)]
    [DataRow(Scope.Favorites)]
    public async Task FromAScopeThatCannotListTheCopyAllShowsWithTheCopySelectedAndEnterAfterSaveHandsItOver(Scope scope)
    {
        using var listed = await ListedStash.Open();
        var cat = await InScope(listed, scope);
        await listed.Editing.Open(cat, EditOrigin.Grid);
        Hide(listed);

        var saved = await listed.Editing.SaveAsNewItem();

        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        var copy = listed.Editing.Session!.Item;
        Assert.AreNotEqual(cat.Id, copy.Id);
        Assert.AreEqual((Scope.All, ""), (listed.Listing.Scope, listed.Listing.Query));
        CollectionAssert.AreEqual(new[] { copy.Id }, listed.Listing.Selection.ToArray(), "the copy, not the original");
        Hide(listed);
        Assert.IsTrue((await listed.Editing.Save()).Succeeded);
        CollectionAssert.AreEqual(new[] { copy.Id }, listed.Listing.Selection.ToArray(), "back on the grid, still the copy");
        Assert.IsFalse((await listed.Stash.Store!.ListRecent()).Items.Value!.Any(item => item.Id == copy.Id), "nothing records the copy as a capture (G4)");

        listed.Keys.Released(Key.Enter);
        var handing = listed.Press(Key.Enter, KeyModifiers.None, KeyPlace.Grid);
        listed.Keys.Released(Key.Enter);
        await handing;

        var finished = (await listed.Stash.All()).Single(item => item.Id == copy.Id).FilePath;
        CollectionAssert.AreEqual(new[] { finished }, listed.Clipboard.Written.Single().Content.FilePaths.ToArray(), "Enter hands over the copy's finished picture");
        Assert.IsTrue((await listed.Stash.Store!.ForEdit(cat.Id)).Value!.Layers.IsEmpty, "the item edited until now as it was (G26)");
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task FromAllAndFromASearchTheCopyMatchesTheScopeAndTheSearchStay()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.ShowScope(Scope.All);
        await SaveACopy(listed, cat);
        Assert.AreEqual((Scope.All, ""), (listed.Listing.Scope, listed.Listing.Query));
        CollectionAssert.AreEqual(new[] { listed.Editing.Session!.Item.Id }, listed.Listing.Selection.ToArray());
        listed.Editing.Cancel();

        await listed.Listing.ShowScope(Scope.Favorites);
        await listed.Listing.SetQuery("cat");
        await SaveACopy(listed, cat);

        Assert.AreEqual("cat", listed.Listing.Query, "“cat 3” matches the search, which stays");
        Assert.AreEqual(Scope.Favorites, listed.Listing.Scope, "and so does the scope it returns to");
        CollectionAssert.AreEqual(new[] { listed.Editing.Session!.Item.Id }, listed.Listing.Selection.ToArray());
    }

    [TestMethod]
    public async Task TheMenusSaveAsNewItemFromRecentThenCancelLeavesAllWithTheCopySelected()
    {
        using var listed = await ListedStash.Open();
        var cat = await InScope(listed, Scope.Recent);
        cat = (await listed.Stash.SaveEdit(cat, EditedItems.BlackBoxAndBox)).Value!;
        await listed.Listing.Refresh();

        var made = await listed.Editing.SaveAsNewItem(cat, EditOrigin.Grid);

        Assert.IsTrue(made.Succeeded, made.Failure?.Sentence);
        var copy = listed.Editing.Session!.Item;
        listed.Editing.Cancel();
        Assert.IsFalse(listed.Editing.IsOpen);
        Assert.AreEqual((Scope.All, ""), (listed.Listing.Scope, listed.Listing.Query));
        CollectionAssert.AreEqual(new[] { copy.Id }, listed.Listing.Selection.ToArray());
    }

    /// <summary>"cat", shown and selected in <paramref name="scope"/>: used, for Recent; a favorite, for Favorites.</summary>
    private static async Task<StashItem> InScope(ListedStash listed, Scope scope)
    {
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        if (scope == Scope.Recent)
        {
            await listed.Stash.Store!.RecordUses([cat], listed.Stash.Clock.Now);
        }
        else
        {
            await listed.Organising.SetFavorite([cat], favorite: true);
        }

        await listed.Listing.ShowScope(scope);
        listed.Listing.Focus(cat.Id);
        return listed.Listing.Items.Single();
    }

    private static async Task SaveACopy(ListedStash listed, StashItem item)
    {
        await listed.Editing.Open(item, EditOrigin.Grid);
        Hide(listed);
        Assert.IsTrue((await listed.Editing.SaveAsNewItem()).Succeeded);
    }

    private static void Hide(ListedStash listed)
    {
        listed.Editing.Session!.Tool = listed.Editing.Session.Hide();
        listed.Editing.Session.DragThrough((1, 1), (4, 3));
    }
}
