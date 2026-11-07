using System.Diagnostics;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.ListedStash;

namespace ImageStash.Tests.Window;

/// <summary>
/// The tag chips, through the one listing model over a real stash: G4's tags carried by the most items and the list of
/// every tag, G5's chips for a typed query with its lit tags, and a tag no item carries gone (G17).
/// </summary>
[TestClass]
public sealed class ListingChipsTests
{
    private readonly List<Failure> failures = [];

    private Listing NewListing() => new(failures.Add);

    [TestMethod]
    public async Task TheChipsAreTheTagsCarriedByTheMostItemsAndATagNoItemCarriesAnyMoreIsGoneUntilItsItemIsBack()
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

        var deleted = await listed.Deletion.Delete([items[5]]);
        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { ("cat", 3), ("dog", 3) }, Chips(listed.Listing), "zebra's only item deleted: a tag no item carries is gone (G17)");

        await listed.Notices.Undo(listed.Notices.Shown.Single());
        CollectionAssert.AreEqual(new[] { ("cat", 3), ("dog", 3), ("zebra", 1) }, Chips(listed.Listing), "and back with its item");
        Assert.IsEmpty(listed.Failures);
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
        CollectionAssert.AreEqual(new[] { "joy", "joyous", "alpha", "work" }, ChipTags(listing), "joy lit, joyous after it once, though it narrows too (G5)");
        CollectionAssert.AreEqual(new[] { "joy" }, listing.LitTags.ToList());

        await listing.SetQuery("alpha #JO");
        CollectionAssert.AreEqual(new[] { "alpha", "joy", "joyous" }, ChipTags(listing), "alpha lit, then the tags containing jo, ignoring case and #; work is on neither match");
        CollectionAssert.AreEqual(new[] { "p1", "p2" }, Names(listing));
    }

    [TestMethod]
    public void ManyTagsBeingTypedAreGroupedInTimeLinearInTheStashsTags()
    {
        // E4 bounds no item's tags: 50,000 tags, every one containing the word being typed.
        var counts = Enumerable.Range(0, 50_000).Select(i => new TagCount($"a{i:00000}", 1)).ToList();
        var clock = Stopwatch.StartNew();

        var chips = Listing.ChipsFor("a", counts, []);

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
    public async Task ALitTagStaysLitWithNoMatchAndGoesOnceNoItemCarriesIt()
    {
        using var listed = await FourTagged();
        var listing = listed.Listing;

        await listing.SetQuery("dog zebra");
        Assert.IsTrue(listing.NoMatch);
        CollectionAssert.AreEqual(new[] { "dog" }, ChipTags(listing), "the lit words come from the existing tags, not from the matches (G5)");
        CollectionAssert.AreEqual(new[] { "dog" }, listing.LitTags.ToList(), "so new items still get it (G11)");

        await listing.SetQuery("fun");
        foreach (var item in listing.Items.ToList())
        {
            await listed.Organising.RemoveTag([item], "fun");
        }

        Assert.AreEqual("fun", listing.Query, "the word stays in the box as a plain word (G17, G5)");
        Assert.IsEmpty(listing.LitTags, "a tag no item carries lights nothing");
        CollectionAssert.DoesNotContain(ChipTags(listing), "fun");
    }

    [TestMethod]
    public async Task EveryTagStaysListedWhateverTheQueryTheCommonestFirst()
    {
        using var listed = await FourTagged();

        await listed.Listing.SetQuery("ca");

        CollectionAssert.AreEqual(new[] { ("dog", 3), ("cat", 2), ("fun", 2) }, listed.Listing.Tags.Select(tag => (tag.Tag, tag.Items)).ToList(), "what the chip that lists every tag opens on (G4)");
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
