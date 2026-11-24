using System.Diagnostics;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.ListedStash;

namespace ImageStash.Tests.Window;

/// <summary>
/// The tag chips, through the one listing model over a real stash: G4's tags carried by the most items and the list of
/// every tag, G5's chips for a typed query with its lit tags, and the inactive place of a tag no item carries (G17).
/// </summary>
[TestClass]
public sealed class ListingChipsTests
{
    private readonly List<Failure> failures = [];

    private Listing NewListing() => new(failures.Add);

    [TestMethod]
    public async Task ChipsHoldTheirOrderAndADeadTagKeepsAnInactivePlaceUntilRevivedOrReordered()
    {
        using var listed = await ListedStash.Open();
        var stash = listed.Stash;
        var items = new List<StashItem>();
        for (var i = 0; i < 6; i++)
        {
            items.Add(await stash.Import($"item {i}"));
        }

        foreach (var item in items.Take(3))
        {
            stash.Tag(item.Id, "dog");
        }

        foreach (var item in items.Skip(2).Take(3))
        {
            stash.Tag(item.Id, "cat");
        }

        stash.Tag(items[5].Id, "zebra");
        await listed.Listing.ShowScope(Scope.All);

        CollectionAssert.AreEqual(new[] { ("cat", 3), ("dog", 3), ("zebra", 1) }, Chips(listed.Listing), "by count, equal counts by spelling (G4)");
        Assert.IsEmpty(listed.Listing.LitTags);

        await listed.Organising.AddTag([items[5]], "dog aardvark");
        CollectionAssert.AreEqual(new[] { ("cat", 3), ("dog", 4), ("zebra", 1), ("aardvark", 1) }, Chips(listed.Listing), "old places stay put and a new tag joins at the end");
        CollectionAssert.AreEqual(new[] { "dog", "cat", "aardvark", "zebra" }, listed.Listing.Tags.Select(tag => tag.Tag).ToList(), "the picker uses live counts");

        var deleted = await listed.Deletion.Delete([items[5]]);
        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { ("cat", 3), ("dog", 3), ("zebra", 0), ("aardvark", 0) }, Chips(listed.Listing), "the dead tags leave inactive places");
        CollectionAssert.AreEqual(new[] { "cat", "dog" }, listed.Listing.Tags.Select(tag => tag.Tag).ToList(), "the dead tags exist only as places");

        await listed.Notices.Undo(listed.Notices.Shown.Single());
        CollectionAssert.AreEqual(new[] { ("cat", 3), ("dog", 4), ("zebra", 1), ("aardvark", 1) }, Chips(listed.Listing), "revived in the same places");
        await listed.Organising.RemoveTag([items[5]], "zebra");
        await listed.Summon(pinned: true);
        CollectionAssert.AreEqual(new[] { ("dog", 4), ("cat", 3), ("aardvark", 1) }, Chips(listed.Listing), "summoning applies the live order and drops dead places");
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task ASettledListingsSummonReadsTagsCommittedSinceItsLastRefresh()
    {
        using var listed = await FourTagged();
        var listing = listed.Listing;
        var untagged = await listed.Stash.Import("Still coming in");
        var wasSelected = listing.Focused;
        CollectionAssert.AreEqual(new[] { "dog", "cat", "fun" }, ChipTags(listing));
        var store = listed.Stash.Store!;
        var b = listing.Items.Single(item => item.Name == "B");

        Assert.IsTrue((await store.AddTag([untagged, b], "cat")).All(result => result.Succeeded));
        Assert.IsTrue(listing.Settled, "the import or tag update has not started the window's refresh");
        CollectionAssert.AreEqual(new[] { "dog", "cat", "fun" }, ChipTags(listing));
        await listed.Summon(pinned: true);

        CollectionAssert.AreEqual(new[] { "cat", "dog", "fun" }, ChipTags(listing));
        Assert.AreEqual(wasSelected, listing.Focused);
    }

