using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

public sealed partial class PendingQueryKeysTests
{
    [TestMethod]
    [DataRow(KeyPlace.SearchBox)]
    [DataRow(KeyPlace.Grid)]
    public async Task DownCopyFavoritesDownCopyUsesBothListingsInOrder(KeyPlace place)
    {
        using var listed = await Listed();
        var matches = await FourCats(listed);
        var store = listed.Listing.Store!;
        var olderFavorite = await listed.Stash.Import("older owl", MediaFixtures.DistinctPng(10));
        var newerFavorite = await listed.Stash.Import("newer owl", MediaFixtures.DistinctPng(11));
        Assert.IsTrue((await store.SetFavorite([olderFavorite, newerFavorite], true)).All(result => result.Succeeded));
        var clipboard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listed.Clipboard.Hold = clipboard.Task;

        Task moved, firstCopy, scope, movedAgain, secondCopy;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("cat");
            Assert.IsFalse(listed.Listing.Settled);
            moved = listed.Press(Key.Down, KeyModifiers.None, place);
            firstCopy = listed.Press(Key.C, KeyModifiers.Control, place);
            scope = listed.Press(Key.D2, KeyModifiers.Control, place);
            movedAgain = listed.Press(Key.Down, KeyModifiers.None, place);
            secondCopy = listed.Press(Key.C, KeyModifiers.Control, place);
        }

        try
        {
            await Task.WhenAll(moved, scope, movedAgain);
            Assert.AreEqual(Scope.Favorites, listed.Listing.LitChip);
            Assert.AreEqual("", listed.Listing.Query);
            Assert.AreEqual(olderFavorite.Id, listed.Listing.Focused);
            Assert.IsFalse(firstCopy.IsCompleted);
        }
        finally
        {
            clipboard.SetResult();
            await Task.WhenAll(firstCopy, secondCopy);
        }

