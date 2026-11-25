using Avalonia.Input;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

public sealed partial class PendingQueryKeysTests
{
    [TestMethod]
    public async Task PagesUseTheArrivingRowsAndCurrentViewportOnEachSideOfAnOrderedScopeKey()
    {
        using var listed = await Listed();
        var cats = await AddPicturesInDisplayOrder(listed, "cat", 1, [62, 100, 200, 150, 100, 75, 150, 200, 62, 100, 200]);
        var owls = await AddPicturesInDisplayOrder(listed, "owl", 21, [200, 150, 62, 100, 200, 62, 150, 75, 75]);
        var store = listed.Listing.Store!;
        Assert.IsTrue((await store.SetFavorite(owls, true)).All(result => result.Succeeded));
        await listed.Listing.ShowScope(Scope.All);
        listed.GridViewport = (240, 90);
        var clipboard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listed.Clipboard.Hold = clipboard.Task;

        Task moved, firstCopy, scope, movedAgain, secondCopy;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("cat");
            Assert.IsFalse(listed.Listing.Settled);
            moved = listed.Press(Key.PageDown, KeyModifiers.None, KeyPlace.Grid);
            firstCopy = listed.Press(Key.C, KeyModifiers.Control, KeyPlace.Grid);
            scope = listed.Press(Key.D2, KeyModifiers.Control, KeyPlace.Grid);
            movedAgain = listed.Press(Key.PageDown, KeyModifiers.None, KeyPlace.Grid);
            secondCopy = listed.Press(Key.C, KeyModifiers.Control, KeyPlace.Grid);
            listed.GridViewport = (240, 165);
        }

        try
        {
            await Task.WhenAll(moved, scope, movedAgain).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(Scope.Favorites, listed.Listing.LitChip);
            Assert.AreEqual("", listed.Listing.Query);
            Assert.AreEqual(owls[6].Id, listed.Listing.Focused);
            CollectionAssert.AreEqual(new[] { owls[6].Id }, listed.Listing.Selection.ToArray());
            Assert.IsFalse(firstCopy.IsCompleted);
        }
        finally
        {
            clipboard.SetResult();
            await Task.WhenAll(firstCopy, secondCopy);
        }

        CollectionAssert.AreEqual(new[] { cats[5].FilePath, owls[6].FilePath },
            listed.Clipboard.Written.Select(write => write.Content.FilePaths.Single()).ToArray());
        Assert.AreEqual(2L, listed.Uses());
        Assert.AreEqual(1L, (await store.UseHistory(cats[5].Id)).Value!.Count);
        Assert.AreEqual(1L, (await store.UseHistory(owls[6].Id)).Value!.Count);
    }

    [TestMethod]
    public async Task ASettledGridMovesByViewportAndEndpointsWithoutWaitingForAStoreRead()
    {
        using var listed = await Listed();
        var pictures = await AddPicturesInDisplayOrder(listed, "picture", 1, [62, 100, 200, 150, 100, 75, 150, 200, 62, 100, 200, 200, 100, 75, 150]);
        var listing = listed.Listing;
        await listing.ShowScope(Scope.All);

        using (listed.Stash.HoldStore())
        {
            listing.Select([pictures[1].Id, pictures[2].Id], pictures[2].Id);
            Move(Key.PageDown, 90, 4);
            listing.Focus(pictures[2].Id);
            Move(Key.PageDown, 165, 7);
            Move(Key.PageUp, 165, 2);
            Move(Key.PageDown, 1, 4);
            Move(Key.PageDown, 2000, 14);
            Move(Key.PageDown, 165, 14);
            Move(Key.PageUp, 2000, 2);
            Move(Key.Home, 165, 0);
            listing.Select([], null);
            Move(Key.End, 165, 14);
        }

        await listing.SetQuery("absent");
        foreach (var key in new[] { Key.Home, Key.End, Key.PageUp, Key.PageDown })
        {
            await listed.Press(key, KeyModifiers.None, KeyPlace.Grid);
            Assert.IsNull(listing.Focused);
            Assert.IsEmpty(listing.Selection);
        }

        void Move(Key key, double viewportHeight, int to)
        {
            listed.GridViewport = (240, viewportHeight);
            Assert.IsTrue(listed.Press(key, KeyModifiers.None, KeyPlace.Grid).IsCompletedSuccessfully, "A settled move uses the displayed listing.");
            Assert.AreEqual(pictures[to].Id, listing.Focused, $"{key} with a {viewportHeight}-unit viewport");
            CollectionAssert.AreEqual(new[] { pictures[to].Id }, listing.Selection.ToArray());
        }
    }

    private static async Task<StashItem[]> AddPicturesInDisplayOrder(ListedStash listed, string name, int seed, int[] widths)
    {
        var items = new StashItem[widths.Length];
        for (var index = widths.Length - 1; index >= 0; index--)
        {
            var bytes = MediaFixtures.DistinctPng(seed + index, widths[index], 100);
            items[index] = await listed.Stash.ImportPicture($"{name} {index}", "picture.png", bytes);
        }

        return items;
    }
}
