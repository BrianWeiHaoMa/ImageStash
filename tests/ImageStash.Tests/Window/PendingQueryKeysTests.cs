using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The window's keys pressed while what was typed is still searched, over a real stash held as another program holds it
/// (E3) so the search stays pending: an arrow reaches the row it asked for among the search's matches once they have
/// arrived and are laid out (G5, G7), and Enter, <c>Ctrl+C</c> and <c>Ctrl+D</c> pressed after it act on that row, in
/// the order pressed, each taking the selection as it is dispatched (G8, G17); a query typed after an arrow supersedes
/// it, so the new matches arrive with their first selected (G5); with nothing pending an arrow moves at once. The acts are
/// given as the window gives them: Enter and <c>Ctrl+C</c> wait for the listing to settle and copy its selection.
/// </summary>
[TestClass]
public sealed class PendingQueryKeysTests
{
    [TestMethod]
    public async Task DownPressedWhileASearchIsPendingReachesTheSecondMatchOnceTheMatchesArrive()
    {
        using var listed = await Listed();
        var (newer, older) = await DogAndTwoCats(listed);
        var keys = Keys(listed);

        Task search, moved;
        using (listed.Stash.HoldStore())
        {
            search = listed.Listing.SetQuery("cat");
            Assert.IsFalse(listed.Listing.Settled, "the search is pending");
            moved = keys.Move(Direction.Down);
            Assert.AreEqual(newer.Id, listed.Listing.Focused, "nothing moves before the matches arrive");
        }

        await search;
        await moved;

        Assert.AreEqual(older.Id, listed.Listing.Focused, "Down from the first match reaches the second, not lost when the search arrives (G5, G7)");
        CollectionAssert.AreEqual(new[] { older.Id }, listed.Listing.Selection.ToList());
    }

    [TestMethod]
    public async Task DownThenEnterBeforeTheMatchesArriveHandsOverTheSecondMatchAndDownDownUpEndsOnTheFirst()
    {
        using var listed = await Listed();
        var (newer, older) = await DogAndTwoCats(listed);
        var keys = Keys(listed);

        Task<Outcome>? handed = null;
        Task drained;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("cat");
            drained = keys.Move(Direction.Down);
            keys.Then(() => handed = HandOver(listed));
        }

        await drained;
        Assert.IsTrue((await handed!).Succeeded);
        CollectionAssert.AreEqual(new[] { older.Id }, listed.Clipboard.Written.Single().Content.Items.ToList(), "the row Down reached, not the first match (G8)");

