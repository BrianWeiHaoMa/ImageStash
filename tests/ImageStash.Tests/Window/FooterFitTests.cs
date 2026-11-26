using Avalonia;
using Avalonia.Controls;
using ImageStash.Tests.Support;
using ImageStash.Window;
using Widths = ImageStash.Window.FooterFit.Widths;

namespace ImageStash.Tests.Window;

/// <summary>
/// What gives way in the footer's row, and in what order, measured with stand-ins of fixed widths, since no text is
/// measured here (G8, G35, G33): the paused line, then Ctrl+C's hint, then the busy words down to their opening, then the
/// count, then Enter's hint, and the busy state's opening last; nothing over another piece or outside the row, and the
/// busy state kept in place as the hints come and go. The real words' fit in pixels is the desktop suite's.
/// </summary>
[TestClass]
public sealed class FooterFitTests
{
    // Stand-ins about as wide as the real pieces at the window's minimum: "Paste 3 items into Slack" with its cap, Ctrl+C's
    // hint, the paused line, "40 items · 3 selected", and the bar with "Adding 12 of 40 to your stash…".
    private static readonly Widths Whole = new(Lead: 190, Copy: 95, Paused: 150, Count: 115, Busy: 221);

    [TestMethod]
    public void EachPieceGivesWayInItsTurnAsTheRowNarrows()
    {
        Assert.AreEqual(Whole, FooterFit.Fit(860, Whole), "everything whole where it fits");
        Assert.AreEqual(Whole with { Paused = 0 }, FooterFit.Fit(800, Whole), "the paused line first, whole");
        Assert.AreEqual(Whole with { Paused = 0, Copy = 0 }, FooterFit.Fit(600, Whole), "then Ctrl+C's hint, whole");
        Assert.AreEqual(Whole with { Paused = 0, Copy = 0, Busy = 167 }, FooterFit.Fit(500, Whole), "then the busy words, down to their opening, the count whole");
        Assert.AreEqual(Whole with { Paused = 0, Copy = 0, Count = 0 }, FooterFit.Fit(434, Whole), "then the count: at the minimum, Enter's hint and the busy state whole");
        Assert.AreEqual(new Widths(144, 0, 0, 0, FooterFit.BusyOpening), FooterFit.Fit(320, Whole), "then Enter's hint is cut, beside the busy state's opening");
        Assert.AreEqual(new Widths(0, 0, 0, 0, 100), FooterFit.Fit(100, Whole), "the busy state's opening last");
    }

    [TestMethod]
    public void WithNothingRunningTheCountGivesWayBeforeEntersHintIsCut()
    {
        var idle = Whole with { Busy = 0 };

        Assert.AreEqual(idle with { Paused = 0, Copy = 0 }, FooterFit.Fit(330, idle));
        Assert.AreEqual(idle with { Paused = 0, Copy = 0, Count = 0 }, FooterFit.Fit(300, idle));
        Assert.AreEqual(new Widths(150, 0, 0, 0, 0), FooterFit.Fit(150, idle));
    }

    [TestMethod]
    public void ACopysConfirmationIsCutShortBeforeTheCountWhichStaysWhole()
    {
        var confirming = Whole with { Copy = 0, Busy = 0, Lead = 400 };

        Assert.AreEqual(confirming with { Lead = 300 - FooterFit.Gap - 115, Paused = 0 }, FooterFit.Fit(300, confirming, countGivesWay: false));
        Assert.AreEqual(confirming with { Paused = 0, Count = 0 }, FooterFit.Fit(500, confirming), "where Enter's hint leads, the count gives way first");
    }

    [TestMethod]
    public void AShortBusyStateKeepsItsWholeWordsAsItsOpening()
    {
        var shortBusy = Whole with { Busy = 120 };

        Assert.AreEqual(new Widths(64, 0, 0, 0, 120), FooterFit.Fit(200, shortBusy), "never stretched past its words");
    }

    [TestMethod]
    public void NothingIsDrawnOverAnotherPieceOrOutsideTheRowAndTheBusyStateKeepsItsPlaceAsTheHintsComeAndGo() => ControlThread.Run(() =>
    {
        foreach (var room in new[] { 860d, 800, 600, 500, 434, 320, 100 })
        {
            var (row, pieces) = Row();
            Lay(row, room);
            var shown = pieces.Where(piece => ((Control)piece.Parent!).IsVisible).Select(Place).ToList();
            double[] given = [row.Given.Lead, row.Given.Copy, row.Given.Paused, row.Given.Count, row.Given.Busy];
            CollectionAssert.AreEqual(given.Select(width => width > 0).ToList(), pieces.Select(piece => ((Control)piece.Parent!).IsVisible).ToList(), $"a piece given no room hidden, the rest shown, at {room}");
            Assert.IsTrue(shown.All(place => place.X >= 0 && place.Right <= room + 0.01), $"inside the row at {room}");
            for (var i = 0; i < shown.Count; i++)
            {
                for (var j = i + 1; j < shown.Count; j++)
                {
                    Assert.IsFalse(shown[i].Intersects(shown[j]), $"{shown[i]} and {shown[j]} apart at {room}");
                }
            }
        }

        foreach (var room in new[] { 860d, 434, 320 })
        {
            var (row, pieces) = Row();
            var busy = pieces[4];
            Lay(row, room);
            var withHints = Place(busy);
            pieces[0].IsVisible = false;
            pieces[1].IsVisible = false;
            Lay(row, room);

            if (room > 320)
            {
                Assert.AreEqual(withHints, Place(busy), $"the busy state where it was once the selection has gone, at {room}");
            }

            Assert.AreEqual(room, withHints.Right, $"against the row's right edge with the hints, at {room}");
            Assert.AreEqual(room, Place(busy).Right, $"and without them, at {room}");
        }
    });

    private static (FooterFit Row, StandIn[] Pieces) Row()
    {
        StandIn[] pieces = [new(Whole.Lead), new(Whole.Copy), new(Whole.Paused), new(Whole.Count), new(Whole.Busy)];
        return (new FooterFit(pieces[0], pieces[1], pieces[2], pieces[3], pieces[4]), pieces);
    }

    /// <summary>Where the row laid <paramref name="piece"/>: its slot, which clips it to what it was given.</summary>
    private static Rect Place(Control piece) => ((Control)piece.Parent!).Bounds;

    private static void Lay(FooterFit row, double room)
    {
        var size = new Size(room, 36);
        row.Measure(size);
        row.Arrange(new Rect(size));
    }

    /// <summary>A piece as wide as its words would be whole, taking less when given less, as a trimmed text does.</summary>
    private sealed class StandIn(double whole) : Control
    {
        protected override Size MeasureOverride(Size availableSize) => new(Math.Min(whole, availableSize.Width), 16);
    }
}
