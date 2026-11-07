using Avalonia;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G1's placement from the tray and from the shortcut, in physical pixels on displays of any scaling (E1): above the
/// icon, or beside the caret, else at the pointer, wholly on its display, the remembered size kept; the pinned window where it was left; G2's
/// hide rules for focus loss, the paste and Esc; G16's notification above the tray on the primary display; the window
/// never placed below its minimum size, which yields only to a display too small for it (G20, G21).
/// </summary>
[TestClass]
public sealed class SummonTests
{
    private static readonly Display Primary = new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1, IsPrimary: true);
    private static readonly Display Right150 = new(new PixelRect(1920, 0, 2560, 1440), new PixelRect(1920, 0, 2560, 1392), 1.5, IsPrimary: false);
    private static readonly Display[] Two = [Primary, Right150];
    private static readonly Display Small = new(new PixelRect(0, 0, 1280, 800), new PixelRect(0, 0, 1280, 752), 1, IsPrimary: true);
    private static readonly WindowSize Flyout = new(460, 640);

    [TestMethod]
    public void TheFlyoutOpensAboveTheTrayIconWhereverTheIconIsAtThatSummon()
    {
        var first = Summon.AboveTrayIcon(new PixelRect(100, 1032, 32, 48), Two, Flyout);
        var second = Summon.AboveTrayIcon(new PixelRect(2020, 1392, 32, 48), Two, Flyout);

        Assert.IsTrue(Primary.WorkingArea.Contains(first), first.ToString());
        Assert.IsLessThanOrEqualTo(1032, first.Bottom);
        Assert.AreEqual(new PixelSize(460, 640), first.Size);
        Assert.IsTrue(Right150.WorkingArea.Contains(second), second.ToString());
        Assert.IsLessThanOrEqualTo(1392, second.Bottom);
        Assert.AreEqual(new PixelSize(690, 960), second.Size, "sized by the scaling of the display it opens on");
    }

    [TestMethod]
    public void NearTheRightEdgeTheFlyoutStaysOnTheDisplayAndAboveTheIcon()
    {
        var icon = new PixelRect(4480 - 40 - 32, 1392, 32, 48);

        var place = Summon.AboveTrayIcon(icon, Two, Flyout);

        Assert.IsLessThanOrEqualTo(4480, place.Right);
        Assert.IsLessThan(icon.Y, place.Bottom);
        Assert.IsTrue(Right150.WorkingArea.Contains(place));
    }

    [TestMethod]
    public void WithTheTaskbarAtTheTopTheFlyoutOpensBelowTheIcon()
    {
        var top = new Display(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 48, 1920, 1032), 1, true);
        var icon = new PixelRect(1700, 0, 32, 48);

        var place = Summon.AboveTrayIcon(icon, [top], Flyout);

        Assert.IsGreaterThan(icon.Bottom, place.Y);
        Assert.IsTrue(top.WorkingArea.Contains(place));
    }

    [TestMethod]
    public void WithNoIconRectangleTheFlyoutIsCentredOnThePrimaryDisplay()
    {
        var place = Summon.AboveTrayIcon(null, [Right150, Primary], Flyout);

        Assert.AreEqual(new PixelRect(730, 196, 460, 640), place);
    }

    [TestMethod]
    public void ARememberedSizeTooTallForTheDisplayIsReducedForThisShowingOnly()
    {
        var short150 = new Display(new PixelRect(0, 0, 1600, 760), new PixelRect(0, 0, 1600, 700), 1.5, true);
        var remembered = new WindowSize(460, 900);

        var place = Summon.AboveTrayIcon(new PixelRect(1500, 710, 32, 48), [short150], remembered);

        Assert.AreEqual(690, place.Width);
        Assert.AreEqual(700, place.Height);
        Assert.IsTrue(short150.WorkingArea.Contains(place));
        Assert.AreEqual(new WindowSize(460, 900), remembered);
    }

    [TestMethod]
    public void AStoredSizeThatCannotSizeAWindowReadsAsTheDefault()
    {
        Assert.AreEqual(WindowSize.PinnedDefault, new WindowSize(0, 0).UsableOr(WindowSize.PinnedDefault));
        Assert.AreEqual(WindowSize.PinnedDefault, new WindowSize(-5, 400).UsableOr(WindowSize.PinnedDefault));
        Assert.AreEqual(WindowSize.PinnedDefault, new WindowSize(double.NaN, 400).UsableOr(WindowSize.PinnedDefault));
        Assert.AreEqual(WindowSize.PinnedDefault, new WindowSize(500, double.PositiveInfinity).UsableOr(WindowSize.PinnedDefault));
        Assert.AreEqual(new WindowSize(500, 400), new WindowSize(500, 400).UsableOr(WindowSize.PinnedDefault));
    }

    [TestMethod]
    public void AStoredSizeBelowTheMinimumIsRaisedToItForThisShowingOnlyWhereverTheWindowIsPlaced()
    {
        var icon = new PixelRect(1200, 752, 32, 48);
        var tiny = new WindowSize(200, 200);

        var aboveTheIcon = Summon.AboveTrayIcon(icon, [Small], tiny);
        var onlyShort = Summon.AboveTrayIcon(icon, [Small], new WindowSize(500, 300));
        var above = Summon.AboveTrayIcon(icon, [Small], new WindowSize(600, 560));
        var atThePointer = Summon.AtAnchor(new SummonAnchor(new PixelPoint(1200, 700)), [Small], tiny);
        var besideTheCaret = Summon.AtAnchor(new SummonAnchor(null, new PixelRect(300, 700, 1, 16)), [Small], tiny);
        var centred = Summon.AtAnchor(new SummonAnchor(null), [Small], tiny);
        var unpinned = Summon.Unpinned(new PixelPoint(2000, 100), Two, tiny);
        var pinned = Summon.Pinned(new WindowPosition(1240, 712), [Small], new WindowSize(300, 300));

        Assert.AreEqual(new PixelSize(458, 508), aboveTheIcon.Size, "raised to the minimum on both sides");
        Assert.AreEqual(new PixelSize(500, 508), onlyShort.Size, "only the side below it raised");
        Assert.AreEqual(new PixelSize(600, 560), above.Size, "a size above the minimum as it was");
        Assert.IsTrue(new[] { aboveTheIcon, onlyShort, above }.All(place => Small.WorkingArea.Contains(place) && place.Bottom <= icon.Y), "above the icon and wholly on screen at the raised size (G1)");
        Assert.AreEqual(new PixelRect(822, 244, 458, 508), atThePointer, "at the pointer, moved the least onto the working area at the raised size");
        Assert.AreEqual(new PixelRect(272, 184, 458, 508), besideTheCaret, "its bottom 8 above the caret at the raised size, not over it");
        Assert.AreEqual(new PixelRect(411, 122, 458, 508), centred);
        Assert.AreEqual(new PixelRect(2000, 100, 687, 762), unpinned, "raised in device-independent units, sized by the display (E1)");
        Assert.AreEqual(new PixelRect(1240, 712, 458, 508), pinned, "the pinned window at its corner where the user left it, at the minimum");
        Assert.AreEqual(new WindowSize(200, 200), tiny, "the stored size stays as it was (G2, G31)");
    }

    [TestMethod]
    public void OnEveryDisplayThatHoldsItTheMinimumIsTheWindowsOwn()
    {
        var frame = new Size(16, 39);

        Assert.AreEqual(WindowSize.Minimum, Summon.MinimumOn(Primary, frame));
        Assert.AreEqual(WindowSize.Minimum, Summon.MinimumOn(Small, frame));
        Assert.AreEqual(WindowSize.Minimum, Summon.MinimumOn(Right150, frame), "in device-independent units: 1706 × 928 at 150 % holds it (E1)");
    }

    [TestMethod]
    public void OnADisplaySmallerThanTheMinimumTheMinimumYieldsToItsWorkingAreaSoTheFlyoutIsWhollyOnIt()
    {
        // 640 × 360 at 200 %: a working area of 320 × 156 device-independent pixels.
        var tiny = new Display(new PixelRect(0, 0, 640, 360), new PixelRect(0, 0, 640, 312), 2, IsPrimary: true);
        var frame = new Size(2, 2);

        var least = Summon.MinimumOn(tiny, frame);
        var place = Summon.AboveTrayIcon(new PixelRect(600, 312, 32, 48), [tiny], new WindowSize(200, 200));

        Assert.AreEqual(new WindowSize(318, 154), least, "the working area less the frame");
        Assert.AreEqual(tiny.WorkingArea, place, "the flyout as large as the working area, wholly on it (G1)");
        Assert.AreEqual(place, Summon.Grown(place, PixelSize.FromSize(new Size(least.Width + frame.Width, least.Height + frame.Height), tiny.Scaling), [tiny]),
            "the window at that minimum, frame included, fills the place and no more");
    }

    [TestMethod]
    public void AWindowGrownPastItsPlaceByItsFrameIsMovedBackOnScreenAndAPlaceAcrossAnEdgeKeepsItsCorner()
    {
        // A flyout placed at the minimum, its border of 2 added; a pinned window, its title bar and border of 16 × 39.
        var atTheCorner = Summon.Grown(new PixelRect(1462, 592, 458, 440), new PixelSize(460, 442), Two);
        var withRoom = Summon.Grown(new PixelRect(100, 100, 458, 440), new PixelSize(474, 479), Two);
        var acrossAnEdge = Summon.Grown(new PixelRect(1700, 900, 458, 440), new PixelSize(474, 479), Two);
        var onTheScaledDisplay = Summon.Grown(new PixelRect(3793, 732, 687, 660), new PixelSize(690, 663), Two);

        Assert.AreEqual(new PixelRect(1460, 590, 460, 442), atTheCorner, "moved the least: the whole frame inside the working area (G1)");
        Assert.AreEqual(new PixelRect(100, 100, 474, 479), withRoom, "grown from its corner where that keeps it inside");
        Assert.AreEqual(new PixelRect(1700, 900, 474, 479), acrossAnEdge, "a place the user left across an edge kept where it is (G2)");
        Assert.AreEqual(new PixelRect(3790, 729, 690, 663), onTheScaledDisplay, "inside the working area of the display holding the place");
    }

    [TestMethod]
    public void ThePinnedWindowOpensWhereItWasLeftOrCentredWhenItNeverWas()
    {
        var left = Summon.Pinned(new WindowPosition(100, 100), Two, new WindowSize(900, 700));
        var never = Summon.Pinned(null, Two, new WindowSize(900, 700));
        var onTheRight = Summon.Pinned(new WindowPosition(2000, 50), Two, new WindowSize(900, 700));

        Assert.AreEqual(new PixelRect(100, 100, 900, 700), left);
        Assert.AreEqual(new PixelRect(510, 166, 900, 700), never);
        Assert.AreEqual(new PixelRect(2000, 50, 1350, 1050), onTheRight);
    }

    [TestMethod]
    public void APinnedPlaceNoLongerOnAnyDisplayIsBroughtWhollyOntoTheNearest()
    {
        var place = Summon.Pinned(new WindowPosition(9000, 5000), Two, new WindowSize(900, 700));

        Assert.IsTrue(Right150.WorkingArea.Contains(place), place.ToString());
    }

    [TestMethod]
    public void APinnedPlaceWhoseTitleBarIsOnNoDisplayOnceTheDisplayAboveIsGoneIsBroughtWhollyOntoTheOneLeft()
    {
        // Left on a display above the primary, its bottom running onto the primary; that display then unplugged.
        var primary = new Display(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1, IsPrimary: true);
        var placed = Summon.Pinned(new WindowPosition(200, -600), [primary], new WindowSize(900, 640));
        var taller = Summon.Pinned(new WindowPosition(300, -600), [Primary], new WindowSize(900, 700));
        var shown = Summon.Grown(taller, taller.Size, [Primary]);

        Assert.IsTrue(primary.WorkingArea.Contains(placed), $"wholly on the display left (G1): {placed}");
        Assert.AreEqual(new PixelRect(200, 0, 900, 640), placed, "moved the least");
        Assert.IsTrue(Primary.WorkingArea.Contains(shown), $"its title bar reachable once shown (G2): {shown}");
    }

    [TestMethod]
    public void APinnedPlaceAcrossADisplaysEdgeWithItsTitleBarOnADisplayIsKeptWhereTheUserLeftIt()
    {
        var belowTheBottom = Summon.Pinned(new WindowPosition(100, 800), Two, new WindowSize(900, 700));
        var acrossTwo = Summon.Pinned(new WindowPosition(1500, 100), Two, new WindowSize(900, 700));
        var barPartlyAbove = Summon.Pinned(new WindowPosition(200, -20), Two, new WindowSize(900, 640));
        // On the 150 % display the strip is 48 pixels tall: from -40 it reaches 8 pixels onto the working area.
        var scaledBar = Summon.Pinned(new WindowPosition(2000, -40), Two, new WindowSize(900, 700));
        var scaledBarOff = Summon.Pinned(new WindowPosition(2000, -48), Two, new WindowSize(900, 700));

        Assert.AreEqual(new PixelRect(100, 800, 900, 700), belowTheBottom, "the title bar on the display, the rest below it (G2)");
        Assert.AreEqual(new PixelRect(1500, 100, 900, 700), acrossTwo, "straddling two displays");
        Assert.AreEqual(new PixelRect(200, -20, 900, 640), barPartlyAbove, "part of the title bar on the display is enough to grab it by");
        Assert.AreEqual(new PixelRect(2000, -40, 1350, 1050), scaledBar, "the strip scaled with the display (E1)");
        Assert.IsTrue(Right150.WorkingArea.Contains(scaledBarOff), $"a strip wholly above the working area is not: {scaledBarOff}");
    }

    [TestMethod]
    public void ThePinnedTitleStripIsMeasuredAtTheScalingOfTheDisplayHoldingMostOfTheWindow()
    {
        // Its corner on the 150 % display, nearly all of it on the 100 % one, which Windows then scales it by: a title bar of 32 pixels.
        var left150 = new Display(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1.5, IsPrimary: true);
        var right100 = new Display(new PixelRect(1920, 0, 1920, 1080), new PixelRect(1920, 0, 1920, 1032), 1, IsPrimary: false);

        var place = Summon.Pinned(new WindowPosition(1900, -40), [left150, right100], new WindowSize(900, 640));

        Assert.IsTrue(new[] { left150, right100 }.Any(display => display.WorkingArea.Contains(place)),
            $"a 32-pixel bar from -40 reaches no working area, so the window is brought wholly onto a display (G1, G2): {place}");
    }

    [TestMethod]
    public void UnpinningKeepsTheWindowWhereItIsAtTheFlyoutSizeWhollyOnScreen()
    {
        var kept = Summon.Unpinned(new PixelPoint(300, 200), Two, Flyout);
        var nearTheBottom = Summon.Unpinned(new PixelPoint(1700, 900), Two, Flyout);

        Assert.AreEqual(new PixelRect(300, 200, 460, 640), kept);
        Assert.AreEqual(new PixelRect(1460, 392, 460, 640), nearTheBottom);
    }

    [TestMethod]
    public void FromTheShortcutTheFlyoutOpensWithItsTopLeftCornerAtThePointer()
    {
        var place = Summon.AtAnchor(new SummonAnchor(new PixelPoint(300, 200)), Two, Flyout);

        Assert.AreEqual(new PixelRect(300, 200, 460, 640), place);
    }

    [TestMethod]
    public void APointerNearTheDisplaysCornerKeepsTheFlyoutWhollyInsideItsWorkingArea()
    {
        var place = Summon.AtAnchor(new SummonAnchor(new PixelPoint(1900, 1000)), Two, Flyout);

        Assert.IsTrue(Primary.WorkingArea.Contains(place), place.ToString());
        Assert.AreEqual(new PixelRect(1460, 392, 460, 640), place, "moved the least: its right edge and bottom at the working area's");
    }

    [TestMethod]
    public void APointerOnTheScaledDisplaySizesTheFlyoutByThatDisplay()
    {
        var place = Summon.AtAnchor(new SummonAnchor(new PixelPoint(2000, 100)), Two, Flyout);

        Assert.AreEqual(new PixelRect(2000, 100, 690, 960), place);
        Assert.IsTrue(Right150.WorkingArea.Contains(place));
    }

    [TestMethod]
    public void WithNoPointerAndNoWindowInFrontTheShortcutsFlyoutIsCentredOnThePrimaryDisplay()
    {
        var place = Summon.AtAnchor(new SummonAnchor(null), [Right150, Primary], Flyout);

        Assert.AreEqual(new PixelRect(730, 196, 460, 640), place);
    }

    [TestMethod]
    public void WithNoCaretAndNoPointerTheShortcutsFlyoutIsCentredOnTheDisplayOfTheWindowInFront()
    {
        var place = Summon.AtAnchor(new SummonAnchor(null, null, new PixelRect(2200, 200, 800, 600)), [Primary, Right150], Flyout);

        Assert.AreEqual(new PixelRect(2855, 216, 690, 960), place, "G1: else centred on the active display, sized by it");
    }

    [TestMethod]
    public void BesideTheCaretTheFlyoutsBottomIsAboveTheCaretsLineAndItsLeftEdgeLeftOfTheCaret()
    {
        var place = Summon.AtAnchor(new SummonAnchor(null, new PixelRect(300, 700, 1, 16)), [Small], Flyout);

        Assert.AreEqual(new PixelRect(272, 52, 460, 640), place, "left edge 28 left of the caret's, bottom 8 above its top");
    }

    [TestMethod]
    public void WithNoRoomAboveTheCaretTheFlyoutOpensBelowItsLine()
    {
        var place = Summon.AtAnchor(new SummonAnchor(null, new PixelRect(220, 50, 1, 16)), Two, Flyout);

        Assert.AreEqual(new PixelRect(192, 74, 460, 640), place, "top 8 below the caret's bottom");
    }

    [TestMethod]
    public void BesideTheCaretTheFlyoutIsStillMovedTheLeastOntoTheWorkingArea()
    {
        var nearTheRight = Summon.AtAnchor(new SummonAnchor(null, new PixelRect(1000, 700, 1, 16)), [Small], Flyout);
        var atTheLeft = Summon.AtAnchor(new SummonAnchor(null, new PixelRect(10, 700, 1, 16)), [Small], Flyout);

        Assert.AreEqual(new PixelRect(820, 52, 460, 640), nearTheRight, "above the caret, its right edge at the working area's");
        Assert.AreEqual(new PixelRect(0, 52, 460, 640), atTheLeft);
        Assert.IsTrue(new[] { nearTheRight, atTheLeft }.All(Small.WorkingArea.Contains), "always fully on screen (G1)");
    }

    [TestMethod]
    public void WithRoomNeitherAboveNorBelowTheCaretTheFlyoutOpensBesideItsLine()
    {
        var caretOnTheLeft = new PixelRect(220, 243, 1, 16);
        var caretOnTheRight = new PixelRect(1000, 400, 1, 16);

        var right = Summon.AtAnchor(new SummonAnchor(null, caretOnTheLeft), [Small], Flyout);
        var left = Summon.AtAnchor(new SummonAnchor(null, caretOnTheRight), [Small], Flyout);

        Assert.AreEqual(new PixelRect(229, 0, 460, 640), right, "8 right of the caret, centred on its line and moved onto the working area");
        Assert.AreEqual(new PixelRect(532, 88, 460, 640), left, "8 left of the caret when the right has no room");
        Assert.IsFalse(right.Intersects(caretOnTheLeft) || left.Intersects(caretOnTheRight), "beside the caret, not over it (G1)");
        Assert.IsTrue(Small.WorkingArea.Contains(right) && Small.WorkingArea.Contains(left), "always fully on screen (G1)");
    }

    [TestMethod]
    public void WithRoomOnNoSideOfTheCaretTheFlyoutIsNarrowedBesideItForThatShowingButNeverBelowTheMinimum()
    {
        // 1280×800 at 100 %: a flyout remembered 900 wide and as tall as the working area fits neither above, below nor beside.
        var tall = new WindowSize(900, 752);
        var caret = new PixelRect(640, 300, 2, 20);

        var narrowed = Summon.AtAnchor(new SummonAnchor(null, caret), [Small], tall);

        Assert.AreEqual(new PixelRect(0, 0, 632, 752), narrowed, "on the wider side, narrowed to its room, 8 from the caret");
        Assert.IsFalse(narrowed.Intersects(caret), "beside the caret, not over it (G1)");
        Assert.IsGreaterThanOrEqualTo(WindowSize.Minimum.Width, (double)narrowed.Width, "no narrower than the window takes");
    }

    [TestMethod]
    public void WithRoomForTheMinimumWidthOnNoSideOfTheCaretTheFlyoutLiesOverItAtThatWidthWhollyOnScreen()
    {
        // 1280×800 at a custom 200 %: the flyout, 920 by the working area's 752, fits neither above, below nor beside,
        // and neither side has the 916 the window takes no less than.
        var narrow = new Display(new PixelRect(0, 0, 1280, 800), new PixelRect(0, 0, 1280, 752), 2, IsPrimary: true);
        var caretLeftOfCentre = new PixelRect(600, 368, 2, 32);
        var caretRightOfCentre = new PixelRect(700, 368, 2, 32);

        var left = Summon.AtAnchor(new SummonAnchor(null, caretRightOfCentre), [narrow], Flyout);
        var right = Summon.AtAnchor(new SummonAnchor(null, caretLeftOfCentre), [narrow], Flyout);

        Assert.AreEqual(new PixelRect(0, 0, 916, 752), left, "from the wider side's edge, at the minimum width on that display");
        Assert.AreEqual(new PixelRect(364, 0, 916, 752), right, "moved the least onto the working area");
        Assert.IsTrue(left.Intersects(caretRightOfCentre) && right.Intersects(caretLeftOfCentre), "over the caret, since neither side has room for the minimum width (G1)");
        Assert.IsTrue(narrow.WorkingArea.Contains(left) && narrow.WorkingArea.Contains(right), "always fully on screen (G1)");
    }

    [TestMethod]
    public void ACaretOnTheScaledDisplayPlacesAndSizesTheFlyoutByThatDisplay()
    {
        var above = Summon.AtAnchor(new SummonAnchor(null, new PixelRect(2000, 1000, 2, 24)), Two, Flyout);
        var below = Summon.AtAnchor(new SummonAnchor(null, new PixelRect(3000, 100, 2, 24)), Two, Flyout);

        Assert.AreEqual(new PixelRect(1958, 28, 690, 960), above, "the gaps scale with the display: 42 left, 12 above");
        Assert.AreEqual(new PixelRect(2958, 136, 690, 960), below);
        Assert.IsTrue(Right150.WorkingArea.Contains(above) && Right150.WorkingArea.Contains(below), "on the caret's display, not the primary");
    }

    [TestMethod]
    public void WithBothACaretAndAPointerTheFlyoutOpensBesideTheCaret()
    {
        var place = Summon.AtAnchor(new SummonAnchor(new PixelPoint(5, 5), new PixelRect(300, 700, 1, 16)), [Small], Flyout);

        Assert.AreEqual(new PixelRect(272, 52, 460, 640), place, "the pointer only when no caret can be found (G1)");
    }

    [TestMethod]
    public void LosingFocusTheShortcutOrAPasteHidesOnlyTheFlyoutAndNotWhileItsDialogIsUpItsDragIsInProgressOrAnEditIsOpen()
    {
        Assert.IsTrue(Summon.FlyoutHides(pinned: false, dialogUp: false, dragInProgress: false, editOpen: false));
        Assert.IsFalse(Summon.FlyoutHides(pinned: true, dialogUp: false, dragInProgress: false, editOpen: false));
        Assert.IsFalse(Summon.FlyoutHides(pinned: false, dialogUp: true, dragInProgress: false, editOpen: false));
        Assert.IsFalse(Summon.FlyoutHides(pinned: false, dialogUp: false, dragInProgress: true, editOpen: false));
        Assert.IsFalse(Summon.FlyoutHides(pinned: true, dialogUp: false, dragInProgress: true, editOpen: false));
        Assert.IsFalse(Summon.FlyoutHides(pinned: false, dialogUp: false, dragInProgress: false, editOpen: true), "G2: the flyout stays while an edit is open");
    }

    [TestMethod]
    public void ANotificationOpensAboveTheTrayIconOnThePrimaryDisplay()
    {
        var icon = new PixelRect(1800, 1032, 32, 48);

        var place = Summon.NotificationPlace(icon, Two, new WindowSize(260, 48));

        Assert.AreEqual(new PixelSize(260, 48), place.Size, "sized to its text at the primary display's scaling");
        Assert.IsTrue(Primary.WorkingArea.Contains(place), place.ToString());
        Assert.IsLessThan(icon.Y, place.Bottom, "above the icon");
        Assert.IsGreaterThan(icon.X - 260, place.X, "beside the icon, not across the display");
    }

    [TestMethod]
    public void WithNoIconOrAnIconOnAnotherDisplayTheNotificationOpensAboveThePrimaryDisplaysBottomRightCorner()
    {
        var size = new WindowSize(260, 48);

        var unknown = Summon.NotificationPlace(null, Two, size);
        var elsewhere = Summon.NotificationPlace(new PixelRect(4300, 1392, 32, 48), Two, size);

        Assert.AreEqual(new PixelRect(1920 - 12 - 260, 1032 - 12 - 48, 260, 48), unknown);
        Assert.AreEqual(unknown, elsewhere, "notifications appear on the primary display (G16)");
    }

    [TestMethod]
    public void ANotificationOnAScaledPrimaryDisplayIsSizedByItAndStaysWhollyOnIt()
    {
        var scaled = new Display(new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 1.5, IsPrimary: true);

        var place = Summon.NotificationPlace(new PixelRect(2540, 1392, 20, 48), [scaled], new WindowSize(260, 48));

        Assert.AreEqual(new PixelSize(390, 72), place.Size);
        Assert.IsTrue(scaled.WorkingArea.Contains(place), place.ToString());
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void EscClosesAnOpenTagListAndNothingElseBeforeAnythingEscDoes(bool pinned, bool previewOpen) =>
        Assert.AreEqual(EscapeAction.ClosePicker, Summon.OnEscape("do", pinned, previewOpen, pickerOpen: true));

    [TestMethod]
    public void EscClosesThePreviewThenClearsTheSearchThenHidesTheFlyoutAndPinnedGoesNoFurther()
    {
        Assert.AreEqual(EscapeAction.ClosePreview, Summon.OnEscape("do", pinned: false, previewOpen: true), "the preview first, before the search it covers");
        Assert.AreEqual(EscapeAction.ClosePreview, Summon.OnEscape("", pinned: false, previewOpen: true), "never the flyout hidden with the preview open");
        Assert.AreEqual(EscapeAction.ClosePreview, Summon.OnEscape("", pinned: true, previewOpen: true), "pinned, the preview still closes");
        Assert.AreEqual(EscapeAction.ClearSearch, Summon.OnEscape("do", pinned: false, previewOpen: false));
        Assert.AreEqual(EscapeAction.ClearSearch, Summon.OnEscape(" ", pinned: true, previewOpen: false));
        Assert.AreEqual(EscapeAction.Hide, Summon.OnEscape("", pinned: false, previewOpen: false));
        Assert.AreEqual(EscapeAction.Nothing, Summon.OnEscape("", pinned: true, previewOpen: false));
    }
}
