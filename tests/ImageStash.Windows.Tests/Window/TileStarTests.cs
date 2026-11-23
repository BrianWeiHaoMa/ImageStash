using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

[TestClass]
public sealed class TileStarTests
{
    [TestMethod]
    public async Task SmallTilesShowTheStarsAndTheGifBadgeOverTheirPictures()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(
            shown.Fixture("animated.gif", "animated.gif"),
            shown.Picture("white.png", 64, 64, (_, _) => SKColors.White),
            shown.Picture("black.png", 64, 64, (_, _) => SKColors.Black),
            shown.Picture("clear.png", 64, 64, (x, _) => x < 38 ? SKColors.Magenta : SKColors.Transparent),
            shown.Picture("plain.png", 64, 64, (_, _) => SKColors.Navy));
        var (animated, white, black, clear, plain) = (items[0], items[1], items[2], items[3], items[4]);
        TransferredStash.CloseNotices(shown);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(plain.Id);
            shown.Search.Focus();
            Tap(shown.Search, Key.OemMinus, KeyModifiers.Control);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Settings.Current.ThumbnailSize == ThumbnailSize.Small
            && shown.Grid.Rows is [{ Height: 64, Tiles.Count: 5 }]));
        await TransferredStash.EveryTileShows(shown, new()
        {
            [animated.Name] = (uint)SKColors.Red,
            [white.Name] = (uint)SKColors.White,
            [black.Name] = (uint)SKColors.Black,
            [clear.Name] = (uint)SKColors.Magenta,
            [plain.Name] = (uint)SKColors.Navy,
        }, "the small thumbnails");

        var before = await RenderedWindow.Of(shown.Window);
        var boxes = TrayDesktop.Get(() => items.ToDictionary(item => item.Id, item => TileBox(shown, item.Id)));
        var animatedBox = boxes[animated.Id];
        var badge = new Rect(animatedBox.X + 6, animatedBox.Y + 6, 35, 18);
        Assert.IsFalse(badge.Intersects(TileStar.Place(animatedBox)), "the GIF badge and the star have separate places (G6)");
        Assert.IsGreaterThan(0, WhiteInk(before, InPixels(badge, before.Scaling)), "the GIF badge's white lettering");

        var favorites = new[] { animated, white, black, clear };
        var saved = await shown.Listing.Store!.SetFavorite(favorites, true);
        Assert.IsTrue(saved.All(result => result.Succeeded));
        await TrayDesktop.Get(() => shown.Listing.Refresh());
        var rendered = await RenderedWindow.Of(shown.Window);

        foreach (var item in favorites)
        {
            var box = boxes[item.Id];
            var baseline = before.Colors(StarInterior(box, before.Scaling));
            Assert.HasCount(1, baseline, $"{item.Name}: the picture under the mark before it became a favorite");
            var ground = baseline.Single();
            Assert.IsGreaterThan(0, StarInk(rendered, box), $"{item.Name}: the star's white pixels (G6)");
            Assert.IsTrue(rendered.Colors(StarInterior(box, rendered.Scaling)).Any(color =>
                RenderedWindow.Between(color, 0xFF000000, ground) && (color != ground || ground == 0xFF000000)),
                $"{item.Name}: dark backing visible beside the white glyph (G6)");
        }

        Assert.IsGreaterThan(0, WhiteInk(rendered, InPixels(badge, rendered.Scaling)), "the animated favorite still has the GIF badge's white lettering");
        Assert.AreEqual(0, StarInk(rendered, boxes[plain.Id]), "no star pixels on the plain tile (G6)");
    }

    internal static Rect TileBox(ShownWindow shown, long id)
    {
        var tile = shown.Grid.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Id == id);
        var corner = shown.Grid.TranslatePoint(new Point(tile.X, tile.Y), (Control)shown.Window.Content!)!.Value;
        return new Rect(corner, new Size(tile.Width, tile.Height));
    }

    internal static int StarInk(RenderedWindow rendered, Rect tile) => WhiteInk(rendered, StarInterior(tile, rendered.Scaling));

    // The inset keeps the sampling rectangle inside the ellipse, away from the picture around its curved edge.
    private static Rect StarInterior(Rect tile, double scaling) => InPixels(TileStar.Place(tile).Deflate(4), scaling);

    private static Rect InPixels(Rect area, double scaling) => new(area.Position * scaling, area.Size * scaling);

    private static int WhiteInk(RenderedWindow rendered, Rect pixels) =>
        rendered.ColorCounts(pixels.X, pixels.Y, pixels.Width, pixels.Height).Where(pair =>
        {
            var color = Color.FromUInt32(pair.Key);
            return color.R >= 245 && color.G >= 245 && color.B >= 245;
        }).Sum(pair => pair.Value);
}
