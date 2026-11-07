using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// G5's search: every word a case-insensitive substring of the name or of one tag, a leading # ignored on each word;
/// and how a query meets a tag alone, for the chip row: the tags that match it, and the one it is exactly. The search
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
    public async Task MatchesAreNewestAddedFirst()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var older = await stash.Import("cat one");
        stash.Clock.Now = stash.Clock.Now.AddSeconds(1);
        var newer = await stash.Import("cat two");

        var found = await stash.Store!.Search("cat");

        CollectionAssert.AreEqual(new[] { newer.Id, older.Id }, found.Value!.Select(i => i.Id).ToList());
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