    [TestMethod]
    public async Task WhileAQueryHasWordsTheRowShowsTheLitChipsThenTheTagsThatNarrowTheMatchesTheMostCarriedFirst()
    {
        using var listed = await FourTagged();
        var listing = listed.Listing;

        await listing.SetQuery("cat");
        CollectionAssert.AreEqual(new[] { "cat", "dog", "fun" }, ChipTags(listing), "G5: cat lit; dog and fun each on one of the two matches, the tie by the stash's count");
        CollectionAssert.AreEqual(new[] { "cat" }, listing.LitTags.ToList());
        Assert.AreEqual(Scope.All, listing.LitChip, "All lit beside it (G5)");
        CollectionAssert.AreEqual(new[] { "A", "D" }, Names(listing), "the matches");

        await listing.SetQuery("cat dog");
        CollectionAssert.AreEqual(new[] { "cat", "dog" }, ChipTags(listing), "both lit; fun on none of the one match, and nothing every match carries narrows");
        CollectionAssert.AreEqual(new[] { "cat", "dog" }, listing.LitTags.ToList(), "in the query's order");
        CollectionAssert.AreEqual(new[] { "A" }, Names(listing));

        await listing.SetQuery("dog");
        CollectionAssert.AreEqual(new[] { "dog", "cat", "fun" }, ChipTags(listing), "one of three each: the tie by the stash's count, then spelling");
    }

    [TestMethod]
    public async Task ATagEveryMatchCarriesOrNoMatchCarriesIsNoChipAndAWordThatIsOnlyPartOfATagListsItUnlit()
    {
        using var listed = await FourTagged();
        var listing = listed.Listing;

        await listing.SetQuery("ca");
        CollectionAssert.AreEqual(new[] { "cat", "dog", "fun" }, ChipTags(listing), "cat, which every match carries, only as the tag being typed, then the narrowing tags (G5)");
        Assert.IsEmpty(listing.LitTags, "no tag is ca");

        await listing.SetQuery("zebra");
        Assert.IsEmpty(listing.Chips, "a word no tag contains and nothing matches: no chip");

        await listing.SetQuery("#cat");
        CollectionAssert.AreEqual(new[] { "cat" }, listing.LitTags.ToList(), "a leading # is ignored (G5)");

        await listing.SetQuery("cat fun");
        CollectionAssert.AreEqual(new[] { "cat", "fun" }, ChipTags(listing), "both lit, no narrowing chip");
        CollectionAssert.AreEqual(new[] { "D" }, Names(listing));
    }

    [TestMethod]
    public async Task ATagBeingTypedStaysInTheRowAfterTheLitChipsAndBeforeTheNarrowingOnesEachTagOnce()
    {
        using var listed = await ListedStash.Open();
        var stash = listed.Stash;
        foreach (var (name, tags) in new[]
        {
            ("p1", new[] { "joy", "joyous", "alpha" }), ("p2", ["joy", "alpha"]), ("p3", ["joy", "work"]), ("p4", ["joy"]), ("p5", ["joy"]),
            ("q1", ["alpha"]), ("q2", ["alpha"]), ("q3", ["alpha", "work"]), ("q4", ["work"]),
        })
        {
            var item = await stash.Import(name);
            foreach (var tag in tags)
            {
                stash.Tag(item.Id, tag);
            }
        }

        var listing = listed.Listing;
        await listing.ShowScope(Scope.All);
        CollectionAssert.AreEqual(new[] { ("alpha", 5), ("joy", 5), ("work", 3), ("joyous", 1) }, Chips(listing), "the stash's order");

        await listing.SetQuery("jo");
        CollectionAssert.AreEqual(
            new[] { ("joy", 5), ("joyous", 1), ("alpha", 2), ("work", 1) },
            Chips(listing),
            "nothing lit; joy and joyous, which contain jo, in the stash's order (joy on every match, joyous on one), then the tags narrowing the five matches, the most carried first (G5)");
        Assert.IsEmpty(listing.LitTags);
        Assert.AreEqual(Scope.All, listing.LitChip);

        await listing.SetQuery("joy");
        CollectionAssert.AreEqual(new[] { "joy", "alpha", "work", "joyous" }, ChipTags(listing), "joy is a complete tag and joyous only joins as a narrowing tag");
        CollectionAssert.AreEqual(new[] { "joy" }, listing.LitTags.ToList());

        await listing.SetQuery("alpha #JO");
        CollectionAssert.AreEqual(new[] { "alpha", "joy", "joyous" }, ChipTags(listing), "alpha lit, then the tags containing jo, ignoring case and #; work is on neither match");
        CollectionAssert.AreEqual(new[] { "p1", "p2" }, Names(listing));

        await listing.SetQuery("alpha #OY");
        CollectionAssert.AreEqual(new[] { "alpha", "joy", "joyous" }, ChipTags(listing), "a final fragment can occur inside an existing tag");

        await listing.SetQuery("jo alpha");
        CollectionAssert.AreEqual(new[] { "alpha", "joyous" }, ChipTags(listing), "jo is no longer the final word, and joy is on every match");
        await listing.SetQuery("jo ");
        CollectionAssert.AreEqual(new[] { "alpha", "work", "joyous" }, ChipTags(listing), "with a trailing space, only narrowing tags follow");
    }

