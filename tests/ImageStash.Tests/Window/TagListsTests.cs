using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The item menu's tag lists over a real stash (G17): Add tag ▸ Type a tag… first, then every tag in the store's order,
/// the commonest first, each marked while every selected item carries it; Remove tag ▸ the tags the selection carries;
/// a click on Add tag ▸'s entry adding the tag to every item, or taking it off all once all carry it, decided from the
/// store and not from the copies the menu opened on, so two clicks give and take; Remove tag ▸'s click taking the tag
/// off, the list then offering the rest; a tag no item carries gone from the lists and the chips; an item deleted
/// meanwhile named once, the others changed (G35); no use recorded (G10).
/// </summary>
[TestClass]
public sealed class TagListsTests
{
    [TestMethod]
    public void AddTagListsTypeATagFirstThenEveryTagInTheStoresOrderMarkingThoseEveryItemCarries()
    {
        TagCount[] counts = [new("five", 5), new("three", 3), new("trio", 3), new("one", 1)];
        var item = TestItems.Sized(1, 100, 100) with { Tags = ["three", "TRIO"] };

        var entries = TagLists.AddTagEntries(counts, [item]);
        var none = TagLists.AddTagEntries([], [item]);

        CollectionAssert.AreEqual(new[] { "Type a tag…", "five", "three", "trio", "one" }, entries.Select(entry => entry.Label).ToList(), "G17: \"Add tag ▸ lists Type a tag… first, then every existing tag, the commonest first\"");
        CollectionAssert.AreEqual(new[] { null, "five", "three", "trio", "one" }, entries.Select(entry => entry.Tag).ToList());
        CollectionAssert.AreEqual(new[] { false, false, true, true, false }, entries.Select(entry => entry.Marked).ToList(), "the two it carries marked, in any spelling; Type a tag… never");
        CollectionAssert.AreEqual(Enumerable.Repeat(ItemMenuAction.AddTag, 4).Prepend(ItemMenuAction.TypeATag).ToList(), entries.Select(entry => entry.Action).ToList());
        CollectionAssert.AreEqual(new[] { false, true, false, false, false }, entries.Select(entry => entry.AfterSeparator).ToList(), "a separator between Type a tag… and the tags, and nowhere else");
        CollectionAssert.AreEqual(new[] { "Type a tag…" }, none.Select(entry => entry.Label).ToList(), "with no tag in the stash, Type a tag… alone");
        Assert.IsFalse(none[0].AfterSeparator, "no separator, above or below the only entry");
    }

    [TestMethod]
    public void ATagIsMarkedOnlyWhileEverySelectedItemCarriesIt()
    {
        TagCount[] counts = [new("shared", 3), new("all", 2)];
        var first = TestItems.Sized(1, 100, 100) with { Tags = ["shared", "all"] };
        var second = TestItems.Sized(2, 100, 100) with { Tags = ["shared", "all"] };
        var third = TestItems.Sized(3, 100, 100) with { Tags = ["all"] };

        var entries = TagLists.AddTagEntries(counts, [first, second, third]);

        CollectionAssert.AreEqual(new[] { false, false, true }, entries.Select(entry => entry.Marked).ToList(), "Type a tag… first and unmarked; carried by two of three is not marked; by all three, marked (G17)");
        Assert.AreEqual(ItemMenuAction.TypeATag, entries[0].Action);
        Assert.IsFalse(TagLists.AddTagEntries(counts, []).Single(entry => entry.Tag == "shared").Marked, "no items, no mark");
    }

