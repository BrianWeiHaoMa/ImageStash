using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The room the grid leaves below its last row while the window's notices show over the body's bottom (G16), through the
/// window's own wiring (<see cref="WindowStatus"/>) on a laid-out grid: scrolled to the end the last row stands above the
/// notices and a tile the keys reach is revealed clear of them (G7); the room follows the cards as they stand, a problem
/// notice staying past its time and a room kept under the pointer included, and goes with the last of them; it changes
/// the extent alone, so nothing the person looks at moves (G4); none under the no-match state; a row stays in view above
/// notices however tall; and under the preview or the editor the notices leave the grid's extent alone.
/// </summary>
/// <remarks>
/// No Avalonia platform runs here, so a card measures to its borders and padding alone, its words and buttons drawing
/// nothing; the room is read against those heights, laid out as the window lays them out.
/// </remarks>
[TestClass]
public sealed class GridNoticeRoomTests
{
    private const double Width = 240;
    private const double Body = 300;

    [TestMethod]
    public async Task ANoticeGivesTheGridRoomBelowItsLastRowAndItsLeavingTakesItBack()
    {
        using var listed = await Listed(16);
        ControlThread.Run(() =>
        {
            var shown = Shown.Over(listed);
            var rows = shown.Grid.Rows.Select(row => row.Tiles.Select(tile => tile.Bounds).ToList()).ToList();
            Assert.AreEqual(shown.LastRowBottom, shown.Extent(), "no notice, no room");

            listed.Notices.Raise("Copied “picture 3”.");
            shown.Show();
            var cards = shown.CardsHeight;
            Assert.IsGreaterThan(0, cards);
            Assert.IsGreaterThanOrEqualTo(shown.LastRowBottom + cards + WindowLayout.NoticeMargin().Bottom, shown.Extent(),
                "scrolled to the end, the last row's bottom stands above the notices and their margin");
            CollectionAssert.AreEqual(rows.SelectMany(row => row).ToList(), shown.Grid.Rows.SelectMany(row => row.Tiles.Select(tile => tile.Bounds)).ToList(),
                "the rows are laid out as they were (G6)");

            listed.Stash.Clock.Now += Notices.LastingAtMost;
            listed.Notices.Sweep();
            shown.Show();
            Assert.IsEmpty(listed.Notices.Shown);
            Assert.AreEqual(shown.LastRowBottom, shown.Extent(), "the last notice gone, the extent is as it was");
        });
    }

    [TestMethod]
    public async Task NoRoomIsAddedUnderTheNoMatchState()
    {
        using var listed = await Listed(4);
        await listed.Listing.SetQuery("nothing matches this");
        ControlThread.Run(() =>
        {
            var shown = Shown.Over(listed);
            listed.Notices.Raise(new Failure("Rename", "picture 1", "the name is taken"));
            shown.Show();

            Assert.IsEmpty(listed.Listing.Items);
            Assert.AreEqual(0, shown.Extent(), "an empty grid does not scroll under the notices");
        });
    }

    [TestMethod]
    public async Task ANoticeArrivingOrLeavingAsksTheScrollerToMoveNothing()
    {
        using var listed = await Listed(16);
        ControlThread.Run(() =>
        {
            var shown = Shown.Over(listed);
            listed.Listing.Focus(listed.Listing.Items[5].Id);
            // The focus's own reveal runs first; what follows is the notices' alone.
            Dispatcher.UIThread.RunJobs();
            Rect? requested = null;
            shown.Grid.AddHandler(Control.RequestBringIntoViewEvent, (_, args) => requested = args.TargetRect);

            var notice = listed.Notices.Raise("Copied “picture 5”.");
            shown.Show();
            shown.Extent();
            Dispatcher.UIThread.RunJobs();
            listed.Notices.Close(notice);
            shown.Show();
            shown.Extent();
            Dispatcher.UIThread.RunJobs();

            Assert.IsNull(requested, "the room changes the extent alone, never what is in view");
        });
    }