    [TestMethod]
    public void ManyTagsBeingTypedAreGroupedInTimeLinearInTheStashsTags()
    {
        // E4 bounds no item's tags: 50,000 tags, every one containing the word being typed.
        var counts = Enumerable.Range(0, 50_000).Select(i => new TagCount($"a{i:00000}", 1)).ToList();
        var clock = Stopwatch.StartNew();

        var order = new TagChipOrder();
        order.Counted(counts);
        var chips = order.Shown("a", searching: true);

        Assert.HasCount(50_000, chips, "each tag once, as a tag being typed (G5)");
        Assert.IsLessThan(2000, clock.ElapsedMilliseconds, $"well within G1's second for the row's input: {clock.ElapsedMilliseconds} ms");
    }

    [TestMethod]
    public async Task TheMostCarriedCountsTheMatchesNotTheStash()
    {
        using var listed = await ListedStash.Open();
        var stash = listed.Stash;
        var fun = new List<StashItem>();
        for (var i = 0; i < 5; i++)
        {
            fun.Add(await stash.Import($"other {i}"));
            stash.Tag(fun[i].Id, "fun");
        }

        var one = await stash.Import("match one");
        var two = await stash.Import("match two");
        var three = await stash.Import("match three");
        stash.Tag(one.Id, "fun");
        stash.Tag(two.Id, "rare");
        stash.Tag(three.Id, "rare");

        await listed.Listing.SetQuery("match");

        CollectionAssert.AreEqual(new[] { ("rare", 2), ("fun", 1) }, Chips(listed.Listing), "rare on two matches, fun on one, though the stash holds six of fun (G5)");
    }

    [TestMethod]
    public async Task ALitTagStaysLitWithNoMatchAndLeavesADeadPlaceUntilTheNextEdit()
    {
        using var listed = await FourTagged();
        var listing = listed.Listing;

        await listing.SetQuery("dog zebra");
        Assert.IsTrue(listing.NoMatch);
        CollectionAssert.AreEqual(new[] { "dog" }, ChipTags(listing), "the lit words come from the existing tags, not from the matches (G5)");
        CollectionAssert.AreEqual(new[] { "dog" }, listing.LitTags.ToList(), "so new items still get it (G11)");

        await listing.SetQuery("fun");
        var places = ChipTags(listing);
        foreach (var item in listing.Items.ToList())
        {
            await listed.Organising.RemoveTag([item], "fun");
        }

        Assert.AreEqual("fun", listing.Query, "the word stays in the box as a plain word (G17, G5)");
        Assert.IsEmpty(listing.LitTags, "a tag no item carries lights nothing");
        CollectionAssert.AreEqual(places, ChipTags(listing), "the refresh holds the typed row in place");
        Assert.AreEqual(new TagCount("fun", 0), listing.Chips.Single(chip => chip.Tag == "fun"));
        await listing.SetQuery("fu");
        CollectionAssert.DoesNotContain(ChipTags(listing), "fun", "the next edit discards dead places");
    }

