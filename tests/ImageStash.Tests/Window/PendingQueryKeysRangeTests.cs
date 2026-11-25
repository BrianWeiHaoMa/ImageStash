using Avalonia.Input;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

public sealed partial class PendingQueryKeysTests
{
    [TestMethod]
    public async Task AQueuedRangeCopiesAndTagsExactlyItsArrivingMatchesInDisplayedOrder()
    {
        using var listed = await Listed();
        var matches = await FourCats(listed);
        Task first, second, reversed, copied, tagged;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("cat");
            first = listed.Press(Key.Down, KeyModifiers.Shift, KeyPlace.SearchBox);
            second = listed.Press(Key.Down, KeyModifiers.Shift, KeyPlace.SearchBox);
            reversed = listed.Press(Key.Up, KeyModifiers.Shift, KeyPlace.SearchBox);
            copied = listed.Press(Key.C, KeyModifiers.Control, KeyPlace.SearchBox);
            tagged = listed.Press(Key.T, KeyModifiers.Control, KeyPlace.SearchBox);
            Assert.IsFalse(copied.IsCompleted);
            Assert.IsEmpty(listed.Clipboard.Written);
        }

        await Task.WhenAll(first, second, reversed, copied, tagged);
        var expected = matches.Take(2).ToArray();
        CollectionAssert.AreEqual(expected.Select(item => item.FilePath).ToArray(), listed.Clipboard.Written.Single().Content.FilePaths.ToArray());
        CollectionAssert.AreEqual(expected.Select(item => item.Id).ToArray(), listed.TagFieldItems.Select(item => item.Id).ToArray());
        Assert.AreEqual(expected[^1].Id, listed.Listing.Focused);
        await FieldLeaving.AddTags(listed.Organising, listed.TagFieldItems, "chosen");

        var store = listed.Stash.Store!;
        CollectionAssert.AreEqual(expected.Select(item => item.Id).ToArray(),
            (await store.Search("chosen")).Value!.Select(item => item.Id).ToArray());
        Assert.AreEqual(2L, listed.Uses());
        foreach (var item in expected)
        {
            Assert.AreEqual(1L, (await store.UseHistory(item.Id)).Value!.Count);
        }
    }

    [TestMethod]
    public async Task DeleteAfterAShiftArrowRecyclesTheWholeRangeAndLeavesTheOtherPictures()
    {
        using var listed = await Listed();
        var matches = await FourCats(listed);
        await listed.Listing.SetQuery("cat");
        await listed.Press(Key.Down, KeyModifiers.Shift, KeyPlace.Grid);
        await listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);

        CollectionAssert.AreEqual(matches.Take(2).Select(item => item.FilePath).ToArray(), listed.Stash.RecycleBin.Asked);
        CollectionAssert.AreEqual(matches.Skip(2).Select(item => item.Id).ToArray(),
            (await listed.Stash.Store!.Search("cat")).Value!.Select(item => item.Id).ToArray());
    }
}