    [TestMethod]
    public void RemoveTagListsEveryTagAnyItemCarriesOnceInTheStoresOrderWithItsCountMarkedWhileAllCarryIt()
    {
        TagCount[] counts = [new("y", 4), new("other", 3), new("Zed", 2), new("x", 2)];
        var first = TestItems.Sized(1, 100, 100) with { Tags = ["y", "x"] };
        var second = TestItems.Sized(2, 100, 100) with { Tags = ["y", "zed"] };

        var entries = TagLists.RemoveTagEntries(TagLists.Carried(counts, [first, second]), [first, second]);

        CollectionAssert.AreEqual(new[] { "y", "Zed", "x" }, entries.Select(entry => entry.Label).ToList(), "the union, not only the tags all carry, each once, the commonest first, then by spelling (G17)");
        CollectionAssert.AreEqual(new[] { "y", "Zed", "x" }, entries.Select(entry => entry.Tag).ToList());
        CollectionAssert.AreEqual(new int?[] { 4, 2, 2 }, entries.Select(entry => entry.Count).ToList(), "each with the number of items in the stash that carry it, not of the selection");
        CollectionAssert.AreEqual(new[] { true, false, false }, entries.Select(entry => entry.Marked).ToList(), "marked by the same rule as Add tag ▸'s");
        Assert.IsTrue(entries.All(entry => entry.Action == ItemMenuAction.RemoveTag && !entry.AfterSeparator));
    }

    [TestMethod]
    public async Task AClickGivesTheTagToEveryItemAndASecondTakesItOffAllDecidedFromTheStoreNotTheMenusCopies()
    {
        using var listed = await ListedStash.Open();
        var items = new[] { await listed.Stash.Import("one"), await listed.Stash.Import("two"), await listed.Stash.Import("three") };
        await listed.Stash.Store!.AddTag(items[..2], "half");
        await listed.Listing.ShowScope(Scope.All);
        var opened = await listed.Organising.Present(items);
        var before = TagLists.AddTagEntries((await listed.Stash.Store!.TagCounts()).Value!, opened).Single(entry => entry.Tag == "half");

        var given = await TagLists.Click(listed.Organising, ItemMenuAction.AddTag, opened, "half");
        var afterGiving = TagLists.AddTagEntries((await listed.Stash.Store!.TagCounts()).Value!, await listed.Organising.Present(opened)).Single(entry => entry.Tag == "half");
        var taken = await TagLists.Click(listed.Organising, ItemMenuAction.AddTag, opened, "half");
        var afterTaking = await listed.Organising.Present(opened);

        Assert.IsFalse(before.Marked, "carried by two of the three: unmarked");
        Assert.IsTrue(given.Succeeded, given.Failure?.Sentence);
        Assert.IsTrue(afterGiving.Marked, "every one carries it now, so it shows its mark (G17)");
        Assert.IsTrue(taken.Succeeded, taken.Failure?.Sentence);
        Assert.IsTrue(afterTaking.All(item => !item.Tags.Contains("half")), "the second click, on the same copies the menu opened on, took it off all three (G17)");
        Assert.IsFalse(TagLists.AddTagEntries([new("half", 0)], afterTaking).Single(entry => entry.Tag == "half").Marked);
        Assert.IsEmpty((await listed.Stash.Store!.TagCounts()).Value!, "carried by none, the tag ceased to exist (G17)");
        Assert.AreEqual(new TagCount("half", 0), listed.Listing.Chips.Single(), "the chip row holds its inactive place");
        CollectionAssert.AreEqual(new[] { "Type a tag…" }, TagLists.AddTagEntries((await listed.Stash.Store!.TagCounts()).Value!, afterTaking).Select(entry => entry.Label).ToList(), "so the next opening does not list it");
        CollectionAssert.AreEqual(new[] { "Tagged 1 item #half", "Removed #half from 3 items" },
            listed.Notices.Shown.Select(notice => notice.Text).ToArray());
        Assert.IsTrue(listed.Notices.Shown.All(listed.Notices.OffersUndo));
        listed.Stash.Close();
        Assert.IsEmpty(listed.Stash.SqlQuery("SELECT item_id FROM uses", row => row.GetInt64(0)), "tagging is not a use (G10)");
    }

