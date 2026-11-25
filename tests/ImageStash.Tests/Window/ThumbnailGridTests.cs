using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G6's badge: an animated item names the file it is handed over as; a still, a still GIF included, carries none. A
/// favorite's star (G6, G17): the tiles that carry it are the listing's favorites as the store last answered, after
/// <c>Ctrl+D</c>, a drop on Favorites and the item menu's entries, never ahead of the store (R9, G28); it lies inside the
/// smallest tile apart from the badge.
/// </summary>
[TestClass]
public sealed class ThumbnailGridTests
{
    [TestMethod]
    public void TheGridRevealsTheTileWithItsRingWhenTheViewportCanHoldBoth()
    {
        var tile = new GridTile(1, 0, 0, 104, 96, 96);

        Assert.AreEqual(tile.Bounds.Inflate(JustifiedRows.Gap), ThumbnailGrid.FocusRevealBounds(tile, 120));
        Assert.AreEqual(tile.Bounds, ThumbnailGrid.FocusRevealBounds(tile, 3),
            "a viewport shorter than the ring's padding needs to reveal the tile itself");
    }

    [TestMethod]
    public async Task ARevealRequestsTheFocusedTilesBoundsFromTheScroller()
    {
        using var listed = await ListedStash.Open();
        var picture = await listed.Stash.Import("picture", [1]);
        await listed.Listing.ShowScope(Scope.All);
        ControlThread.Run(() =>
        {
            var grid = LayOut(listed.Listing, 240);
            var tile = grid.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Id == picture.Id);
            Rect? requested = null;
            grid.AddHandler(Control.RequestBringIntoViewEvent, (_, args) => requested = args.TargetRect);

            grid.BringFocusedIntoView();

            Assert.AreEqual(tile.Bounds, requested, "with zero arranged height, request the tile without padding");
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReflowRevealsTheSameKeyboardRangeEndAtItsNewBounds(bool stepSize)
    {
        using var listed = await ListedStash.Open();
        for (var index = 0; index < 16; index++)
        {
            await listed.Stash.Import($"picture {index}", MediaFixtures.DistinctPng(index));
        }

        await listed.Listing.ShowScope(Scope.All);
        var listing = listed.Listing;
        ControlThread.Run(() =>
        {
            // Layout and the reveal event run without bitmap decoding on the portable control thread.
            var thumbnails = new Thumbnails<Bitmap>(1, 0, TimeSpan.Zero, FrameBitmap.Of,
                action => Dispatcher.UIThread.Post(action), image => image.Dispose(), failure => Assert.Fail(failure.Sentence));
            var size = stepSize ? ThumbnailSize.Small : ThumbnailSize.Medium;
            var width = stepSize ? 280 : 600;
            var grid = new ThumbnailGrid(listing, thumbnails, () => size);
            Place(width);
            listing.Focus(listing.Items[^2].Id);
            listing.MoveFocus(Direction.Right, grid.Rows, extendSelection: true);
            Dispatcher.UIThread.RunJobs();

            var end = listing.Focused!.Value;
            var wide = grid.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Id == end);
            var chosen = listing.SelectedItems.Select(item => item.Id).ToArray();
            Rect? requested = null;
            grid.AddHandler(Control.RequestBringIntoViewEvent, (_, args) => requested = args.TargetRect);

            size = ThumbnailSize.Medium;
            grid.InvalidateMeasure();
            Place(280);
            Dispatcher.UIThread.RunJobs();

            var narrow = grid.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Id == end);
            Assert.IsGreaterThan(wide.Y, narrow.Y, "the same item is farther down after reflow");
            Assert.AreEqual(narrow.Bounds.Inflate(JustifiedRows.Gap), requested);
            Assert.AreEqual(end, listing.Focused);
            CollectionAssert.AreEqual(chosen, listing.SelectedItems.Select(item => item.Id).ToArray());

            void Place(double width)
            {
                grid.Measure(new Size(width, double.PositiveInfinity));
                grid.Arrange(new Rect(new Size(width, grid.DesiredSize.Height)));
            }
        });
    }