    [TestMethod]
    public async Task EveryTagStaysListedWhateverTheQueryTheCommonestFirst()
    {
        using var listed = await FourTagged();

        await listed.Listing.SetQuery("ca");

        CollectionAssert.AreEqual(new[] { ("dog", 3), ("cat", 2), ("fun", 2) }, listed.Listing.Tags.Select(tag => (tag.Tag, tag.Items)).ToList(), "what the chip that lists every tag opens on (G4)");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task APendingChipReadCannotAdmitAnUnrelatedTagAndALitChipCanStillBeRemoved(bool removeCat)
    {
        using var listed = await ListedStash.Open();
        var store = listed.Stash.Store!;
        var cat = await listed.Stash.Import("one");
        var dog = await listed.Stash.Import("two");
        await store.AddTag([cat], "cat");
        await store.AddTag([dog], "dog");
        var listing = listed.Listing;
        await listing.ShowScope(Scope.All);
        var changes = listing.PersonsChanges;
        Task catRead, dogChoice, undo = Task.CompletedTask;
        using (listed.Stash.HoldStore())
        {
            catRead = listing.ChooseTag("cat");
            Assert.IsFalse(listing.Settled);
            dogChoice = listing.ChooseTag("dog");
            Assert.AreEqual("cat", listing.Query, "the previous unfiltered grid cannot license the dog click");
            if (removeCat)
            {
                undo = listing.ChooseTag("cat");
                Assert.AreEqual("", listing.Query, "the lit chip is removable before any read comes back");
                Assert.IsFalse(listing.Settled);
            }
        }

        await Task.WhenAll(catRead, dogChoice, undo);
        Assert.AreEqual(removeCat ? "" : "cat", listing.Query);
        CollectionAssert.AreEqual(removeCat ? Array.Empty<string>() : new[] { "cat" }, listing.LitTags.ToArray());
        CollectionAssert.AreEqual(removeCat ? new[] { dog.Id, cat.Id } : new[] { cat.Id }, listing.Items.Select(item => item.Id).ToArray());
        Assert.AreEqual(changes + (removeCat ? 2 : 1), listing.PersonsChanges);
        CollectionAssert.AreEqual(new[] { "cat", "dog" }, ChipTags(listing));
    }

    [TestMethod]
    public async Task EveryTagCanAddAnUnavailableTagAndBothLitTagsCanBeRemovedFromAnEmptyResult()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("one");
        var dog = await listed.Stash.Import("two");
        listed.Stash.Tag(cat.Id, "cat");
        listed.Stash.Tag(dog.Id, "dog");
        var listing = listed.Listing;
        await listing.ShowScope(Scope.All);
        await listing.ChooseTag("cat");

        Assert.IsFalse(listing.CanFilterTag("dog", searching: true, lit: false));
        await listing.ChooseTag("dog");
        Assert.AreEqual("cat", listing.Query, "the row's live dog chip is unavailable for filtering");
        await listing.ChooseTag("dog", fromPicker: true);
        Assert.IsTrue(listing.NoMatch);
        CollectionAssert.AreEqual(new[] { "cat", "dog" }, listing.LitTags.ToArray());
        await listing.ChooseTag("cat");
        Assert.AreEqual(dog.Id, listing.Items.Single().Id);
        await listing.ChooseTag("dog");
        Assert.AreEqual("", listing.Query);
        Assert.IsTrue(listing.CanFilterTag("cat", searching: false, lit: false), "clearing by clicking releases the restriction immediately");
        CollectionAssert.AreEqual(new[] { "cat", "dog" }, ChipTags(listing));
    }

