using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ImageStash.Look;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

[TestClass]
public sealed class ThumbnailGridHoverTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HoverIdentifiesAStillWithoutChangingFocusSelectionCopyOrDrag(bool pinned)
    {
        using var mouse = TestMouse.PutBackAfterwards();
        var drags = new PresetDrags();
        using var shown = ShownWindow.Open(drags);
        TrayDesktop.Run(() =>
        {
            if (pinned)
            {
                shown.Window.SummonPinned();
            }

            shown.Window.RequestedThemeVariant = pinned ? ThemeVariant.Dark : ThemeVariant.Light;
        });
        var items = await shown.OpenStash(
            shown.Picture("Favorite.png", 40, 41, (_, _) => SKColors.Transparent),
            shown.Picture("Plain.png", 40, 42, (_, _) => SKColors.Transparent));
        var favorite = items.Single(item => item.Name == "Favorite");
        var plain = items.Single(item => item.Name == "Plain");
        await shown.Tag([favorite], "cat mood reaction spare");
        Assert.IsTrue((await shown.Listing.Store!.SetFavorite([favorite], true)).Single().Succeeded);
        await TrayDesktop.Get(() => shown.Listing.Refresh());
        await shown.ClickSearchInFront();
        TrayDesktop.Run(() => shown.Listing.Focus(plain.Id));
        var at = TrayDesktop.Get(() => shown.TileOnScreen(favorite.Id));
        await Eventually(() => TrayDesktop.Get(() => shown.Window.InputHitTest(shown.Window.PointToClient(at)) is ThumbnailGrid));
        var (ground, lighter) = TrayDesktop.Get(() =>
            (AppliedLook.Of(shown.Grid, LookKeys.TileGround), AppliedLook.Of(shown.Grid, LookKeys.TileHoverGround)));
        await Eventually(() => Near(ground, Pixel(at)));

        TestMouse.MoveTo(at);
        await Eventually(() => Near(lighter, Pixel(at)) && HoverBorderShows(shown, favorite.Id));
        await TipShows(shown, favorite.Id, "Favorite\n#cat #mood #reaction\n★");
        TrayDesktop.Run(() =>
        {
            var root = PopupWindow(shown)!;
            var popupPoint = root.PointToScreen(new Point(root.ClientSize.Width / 2, root.ClientSize.Height / 2));
            Assert.AreEqual(shown.Window.TryGetPlatformHandle()!.Handle, FrontWindow.Under(popupPoint),
                "even a point inside the visible tip still hits the stash window");
            Assert.IsTrue(shown.Search.IsFocused);
            Assert.AreEqual(plain.Id, shown.Listing.SelectedItems.Single().Id);
            Tap(shown.Search, Key.C, KeyModifiers.Control);
        });
        await Eventually(() => TrayDesktop.Get(() => drags.LastWritten.SequenceEqual([plain.Id])));

        TestMouse.Click(at);
        await Eventually(() => TrayDesktop.Get(() => shown.Grid.IsFocused && shown.Listing.Focused == favorite.Id));
        var next = TrayDesktop.Get(() => shown.TileOnScreen(plain.Id));
        TestMouse.MoveTo(next);
        await TipShows(shown, plain.Id, "Plain");
        TestMouse.Press(next);
        try
        {
            await Eventually(() => TrayDesktop.Get(() => !PopupFor(shown).IsOpen));
            var move = TrayDesktop.Get(() => (int)Math.Ceiling(12 * shown.Window.RenderScaling));
            TestMouse.MoveTo(new PixelPoint(next.X - move, next.Y));
            await Eventually(() => TrayDesktop.Get(() => drags.Dragged.Any()));
        }
        finally
        {
            TestMouse.Release(next);
        }

        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { plain.FilePath }, drags.Dragged.Single().ToArray()));
    }

    [TestMethod]
    public async Task AScrolledGridAnchorsTheTipToTheVisibleTileAndReplacesItUnderAStationaryPointer()
    {
        using var mouse = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(Enumerable.Range(1, 114).Select(i =>
            shown.Picture($"Still {i:00}.png", 32, 34 + i, (_, _) => SKColors.Transparent)).ToArray());
        await shown.Tag(items.Where((_, i) => i % 3 != 0).ToArray(), "browse");
        await shown.ClickSearchInFront();
        var chip = TrayDesktop.Get(() => shown.Chip("#browse"));
        TestMouse.Click(await ChipRowTests.ClickPoint(chip));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "browse"));
        var scroller = TrayDesktop.Get(() => shown.Grid.FindAncestorOfType<ScrollViewer>()!);
        await ShowHoveredRow(shown, scroller);

        TestMouse.Click(await ChipRowTests.ClickPoint(chip));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == ""));
        var id = await ShowHoveredRow(shown, scroller);
        TrayDesktop.Run(() => shown.OpenPreview(shown.Listing.Items.Single(item => item.Id == id)));
        await Eventually(() => TrayDesktop.Get(() => !PopupFor(shown).IsOpen));
        TrayDesktop.Run(() => Tap(shown.Preview, Key.Escape));
        await Eventually(() => TrayDesktop.Get(() => !shown.Preview.IsOpen));
        TestMouse.MoveTo(TestMouse.Centre(TrayDesktop.Get(() => shown.Search)));

        await ShowHoveredRow(shown, scroller);
        TrayDesktop.Run(shown.Window.Hide);
        await Eventually(() => TrayDesktop.Get(() => !PopupFor(shown).IsOpen));
    }

    private static async Task<long> ShowHoveredRow(ShownWindow shown, ScrollViewer scroller)
    {
        var (row, next) = TrayDesktop.Get(() => (shown.Grid.Rows[4], shown.Grid.Rows[5]));
        const int clippedTop = 12;
        ScrollTo(row);
        var first = row.Tiles[0];
        var at = TrayDesktop.Get(() => shown.TileOnScreen(first.Id));
        TestMouse.MoveTo(at);
        var firstName = TrayDesktop.Get(() => shown.Listing.Items[first.Index].Name);
        await TipShows(shown, first.Id, firstName, compareNameOnly: true);

        var pointer = TestMouse.Position();
        ScrollTo(next);
        var hit = TrayDesktop.Get(() => next.Tiles.Single(tile => tile.Bounds.ContainsExclusive(shown.Grid.PointToClient(pointer))));
        var nextName = TrayDesktop.Get(() => shown.Listing.Items[hit.Index].Name);
        await TipShows(shown, hit.Id, nextName, compareNameOnly: true);
        Assert.AreEqual(pointer, TestMouse.Position(), "the pointer has not moved");
        return hit.Id;

        void ScrollTo(GridRow target) => TrayDesktop.Run(() =>
        {
            scroller.Offset = new Vector(0, target.Y + clippedTop);
            shown.Window.UpdateLayout();
            Assert.AreEqual(target.Y + clippedTop, scroller.Offset.Y, 1, "the fixture can scroll this far");
        });
    }

    private static async Task TipShows(ShownWindow shown, long id, string words, bool compareNameOnly = false)
    {
        await Eventually(() => TrayDesktop.Get(() =>
        {
            if (PopupWindow(shown) is not { IsVisible: true } root)
            {
                return false;
            }

            return root.GetVisualDescendants().OfType<TextBlock>().Any(text => compareNameOnly
                ? text.Text?.Split('\n')[0] == words
                : text.Text == words);
        }));
        TrayDesktop.Run(() => CheckPlacement(shown, id));
    }

    private static void CheckPlacement(ShownWindow shown, long id)
    {
        var popup = PopupFor(shown);
        var root = PopupWindow(shown)!;
        root.UpdateLayout();
        var tile = shown.Grid.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Id == id);
        var viewport = shown.Grid.FindAncestorOfType<ScrollContentPresenter>()!;
        var view = new Rect(viewport.TranslatePoint(default, shown.Grid)!.Value, viewport.Bounds.Size);
        var screen = shown.Window.Screens.ScreenFromPoint(TestMouse.Position())!;
        var display = new Rect(shown.Grid.PointToClient(screen.Bounds.TopLeft), shown.Grid.PointToClient(screen.Bounds.BottomRight));
        var visibleBox = tile.Bounds.Intersect(view).Intersect(display);
        Assert.AreEqual(visibleBox, popup.PlacementRect!.Value, "the anchor is the visible part of this tile on the pointer's display");
        var work = screen.WorkingArea;
        var start = root.PointToScreen(default);
        var end = root.PointToScreen(new Point(root.ClientSize.Width, root.ClientSize.Height));
        Assert.IsTrue(start.X >= work.X && start.Y >= work.Y && end.X <= work.Right + 1 && end.Y <= work.Bottom + 1,
            $"the popup fits the display's working area: {start} to {end}, within {work}");
        var tileTop = shown.Grid.PointToScreen(visibleBox.TopLeft);
        var tileBottom = shown.Grid.PointToScreen(visibleBox.BottomRight);
        var below = start.Y - tileBottom.Y;
        var above = tileTop.Y - end.Y;
        var maximumGap = 8 * root.RenderScaling;
        Assert.IsTrue(start.X < tileBottom.X && end.X > tileTop.X
            && (below >= 0 && below <= maximumGap || above >= 0 && above <= maximumGap),
            $"the popup sits beside the visible tile: {start} to {end}, tile {tileTop} to {tileBottom}");
    }

    private static Popup PopupFor(ShownWindow shown) => shown.Grid.GetLogicalChildren().OfType<Popup>().Single();

    private static WindowBase? PopupWindow(ShownWindow shown) =>
        PopupFor(shown) is { IsOpen: true, Child: { } child } ? TopLevel.GetTopLevel(child) as WindowBase : null;

    private static bool HoverBorderShows(ShownWindow shown, long id)
    {
        var (edge, brush) = TrayDesktop.Get(() =>
        {
            var box = shown.Grid.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Id == id).Bounds;
            return (shown.Grid.PointToScreen(new Point(box.Center.X, box.Y)),
                AppliedLook.Of(shown.Grid, LookKeys.TileHoverBorder));
        });
        return Enumerable.Range(0, 3).Any(dy => Near(brush, Pixel(new PixelPoint(edge.X, edge.Y + dy)), 55));
    }

    private static bool Near(Color wanted, Color seen, int distance = 15) =>
        Math.Abs(wanted.R - seen.R) + Math.Abs(wanted.G - seen.G) + Math.Abs(wanted.B - seen.B) <= distance;

    private static Color Pixel(PixelPoint at)
    {
        var result = new WindowsScreen().Grab(new PixelRect(at, new PixelSize(1, 1)), withPointer: false);
        Assert.IsTrue(result.Succeeded, $"could not read the screen at {at}");
        var bytes = result.Value!.Pixels;
        return Color.FromRgb(bytes[2], bytes[1], bytes[0]);
    }
}