    [TestMethod]
    public async Task AProblemNoticeKeepsTheRoomPastItsTimeAndItsCloseGivesItBack()
    {
        using var listed = await Listed(16);
        ControlThread.Run(() =>
        {
            var shown = Shown.Over(listed);
            var problem = listed.Notices.Raise(new Failure("Move to the Recycle Bin", "picture 2", "another program has it open"));
            shown.Show();
            var room = shown.Extent() - shown.LastRowBottom;

            listed.Stash.Clock.Now += Notices.LastingAtMost * 3;
            listed.Notices.Sweep();
            shown.Show();
            Assert.Contains(problem, listed.Notices.Shown);
            Assert.AreEqual(room, shown.Extent() - shown.LastRowBottom, "staying past its time, it keeps its room");

            listed.Notices.Close(problem);
            shown.Show();
            Assert.AreEqual(shown.LastRowBottom, shown.Extent(), "its × gives the room back");
        });
    }

    [TestMethod]
    public async Task ThePointerOnTheNoticesKeepsTheClosedLowerOnesRoomUntilItLeaves()
    {
        using var listed = await Listed(16);
        ControlThread.Run(() =>
        {
            var shown = Shown.Over(listed);
            var upper = listed.Notices.Raise("Deleted “picture 1”.", "Undo", () => Task.FromResult(Outcome.Done));
            var lower = listed.Notices.Raise("Copied “picture 4”.");
            shown.Show();
            var both = shown.Extent();
            var upperRoom = shown.Status.NoticeCards.RoomOf(upper)!.DesiredSize.Height;

            shown.Pointer(InputElement.PointerEnteredEvent);
            listed.Notices.Close(lower);
            shown.Show();
            Assert.AreEqual(both, shown.Extent(), "the closed card keeps its room under the pointer, and so does the grid");

            shown.Pointer(InputElement.PointerExitedEvent);
            shown.Show();
            Assert.AreEqual(shown.LastRowBottom + upperRoom + WindowLayout.NoticeMargin().Bottom, shown.Extent(),
                "the pointer gone, the room follows what is left");
        });
    }

    [TestMethod]
    public async Task DownOntoTheLastRowRevealsTheTileClearOfAStayingNotice()
    {
        using var listed = await Listed(16);
        ControlThread.Run(() =>
        {
            var shown = Shown.Over(listed);
            listed.Notices.Raise(new Failure("Add to the stash", "dog.png", "it is unreadable"));
            listed.Stash.Clock.Now += Notices.LastingAtMost * 2;
            listed.Notices.Sweep();
            shown.Show();
            var room = shown.Extent() - shown.LastRowBottom;
            Assert.IsGreaterThanOrEqualTo(shown.CardsHeight + WindowLayout.NoticeMargin().Bottom, room, "past its time, the notice keeps its room");
            shown.ArrangeGrid();
            var rows = shown.Grid.Rows;
            listed.Listing.Focus(rows[^2].Tiles[0].Id);
            Rect? requested = null;
            shown.Grid.AddHandler(Control.RequestBringIntoViewEvent, (_, args) => requested = args.TargetRect);

            listed.Listing.MoveFocus(Direction.Down, rows, extendSelection: false);
            shown.Grid.BringFocusedIntoView();

            var tile = rows[^1].Tiles.Single(tile => tile.Id == listed.Listing.Focused);
            var withRing = tile.Bounds.Inflate(JustifiedRows.Gap);
            Assert.AreEqual(withRing.WithHeight(withRing.Height + room), requested, "the tile is asked into view with the room below it");
        });
    }

    [TestMethod]
    public async Task DownOnTheLastRowRevealsTheTileItStaysOnClearOfAStayingNotice()
    {
        using var listed = await Listed(16);
        ControlThread.Run(() =>
        {
            var shown = Shown.Over(listed);
            listed.Notices.Raise(new Failure("Add to the stash", "dog.png", "it is unreadable"));
            shown.Show();
            var room = shown.Extent() - shown.LastRowBottom;
            Assert.IsGreaterThan(0, room);
            shown.ArrangeGrid();
            var rows = shown.Grid.Rows;
            var tile = rows[^1].Tiles[0];
            // Reached by a click, which reveals it against the viewport alone.
            listed.Listing.Focus(tile.Id);
            Dispatcher.UIThread.RunJobs();
            Rect? requested = null;
            shown.Grid.AddHandler(Control.RequestBringIntoViewEvent, (_, args) => requested = args.TargetRect);

            listed.Listing.MoveFocus(Direction.Down, rows, extendSelection: false);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(tile.Id, listed.Listing.Focused, "there is no row below");
            var withRing = tile.Bounds.Inflate(JustifiedRows.Gap);
            Assert.AreEqual(withRing.WithHeight(withRing.Height + room), requested, "the tile the keys are on is asked into view clear of the notices (G7)");
        });
    }

