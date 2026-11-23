using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// One path through the real window with real keys (G12, G16, G7, G18, G17): a drop whose batch ends after the flyout
/// hid leaves its notice for the summon, counting what was added and naming the file that was not; Del then <c>Ctrl+Z</c>
/// brings the item back; <c>Ctrl+T</c>, a tag typed and a real click on the grid tag the item. It shows the stash window,
/// clicks its grid with the real pointer and deletes a picture of its own into the Recycle Bin, whose entry the Undo
/// removes.
/// </summary>
[TestClass]
public sealed class StashWindowPathTests
{
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task ADropEndingHiddenThenDelAndCtrlZThenCtrlTTypeAndClickTheGrid()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("path kept.png"));
        TrayDesktop.Run(() => shown.Notices.Close(shown.Notices.Shown.Single()));
        var pictures = new[] { "path one.png", "path two.png", "path three.png", "path four.png" }.Select(shown.Picture).ToList();
        var clip = Path.Combine(Path.GetDirectoryName(pictures[0])!, "clip.mp4");
        File.WriteAllBytes(clip, [1, 2, 3]);
        var dropped = new DropContents([.. pictures.Select(path => new DroppedItem(Path.GetFileName(path), path, null, IsFolder: false)), new DroppedItem("clip.mp4", clip, null, IsFolder: false)], false, false, []);
        var clock = (StoppedClock)shown.Notices.Clock;

        // The drop starts, and the flyout hides in the same turn, before its batch ends.
        var importing = TrayDesktop.Get(() =>
        {
            var started = shown.Import.Drop(dropped);
            shown.Window.Hide();
            return started;
        });
        var four = (await importing).Where(file => file.Item is not null).Select(file => file.Item!).ToList();
        clock.Advance(TimeSpan.FromHours(1));
        TrayDesktop.Run(shown.Notices.Sweep);
        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(default, new SummonAnchor(null)));

        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible && CardSays(shown, "Added 4 · 1 skipped")));
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.IsEffectivelyVisible && block.Text is { } text && text.StartsWith("Skipped clip.mp4 (a video)")), "the file not added, named with its reason"));

        var chosen = four[1];
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(chosen.Id);
            shown.Grid.Focus();
            Tap(shown.Grid, Key.Delete);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.All(item => item.Id != chosen.Id)));
        TrayDesktop.Run(() => Tap(shown.Grid, Key.Z, KeyModifiers.Control));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Any(item => item.Id == chosen.Id)));
        Assert.IsTrue(File.Exists(chosen.FilePath), "back with its file");

        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(chosen.Id);
            shown.Grid.Focus();
            Tap(shown.Grid, Key.T, KeyModifiers.Control);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.TagField.IsVisible && shown.TagBox.IsFocused));
        TrayDesktop.Run(() =>
        {
            Type(shown.TagBox, "cat");
            // The import's notice sits over the grid's foot, where the click goes.
            foreach (var notice in shown.Notices.Shown.ToList())
            {
                shown.Notices.Dismiss(notice);
            }
        });
        // The real pointer lands where the last frame drew the grid.
        await Task.Delay(200);
        using var pointer = TestMouse.PutBackAfterwards();

        TestMouse.Click(TrayDesktop.Get(() => shown.Grid.PointToScreen(new Point(shown.Grid.Bounds.Width / 2, shown.Grid.Bounds.Height - 12))));

        await Eventually(() => shown.TagsOf(chosen.Id).Contains("cat"));
        TrayDesktop.Run(() => Assert.IsFalse(shown.TagField.IsVisible));
    }

    [TestMethod]
    public async Task CtrlDInAllStarsOnlyTheMiddleTileAndClickThenSpaceShowsItsStarBeforeArrowsStepToBothEnds()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        await shown.OpenStash(
            shown.Picture("path red.png", 32, 32, (_, _) => SKColors.Maroon),
            shown.Picture("path green.png", 32, 32, (_, _) => SKColors.Green),
            shown.Picture("path blue.png", 32, 32, (_, _) => SKColors.Navy));
        TransferredStash.CloseNotices(shown);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        await TransferredStash.EveryTileShows(shown, new()
        {
            ["path red"] = (uint)SKColors.Maroon,
            ["path green"] = (uint)SKColors.Green,
            ["path blue"] = (uint)SKColors.Navy,
        }, "the three pictures in All");
        var tiles = TrayDesktop.Get(() => shown.Grid.Rows.Single().Tiles.ToArray());
        Assert.HasCount(3, tiles);
        var middle = tiles[1].Id;

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(Scope.All, shown.Listing.LitChip);
            shown.Listing.Focus(middle);
            shown.Grid.Focus();
            Assert.AreSame(shown.Grid, shown.Focused);
            Tap(shown.Grid, Key.D, KeyModifiers.Control);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Any(item => item.Id == middle && item.Favorite)));
        var rendered = await RenderedWindow.Of(shown.Window);
        TrayDesktop.Run(() =>
        {
            Assert.IsGreaterThan(0, TileStarTests.StarInk(rendered, TileStarTests.TileBox(shown, middle)), "the middle tile shows the star (G6)");
            foreach (var tile in new[] { tiles[0], tiles[2] })
            {
                Assert.AreEqual(0, TileStarTests.StarInk(rendered, TileStarTests.TileBox(shown, tile.Id)), $"tile {tile.Id} shows no star (G6)");
            }

            ChipRowRoomTests.AssertScopeGlyphs(shown);
            RaiseClick(shown.Chip("Favorites"));
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Scope == Scope.Favorites));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            CollectionAssert.AreEqual(new[] { middle }, shown.Grid.Rows.SelectMany(row => row.Tiles).Select(tile => tile.Id).ToArray(),
                "the Favorites chip shows only the starred tile (G4)");
            RaiseClick(shown.Chip("All"));
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Scope == Scope.All));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            CollectionAssert.AreEqual(tiles.Select(tile => tile.Id).ToArray(), shown.Grid.Rows.SelectMany(row => row.Tiles).Select(tile => tile.Id).ToArray(),
                "the All chip restores all three tiles (G4)");
            shown.Search.Focus();
        });

        TestMouse.Click(TrayDesktop.Get(() => shown.TileOnScreen(middle)));
        await Eventually(() => TrayDesktop.Get(() => shown.Grid.IsFocused && shown.Listing.Selection.SequenceEqual([middle])));
        TrayDesktop.Run(() =>
        {
            Tap(shown.Grid, Key.Space);
            Assert.AreEqual(middle, shown.Preview.Item?.Id);
            Assert.AreEqual(FavoriteStar.Filled, ((TextBlock)PreviewStarTests.StarButton(shown).Content!).Text, "the preview opens with the same favorite flag (G19)");

            Tap(shown.PreviewPicture, Key.Right);
            Assert.AreEqual(tiles[2].Id, shown.Preview.Item?.Id, "Right shows the next item in All (G19)");
            CollectionAssert.AreEqual(new[] { tiles[2].Id }, shown.Listing.Selection.ToList());
            Assert.AreEqual(FavoriteStar.Outlined, ((TextBlock)PreviewStarTests.StarButton(shown).Content!).Text, "the last item is plain");

            Tap(shown.PreviewPicture, Key.Right);
            Assert.AreEqual(tiles[2].Id, shown.Preview.Item?.Id, "Right at the last item stays there (G19)");
            CollectionAssert.AreEqual(new[] { tiles[2].Id }, shown.Listing.Selection.ToList());

            Tap(shown.PreviewPicture, Key.Left);
            Assert.AreEqual(middle, shown.Preview.Item?.Id);
        });

        await PreviewStepButtonsTests.ClickStep(shown, shown.Preview.StepButtons.Back);
        await Eventually(() => TrayDesktop.Get(() => shown.Preview.Item?.Id == tiles[0].Id));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(tiles[0].Id, shown.Preview.Item?.Id, "Left followed by Previous reaches the first item in All (G19)");
            Assert.IsTrue(shown.Preview.IsOpen);
            CollectionAssert.AreEqual(new[] { tiles[0].Id }, shown.Listing.Selection.ToList());
        });
    }

    private static bool CardSays(ShownWindow shown, string text) =>
        shown.Window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == text && block.IsEffectivelyVisible);
}
