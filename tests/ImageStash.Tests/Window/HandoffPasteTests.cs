using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The handoff's paste over a real stash with real pictures: in How G8 holds's order, its fallbacks with the
/// notification and no second write or use, a clipboard that cannot be written pasting nothing, and the one-at-a-time
/// gate held through the keystroke (G8, G10, G16); and Enter's handover, a paste with a target and a copy without (G8).
/// </summary>
[TestClass]
public sealed class HandoffPasteTests
{
    [TestMethod]
    public async Task APasteWritesRecordsTheUseConfirmsHidesThenBringsTheTargetForwardAndSaysNothing()
    {
        using var handing = await Handing.Open("transparent.png");
        var item = handing.Items.Single();
        handing.Clipboard.AtWrite = () =>
        {
            handing.Steps.Add("write");
            return Task.CompletedTask;
        };
        handing.Target.AtConfirm = () => handing.Steps.Add($"uses {handing.Stash.Store!.Recent().Result.Value!.Count}");

        var pasted = await handing.Paste([item], RecordingTarget.Relay);

        Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { "write", "confirm Relay", "uses 1", "hide", "activate and paste Relay", "pasted Relay" }, handing.Steps,
            "How G8 holds: the write, the use, the confirmation, the hide, then the target brought forward and sent the keystroke");
        Assert.AreEqual((RecordingTarget.Relay, Handoff.PasteBound), handing.Target.Pasted.Single());
        var content = handing.Clipboard.Written.Single().Content;
        CollectionAssert.AreEqual(new[] { item.FilePath }, content.FilePaths.ToList());
        Assert.IsNotNull(content.Png, "the same content a copy writes (G8)");
        Assert.IsEmpty(handing.Notices.Shown, "a paste says nothing (G10)");
        Assert.IsEmpty(handing.Notifications.Shown);
        Assert.IsNull(handing.Handoff.Busy);
        Assert.HasCount(1, handing.Uses(), "one use (G10)");
    }

    [TestMethod]
    [DataRow(PasteTargetReasons.Gone)]
    [DataRow(PasteTargetReasons.HigherRights)]
    [DataRow(PasteTargetReasons.RightsUnreadable)]
    public async Task ATargetThatCannotTakeThePasteGetsTheCopyTheHideAndThenTheNotification(string reason)
    {
        using var handing = await Handing.Open("animated.gif");
        handing.Clipboard.AtWrite = () =>
        {
            handing.Steps.Add("write");
            return Task.CompletedTask;
        };
        handing.Target.AtConfirm = () => handing.Steps.Add($"uses {handing.Uses().Count}");
        handing.Target.Confirms = new Failure(PasteTargetReasons.Action, "Relay", reason);

        var pasted = await handing.Handoff.Paste(null!, handing.Items, RecordingTarget.Relay, () => handing.Steps.Add($"hide with {handing.Notifications.Shown.Count} notifications"));

        Assert.AreEqual($"Paste failed: Relay, {reason}", pasted.Failure?.Sentence, "named in the outcome (G35)");
        CollectionAssert.AreEqual(new[] { "write", "confirm Relay", "uses 1", "hide with 0 notifications" }, handing.Steps,
            "the copy and the use first, then the hide, before the notification, so the app is in front for its Ctrl+V, and no activation or keystroke (G8, G2, N3)");
        Assert.AreEqual("Copied · press Ctrl+V", handing.Notifications.Shown.Single().Text, "G8's words");
        Assert.IsEmpty(handing.Notices.Shown, "the notification, not a notice inside the window");
        Assert.HasCount(1, handing.Clipboard.Written, "the copy is made once");
        Assert.HasCount(1, handing.Uses(), "and its use recorded once (G10)");
    }

    [TestMethod]
    public async Task ATargetThatDoesNotComeToTheFrontGetsTheCopyAndTheNotificationWithTheFlyoutHidden()
    {
        using var handing = await Handing.Open("photo.jpg");
        handing.Target.Pastes = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.NotBroughtToFront);

        var pasted = await handing.Paste(handing.Items, RecordingTarget.Relay);

        Assert.AreEqual($"Paste failed: Relay, {PasteTargetReasons.NotBroughtToFront}", pasted.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { "confirm Relay", "hide", "activate and paste Relay", "pasted Relay" }, handing.Steps);
        Assert.AreEqual(Handoff.CopiedNotification, handing.Notifications.Shown.Single().Text);
        Assert.IsEmpty(handing.Notices.Shown);
        Assert.HasCount(1, handing.Clipboard.Written);
        Assert.HasCount(1, handing.Uses());
    }

    [TestMethod]
    public async Task AClipboardThatCannotBeWrittenPastesNothingAndNamesTheFailure()
    {
        using var handing = await Handing.Open("transparent.png");
        handing.Clipboard.FailNext = RecordingClipboard.Held;

        var pasted = await handing.Paste(handing.Items, RecordingTarget.Relay);

        Assert.AreEqual($"Copy failed: transparent, {HeldClipboard.Reason}", pasted.Failure?.Sentence);
        Assert.AreEqual(pasted.Failure!.Sentence, handing.Notices.Shown.Single().Text, "named as a copy's failure is (G8, G35)");
        Assert.IsEmpty(handing.Steps, "no confirmation, no hide, no keystroke");
        Assert.IsEmpty(handing.Notifications.Shown);
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    public async Task ACopyStartedWhileAPasteWaitsForItsKeystrokeWritesOnlyAfterTheKeystroke()
    {
        using var handing = await Handing.Open("animated.gif", "animated.webp");
        var (first, second) = (handing.Items[0], handing.Items[1]);
        handing.Clipboard.AtWrite = () =>
        {
            handing.Steps.Add($"write {handing.Clipboard.Written[^1].Content.FilePaths.Single() == first.FilePath}");
            return Task.CompletedTask;
        };
        var keystroke = new TaskCompletionSource();
        handing.Target.HoldPaste = keystroke.Task;

        var pasting = handing.Paste([first], RecordingTarget.Relay);
        await Handing.Eventually(() => handing.Steps.Contains("activate and paste Relay"));
        var copying = handing.Copy([second]);
        await Task.Delay(100);

        Assert.AreEqual(1, handing.Clipboard.Begun, "the copy waits for the paste's keystroke");
        keystroke.SetResult();
        await Task.WhenAll(pasting, copying);
        CollectionAssert.AreEqual(new[] { "write True", "confirm Relay", "hide", "activate and paste Relay", "pasted Relay", "write False" }, handing.Steps,
            "the target pastes the picture the Enter was for (G8)");
    }

    [TestMethod]
    public async Task HandingOverWithoutATargetCopiesAndNeitherConfirmsNorHides()
    {
        using var handing = await Handing.Open("photo.jpg");

        var handed = await handing.Handoff.HandOver(null!, handing.Items, null, () => handing.Steps.Add("hide"));

        Assert.IsTrue(handed.Succeeded);
        Assert.IsEmpty(handing.Steps, "Enter copies and the window stays (G8, In use)");
        Assert.HasCount(1, handing.Clipboard.Written);
        Assert.IsEmpty(handing.Notifications.Shown);
        Assert.HasCount(1, handing.Uses());
    }

    [TestMethod]
    public async Task HandingOverWithATargetPastesIntoIt()
    {
        using var handing = await Handing.Open("photo.jpg");

        await handing.Handoff.HandOver(null!, handing.Items, RecordingTarget.Relay, () => handing.Steps.Add("hide"));

        Assert.AreEqual(RecordingTarget.Relay, handing.Target.Pasted.Single().Target);
    }
}