    [TestMethod]
    public async Task NoticesTallerThanTheBodyLeaveARowInViewAndScrollWithinTheirArea()
    {
        using var listed = await Listed(16);
        ControlThread.Run(() =>
        {
            var shown = Shown.Over(listed);
            for (var index = 0; index < 30; index++)
            {
                listed.Notices.Raise($"Copied “picture {index}”.");
            }

            shown.Show();
            Assert.IsGreaterThan(Body, shown.CardsHeight, "the cards together are taller than the body");
            var rowInView = shown.Grid.RowHeight + 2 * JustifiedRows.Gap;
            var area = shown.Status.NoticeArea;
            Assert.AreEqual(Body - WindowLayout.NoticeMargin().Bottom - rowInView, area.MaxHeight, 0.001,
                "the notices' area leaves a row of the grid in view above it, and takes the rest");
            Assert.AreEqual(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, area.VerticalScrollBarVisibility, "the notices scroll within it");
            Assert.AreEqual(area.MaxHeight + WindowLayout.NoticeMargin().Bottom, shown.Extent() - shown.LastRowBottom, 0.001,
                "the room is what the notices' area shows, leaving a row in view too");
        });
    }

    [TestMethod]
    public async Task UnderThePreviewOrTheEditorANoticeLeavesTheExtentAndBackOnTheGridTheRoomIsThere()
    {
        using var listed = await Listed(16);
        ControlThread.Run(() =>
        {
            var covered = true;
            var shown = Shown.Over(listed, () => covered);
            listed.Notices.Raise(new Failure("Save", "picture 1", "the disk is full"));
            shown.Show();
            Assert.AreEqual(shown.LastRowBottom, shown.Extent(), "covered, the grid's extent is no concern of the notices");

            covered = false;
            shown.Status.ShowListing();
            Assert.AreEqual(shown.LastRowBottom + shown.CardsHeight + WindowLayout.NoticeMargin().Bottom, shown.Extent(),
                "back on the grid with the notice still showing, the room is there");
        });
    }

    [TestMethod]
    public async Task InABodyTooShortToLeaveARowTheNewestNoticeStillShowsWithItsClose()
    {
        using var listed = await Listed(16);
        ControlThread.Run(() =>
        {
            var shown = Shown.Over(listed, body: 60);
            listed.Notices.Raise("Copied “picture 1”.");
            var newest = listed.Notices.Raise(new Failure("Paste", "the clipboard", "another program has it open"));
            shown.Show();

            var newestRoom = shown.Status.NoticeCards.RoomOf(newest)!.Bounds.Height;
            Assert.IsGreaterThan(0, newestRoom);
            Assert.IsGreaterThanOrEqualTo(Math.Min(newestRoom, GridNoticeRoom.LeastShown), shown.Status.NoticeArea.MaxHeight,
                "the notices' area is never held so low that the newest card and its × leave it (G16)");
        });
    }

    [TestMethod]
    public async Task DelNearTheViewportsBottomRevealsTheItemInItsPlaceAgainstTheViewportAlone()
    {
        using var listed = await Listed(16);
        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var shown = Shown.Over(listed);
            // A problem notice already stands, so the room is there whenever the Delete's reveal runs.
            listed.Notices.Raise(new Failure("Rename", "picture 1", "the name is taken"));
            shown.Show();
            Assert.IsGreaterThan(shown.LastRowBottom, shown.Extent());
            shown.ArrangeGrid();
            var rows = shown.Grid.Rows;
            var deleted = rows[^1].Tiles[0].Id;
            listed.Listing.Select([deleted], deleted);
            Dispatcher.UIThread.RunJobs();
            Rect? requested = null;
            shown.Grid.AddHandler(Control.RequestBringIntoViewEvent, (_, args) => requested = args.TargetRect);

            listed.Keys.Released(Key.Delete);
            await listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);
            shown.Show();
            Assert.IsTrue(listed.Notices.Shown.Any(notice => notice.Text.StartsWith("Deleted", StringComparison.Ordinal)), "the Delete's notice shows");
            Assert.IsGreaterThan(shown.LastRowBottom, shown.Extent(), "and the grid has room for the notices");
            shown.ArrangeGrid();
            Dispatcher.UIThread.RunJobs();

