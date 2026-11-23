using Avalonia.Input;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// <c>Ctrl+T</c>'s act (G7, G17) through the window's routing and the act its case calls, over a real store: from the box
/// or the grid the tag field opens on every selected item, as the key is dispatched, after an arrow still waiting for
/// what was typed, and what is typed there tags each of them; with nothing selected, with the field open already, inside
/// a tag field and in the editor it opens nothing; in the preview it sends focus to the preview's tag box whatever the
/// grid selects, never opening the field over the preview.
/// </summary>
[TestClass]
public sealed class TagKeyTests
{
    [TestMethod]
    [DataRow(KeyPlace.Grid)]
    [DataRow(KeyPlace.SearchBox)]
    public async Task WithThreeSelectedCtrlTOpensTheFieldOnAllThreeAndWhatIsTypedTagsEach(KeyPlace place)
    {
        using var listed = await ListedStash.Open();
        var (a, b, c) = await Three(listed);
        await listed.Stash.Import("other", TestStash.Bytes(4));
        await listed.Listing.Refresh();
        listed.Listing.Select([a.Id, b.Id, c.Id], focus: b.Id);

        Assert.AreEqual(KeyAction.AddTags, await listed.Press(Key.T, KeyModifiers.Control, place));

        CollectionAssert.AreEquivalent(new[] { a.Id, b.Id, c.Id }, listed.TagFieldItems.Select(item => item.Id).ToList(), "every selected item, not the focused one alone (G17)");
        await FieldLeaving.AddTags(listed.Organising, listed.TagFieldItems, "cat");
        var tagged = (await listed.Stash.All()).Where(item => item.Tags.Contains("cat")).Select(item => item.Id).ToList();
        CollectionAssert.AreEquivalent(new[] { a.Id, b.Id, c.Id }, tagged, "the words go to the three, and to nothing else");
    }

    [TestMethod]
    public async Task ACtrlTPressedAfterADownStillWaitingForWhatWasTypedOpensOnTheItemDownReached()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(10));
        await listed.Stash.Import("dog", MediaFixtures.DistinctPng(3));
        var older = await listed.Stash.Import("cat older", MediaFixtures.DistinctPng(1));
        await listed.Stash.Import("cat newer", MediaFixtures.DistinctPng(2));
        await listed.Listing.ShowScope(Scope.All);
        // The window's queue, as the window's case hands it Ctrl+T's act, its rows one tile each.
        var keys = new PendingQueryKeys(listed.Listing, () => JustifiedRows.Layout(listed.Listing.Items, 100, 96));

        Task search, drained;
        using (listed.Stash.HoldStore())
        {
            search = listed.Listing.SetQuery("cat");
            drained = keys.Move(Direction.Down);
            keys.Then(() => listed.TagKey.Press());
            Assert.IsEmpty(listed.TagFieldItems, "nothing opens before the arrow before it has moved");
        }

        await search;
        await drained;

        CollectionAssert.AreEqual(new[] { older.Id }, listed.TagFieldItems.Select(item => item.Id).ToList(), "the second match, which Down reached (G5, G17)");
    }

    [TestMethod]
    public async Task WithNothingSelectedOrTheFieldOpenAlreadyCtrlTOpensNothingNew()
    {
        using var listed = await ListedStash.Open();
        var (a, b, _) = await Three(listed);
        await listed.Listing.Refresh();
        listed.Listing.Select([], focus: null);

        await listed.Press(Key.T, KeyModifiers.Control, KeyPlace.Grid);
        Assert.IsEmpty(listed.TagFieldItems, "nothing selected, nothing to tag");

        listed.Listing.Focus(a.Id);
        await listed.Press(Key.T, KeyModifiers.Control, KeyPlace.Grid);
        listed.Listing.Focus(b.Id);
        await listed.Press(Key.T, KeyModifiers.Control, KeyPlace.Grid);

        CollectionAssert.AreEqual(new[] { a.Id }, listed.TagFieldItems.Select(item => item.Id).ToList(), "with the field open, it stays on the items it opened on");
    }

    [TestMethod]
    [DataRow(KeyPlace.Field)]
    [DataRow(KeyPlace.Editor)]
    public async Task InATagFieldsOwnBoxAndInTheEditorCtrlTDoesNothing(KeyPlace place)
    {
        using var listed = await ListedStash.Open();
        var (a, _, _) = await Three(listed);
        await listed.Listing.Refresh();
        listed.Listing.Focus(a.Id);

        Assert.AreEqual(KeyAction.None, await listed.Press(Key.T, KeyModifiers.Control, place));

        Assert.IsEmpty(listed.TagFieldItems);
        Assert.AreEqual(0, listed.PreviewTagsFocused);
    }

    [TestMethod]
    [DataRow(KeyPlace.Preview)]
    [DataRow(KeyPlace.PreviewButton)]
    [DataRow(KeyPlace.PreviewName)]
    public async Task InThePreviewCtrlTSendsFocusToItsTagBoxWhateverTheGridSelects(KeyPlace place)
    {
        using var listed = await ListedStash.Open();
        var (a, _, _) = await Three(listed);
        await listed.Listing.Refresh();
        listed.PreviewOpen = true;
        // A rename that ends the search's match leaves the preview on its item with nothing selected beneath it.
        listed.Listing.Select([], focus: null);

        Assert.AreEqual(KeyAction.AddTags, await listed.Press(Key.T, KeyModifiers.Control, place));
        Assert.AreEqual(1, listed.PreviewTagsFocused, "the preview's tag box, though the grid selects nothing (G17)");

        listed.Listing.Focus(a.Id);
        await listed.Press(Key.T, KeyModifiers.Control, place);

        Assert.AreEqual(2, listed.PreviewTagsFocused);
        Assert.IsEmpty(listed.TagFieldItems, "the \"Tags of …\" field never opens over the preview");
    }

    private static async Task<(StashItem A, StashItem B, StashItem C)> Three(ListedStash listed) =>
        (await listed.Stash.Import("a", TestStash.Bytes(1)), await listed.Stash.Import("b", TestStash.Bytes(2)), await listed.Stash.Import("c", TestStash.Bytes(3)));
}