    [TestMethod]
    public async Task AClickAfterOneItemWasDeletedGivesTheOthersTheTagNamesTheRefusalOnceAndMarksByThoseThatRemain()
    {
        using var listed = await ListedStash.Open();
        var items = new[] { await listed.Stash.Import("kept"), await listed.Stash.Import("also kept"), await listed.Stash.Import("gone") };
        var otherCarrier = await listed.Stash.Import("other carrier");
        await listed.Stash.Store!.AddTag([items[2], otherCarrier], "late");
        await listed.Listing.ShowScope(Scope.All);
        var opened = await listed.Organising.Present(items);
        await listed.Deletion.Delete([items[2]]);
        listed.Notices.Close(listed.Notices.Shown.Single());

        var clicked = await TagLists.Click(listed.Organising, ItemMenuAction.AddTag, opened, "late");
        var present = await listed.Organising.Present(opened);

        Assert.AreEqual("Can't add a tag: gone is no longer in the stash.", clicked.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { clicked.Failure!.Sentence },
            listed.Notices.Shown.Where(notice => notice.NamesAProblem).Select(notice => notice.Text).ToArray(), "named once, by item (G35)");
        Assert.AreEqual("Tagged 2 items #late", listed.Notices.Shown.Single(listed.Notices.OffersUndo).Text);
        Assert.HasCount(2, listed.Notices.Shown, "the change and the refusal");
        CollectionAssert.AreEquivalent(new[] { items[0].Id, items[1].Id }, present.Select(item => item.Id).ToList(), "the deleted item is not among those the lists read");
        Assert.IsTrue(present.All(item => item.Tags.SequenceEqual(["late"])), "the others carry it");
        Assert.IsTrue(TagLists.AddTagEntries((await listed.Stash.Store!.TagCounts()).Value!, present).Single(entry => entry.Tag == "late").Marked, "the mark is the store's truth for the items that remain");
    }

    [TestMethod]
    public async Task AStaleAddTagEntryCannotRestoreATagThatLostItsLastCarrier()
    {
        using var listed = await ListedStash.Open();
        var target = await listed.Stash.Import("target");
        var carrier = await listed.Stash.Import("carrier");
        var store = listed.Stash.Store!;
        Assert.IsTrue((await store.AddTag([carrier], "picked")).Single().Succeeded);
        var entry = TagLists.AddTagEntries((await store.TagCounts()).Value!, [target]).Single(entry => entry.Tag == "picked");
        Assert.IsTrue((await store.RemoveTag([carrier], "picked")).Single().Succeeded);

        var picked = await TagLists.Click(listed.Organising, entry.Action, [target], entry.Tag!);

        Assert.AreEqual("Can't add a tag (target): no item carries #picked any more.", picked.Failure?.Sentence);
        Assert.IsEmpty((await store.TagCounts()).Value!);
    }

