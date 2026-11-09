using Avalonia;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The places the desktop checks lay their patterns, counter and stand-in tray icon, as arithmetic on a display's record
/// (G3, G14, G16, E1): on a 1280 × 800 display at 100 % the places the checks have always used; on a larger display, and
/// on one at 150 %, the pattern still under the flyout, the + menu's area still reaching the working area's right edge,
/// and the notification still above the stand-in icon and inside the pattern under it; and the stand-in icon the checks'
/// seam answers is the one for this PC's primary display. These checks touch no window.
/// </summary>
[TestClass]
public sealed class DisplayPlacesTests
{
    /// <summary>A capture card's size, the largest notification the checks show above the stand-in icon, in device-independent units.</summary>
    private static readonly WindowSize Card = new(430, 146);

    [TestMethod]
    public void OnA1280By800DisplayAt100PercentThePlacesAreTheOnesTheChecksHaveAlwaysUsed()
    {
        var display = new Display(new PixelRect(0, 0, 1280, 800), new PixelRect(0, 0, 1280, 752), 1, IsPrimary: true);
        var client = new PixelSize(460, 508);

        Assert.AreEqual(new PixelPoint(800, 20), DisplayPlaces.Flyout(display, client).Position, "the flyout at the top right");
        Assert.AreEqual(new PixelRect(820, 40, 220, 200), DisplayPlaces.UnderFlyout(display, client));
        Assert.AreEqual(new PixelRect(640, 20, 640, 440), DisplayPlaces.AroundPlusMenu(display, client));
        Assert.AreEqual(new PixelRect(500, 180, 760, 480), DisplayPlaces.AroundPinnedPlusButton(display, new PixelRect(560, 260, 600, 480)));
        Assert.AreEqual(new PixelRect(680, 520, 24, 24), DisplayPlaces.TrayIcon(display));
        Assert.AreEqual(new PixelRect(400, 320, 560, 320), DisplayPlaces.UnderNotification(display));
        Assert.AreEqual(new PixelRect(400, 380, 560, 280), DisplayPlaces.Counter(display));
    }

    [TestMethod]
    [DataRow(1280, 800, 752, 1.0, DisplayName = "1280 × 800 at 100 %")]
    [DataRow(1920, 1080, 1032, 1.0, DisplayName = "1920 × 1080 at 100 %")]
    [DataRow(2560, 1440, 1368, 1.5, DisplayName = "2560 × 1440 at 150 %")]
    public void ThePatternLiesUnderTheFlyoutAndThePlusMenusAreaReachesTheWorkingAreasRightEdge(int width, int height, int workingHeight, double scaling)
    {
        var display = new Display(new PixelRect(0, 0, width, height), new PixelRect(0, 0, width, workingHeight), scaling, IsPrimary: true);
        // The stash window's client as the checks show it: its default width, at its minimum height.
        var client = PixelSize.FromSize(new Size(WindowSize.FlyoutDefault.Width, WindowSize.Minimum.Height), scaling);
        var flyout = DisplayPlaces.Flyout(display, client);
        var under = DisplayPlaces.UnderFlyout(display, client);
        var around = DisplayPlaces.AroundPlusMenu(display, client);

        Assert.AreEqual(client, flyout.Size);
        Assert.IsTrue(display.WorkingArea.Contains(flyout), $"the flyout at {flyout} wholly inside {display.WorkingArea}");
        Assert.IsTrue(flyout.Contains(under), $"the pattern at {under} under the flyout at {flyout} (G3, G14)");
        Assert.AreEqual(display.WorkingArea.Right, around.Right, "the + menu's area ends at the working area's right edge, which the menu may reach");
        Assert.IsGreaterThan(flyout.Right, around.Right, "past the flyout's own right edge");
        Assert.IsTrue(around.Contains(new PixelRect(flyout.X, flyout.Y, flyout.Width, DisplayPlaces.Scaled(display, 60))), $"the area at {around} holds the flyout's top strip, where the + button is");
        Assert.IsTrue(display.WorkingArea.Contains(around), $"the area at {around} wholly on screen");
    }

