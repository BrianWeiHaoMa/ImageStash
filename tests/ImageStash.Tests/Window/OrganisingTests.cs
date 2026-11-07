using System.Diagnostics;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Tags and the favorite flag through the window's one organising entry over a real stash (G17): <c>Ctrl+D</c>'s toggle,
/// a mixed selection becoming all favorites; the listing refreshed with the selection kept; nothing said on success and
/// no use recorded (G10); every refusal named by item, the rest changed (G35); nothing asked of the store before it has
/// opened.
/// </summary>
[TestClass]
public sealed class OrganisingTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task AMixedSelectionBecomesAllFavoritesAndAgainNoneWithTheSelectionKeptAndNothingSaid()
    {
        using var listed = await ListedStash.Open();
        var favorite = await listed.Stash.Import("favorite");
        var plain = await listed.Stash.Import("plain");
        await listed.Stash.Import("other");
        await listed.Stash.Store!.SetFavorite([favorite], true);
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.Select([favorite.Id, plain.Id], plain.Id);

        var made = await listed.Organising.ToggleFavorite(listed.Listing.SelectedItems);

        Assert.IsTrue(made.Succeeded);
        CollectionAssert.AreEquivalent(new[] { favorite.Id, plain.Id }, await FavoriteIds(listed), "a mixed selection becomes all favorites (G17)");
        Assert.IsTrue(listed.Listing.SelectedItems.All(item => item.Favorite), "the listing shows the store's answer");
        CollectionAssert.AreEquivalent(new[] { favorite.Id, plain.Id }, listed.Listing.Selection.ToList(), "the selection kept");
        Assert.AreEqual(plain.Id, listed.Listing.Focused);

        var unmade = await listed.Organising.ToggleFavorite(listed.Listing.SelectedItems);

        Assert.IsTrue(unmade.Succeeded);
        Assert.IsEmpty(await FavoriteIds(listed), "all favorites: none stays one");
        Assert.IsEmpty(listed.Notices.Shown, "a change that succeeded says nothing more");
        Assert.AreEqual(0L, listed.Uses(), "favoriting is not a use (G10)");
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task CtrlDPressedAgainBeforeTheFirstPressFinishedTogglesAgain()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("pressed");
        await listed.Listing.ShowScope(Scope.All);

        var first = listed.Organising.ToggleFavoriteOfSelection();
        var second = listed.Organising.ToggleFavoriteOfSelection();
        await Task.WhenAll(first, second);
        var afterTwo = await FavoriteIds(listed);
        var third = listed.Organising.ToggleFavoriteOfSelection();
        var fourth = listed.Organising.ToggleFavoriteOfSelection();
        var fifth = listed.Organising.ToggleFavoriteOfSelection();
        await Task.WhenAll(third, fourth, fifth);

        Assert.IsEmpty(afterTwo, "two presses, as a held key repeats them, toggle twice (G17)");
        CollectionAssert.AreEqual(new[] { item.Id }, await FavoriteIds(listed), "and three toggle three times");
        Assert.IsTrue(listed.Listing.Items.Single().Favorite, "the listing shows the store's answer");
    }

    [TestMethod]
    public async Task CtrlDPressedTwiceOnACopyOfAnItemThatLeavesTheListingBringsItsFlagBack()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("leaving");
        await listed.Stash.Store!.SetFavorite([item], true);
        await listed.Listing.ShowScope(Scope.Favorites);
        var asPressed = listed.Listing.Items.Single();

        // Both presses hand over the copy as it was at the press; the first takes the item out of Favorites.
        var first = listed.Organising.ToggleFavorite([asPressed]);
        var second = listed.Organising.ToggleFavorite([asPressed]);
        await Task.WhenAll(first, second);

        CollectionAssert.AreEqual(new[] { item.Id }, await FavoriteIds(listed), "two toggles bring the flag back, the second deciding from the first's answer (G17)");
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task CtrlDActsOnTheFirstMatchOfWhatWasTypedNotOnWhatShowedBefore()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Stash.Import("dog");
        await listed.Listing.ShowScope(Scope.All);

        var typing = listed.Listing.SetQuery("ca");
        var toggled = await listed.Organising.ToggleFavoriteOfSelection();
        await typing;

        Assert.IsTrue(toggled.Succeeded);
        CollectionAssert.AreEqual(new[] { cat.Id }, await FavoriteIds(listed), "the selection of what was typed (G5, G7)");
    }

    [TestMethod]
    public async Task TwoCtrlDPressedInFavoritesBeforeTheFirstIsWrittenToggleThePressedItemTwiceNotTheOneThatReplacesIt()
    {
        // The store waits for its holder instead of naming it, so both presses are made before the first is written.
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(10));
        var older = await listed.Stash.Import("older", MediaFixtures.DistinctPng(4));
        var newer = await listed.Stash.Import("newer", MediaFixtures.DistinctPng(5));
        await listed.Stash.Store!.SetFavorite([older, newer], true);
        await listed.Listing.ShowScope(Scope.Favorites);
        Assert.AreEqual(newer.Id, listed.Listing.Focused);

        Task<Outcome> first, second;
        using (listed.Stash.HoldStore())
        {
            first = listed.Organising.ToggleFavoriteOfSelection();
            second = listed.Organising.ToggleFavoriteOfSelection();
        }

        Assert.IsTrue((await first).Succeeded && (await second).Succeeded);
        CollectionAssert.AreEquivalent(new[] { older.Id, newer.Id }, await FavoriteIds(listed),
            "the first press unset newer and the second set it again; older, which the first press's refresh selected, untouched (G17)");
    }

    [TestMethod]
    public async Task AnItemDeletedMeanwhileIsNamedAsNoLongerInTheStashAndTheOthersAreChanged()
    {
        using var listed = await ListedStash.Open();
        var kept = await listed.Stash.Import("kept");
        var gone = await listed.Stash.Import("gone");
        await listed.Listing.ShowScope(Scope.All);
        var selection = listed.Listing.Items.ToList();
        await listed.Deletion.Delete([gone]);
        listed.Notices.Close(listed.Notices.Shown.Single());

        var toggled = await listed.Organising.ToggleFavorite(selection);
        var tagged = await listed.Organising.AddTag(selection, "reaction");

        Assert.AreEqual("Add to Favorites failed: gone, it is no longer in the stash", toggled.Failure?.Sentence);
        Assert.AreEqual("Add a tag failed: gone, it is no longer in the stash", tagged.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { toggled.Failure!.Sentence, tagged.Failure!.Sentence }, listed.Notices.Shown.Select(notice => notice.Text).ToList(), "each named once, by item (G35)");
        CollectionAssert.AreEqual(new[] { kept.Id }, await FavoriteIds(listed), "the other item changed");
        CollectionAssert.AreEqual(new[] { "reaction" }, listed.Listing.Items.Single().Tags.ToList());
    }

    [TestMethod]
    public async Task TagsAreAddedAsTypedOnePerWordAndRemovedInAnySpellingWithTheChipsFollowing()
    {
        using var listed = await ListedStash.Open();
        var one = await listed.Stash.Import("one");
        var two = await listed.Stash.Import("two");
        await listed.Listing.ShowScope(Scope.All);

        var added = await listed.Organising.AddTag([one, two], "#Reaction happy");
        var chipsAdded = listed.Listing.Chips.Select(chip => (chip.Tag, chip.Items)).ToList();
        var removed = await listed.Organising.RemoveTag([one, two], "REACTION");

        Assert.IsTrue(added.Succeeded);
        Assert.IsTrue(removed.Succeeded);
        CollectionAssert.AreEqual(new[] { ("happy", 2), ("Reaction", 2) }, chipsAdded, "one tag per word, as typed, no #");
        CollectionAssert.AreEqual(new[] { ("happy", 2) }, listed.Listing.Chips.Select(chip => (chip.Tag, chip.Items)).ToList(), "a tag no item carries is gone (G17)");
        Assert.IsEmpty(listed.Notices.Shown);

        var nothing = await listed.Organising.AddTag([one], " # ");
        Assert.IsTrue(nothing.Succeeded, "nothing requires a tag (G17)");
        CollectionAssert.AreEqual(new[] { "happy" }, listed.Listing.Items.Single(item => item.Id == one.Id).Tags.ToList());
    }

    [TestMethod]
    public async Task NothingToChangeIsDoneWithNothingShown()
    {
        using var listed = await ListedStash.Open();

        var outcome = await listed.Organising.ToggleFavorite([]);

        Assert.IsTrue(outcome.Succeeded);
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task WhileTheStashOpensOrAfterItFailedTheItemsAreRefusedByNameAndNothingIsAskedOfTheStore()
    {
        var notices = new Notices(TimeProvider.System);
        var listing = new Listing(_ => { });
        var organising = new Organising(listing, notices);

        var opening = await organising.ToggleFavorite([TestItems.Sized(1, 10, 10)]);
        listing.StashFailed(new Failure("Open the stash", "stash", "the disk is gone"));
        var failed = await organising.AddTag(TestItems.Many(4), "dog");

        Assert.AreEqual("Add to Favorites failed: “item 1”, your stash is still opening", opening.Failure?.Sentence);
        Assert.AreEqual("Add a tag failed: 4 items, your stash could not be opened", failed.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { opening.Failure!.Sentence, failed.Failure!.Sentence }, notices.Shown.Select(notice => notice.Text).ToList());
    }

    /// <summary>
    /// E4: the flag over 5,000 selected items is one transaction and the refresh, well within a moment, so no busy state is
    /// shown and nothing is said. Its time is written, not asserted: no line binds it, and parallel checks stretch it.
    /// </summary>
    [TestMethod]
    public async Task FiveThousandSelectedItemsAreFavoritedWellWithinASecond()
    {
        using var listed = await ListedStash.Open();
        listed.Stash.Close();
        listed.Stash.AddPictures(5000);
        await listed.Reopen();
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.SelectAll();

        var clock = Stopwatch.StartNew();
        var toggled = await listed.Organising.ToggleFavoriteOfSelection();
        clock.Stop();

        TestContext.WriteLine($"Ctrl+D over 5,000 items, refresh included: {clock.Elapsed.TotalMilliseconds:F0} ms");
        Assert.IsTrue(toggled.Succeeded);
        Assert.HasCount(5000, await FavoriteIds(listed));
        Assert.HasCount(5000, listed.Listing.Selection, "the selection kept");
        Assert.IsEmpty(listed.Notices.Shown, "nothing said: the toggle shows no busy state, and raises a notice only to name a failure (G35)");
        Assert.IsEmpty(listed.Failures);
    }

    private static async Task<List<long>> FavoriteIds(ListedStash listed) => (await listed.Stash.Store!.Favorites()).Value!.Select(item => item.Id).ToList();

}