        await listed.Listing.ShowScope(Scope.All);
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("cat");
            drained = keys.Move(Direction.Down);
            _ = keys.Move(Direction.Down);
            _ = keys.Move(Direction.Up);
        }

        await drained;
        Assert.AreEqual(newer.Id, listed.Listing.Focused, "each arrow in press order: the second Down stays on the last match, Up goes back to the first");
    }

    [TestMethod]
    public async Task DownEnterDownHandsOverTheSecondMatchAndEndsOnTheThirdAndCtrlCDoesTheSame()
    {
        foreach (var copy in new[] { false, true })
        {
            using var listed = await Listed();
            var matches = await FourCats(listed);
            var keys = Keys(listed);

            Task<Outcome>? acted = null;
            Task drained;
            using (listed.Stash.HoldStore())
            {
                _ = listed.Listing.SetQuery("cat");
                drained = keys.Move(Direction.Down);
                keys.Then(() => acted = copy ? CopySelection(listed) : HandOver(listed));
                _ = keys.Move(Direction.Down);
            }

            await drained;
            Assert.IsTrue((await acted!).Succeeded);
            CollectionAssert.AreEqual(new[] { matches[1].Id }, listed.Clipboard.Written.Single().Content.Items.ToList(), $"the second match, Ctrl+C {copy} (G8, G9)");
            Assert.AreEqual(matches[2].Id, listed.Listing.Focused, "the acts in press order, not every arrow before every act");
        }
    }

    [TestMethod]
    public async Task DownCtrlDDownTogglesOnlyTheSecondMatchAndEndsOnTheThird()
    {
        using var listed = await Listed();
        var matches = await FourCats(listed);
        var keys = Keys(listed);

        Task<Outcome>? toggled = null;
        Task drained;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("cat");
            drained = keys.Move(Direction.Down);
            keys.Then(() => toggled = listed.Organising.ToggleFavoriteOfSelection());
            _ = keys.Move(Direction.Down);
        }

        await drained;
        Assert.IsTrue((await toggled!).Succeeded);
        await listed.Listing.Settle();
        var favorites = (await listed.Stash.Store!.Favorites()).Value!;
        CollectionAssert.AreEqual(new[] { matches[1].Id }, favorites.Select(item => item.Id).ToList(), "only the row Down reached (G17)");
        Assert.AreEqual(matches[2].Id, listed.Listing.Focused);
    }

    [TestMethod]
    public async Task DownThenCtrlABeforeTheMatchesArriveSelectsEveryMatch()
    {
        using var listed = await Listed();
        var matches = await FourCats(listed);
        var keys = Keys(listed);

        Task drained;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("cat");
            drained = keys.Move(Direction.Down);
            keys.Then(listed.Listing.SelectAll);
        }

        await drained;
        CollectionAssert.AreEquivalent(matches.Select(item => item.Id).ToList(), listed.Listing.Selection.ToList(),
            "Ctrl+A after Down selects every match, in press order, not the items shown before (G7, G5)");
    }

    [TestMethod]
    public async Task InFavoritesDownThenTwoCtrlDBringTheSecondItemsFlagBackAndLeaveTheOthers()
    {
        using var listed = await Listed();
        var c = await listed.Stash.Import("c", MediaFixtures.DistinctPng(1));
        var b = await listed.Stash.Import("b", MediaFixtures.DistinctPng(2));
        var a = await listed.Stash.Import("a", MediaFixtures.DistinctPng(3));
        await listed.Stash.Store!.SetFavorite([a, b, c], true);
        await listed.Listing.ShowScope(Scope.Favorites);
        Assert.AreEqual(a.Id, listed.Listing.Focused);
        var keys = Keys(listed);

        // One press first: it toggles the row Down reached, not the first row of the scope's arriving load.
        Task<Outcome>? once = null;
        Task drained;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.ShowScope(Scope.Favorites);
            drained = keys.Move(Direction.Down);
            keys.Then(() => once = listed.Organising.ToggleFavoriteOfSelection());
        }

        await drained;
        Assert.IsTrue((await once!).Succeeded);
        CollectionAssert.AreEquivalent(new[] { a.Id, c.Id }, (await listed.Stash.Store.Favorites()).Value!.Select(item => item.Id).ToList(), "b, the row Down reached, unset");
        await listed.Stash.Store.SetFavorite([b], true);
        await listed.Listing.ShowScope(Scope.Favorites);

        var toggles = new List<Task<Outcome>>();
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.ShowScope(Scope.Favorites);
            drained = keys.Move(Direction.Down);
            keys.Then(() => toggles.Add(listed.Organising.ToggleFavoriteOfSelection()));
            keys.Then(() => toggles.Add(listed.Organising.ToggleFavoriteOfSelection()));
        }

        await drained;
        Assert.HasCount(2, toggles, "both presses dispatched once Down had applied, the second without waiting for the first's refresh");
        foreach (var outcome in await Task.WhenAll(toggles))
        {
            Assert.IsTrue(outcome.Succeeded, outcome.Failure?.Sentence);
        }

        var favorites = (await listed.Stash.Store.Favorites()).Value!;
        CollectionAssert.AreEquivalent(new[] { a.Id, b.Id, c.Id }, favorites.Select(item => item.Id).ToList(),
            "the first press unset b, the second set it again; a and c untouched (G17)");
    }

    [TestMethod]
    public async Task WithNothingPendingAnArrowMovesAtOnceAndAnArrowBeforeASearchWithNoMatchDoesNothing()
    {
        using var listed = await Listed();
        var (_, older) = await DogAndTwoCats(listed);
        await listed.Listing.SetQuery("cat");
        var keys = Keys(listed);

        var moved = keys.Move(Direction.Down);

        Assert.IsTrue(moved.IsCompleted, "nothing waited");
        Assert.AreEqual(older.Id, listed.Listing.Focused, "moved at once, as without a pending search");
        var acted = false;
        keys.Then(() => acted = true);
        Assert.IsTrue(acted, "an act with no arrow waiting is dispatched at once");

        Task drained;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("zebra");
            drained = keys.Move(Direction.Down);
        }

        await drained;
        Assert.IsTrue(listed.Listing.NoMatch, "the no-match state (G5)");
        Assert.IsNull(listed.Listing.Focused);
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task ACharacterTypedAfterAWaitingDownStartsANewQueryThatEndsOnItsFirstMatch()
    {
        using var listed = await Listed();
        await listed.Stash.Import("cat older", MediaFixtures.DistinctPng(1));
        var newer = await listed.Stash.Import("cat newer", MediaFixtures.DistinctPng(2));
        await listed.Stash.Import("car", MediaFixtures.DistinctPng(3));
        await listed.Listing.ShowScope(Scope.All);
        var keys = Keys(listed);

        // ca, Down, t while ca is still pending: the Down is superseded.
        Task drained;
        Task typed;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("ca");
            drained = keys.Move(Direction.Down);
            typed = listed.Listing.SetQuery("cat");
        }

        await Task.WhenAll(drained, typed);
        await listed.Listing.Settle();
        Assert.AreEqual(newer.Id, listed.Listing.Focused, "the first match of cat (G5)");

        // ca settling before t is typed: the Down applies to ca's rows, then cat's arrival selects its first match.
        await listed.Listing.ShowScope(Scope.All);
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("ca");
            drained = keys.Move(Direction.Down);
        }

        await drained;
        Assert.AreEqual(newer.Id, listed.Listing.Focused, "Down from car, ca's first match, reached cat newer");
        await listed.Listing.SetQuery("cat");
        Assert.AreEqual(newer.Id, listed.Listing.Focused, "the first match of cat either way");
    }

    /// <summary>A stash whose store waits for a holder instead of naming it, so a search stays pending while it is held.</summary>
    private static Task<ListedStash> Listed() => ListedStash.Open(storeRetry: TimeSpan.FromSeconds(10));

    /// <summary>The window's queue, its rows laid out as the grid lays them out at its width, one tile per row.</summary>
    private static PendingQueryKeys Keys(ListedStash listed) => new(listed.Listing, () => JustifiedRows.Layout(listed.Listing.Items, 100, 96));

    /// <summary><c>dog</c>, then <c>cat older</c> and <c>cat newer</c> added after it, All showing.</summary>
    private static async Task<(StashItem Newer, StashItem Older)> DogAndTwoCats(ListedStash listed)
    {
        await listed.Stash.Import("dog", MediaFixtures.DistinctPng(3));
        var older = await listed.Stash.Import("cat older", MediaFixtures.DistinctPng(1));
        var newer = await listed.Stash.Import("cat newer", MediaFixtures.DistinctPng(2));
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsTrue(JustifiedRows.Layout(listed.Listing.Items, 100, 96).All(row => row.Tiles.Count == 1), "one tile per row");
        return (newer, older);
    }

    /// <summary>Four items matching <c>cat</c> and one not, All showing; the matches newest-added first.</summary>
    private static async Task<IReadOnlyList<StashItem>> FourCats(ListedStash listed)
    {
        var added = new List<StashItem>();
        for (var i = 1; i <= 4; i++)
        {
            added.Add(await listed.Stash.Import($"cat {i}", MediaFixtures.DistinctPng(i)));
        }

        await listed.Stash.Import("dog", MediaFixtures.DistinctPng(9));
        await listed.Listing.ShowScope(Scope.All);
        added.Reverse();
        return added;
    }

    /// <summary>Enter as the window dispatches it with no paste target: once the listing settles, its selection copied.</summary>
    private static async Task<Outcome> HandOver(ListedStash listed)
    {
        await listed.Listing.Settle();
        return await listed.Handoff.HandOver(null!, listed.Listing.SelectedItems, null, () => { });
    }

    /// <summary><c>Ctrl+C</c> as the window dispatches it: once the listing settles, its selection copied.</summary>
    private static async Task<Outcome> CopySelection(ListedStash listed)
    {
        await listed.Listing.Settle();
        return await listed.Handoff.Copy(null!, listed.Listing.SelectedItems);
    }
}
