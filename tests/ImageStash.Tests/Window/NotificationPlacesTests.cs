using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Where a card's opening asks the flyout to stand, through the card's own Edit, Trim and click on two displays: on the
/// display the card stood on, whichever path the opening takes, above the tray for a card there or one whose display has
/// gone (G1, G16); and every notifications window placed again, clear of the stash window, whenever that window's
/// rectangle changes, through the places the app tells (G16).
/// </summary>
[TestClass]
public sealed class NotificationPlacesTests
{
    private static readonly PixelRect TrayIcon = new(1200, 752, 32, 48);

    [TestMethod]
    public async Task EditOnACardOnTheSecondDisplayOpensTheFlyoutThereWithItsTypedNameKeptAndOnTheTraysAboveTheIcon()
    {
        var (displays, placed) = await Open();
        using var listed = displays.Listed;
        await displays.Screenshot(TwoDisplays.Second);
        await displays.Screenshot(RecordingScreen.Primary);
        var shot = (await listed.Stash.All()).OrderBy(item => item.Id).First();

        OnControls(async () =>
        {
            var onSecond = CaptureCardPresses.Newest(displays.OnSecond);
            onSecond.Field.Edit();
            onSecond.Field.GetLogicalDescendants().OfType<TextBox>().Single().Text = "cat facepalm";
            CaptureCardPresses.Click(onSecond, "Edit");
            await Handing.Eventually(() => listed.Editing.Session is not null);
        });

        Assert.AreEqual(SecondCorner, placed.FlyoutAsked.Single(), "above the second display's corner, at its scaling");
        Assert.AreEqual("cat facepalm", (await listed.Stash.All()).Single(item => item.Id == shot.Id).Name, "the typed name kept");
        listed.Editing.Cancel();

        OnControls(async () =>
        {
            CaptureCardPresses.Click(CaptureCardPresses.Newest(listed.Notifications), "Edit");
            await Handing.Eventually(() => placed.FlyoutAsked.Count == 2 && listed.Editing.Session is not null);
        });

        Assert.AreEqual(Summon.AboveTrayIcon(TrayIcon, listed.Screen.Shown, WindowSize.FlyoutDefault), placed.FlyoutAsked[1], "the tray's card: above the tray icon");
        listed.Editing.Cancel();
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ACardsClickOnTheSecondDisplayOpensTheFlyoutThereOnEitherPath(bool inFront)
    {
        var (displays, placed) = await Open();
        using var listed = displays.Listed;
        await displays.Screenshot(TwoDisplays.Second);
        var opened = new List<CaptureOpening>();
        listed.CaptureNotification.OpenRequested += opened.Add;

        OnControls(async () =>
        {
            var card = CaptureCardPresses.Newest(displays.OnSecond);
            card.RaiseEvent(CaptureCardPresses.Body(card));
            await Handing.Eventually(() => opened.Count == 1);
        });

        // The press asks with the activation the card's window holds; the opening as it is asked on each path.
        listed.CaptureNotification.Open(opened.Single() with { NotificationInFront = inFront });

        CollectionAssert.AreEqual(new[] { SecondCorner, SecondCorner }, placed.FlyoutAsked, "the click, and the opening on this path");
    }

    [TestMethod]
    public async Task TrimOnARecordingsCardOnTheSecondDisplayOpensTheFlyoutThere()
    {
        var displays = await TwoDisplays.OpenRecording();
        var placed = Placed(displays.Listed);
        using var listed = displays.Listed;
        await displays.FinishRecording(await displays.RecordingWaitingOn(TwoDisplays.Second));

        OnControls(async () =>
        {
            CaptureCardPresses.Click(CaptureCardPresses.Newest(displays.OnSecond), "Trim");
            await Handing.Eventually(() => listed.Editing.Session is not null);
        });

        Assert.AreEqual(SecondCorner, placed.FlyoutAsked.Single());
        listed.Editing.Cancel();
    }

    [TestMethod]
    public async Task ACardWhoseDisplayHasGoneOpensTheFlyoutAboveTheTrayIcon()
    {
        var (displays, placed) = await Open();
        using var listed = displays.Listed;
        await displays.Screenshot(TwoDisplays.Second);
        listed.Screen.Shown = [RecordingScreen.Primary];

        OnControls(async () =>
        {
            var card = CaptureCardPresses.Newest(displays.OnSecond);
            card.RaiseEvent(CaptureCardPresses.Body(card));
            await Handing.Eventually(() => placed.FlyoutAsked.Count == 1);
        });

        Assert.AreEqual(Summon.AboveTrayIcon(TrayIcon, [RecordingScreen.Primary], WindowSize.FlyoutDefault), placed.FlyoutAsked.Single());
    }

    [TestMethod]
    public async Task ACardShowingAboveTheTrayIsPlacedAgainClearOfAFlyoutSummonedFromTheTray()
    {
        var (displays, placed) = await Open();
        using var listed = displays.Listed;
        await displays.Screenshot(RecordingScreen.Primary);
        var usual = placed.PlaceOf(listed.Notifications);
        var flyout = Summon.AboveTrayIcon(TrayIcon, listed.Screen.Shown, WindowSize.FlyoutDefault);
        Assert.IsTrue(usual.Intersects(flyout), "the card stands where the flyout will");

        // As the window summoned from the tray tells the places where it shows.
        placed.Places.StashWindowAt(flyout);

        var clear = placed.PlaceOf(listed.Notifications);
        Assert.IsFalse(clear.Intersects(flyout), $"{clear} clear of the flyout at {flyout}");
        Assert.IsTrue(RecordingScreen.Primary.WorkingArea.Contains(clear), clear.ToString());

        placed.Places.StashWindowAt(null);

        Assert.AreEqual(usual, placed.PlaceOf(listed.Notifications), "back above the tray once the window hides");
    }

    [TestMethod]
    public async Task EditOnTheNewerOfTwoCardsLeavesTheOlderClearOfTheFlyout()
    {
        var (displays, placed) = await Open();
        using var listed = displays.Listed;
        await displays.Screenshot(RecordingScreen.Primary);
        await displays.Screenshot(RecordingScreen.Primary);

        OnControls(async () =>
        {
            CaptureCardPresses.Click(CaptureCardPresses.Newest(listed.Notifications), "Edit");
            await Handing.Eventually(() => listed.Editing.Session is not null && listed.Notifications.Shown.Count == 1);
        });

        var flyout = placed.FlyoutAsked.Single();
        Assert.IsFalse(placed.PlaceOf(listed.Notifications).Intersects(flyout), $"the older card at {placed.PlaceOf(listed.Notifications)} clear of the editor's flyout at {flyout}");
        listed.Editing.Cancel();
    }

    [TestMethod]
    public async Task TheOlderCardKeepsItsSideOfTheFlyoutWhenTheNewerGoesAtItsEdit()
    {
        var (displays, placed) = await Open();
        using var listed = displays.Listed;
        // A flyout leaving room above it for one card and not for two.
        listed.Settings.Change(s => s with { FlyoutSize = new WindowSize(460, 560) });
        await displays.Screenshot(RecordingScreen.Primary);
        await displays.Screenshot(RecordingScreen.Primary);
        var before = placed.AskedOf(listed.Notifications).Count;

        OnControls(async () =>
        {
            CaptureCardPresses.Click(CaptureCardPresses.Newest(listed.Notifications), "Edit");
            await Handing.Eventually(() => listed.Editing.Session is not null && listed.Notifications.Shown.Count == 1);
        });

        var flyout = placed.FlyoutAsked.Single();
        var since = placed.AskedOf(listed.Notifications).Skip(before)
            .Where(place => !place.Intersects(flyout)).ToList();
        Assert.IsNotEmpty(since, "placed clear of the flyout");
        Assert.IsTrue(since.All(place => place.Right <= flyout.X), $"every place left of the flyout at {flyout}, never jumping above it: {string.Join(", ", since)}");
        Assert.AreEqual(since[^1], placed.PlaceOf(listed.Notifications), "where it stands once the newer card has gone");
        Assert.AreEqual(130, placed.PlaceOf(listed.Notifications).Height, "the older card alone");
        listed.Editing.Cancel();
    }

    [TestMethod]
    public async Task AStackIsPlacedAgainWhenThePinnedWindowIsDraggedOrResizedOverItsPlace()
    {
        var (displays, placed) = await Open();
        using var listed = displays.Listed;
        await displays.Screenshot(RecordingScreen.Primary);
        var usual = placed.PlaceOf(listed.Notifications);

        placed.Places.StashWindowAt(new PixelRect(1400, 100, 900, 700));
        Assert.AreEqual(usual, placed.PlaceOf(listed.Notifications), "the pinned window on the other display leaves it above the tray");

        var dragged = new PixelRect(usual.X - 100, 80, 700, 660);
        placed.Places.StashWindowAt(dragged);
        Assert.IsFalse(placed.PlaceOf(listed.Notifications).Intersects(dragged), "dragged over it, the stack stands off it");

        var resized = new PixelRect(0, 0, 1280, 752);
        placed.Places.StashWindowAt(resized);
        Assert.AreEqual(usual, placed.PlaceOf(listed.Notifications), "resized to fill the working area, it leaves no room: the usual place");
    }

    [TestMethod]
    public async Task AnotherCardOnTheSecondDisplayStandsClearOfTheFlyoutACardThereOpened()
    {
        var (displays, placed) = await Open();
        using var listed = displays.Listed;
        await displays.Screenshot(TwoDisplays.Second);
        await displays.Screenshot(TwoDisplays.Second);

        OnControls(async () =>
        {
            var newer = CaptureCardPresses.Newest(displays.OnSecond);
            newer.RaiseEvent(CaptureCardPresses.Body(newer));
            await Handing.Eventually(() => placed.FlyoutAsked.Count == 1 && displays.OnSecond.Shown.Count == 1);
        });

        var flyout = placed.FlyoutAsked.Single();
        var stack = placed.PlaceOf(displays.OnSecond);
        Assert.IsFalse(stack.Intersects(flyout), $"the other card at {stack} clear of the flyout at {flyout}");
        Assert.IsTrue(TwoDisplays.Second.WorkingArea.Contains(stack), stack.ToString());
    }

    private static async Task<(TwoDisplays Displays, PlacedNotifications Placed)> Open()
    {
        var displays = await TwoDisplays.Open();
        return (displays, Placed(displays.Listed));
    }

    private static PlacedNotifications Placed(ListedStash listed) => new(listed) { TrayIcon = TrayIcon };

    /// <summary>
    /// The flyout, 460 × 640 at the second display's scaling of 1.5, 18 pixels (the gap, scaled) from its working area's
    /// right edge at 3200 and bottom at 1040.
    /// </summary>
    private static readonly PixelRect SecondCorner = new(3182 - 690, 1022 - 960, 690, 960);

    private static void OnControls(Func<Task> act) => ControlThread.Run(() => ControlThread.FinishOnDispatcher(act));
}
