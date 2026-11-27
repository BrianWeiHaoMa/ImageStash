using Avalonia.Input;
using Avalonia.Threading;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.TagMenuControls;

namespace ImageStash.Tests.Window;

/// <summary>
/// A tag renamed to a different word offers Undo on its notice, which gives each item back the tags it carried, a merge
/// included, and carries into the search: each word that is the renamed tag becomes the new name, its chip lit in its
/// place, and Undo puts back the words the rename changed and no others (G17, G5, G4).
/// </summary>
public sealed partial class TagRenameTests
{
    [TestMethod]
    public async Task UndoOfAMergeGivesEachItemBackTheTagsItCarried()
    {
        using var listed = await ListedStash.Open();
        var (a, b, c) = (await listed.Stash.Import("a", TestStash.Bytes(1)), await listed.Stash.Import("b", TestStash.Bytes(2)), await listed.Stash.Import("c", TestStash.Bytes(3)));
        listed.Stash.Tag(a.Id, "lol");
        listed.Stash.Tag(b.Id, "lol");
        listed.Stash.Tag(b.Id, "lmao");
        listed.Stash.Tag(c.Id, "lmao");
        await listed.Listing.ShowScope(Scope.All);

        await listed.Organising.RenameTag(listed.Listing.Store!, "lol", "lmao");

        var notice = listed.Notices.Shown.Single();
        Assert.AreEqual("Merged #lol into #lmao", notice.Text);
        Assert.IsTrue(listed.Notices.OffersUndo(notice), "a change to many items offers Undo");
        foreach (var item in new[] { a, b, c })
        {
            CollectionAssert.AreEqual(new[] { "lmao" }, (await Stored(listed, item)).Tags.ToArray());
        }

        await listed.WaitForOrganising(() => _ = listed.Notices.Undo(notice));

        CollectionAssert.AreEqual(new[] { "lol" }, (await Stored(listed, a)).Tags.ToArray(), "who carried what is put back");
        CollectionAssert.AreEquivalent(new[] { "lol", "lmao" }, (await Stored(listed, b)).Tags.ToArray());
        CollectionAssert.AreEqual(new[] { "lmao" }, (await Stored(listed, c)).Tags.ToArray());
        Assert.IsEmpty(listed.Notices.Shown.Where(shown => shown != notice && !listed.Notices.OffersUndo(shown) && shown.Text.StartsWith("Can't")).ToList(), "nothing refused");
    }

    [TestMethod]
    public async Task UndoOfARenameGivesTheOldTagBackAndACaseChangeKeepsItsPlainNotice()
    {
        using var listed = await ListedStash.Open();
        var (a, b) = (await listed.Stash.Import("a", TestStash.Bytes(1)), await listed.Stash.Import("b", TestStash.Bytes(2)));
        listed.Stash.Tag(a.Id, "cat");
        listed.Stash.Tag(b.Id, "cat");
        await listed.Listing.ShowScope(Scope.All);

        await listed.Organising.RenameTag(listed.Listing.Store!, "cat", "kitty");
        var notice = listed.Notices.Shown.Single();
        Assert.AreEqual("Renamed #cat to #kitty", notice.Text);
        Assert.IsTrue(listed.Notices.OffersUndo(notice));
        Assert.IsTrue((await listed.Stash.Store!.Delete([b.Id])).Single().Succeeded, "an item changed since the rename");
        await listed.WaitForOrganising(() => _ = listed.Notices.Undo(notice));

        CollectionAssert.AreEqual(new[] { "cat" }, (await Stored(listed, a)).Tags.ToArray(), "the former carrier carries the old tag again");
        Assert.IsFalse(listed.Listing.Tags.Any(tag => tag.Tag == "kitty"), "and the new one is on no item listed");

        await listed.Organising.RenameTag(listed.Listing.Store!, "cat", "CAT");
        var recased = listed.Notices.Shown.Last();
        Assert.AreEqual("Renamed #cat to #CAT", recased.Text);
        Assert.IsFalse(listed.Notices.OffersUndo(recased), "a change of case alone: renaming back undoes it");
    }

