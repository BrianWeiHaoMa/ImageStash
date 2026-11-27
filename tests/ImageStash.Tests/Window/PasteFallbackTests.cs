using Avalonia;
using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G8's notification for a paste that could not be made: it leads with "Copied · press Ctrl+V", names the target as the
/// footer does and says in one sentence why, never that anything arrived (G10), only once the clipboard is written, once
/// per paste; and it stands above the tray, unless the stash window stood on another display when the handoff was asked,
/// where it stands at that display's bottom-right working-area corner, the tray's notifications staying where they are (G16).
/// </summary>
[TestClass]
public sealed class PasteFallbackTests
{
    private static readonly Display Primary = new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1, IsPrimary: true);
    private static readonly Display Right = new(new PixelRect(1920, 0, 2560, 1440), new PixelRect(1920, 0, 2560, 1392), 1.5, IsPrimary: false);
    private static readonly Display Left = new(new PixelRect(-1920, 0, 1920, 1080), new PixelRect(-1920, 0, 1920, 1040), 1, IsPrimary: false);
    private static readonly Display Above = new(new PixelRect(0, -1200, 1920, 1200), new PixelRect(0, -1200, 1920, 1152), 1.25, IsPrimary: false);
    private static readonly WindowSize Card = new(300, 80);

    /// <summary>Every reason a paste can fall back for, by how the paste meets it.</summary>
    public static IEnumerable<object[]> Reasons =>
    [
        ["confirm", PasteTargetReasons.Gone],
        ["confirm", PasteTargetReasons.HigherRights],
        ["confirm", PasteTargetReasons.RightsUnreadable],
        ["paste", PasteTargetReasons.NotBroughtToFront],
        ["paste", PasteTargetReasons.KeystrokeRefused],
        ["paste", PasteTargetReasons.KeyHeld],
        ["edit", PasteFallback.EditOpen],
        ["enter", PasteFallback.EnterHeld],
    ];

    [TestMethod]
    [DynamicData(nameof(Reasons))]
    public async Task EachReasonLeadsWithTheQuotedWordsThenNamesTheAppAndSaysWhyInOneSentence(string meets, string reason)
    {
        var (handing, pasted) = await PasteFallingBack(meets, reason, RecordingTarget.App("Slack"));
        using var _ = handing;

        var notice = handing.Notifications.Shown.Single();
        Assert.AreEqual("Copied · press Ctrl+V", notice.Heading, "the quoted words lead (G8, In use, Done when 5)");
        StringAssert.Contains(notice.Text, "Slack", "it names the target as the footer does (G8)");
        Assert.EndsWith(".", notice.Text, "one sentence that ends as a sentence");
        Assert.AreEqual(1, notice.Text.Count(c => c == '.'), $"one sentence: {notice.Text}");
        Assert.AreNotEqual(reason, notice.Text, "a person's words, not the seam's");
        StringAssert.DoesNotMatch(notice.Text, new System.Text.RegularExpressions.Regex(@"\b(pasted|sent|delivered|arrived)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            "nothing states or implies delivery, or that nothing was pasted (G10)");
        // An edit keeps the window up, which names the refusal too (G35).
        Assert.HasCount(meets == "edit" ? 1 : 0, handing.Notices.Shown, "a notification, not a notice inside the window");
        Assert.HasCount(1, handing.Clipboard.Written, "one copy");
        Assert.HasCount(1, handing.Uses(), "one use (G10)");
        Assert.AreEqual(reason, pasted.Failure?.Reason, "the outcome still carries the paste's own reason");
    }

    [TestMethod]
    public async Task EveryReasonSaysSomethingOfItsOwn()
    {
        var said = new List<string>();
        foreach (var reason in Reasons)
        {
            var (handing, _) = await PasteFallingBack((string)reason[0], (string)reason[1], RecordingTarget.Relay);
            using var _ = handing;
            said.Add(handing.Notifications.Shown.Single().Text);
        }

        Assert.HasCount(said.Count, said.Distinct().ToList(), string.Join(Environment.NewLine, said));
    }

    [TestMethod]
    public async Task RightsThatCouldNotBeReadAreNeverCalledHigherAndAboveIsCalledAdministrator()
    {
        var (unreadable, _) = await PasteFallingBack("confirm", PasteTargetReasons.RightsUnreadable, RecordingTarget.Relay);
        var (above, _) = await PasteFallingBack("confirm", PasteTargetReasons.HigherRights, RecordingTarget.Relay);
        using var a = unreadable;
        using var b = above;

        var unreadableWords = unreadable.Notifications.Shown.Single().Text;
        Assert.DoesNotContain("runs as administrator", unreadableWords, "a window ImageStash may not query may be another account's (E6)");
        Assert.DoesNotContain("higher rights", unreadableWords);
        StringAssert.Contains(above.Notifications.Shown.Single().Text, "runs as administrator");
    }

    [TestMethod]
    public async Task AGoneWindowIsNotToldToTakeCtrlVButOneThatIsLeftIs()
    {
        var (gone, _) = await PasteFallingBack("confirm", PasteTargetReasons.Gone, RecordingTarget.Relay);
        using var _ = gone;

        Assert.DoesNotContain("Ctrl+V", gone.Notifications.Shown.Single().Text, "no window is left to press it in");
        foreach (var reason in Reasons.Where(r => (string)r[1] != PasteTargetReasons.Gone))
        {
            var (left, _) = await PasteFallingBack((string)reason[0], (string)reason[1], RecordingTarget.Relay);
            using var __ = left;
            StringAssert.Contains(left.Notifications.Shown.Single().Text, "press Ctrl+V there", (string)reason[1]);
        }
    }

    [TestMethod]
    public async Task AWindowWhoseAppCannotBeReadIsNamedByTheFootersWordsForIt()
    {
        var (handing, _) = await PasteFallingBack("confirm", PasteTargetReasons.RightsUnreadable, RecordingTarget.App("the app you came from"));
        using var _ = handing;

        StringAssert.Contains(handing.Notifications.Shown.Single().Text, "the app you came from", "named as the footer names it (G8, E6)");
    }

    [TestMethod]
    public async Task AClipboardThatCannotBeWrittenRaisesNoCopiedAnywhereAndIsNamedWhereTheWindowStood()
    {
        using var handing = await Handing.Open("photo.jpg");
        handing.Clipboard.FailNext = RecordingClipboard.Held;
        handing.Target.Confirms = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.Gone);
        var opened = Opened(handing);

        var pasted = await handing.Handoff.Paste(null!, handing.Items, RecordingTarget.Relay, StandingOn(Right, handing));

        Assert.IsFalse(pasted.Succeeded);
        Assert.IsEmpty(handing.Notifications.Shown, "nothing above the tray: the flyout stood on another display");
        var named = opened.Single().Stack.Shown.Single();
        Assert.IsNull(named.Heading, "\"Copied\" only once the clipboard is written (technical G8, R9)");
        Assert.AreEqual(pasted.Failure!.Sentence, named.Text, "G8: \"the failure is named (G35)\", where the person was looking, the flyout having hidden");
        Assert.IsEmpty(handing.Notices.Shown);
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    public async Task OnAnotherDisplayTheNotificationStandsThereAndTheTraysStayAboveTheTray()
    {
        using var handing = await Handing.Open("photo.jpg");
        var capture = handing.Notifications.Raise("Screenshot 2026-11-26 15.00.00 is in the stash", "Undo", () => Task.FromResult(Outcome.Done));
        handing.Target.Confirms = new Failure(PasteTargetReasons.Action, "Slack", PasteTargetReasons.HigherRights);
        var opened = Opened(handing);

        await handing.Handoff.Paste(null!, handing.Items, RecordingTarget.App("Slack"), StandingOn(Right, handing));

        var (stack, display) = opened.Single();
        var elsewhere = stack.Shown.Single();
        Assert.AreEqual("Copied · press Ctrl+V", elsewhere.Heading);
        StringAssert.Contains(elsewhere.Text, "Slack");
        Assert.AreEqual(Right, display, "on the display the stash window stood on (G16)");
        CollectionAssert.AreEqual(new[] { capture }, handing.Notifications.Shown.ToList(), "the capture's stays above the tray");
        Assert.IsTrue(handing.Notifications.OffersUndo(capture), "with its Undo");
    }

    [TestMethod]
    public async Task OnThePrimaryDisplayOrWithNoDisplayKnownItJoinsTheNotificationsAboveTheTray()
    {
        foreach (var standsOn in new[] { Primary, null })
        {
            using var handing = await Handing.Open("photo.jpg");
            handing.Target.Confirms = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.Gone);
            var opened = Opened(handing);

            await handing.Handoff.Paste(null!, handing.Items, RecordingTarget.Relay, StandingOn(standsOn, handing));

            Assert.HasCount(1, handing.Notifications.Shown, $"{standsOn}");
            Assert.IsEmpty(opened);
        }
    }

    [TestMethod]
    public async Task AnEnterPressKeepsWhereTheStashWindowStoodAsTheHandoffWasAsked()
    {
        using var handing = await Handing.Open("photo.jpg");
        handing.Target.Pastes = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.NotBroughtToFront);
        var opened = Opened(handing);

        var pasting = handing.Handoff.Paste(null!, handing.Items, RecordingTarget.Relay, StandingOn(Right, handing).ForEnterPress());
        handing.Keys.Released(Key.Enter);
        await pasting;

        CollectionAssert.AreEqual(new[] { "confirm Relay", "hide", "confirm Relay", "activate and paste Relay", "pasted Relay" }, handing.Steps,
            "the flyout hid before the notification, which still stands where the window stood (G16); the target asked again once the write completed (G8)");
        Assert.AreEqual(Right, opened.Single().Display);
        Assert.HasCount(1, opened.Single().Stack.Shown);
        Assert.IsEmpty(handing.Notifications.Shown);
    }

    [TestMethod]
    public async Task EachOtherDisplayHasItsOwnStackSoALaterOneMovesNoneAlreadyShowing()
    {
        using var handing = await Handing.Open("photo.jpg");
        handing.Target.Confirms = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.Gone);
        var opened = Opened(handing);

        await handing.Handoff.Paste(null!, handing.Items, RecordingTarget.Relay, StandingOn(Right, handing));
        await handing.Handoff.Paste(null!, handing.Items, RecordingTarget.Relay, StandingOn(Left, handing));
        await handing.Handoff.Paste(null!, handing.Items, RecordingTarget.Relay, StandingOn(Right, handing));

        CollectionAssert.AreEqual(new[] { Right, Left }, opened.Select(stack => stack.Display).ToList(), "one stack per display, each standing where it was opened (G16)");
        Assert.HasCount(2, opened[0].Stack.Shown, "the third joins the first on its own display");
        Assert.HasCount(1, opened[1].Stack.Shown);
        Assert.IsEmpty(handing.Notifications.Shown);
    }

    [TestMethod]
    public async Task AFallbackElsewhereLastsAsItsWordsNeedLongerWhileHoveredAndLeavesOnItsX()
    {
        using var handing = await Handing.Open("photo.jpg");
        handing.Target.Confirms = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.Gone);
        var opened = Opened(handing);
        await handing.Handoff.Paste(null!, handing.Items, RecordingTarget.Relay, StandingOn(Right, handing));
        var elsewhere = opened.Single().Stack;
        var notice = elsewhere.Shown.Single();
        var lasting = Notices.LastingFor($"{notice.Heading} {notice.Text}");

        elsewhere.Hover(notice, over: true);
        handing.Stash.Clock.Now += TimeSpan.FromSeconds(30);
        elsewhere.Sweep();
        Assert.HasCount(1, elsewhere.Shown, "longer while hovered (G16)");
        elsewhere.Hover(notice, over: false);
        handing.Stash.Clock.Now += lasting - TimeSpan.FromMilliseconds(100);
        elsewhere.Sweep();
        Assert.HasCount(1, elsewhere.Shown);
        handing.Stash.Clock.Now += TimeSpan.FromMilliseconds(200);
        elsewhere.Sweep();
        Assert.IsEmpty(elsewhere.Shown, "as long as its words need (G16)");

        await handing.Handoff.Paste(null!, handing.Items, RecordingTarget.Relay, StandingOn(Right, handing));
        elsewhere.Close(elsewhere.Shown.Single());
        Assert.IsEmpty(elsewhere.Shown, "its ×");
    }

    [TestMethod]
    public void TheStashWindowStandsOnTheDisplayHoldingMostOfIt()
    {
        Display[] displays = [Primary, Right, Left, Above];

        Assert.AreEqual(Primary, Summon.StandsOn(new PixelRect(100, 100, 600, 500), displays));
        Assert.AreEqual(Right, Summon.StandsOn(new PixelRect(2500, 300, 900, 750), displays));
        Assert.AreEqual(Left, Summon.StandsOn(new PixelRect(-1000, 200, 600, 500), displays));
        Assert.AreEqual(Above, Summon.StandsOn(new PixelRect(400, -700, 600, 500), displays));
        Assert.AreEqual(Right, Summon.StandsOn(new PixelRect(1700, 100, 600, 500), displays), "a pinned window across an edge: where most of it is");
        Assert.AreEqual(Primary, Summon.StandsOn(new PixelRect(1500, 100, 600, 500), displays));
    }

    [TestMethod]
    public void OnTheNonPrimaryDisplayTheNotificationStandsAtItsWorkingAreasBottomRightCornerSizedByItsScaling()
    {
        Display[] displays = [Primary, Right, Left, Above];
        var icon = new PixelRect(1800, 1032, 32, 48);

        var right = Summon.NotificationPlace(icon, displays, Card, on: Right);
        var left = Summon.NotificationPlace(icon, displays, Card, on: Left);
        var above = Summon.NotificationPlace(icon, displays, Card, on: Above);

        Assert.AreEqual(new PixelRect(1920 + 2560 - 18 - 450, 1392 - 18 - 120, 450, 120), right, "sized and spaced by its 150 %");
        Assert.AreEqual(new PixelRect(-12 - 300, 1040 - 12 - 80, 300, 80), left);
        Assert.AreEqual(new PixelRect(1920 - 15 - 375, -1200 + 1152 - 15 - 100, 375, 100), above);
        foreach (var (place, display) in new[] { (right, Right), (left, Left), (above, Above) })
        {
            Assert.IsTrue(display.WorkingArea.Contains(place), $"wholly inside {display}: {place}");
        }
    }

    [TestMethod]
    public void TheOtherDisplayIsReadAsItIsNowWhenItsTaskbarOrScalingChangedSinceThePaste()
    {
        var now = Right with { WorkingArea = new PixelRect(1920, 0, 2560, 1300), Scaling = 2 };

        var place = Summon.NotificationPlace(null, [Primary, now], Card, on: Right);

        Assert.AreEqual(new PixelRect(1920 + 2560 - 24 - 600, 1300 - 24 - 160, 600, 160), place);
    }

    [TestMethod]
    public void OnThePrimaryDisplayTheNotificationStandsAboveTheTrayAsEveryNotificationDoes()
    {
        Display[] displays = [Primary, Right];
        var icon = new PixelRect(1800, 1032, 32, 48);

        Assert.AreEqual(Summon.NotificationPlace(icon, displays, Card), Summon.NotificationPlace(icon, displays, Card, on: Primary));
        Assert.AreEqual(Summon.NotificationPlace(null, displays, Card), Summon.NotificationPlace(null, displays, Card, on: Primary));
    }

    [TestMethod]
    public void ANotificationLargerThanTheOtherDisplaysWorkingAreaStaysInsideIt()
    {
        var small = new Display(new PixelRect(1920, 0, 400, 300), new PixelRect(1920, 0, 400, 260), 2, IsPrimary: false);

        var place = Summon.NotificationPlace(null, [Primary, small], Card, on: small);

        Assert.IsTrue(small.WorkingArea.Contains(place), place.ToString());
    }

    /// <summary>A paste that falls back for <paramref name="reason"/>, met where <paramref name="meets"/> says, the stash window on the primary display.</summary>
    private static async Task<(Handing, Outcome)> PasteFallingBack(string meets, string reason, PasteTarget target)
    {
        var clock = new SteppedClock();
        var handing = await Handing.Open(meets == "enter" ? clock : null, "photo.jpg");
        var failure = new Failure(PasteTargetReasons.Action, target.AppName, reason);
        var hide = StandingOn(Primary, handing);
        switch (meets)
        {
            case "confirm":
                handing.Target.Confirms = failure;
                break;
            case "paste":
                handing.Target.Pastes = failure;
                break;
            case "edit":
                hide = hide with { RefusalBeforePaste = () => PasteFallback.EditOpen };
                break;
        }

        if (meets != "enter")
        {
            return (handing, await handing.Handoff.Paste(null!, handing.Items, target, hide));
        }

        handing.Keys.Acting(Key.Enter, KeyAction.Handoff);
        var pasting = handing.Handoff.Paste(null!, handing.Items, target, hide.ForEnterPress());
        await Handing.Eventually(() => clock.NextDue is not null);
        clock.Advance(Handoff.PasteBound);
        return (handing, await pasting);
    }

    /// <summary>The stacks the fallback opens on displays other than the primary, as the app draws each in a window of its own.</summary>
    private static List<(Notices Stack, Display Display)> Opened(Handing handing)
    {
        var opened = new List<(Notices, Display)>();
        handing.Handoff.Fallback.Opened += (stack, display) => opened.Add((stack, display));
        return opened;
    }

    private static PasteHide StandingOn(Display? display, Handing handing) => new(() =>
    {
        handing.Steps.Add("hide");
        return handing.PasteHidesFlyout;
    }, handing.Keys)
    {
        StoodOn = display,
    };
}
