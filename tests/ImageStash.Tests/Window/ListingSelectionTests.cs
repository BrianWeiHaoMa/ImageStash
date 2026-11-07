using System.Diagnostics;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.ListedStash;

namespace ImageStash.Tests.Window;

/// <summary>
/// The selection, through the one listing model over a real stash: G7's arrows, G17's multi-selection, what a refresh or
/// a typed query does to it (G5), G6's one playing item, and G1 and E4 over 5,000 pictures.
/// </summary>
[TestClass]
public sealed class ListingSelectionTests
{
    public TestContext TestContext { get; set; } = null!;

    private readonly List<Failure> failures = [];

    private Listing NewListing() => new(failures.Add);

    [TestMethod]
    public async Task ArrowsMoveTheFocusThroughTheRowsAndMakeItTheWholeSelection()
    {
        using var stash = new TestStash();
        var (listing, rows, ids) = await Twelve(stash);
        listing.Focus(ids[1]);

        listing.MoveFocus(Direction.Down, rows);
        Assert.AreEqual(ids[5], listing.Focused, "the tile in the next row under the focused tile's centre");
        CollectionAssert.AreEquivalent(new[] { ids[5] }, listing.Selection.ToList());

        listing.MoveFocus(Direction.Right, rows);
        Assert.AreEqual(ids[6], listing.Focused);
        listing.MoveFocus(Direction.Left, rows);
        listing.MoveFocus(Direction.Left, rows);
        Assert.AreEqual(ids[4], listing.Focused);
        listing.MoveFocus(Direction.Up, rows);
        Assert.AreEqual(ids[0], listing.Focused);
        listing.MoveFocus(Direction.Up, rows);
        Assert.AreEqual(ids[0], listing.Focused, "Up from the first row stays");
        listing.MoveFocus(Direction.Left, rows);
        Assert.AreEqual(ids[0], listing.Focused, "Left from the first item stays");
    }

    [TestMethod]
    public async Task DownIntoAShorterRowTakesTheNearestTile()
    {
        using var stash = new TestStash();
        var (listing, rows, ids) = await Twelve(stash, extra: 1);
        Assert.HasCount(1, rows[^1].Tiles, "the last row holds the thirteenth item alone, at the left");
        listing.Focus(ids[11]);

        listing.MoveFocus(Direction.Down, rows);

        Assert.AreEqual(ids[12], listing.Focused);
        listing.MoveFocus(Direction.Right, rows);
        Assert.AreEqual(ids[12], listing.Focused, "Right from the last item stays");
    }

    [TestMethod]
    public async Task ShiftClickExtendsFromTheFocusedItemAndCtrlClickTogglesOneLeavingTheRest()
    {
        using var stash = new TestStash();
        var (listing, _, ids) = await Twelve(stash);
        listing.Focus(ids[1]);

        listing.ExtendTo(ids[4]);
        CollectionAssert.AreEquivalent(ids[1..5], listing.Selection.ToList());
        Assert.AreEqual(ids[1], listing.Focused, "the run is anchored at the focused item");
        listing.ExtendTo(ids[0]);
        CollectionAssert.AreEquivalent(ids[0..2], listing.Selection.ToList());

        listing.ExtendTo(ids[4]);
        listing.Toggle(ids[1]);
        CollectionAssert.AreEquivalent(new[] { ids[2], ids[3], ids[4] }, listing.Selection.ToList());
        Assert.AreEqual(ids[2], listing.Focused, "removing the focused item moves the focus to the nearest selected one");

        listing.Toggle(ids[9]);
        CollectionAssert.AreEquivalent(new[] { ids[2], ids[3], ids[4], ids[9] }, listing.Selection.ToList());
        Assert.AreEqual(ids[9], listing.Focused);

        listing.SelectAll();
        Assert.HasCount(12, listing.Selection);
        Assert.AreEqual(ids[9], listing.Focused);
    }