            var neighbour = listed.Listing.Focused!.Value;
            Assert.AreNotEqual(deleted, neighbour);
            var tile = shown.Grid.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Id == neighbour);
            Assert.AreEqual(tile.Bounds.Inflate(JustifiedRows.Gap), requested,
                "the place kept after a Delete is revealed as before, so the grid stays where it was (G4)");
        }));
    }

    private static async Task<ListedStash> Listed(int items)
    {
        var listed = await ListedStash.Open();
        for (var index = 0; index < items; index++)
        {
            await listed.Stash.Import($"picture {index}", MediaFixtures.DistinctPng(index));
        }

        await listed.Listing.ShowScope(Scope.All);
        return listed;
    }

    /// <summary>The window's grid in its scroller with the window's notices, laid out as the window lays them out, the body <see cref="Body"/> tall unless given.</summary>
    private sealed class Shown
    {
        private readonly ScrollViewer scroller;

        private readonly double body;

        private Shown(ListedStash listed, Func<bool> covered, double body)
        {
            this.body = body;
            // Layout and the reveal run without bitmap decoding on the portable control thread.
            var thumbnails = new Thumbnails<Bitmap>(1, 0, TimeSpan.Zero, FrameBitmap.Of,
                action => Dispatcher.UIThread.Post(action), image => image.Dispose(), failure => Assert.Fail(failure.Sentence));
            Grid = new ThumbnailGrid(listed.Listing, thumbnails, () => ThumbnailSize.Medium);
            scroller = WindowLayout.GridScroller(Grid);
            Status = new WindowStatus(listed.Listing, listed.Notices, listed.Settings, listed.FooterHints, scroller, covered, () => { }, listed.WaysIn, () => false);
            Status.NoticesShowing(true);
            scroller.Measure(new Size(Width, body));
            scroller.Arrange(new Rect(0, 0, Width, body));
            Extent();
        }

        public ThumbnailGrid Grid { get; }

        public WindowStatus Status { get; }

        public double LastRowBottom => Grid.Rows.Count == 0 ? 0 : Grid.Rows[^1].Y + Grid.Rows[^1].Height;

        public double CardsHeight => Status.NoticeCards.View.Bounds.Height;

        public static Shown Over(ListedStash listed, Func<bool>? covered = null, double body = Body) => new(listed, covered ?? (() => false), body);

        /// <summary>The notices changed, as the window hears it, and their cards are laid out again.</summary>
        public void Show()
        {
            Status.ShowNotices();
            var cards = Status.NoticeCards.View;
            cards.Measure(new Size(Width, double.PositiveInfinity));
            cards.Arrange(new Rect(0, 0, Width, cards.DesiredSize.Height));
        }

        /// <summary>The grid's extent as its scroller measures it.</summary>
        public double Extent()
        {
            Grid.Measure(new Size(Width, double.PositiveInfinity));
            return Grid.DesiredSize.Height;
        }

        /// <summary>The grid arranged in the body's viewport, so a reveal reads the body's height.</summary>
        public void ArrangeGrid() => Grid.Arrange(new Rect(0, 0, Width, body));

        /// <summary>The pointer comes onto the notices' area, or leaves it.</summary>
        public void Pointer(RoutedEvent<PointerEventArgs> routedEvent)
        {
            var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
            var area = Status.NoticeArea;
            area.RaiseEvent(new PointerEventArgs(routedEvent, area, pointer, null, default, 0, PointerPointProperties.None, KeyModifiers.None));
        }
    }
}
