using Avalonia;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The places a notification gives, as pure places in physical pixels (G1, G16, E1): the flyout a click on one opens,
/// above the tray icon or above the bottom-right corner of the other display its card stood on, wholly inside it at its
/// scaling; and a notifications window standing clear of the stash window's real rectangle where it covers the window's
/// usual place, above it, else beside it, wholly inside its display's working area, or in its usual place where none has
/// room.
/// </summary>
[TestClass]
public sealed class SummonNotificationTests
{
    private static readonly Display Primary = new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1, IsPrimary: true);
    private static readonly Display Right150 = new(new PixelRect(1920, 0, 2560, 1440), new PixelRect(1920, 0, 2560, 1392), 1.5, IsPrimary: false);
    private static readonly Display[] Two = [Primary, Right150];
    private static readonly PixelRect Icon = new(1800, 1032, 32, 48);
    private static readonly WindowSize Flyout = new(460, 640);
    private static readonly WindowSize TwoCards = new(480, 260);

    [TestMethod]
    public void TheFlyoutFromACardOnAnotherDisplayOpensAboveThatDisplaysCornerAtItsScaling()
    {
        var place = Summon.FromNotification(Icon, Two, Flyout, Right150);

        Assert.IsTrue(Right150.WorkingArea.Contains(place), place.ToString());
        Assert.AreEqual(new PixelSize(690, 960), place.Size, "the remembered size at that display's scaling");
        Assert.AreEqual(Right150.WorkingArea.Right - 18, place.Right, "the gap, scaled, from its right edge");
        Assert.AreEqual(Right150.WorkingArea.Bottom - 18, place.Bottom, "and from its bottom");
    }

    [TestMethod]
    public void TheFlyoutFromACardAboveTheTrayOrOnTheprimaryOpensAboveTheTrayIcon()
    {
        var aboveTray = Summon.AboveTrayIcon(Icon, Two, Flyout);

        Assert.AreEqual(aboveTray, Summon.FromNotification(Icon, Two, Flyout, null));
        Assert.AreEqual(aboveTray, Summon.FromNotification(Icon, Two, Flyout, Primary));
    }

    [TestMethod]
    public void TheFlyoutFromACardWhoseDisplayIsGoneOpensAboveTheTrayIcon()
    {
        Display[] primaryAlone = [Primary];
        var moved = Right150 with { Bounds = new PixelRect(1920, 0, 1920, 1080), WorkingArea = new PixelRect(1920, 0, 1920, 1032) };

        Assert.AreEqual(Summon.AboveTrayIcon(Icon, primaryAlone, Flyout), Summon.FromNotification(Icon, primaryAlone, Flyout, Right150), "unplugged");
        Assert.AreEqual(Summon.AboveTrayIcon(Icon, [Primary, moved], Flyout), Summon.FromNotification(Icon, [Primary, moved], Flyout, Right150), "no longer the display it was");
    }

    [TestMethod]
    public void TheFlyoutFromACardOnASmallScaledDisplayIsWhollyInsideIt()
    {
        var small = new Display(new PixelRect(1920, 0, 800, 600), new PixelRect(1920, 0, 800, 560), 1.5, IsPrimary: false);

        var place = Summon.FromNotification(Icon, [Primary, small], Flyout, small);

        Assert.IsTrue(small.WorkingArea.Contains(place), place.ToString());
    }

    [TestMethod]
    public void ACardsStackStandsAboveAFlyoutSummonedFromTheTrayOverItsPlace()
    {
        var flyout = Summon.AboveTrayIcon(Icon, Two, Flyout);
        Assert.IsTrue(Summon.NotificationPlace(Icon, Two, TwoCards).Intersects(flyout), "the usual place lies under the flyout");

        var place = Summon.NotificationPlace(Icon, Two, TwoCards, clearOf: flyout);

        Assert.IsFalse(place.Intersects(flyout), $"{place} clear of {flyout}");
        Assert.IsLessThanOrEqualTo(flyout.Y, place.Bottom, "above it");
        Assert.AreEqual(flyout.Right, place.Right, "right-aligned to it");
        Assert.AreEqual(new PixelSize(480, 260), place.Size, "at its own size");
        Assert.IsTrue(Primary.WorkingArea.Contains(place), place.ToString());
    }

    [TestMethod]
    public void WithNoRoomAboveTheStackStandsBesideTheWindow()
    {
        var tall = new PixelRect(1300, 20, 600, 1000);

        var place = Summon.NotificationPlace(Icon, Two, TwoCards, clearOf: tall);

        Assert.IsFalse(place.Intersects(tall), $"{place} clear of {tall}");
        Assert.IsLessThanOrEqualTo(tall.X, place.Right, "left of it");
        Assert.IsTrue(Primary.WorkingArea.Contains(place), place.ToString());
    }

    [TestMethod]
    public void WithRoomAboveAndNoneBesideTheStackStandsAbove()
    {
        var wide = new PixelRect(0, 500, 1920, 520);

        var place = Summon.NotificationPlace(Icon, Two, TwoCards, clearOf: wide);

        Assert.IsLessThanOrEqualTo(wide.Y, place.Bottom, $"{place} above {wide}");
        Assert.IsTrue(Primary.WorkingArea.Contains(place), place.ToString());
    }

    [TestMethod]
    public void AWindowFillingTheWorkingAreaLeavesTheStackInItsUsualPlaceWhole()
    {
        var usual = Summon.NotificationPlace(Icon, Two, TwoCards);

        var place = Summon.NotificationPlace(Icon, Two, TwoCards, clearOf: Primary.WorkingArea);

        Assert.AreEqual(usual, place);
        Assert.IsTrue(Primary.WorkingArea.Contains(place), "wholly on screen at its own size");
    }

    [TestMethod]
    public void AWindowElsewhereLeavesTheStackAboveTheTrayIcon()
    {
        var usual = Summon.NotificationPlace(Icon, Two, TwoCards);
        var besideACaret = new PixelRect(600, 200, 460, 640);
        var pinnedOnTheOther = new PixelRect(3000, 300, 900, 900);

        Assert.AreEqual(usual, Summon.NotificationPlace(Icon, Two, TwoCards, clearOf: besideACaret), "a flyout beside a caret mid-screen");
        Assert.AreEqual(usual, Summon.NotificationPlace(Icon, Two, TwoCards, clearOf: pinnedOnTheOther), "the pinned window on the other display");
    }

    [TestMethod]
    public void AnotherDisplaysStackStandsClearOfTheFlyoutACardThereOpened()
    {
        var flyout = Summon.FromNotification(Icon, Two, Flyout, Right150);

        var place = Summon.NotificationPlace(Icon, Two, TwoCards, Right150, flyout);

        Assert.IsFalse(place.Intersects(flyout), $"{place} clear of {flyout}");
        Assert.IsTrue(Right150.WorkingArea.Contains(place), place.ToString());
        Assert.AreEqual(new PixelSize(720, 390), place.Size, "at that display's scaling");
    }

    [TestMethod]
    public void AStackWhoseDisplayHasGoneStandsInTheTraysPlaceWhollyOnThePrimary()
    {
        Display[] primaryAlone = [Primary];

        var place = Summon.NotificationPlace(Icon, primaryAlone, TwoCards, Right150, Summon.AboveTrayIcon(Icon, primaryAlone, Flyout));

        Assert.IsTrue(Primary.WorkingArea.Contains(place), $"{place} on a display still shown");
        Assert.AreEqual(Summon.NotificationPlace(Icon, primaryAlone, TwoCards, clearOf: Summon.AboveTrayIcon(Icon, primaryAlone, Flyout)), place, "as the tray's stack stands");
        Assert.AreEqual(Summon.NotificationPlace(Icon, primaryAlone, TwoCards), Summon.NotificationPlace(Icon, primaryAlone, TwoCards, Right150), "above the tray icon, with no window shown");
    }

    [TestMethod]
    public void AStackThatShrinksBesideTheSameWindowKeepsTheSideItTook()
    {
        // Room above for one card and not for two.
        var window = new PixelRect(1300, 150, 600, 870);
        var oneCard = new WindowSize(480, 130);
        var two = Summon.NotificationPlace(Icon, Two, TwoCards, clearOf: window);
        Assert.IsLessThanOrEqualTo(window.X, two.Right, "two cards stand left of it");

        var kept = Summon.NotificationPlace(Icon, Two, oneCard, clearOf: window, standing: two);
        var afresh = Summon.NotificationPlace(Icon, Two, oneCard, clearOf: window);

        Assert.IsLessThanOrEqualTo(window.X, kept.Right, $"{kept}: still left of it once a card has gone");
        Assert.AreEqual(new PixelSize(480, 130), kept.Size, "at the one card's size");
        Assert.IsFalse(kept.Intersects(window));
        Assert.IsLessThanOrEqualTo(window.Y, afresh.Bottom, "placed afresh, one card stands above");
    }

    [TestMethod]
    public void AWindowMostlyOnThePrimaryCoversTheOtherDisplaysStackWhereItReachesOverIt()
    {
        var left = new Display(new PixelRect(-800, 432, 800, 600), new PixelRect(-800, 432, 800, 560), 1, IsPrimary: false);
        Display[] displays = [Primary, left];
        var pinned = new PixelRect(-300, 300, 1400, 750);
        Assert.AreEqual(Primary, Summon.StandsOn(pinned, displays), "the window stands on the primary display");
        Assert.IsTrue(Summon.NotificationPlace(Icon, displays, TwoCards, left).Intersects(pinned), "and reaches over the other display's stack");

        var place = Summon.NotificationPlace(Icon, displays, TwoCards, left, pinned);

        Assert.IsFalse(place.Intersects(pinned), $"{place} clear of {pinned}");
        Assert.IsTrue(left.WorkingArea.Contains(place), place.ToString());
    }
}