    [TestMethod]
    [DataRow(1280, 800, 752, 1.0, DisplayName = "1280 × 800 at 100 %")]
    [DataRow(1920, 1080, 1032, 1.0, DisplayName = "1920 × 1080 at 100 %")]
    [DataRow(2560, 1440, 1368, 1.5, DisplayName = "2560 × 1440 at 150 %")]
    public void TheAreaAroundAPinnedWindowsPlusButtonHoldsTheWindowsTopAtItsScaledSize(int width, int height, int workingHeight, double scaling)
    {
        var display = new Display(new PixelRect(0, 0, width, height), new PixelRect(0, 0, width, workingHeight), scaling, IsPrimary: true);
        // A pinned window the check places at a corner of its own, its size in device-independent units as the settings keep it.
        var pinned = new PixelRect(new PixelPoint(560, 260), PixelSize.FromSize(new Size(600, 480), scaling));
        var around = DisplayPlaces.AroundPinnedPlusButton(display, pinned);
        var top = new PixelRect(pinned.X, pinned.Y, pinned.Width, DisplayPlaces.Scaled(display, 120));

        Assert.IsTrue(around.Contains(top), $"the area at {around} holds the window's top at {top}, where the + button is");
        Assert.IsTrue(display.WorkingArea.Contains(around), $"the area at {around} wholly on screen");
    }

    [TestMethod]
    [DataRow(1280, 800, 752, 1.0, DisplayName = "1280 × 800 at 100 %")]
    [DataRow(1920, 1080, 1032, 1.0, DisplayName = "1920 × 1080 at 100 %")]
    [DataRow(2560, 1440, 1368, 1.5, DisplayName = "2560 × 1440 at 150 %")]
    public void ANotificationStandsAboveTheStandInIconInsideThePatternUnderItAndOverTheCounter(int width, int height, int workingHeight, double scaling)
    {
        var display = new Display(new PixelRect(0, 0, width, height), new PixelRect(0, 0, width, workingHeight), scaling, IsPrimary: true);
        var icon = DisplayPlaces.TrayIcon(display);
        var card = Summon.NotificationPlace(icon, [display], Card);
        var margin = DisplayPlaces.Scaled(display, 20);
        var around = new PixelRect(card.X - margin, card.Y - margin, card.Width + 2 * margin, card.Height + 2 * margin);
        var under = DisplayPlaces.UnderNotification(display);
        var counter = DisplayPlaces.Counter(display);

        Assert.IsGreaterThanOrEqualTo(display.Bounds.Center.Y, icon.Center.Y, $"the icon at {icon} at or below the display's centre line (G16)");
        Assert.AreEqual((int)Math.Round(Card.Width * scaling), card.Width, "the card scaled with the display");
        Assert.IsLessThanOrEqualTo(icon.Y, card.Bottom, $"the card at {card} above the icon at {icon} (G16)");
        Assert.IsTrue(under.Contains(around), $"the card at {card}, with room round it, inside the pattern at {under} (S4)");
        Assert.IsTrue(display.WorkingArea.Contains(under) && display.WorkingArea.Contains(counter), "both on screen");
        Assert.IsTrue(counter.Intersects(card) && card.Bottom < counter.Bottom, $"the card at {card} over the counter at {counter}, short of its bottom");
        var centred = Summon.AboveTrayIcon(null, [display], WindowSize.FlyoutDefault);
        Assert.IsTrue(centred.Intersects(card), $"the card at {card} over the flyout summoned with no tray icon, centred at {centred} (G16)");
    }

    [TestMethod]
    [DataRow(1.0, 560, 160, 240, 160, DisplayName = "at 100 %")]
    [DataRow(1.25, 700, 200, 300, 200, DisplayName = "at 125 %")]
    [DataRow(1.5, 840, 240, 360, 240, DisplayName = "at 150 %")]
    [DataRow(1.75, 980, 280, 420, 280, DisplayName = "at 175 %")]
    public void AReferencePlaceIsTheSamePlaceMagnifiedEachCoordinateRoundedOnce(double scaling, int x, int y, int width, int height)
    {
        var display = new Display(new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), scaling, IsPrimary: true);

        Assert.AreEqual(new PixelRect(x, y, width, height), DisplayPlaces.Rect(display, 560, 160, 240, 160));
        Assert.AreEqual(new PixelPoint(x, y), DisplayPlaces.At(display, 560, 160));
        Assert.AreEqual(DisplayPlaces.Scaled(display, 12), DisplayPlaces.Rect(display, 0, 0, 12, 0).Width, "one conversion: a length as Scaled gives it");
    }

    [TestMethod]
    public void TheStandInIconTheChecksSeamAnswersIsTheOneForThisPCsPrimaryDisplay()
    {
        using var tray = new WindowsTray();
        var primary = new WindowsScreen().Displays().Single(display => display.IsPrimary);

        Assert.AreEqual(DisplayPlaces.TrayIcon(primary), new TrayIconMidScreen(new WindowsQuietWindows(tray)).TrayIconRectangle(), $"from the Screen seam's primary display, {primary}, its bounds and scaling (E1)");
    }
}
