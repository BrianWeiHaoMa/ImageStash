using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Look;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The grid as the real window draws it and as a real click reaches it (G6, G7, E4): scrolled by one viewport, by three,
/// to the end and back, every tile in view shows its own picture; Down from the first item brings each focused tile into
/// view with its ring; a selected tile's ring stands off it by a gap of the window's ground at each thumbnail size,
/// rounded and clear of its neighbours, keyboard focus filling that gap; a real click between tiles, beside the last row, below it or in the margin round the rows moves
/// focus to the grid and leaves the selection, with no menu and no drag, so <c>Ctrl+A</c> then selects every item and
/// typing returns to the box.
/// These checks click the stash window at the primary display's left edge with real mouse input.
/// </summary>
[TestClass]
public sealed class ThumbnailGridTests
{
    private static readonly SKColor First = new(0xC8, 0x28, 0x0A);

    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task AnItemUsedFromDeepInRecentMovesToTheTopWithTheViewFollowingItsSelection()
    {
        using var shown = await Pile();
        var scroller = TrayDesktop.Get(() => Scroller(shown));
        var items = TrayDesktop.Get(() => shown.Listing.Items.ToList());
        await TrayDesktop.Get(() => shown.Listing.Store!.RecordUses(items, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.Recent));
        var deep = TrayDesktop.Get(() => shown.Listing.Items[^1]);
        TrayDesktop.Run(() => shown.Listing.Focus(deep.Id));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => scroller.Offset.Y) > scroller.Viewport.Height);

        // As a handoff records its use and lists again: the item moves to the top, its selection with it (G4, G10).
        await TrayDesktop.Get(() => shown.Listing.Store!.RecordUses([deep], DateTimeOffset.UtcNow));
        await TrayDesktop.Get(() => shown.Listing.Refresh());

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Items[0].Id == deep.Id && scroller.Offset.Y < scroller.Viewport.Height / 2));
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { deep.Id }, shown.Listing.Selection.ToList(), "the selection it was left with, showing (G1)"));
    }

    [TestMethod]
    public async Task ScrolledByOneViewportByThreeToTheEndAndBackEveryTileInViewShowsItsPicture()
    {
        using var shown = await Pile();
        var scroller = TrayDesktop.Get(() => Scroller(shown));
        await EveryTileInViewShowsItsPicture(shown, "at the top");
        foreach (var (viewports, where) in new[] { (1.0, "one viewport down"), (3.0, "three viewports down") })
        {
            TrayDesktop.Run(() => scroller.Offset = new Vector(0, scroller.Viewport.Height * viewports));
            await EveryTileInViewShowsItsPicture(shown, where);
        }

        TrayDesktop.Run(scroller.ScrollToEnd);
        await EveryTileInViewShowsItsPicture(shown, "at the end");
        TrayDesktop.Run(() => Assert.AreEqual(scroller.Extent.Height - scroller.Viewport.Height, scroller.Offset.Y, 0.5, "scrolled to the end"));
        TrayDesktop.Run(scroller.ScrollToHome);
        await EveryTileInViewShowsItsPicture(shown, "back at the top");
    }

    [TestMethod]
    public async Task DownFromTheFirstItemBringsEachFocusedTileIntoViewWithItsRingPastTheFirstPage()
    {
        using var shown = await Pile();
        var scroller = TrayDesktop.Get(() => Scroller(shown));
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(shown.Listing.Items[0].Id);
            shown.Grid.Focus(NavigationMethod.Tab);
        });
        for (var step = 1; TrayDesktop.Get(() => scroller.Offset.Y) < 2 * TrayDesktop.Get(() => scroller.Viewport.Height); step++)
        {
            Assert.IsLessThan(80, step, "two viewports scrolled within eighty steps");
            TrayDesktop.Run(() => Press(shown.Grid, Key.Down));
            await TheFocusedTileShowsInViewWithItsRing(shown, $"after Down {step}");
        }
    }

    [TestMethod]
    public async Task ASelectedTilesRingStandsOffItByAGapOfTheWindowsGroundAtEachSizeAndKeyboardFocusFillsTheGap()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = ShownWindow.Open();
        // Upright, so the three keep one row at the largest size in the flyout; each its own size, so none is a duplicate (G12).
        await shown.OpenStash(shown.Picture("one.png", 40, 50, (_, _) => First), shown.Picture("two.png", 40, 51, (_, _) => First), shown.Picture("three.png", 40, 52, (_, _) => First));
        await Eventually(() => TrayDesktop.Get(() => shown.Grid.Rows is [{ Tiles.Count: 3 }, ..]));
        var middle = TrayDesktop.Get(() => shown.Grid.Rows[0].Tiles.OrderBy(tile => tile.X).ElementAt(1).Id);
        TrayDesktop.Run(() =>
        {
            shown.Press(middle);
            shown.Release(middle);
            shown.Search.Focus();
            for (var step = 0; step < 2; step++)
            {
                Press(shown.Search, Key.OemMinus, KeyModifiers.Control);
            }
        });

        foreach (var size in new[] { ThumbnailSize.Small, ThumbnailSize.Medium, ThumbnailSize.Large })
        {
            await Eventually(() => TrayDesktop.Get(() => shown.Settings.Current.ThumbnailSize == size && shown.Listing.Selection.SequenceEqual([middle]) && shown.Grid.Rows[0].Tiles.Count == 3));
            await RingedEventually(shown, middle, focused: false, $"{size}, the search box focused");
            TrayDesktop.Run(() => Press(shown.Search, Key.OemPlus, KeyModifiers.Control));
        }

        TrayDesktop.Run(() => shown.Grid.Focus());
        await RingedEventually(shown, middle, focused: true, "the grid focused");

        // Two selected side by side, the earlier focused: the later one's ring leaves its focus mark whole on their shared side (G7).
        var right = TrayDesktop.Get(() => shown.Grid.Rows[0].Tiles.OrderBy(tile => tile.X).ElementAt(2).Id);
        TrayDesktop.Run(() =>
        {
            shown.Listing.Select([middle, right], middle);
            shown.Grid.Focus();
        });
        await RingedEventually(shown, middle, focused: true, "two side by side, the earlier focused");
    }

    /// <summary>
    /// Reads the screen round the tile of <paramref name="id"/> until, on each of its four sides, the pixels from its edge
    /// outward show the gap (the window's ground, or the focus ring while <paramref name="focused"/>) and then the
    /// selection ring, the ring's outer corners show the ground, and a neighbour beside the tile shows its picture to its
    /// edge; fails, naming what each side showed, after ten seconds.
    /// </summary>
    private static async Task RingedEventually(ShownWindow shown, long id, bool focused, string when)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            await Task.Delay(200);
            var wrong = Ringed(shown, id, focused);
            if (wrong.Count == 0)
            {
                return;
            }

            Assert.IsLessThan(deadline, DateTime.UtcNow, $"{when}: {string.Join("; ", wrong)} (G6, G7)");
        }
    }

    private static List<string> Ringed(ShownWindow shown, long id, bool focused)
    {
        var (origin, scale, box, others, ground, mark, focus, gapWidth) = TrayDesktop.Get(() =>
        {
            var tiles = shown.Grid.Rows.SelectMany(row => row.Tiles).ToList();
            Rect InWindow(GridTile tile) => new(shown.Grid.TranslatePoint(new Point(tile.X, tile.Y), shown.Window)!.Value, new Size(tile.Width, tile.Height));
            var tile = tiles.Single(tile => tile.Id == id);
            // The window's ground: the grid's blank space below the one row.
            var blank = shown.Window.PointToScreen(new Point(InWindow(tile).Center.X, InWindow(tile).Bottom + 24));
            return (shown.Window.PointToScreen(default), shown.Window.RenderScaling, InWindow(tile), tiles.Where(other => other.Id != id).Select(InWindow).ToList(),
                Pixel(blank), AppliedLook.Of(shown.Grid, LookKeys.SelectionRing), AppliedLook.Of(shown.Grid, LookKeys.FocusRing),
                shown.Grid.TryFindResource(LookKeys.SelectionRingGap, shown.Grid.ActualThemeVariant, out var gap) && gap is Thickness thickness ? thickness.Left : 0);
        });
        var picture = Color.FromRgb(First.Red, First.Green, First.Blue);
        var classes = new[] { ("tile", picture), ("ground", ground), ("ring", mark), ("focus", focus) };
        string Nearest(Color seen) => classes.MinBy(each => Distance(each.Item2, seen)).Item1;
        var wrong = new List<string>();
        // From the tile's edge outward across the space between tiles, one physical pixel at a time.
        var space = (int)Math.Ceiling(JustifiedRows.Gap * scale);
        foreach (var (side, start, step) in new[]
        {
            ("left", new Point(box.X, box.Center.Y), new Vector(-1, 0)),
            ("right", new Point(box.Right, box.Center.Y), new Vector(1, 0)),
            ("top", new Point(box.Center.X, box.Y), new Vector(0, -1)),
            ("bottom", new Point(box.Center.X, box.Bottom), new Vector(0, 1)),
        })
        {
            var edge = new Point(origin.X + start.X * scale, origin.Y + start.Y * scale);
            var colours = Enumerable.Range(0, space).Select(k => Pixel(new PixelPoint((int)Math.Floor(edge.X + step.X * (k + 0.5)), (int)Math.Floor(edge.Y + step.Y * (k + 0.5))))).ToList();
            var seen = colours.Select(Nearest).ToList();
            // The middle of the gap shows the gap's own colour: a ring over it, the neighbour's included, would show there.
            var inGap = Pixel(new PixelPoint((int)Math.Floor(edge.X + step.X * gapWidth / 2 * scale), (int)Math.Floor(edge.Y + step.Y * gapWidth / 2 * scale)));
            // The ring by the accent itself, near exactly: its band, two wide, holds a whole pixel, where a narrower band
            // lying between pixels shows only as a blend; so a ring in another colour is no ring (F10).
            var ring = colours.FindIndex(colour => Distance(colour, mark) <= 24);
            if (Nearest(inGap) != (focused ? "focus" : "ground") || ring < 0)
            {
                wrong.Add($"{side}: the gap's middle {Nearest(inGap)} {inGap}; outward {string.Join(" ", seen.Zip(colours, (name, colour) => $"{name} {colour}"))}");
            }
        }

        // Beside the tile, a neighbour: no ring at its edge (a pixel there may straddle it, the tiles lying between pixels),
        // and its own picture just inside, so the ring never lies over it.
        foreach (var other in others.Where(other => Math.Abs(other.Center.Y - box.Center.Y) < 1))
        {
            foreach (var (inside, shows) in new[] { (0.5, "no ring"), (1.5, "tile") })
            {
                var near = other.Center.X < box.Center.X ? other.Right - inside : other.X + inside;
                var at = new PixelPoint((int)Math.Floor(origin.X + near * scale), (int)Math.Floor(origin.Y + box.Center.Y * scale));
                var seen = Nearest(Pixel(at));
                if (shows == "no ring" ? seen == "ring" : seen != shows)
                {
                    wrong.Add($"the neighbour {inside} inside its edge at {at} shows {Pixel(at)}, {seen}");
                }
            }
        }

        // The ring's outer corners are rounded as the tile's are: the corner of the square round the ring shows the ground.
        var reach = JustifiedRows.Gap - 0.5;
        foreach (var corner in new[] { box.TopLeft + new Vector(-reach, -reach), box.TopRight + new Vector(reach, -reach), box.BottomLeft + new Vector(-reach, reach), box.BottomRight + new Vector(reach, reach) })
        {
            var at = new PixelPoint((int)Math.Floor(origin.X + corner.X * scale), (int)Math.Floor(origin.Y + corner.Y * scale));
            if (Nearest(Pixel(at)) != "ground")
            {
                wrong.Add($"the ring's corner at {at} shows {Pixel(at)}");
            }
        }

        return wrong;
    }

    private static Color Pixel(PixelPoint at)
    {
        var grab = new WindowsScreen().Grab(new PixelRect(at, new PixelSize(1, 1)), withPointer: false);
        Assert.IsTrue(grab.Succeeded, $"the screen at {at} could not be read");
        var pixels = grab.Value!.Pixels;
        return Color.FromRgb(pixels[2], pixels[1], pixels[0]);
    }

    private static int Distance(Color one, Color other) => Math.Abs(one.R - other.R) + Math.Abs(one.G - other.G) + Math.Abs(one.B - other.B);

    [TestMethod]
    public async Task ARealClickBetweenTilesBesideOrBelowTheLastRowOrInTheMarginFocusesTheGridAndKeepsTheSelection()
    {
        var drags = new PresetDrags();
        using var shown = ShownWindow.Open(drags: drags);
        var items = await shown.OpenStash(
            shown.Picture("one.png", 8, 8, (_, _) => First),
            shown.Picture("two.png", 8, 8, (_, _) => new SKColor(0x0A, 0xB4, 0x3C)),
            shown.Picture("three.png", 8, 8, (_, _) => new SKColor(0x1E, 0x3C, 0xDC)));
        TrayDesktop.Run(() =>
        {
            foreach (var notice in shown.Notices.Shown.ToList())
            {
                shown.Notices.Close(notice);
            }
        });
        var (menus, dragged) = (0, 0);
        TrayDesktop.Run(() =>
        {
            shown.Grid.ItemMenuRequested += _ => menus++;
            shown.Grid.DragStarted += _ => dragged++;
        });
        // The grid shows once the stash holds items, and a click reaches only what has been drawn: its first frame first.
        await Eventually(() => TrayDesktop.Get(() => shown.Window.InputHitTest(shown.Window.PointToClient(TileCentre(shown, items[0].Id))) is ThumbnailGrid));
        var places = TrayDesktop.Get(() => BlankPlaces(shown));
        using var pointer = TestMouse.PutBackAfterwards();

        foreach (var (what, at) in places)
        {
            TrayDesktop.Run(() =>
            {
                shown.Listing.Focus(items[1].Id);
                shown.Search.Focus();
            });
            TestMouse.Click(at);
            await Eventually(() => TrayDesktop.Get(() => ReferenceEquals(shown.Focused, shown.Grid)));
            TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { items[1].Id }, shown.Listing.Selection.ToList(), $"a click {what} leaves the selection (G7)"));

            TestMouse.RightClick(at);
            TestMouse.Press(at);
            TestMouse.MoveTo(new PixelPoint(at.X + 10, at.Y));
            TestMouse.Release(new PixelPoint(at.X + 10, at.Y));
            await Task.Delay(300);
            TrayDesktop.Run(() =>
            {
                Assert.IsNull(shown.OpenMenu, $"no menu after a right-click {what}");
                Assert.AreEqual(0, menus, $"no item menu asked for {what}");
                Assert.AreEqual(0, dragged, $"no drag from a press {what}");
                CollectionAssert.AreEqual(new[] { items[1].Id }, shown.Listing.Selection.ToList(), $"the selection kept {what}");
            });
        }

        Assert.IsEmpty(drags.Dragged, "nothing dragged out");
        TrayDesktop.Run(() => Press(shown.Grid, Key.A, KeyModifiers.Control));
        TrayDesktop.Run(() => CollectionAssert.AreEquivalent(items.Select(item => item.Id).ToList(), shown.Listing.Selection.ToList(), "Ctrl+A after the click selects every item (G7, G17)"));
        TrayDesktop.Run(() => Type(shown.Grid, "t"));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Search.IsFocused, "typing returns focus to the box (G7)");
            Assert.AreEqual("t", shown.Search.Text);
        });

        // A click on a tile still makes it the whole selection.
        TrayDesktop.Run(() => shown.Search.Text = "");
        await TrayDesktop.Get(() => shown.Listing.Settle());
        TrayDesktop.Run(() => shown.Listing.SelectAll());
        TestMouse.Click(TrayDesktop.Get(() => TileCentre(shown, items[2].Id)));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Selection.SequenceEqual([items[2].Id])));
        TrayDesktop.Run(() => Assert.AreSame(shown.Grid, shown.Focused));
    }

    [TestMethod]
    public async Task AnImportConfirmedWhileATilePlaysReplacesTheStashAndTheGridShowsTheImportedPicturesNotTheReplacedOnesOfTheSameIdentity()
    {
        // The check's own pointer only: a real cursor resting over the grid would hover a tile of its own.
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = ShownWindow.Open();
        var red = new SKColor(0xD0, 0x20, 0x20);
        var items = await shown.OpenStash(shown.Picture("image.png", 8, 6, (_, _) => red), shown.Fixture("animated.gif", "import playing.gif"));
        var (image, playing) = (items.Single(item => item.Name == "image"), items.Single(item => item.Name == "import playing"));
        TransferredStash.CloseNotices(shown);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        await TransferredStash.EveryTileShows(shown, new() { ["image"] = (uint)red }, "the replaced stash");
        TrayDesktop.Run(() => shown.Listing.Focus(playing.Id));
        await Eventually(() => Held(playing.FilePath));
        var folder = TrayDesktop.Get(() => shown.Listing.Store!.Folder.Root);
        // The imported item 1 has the replaced item 1's name, file name and revision, and another picture.
        var blue = new SKColor(0x20, 0x30, 0xD0);
        var green = new SKColor(0x20, 0xB0, 0x40);
        var archive = await TransferredStash.ExportOf(shown, ("image", blue), ("other", green));
        try
        {
            var imported = await TransferredStash.Import(shown, archive);

            Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence + " | " + string.Join(" | ", TrayDesktop.Get(() => shown.Notices.Shown.Select(notice => notice.Text).ToList())));
            await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
            TrayDesktop.Run(() =>
            {
                Assert.AreEqual(folder, shown.Listing.Store!.Folder.Root, "listed again from the same folder (G36)");
                Assert.AreEqual(image.Id, shown.Listing.Items.Single(item => item.Name == "image").Id, "the imported store gives the replaced item's identity to another");
            });
            await TransferredStash.EveryTileShows(shown, new() { ["image"] = (uint)blue, ["other"] = (uint)green }, "the imported stash (G6)");
        }
        finally
        {
            TransferredStash.RemoveImportsLeftovers(shown, archive);
        }
    }

    /// <summary>A shown window over a stash of three hundred flat pictures, at its first item, its notices closed.</summary>
    private static async Task<ShownWindow> Pile()
    {
        var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("first.png", 8, 6, (_, _) => First));
        shown.AddPictures(299, _ => "pile");
        await Dispatcher.UIThread.InvokeAsync(() => shown.Listing.Refresh());
        await TrayDesktop.Get(() => shown.Listing.Settle());
        TrayDesktop.Run(() =>
        {
            foreach (var notice in shown.Notices.Shown.ToList())
            {
                shown.Notices.Close(notice);
            }
        });
        return shown;
    }

    private static ScrollViewer Scroller(ShownWindow shown) => shown.Grid.FindAncestorOfType<ScrollViewer>()!;

    /// <summary>The colour a tile shows at its middle: its item's one flat colour.</summary>
    private static uint Colour(StashItem item) =>
        (uint)(item.Name == "first" ? First : AddedColour(int.Parse(item.Name["added ".Length..], System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>
    /// Renders the window again until every tile that shows at least four pixels each way inside the scroller shows its
    /// own picture in the middle of what shows, and fails, naming the first that do not, after ten seconds.
    /// </summary>
    private static async Task EveryTileInViewShowsItsPicture(ShownWindow shown, string where)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var rendered = await RenderedWindow.Of(shown);
            var (inView, wrong) = TrayDesktop.Get(() => TilesInView(shown, rendered));
            Assert.IsGreaterThan(9, inView, $"{where}: rows of tiles in view");
            if (wrong.Count == 0)
            {
                return;
            }

            Assert.IsLessThan(deadline, DateTime.UtcNow, $"{where}: {wrong.Count} of {inView} tiles in view without their picture (G6), the first {string.Join("; ", wrong.Take(4))}");
        }
    }

    private static (int InView, List<string> Wrong) TilesInView(ShownWindow shown, RenderedWindow rendered)
    {
        var root = (Control)shown.Window.Content!;
        var scroller = Scroller(shown);
        var view = new Rect(scroller.TranslatePoint(default, root)!.Value, scroller.Bounds.Size);
        var items = shown.Listing.Items.ToDictionary(item => item.Id);
        var (inView, wrong) = (0, new List<string>());
        foreach (var tile in shown.Grid.Rows.SelectMany(row => row.Tiles))
        {
            var shows = new Rect(shown.Grid.TranslatePoint(new Point(tile.X, tile.Y), root)!.Value, new Size(tile.Width, tile.Height)).Intersect(view);
            if (shows.Width < 4 || shows.Height < 4)
            {
                continue;
            }

            inView++;
            var at = shows.Center * rendered.Scaling;
            var colour = rendered.At((int)at.X, (int)at.Y);
            if (colour != Colour(items[tile.Id]))
            {
                wrong.Add($"{items[tile.Id].Name} at {at}: {colour:X8}, not {Colour(items[tile.Id]):X8}");
            }
        }

        return (inView, wrong);
    }

    /// <summary>
    /// Renders the window again until the focused tile lies whole inside the scroller, shows its picture and is ringed in
    /// the theme's focus colour just above it, and fails after ten seconds.
    /// </summary>
    private static async Task TheFocusedTileShowsInViewWithItsRing(ShownWindow shown, string when)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var rendered = await RenderedWindow.Of(shown);
            var seen = TrayDesktop.Get(() =>
            {
                var root = (Control)shown.Window.Content!;
                var scroller = Scroller(shown);
                var view = new Rect(scroller.TranslatePoint(default, root)!.Value, scroller.Bounds.Size);
                var focused = shown.Listing.Focused!.Value;
                var tile = shown.Grid.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Id == focused);
                var box = new Rect(shown.Grid.TranslatePoint(new Point(tile.X, tile.Y), root)!.Value, new Size(tile.Width, tile.Height));
                var ring = shown.Grid.ActualThemeVariant == ThemeVariant.Dark ? 0xFFFFFFFFu : 0xFF000000u;
                var middle = box.Center * rendered.Scaling;
                var item = shown.Listing.Items.Single(item => item.Id == focused);
                var picture = rendered.At((int)middle.X, (int)middle.Y) == Colour(item);
                // The focus ring fills the gap beside the tile: some pixel of the four above the tile's top is near its colour.
                var ringed = Enumerable.Range(1, 4).Any(up => Near(rendered.At((int)middle.X, (int)(box.Y * rendered.Scaling) - up), ring));
                return (whole: view.Contains(box), picture, ringed, item.Name);
            });
            if (seen is { whole: true, picture: true, ringed: true })
            {
                return;
            }

            Assert.IsLessThan(deadline, DateTime.UtcNow, $"{when}: {seen.Name} in view {seen.whole}, its picture {seen.picture}, its ring {seen.ringed} (G6, G7)");
        }
    }

    private static bool Near(uint colour, uint other) =>
        Enumerable.Range(0, 3).All(channel => Math.Abs((int)((colour >> (8 * channel)) & 0xFF) - (int)((other >> (8 * channel)) & 0xFF)) < 64);

    /// <summary>
    /// Three places on the grid's blank space, on the desktop: the gap between the first two tiles, the space right of
    /// the last row's last tile, and the space below the last row, each inside the grid and on no tile.
    /// </summary>
    private static List<(string What, PixelPoint At)> BlankPlaces(ShownWindow shown)
    {
        var grid = shown.Grid;
        var row = grid.Rows.Single();
        var (first, last) = (row.Tiles[0], row.Tiles[^1]);
        var places = new List<(string, Point)>
        {
            ("between two tiles", new Point(first.X + first.Width + JustifiedRows.Gap / 2, first.Y + first.Height / 2)),
            ("right of the last row", new Point((last.X + last.Width + grid.Bounds.Width) / 2, last.Y + last.Height / 2)),
            ("below the last row", new Point(grid.Bounds.Width / 2, (row.Y + row.Height + grid.Bounds.Height) / 2)),
        };
        foreach (var (what, at) in places)
        {
            Assert.IsTrue(new Rect(grid.Bounds.Size).Contains(at), $"{what}: inside the grid, which reaches the viewport's bottom even with one row");
            Assert.IsFalse(row.Tiles.Any(tile => new Rect(tile.X, tile.Y, tile.Width, tile.Height).Contains(at)), $"{what}: on no tile");
        }

        // The margin round the rows, inside the scroller, is the grid's blank space to a person too (G7).
        var margin = new List<(string, Point)>
        {
            ("in the margin left of the row", new Point(-grid.Margin.Left / 2, first.Y + first.Height / 2)),
            ("in the margin above the row", new Point(first.X + first.Width / 2, -grid.Margin.Top / 2)),
            ("in the margin right of the row", new Point(grid.Bounds.Width + grid.Margin.Right / 2, last.Y + last.Height / 2)),
            ("in the margin below the grid", new Point(grid.Bounds.Width / 2, grid.Bounds.Height + grid.Margin.Bottom / 2)),
        };
        var scroller = grid.FindAncestorOfType<ScrollViewer>()!;
        foreach (var (what, at) in margin)
        {
            Assert.IsFalse(new Rect(grid.Bounds.Size).Contains(at), $"{what}: outside the grid's own bounds");
            Assert.IsTrue(new Rect(scroller.Bounds.Size).Contains(grid.TranslatePoint(at, scroller)!.Value), $"{what}: inside the scroller");
        }

        return places.Concat(margin).Select(place => (place.Item1, grid.PointToScreen(place.Item2))).ToList();
    }

    /// <summary>The open menu's top-left on the desktop, in physical pixels.</summary>
    private static PixelPoint MenuPlace(ShownWindow shown) => TopLevel.GetTopLevel(shown.OpenMenu!)!.PointToScreen(default);

    [TestMethod]
    public async Task ARightClickAsksForTheItemMenuAtThePixelUnderThePointerAndTheMenuOpensThere()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("menu at one.png"), shown.Picture("menu at two.png"));
        Rect? anchor = null;
        TrayDesktop.Run(() => shown.Grid.ItemMenuRequested += asked => anchor = asked);
        await Eventually(() => TrayDesktop.Get(() => shown.Grid.Rows.Count > 0));

        TrayDesktop.Run(() => shown.RightClick(items[1].Id));
        await Eventually(() => TrayDesktop.Get(() => shown.OpenMenu is { IsOpen: true } && MenuPlace(shown) != default));

        TrayDesktop.Run(() =>
        {
            Assert.IsGreaterThan(0, anchor!.Value.Width * anchor.Value.Height, "a place Avalonia does not cut away to the grid's corner");
            var (place, pointer) = (MenuPlace(shown), TileCentre(shown, items[1].Id));
            Assert.IsLessThanOrEqualTo(4, Math.Abs(place.X - pointer.X), $"the menu at {place}, the pointer at {pointer}");
            Assert.IsLessThanOrEqualTo(4, Math.Abs(place.Y - pointer.Y), $"the menu at {place}, the pointer at {pointer}");
            shown.CloseMenu();
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ARealRightClickOpensTheMenuBesideThePointerInTheFlyoutAndThePinnedWindowAndWhollyOnScreenAtTheDisplaysEdge(bool pinned)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("real menu one.png"), shown.Picture("real menu two.png"));
        TrayDesktop.Run(() =>
        {
            if (pinned)
            {
                shown.Window.SummonPinned();
            }
            else
            {
                shown.Window.Position = DisplayPlaces.At(DisplayPlaces.Primary(), 560, 260);
            }
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Window.InputHitTest(shown.Window.PointToClient(TileCentre(shown, items[0].Id))) is ThumbnailGrid));

        foreach (var nearTheEdge in new[] { false, true })
        {
            if (nearTheEdge)
            {
                TrayDesktop.Run(() =>
                {
                    var area = shown.Window.Screens.Primary!.WorkingArea;
                    var frame = PixelSize.FromSize(shown.Window.FrameSize!.Value, shown.Window.RenderScaling);
                    shown.Window.Position = new PixelPoint(area.Right - frame.Width, area.Bottom - frame.Height);
                });
                await Task.Delay(300);
            }

            var at = TrayDesktop.Get(() => TileCentre(shown, items[0].Id));
            TestMouse.RightClick(at);
            await Eventually(() => TrayDesktop.Get(() => shown.OpenMenu is { IsOpen: true }));
            await Task.Delay(200);
            TrayDesktop.Run(() =>
            {
                var root = (WindowBase)TopLevel.GetTopLevel(shown.OpenMenu!)!;
                var menu = new PixelRect(root.PointToScreen(default), PixelSize.FromSize(root.ClientSize, root.RenderScaling));
                var area = shown.Window.Screens.Primary!.Bounds;
                Assert.IsTrue(area.Contains(menu), $"the menu at {menu} wholly on {area} (G1)");
                if (!nearTheEdge)
                {
                    Assert.IsLessThanOrEqualTo(4, Math.Abs(menu.X - at.X), $"the menu at {menu}, the pointer at {at}");
                    Assert.IsLessThanOrEqualTo(4, Math.Abs(menu.Y - at.Y), $"the menu at {menu}, the pointer at {at}");
                }

                shown.CloseMenu();
            });
        }
    }

    private static PixelPoint TileCentre(ShownWindow shown, long id)
    {
        var tile = shown.Grid.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Id == id);
        return shown.Grid.PointToScreen(new Point(tile.X + tile.Width / 2, tile.Y + tile.Height / 2));
    }
}
