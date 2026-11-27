using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using ImageStash.Capture;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// A screenshot's or a recording's card stands on the display its region was on: in that display's stack when it is
/// another than the primary, above the tray otherwise, each capture by its own region; a capture's refusal or failure,
/// and Stash clipboard image, taken on no display, stand above the tray (G16, G13). A card on another display acts as
/// one above the tray does (G16, G8, G17), and keeps its display for the opening it asks.
/// </summary>
[TestClass]
public sealed class CaptureCardDisplayTests
{
    [TestMethod]
    public async Task AScreenshotOfARegionOnTheSecondDisplayRaisesItsCardInThatDisplaysStackAndNoneAboveTheTray()
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;

        await displays.Screenshot(TwoDisplays.Second);

        var card = displays.OnSecond.Shown.Single();
        StringAssert.StartsWith(card.Text, "Screenshot stashed:");
        Assert.IsTrue(displays.OnSecond.OffersUndo(card));
        Assert.IsEmpty(listed.Notifications.Shown, "nothing above the tray");
        Assert.AreEqual(0, displays.Opened.Single().HeldAtOpening, "the stack is drawn before its first card shows");
    }

    [TestMethod]
    public async Task AScreenshotOfARegionOnThePrimaryRaisesItsCardAboveTheTrayAndOpensNoOtherStack()
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;

        await displays.Screenshot(RecordingScreen.Primary);

        StringAssert.StartsWith(listed.Notifications.Shown.Single().Text, "Screenshot stashed:");
        Assert.IsEmpty(displays.Opened, "no stack opened for the primary display");
        CollectionAssert.AreEqual(new[] { listed.Notifications }, listed.Stacks.All.ToList());
    }

    [TestMethod]
    public async Task ARecordingOfARegionOnTheSecondDisplayRaisesItsCardThere()
    {
        var displays = await TwoDisplays.OpenRecording();
        using var listed = displays.Listed;

        await displays.FinishRecording(await displays.RecordingWaitingOn(TwoDisplays.Second));

        StringAssert.StartsWith(displays.OnSecond.Shown.Single().Text, "Recording stashed:");
        Assert.IsEmpty(listed.Notifications.Shown, "nothing above the tray");
    }

    [TestMethod]
    public async Task AStashedClipboardImagesCardStandsAboveTheTrayWithTwoDisplays()
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;
        listed.Clipboard.ImageReading = Outcome<ClipboardImage?>.Of(new ClipboardImage(MediaFixtures.Bytes("transparent.png"), null));

        await listed.StashClipboardImage.Stash();

        StringAssert.StartsWith(listed.Notifications.Shown.Single().Text, "Clipboard image stashed:");
        Assert.IsEmpty(displays.Opened, "taken on no display");
    }

    [TestMethod]
    public async Task EachCaptureStandsOnItsOwnRegionsDisplayWhenOneWaitsWhileTheOtherIsTaken()
    {
        var displays = await TwoDisplays.OpenRecording();
        using var listed = displays.Listed;
        var recording = await displays.RecordingWaitingOn(TwoDisplays.Second);

        await displays.Screenshot(RecordingScreen.Primary);

        StringAssert.StartsWith(listed.Notifications.Shown.Single().Text, "Screenshot stashed:", "the screenshot's region was on the primary");
        Assert.IsEmpty(displays.Opened, "nothing on the second display yet");

        listed.Selection.Answer = TwoDisplays.RegionOn(RecordingScreen.Primary);
        await displays.FinishRecording(recording);

        StringAssert.StartsWith(displays.OnSecond.Shown.Single().Text, "Recording stashed:", "the recording's region was on the second, whatever was taken since");
        Assert.HasCount(1, listed.Notifications.Shown, "the screenshot's card alone above the tray");
    }

    [TestMethod]
    public async Task AScreenshotsFailuresStandAboveTheTrayWhateverDisplayItsRegionWasOn()
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;

        using (listed.Stash.HoldStore())
        {
            await displays.Screenshot(TwoDisplays.Second);
        }

        StringAssert.StartsWith(listed.Notifications.Shown.Single().Text, "Couldn't add to the stash:");
        Assert.IsEmpty(displays.Opened, "no stack opened for a failure");

        listed.Screen.Refuse(TwoDisplays.Second, new Failure("Grab the screen", "the display", "access is denied"));
        await displays.Screenshot(TwoDisplays.Second);

        Assert.AreEqual("Couldn't grab the screen (the display): access is denied.", listed.Notifications.Shown.Single().Text, "refused before any region, above the tray; it takes the earlier failure's place");
        Assert.IsEmpty(displays.Opened);
    }

    [TestMethod]
    public async Task AScreenshotsCopyFailureStandsAboveTheTrayAndItsCardOnTheSecondDisplay()
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;
        listed.Clipboard.FailNext = RecordingClipboard.Held;

        await displays.Screenshot(TwoDisplays.Second);

        StringAssert.StartsWith(listed.Notifications.Shown.Single().Text, "Couldn't copy");
        StringAssert.StartsWith(displays.OnSecond.Shown.Single().Text, "Screenshot stashed:");
    }

    [TestMethod]
    public async Task TheNextScreenshotsCardOnTheSecondDisplayTakesTheEarlierFailuresPlaceAboveTheTray()
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;
        using (listed.Stash.HoldStore())
        {
            await displays.Screenshot(RecordingScreen.Primary);
        }

        Assert.HasCount(1, listed.Notifications.Shown);

        await displays.Screenshot(TwoDisplays.Second);

        Assert.IsEmpty(listed.Notifications.Shown, "the failure has left (Q042 text 3: until the next notification of the same action)");
        StringAssert.StartsWith(displays.OnSecond.Shown.Single().Text, "Screenshot stashed:");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ACardOnTheSecondDisplayOpensTheWindowOnceWithTheTargetANotificationGives(bool inFront)
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;
        await displays.Screenshot(TwoDisplays.Second);
        var shot = (await listed.Stash.All()).Single();
        listed.Target.InFront = new ForegroundReading(true, null);
        listed.Target.Before = RecordingTarget.App("Window it gained focus from");
        listed.Target.BeforeHeld = RecordingTarget.App("Chat");
        var opened = new List<CaptureOpening>();
        listed.CaptureNotification.OpenRequested += opened.Add;

        OnControls(async () =>
        {
            var card = CardOf(displays.OnSecond);
            card.RaiseEvent(CaptureCardPresses.Body(card));
            await Handing.Eventually(() => opened.Count == 1);
        });

        // The press asks with the activation the card's window holds; the opening as it is asked for each path.
        var asked = opened.Single() with { NotificationInFront = inFront };
        opened.Clear();
        listed.CaptureNotification.Open(asked);
        await Handing.Eventually(() => listed.Listing.Selection.SequenceEqual([shot.Id]));

        Assert.HasCount(1, opened, "one opening for one click");
        Assert.AreEqual(RecordingTarget.App(inFront ? "Chat" : "Window it gained focus from"), listed.PasteTarget.Current, "G8: the window before the notification, or the one the window gained focus from");
        Assert.AreEqual(inFront ? 1 : 0, listed.Target.Steps.Count(step => step == "hold before mine"), "read once, only for a notification in front");
        Assert.IsEmpty(displays.OnSecond.Shown, "the card goes at its click");
    }

    [TestMethod]
    public async Task ANameTypedInACardOnTheSecondDisplayAndLeftByEditRenamesTheItem()
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;
        await displays.Screenshot(TwoDisplays.Second);
        var shot = (await listed.Stash.All()).Single();

        OnControls(async () =>
        {
            var card = CardOf(displays.OnSecond);
            card.Field.Edit();
            card.Field.GetLogicalDescendants().OfType<TextBox>().Single().Text = "cat facepalm";
            card.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Edit")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Handing.Eventually(() => listed.Editing.Session is not null);
        });

        Assert.AreEqual("cat facepalm", (await listed.Stash.All()).Single(item => item.Id == shot.Id).Name);
        Assert.AreEqual("cat facepalm", listed.Editing.Session!.Item.Name);
        listed.Editing.Cancel();
    }

    [TestMethod]
    public async Task ACardsOpeningNamesTheDisplayItStoodOnAsTheCardGoes()
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;
        await displays.Screenshot(TwoDisplays.Second);
        await displays.Screenshot(RecordingScreen.Primary);
        var opened = new List<CaptureOpening>();
        listed.CaptureNotification.OpenRequested += opened.Add;

        OnControls(async () =>
        {
            var onSecond = CardOf(displays.OnSecond);
            var aboveTray = CardOf(listed.Notifications);
            onSecond.RaiseEvent(CaptureCardPresses.Body(onSecond));
            aboveTray.RaiseEvent(CaptureCardPresses.Body(aboveTray));
            await Handing.Eventually(() => opened.Count == 2 && displays.OnSecond.Shown.Count == 0 && listed.Notifications.Shown.Count == 0);
        });

        Assert.AreEqual(TwoDisplays.Second, opened[0].StoodOn, "kept with the card, which has gone");
        Assert.IsNull(opened[1].StoodOn, "a card above the tray names none");
    }

    /// <summary>The card the notifications' window draws for the newest notice of <paramref name="stack"/>.</summary>
    private static CaptureCard CardOf(Notices stack)
    {
        var notice = stack.Shown.Last();
        return (CaptureCard)notice.Card!(notice, stack);
    }

    private static void OnControls(Func<Task> act) => ControlThread.Run(() => ControlThread.FinishOnDispatcher(act));
}