    [TestMethod]
    public async Task SuccessiveCompatibleChipsClickedDuringAReadApplyInClickOrder()
    {
        using var listed = await ListedStash.Open();
        var together = await listed.Stash.Import("one");
        Assert.IsTrue((await listed.Stash.Store!.AddTag([together], "cat vacation joy")).Single().Succeeded);
        var listing = listed.Listing;
        await listing.ShowScope(Scope.All);

        Task cat, vacation, joy;
        using (listed.Stash.HoldStore())
        {
            cat = listing.ChooseTag("cat");
            Assert.IsFalse(listing.Settled);
            vacation = listing.ChooseTag("vacation");
            joy = listing.ChooseTag("joy");
        }
        await Task.WhenAll(cat, vacation, joy);

        Assert.AreEqual("cat vacation joy", listing.Query);
        CollectionAssert.AreEqual(new[] { "cat", "vacation", "joy" }, listing.LitTags.ToArray());
        Assert.AreEqual(together.Id, listing.Items.Single().Id);
    }

    [TestMethod]
    public async Task RemovingALitChipDoesNotDiscardAnEarlierCompatibleClickAwaitingItsRead()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("picture");
        Assert.IsTrue((await listed.Stash.Store!.AddTag([item], "cat dog")).Single().Succeeded);
        var listing = listed.Listing;
        await listing.ShowScope(Scope.All);

        Task first, second, undo;
        using (listed.Stash.HoldStore())
        {
            first = listing.ChooseTag("cat");
            second = listing.ChooseTag("dog");
            undo = listing.ChooseTag("cat");
            Assert.AreEqual("", listing.Query, "the lit word comes out before the earlier read is available");
        }