        CollectionAssert.AreEqual(new[] { matches[1].FilePath, olderFavorite.FilePath },
            listed.Clipboard.Written.Select(write => write.Content.FilePaths.Single()).ToArray());
        Assert.AreEqual(2L, listed.Uses());
        Assert.AreEqual(1L, (await store.UseHistory(matches[1].Id)).Value!.Count);
        Assert.AreEqual(1L, (await store.UseHistory(olderFavorite.Id)).Value!.Count);
    }

    [TestMethod]
    [DataRow(KeyPlace.SearchBox)]
    [DataRow(KeyPlace.Grid)]
    public async Task ANewQuerySupersedesAWaitingScopeKeyAndItsCopy(KeyPlace place)
    {
        using var listed = await Listed();
        await DogAndTwoCats(listed);
        var listing = listed.Listing;
        Task scope, copy, dog;
        using (listed.Stash.HoldStore())
        {
            _ = listing.SetQuery("cat");
            _ = listed.Press(Key.Down, KeyModifiers.None, place);
            scope = listed.Press(Key.D2, KeyModifiers.Control, place);
            copy = listed.Press(Key.C, KeyModifiers.Control, place);
            dog = listing.SetQuery("dog");
        }

        await Task.WhenAll(scope, copy, dog);
        Assert.AreEqual(Scope.All, listing.Scope);
        Assert.AreEqual("dog", listing.Query);
        Assert.AreEqual("dog", listing.SelectedItems.Single().Name);
        Assert.IsEmpty(listed.Clipboard.Written);
    }

    [TestMethod]
    public async Task AStashReplacementDoesNotInheritAWaitingScopeKeyOrCopy()
    {
        using var listed = await Listed();
        var (old, _) = await DogAndTwoCats(listed);
        using var replacement = new TestStash();
        var newStore = await replacement.OpenCleanly();
        await replacement.Import("cat replacement one", MediaFixtures.DistinctPng(8));
        await replacement.Import("cat replacement two", MediaFixtures.DistinctPng(9));
        var sameId = await replacement.Import("cat replacement three", MediaFixtures.DistinctPng(10));
        Assert.AreEqual(old.Id, sameId.Id);
        var listing = listed.Listing;

        Task scope, copy, oldRead;
        using (listed.Stash.HoldStore())
        {
            oldRead = listing.SetQuery("cat");
            _ = listed.Press(Key.Down, KeyModifiers.None, KeyPlace.Grid);
            scope = listed.Press(Key.D2, KeyModifiers.Control, KeyPlace.Grid);
            copy = listed.Press(Key.C, KeyModifiers.Control, KeyPlace.Grid);
            await listing.Withdraw();
            listing.StashReplaced();
            await listing.StashOpened(newStore);
        }

        await Task.WhenAll(scope, copy, oldRead);
        Assert.AreEqual(Scope.All, listing.Scope);
        Assert.AreEqual("cat", listing.Query);
        CollectionAssert.AreEqual(new[] { sameId.Id }, listing.Selection.ToArray());
        Assert.IsEmpty(listed.Clipboard.Written);
    }

    [TestMethod]
    public async Task ScopeKeyCancelsAnUnresolvedChipChoice()
    {
        using var listed = await Listed();
        var (_, older) = await DogAndTwoCats(listed);
        await listed.Stash.Store!.AddTag([older], "older");
        await listed.Listing.Refresh();

        Task search, chip, scope;
        using (listed.Stash.HoldStore())
        {
            search = listed.Listing.SetQuery("cat");
            chip = listed.Listing.ChooseTag("older");
            Assert.IsFalse(chip.IsCompleted);
            scope = listed.Press(Key.D1, KeyModifiers.Control, KeyPlace.SearchBox);
        }

        await Task.WhenAll(search, chip, scope);
        await listed.Listing.Settle();
        Assert.AreEqual("", listed.Listing.Query);
        Assert.AreEqual(Scope.Recent, listed.Listing.LitChip);
        Assert.IsEmpty(listed.Listing.LitTags);
        Assert.IsEmpty(listed.Listing.Items);
    }

    [TestMethod]
    public async Task ACopyAfterWaitingFavoritesCannotUseTheTileClickedBeforeIt()
    {
        using var listed = await Listed();
        var (favorite, clicked) = await DogAndTwoCats(listed);
        Assert.IsTrue((await listed.Listing.Store!.SetFavorite([favorite], true)).Single().Succeeded);
        Task scope, copy;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("cat");
            _ = listed.Press(Key.Down, KeyModifiers.None, KeyPlace.Grid);
            listed.Listing.Focus(clicked.Id);
            scope = listed.Press(Key.D2, KeyModifiers.Control, KeyPlace.Grid);
            copy = listed.Press(Key.C, KeyModifiers.Control, KeyPlace.Grid);
        }

        await Task.WhenAll(scope, copy);
        Assert.AreEqual(Scope.Favorites, listed.Listing.LitChip);
        Assert.AreEqual(favorite.FilePath, listed.Clipboard.Written.Single().Content.FilePaths.Single());
    }

    [TestMethod]
    public async Task AnImportThatFinishesWhileAScopeKeyWaitsShowsThePersonsScopeAndOffersShow()
    {
        using var listed = await Listed();
        var (favorite, _) = await DogAndTwoCats(listed);
        Assert.IsTrue((await listed.Listing.Store!.SetFavorite([favorite], true)).Single().Succeeded);
        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var closingView = false;
            listed.Import.Finishing += () => closingView = true;
            var processing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            listed.Import.Changed += () =>
            {
                if (listed.Import.Busy == "Adding 1 of 1 to your stash…")
                {
                    processing.TrySetResult();
                }
            };

            Task scope, paste;
            using (listed.Stash.HoldStore())
            {
                _ = listed.Listing.SetQuery("dog");
                // The clipboard read started here and returns its file list while the search is still held.
                var pasteBegan = ImportBegan.In(listed.Listing);
                _ = listed.Press(Key.Down, KeyModifiers.None, KeyPlace.SearchBox);
                scope = listed.Press(Key.D2, KeyModifiers.Control, KeyPlace.SearchBox);
                paste = listed.Import.Paste([new DroppedItem("clip.mp4", null, null, IsFolder: false)], [], pasteBegan);
                // The unsupported file returns immediately on this dispatcher turn after reporting its progress.
                await processing.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.IsFalse(closingView, "the pending scope choice must be resolved before the import can take over");
            }

            await Task.WhenAll(scope, paste);
            Assert.IsFalse(closingView);
        }));
        Assert.AreEqual(Scope.Favorites, listed.Listing.LitChip);
        Assert.AreEqual(favorite.Id, listed.Listing.Focused);
        Assert.AreEqual(ImportEnd.Show, listed.Notices.Shown.Single().Button?.Label);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AfterAFailedScopeKeyCopyCanUseTheSelectionOnScreenOrNameTheFailedView(bool move)
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.Zero);
        var (selected, _) = await DogAndTwoCats(listed);
        using (listed.Stash.HoldStore())
        {
            await listed.Press(Key.D2, KeyModifiers.Control, KeyPlace.Grid);
        }

        Assert.IsNotEmpty(listed.Failures);
        Assert.AreEqual(selected.Id, listed.Listing.Focused);
        if (move)
        {
            await listed.Press(Key.Down, KeyModifiers.None, KeyPlace.Grid);
        }

        await listed.Press(Key.C, KeyModifiers.Control, KeyPlace.Grid);
        if (move)
        {
            Assert.IsEmpty(listed.Clipboard.Written, "a copy after the failed move must not substitute the old tile");
            StringAssert.Contains(listed.Notices.Shown.Single().Text, "the requested list could not be read");
        }
        else
        {
            Assert.AreEqual(selected.FilePath, listed.Clipboard.Written.Single().Content.FilePaths.Single());
        }
    }
}
