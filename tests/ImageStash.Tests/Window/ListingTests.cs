using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// What the window shows, through the one listing model over a real stash: G1's first show and what a summon keeps, G4's
/// scopes, Favorites among them, G5's search with its no-match state, and the stash's states; nothing of the stash read
/// before it has opened (State: Every change, G35). The chips, the selection and the store's changes have files of their
/// own beside this one.
/// </summary>
[TestClass]
public sealed class ListingTests
{
    private readonly List<Failure> failures = [];

    private Listing NewListing() => new(failures.Add);

    [TestMethod]
    public async Task TheFirstShowAfterStartIsAllWithItsFirstItemSelectedWhileRecentIsEmpty()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var items = new List<StashItem>();
        for (var i = 0; i < 10; i++)
        {
            items.Add(await stash.Import($"item {i}"));
        }

        var listing = NewListing();
        await listing.StashOpened(stash.Store!);

        Assert.AreEqual("", listing.Query);
        Assert.AreEqual(Scope.All, listing.Scope);
        Assert.AreEqual(Scope.All, listing.LitChip);
        Assert.HasCount(10, listing.Items);
        Assert.AreEqual(items[9].Id, listing.Items[0].Id, "All lists newest-added first");
        CollectionAssert.AreEquivalent(new[] { items[9].Id }, listing.Selection.ToList());
        Assert.AreEqual(items[9].Id, listing.Focused);
    }

    [TestMethod]
    public async Task TheFirstShowAfterStartIsRecentOnceAnItemHasBeenUsed()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var used = await stash.Import("used");
        await stash.Import("newer, never used");
        await stash.Store!.RecordUses([used], stash.Clock.Now);
        var listing = NewListing();
        await listing.StashOpened(stash.Store);

        Assert.AreEqual(Scope.Recent, listing.Scope);
        Assert.AreEqual(used.Id, listing.Items.Single().Id);
        Assert.AreEqual(used.Id, listing.Focused);
    }

    [TestMethod]
    public async Task ASummonLoadsNothingAnewSoTheScopeTheQueryAndTheSelectionStayAsTheyWereLeft()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("Cat");
        var cap = await stash.Import("Cap");
        await stash.Import("Dog");
        await stash.Store!.SetFavorite([cat, cap], true);
        var listing = NewListing();
        await listing.StashOpened(stash.Store);
        await listing.ShowScope(Scope.Favorites);
        listing.Focus(cat.Id);
        var changes = 0;
        listing.Changed += () => changes++;

        var summon = listing.RetryFailedLoad();

        Assert.IsTrue(summon.IsCompleted, "no load started");
        Assert.AreEqual(0, changes);
        Assert.AreEqual(Scope.Favorites, listing.Scope, "G1: the scope it was left with");
        CollectionAssert.AreEqual(new[] { cat.Id }, listing.Selection.ToList(), "the second tile, not moved back to the first");

        await listing.SetQuery("ca");
        listing.Focus(cat.Id);
        await listing.RetryFailedLoad();

        Assert.AreEqual("ca", listing.Query, "the search it was left with");
        Assert.AreEqual(Scope.All, listing.LitChip);
        CollectionAssert.AreEqual(new[] { cat.Id }, listing.Selection.ToList());

        await listing.SetQuery("");

        Assert.AreEqual(Scope.Favorites, listing.Scope, "the search cleared afterwards, the scope the user was in (G2, G5)");
    }

    [TestMethod]
    public async Task ALoadTheStoreRefusedIsAskedForAgainAtTheNextSummonAndOnlyThat()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("Cat");
        var dog = await stash.Import("Dog");
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);
        listing.Focus(cat.Id);
        using (stash.HoldStore())
        {
            await listing.SetQuery("dog");
        }

        Assert.AreEqual(cat.Id, listing.Focused, "the previous items stay showing (G35)");
        Assert.IsNotEmpty(failures);

        await listing.RetryFailedLoad();

        CollectionAssert.AreEqual(new[] { dog.Id }, listing.Items.Select(item => item.Id).ToList(), "the store answers again: the query's matches");
        CollectionAssert.AreEqual(new[] { dog.Id }, listing.Selection.ToList(), "and the first match selected, the refused load's move not lost (G5)");
        Assert.IsTrue(listing.RetryFailedLoad().IsCompleted, "answered once, nothing is owed");
    }

    [TestMethod]
    public async Task AFirstLoadTheStoreRefusedStillOpensOnAllAtTheNextSummon()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("Cat");
        var listing = NewListing();
        using (stash.HoldStore())
        {
            await listing.StashOpened(stash.Store!);
        }

        await listing.RetryFailedLoad();

        Assert.AreEqual(Scope.All, listing.Scope, "G1: Recent is empty");
        CollectionAssert.AreEqual(new[] { cat.Id }, listing.Selection.ToList());
    }

    [TestMethod]
    public async Task TypingSearchesNamesAndTagsLightsAllAndSelectsTheFirstMatch()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("Cat");
        var catalogue = await stash.Import("catalogue");
        var dog = await stash.Import("Dog");
        await stash.Import("Walrus-free zone");
        stash.Tag(dog.Id, "cat");
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);

        await listing.SetQuery("CAT");

        CollectionAssert.AreEqual(new[] { dog.Id, catalogue.Id, cat.Id }, listing.Items.Select(i => i.Id).ToList());
        Assert.AreEqual(Scope.All, listing.LitChip);
        Assert.AreEqual(dog.Id, listing.Focused);
        CollectionAssert.AreEquivalent(new[] { dog.Id }, listing.Selection.ToList());
        Assert.IsFalse(listing.NoMatch);

        await listing.SetQuery("cat dog");

        Assert.AreEqual(dog.Id, listing.Items.Single().Id);
    }

    [TestMethod]
    public async Task AQueryMatchingNothingIsTheNoMatchStateNamingItAndNotTheEmptyStash()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        await stash.Import("Cat");
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);

        await listing.SetQuery("walrus ");

        Assert.IsEmpty(listing.Items);
        Assert.IsTrue(listing.NoMatch);
        Assert.IsFalse(listing.IsEmptyStash);
        Assert.AreEqual("walrus ", listing.Query, "the no-match state shows the query as typed");
        Assert.IsNull(listing.Focused);
    }

    [TestMethod]
    public async Task AnEmptyStashIsTheEmptyStateInEitherScopeAndNotNoMatch()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);

        Assert.IsTrue(listing.IsEmptyStash);
        Assert.AreEqual(Scope.All, listing.Scope);

        await listing.ShowScope(Scope.Recent);

        Assert.IsTrue(listing.IsEmptyStash, "an empty stash is the empty state on Recent too");
        Assert.IsFalse(listing.NoMatch);
        await listing.SetQuery("cat");
        Assert.IsFalse(listing.IsEmptyStash);
        Assert.IsTrue(listing.NoMatch);
    }

    [TestMethod]
    public async Task AnEmptyRecentInAStashWithItemsIsNotTheEmptyStash()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        await stash.Import("Cat");
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);

        await listing.ShowScope(Scope.Recent);

        Assert.AreEqual(Scope.Recent, listing.LitChip);
        Assert.IsEmpty(listing.Items);
        Assert.IsFalse(listing.IsEmptyStash);
        Assert.IsFalse(listing.NoMatch);
    }

    [TestMethod]
    public async Task AScopeChipClearsTheSearchAndShowsThatScope()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("Cat");
        await stash.Store!.RecordUses([cat], stash.Clock.Now);
        await stash.Import("Dog");
        var listing = NewListing();
        await listing.StashOpened(stash.Store);
        await listing.SetQuery("dog");

        await listing.ShowScope(Scope.All);

        Assert.AreEqual("", listing.Query);
        Assert.HasCount(2, listing.Items);

        await listing.SetQuery("dog");
        await listing.ShowScope(Scope.Recent);

        Assert.AreEqual("", listing.Query);
        Assert.AreEqual(cat.Id, listing.Items.Single().Id);
    }

    [TestMethod]
    public async Task TheListingTellsOfItsOwnClearOfTheQueryAtOnceAndOfNothingElse()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("Cat");
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);
        var cleared = new List<string>();
        listing.QueryCleared += () => cleared.Add(listing.Query);

        await listing.SetQuery("cat");
        await listing.Refresh();
        listing.Focus(cat.Id);
        Assert.IsEmpty(cleared, "typing, a refresh and a click clear nothing");

        var scope = listing.ShowScope(Scope.All);
        CollectionAssert.AreEqual(new[] { "" }, cleared, "a scope tells of its clear before its load has run");
        await scope;
    }

    [TestMethod]
    public async Task ClearingTheSearchReturnsToTheScopeTheUserWasIn()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("Cat");
        await stash.Import("Dog");
        await stash.Store!.RecordUses([cat], stash.Clock.Now);
        var listing = NewListing();
        await listing.StashOpened(stash.Store);
        await listing.ShowScope(Scope.Recent);

        await listing.SetQuery("d");
        Assert.AreEqual(Scope.All, listing.LitChip);
        await listing.SetQuery("");

        Assert.AreEqual(Scope.Recent, listing.LitChip);
        Assert.AreEqual(cat.Id, listing.Items.Single().Id);

        await listing.ShowScope(Scope.All);
        await listing.SetQuery("c");
        await listing.SetQuery("   ");

        Assert.AreEqual(Scope.All, listing.LitChip, "spaces alone are no search: back to All, where the user was");
        Assert.HasCount(2, listing.Items);
    }

    [TestMethod]
    public async Task OnlyTheLatestLoadIsShown()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        await stash.Import("Cat");
        var dog = await stash.Import("Dog");
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);

        var first = listing.SetQuery("c");
        var second = listing.SetQuery("d");
        await Task.WhenAll(first, second);

        Assert.AreEqual("d", listing.Query);
        Assert.AreEqual(dog.Id, listing.Items.Single().Id);
    }

    [TestMethod]
    public async Task FavoritesListsTheFavoritesNewestAddedFirstAndIsTheScopeAClearedSearchReturnsTo()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        await stash.Import("c");
        await stash.Store!.SetFavorite([b, a], true);
        var listing = NewListing();
        await listing.StashOpened(stash.Store);
        await listing.SetQuery("c");

        await listing.ShowScope(Scope.Favorites);

        Assert.AreEqual("", listing.Query, "Favorites clears the search (G4)");
        CollectionAssert.AreEqual(new[] { b.Id, a.Id }, listing.Items.Select(item => item.Id).ToList(), "newest-added first (G4)");
        Assert.AreEqual(Scope.Favorites, listing.LitChip);
        Assert.AreEqual(b.Id, listing.Focused);

        await listing.SetQuery("x");
        Assert.AreEqual(Scope.All, listing.LitChip, "All is lit while a query is typed (G5)");
        await listing.SetQuery("");

        Assert.AreEqual(Scope.Favorites, listing.LitChip, "the search cleared returns to Favorites, where the user was (G5)");
        CollectionAssert.AreEqual(new[] { b.Id, a.Id }, listing.Items.Select(item => item.Id).ToList());
    }

    [TestMethod]
    public async Task NoFavoritesInAStashWithItemsIsNeitherTheEmptyStashNorNoMatchAndInAnEmptyStashItIsTheEmptyStash()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);

        await listing.ShowScope(Scope.Favorites);
        Assert.IsTrue(listing.IsEmptyStash, "an empty stash is the empty state on Favorites too (G2)");

        await stash.Import("cat");
        await listing.Refresh();

        Assert.AreEqual(Scope.Favorites, listing.Scope);
        Assert.IsEmpty(listing.Items);
        Assert.IsFalse(listing.IsEmptyStash, "the stash holds an item (G2)");
        Assert.IsFalse(listing.NoMatch, "nothing was searched (G5)");
    }

    [TestMethod]
    public async Task WhileTheStashOpensNothingIsShownAndWhatWasTypedFillsTheGridOnceItHasOpened()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("Cat");
        await stash.Import("Dog");
        var listing = NewListing();

        await listing.SetQuery("ca");

        Assert.AreEqual(StashState.Opening, listing.State);
        Assert.IsEmpty(listing.Items);
        Assert.IsFalse(listing.IsEmptyStash, "the opening is not an empty stash");
        Assert.IsFalse(listing.NoMatch);

        await listing.StashOpened(stash.Store!);

        Assert.AreEqual(StashState.Open, listing.State);
        Assert.AreEqual(cat.Id, listing.Items.Single().Id);
        Assert.AreEqual(cat.Id, listing.Focused);
    }

    [TestMethod]
    public void AStashThatDidNotOpenIsNamedAndIsNeitherEmptyNorAMatch()
    {
        var listing = NewListing();
        var failure = new Failure("Open the stash", @"C:\stash", "the folder cannot be created");

        listing.StashFailed(failure);

        Assert.AreEqual(StashState.Failed, listing.State);
        Assert.AreEqual(failure, listing.OpenFailure);
        Assert.IsFalse(listing.IsEmptyStash);
        Assert.IsFalse(listing.NoMatch);
        Assert.IsEmpty(listing.Items);
    }

    [TestMethod]
    public async Task AQueryTheStoreCannotAnswerIsNamedAndThePreviousItemsStay()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("Cat");
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);

        using (stash.HoldStore())
        {
            await listing.SetQuery("dog");
        }

        Assert.AreEqual("List the stash", failures.Single().Action);
        Assert.AreEqual(cat.Id, listing.Items.Single().Id);
        Assert.IsFalse(listing.NoMatch);
    }
}