        await Task.WhenAll(first, second, undo);
        Assert.AreEqual("dog", listing.Query, "the earlier compatible choice still applies");
        CollectionAssert.AreEqual(new[] { "dog" }, listing.LitTags.ToArray());
        Assert.AreEqual(item.Id, listing.Items.Single().Id);
    }

    [TestMethod]
    public async Task AQueuedChipCannotCompleteAFragmentOnlyExposedByALaterRemoval()
    {
        using var listed = await ListedStash.Open();
        var carrier = await listed.Stash.Import("picture");
        await listed.Stash.Import("job");
        Assert.IsTrue((await listed.Stash.Store!.AddTag([carrier], "cat joy")).Single().Succeeded);
        var listing = listed.Listing;
        await listing.SetQuery("jo");

        Task cat, joy, undo;
        using (listed.Stash.HoldStore())
        {
            cat = listing.ChooseTag("cat");
            joy = listing.ChooseTag("joy");
            undo = listing.ChooseTag("cat");
            Assert.AreEqual("jo", listing.Query, "remove cat immediately");
        }

        await Task.WhenAll(cat, joy, undo);
        Assert.AreEqual("jo joy", listing.Query);
        Assert.AreEqual(carrier.Id, listing.Items.Single().Id);
    }

    [TestMethod]
    public async Task SummoningWaitsForAnEarlierCompatibleChipChoiceAndThenReranks()
    {
        using var listed = await ListedStash.Open();
        var carrier = await listed.Stash.Import("picture");
        Assert.IsTrue((await listed.Stash.Store!.AddTag([carrier], "cat dog zoo")).Single().Succeeded);
        var listing = listed.Listing;
        await listing.ShowScope(Scope.All);

        Task cat, dog, summoned;
        using (listed.Stash.HoldStore())
        {
            cat = listing.ChooseTag("cat");
            dog = listing.ChooseTag("dog");
            summoned = listed.Summon(pinned: true);
        }

        await Task.WhenAll(cat, dog, summoned);
        Assert.AreEqual("cat dog", listing.Query);
        Assert.AreEqual(carrier.Id, listing.Focused);
        CollectionAssert.AreEqual(new[] { "cat", "dog" }, ChipTags(listing));
    }

    [TestMethod]
    public async Task TakingOutALitTagDoesNotReopenAnExplicitlyFinishedWordForCompletion()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("picture");
        Assert.IsTrue((await listed.Stash.Store!.AddTag([item], "cat joy")).Single().Succeeded);
        var listing = listed.Listing;
        await listing.SetQuery("cat jo ");

        await listing.ChooseTag("cat");
        Assert.AreEqual("jo ", listing.Query);
        await listing.ChooseTag("joy", fromPicker: true);
        Assert.AreEqual("jo joy", listing.Query);
    }

    [TestMethod]
    public async Task TypingReranksAfterAChipClickEvenIfItsReadFinishesLate()
    {
        using var listed = await FourTagged();
        var listing = listed.Listing;
        Task picked, typed;
        using (listed.Stash.HoldStore())
        {
            picked = listing.ChooseTag("cat");
            typed = listing.SetQuery("fun cat");
        }
        await Task.WhenAll(picked, typed);

        Assert.AreEqual("fun cat", listing.Query);
        CollectionAssert.AreEqual(new[] { "fun", "cat" }, ChipTags(listing));
        CollectionAssert.AreEqual(new[] { "D" }, Names(listing));

        await listing.ChooseTag("cat");
        CollectionAssert.AreEqual(new[] { "fun", "cat" }, ChipTags(listing), "the held row keeps the two original places");
        await listed.Summon(pinned: true);
        CollectionAssert.AreEqual(new[] { "fun", "dog", "cat" }, ChipTags(listing), "summon ranks the same search afresh");
        await listing.ChooseTag("fun");
        CollectionAssert.AreEqual(new[] { "fun", "dog", "cat" }, ChipTags(listing), "clicking off the last word keeps the row");

        await listing.SetQuery("  ");
        CollectionAssert.AreEqual(new[] { "dog", "cat", "fun" }, ChipTags(listing), "a direct edit back to no words restores the unfiltered row");
        await listing.ChooseTag("cat");
        await listing.ShowScope(Scope.Favorites);
        CollectionAssert.AreEqual(new[] { "dog", "cat", "fun" }, ChipTags(listing));
        Assert.AreEqual("", listing.Query);
        Assert.AreEqual(Scope.Favorites, listing.LitChip);
    }

    [TestMethod]
    public async Task OnlyTheLatestLoadsChipsAreShown()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        stash.Tag((await stash.Import("one")).Id, "cat");
        stash.Tag((await stash.Import("two")).Id, "dog");
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);

        var first = listing.SetQuery("c");
        var second = listing.SetQuery("d");
        await Task.WhenAll(first, second);
        var favorites = listing.ShowScope(Scope.Favorites);
        var typed = listing.SetQuery("cat");
        await Task.WhenAll(favorites, typed);

        Assert.AreEqual("cat", listing.Query);
        CollectionAssert.AreEqual(new[] { ("cat", 1) }, Chips(listing));
        CollectionAssert.AreEqual(new[] { "cat" }, listing.LitTags.ToList());
        Assert.HasCount(1, listing.Items, "the query's match, not the empty Favorites it superseded");
    }

    private static List<string> ChipTags(Listing listing) => listing.Chips.Select(chip => chip.Tag).ToList();

    private static List<string> Names(Listing listing) => listing.Items.Select(item => item.Name).OrderBy(name => name).ToList();

    /// <summary>A{dog, cat}, B{dog, fun}, C{dog}, D{cat, fun}.</summary>
    private static async Task<ListedStash> FourTagged()
    {
        var listed = await ListedStash.Open();
        var stash = listed.Stash;
        foreach (var (name, tags) in new[] { ("A", new[] { "dog", "cat" }), ("B", ["dog", "fun"]), ("C", ["dog"]), ("D", ["cat", "fun"]) })
        {
            var item = await stash.Import(name);
            foreach (var tag in tags)
            {
                stash.Tag(item.Id, tag);
            }
        }

        await listed.Listing.ShowScope(Scope.All);
        return listed;
    }
}
