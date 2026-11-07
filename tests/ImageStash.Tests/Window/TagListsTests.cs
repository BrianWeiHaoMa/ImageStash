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
    public void RemoveTagListsEveryTagAnyItemCarriesOnceMarkedWhileAllCarryIt()
    {
        var first = TestItems.Sized(1, 100, 100) with { Tags = ["y", "x"] };
        var second = TestItems.Sized(2, 100, 100) with { Tags = ["y", "Zed"] };

        var entries = TagLists.RemoveTagEntries([first, second]);

        CollectionAssert.AreEqual(new[] { "x", "y", "Zed" }, entries.Select(entry => entry.Label).ToList(), "the union, not only the tags all carry, each once, in spelling order ignoring case (G17)");
        CollectionAssert.AreEqual(new[] { "x", "y", "Zed" }, entries.Select(entry => entry.Tag).ToList());
        CollectionAssert.AreEqual(new[] { false, true, false }, entries.Select(entry => entry.Marked).ToList(), "marked by the same rule as Add tag ▸'s");
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
        Assert.IsEmpty(listed.Listing.Chips, "and left the chip row (G4)");
        CollectionAssert.AreEqual(new[] { "Type a tag…" }, TagLists.AddTagEntries((await listed.Stash.Store!.TagCounts()).Value!, afterTaking).Select(entry => entry.Label).ToList(), "so the next opening does not list it");
        Assert.IsEmpty(listed.Notices.Shown, "a change that succeeded says nothing more");
        listed.Stash.Close();
        Assert.IsEmpty(listed.Stash.SqlQuery("SELECT item_id FROM uses", row => row.GetInt64(0)), "tagging is not a use (G10)");
    }

    [TestMethod]
    public async Task AClickAfterOneItemWasDeletedGivesTheOthersTheTagNamesTheRefusalOnceAndMarksByThoseThatRemain()
    {
        using var listed = await ListedStash.Open();
        var items = new[] { await listed.Stash.Import("kept"), await listed.Stash.Import("also kept"), await listed.Stash.Import("gone") };
        await listed.Stash.Store!.AddTag([items[2]], "late");
        await listed.Listing.ShowScope(Scope.All);
        var opened = listed.Listing.Items.ToList();
        await listed.Deletion.Delete([items[2]]);
        listed.Notices.Close(listed.Notices.Shown.Single());

        var clicked = await TagLists.Click(listed.Organising, ItemMenuAction.AddTag, opened, "late");
        var present = await listed.Organising.Present(opened);

        Assert.AreEqual("Add a tag failed: gone, it is no longer in the stash", clicked.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { clicked.Failure!.Sentence }, listed.Notices.Shown.Select(notice => notice.Text).ToList(), "named once, by item (G35)");
        CollectionAssert.AreEquivalent(new[] { items[0].Id, items[1].Id }, present.Select(item => item.Id).ToList(), "the deleted item is not among those the lists read");
        Assert.IsTrue(present.All(item => item.Tags.SequenceEqual(["late"])), "the others carry it");
        Assert.IsTrue(TagLists.AddTagEntries((await listed.Stash.Store!.TagCounts()).Value!, present).Single(entry => entry.Tag == "late").Marked, "the mark is the store's truth for the items that remain");
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
        CollectionAssert.AreEqual(new[] { "b" }, TagLists.RemoveTagEntries(afterFirst).Select(entry => entry.Tag).ToList(), "a, in any spelling, off both; the list offers b alone (G17)");
        Assert.IsEmpty(TagLists.RemoveTagEntries(afterSecond), "nothing left to take off");
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
}
