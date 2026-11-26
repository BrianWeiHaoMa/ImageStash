using Avalonia;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Where the pin puts the pinned window, in physical pixels on displays of any scaling (G2, G1, E1): with no place
/// remembered, where the flyout stands, on the flyout's display, wholly inside its working area, at the pinned size and
/// never the flyout's, its top-left at the flyout's where the working area allows; once placed, there again whatever
/// flyout is pinned; a remembered place honoured, one on a gone display brought onto one; the first run, with no flyout
/// showing, centred on the primary display.
/// </summary>
[TestClass]
public sealed class PinnedPlaceTests
{
    private static readonly Display Primary = new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1, IsPrimary: true);
    private static readonly Display Right150 = new(new PixelRect(1920, 0, 2560, 1440), new PixelRect(1920, 0, 2560, 1392), 1.5, IsPrimary: false);
    private static readonly Display Left125 = new(new PixelRect(-1600, 100, 1600, 900), new PixelRect(-1600, 100, 1600, 852), 1.25, IsPrimary: false);
    private static readonly Display[] One = [Primary];
    private static readonly Display[] Three = [Primary, Right150, Left125];
    private static readonly WindowSize Pinned = WindowSize.PinnedDefault;
    private static readonly WindowSize Flyout = WindowSize.FlyoutDefault;
    private static readonly WindowSize LargeFlyout = new(1000, 760);

    /// <summary>Every flyout a summon puts on <paramref name="display"/>: in the middle, at each corner, and above a tray icon, at its default size and dragged larger than the pinned size.</summary>
    private static IEnumerable<(string Where, PixelRect Flyout)> FlyoutsOn(Display display, IReadOnlyList<Display> displays)
    {
        var area = display.WorkingArea;
        foreach (var (size, named) in new[] { (Flyout, "default"), (LargeFlyout, "dragged larger") })
        {
            yield return ($"middle, {named}", Summon.AtAnchor(new SummonAnchor(null, FrontWindow: area), displays, size));
            yield return ($"top-left, {named}", Summon.AtAnchor(new SummonAnchor(area.TopLeft), displays, size));
            yield return ($"top-right, {named}", Summon.AtAnchor(new SummonAnchor(new PixelPoint(area.Right - 1, area.Y)), displays, size));
            yield return ($"bottom-left, {named}", Summon.AtAnchor(new SummonAnchor(new PixelPoint(area.X, area.Bottom - 1)), displays, size));
            yield return ($"bottom-right, {named}", Summon.AtAnchor(new SummonAnchor(new PixelPoint(area.Right - 1, area.Bottom - 1)), displays, size));
            var icon = new PixelRect(area.Right - 120, area.Bottom + 8, 32, 32);
            yield return ($"above the tray, {named}", Summon.AboveTrayIcon(icon, displays, size));
        }
    }

    [TestMethod]
    public void TheFirstPinOverlapsTheFlyoutOnItsDisplayWhollyInsideItsWorkingAreaAtThePinnedSize()
    {
        foreach (var (displays, display) in new[] { (One, Primary), (Three, Primary), (Three, Right150), (Three, Left125) })
        {
            foreach (var (where, flyout) in FlyoutsOn(display, displays))
            {
                var place = Summon.Pinned(null, displays, Pinned, flyout);
                var because = $"{where} on the display at {display.Bounds}, {displays.Length} displays: flyout {flyout}, pinned {place}";
                Assert.IsTrue(display.WorkingArea.Contains(flyout), $"the summon put the flyout on that display: {because}");
                Assert.IsTrue(display.WorkingArea.Contains(place), $"wholly inside the flyout's display's working area: {because}");
                Assert.IsTrue(place.Intersects(flyout), $"over where the flyout stood: {because}");
                Assert.AreEqual(new PixelSize((int)Math.Round(Pinned.Width * display.Scaling), (int)Math.Round(Pinned.Height * display.Scaling)), place.Size,
                    $"the pinned size by that display's scaling, never the flyout's (G2: remembered separately): {because}");
                // Its top-left at the flyout's, moved only as far as the working area needs.
                Assert.AreEqual(Math.Min(flyout.X, display.WorkingArea.Right - place.Width), place.X, because);
                Assert.AreEqual(Math.Min(flyout.Y, display.WorkingArea.Bottom - place.Height), place.Y, because);
            }
        }
    }

    [TestMethod]
    public void AFlyoutInTheMiddleIsPinnedWithItsTopLeftUnmoved()
    {
        var flyout = new PixelRect(700, 200, 460, 640);

        Assert.AreEqual(new PixelRect(700, 200, 880, 620), Summon.Pinned(null, One, Pinned, flyout));
        Assert.AreEqual(new PixelRect(2200, 100, 1320, 930), Summon.Pinned(null, Three, Pinned, new PixelRect(2200, 100, 690, 960)), "at 150 %");
    }

    [TestMethod]
    public void ThePlaceTheFirstPinGaveIsWhereEveryLaterPinGoesWhereverTheFlyoutStands()
    {
        var first = Summon.Pinned(null, Three, Pinned, Summon.AboveTrayIcon(new PixelRect(4300, 1400, 32, 32), Three, Flyout));
        var remembered = new WindowPosition(first.X, first.Y);

        foreach (var (where, flyout) in FlyoutsOn(Primary, Three).Concat(FlyoutsOn(Left125, Three)))
        {
            Assert.AreEqual(first, Summon.Pinned(remembered, Three, Pinned, flyout), where);
        }

        Assert.AreEqual(first, Summon.Pinned(remembered, Three, Pinned), "and with no flyout showing, as after a restart's first run");
    }

    [TestMethod]
    public void AFirstPinAtTheLeftEdgeOfADisplayOfAnotherScalingComesBackThereAtTheSameSize()
    {
        // Beside a caret near the 150 % display's left edge, the flyout is moved onto its first column, where the 100 % display ends.
        var flyout = Summon.AtAnchor(new SummonAnchor(null, Caret: new PixelRect(Right150.WorkingArea.X + 10, 1200, 2, 30)), Three, Flyout);
        var first = Summon.Pinned(null, Three, Pinned, flyout);
        var again = Summon.Pinned(new WindowPosition(first.X, first.Y), Three, Pinned, flyout);

        Assert.AreEqual(Right150.WorkingArea.X, flyout.X, "the flyout at the display's left edge");
        Assert.AreEqual(new PixelSize(1320, 930), first.Size, "the first pin sized by that display's scaling");
        Assert.AreEqual(first, again, "pin again: the pinned size and position are back (Done when 5)");
    }

    [TestMethod]
    public void ARememberedPlaceIsHonouredAndOneOnAGoneDisplayIsBroughtOntoOne()
    {
        var flyout = new PixelRect(2200, 100, 690, 960);

        Assert.AreEqual(new PixelRect(100, 100, 880, 620), Summon.Pinned(new WindowPosition(100, 100), Three, Pinned, flyout), "where the user left it, not where the flyout stands");
        var gone = Summon.Pinned(new WindowPosition(-1500, 200), One, Pinned, new PixelRect(700, 200, 460, 640));
        Assert.IsTrue(Primary.WorkingArea.Contains(gone), $"the display it was left on is gone: {gone}");
    }

    [TestMethod]
    public void TheFirstRunWithNoFlyoutShowingIsCentredOnThePrimaryDisplay()
    {
        Assert.AreEqual(new PixelRect(520, 206, 880, 620), Summon.Pinned(null, Three, Pinned));
    }
}