    [TestMethod]
    public async Task AToggleWhoseMembershipReadIsRefusedNamesTheTag()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.Zero);
        var first = await listed.Stash.Import("one");
        var second = await listed.Stash.Import("two");
        listed.Stash.Tag(first.Id, "picked");
        await listed.Listing.ShowScope(Scope.All);
        var selected = listed.Listing.Items;
        var entry = TagLists.AddTagEntries(listed.Listing.Tags, selected).Single(tag => tag.Tag == "picked");

        using (listed.Stash.HoldStore())
        {
            var refused = await TagLists.Click(listed.Organising, entry.Action, selected, entry.Tag!);
            Assert.AreEqual("Add a tag", refused.Failure?.Action);
            Assert.AreEqual("#picked", refused.Failure?.Subject);
        }

        Assert.AreEqual(1, (await listed.Stash.Store!.TagCounts()).Value!.Single().Items);
        Assert.HasCount(2, listed.Listing.Items);
        Assert.IsEmpty((await listed.Stash.Store.Item(second.Id)).Value!.Tags);
    }

    [TestMethod]
    public async Task AddTagClickReadsTheMembershipJustGivenByADuplicateImport()
    {
        using var listed = await ListedStash.Open();
        var bytes = TestStash.Bytes(7);
        var first = await listed.Stash.Import("first", bytes);
        var second = await listed.Stash.Import("second", TestStash.Bytes(8));
        var store = listed.Stash.Store!;
        await store.AddTag([second], "picked");
        await listed.Listing.ShowScope(Scope.All);
        var opened = listed.Listing.Items.ToArray();
        var staged = await listed.Stash.Stage("duplicate.png", bytes);
        var imported = await store.CommitImport(TestStash.Candidate(staged, "duplicate", bytes) with { Tags = ["picked"] });
        Assert.IsTrue(imported.Succeeded && imported.Value!.AlreadyInStash);
        Assert.IsFalse(opened.Single(item => item.Id == first.Id).Tags.Contains("picked"));

        Assert.IsTrue((await TagLists.Click(listed.Organising, ItemMenuAction.AddTag, opened, "picked")).Succeeded);

        Assert.IsTrue((await listed.Stash.All()).All(item => item.Tags.Count == 0));
        Assert.AreEqual("Removed #picked from 2 items", listed.Notices.Shown.Single(listed.Notices.OffersUndo).Text);
    }

    [TestMethod]
    public async Task RemoveTagsClickTakesTheTagOffEveryItemAndTheListThenOffersTheRestUntilNothingIsCarried()
    {
        using var listed = await ListedStash.Open();
        var items = new[] { await listed.Stash.Import("one"), await listed.Stash.Import("two") };
        await listed.Stash.Store!.AddTag([items[0]], "a b");
        await listed.Stash.Store!.AddTag([items[1]], "a");
        await listed.Listing.ShowScope(Scope.All);
        var opened = listed.Listing.Items.ToList();

        var first = await TagLists.Click(listed.Organising, ItemMenuAction.RemoveTag, opened, "A");
        var afterFirst = await listed.Organising.Present(opened);
        var second = await TagLists.Click(listed.Organising, ItemMenuAction.RemoveTag, opened, "b");
        var afterSecond = await listed.Organising.Present(opened);

        Assert.IsTrue(first.Succeeded && second.Succeeded);
        CollectionAssert.AreEqual(new[] { "b" }, TagLists.Carried([new("a", 2), new("b", 1)], afterFirst).Select(count => count.Tag).ToList(), "a, in any spelling, off both; a new opening offers b alone (G17)");
        Assert.IsEmpty(TagLists.Carried([new("a", 2), new("b", 1)], afterSecond), "nothing left to take off");
        Assert.IsFalse(ItemMenu.Entries(afterSecond, null).Any(entry => entry.Action == ItemMenuAction.RemoveTag), "so the menu's next opening has no Remove tag ▸");
        Assert.IsEmpty((await listed.Stash.Store!.TagCounts()).Value!);
    }

    [TestMethod]
    public async Task AddTagListsEveryTagOfALargeStashAsTheStoreHoldsItWhenTheMenuOpens()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("many");
        var sixty = Enumerable.Range(1, 60).Select(number => $"tag{number:00}").ToList();
        await listed.Stash.Store!.AddTag([item], string.Join(' ', sixty));
        await listed.Stash.Store!.AddTag([await listed.Stash.Import("other")], "tag60 fresh");

        var before = TagLists.AddTagEntries((await listed.Stash.Store!.TagCounts()).Value!, await listed.Organising.Present([item]));
        await listed.Stash.Store!.RemoveTag([item], "tag01");
        var after = TagLists.AddTagEntries((await listed.Stash.Store!.TagCounts()).Value!, await listed.Organising.Present([item]));

        Assert.HasCount(62, before, "Type a tag…, then all sixty-one tags: no cap on the list (G17)");
        Assert.AreEqual("Type a tag…", before[0].Label, "G17: \"Add tag ▸ lists Type a tag… first\"");
        Assert.AreEqual("tag60", before[1].Label, "carried by two items, it comes first among the tags");
        CollectionAssert.AreEqual(sixty.Take(59).Prepend("fresh").Prepend("tag60").Prepend("Type a tag…").ToList(), before.Select(entry => entry.Label).ToList(), "then by spelling");
        Assert.IsFalse(before.Single(entry => entry.Tag == "fresh").Marked, "a tag the item does not carry is listed unmarked");
        Assert.IsTrue(before.Where(entry => entry.Tag?.StartsWith("tag") == true).All(entry => entry.Marked));
        Assert.IsFalse(after.Any(entry => entry.Tag == "tag01"), "once no item carries it, the tag is not listed (G17)");
    }

    [TestMethod]
    public async Task UntilTheMenusFirstReadLandsAddTagShowsTypeATagAloneThenEveryTagWithItsCount()
    {
        using var listed = await ListedStash.Open();
        var selected = await listed.Stash.Import("selected");
        await listed.Stash.Store!.AddTag([selected], "mine");
        await listed.Stash.Store!.AddTag([await listed.Stash.Import("other")], "theirs mine");
        await listed.Listing.ShowScope(Scope.All);
        var lists = new ItemMenuTagLists(listed.Listing, listed.Notices, listed.Organising, Listed(listed, selected));

        CollectionAssert.AreEqual(new[] { "Type a tag…" }, lists.AddTag.Select(entry => entry.Label).ToList(), "nothing offered before the read");
        Assert.IsTrue(await lists.Read());

        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("mine", 2, true), ("theirs", 1, false) }, Rows(lists.AddTag));
        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("mine", 2, true) }, Rows(lists.RemoveTag));
        Assert.AreEqual("mine", lists.AddTag[1].Label, "an entry's words are the tag's alone, as scripts find it; the number is its own");
    }

    [TestMethod]
    public async Task AClickBreakingATieInAddTagLeavesTheRowsInTheOrderTheyOpenedWithItsNewCountInPlace()
    {
        using var listed = await ListedStash.Open();
        var selected = await listed.Stash.Import("selected");
        await listed.Stash.Store!.AddTag([await listed.Stash.Import("a")], "alpha");
        await listed.Stash.Store!.AddTag([await listed.Stash.Import("b")], "beta");
        var lists = await Opened(listed, selected);
        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("alpha", 1, false), ("beta", 1, false) }, Rows(lists.AddTag));

        Assert.IsTrue((await lists.Click(ItemMenuAction.AddTag, "beta")).Succeeded);

        CollectionAssert.AreEqual(new[] { "beta", "alpha" }, (await listed.Stash.Store!.TagCounts()).Value!.Select(count => count.Tag).ToList(), "the store now counts beta first");
        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("alpha", 1, false), ("beta", 2, true) }, Rows(lists.AddTag), "the rows hold the order they opened with; the row clicked shows its new count and mark (G17)");
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public async Task ATagMadeWhileOpenJoinsTheEndAndEveryCountIsTheStashsAsTheListOfEveryTagShowsIt(int selectedCount)
    {
        using var listed = await ListedStash.Open();
        var a = await listed.Stash.Import("a");
        var b = await listed.Stash.Import("b");
        var selected = new[] { await listed.Stash.Import("first selected"), await listed.Stash.Import("second selected") }[..selectedCount];
        await listed.Stash.Store!.AddTag([a, b], "beta");
        await listed.Stash.Store!.AddTag([a], "alpha");
        var lists = await Opened(listed, selected);

        await listed.Organising.AddTag([a, b, .. selected], "zeta");
        Assert.IsTrue(await lists.Read());
        Assert.IsTrue((await lists.Click(ItemMenuAction.AddTag, "alpha")).Succeeded);

        CollectionAssert.AreEqual(new[] { "beta", "alpha", "zeta" }, Rows(lists.AddTag).Select(row => row.Item1).ToList(), "zeta, the commonest now, joins the end; the others do not move (G17)");
        var stash = (await listed.Stash.Store!.TagCounts()).Value!.ToDictionary(count => count.Tag, count => (int?)count.Items);
        CollectionAssert.AreEqual(new int?[] { stash["beta"], stash["alpha"], stash["zeta"] }, Rows(lists.AddTag).Select(row => row.Item2).ToList(), "each the number of items in the stash that carry it, not of the selection");
        CollectionAssert.AreEqual(new int?[] { 2, 1 + selectedCount, 2 + selectedCount }, Rows(lists.AddTag).Select(row => row.Item2).ToList());
        var everyTag = listed.Listing.Tags.ToDictionary(count => count.Tag, count => (int?)count.Items);
        CollectionAssert.AreEqual(Rows(lists.AddTag).Select(row => everyTag[row.Item1!]).ToList(), Rows(lists.AddTag).Select(row => row.Item2).ToList(), "the one number the list of every tag shows");
        CollectionAssert.AreEqual(new[] { "zeta", "alpha" }, Rows(lists.RemoveTag).Select(row => row.Item1).ToList(), "Remove tag ▸ gains each tag the items come to carry, at its end");
    }

    [TestMethod]
    public async Task ATagAddTagTookOffItsLastItemStaysReadingNoneAndTheNextClickGivesItBack()
    {
        using var listed = await ListedStash.Open();
        var selected = await listed.Stash.Import("selected");
        await listed.Stash.Store!.AddTag([selected], "solo");
        await listed.Stash.Store!.AddTag([await listed.Stash.Import("other")], "kept");
        var lists = await Opened(listed, selected);

        Assert.IsTrue((await lists.Click(ItemMenuAction.AddTag, "solo")).Succeeded);
        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("kept", 1, false), ("solo", 0, false) }, Rows(lists.AddTag), "the row stays, reading 0, unmarked");
        Assert.IsEmpty((await listed.Stash.Store!.TagCounts()).Value!.Where(count => count.Tag == "solo"), "no item carries it now");

        Assert.IsTrue((await lists.Click(ItemMenuAction.AddTag, "solo")).Succeeded, "given back, not refused as a tag no item carries");
        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("kept", 1, false), ("solo", 1, true) }, Rows(lists.AddTag));
        CollectionAssert.AreEqual(new[] { "solo" }, (await listed.Organising.Present([selected])).Single().Tags.ToList());
    }

    [TestMethod]
    public async Task ATagAnotherActEmptiedKeepsItsRowReadingNoneAndAClickIsRefused()
    {
        using var listed = await ListedStash.Open();
        var selected = await listed.Stash.Import("selected");
        var carrier = await listed.Stash.Import("carrier");
        await listed.Stash.Store!.AddTag([carrier], "gone");
        var lists = await Opened(listed, selected);

        Assert.IsTrue((await listed.Organising.RemoveTag([carrier], "gone")).Succeeded);
        Assert.IsTrue(await lists.Read());
        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("gone", 0, false) }, Rows(lists.AddTag), "the row stays, reading 0");
        var clicked = await lists.Click(ItemMenuAction.AddTag, "gone");

        Assert.AreEqual("Can't add a tag (selected): no item carries #gone any more.", clicked.Failure?.Sentence, "refused as before: only a tag this list emptied is given back (G17, G35)");
        Assert.IsEmpty((await listed.Stash.Store!.TagCounts()).Value!);
        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("gone", 0, false) }, Rows(lists.AddTag));
    }

    [TestMethod]
    public async Task RemoveTagKeepsTheEntryClickedUnmarkedInItsPlaceAndItsNextClickGivesTheTagBack()
    {
        using var listed = await ListedStash.Open();
        var selected = await listed.Stash.Import("selected");
        var a = await listed.Stash.Import("a");
        await listed.Stash.Store!.AddTag([selected, a, await listed.Stash.Import("b")], "x");
        await listed.Stash.Store!.AddTag([selected, a], "y");
        await listed.Stash.Store!.AddTag([selected], "z");
        var lists = await Opened(listed, selected);
        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("x", 3, true), ("y", 2, true), ("z", 1, true) }, Rows(lists.RemoveTag), "the commonest first, each with its count");

        Assert.IsTrue((await lists.Click(ItemMenuAction.RemoveTag, "x")).Succeeded);
        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("x", 2, false), ("y", 2, true), ("z", 1, true) }, Rows(lists.RemoveTag), "three rows in order, the first unmarked: nothing slides under the pointer (G17)");
        Assert.IsTrue((await lists.Click(ItemMenuAction.RemoveTag, "x")).Succeeded);

        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("x", 3, true), ("y", 2, true), ("z", 1, true) }, Rows(lists.RemoveTag), "the second click gave it back");
        CollectionAssert.AreEquivalent(new[] { "x", "y", "z" }, (await listed.Organising.Present([selected])).Single().Tags.ToList());
    }

    [TestMethod]
    public async Task RemoveTagGivesATagItTookOffItsLastItemsBackToEverySelectedItem()
    {
        using var listed = await ListedStash.Open();
        var selected = new[] { await listed.Stash.Import("first"), await listed.Stash.Import("second") };
        await listed.Stash.Store!.AddTag(selected, "pair");
        var lists = await Opened(listed, selected);

        Assert.IsTrue((await lists.Click(ItemMenuAction.RemoveTag, "pair")).Succeeded);
        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("pair", 0, false) }, Rows(lists.RemoveTag), "kept, reading 0, though the items carry nothing now");
        Assert.IsTrue((await lists.Click(ItemMenuAction.RemoveTag, "pair")).Succeeded);

        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("pair", 2, true) }, Rows(lists.RemoveTag));
        Assert.IsTrue((await listed.Organising.Present(selected)).All(item => item.Tags.SequenceEqual(["pair"])), "every selected item carries it again");
    }

    /// <summary>The selected items as the window lists them.</summary>
    private static List<StashItem> Listed(ListedStash listed, params StashItem[] selected) =>
        listed.Listing.Items.Where(item => selected.Any(chosen => chosen.Id == item.Id)).ToList();

    /// <summary>One item menu's tag lists on <paramref name="selected"/>, as the window shows All, once its first read has landed.</summary>
    private static async Task<ItemMenuTagLists> Opened(ListedStash listed, params StashItem[] selected)
    {
        await listed.Listing.ShowScope(Scope.All);
        var lists = new ItemMenuTagLists(listed.Listing, listed.Notices, listed.Organising, Listed(listed, selected));
        Assert.IsTrue(await lists.Read());
        return lists;
    }

    /// <summary>A list's tag entries as they read: tag, count, mark.</summary>
    private static (string?, int?, bool)[] Rows(IReadOnlyList<ItemMenuEntry> entries) =>
        [.. entries.Where(entry => entry.Tag is not null).Select(entry => (entry.Tag, entry.Count, entry.Marked))];

    [TestMethod]
    public async Task ASecondClickMadeBeforeTheFirstIsAnsweredGivesBackTheTagTheFirstTookOffItsLastItem()
    {
        using var listed = await ListedStash.Open();
        var selected = await listed.Stash.Import("selected");
        await listed.Stash.Store!.AddTag([selected], "solo");
        var lists = await Opened(listed, selected);

        var first = lists.Click(ItemMenuAction.AddTag, "solo");
        var second = lists.Click(ItemMenuAction.AddTag, "solo");
        await Task.WhenAll(first, second);

        Assert.IsTrue(first.Result.Succeeded && second.Result.Succeeded, second.Result.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { "solo" }, (await listed.Organising.Present([selected])).Single().Tags.ToList(), "taken off, then given back: not refused as a tag no item carries (G17)");
        Assert.IsTrue(await lists.Read());
        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("solo", 1, true) }, Rows(lists.AddTag));
    }

    [TestMethod]
    public async Task AnOpenListShowsATagsCorrectedSpellingInItsPlace()
    {
        using var listed = await ListedStash.Open();
        var selected = await listed.Stash.Import("selected");
        await listed.Stash.Store!.AddTag([selected], "cat");
        await listed.Stash.Store!.AddTag([await listed.Stash.Import("other")], "dog");
        var lists = await Opened(listed, selected);

        Assert.IsTrue((await listed.Stash.Store!.RenameTag("cat", "Cat")).Succeeded);
        Assert.IsTrue(await lists.Read());

        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("Cat", 1, true), ("dog", 1, false) }, Rows(lists.AddTag), "the spelling the store now holds, in the same place (G17)");
        CollectionAssert.AreEqual(new (string?, int?, bool)[] { ("Cat", 1, true) }, Rows(lists.RemoveTag));
    }
}