    [TestMethod]
    public async Task ARenameCarriesIntoTheSearchAndUndoPutsBackOnlyTheWordsItChanged()
    {
        using var listed = await ListedStash.Open();
        var one = await listed.Stash.Import("match one", TestStash.Bytes(1));
        var two = await listed.Stash.Import("match two", TestStash.Bytes(2));
        var named = await listed.Stash.Import("match category", TestStash.Bytes(3));
        listed.Stash.Tag(one.Id, "cat");
        listed.Stash.Tag(one.Id, "dog");
        listed.Stash.Tag(two.Id, "cat");
        await listed.Listing.ShowScope(Scope.All);
        await listed.Listing.SetQuery("match cat");
        await listed.Listing.Settle();
        listed.Listing.Select([two.Id], two.Id);
        var place = listed.Listing.Chips.ToList().FindIndex(chip => chip.Tag == "cat");
        var neighbours = listed.Listing.Chips.Where(chip => chip.Tag != "cat").Select(chip => chip.Tag).ToList();
        CollectionAssert.Contains(listed.Listing.Items.Select(item => item.Id).ToList(), named.Id, "matched by its name alone");

        await listed.Organising.RenameTag(listed.Listing.Store!, "cat", "kitty");
        await listed.Listing.Settle();

        Assert.AreEqual("match kitty", listed.Listing.Query, "the window changed the word");
        CollectionAssert.AreEqual(new[] { "kitty" }, listed.Listing.LitTags.ToArray(), "its chip stays lit");
        Assert.AreEqual(place, listed.Listing.Chips.ToList().FindIndex(chip => chip.Tag == "kitty"), "where #cat stood");
        CollectionAssert.AreEqual(neighbours, listed.Listing.Chips.Where(chip => chip.Tag != "kitty").Select(chip => chip.Tag).ToList(), "each neighbour where it was");
        CollectionAssert.AreEquivalent(new[] { one.Id, two.Id }, listed.Listing.Items.Select(item => item.Id).ToArray(), "every item that carried it, the name-only match gone (G5)");
        CollectionAssert.AreEqual(new[] { two.Id }, listed.Listing.Selection.ToArray(), "the selected item still listed stays selected");

        await listed.WaitForOrganising(() => _ = listed.Notices.Undo(listed.Notices.Shown.Single()));
        await listed.Listing.Settle();

        Assert.AreEqual("match cat", listed.Listing.Query, "the word the rename changed is back");
        CollectionAssert.AreEquivalent(new[] { one.Id, two.Id, named.Id }, listed.Listing.Items.Select(item => item.Id).ToArray());

        await listed.Organising.RenameTag(listed.Listing.Store!, "cat", "kitty");
        await listed.Listing.SetQuery("other");
        await listed.WaitForOrganising(() => _ = listed.Notices.Undo(listed.Notices.Shown.Last()));
        Assert.AreEqual("other", listed.Listing.Query, "a search edited since is left alone");
    }

    [TestMethod]
    public void OnlyWholeWordsThatAreTheTagAreReplacedAndUndoLeavesTheOthers()
    {
        var (renamed, replaced) = ItemSearch.WithRenamedTag("match  category CAT #cat kitty", "cat", "kitty");
        Assert.AreEqual("match  category kitty #kitty kitty", renamed, "case ignored, # kept, a word holding it left, spaces kept");

        Assert.AreEqual("match  category CAT #cat kitty", ItemSearch.WithoutRenamedTag(renamed, renamed, replaced, "kitty"), "a destination already searched for stays");
        Assert.AreEqual("match  category dog #cat kitty", ItemSearch.WithoutRenamedTag("match  category dog #kitty kitty", renamed, replaced, "kitty"), "a word typed in place since stays");
        Assert.AreEqual("kitty " + renamed, ItemSearch.WithoutRenamedTag("kitty " + renamed, renamed, replaced, "kitty"), "words added since: no place is trusted, nothing changes");
    }

    [TestMethod]
    public async Task AMergeThroughALitChipLightsTheDestinationsChipAndLeavesTheSourcesPlaceGrey()
    {
        using var listed = await ListedStash.Open();
        var (a, b, c) = (await listed.Stash.Import("a", TestStash.Bytes(1)), await listed.Stash.Import("b", TestStash.Bytes(2)), await listed.Stash.Import("c", TestStash.Bytes(3)));
        listed.Stash.Tag(a.Id, "lol");
        listed.Stash.Tag(b.Id, "lol");
        listed.Stash.Tag(b.Id, "lmao");
        listed.Stash.Tag(c.Id, "lmao");
        listed.Stash.Tag(c.Id, "other");
        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var row = Chips(listed);
            Click(Chip(row, "lol"));
            await listed.Listing.Settle();
            Assert.AreEqual("lol", listed.Listing.Query);
            var places = listed.Listing.Chips.Select(chip => chip.Tag).ToList();

            var box = OpenRename(Chip(row, "lol"));
            box.Text = "lmao";
            await listed.WaitForOrganising(() => Press(box, Key.Enter));
            listed.Keys.Released(Key.Enter);
            await listed.Listing.Settle();
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual("lmao", listed.Listing.Query, "the search's word becomes the destination");
            CollectionAssert.AreEqual(new[] { "lmao" }, listed.Listing.LitTags.ToArray());
            Assert.IsTrue(Chip(row, "lmao").Classes.Contains("accent"), "the destination's chip is lit");
            Assert.IsTrue(Chip(row, "lmao").IsEnabled);
            CollectionAssert.AreEqual(places, listed.Listing.Chips.Select(chip => chip.Tag).ToList(), "every place where it was");
            Assert.AreEqual(new TagCount("lol", 0), listed.Listing.Chips.Single(chip => chip.Tag == "lol"), "the source's place is a tag that lost its last item");
            Assert.IsFalse(Chip(row, "lol").IsEnabled, "grey: no chip to click, light or drop on (G4)");
            Assert.IsFalse(Chip(row, "lol").Classes.Contains("accent"));
            CollectionAssert.AreEquivalent(new[] { a.Id, b.Id, c.Id }, listed.Listing.Items.Select(item => item.Id).ToArray(), "every former carrier, and the destination's own, listed");
            CollectionAssert.AreEquivalent(new[] { "lmao", "other" }, (await Stored(listed, c)).Tags.ToArray(), "the destination's own carrier keeps its other tags");
        });
    }
}