    [TestMethod]
    public async Task ASearchReorderedByCopyRevealsTheSameKeyboardEndAtItsNewPlace()
    {
        using var listed = await ListedStash.Open();
        for (var index = 0; index < 16; index++)
        {
            await listed.Stash.Import($"picture {index}", MediaFixtures.DistinctPng(index));
        }

        await listed.Listing.SetQuery("picture");
        var listing = listed.Listing;
        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var grid = LayOut(listing, 280, decoders: 0);
            listing.Changed += () => grid.Measure(new Size(280, double.PositiveInfinity));
            listing.Focus(listing.Items[^2].Id);
            await listed.Press(Key.Right, KeyModifiers.Shift, KeyPlace.Grid);
            Dispatcher.UIThread.RunJobs();

            var end = listing.Focused!.Value;
            var selected = listing.SelectedItems.Select(item => item.Id).ToArray();
            Rect? requested = null;
            grid.AddHandler(Control.RequestBringIntoViewEvent, (_, args) => requested = args.TargetRect);

            await listed.Press(Key.C, KeyModifiers.Control, KeyPlace.Grid);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(end, listing.Items[1].Id, "the copied items lead the search, with the end second");
            Assert.AreEqual(end, listing.Focused);
            CollectionAssert.AreEqual(selected, listing.SelectedItems.Select(item => item.Id).ToArray());
            Assert.AreEqual(grid.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Id == end).Bounds, requested);
        }));
    }

    [TestMethod]
    public async Task NavigationCanReadTheArrivingRowsAfterTheGridHides()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat", [1]);
        await listed.Stash.Import("dog", [2]);
        await listed.Listing.ShowScope(Scope.All);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var grid = LayOut(listed.Listing, 240);
            Assert.HasCount(2, grid.Rows.SelectMany(row => row.Tiles).ToArray());
            grid.IsVisible = false;
            await listed.Listing.SetQuery("cat");

            var current = grid.ReadNavigationLayout();
            CollectionAssert.AreEqual(new[] { cat.Id }, current.Rows.SelectMany(row => row.Tiles).Select(tile => tile.Id).ToArray());
        }));
    }

    [TestMethod]
    public async Task AHoverTipUsesTheTileUnderThePointerAndWaitsAgainAcrossTheGutter()
    {
        using var listed = await ListedStash.Open();
        var first = await listed.Stash.Import("Plain still", [1]);
        var second = await listed.Stash.Import("Favorite still", [2]);
        var store = listed.Stash.Store!;
        await store.SetFavorite([second], true);
        await store.AddTag([second], "cat mood reaction spare");
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.Focus(first.Id);

        var clock = new SteppedClock();
        var hover = new TileHover(clock);
        var view = new Rect(0, 0, 300, 90);
        ControlThread.Run(() =>
        {
            var grid = LayOut(listed.Listing, view.Width);
            var favorite = grid.Rows[0].Tiles.Single(tile => tile.Id == second.Id);
            var plain = grid.Rows[0].Tiles.Single(tile => tile.Id == first.Id);
            Assert.IsTrue(hover.Track(grid.HoverAt(favorite.Bounds.Center, view)?.Item, listed.Listing.Replacements));
            clock.Pass(TimeSpan.FromMilliseconds(599));
            Assert.IsNull(hover.Words);
            clock.Pass(TimeSpan.FromMilliseconds(1));
            Assert.AreEqual("Favorite still\n#cat #mood #reaction\n★", hover.Words);
            Assert.AreEqual(first.Id, listed.Listing.Focused, "hover leaves selection with the plain still");
            CollectionAssert.AreEqual(new[] { first.Id }, listed.Listing.Selection.ToArray());

            Assert.IsTrue(hover.Track(grid.HoverAt(plain.Bounds.Center, view)?.Item, listed.Listing.Replacements));
            Assert.IsNull(hover.Words, "moving within the grid starts the new tile's own delay");
            clock.Pass(TimeSpan.FromMilliseconds(600));
            Assert.AreEqual("Plain still", hover.Words);

            var gap = new Point(favorite.Bounds.Right + 1, favorite.Bounds.Center.Y);
            Assert.IsTrue(hover.Track(grid.HoverAt(gap, view)?.Item, listed.Listing.Replacements));
            Assert.IsNull(hover.Words);

            Assert.IsTrue(hover.Track(grid.HoverAt(plain.Bounds.Center, view)?.Item, listed.Listing.Replacements));
            Assert.IsNull(hover.Words, "returning from the gutter also starts a fresh delay");
            clock.Pass(TimeSpan.FromMilliseconds(600));
            Assert.AreEqual("Plain still", hover.Words);
        });
    }

    [TestMethod]
    public void AnUnnamedTileIsIdentifiedByItsFileName()
    {
        var clock = new SteppedClock();
        var hover = new TileHover(clock);
        hover.Track(TestItems.Sized(1, 8, 6) with { Name = "", FileName = "Untitled.png" }, replacements: 0);
        clock.Pass(TimeSpan.FromMilliseconds(600));
        Assert.AreEqual("Untitled.png", hover.Words);
    }

    [TestMethod]
    public void AHoverAtFractionalScalingKeepsTheInputPixel()
    {
        var at = new Point(230 / 1.75, 308 / 1.75);
        Assert.AreEqual(new PixelPoint(-1370, 313), ThumbnailGrid.PointerScreenPoint(at, new PixelPoint(-1600, 5), 1.75));
    }

    [TestMethod]
    public async Task AStationaryPointerUsesTheVisibleTileAfterScrollOrThumbnailSizeChange()
    {
        using var listed = await ListedStash.Open();
        for (var i = 1; i <= 8; i++)
        {
            await listed.Stash.Import($"Still {i}", [(byte)i]);
        }

        var listing = listed.Listing;
        await listing.ShowScope(Scope.All);
        var clock = new SteppedClock();
        var hover = new TileHover(clock);
        ControlThread.Run(() =>
        {
            var size = ThumbnailSize.Medium;
            var grid = LayOut(listing, 240, () => size);
            var view = new Rect(0, 0, 240, 70);
            var pointer = new Point(20, 20);
            var initial = grid.HoverAt(pointer, view)!.Value;
            hover.Track(initial.Item, listing.Replacements);
            clock.Pass(TimeSpan.FromMilliseconds(600));
            Assert.AreEqual(initial.Item.Name, hover.Words);
            Assert.AreEqual(view.Bottom, initial.VisibleBox.Bottom, "anchor stops at the viewport edge");

            var offset = new Vector(0, grid.Rows[1].Y);
            var scrolled = grid.HoverAt(pointer + offset, view.Translate(offset))!.Value;
            Assert.AreNotEqual(initial.Item.Id, scrolled.Item.Id);
            hover.Track(scrolled.Item, listing.Replacements);
            Assert.IsNull(hover.Words);
            clock.Pass(TimeSpan.FromMilliseconds(600));
            Assert.AreEqual(scrolled.Item.Name, hover.Words);
            Assert.IsNull(grid.HoverAt(pointer, view.Translate(offset)), "clipped tiles cannot supply hover");

            size = ThumbnailSize.Huge;
            grid.InvalidateMeasure();
            grid.Measure(new Size(240, double.PositiveInfinity));
            var resized = grid.HoverAt(pointer + offset, view.Translate(offset))!.Value;
            Assert.AreNotEqual(scrolled.Item.Id, resized.Item.Id);
            hover.Track(resized.Item, listing.Replacements);
            Assert.IsNull(hover.Words);
        });
    }

    [TestMethod]
    public async Task AReplacementUnderTheSamePointAndItemNumberStillStartsANewTip()
    {
        using var before = new TestStash();
        using var after = new TestStash();
        await before.OpenCleanly();
        await after.OpenCleanly();
        var old = await before.Import("Before", [1]);
        var replacement = await after.Import("Replacement", [2]);
        Assert.AreEqual(old.Id, replacement.Id);
        after.Tag(replacement.Id, "new");
        var listing = new Listing(failure => Assert.Fail(failure.Sentence));
        await listing.StashOpened(before.Store!);
        await listing.ShowScope(Scope.All);
        var clock = new SteppedClock();
        var hover = new TileHover(clock);
        var view = new Rect(0, 0, 220, 90);
        var at = new Point(20, 20);
        ControlThread.Run(() => hover.Track(LayOut(listing, view.Width).HoverAt(at, view)!.Value.Item, listing.Replacements));
        clock.Pass(TimeSpan.FromMilliseconds(600));
        Assert.AreEqual("Before", hover.Words);

        await listing.Withdraw();
        listing.StashReplaced();
        await listing.StashOpened(after.Store!);
        ControlThread.Run(() => hover.Track(LayOut(listing, view.Width).HoverAt(at, view)!.Value.Item, listing.Replacements));
        Assert.IsNull(hover.Words, "the earlier store's item cannot lend its completed delay to the replacement");
        clock.Pass(TimeSpan.FromMilliseconds(600));
        Assert.AreEqual("Replacement\n#new", hover.Words);

        await after.Store!.Rename(replacement, "Renamed");
        await after.Store.AddTag([replacement], "extra");
        await after.Store.SetFavorite([replacement], true);
        await listing.Refresh();
        ControlThread.Run(() => hover.Track(LayOut(listing, view.Width).HoverAt(at, view)!.Value.Item, listing.Replacements));
        Assert.AreEqual("Renamed\n#new #extra\n★", hover.Words, "the same tile supplies its latest listed facts");
    }

    private static ThumbnailGrid LayOut(Listing listing, double width, Func<ThumbnailSize>? size = null, int decoders = 1)
    {
        var thumbnails = new Thumbnails<Bitmap>(1, decoders, TimeSpan.Zero, FrameBitmap.Of,
            action => Dispatcher.UIThread.Post(action), image => image.Dispose(), failure => Assert.Fail(failure.Sentence));
        var grid = new ThumbnailGrid(listing, thumbnails, size ?? (() => ThumbnailSize.Medium));
        // Measuring lays out the real grid. Arranging would request pixels from a platform the portable checks lack.
        grid.Measure(new Size(width, double.PositiveInfinity));
        return grid;
    }

    [TestMethod]
    public void AnAnimatedItemNamesItsFileAndAStillCarriesNoBadge()
    {
        Assert.AreEqual("GIF", TileBadge.Of(TestItems.Sized(1, 8, 6, animated: true) with { FileName = "dance.gif" }));
        Assert.AreEqual("WEBP", TileBadge.Of(TestItems.Sized(2, 8, 6, animated: true) with { FileName = "clap.webp" }));
        Assert.IsNull(TileBadge.Of(TestItems.Sized(3, 8, 6) with { FileName = "still.gif" }));
        Assert.IsNull(TileBadge.Of(TestItems.Sized(4, 8, 6) with { FileName = "still.webp" }));
    }

    [TestMethod]
    public async Task AnAnimatedWebpTheStoreSavedIsBadgedGifAndASavedStillWebpCarriesNone()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var clip = await stash.ImportFixture("clip", "animated.webp");
        var still = await stash.ImportFixture("still", "still.webp");
        Assert.AreEqual("WEBP", TileBadge.Of(clip));

        await stash.SaveEdit(clip, EditedItems.BlackBoxAndBox);
        await stash.SaveEdit(still, EditedItems.BlackBoxAndBox);

        var listed = (await stash.All()).ToDictionary(item => item.Id);
        Assert.AreEqual("GIF", TileBadge.Of(listed[clip.Id]), "the file handed over is its GIF render (G6, N7)");
        Assert.IsNull(TileBadge.Of(listed[still.Id]));
    }

    [TestMethod]
    public async Task CtrlDADropOnFavoritesAndTheMenusEntriesEachShowOnTheTilesAsTheStoreHoldsIt()
    {
        using var listed = await ListedStash.Open();
        var first = await listed.Stash.Import("first");
        var second = await listed.Stash.Import("second");
        var third = await listed.Stash.Import("third");
        await listed.Stash.Store!.SetFavorite([second], true);
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.Select([first.Id, second.Id, third.Id], second.Id);
        Assert.IsTrue(Starred(listed).SetEquals([second.Id]), "a favorite's tile carries the star, a plain one none (G6)");

        Assert.AreEqual(KeyAction.ToggleFavorite, await listed.Press(Key.D, KeyModifiers.Control, KeyPlace.Grid));
        listed.Keys.Released(Key.D);
        Assert.IsTrue(Starred(listed).SetEquals([first.Id, second.Id, third.Id]), "one of three a favorite: Ctrl+D stars all three (G17)");
        await AssertTilesAsStored(listed);

        Assert.AreEqual(KeyAction.ToggleFavorite, await listed.Press(Key.D, KeyModifiers.Control, KeyPlace.Grid));
        listed.Keys.Released(Key.D);
        Assert.IsEmpty(Starred(listed), "again: none");
        await AssertTilesAsStored(listed);

        // As the Favorites chip's drop sets it.
        Assert.IsTrue((await listed.Organising.SetFavorite([first, third], true)).Succeeded);
        Assert.IsTrue(Starred(listed).SetEquals([first.Id, third.Id]), "a drop on Favorites stars the dropped tiles");
        await AssertTilesAsStored(listed);

        var removed = await listed.ItemMenu.Act(ItemMenuAction.RemoveFromFavorites, null!, [Listed(listed, first.Id)], null, new PasteHide(() => false), _ => { });
        Assert.IsTrue(removed.Succeeded);
        Assert.IsTrue(Starred(listed).SetEquals([third.Id]), "the menu's Remove from Favorites takes the star off its tile");
        var added = await listed.ItemMenu.Act(ItemMenuAction.AddToFavorites, null!, [Listed(listed, second.Id)], null, new PasteHide(() => false), _ => { });
        Assert.IsTrue(added.Succeeded);
        Assert.IsTrue(Starred(listed).SetEquals([second.Id, third.Id]), "and its Add to Favorites puts it on");
        await AssertTilesAsStored(listed);
        Assert.IsEmpty(listed.Notices.Shown, "nothing said: the star is what shows");
    }

    [TestMethod]
    public async Task WhileTheStoreHoldsCtrlDBackTheTilesShowTheFlagItLastAnswered()
    {
        // The store waits for its holder instead of naming it, so the change is asked and not yet written.
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(10));
        var item = await listed.Stash.Import("held");
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.Focus(item.Id);

        Task<KeyAction> pressed;
        using (listed.Stash.HoldStore())
        {
            pressed = listed.Press(Key.D, KeyModifiers.Control, KeyPlace.Grid);
            Assert.IsTrue(listed.Organising.Changing, "the change is asked");
            Assert.IsEmpty(Starred(listed), "no star before the store has written the flag (R9, G28)");
        }

        Assert.AreEqual(KeyAction.ToggleFavorite, await pressed);
        Assert.IsTrue(Starred(listed).SetEquals([item.Id]), "once written, its tile carries the star");
    }

    [TestMethod]
    public async Task ACtrlDTheStoreRefusesLeavesTheTilesAsTheyWereAndIsNamed()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("refused");
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.Focus(item.Id);

        using (listed.Stash.HoldStore())
        {
            await listed.Press(Key.D, KeyModifiers.Control, KeyPlace.Grid);
        }

        StringAssert.StartsWith(listed.Notices.Shown.Single().Text, "Add to Favorites failed", "the failure is named (G35)");
        Assert.IsEmpty(Starred(listed), "as the refusal is named, the tile shows no star: the flag was never written");
        await listed.Listing.Refresh();
        Assert.IsEmpty(Starred(listed), "nor once the store can be read again");
        await AssertTilesAsStored(listed);
    }

    [TestMethod]
    public void TheStarLiesInsideTheSmallestNarrowestTileInItsLowerHalfAwayFromTheBadgesCorner()
    {
        // The shortest full row the smallest size lays out: rows shrink to fill the width when a wide item closes them.
        var smallest = Enumerable.Range(62, 149).Select(hundredths => hundredths / 100.0)
            .SelectMany(ratio => JustifiedRows.Layout(Filled(ratio), 400, JustifiedRows.RowHeight(ThumbnailSize.Small)))
            .Min(row => row.Height);
        Assert.IsLessThan(JustifiedRows.RowHeight(ThumbnailSize.Small), smallest, "a full row can be shorter than the size's height");
        var tile = new Rect(0, 0, smallest * JustifiedRows.NarrowestRatio, smallest);

        var star = TileStar.Place(tile);

        Assert.IsTrue(tile.Contains(star), $"whole inside a {tile.Width:F1} × {tile.Height:F1} tile, so the rings outside it stay clear (G7)");
        Assert.IsGreaterThan(tile.Center.Y, star.Top, "in its lower half, the badge being in its top-left (G6)");
    }

    /// <summary>Items of one ratio, then a widest one, many times over, so rows close on a wide item.</summary>
    private static IReadOnlyList<StashItem> Filled(double ratio) =>
        Enumerable.Range(1, 60).Select(i => TestItems.Sized(i, i % 4 == 0 ? 210 : (int)Math.Round(ratio * 100), 100)).ToList();

    private static HashSet<long> Starred(ListedStash listed) => listed.Listing.Items.Where(TileStar.Of).Select(item => item.Id).ToHashSet();

    private static StashItem Listed(ListedStash listed, long id) => listed.Listing.Items.Single(item => item.Id == id);

    /// <summary>The starred tiles are exactly the items the store holds as favorites.</summary>
    private static async Task AssertTilesAsStored(ListedStash listed)
    {
        var stored = (await listed.Stash.Store!.Favorites()).Value!.Select(item => item.Id).ToHashSet();
        Assert.IsTrue(stored.SetEquals(Starred(listed)), "the tiles show the flag the store holds");
    }
}
