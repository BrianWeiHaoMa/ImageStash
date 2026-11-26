using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using ImageStash.Look;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.CaptionedItems;

namespace ImageStash.Tests.Window;

/// <summary>
/// Tags and the favorite flag through the window's one organising entry over a real stash (G17): <c>Ctrl+D</c>'s toggle,
/// a mixed selection becoming all favorites; the listing refreshed with the selection kept; changes on a selection
/// get a notice with Undo and record no use (G10); refusals are named, the rest changed (G35); nothing asked before it has
/// opened. The preview's star (G19): what it reads and says for the item it is handed, and that it shows the store's
/// answer to the preview's toggle, never its own guess (R9, G28).
/// </summary>
[TestClass]
public sealed class OrganisingTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task AMixedSelectionBecomesAllFavoritesAndAgainNoneWithTheSelectionKept()
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
        CollectionAssert.AreEqual(new[] { "Added 1 item to Favorites", "Removed 2 items from Favorites" },
            listed.Notices.Shown.Select(notice => notice.Text).ToArray());
        Assert.IsTrue(listed.Notices.Shown.All(listed.Notices.OffersUndo));
        Assert.AreEqual(0L, listed.Uses(), "favoriting is not a use (G10)");
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task RemoveTagOnAMixedSelectionReportsTheCarriersAndGridUndoRestoresOnlyThem()
    {
        using var listed = await ListedStash.Open();
        var first = await listed.Stash.Import("first");
        var second = await listed.Stash.Import("second");
        var plain = await Save(listed.Stash, await listed.Stash.ImportFixture("plain", "still.png"), Caption("work"));
        await listed.Stash.Store!.AddTag([first, second], "work");
        await listed.Listing.ShowScope(Scope.All);
        await listed.Listing.ChooseTag("work");
        listed.Listing.SelectAll();

        Assert.IsTrue((await TagLists.Click(listed.Organising, ItemMenuAction.RemoveTag, [first, second, plain, first], "work")).Succeeded);
        Assert.AreEqual("Removed #work from 2 items", listed.Notices.Shown.Single(listed.Notices.OffersUndo).Text);
        Assert.AreEqual(new TagCount("work", 0), listed.Listing.Chips.Single());
        Assert.AreEqual("work", listed.Listing.Query);
        Assert.AreEqual(plain.Id, listed.Listing.Items.Single().Id, "the saved caption still matches");
        await listed.Organising.AddTag([first], "later");
        listed.Listing.Focus(plain.Id);

        Assert.AreEqual(KeyAction.UndoNotice, await listed.Press(Key.Z, KeyModifiers.Control, KeyPlace.Grid));

        Assert.AreEqual(plain.Id, listed.Listing.SelectedItems.Single().Id);
        Assert.AreEqual(new TagCount("work", 2), listed.Listing.Chips.First());
        CollectionAssert.AreEquivalent(new[] { first.Id, second.Id, plain.Id }, listed.Listing.Items.Select(item => item.Id).ToArray());
        await listed.Reopen();
        CollectionAssert.AreEquivalent(new[] { (first.Id, "later"), (first.Id, "work"), (second.Id, "work") },
            (await listed.Stash.All()).SelectMany(item => item.Tags.Select(tag => (item.Id, tag))).ToArray());
        Assert.AreEqual(0L, listed.Uses());
    }

    [TestMethod]
    public async Task AnOlderFavoriteCardRestoresItsOwnFlagsWhileANewerTagNoticeStillOffersUndo()
    {
        using var listed = await ListedStash.Open();
        var starred = await listed.Stash.Import("starred");
        var plain = await listed.Stash.Import("plain");
        await listed.Stash.Store!.SetFavorite([starred], true);
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.SelectAll();
        var menuItems = listed.Listing.SelectedItems.ToArray();

        Assert.AreEqual(KeyAction.ToggleFavorite, await listed.Press(Key.D, KeyModifiers.Control, KeyPlace.Grid));
        var toggled = listed.Notices.Shown.Single(listed.Notices.OffersUndo);
        Assert.AreEqual("Added 1 item to Favorites", toggled.Text);
        Assert.IsTrue((await listed.ItemMenu.Act(ItemMenuAction.RemoveFromFavorites, null!, menuItems, null,
            new PasteHide(() => false), _ => { })).Succeeded);
        var menu = listed.Notices.Shown.Last(listed.Notices.OffersUndo);
        Assert.AreEqual("Removed 2 items from Favorites", menu.Text);
        await listed.Organising.AddTag(menuItems, "later");
        var tagged = listed.Notices.Shown.Last(listed.Notices.OffersUndo);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(() =>
        {
            var card = NoticeCard.For(menu, listed.Notices);
            var undo = card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Undo"));
            return listed.WaitForOrganising(() => undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        }));

        CollectionAssert.AreEquivalent(new[] { starred.Id, plain.Id }, await FavoriteIds(listed));
        Assert.IsTrue(listed.Notices.OffersUndo(tagged));
        await listed.Notices.Undo(toggled);
        CollectionAssert.AreEqual(new[] { starred.Id }, await FavoriteIds(listed));
        Assert.AreEqual(new TagCount("later", 2), listed.Listing.Tags.Single());
        Assert.AreEqual(KeyAction.UndoNotice, await listed.Press(Key.Z, KeyModifiers.Control, KeyPlace.Preview));
        Assert.IsEmpty(listed.Listing.Tags);
        Assert.AreEqual(0L, listed.Uses());
    }

    [TestMethod]
    public async Task UndoOfAFavoriteChangeAlreadyRestoredLeavesThatFlagAsItWas()
    {
        using var listed = await ListedStash.Open();
        var starred = await listed.Stash.Import("starred");
        var plain = await listed.Stash.Import("plain");
        await listed.Stash.Store!.SetFavorite([starred], true);
        await listed.Listing.ShowScope(Scope.All);

        await listed.Organising.SetFavorite([starred, plain], true);
        var older = listed.Notices.Shown.Single(listed.Notices.OffersUndo);
        await listed.Organising.SetFavorite([plain], false);
        await listed.Notices.Undo(older);

        CollectionAssert.AreEqual(new[] { starred.Id }, await FavoriteIds(listed));
    }

    [TestMethod]
    public async Task CtrlDPressedAgainBeforeTheFirstPressFinishedTogglesAgain()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("pressed");
        await listed.Listing.ShowScope(Scope.All);

        var first = listed.ToggleFavoriteOfSelection();
        var second = listed.ToggleFavoriteOfSelection();
        await Task.WhenAll(first, second);
        var afterTwo = await FavoriteIds(listed);
        var third = listed.ToggleFavoriteOfSelection();
        var fourth = listed.ToggleFavoriteOfSelection();
        var fifth = listed.ToggleFavoriteOfSelection();
        await Task.WhenAll(third, fourth, fifth);

        Assert.IsEmpty(afterTwo, "two presses, as a held key repeats them, toggle twice (G17)");
        CollectionAssert.AreEqual(new[] { item.Id }, await FavoriteIds(listed), "and three toggle three times");
        Assert.IsTrue(listed.Listing.Items.Single().Favorite, "the listing shows the store's answer");
    }

    [TestMethod]
    public async Task CtrlDUsesTheCommittedFlagsWhileTheListingStillShowsTheEarlierOnes()
    {
        using var listed = await ListedStash.Open();
        var first = await listed.Stash.Import("first");
        var second = await listed.Stash.Import("second");
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.SelectAll();
        var changed = await listed.Stash.Store!.SetFavorite([first, second], true);
        Assert.IsTrue(changed.All(outcome => outcome.Succeeded));
        Assert.IsTrue(listed.Listing.SelectedItems.All(item => !item.Favorite), "the view has yet to apply that change");

        Assert.AreEqual(KeyAction.ToggleFavorite, await listed.Press(Key.D, KeyModifiers.Control, KeyPlace.Grid));

        Assert.IsEmpty(await FavoriteIds(listed));
        Assert.AreEqual("Removed 2 items from Favorites", listed.Notices.Shown.Single(listed.Notices.OffersUndo).Text);
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
        var toggled = await listed.ToggleFavoriteOfSelection();
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
            first = listed.ToggleFavoriteOfSelection();
            second = listed.ToggleFavoriteOfSelection();
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

        Assert.AreEqual("Can't add to Favorites: gone is no longer in the stash.", toggled.Failure?.Sentence);
        Assert.AreEqual("Can't add a tag: gone is no longer in the stash.", tagged.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { toggled.Failure!.Sentence, tagged.Failure!.Sentence },
            listed.Notices.Shown.Where(notice => notice.NamesAProblem).Select(notice => notice.Text).ToArray(), "each named once, by item (G35)");
        CollectionAssert.AreEqual(new[] { kept.Id }, await FavoriteIds(listed), "the other item changed");
        CollectionAssert.AreEqual(new[] { "reaction" }, listed.Listing.Items.Single().Tags.ToList());
        var undo = listed.Notices.Shown.Where(listed.Notices.OffersUndo).ToArray();
        CollectionAssert.AreEqual(new[] { "Added 1 item to Favorites", "Tagged 1 item #reaction" }, undo.Select(notice => notice.Text).ToArray());
        Assert.HasCount(4, listed.Notices.Shown, "one committed-count notice and one refusal for each batch");
        foreach (var notice in undo)
        {
            await listed.Notices.Undo(notice);
        }

        Assert.IsFalse(listed.Listing.Items.Single().Favorite);
        Assert.IsEmpty(listed.Listing.Items.Single().Tags);
    }

    [TestMethod]
    [DataRow("lost", "gone")]
    [DataRow("reaction", "reaction")]
    public async Task UndoOfABatchNamesEachMissingItemOnceAndStillRestoresTheRemainingItem(string lostName, string goneName)
    {
        using var listed = await ListedStash.Open();
        var kept = await listed.Stash.Import("kept");
        var lost = await listed.Stash.Import(lostName, TestStash.Bytes(1));
        var gone = await listed.Stash.Import(goneName, TestStash.Bytes(2));
        await listed.Listing.ShowScope(Scope.All);
        await listed.Organising.AddTag([kept, lost, gone, lost], "work");
        var undo = listed.Notices.Shown.Single(listed.Notices.OffersUndo);
        await listed.Deletion.Delete([lost, gone]);

        await listed.Notices.Undo(undo);

        Assert.IsEmpty(listed.Listing.Items.Single().Tags);
        CollectionAssert.AreEquivalent(new[]
        {
            $"Can't undo adding a tag: {lostName} is no longer in the stash.",
            $"Can't undo adding a tag: {goneName} is no longer in the stash.",
        }, listed.Notices.Shown.Where(notice => notice.NamesAProblem).Select(notice => notice.Text).ToArray());
    }

    [TestMethod]
    public async Task ARefusedBatchNeverRaisesASuccessAndARefusedUndoNamesTheFailureOnlyOnce()
    {
        using var listed = await ListedStash.Open();
        var first = await listed.Stash.Import("first");
        var second = await listed.Stash.Import("second");
        await listed.Listing.ShowScope(Scope.All);
        var items = new[] { first, second };
        listed.Stash.SqlWhileOpen($"CREATE TRIGGER refusal BEFORE INSERT ON tags WHEN NEW.item_id = {second.Id} BEGIN SELECT RAISE(ABORT, 'refused'); END");

        var refused = await listed.Organising.AddTag(items, "work");

        Assert.IsFalse(refused.Succeeded);
        Assert.AreEqual(refused.Failure!.Sentence, listed.Notices.Shown.Single().Text);
        Assert.IsFalse(listed.Notices.OffersUndo(listed.Notices.Shown.Single()));
        Assert.IsTrue((await listed.Stash.All()).All(item => item.Tags.Count == 0));
        listed.Stash.SqlWhileOpen("DROP TRIGGER refusal");
        Assert.IsTrue((await listed.Organising.AddTag(items, "work")).Succeeded);
        var undo = listed.Notices.Shown.Single(listed.Notices.OffersUndo);
        listed.Stash.SqlWhileOpen($"CREATE TRIGGER refusal BEFORE DELETE ON tags WHEN OLD.item_id = {second.Id} BEGIN SELECT RAISE(ABORT, 'refused'); END");

        await listed.Notices.Undo(undo);

        var failure = listed.Notices.Shown.Single();
        StringAssert.StartsWith(failure.Text, "Couldn't undo adding a tag: stash.db ");
        StringAssert.Contains(failure.Text, "refused");
        Assert.IsFalse(listed.Notices.OffersUndo(failure));
        Assert.IsTrue((await listed.Stash.All()).All(item => item.Tags.SequenceEqual(["work"])));
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
        CollectionAssert.AreEqual(new[] { ("happy", 2), ("Reaction", 0) }, listed.Listing.Chips.Select(chip => (chip.Tag, chip.Items)).ToList(), "a tag no item carries leaves an inactive place");
        CollectionAssert.AreEqual(new[] { new TagCount("happy", 2) }, listed.Listing.Tags.ToArray(), "only the remaining tag is live (G17)");
        CollectionAssert.AreEqual(new[] { "Tagged 2 items #Reaction #happy", "Removed #Reaction from 2 items" },
            listed.Notices.Shown.Select(notice => notice.Text).ToArray());
        Assert.IsTrue(listed.Notices.Shown.All(listed.Notices.OffersUndo));

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

        Assert.AreEqual("Can't add to Favorites (“item 1”): your stash is still opening.", opening.Failure?.Sentence);
        Assert.AreEqual("Can't add a tag (4 items): your stash could not be opened.", failed.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { opening.Failure!.Sentence, failed.Failure!.Sentence }, notices.Shown.Select(notice => notice.Text).ToList());
    }

    /// <summary>
    /// E4: the flag over 5,000 selected items is one transaction followed by a refresh. Its time is written, not
    /// asserted: no line binds it, and parallel checks stretch it.
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
        var toggled = await listed.ToggleFavoriteOfSelection();
        clock.Stop();

        TestContext.WriteLine($"Ctrl+D over 5,000 items, refresh included: {clock.Elapsed.TotalMilliseconds:F0} ms");
        Assert.IsTrue(toggled.Succeeded);
        Assert.HasCount(5000, await FavoriteIds(listed));
        Assert.HasCount(5000, listed.Listing.Selection, "the selection kept");
        Assert.AreEqual("Added 5000 items to Favorites", listed.Notices.Shown.Single().Text);
        Assert.IsTrue(listed.Notices.OffersUndo(listed.Notices.Shown.Single()));
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public void ThePreviewsStarReadsTheFlagOfTheItemItIsHandedWithANameSayingItAndATipNamingTheActAndItsKey()
    {
        var favorite = TestItems.Sized(1, 10, 10) with { Favorite = true };
        var plain = TestItems.Sized(2, 10, 10);
        ControlThread.Run(() =>
        {
            var star = new PreviewStar(() => { });
            var accent = new SolidColorBrush(Colors.Teal);
            _ = new Border { Resources = { [LookKeys.FavoriteStar] = accent }, Child = star.Button };
            var glyph = (TextBlock)star.Button.Content!;

            star.Show(favorite);
            Assert.AreEqual((true, FavoriteStar.Filled, "In Favorites", "Remove from Favorites (Ctrl+D)"), Face(star), "filled while a favorite");
            Assert.AreSame(accent, glyph.Foreground, "in the accent (G19)");

            star.Show(plain);
            Assert.AreEqual((false, FavoriteStar.Outlined, "Not in Favorites", "Add to Favorites (Ctrl+D)"), Face(star), "moved to another item, that item's at once");
            Assert.AreNotSame(accent, glyph.Foreground, "outlined, in the button's own words");

            star.Show(null);
            Assert.IsFalse(star.Filled, "closed: outlined");
        });
    }

    [TestMethod]
    public void AClickOnThePreviewsStarAsksForTheToggleAndLeavesItAsItWasUntilAReadIsShown()
    {
        var plain = TestItems.Sized(1, 10, 10);
        ControlThread.Run(() =>
        {
            var asked = 0;
            var star = new PreviewStar(() => asked++);
            star.Show(plain);

            star.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.AreEqual(1, asked, "a click asks for the preview's toggle, as Ctrl+D there does");
            Assert.AreEqual((false, FavoriteStar.Outlined, "Not in Favorites", "Add to Favorites (Ctrl+D)"), Face(star), "nothing shows before the store's answer is read (R9, G28)");
        });
    }

    [TestMethod]
    public async Task InFavoritesThePreviewsToggleTakesItsItemOutOfTheListAndTheReadItemsStarIsOutlined()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("leaving");
        await listed.Stash.Store!.SetFavorite([item], true);
        await listed.Listing.ShowScope(Scope.Favorites);
        var shown = listed.Listing.Items.Single();

        Assert.IsTrue((await listed.Organising.ToggleFavorite([shown])).Succeeded);
        var read = (await listed.Stash.Store!.Item(item.Id)).Value!;

        Assert.IsEmpty(listed.Listing.Items, "it leaves Favorites (G4)");
        ControlThread.Run(() =>
        {
            var star = new PreviewStar(() => { });
            star.Show(shown);
            Assert.IsTrue(star.Filled);
            star.Show(read);
            Assert.IsFalse(star.Filled, "the preview, which stays open on it, reads it outlined");
        });
    }

    [TestMethod]
    public async Task APreviewToggleTheStoreRefusesLeavesTheStarAndTheTileAsTheyWereAndNamesTheFailure()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("refused");
        await listed.Listing.ShowScope(Scope.All);

        Outcome toggled;
        using (listed.Stash.HoldStore())
        {
            toggled = await listed.Organising.ToggleFavorite([item]);
        }

        Assert.AreEqual("Add to Favorites", toggled.Failure?.Action, "the failure is the act's");
        CollectionAssert.Contains(listed.Notices.Shown.Select(notice => notice.Text).ToList(), toggled.Failure!.Sentence, "and named (G35)");
        var read = (await listed.Stash.Store!.Item(item.Id)).Value!;
        await listed.Listing.Refresh();
        Assert.IsFalse(TileStar.Of(listed.Listing.Items.Single()), "the tile carries no star");
        ControlThread.Run(() =>
        {
            var star = new PreviewStar(() => { });
            star.Show(read);
            Assert.IsFalse(star.Filled, "the star reads the flag as it was");
        });
    }

    private static (bool Filled, string? Glyph, string? Name, object? Tip) Face(PreviewStar star) =>
        (star.Filled, ((TextBlock)star.Button.Content!).Text, AutomationProperties.GetName(star.Button), ToolTip.GetTip(star.Button));

    private static async Task<List<long>> FavoriteIds(ListedStash listed) => (await listed.Stash.Store!.Favorites()).Value!.Select(item => item.Id).ToList();

}
