using Avalonia;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// One run through the acts' and the cards' own entries on two displays, Also copy on, adding no rule of its own: a
/// refusal above the tray staying past its time (G16); two screenshots on the second display carded in its stack and
/// none above the tray (G16); the first card's click opening the flyout on that display, the other card placed again
/// clear of it (G1, G16); and the clipboard, read as a paste would get it, holding the second capture until its card's
/// Undo takes it off, the first still listed (G14, G16).
/// </summary>
[TestClass]
public sealed class TwoDisplayCaptureRunTests
{
    private static readonly PixelRect TrayIcon = new(1200, 752, 32, 48);

    [TestMethod]
    public async Task CardsOnTheSecondDisplayOpenTheFlyoutThereStandClearOfItAndTheSecondsUndoTakesItOffTheClipboard()
    {
        var displays = await TwoDisplays.Open();
        using var listed = displays.Listed;
        var placed = new PlacedNotifications(listed) { TrayIcon = TrayIcon };

        await listed.StashClipboardImage.Stash();
        var refusal = listed.Notifications.Shown.Single();
        StringAssert.StartsWith(refusal.Text, "Can't", "no image on the clipboard: refused above the tray (G13)");
        listed.Stash.Clock.Now += Notices.LastingAtMost + TimeSpan.FromSeconds(1);
        listed.Turns.Act(listed.Notifications.Sweep);
        Assert.AreSame(refusal, listed.Notifications.Shown.Single(), "a refusal stays past its time (G16)");

        await displays.Screenshot(TwoDisplays.Second);
        await displays.Screenshot(TwoDisplays.Second);
        var (first, second) = ((await listed.Stash.All()).MinBy(item => item.Id)!, (await listed.Stash.All()).MaxBy(item => item.Id)!);
        Assert.HasCount(2, displays.OnSecond.Shown, "both cards in the second display's stack");
        Assert.AreSame(refusal, listed.Notifications.Shown.Single(), "none above the tray but the refusal");

        OnControls(async () =>
        {
            // The first capture's card, as the notifications' window draws it.
            var olderNotice = displays.OnSecond.Shown[0];
            var older = (CaptureCard)olderNotice.Card!(olderNotice, displays.OnSecond);
            older.RaiseEvent(CaptureCardPresses.Body(older));
            await Handing.Eventually(() => placed.FlyoutAsked.Count == 1 && displays.OnSecond.Shown.Count == 1);
        });

        var flyout = placed.FlyoutAsked.Single();
        Assert.IsTrue(TwoDisplays.Second.WorkingArea.Contains(flyout), $"the flyout asked on the second display, at {flyout} (G1)");
        var stack = placed.PlaceOf(displays.OnSecond);
        Assert.IsFalse(stack.Intersects(flyout), $"the second card at {stack} placed again clear of the flyout at {flyout} (G16)");
        Assert.IsTrue(TwoDisplays.Second.WorkingArea.Contains(stack), stack.ToString());
        CollectionAssert.AreEqual(new[] { second.FilePath }, listed.Clipboard.Holds!.FilePaths.ToList(), "a paste would get the second capture (G14)");

        OnControls(async () =>
        {
            CaptureCardPresses.Click(CaptureCardPresses.Newest(displays.OnSecond), "Undo");
            await Handing.Eventually(() => !listed.CaptureNotification.Undoing);
        });

        Assert.IsNull(listed.Clipboard.Holds, "after the second card's Undo, a paste gets nothing (G16)");
        CollectionAssert.AreEqual(new[] { first.Id }, (await listed.Stash.All()).Select(item => item.Id).ToList(), "the second capture gone, the first still listed");
        Assert.IsEmpty(displays.OnSecond.Shown);
        Assert.AreSame(refusal, listed.Notifications.Shown.Single(), "the refusal still there at the run's end");
    }

    private static void OnControls(Func<Task> act) => ControlThread.Run(() => ControlThread.FinishOnDispatcher(act));
}
