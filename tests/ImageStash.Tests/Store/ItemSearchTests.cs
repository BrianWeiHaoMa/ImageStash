using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// G5's search: every word a case-insensitive substring of the name or of one tag, a leading # ignored on each word;
/// G4's order of its matches: first those where every word starts a word, each group by latest use, then the never-used
/// newest-added first; and how a query meets a tag alone, for the chip row: the tags that match it, and the one it is
/// exactly. The search
/// checks seed tag rows through SQL on the closed store, so a spelling the store would not write can be tried too.
/// </summary>
[TestClass]
public sealed class ItemSearchTests
{
    [TestMethod]
    [DataRow("cat dog")]
    [DataRow("#cat")]
    [DataRow("dog #cat")]
    [DataRow("DOG")]
    [DataRow("  dog   cat  ")]
    public async Task EveryWordMatchesTheNameOrATag(string query)
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var dog = await stash.Import("Dog");
        await stash.Import("Other");
        await Tag(stash, (dog.Id, "cat"));

        var found = await stash.Store!.Search(query);

        CollectionAssert.AreEqual(new[] { dog.Id }, found.Value!.Select(i => i.Id).ToList());
    }

    [TestMethod]
    public async Task CaseIsIgnoredBeyondAscii()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.Import("Ärger");

        var found = await stash.Store!.Search("ärger");

        CollectionAssert.AreEqual(new[] { item.Id }, found.Value!.Select(i => i.Id).ToList());
    }

    [TestMethod]
    public async Task AWordMustLieInsideTheNameOrInsideOneTag()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var category = await stash.Import("category");
        await Tag(stash, (category.Id, "felid"), (category.Id, "ab"), (category.Id, "cd"));

        Assert.HasCount(1, (await stash.Store!.Search("cat")).Value!, "cat lies in the name");
        Assert.HasCount(1, (await stash.Store!.Search("eli")).Value!, "eli lies in the tag felid");
        Assert.IsEmpty((await stash.Store!.Search("bc")).Value!, "bc spans two tags and lies in neither");
        Assert.IsEmpty((await stash.Store!.Search("cat zebra")).Value!, "zebra matches nothing");
    }

    [TestMethod]
    public async Task MatchesListByTheirLatestUseNotHowOftenNorWhenAddedAndTheNeverUsedComeLast()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var today = stash.Clock.Now;
        stash.Clock.Now = today.AddDays(-30);
        var usedYesterday = await stash.Import("cat added first");
        stash.Clock.Now = today.AddDays(-20);
        var usedOftenLastWeek = await stash.Import("cat posted daily once");
        stash.Clock.Now = today;
        var neverUsed = await stash.Import("cat added last");
        for (var day = 14; day >= 7; day--)
        {
            await store.RecordUses([usedOftenLastWeek], today.AddDays(-day));
        }

        // Its first use came before any of the other's: only its latest puts it first.
        await store.RecordUses([usedYesterday], today.AddDays(-29));
        await store.RecordUses([usedYesterday], today.AddDays(-1));

        var found = await store.Search("cat");

        CollectionAssert.AreEqual(new[] { usedYesterday.Id, usedOftenLastWeek.Id, neverUsed.Id }, Ids(found));
    }

    [TestMethod]
    public async Task NeverUsedMatchesListNewestAddedFirstAfterTheUsedOnesAndEqualTimesPutTheLaterIdentityFirst()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var start = stash.Clock.Now;
        var usedA = await stash.Import("cat used a");
        var usedB = await stash.Import("cat used b");
        stash.Clock.Now = start.AddSeconds(2);
        var newer = await stash.Import("cat newer");
        var sameMillisecond = await stash.Import("cat same millisecond");
        // Added after the others by identity, but with the clock set back: its added time, not its identity, places it.
        stash.Clock.Now = start.AddSeconds(1);
        var older = await stash.Import("cat older");
        // One use on two items at once, as a handoff of both records it.
        await store.RecordUses([usedB, usedA], start.AddSeconds(3));

        var found = await store.Search("cat");

        CollectionAssert.AreEqual(new[] { usedB.Id, usedA.Id, sameMillisecond.Id, newer.Id, older.Id }, Ids(found));
    }

    [TestMethod]
    public async Task MatchesWhereEveryWordStartsAWordComeFirstThoughUsedLongerAgo()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bothStart = await stash.Import("cat dog");
        var oneMidWord = await stash.Import("cat hotdog");
        var neitherStarts = await stash.Import("bobcat hotdog");
        await store.RecordUses([bothStart], stash.Clock.Now.AddDays(-300));
        await store.RecordUses([oneMidWord, neitherStarts], stash.Clock.Now.AddMinutes(-1));

        var found = await store.Search("cat do");

        CollectionAssert.AreEqual(new[] { bothStart.Id, neitherStarts.Id, oneMidWord.Id }, Ids(found), "both words start a word: first; the others by latest use, then the later identity");
    }

    [TestMethod]
    public async Task ATagThatStartsWithTheWordPutsAnItemInTheFirstGroup()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var tagged = await stash.Import("Smile");
        var screenshot = await stash.Import("Screenshot 2026-11-22 10.30.00");
        stash.Tag(tagged.Id, "really");
        await store.RecordUses([screenshot], stash.Clock.Now);

        var found = await store.Search("re");

        CollectionAssert.AreEqual(new[] { tagged.Id, screenshot.Id }, Ids(found));
    }

    [TestMethod]
    public async Task ANeverUsedWordStartMatchListsBeforeAScreenshotUsedToday()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var today = stash.Clock.Now;
        stash.Clock.Now = today.AddDays(-400);
        var really = await stash.Import("really");
        stash.Clock.Now = today;
        var screenshot = await stash.Import("Screenshot 2026-11-22 10.30.00");
        await store.RecordUses([screenshot], today);

        var found = await store.Search("re");

        CollectionAssert.AreEqual(new[] { really.Id, screenshot.Id }, Ids(found));
    }

    [TestMethod]
    [DataRow("Screenshot 2026-11-22 10.30.00", "sc", true)]
    [DataRow("Screenshot 2026-11-22 10.30.00", "2026", true)]
    [DataRow("Screenshot 2026-11-22 10.30.00", "11", true)]
    [DataRow("Screenshot 2026-11-22 10.30.00", "30", true)]
    [DataRow("Screenshot 2026-11-22 10.30.00", "11-22", true)]
    [DataRow("Screenshot 2026-11-22 10.30.00", "re", false)]
    [DataRow("Screenshot 2026-11-22 10.30.00", "sh", false)]
    [DataRow("Screenshot 2026-11-22 10.30.00", "ot", false)]
    [DataRow("Screenshot 2026-11-22 10.30.00", "026", false)]
    [DataRow("big_smile", "smile", true)]
    [DataRow("really.tired", "tired", true)]
    [DataRow("Screenshot really", "re", true)]
    [DataRow("Screenshot really", "#RE", true)]
    [DataRow("Ärger", "äR", true)]
    [DataRow("Bärchen", "är", false)]
    [DataRow("bobcat", "cat", false)]
    [DataRow("cat,dog", "dog", false)]
    public void AWordStartsAtTheTextsStartAndAfterASpaceAHyphenAnUnderscoreOrAFullStop(string name, string query, bool startsAWord) =>
        Assert.AreEqual(startsAWord, ItemSearch.MatchesAtWordStarts(Named(name), ItemSearch.Words(query)));

    [TestMethod]
    [DataRow("re", new[] { "really" }, true)]
    [DataRow("re", new[] { "bored", "really" }, true)]
    [DataRow("re", new[] { "bored" }, false)]
    [DataRow("cat ti", new[] { "tired", "cat" }, true)]
    [DataRow("cat ti", new[] { "tired" }, false)]
    public void EveryWordStartsAWordOfTheNameOrOfOneTag(string query, string[] tags, bool startsWords) =>
        Assert.AreEqual(startsWords, ItemSearch.MatchesAtWordStarts(Named("Screenshot 2026-11-22 10.30.00", tags), ItemSearch.Words(query)));

    [TestMethod]
    [DataRow("cat")]
    [DataRow("#CAT")]
    public async Task TheOrderKeepsEveryMatchAndAddsNoneAMidWordMatchListingAfterTheWordStarts(string query)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bobcat = await stash.Import("bobcat");
        var cat = await stash.Import("Cat nap");
        await stash.Import("dog");
        var tagged = await stash.Import("Smile");
        var taggedMidWord = await stash.Import("Grin");
        stash.Tag(tagged.Id, "cats");
        stash.Tag(taggedMidWord.Id, "wildcat");
        await store.RecordUses([bobcat, taggedMidWord], stash.Clock.Now);

        var found = await store.Search(query);

        CollectionAssert.AreEqual(new[] { tagged.Id, cat.Id, taggedMidWord.Id, bobcat.Id }, Ids(found));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task AQueryWithNoWordsListsEveryItemNewestAddedFirstWhateverItsUses(string query)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var first = await stash.Import("first");
        stash.Clock.Now = stash.Clock.Now.AddSeconds(1);
        var second = await stash.Import("second");
        await store.RecordUses([first], stash.Clock.Now);

        var found = await store.Search(query);

        CollectionAssert.AreEqual(new[] { second.Id, first.Id }, Ids(found));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("#")]
    public void AQueryWithNoWordsIsNotASearch(string query) => Assert.IsFalse(ItemSearch.IsSearch(query));

    [TestMethod]
    [DataRow("#Dog")]
    [DataRow("dog #")]
    [DataRow("DOG")]
    [DataRow("  dog  ")]
    public void AWordEqualToATagIgnoringCaseLightsItAsStored(string query) =>
        CollectionAssert.AreEqual(new[] { "Dog" }, ItemSearch.LitTags(query, ["doggo", "Dog"]).ToList());

    [TestMethod]
    [DataRow("   ")]
    [DataRow("do")]
    [DataRow("##dog")]
    [DataRow("dogg zebra")]
    public void AQueryOfNoWordOrOfWordsEqualToNoTagLightsNothing(string query) =>
        Assert.IsEmpty(ItemSearch.LitTags(query, ["Dog", "cat", "doggo"]));

    [TestMethod]
    public void TheLitTagsComeInTheOrderOfTheirWordsEachOnce() =>
        CollectionAssert.AreEqual(new[] { "cat", "Dog" }, ItemSearch.LitTags("zebra cat #dog DOG", ["Dog", "cat", "doggo"]).ToList());

    [TestMethod]
    public void ALitTagHoldsBeyondAscii() => CollectionAssert.AreEqual(new[] { "Ärger" }, ItemSearch.LitTags("ärger", ["Ärger"]).ToList());

    [TestMethod]
    [DataRow("", "dog", "dog")]
    [DataRow("   ", "dog", "dog")]
    [DataRow("cat", "dog", "cat dog")]
    [DataRow("cat ", "dog", "cat dog")]
    [DataRow("  cat  zebra ", "dog", "  cat  zebra dog")]
    public void AChipsTagIsAddedAsTheLastWordInItsOwnSpelling(string query, string tag, string expected) =>
        Assert.AreEqual(expected, ItemSearch.WithWord(query, tag));

    [TestMethod]
    [DataRow("jo", "joy", "joy")]
    [DataRow("Jo", "joy", "joy")]
    [DataRow("#jo", "joy", "joy")]
    [DataRow("alpha jo", "joyous", "alpha joyous")]
    [DataRow("joy jo", "joyous", "joy joyous")]
    [DataRow("jo alpha", "joy", "joy alpha")]
    [DataRow("  alpha   jo ", "JOY", "alpha JOY")]
    [DataRow("# jo", "joy", "# joy")]
    [DataRow("ärg", "Ärger", "Ärger")]
    public void AChipsTagTakesThePlaceOfTheLastWordItContains(string query, string tag, string expected) =>
        Assert.AreEqual(expected, ItemSearch.WithWord(query, tag));

    [TestMethod]
    [DataRow("joy", "jo", false)]
    [DataRow("jo", "joyous", true)]
    [DataRow("alpha #JO", "joyous", true)]
    [DataRow("", "joy", false)]
    [DataRow("#", "joy", false)]
    public void ATagContainsAWordOfTheQueryWhenOneIsPartOfIt(string query, string tag, bool expected) =>
        Assert.AreEqual(expected, ItemSearch.ContainsAWord(tag, ItemSearch.Words(query)));

    [TestMethod]
    [DataRow("cat dog", "dog", "cat")]
    [DataRow("dog", "dog", "")]
    [DataRow("DOG #dog zebra", "Dog", "zebra")]
    [DataRow("  cat   dog  #fun ", "dog", "cat #fun")]
    [DataRow("doggo", "dog", "doggo")]
    [DataRow("##dog zebra", "dog", "##dog zebra")]
    public void ALitChipTakesOutEveryWordEqualToItsTagAndLeavesTheRestAsTyped(string query, string tag, string expected) =>
        Assert.AreEqual(expected, ItemSearch.WithoutWord(query, tag));

    private static List<long> Ids(Outcome<IReadOnlyList<StashItem>> found)
    {
        Assert.IsTrue(found.Succeeded, found.Failure?.ToString());
        return found.Value!.Select(i => i.Id).ToList();
    }

    private static StashItem Named(string name, params string[] tags) => TestItems.Sized(1, 8, 6) with { Name = name, Tags = tags };

    private static async Task Tag(TestStash stash, params (long Id, string Tag)[] tags)
    {
        stash.Close();
        foreach (var (id, tag) in tags)
        {
            stash.Sql("INSERT INTO tags (item_id, tag) VALUES ($id, $tag)", ("$id", id), ("$tag", tag));
        }

        await stash.OpenCleanly();
    }
}