    [TestMethod]
    public async Task SettlingWaitsForTheLatestQuerysMatchesAndItsFirstSelected()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.Import("cat");
        var dog = await stash.Import("dog");
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);
        listing.Focus(dog.Id);

        _ = listing.SetQuery("d");
        _ = listing.SetQuery("ca");
        await listing.Settle();

        Assert.IsTrue(listing.Settled);
        CollectionAssert.AreEqual(new[] { cat.Id }, listing.SelectedItems.Select(item => item.Id).ToList(), "the latest query's first match, not the earlier selection");
        Assert.IsTrue(listing.Settle().IsCompleted, "nothing to wait for once settled");
    }

    [TestMethod]
    public async Task TheSelectedItemsAreInTheGridsOrderWhateverOrderTheyWereSelectedIn()
    {
        using var stash = new TestStash();
        var (listing, _, ids) = await Twelve(stash);
        listing.Focus(ids[9]);
        listing.Toggle(ids[2]);
        listing.Toggle(ids[5]);

        CollectionAssert.AreEqual(new[] { ids[2], ids[5], ids[9] }, listing.SelectedItems.Select(item => item.Id).ToList());
    }

    [TestMethod]
    public async Task ARefreshKeepsTheSelectionWhereItsItemsStillExist()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var a = await stash.Import("a");
        var b = await stash.Import("b");
        var c = await stash.Import("c");
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);
        listing.Select([a.Id, b.Id], b.Id);

        await stash.Store!.Remove([b.Id]);
        await listing.Refresh();

        CollectionAssert.AreEquivalent(new[] { a.Id }, listing.Selection.ToList());
        Assert.AreEqual(a.Id, listing.Focused);

        await stash.Store.Remove([a.Id]);
        await listing.Refresh();

        CollectionAssert.AreEquivalent(new[] { c.Id }, listing.Selection.ToList(), "nothing left of the selection: the first item");
    }

    [TestMethod]
    public async Task ARefreshStartedWhileATypedQueryLoadsStillMovesTheSelectionToTheFirstMatch()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var older = await stash.Import("cat older");
        var newer = await stash.Import("cat newer");
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);
        listing.Select([older.Id], older.Id);

        var typing = listing.SetQuery("cat");
        var refreshing = listing.Refresh();
        await Task.WhenAll(typing, refreshing);

        CollectionAssert.AreEqual(new[] { newer.Id, older.Id }, listing.Items.Select(item => item.Id).ToList());
        CollectionAssert.AreEqual(new[] { newer.Id }, listing.Selection.ToList(), "the selection moves to the first match (G5), the refresh notwithstanding");
        Assert.AreEqual(newer.Id, listing.Focused);
    }

    [TestMethod]
    public void ThePlayingItemIsTheHoveredAnimatedOneElseTheFocusedOneWhenAnimated()
    {
        var still = TestItems.Sized(1, 8, 6);
        var moving = TestItems.Sized(2, 8, 6, animated: true);
        var other = TestItems.Sized(3, 8, 6, animated: true);

        Assert.AreEqual(3L, Listing.PlayingItem(hovered: other, focused: moving));
        Assert.AreEqual(2L, Listing.PlayingItem(hovered: still, focused: moving));
        Assert.AreEqual(2L, Listing.PlayingItem(hovered: null, focused: moving));
        Assert.IsNull(Listing.PlayingItem(hovered: still, focused: still));
    }

    /// <summary>
    /// G1 and E4 over 5,000 real pictures: the summon's reset and the layout of every row well within the second, the
    /// thumbnails of the first view only, and each search as the user types within the time a keystroke takes. With
    /// <c>IMAGESTASH_FIVE_THOUSAND_STASH</c> naming a folder, a copy of the stash is left there for the published check.
    /// </summary>
    [TestMethod]
    public async Task FiveThousandPicturesAreSummonedLaidOutAndSearchedWellWithinTheirTimes()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        stash.Close();
        stash.AddPictures(5000);
        await stash.OpenCleanly();
        var everything = await stash.All();
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);
        var thumbnails = new Thumbnails<MediaFrame>(96L << 20, 4, TestStash.ShortRetry, frame => frame, action => action(), _ => { }, failures.Add);

        var clock = Stopwatch.StartNew();
        await listing.Refresh();
        var rows = JustifiedRows.Layout(listing.Items, 440, JustifiedRows.RowHeight(ThumbnailSize.Medium));
        var summoned = clock.Elapsed;
        var selected = listing.Selection.ToList();
        // The flyout's grid is about 520 units high; thumbnails are asked for the view and one view below it.
        var (first, last) = JustifiedRows.InView(rows, 0, 520, 520);
        var firstView = rows.Skip(first).Take(last - first + 1).SelectMany(row => row.Tiles).Select(tile => listing.Items[tile.Index]).ToList();
        var key = (StashItem item) => ThumbnailKey.Of(item, ThumbnailSize.Medium);
        thumbnails.Want(firstView.Select(item => new ThumbnailRequest(key(item), item.FilePath, item.Name, Thumbnails<MediaFrame>.Bound(item.Facts, 96, 1))).ToList());
        await thumbnails.Idle();
        var pictured = clock.Elapsed;
        var typed = new List<(string Query, TimeSpan Took, int Matches)>();
        foreach (var query in new[] { "a", "ab", "mood" })
        {
            var before = clock.Elapsed;
            await listing.SetQuery(query);
            typed.Add((query, clock.Elapsed - before, listing.Items.Count));
        }

        TestContext.WriteLine($"5,000 pictures: summoned and laid out in {summoned.TotalMilliseconds:F0} ms ({rows.Count} rows), the first view's {firstView.Count} thumbnails by {pictured.TotalMilliseconds:F0} ms; "
            + string.Join(", ", typed.Select(t => $"\"{t.Query}\" {t.Took.TotalMilliseconds:F0} ms, {t.Matches} matches")));
        Assert.HasCount(5000, everything);
        Assert.AreEqual(5000, rows.Sum(r => r.Tiles.Count));
        CollectionAssert.AreEqual(new[] { everything[0].Id }, selected, "All, its first item selected (G1)");
        Assert.IsLessThan(500, summoned.TotalMilliseconds, "well within G1's second");
        Assert.IsLessThan(200, firstView.Count);
        Assert.IsTrue(firstView.All(item => thumbnails.Get(key(item)) is not null), "the first view's thumbnails arrived");
        Assert.IsTrue(everything.Where(item => !firstView.Any(viewed => viewed.Id == item.Id)).All(item => thumbnails.Get(key(item)) is null && !thumbnails.CouldNotRead(key(item))), "no other picture was read");
        CollectionAssert.AreEqual(new[] { ("mood", 555) }, Chips(listing), "the chips, loaded with every search (G4, G5)");
        foreach (var (query, took, matches) in typed)
        {
            Assert.AreEqual(everything.Count(item => item.Name.Contains(query) || item.Tags.Any(tag => tag.Contains(query))), matches, query);
            Assert.IsLessThan(150, took.TotalMilliseconds, $"\"{query}\" answers within the time a keystroke takes");
        }

        Assert.IsEmpty(failures);
        if (Environment.GetEnvironmentVariable("IMAGESTASH_FIVE_THOUSAND_STASH") is { Length: > 0 } keep)
        {
            stash.Close();
            stash.CopyTo(keep);
        }
    }

    /// <summary>Twelve items of ratio 1.5 in a stash (plus <paramref name="extra"/>), laid out four to a row, newest first.</summary>
    private async Task<(Listing Listing, IReadOnlyList<GridRow> Rows, long[] Ids)> Twelve(TestStash stash, int extra = 0)
    {
        await stash.OpenCleanly();
        for (var i = 0; i < 12 + extra; i++)
        {
            await stash.Import($"item {i}");
        }

        var listing = NewListing();
        await listing.StashOpened(stash.Store!);
        var shaped = listing.Items.Select(item => item with { Facts = item.Facts with { Width = 300, Height = 200 } }).ToList();
        var rows = JustifiedRows.Layout(shaped, 600, 100, gap: 0);
        return (listing, rows, listing.Items.Select(item => item.Id).ToArray());
    }
}
