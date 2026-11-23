using Avalonia;
using Avalonia.Input;
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
