using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The paste's order with its clipboard write held open, as a large picture's decode and encode hold it: the flyout hides
/// once the handoff is not refused and Enter is up, without waiting for the write; nothing is activated or sent until the
/// write has completed, when Enter, an edit and the target are asked again; a failed write sends nothing and is said
/// above the tray; the fallback waits for the copy; the use comes after the keystroke, and the next summon starts on
/// Recent unless the person summoned the window meanwhile; a second handoff waits (G8, G10, G1, N3, R9).
/// </summary>
[TestClass]
public sealed class HandoffPasteOrderTests
{
    [TestMethod]
    public async Task TheFlyoutHidesBeforeTheWriteCompletesAndTheTargetIsSentNothingUntilItHas()
    {
        using var handing = await Handing.Open("photo.jpg");
        var write = HoldTheWrite(handing);
        RecordTheWrite(handing, () => "write");
        var keystroke = new TaskCompletionSource();
        handing.Target.HoldPaste = keystroke.Task;

        var pasting = handing.Paste(handing.Items, RecordingTarget.Relay);
        await Handing.Eventually(() => handing.Clipboard.Begun == 1);

        CollectionAssert.AreEqual(new[] { "confirm Relay", "hide" }, handing.Steps, "hidden while the write runs; no activation, no keystroke before it completes (G8)");
        Assert.IsEmpty(handing.Clipboard.Written);
        Assert.IsEmpty(handing.Target.Pasted);
        Assert.IsFalse(pasting.IsCompleted);

        write.SetResult();
        await Handing.Eventually(() => handing.Steps.Contains("activate and paste Relay"));
        Assert.HasCount(1, handing.Clipboard.Written, "the write completed before the target was brought forward");
        Assert.IsEmpty(await Recent(handing), "no use while the keystroke is still being sent (State: Tags, favorite, use)");
        handing.Stash.Clock.Now += TimeSpan.FromSeconds(5);
        var keystrokeAt = handing.Stash.Clock.Now;
        keystroke.SetResult();
        var pasted = await pasting;

        Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { "confirm Relay", "hide", "write", "confirm Relay", "activate and paste Relay", "pasted Relay" }, handing.Steps, "one activation and one keystroke, after the write (G8, R6)");
        Assert.IsEmpty(handing.Notifications.Shown);
        Assert.IsEmpty(handing.Notices.Shown);
        var use = handing.Uses().Single();
        Assert.IsGreaterThanOrEqualTo(keystrokeAt.ToUnixTimeMilliseconds(), use.At, "the use's time is not before the keystroke's (G10)");
    }

    [TestMethod]
    public async Task WithEnterHeldTheFlyoutStaysAndLetGoItHidesAtOnceThoughTheWriteStillRuns()
    {
        var clock = new SteppedClock();
        using var handing = await Handing.Open(clock, "photo.jpg");
        var write = HoldTheWrite(handing);
        handing.Keys.Acting(Key.Enter, KeyAction.Handoff);

        var pasting = handing.Paste(handing.Items, RecordingTarget.Relay);
        await Handing.Eventually(() => clock.NextDue is not null && handing.Clipboard.Begun == 1);
        Assert.IsEmpty(handing.Steps, "Enter down: the flyout stays and nothing is brought forward, so its repeats reach no app as Send (N3)");

        clock.Pass(TimeSpan.FromMilliseconds(300));
        handing.Keys.Released(Key.Enter);
        await Handing.Eventually(() => handing.Steps.Contains("hide"));
        Assert.IsEmpty(handing.Clipboard.Written, "hidden at Enter's release, the write not done");
        Assert.IsEmpty(handing.Target.Pasted);

        write.SetResult();
        Assert.IsTrue((await pasting).Succeeded);
        Assert.AreEqual(RecordingTarget.Relay, handing.Target.Pasted.Single().Target);
    }

    [TestMethod]
    public async Task AnEditOpenedWhileEnterIsStillHeldKeepsTheFlyoutAndTheCopyIsSaidOnceWritten()
    {
        var clock = new SteppedClock();
        using var handing = await Handing.Open(clock, "photo.jpg");
        var write = HoldTheWrite(handing);
        RecordTheWrite(handing, () => $"write with {handing.Notifications.Shown.Count} notifications");
        var editOpen = false;
        handing.Keys.Acting(Key.Enter, KeyAction.Handoff);

        var pasting = handing.Handoff.Paste(null!, handing.Items, RecordingTarget.Relay, handing.Hide with { RefusalBeforePaste = () => editOpen ? PasteFallback.EditOpen : null });
        await Handing.Eventually(() => clock.NextDue is not null && handing.Clipboard.Begun == 1);
        editOpen = true;
        handing.Keys.Released(Key.Enter);
        write.SetResult();
        var pasted = await pasting;

        Assert.AreEqual(PasteFallback.EditOpen, pasted.Failure?.Reason);
        CollectionAssert.AreEqual(new[] { "write with 0 notifications" }, handing.Steps, "G2: the flyout \"stays ... while an edit is open\"; nothing confirmed, hidden or brought forward");
        Assert.AreEqual(PasteFallback.Copied, handing.Notifications.Shown.Single().Heading, "said once the copy is made (R9)");
        Assert.HasCount(1, handing.Uses());
    }

    [TestMethod]
    [DataRow("an item no longer in the stash")]
    [DataRow("Move copying")]
    [DataRow("an edit open")]
    public async Task ARefusalShowsInTheFlyoutWhichStays(string refusal)
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.ShowScope(Scope.All);
        var hidden = false;
        var hide = new PasteHide(() => hidden = true)
        {
            RefusalBeforePaste = () => listed.Editing.IsOpen ? PasteFallback.EditOpen : null,
        };
        switch (refusal)
        {
            case "an item no longer in the stash":
                Assert.IsTrue((await listed.Stash.Store!.Delete([cat.Id])).Single().Succeeded);
                break;
            case "Move copying":
                listed.Listing.WholeStashActAsked = "the stash is being moved";
                listed.Listing.WholeStashActBegun = true;
                break;
            case "an edit open":
                Assert.IsTrue((await listed.Editing.Open(cat, EditOrigin.Grid)).Succeeded);
                break;
        }

        var pasted = await listed.Handoff.Paste(null!, [cat], RecordingTarget.Relay, hide);

        Assert.IsFalse(pasted.Succeeded);
        Assert.IsFalse(hidden, "the flyout stays, so the refusal is seen (G8, G35, G2)");
        Assert.IsEmpty(listed.Target.Pasted);
        Assert.AreEqual(pasted.Failure!.Sentence, listed.Notices.Shown.Single(notice => notice.NamesAProblem).Text, "said inside the flyout");
        if (refusal == "an edit open")
        {
            Assert.AreEqual(PasteFallback.Copied, listed.Notifications.Shown.Single().Heading, "the copy made, then said (G8)");
            Assert.AreEqual(1L, listed.Uses(), "the copy is a use (G10)");
        }
        else
        {
            Assert.IsEmpty(listed.Clipboard.Written, "nothing written");
            Assert.IsEmpty(listed.Notifications.Shown);
            Assert.AreEqual(0L, listed.Uses(), "a refusal records nothing (G10)");
        }
    }

    [TestMethod]
    [DataRow("Enter pressed again and held")]
    [DataRow("Enter pressed again and let go")]
    [DataRow("Enter pressed again, then focus lost while it was held")]
    [DataRow("an edit opened")]
    [DataRow("the target closed")]
    public async Task WhatLetTheFlyoutHideIsAskedAgainOnceTheWriteHasCompleted(string meanwhile)
    {
        var clock = new SteppedClock();
        using var handing = await Handing.Open(clock, "photo.jpg");
        var write = HoldTheWrite(handing);
        var editOpen = false;
        var hide = handing.Hide with { RefusalBeforePaste = () => editOpen ? PasteFallback.EditOpen : null };

        var pasting = handing.Handoff.Paste(null!, handing.Items, RecordingTarget.Relay, hide);
        await Handing.Eventually(() => handing.Steps.Contains("hide") && handing.Clipboard.Begun == 1);
        switch (meanwhile)
        {
            case "Enter pressed again and held":
            case "Enter pressed again and let go":
                // The pinned window, or the flyout summoned again: Enter goes down on it while the write runs.
                handing.Keys.Acting(Key.Enter, KeyAction.Handoff);
                break;
            case "Enter pressed again, then focus lost while it was held":
                // The window lets go of every key as it loses focus: Enter's release is never seen there.
                handing.Keys.Acting(Key.Enter, KeyAction.Handoff);
                handing.Keys.Released();
                break;
            case "an edit opened":
                editOpen = true;
                break;
            case "the target closed":
                handing.Target.Confirms = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.Gone);
                break;
        }

        write.SetResult();
        if (meanwhile.StartsWith("Enter", StringComparison.Ordinal))
        {
            await Handing.Eventually(() => clock.NextDue is not null);
            Assert.IsEmpty(handing.Target.Pasted, "the target waits while Enter is down (N3)");
            if (meanwhile == "Enter pressed again and let go")
            {
                handing.Keys.Released(Key.Enter);
            }
            else
            {
                clock.Advance(Handoff.PasteBound);
            }
        }

        var pasted = await pasting;

        if (meanwhile == "Enter pressed again and let go")
        {
            Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
            Assert.HasCount(1, handing.Target.Pasted, "let go within the bound, the paste goes on, as today");
            Assert.IsEmpty(handing.Notifications.Shown);
        }
        else
        {
            var reason = meanwhile switch
            {
                "an edit opened" => PasteFallback.EditOpen,
                "the target closed" => PasteTargetReasons.Gone,
                _ => PasteFallback.EnterHeld,
            };
            Assert.AreEqual(reason, pasted.Failure?.Reason);
            Assert.DoesNotContain("activate and paste Relay", handing.Steps, "never an activation (G8, N3, G2)");
            Assert.AreEqual(PasteFallback.Copied, handing.Notifications.Shown.Single().Heading, "the fallback it has today");
        }

        Assert.HasCount(1, handing.Clipboard.Written);
        Assert.HasCount(1, handing.Uses(), "one use, the copy having completed (G10)");
    }

    [TestMethod]
    [DataRow("Enter on a menu item that took focus away")]
    [DataRow("Enter lost while an earlier paste ran")]
    public async Task APointerPasteAfterAnEnterWhoseReleaseWentUnseenIsPasted(string before)
    {
        var clock = new SteppedClock();
        using var handing = await Handing.Open(clock, "photo.jpg");
        if (before == "Enter on a menu item that took focus away")
        {
            // Reveal in File Explorer, Save a copy… or Open with…: another window takes focus while Enter is down.
            Assert.IsTrue(handing.Keys.PressedInMenu(Key.Enter));
            handing.Keys.Released();
        }
        else
        {
            var write = HoldTheWrite(handing);
            var earlier = handing.Paste(handing.Items, RecordingTarget.Relay);
            await Handing.Eventually(() => handing.Steps.Contains("hide"));
            handing.Keys.Acting(Key.Enter, KeyAction.Handoff);
            handing.Keys.Released();
            write.SetResult();
            await Handing.Eventually(() => clock.NextDue is not null);
            clock.Advance(Handoff.PasteBound);
            Assert.AreEqual(PasteFallback.EnterHeld, (await earlier).Failure?.Reason, "lost while that paste ran, it kept that one (N3)");
        }

        // Back in the window, a double-click on a tile: the pointer's paste, with no key held.
        var pasting = handing.Paste(handing.Items, RecordingTarget.Relay);
        await Handing.Eventually(() => clock.NextDue is not null || pasting.IsCompleted);
        Assert.IsTrue(pasting.IsCompleted, "nothing to wait for: no key the window saw is down, and no paste ran as one was lost");

        var pasted = await pasting;
        Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
        Assert.AreEqual(RecordingTarget.Relay, handing.Target.Pasted.Single().Target, "G8, G7: the paste with the pointer alone");
    }

    [TestMethod]
    public async Task AWriteThatFailsBehindTheHiddenFlyoutSendsNothingAndIsSaidAboveTheTrayAtOnce()
    {
        using var handing = await Handing.Open("photo.jpg");
        var write = HoldTheWrite(handing);
        handing.Clipboard.FailNext = RecordingClipboard.Held;

        var pasting = handing.Paste(handing.Items, RecordingTarget.Relay);
        await Handing.Eventually(() => handing.Steps.Contains("hide"));
        write.SetResult();
        var pasted = await pasting;

        Assert.AreEqual(HeldClipboard.Reason, pasted.Failure?.Reason);
        var said = handing.Notifications.Shown.Single();
        Assert.AreEqual(pasted.Failure!.Sentence, said.Text, "G8: \"the failure is named (G35)\", where the person sees it without a summon");
        Assert.AreNotEqual(PasteFallback.Copied, said.Heading, "nothing was copied");
        Assert.IsEmpty(handing.Notices.Shown);
        CollectionAssert.AreEqual(new[] { "confirm Relay", "hide" }, handing.Steps, "no activation, no keystroke (G8)");
        Assert.IsEmpty(handing.Uses(), "one that fails records nothing (G10)");
    }

    [TestMethod]
    public async Task AFallbackFoundWhileTheWriteRunsIsSaidOnlyOnceTheWriteHasCompleted()
    {
        using var handing = await Handing.Open("photo.jpg");
        var write = HoldTheWrite(handing);
        RecordTheWrite(handing, () => $"write with {handing.Notifications.Shown.Count} notifications");
        handing.Target.Confirms = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.Gone);

        var pasting = handing.Paste(handing.Items, RecordingTarget.Relay);
        await Handing.Eventually(() => handing.Steps.Contains("hide"));
        write.SetResult();
        await pasting;

        CollectionAssert.AreEqual(new[] { "confirm Relay", "hide", "write with 0 notifications" }, handing.Steps, "\"Copied · press Ctrl+V\" only with the clipboard already written (R9, technical G8)");
        Assert.AreEqual(PasteFallback.Copied, handing.Notifications.Shown.Single().Heading, "once");
        Assert.HasCount(1, handing.Clipboard.Written);
        Assert.HasCount(1, handing.Uses());
    }

    [TestMethod]
    public async Task ASummonTheInstantThePasteReturnsShowsRecentWithThePastedItemFirstAndSelected()
    {
        using var listed = await ListedStash.Open(SetClock.Ahead);
        var start = listed.Stash.Clock.Now;
        var older = await listed.Stash.ImportFixture("older", "still.png");
        Assert.IsTrue((await listed.Stash.Store!.RecordUses([older], start)).Succeeded);
        var item = await listed.Stash.ImportFixture("pasted", "photo.jpg");
        await listed.Summon();
        await listed.Listing.SetQuery("pas");
        listed.PasteTarget.FromPress(new ForegroundReading(false, RecordingTarget.Relay));
        listed.Stash.Clock.Now = start.AddSeconds(1);

        Assert.IsTrue((await listed.PasteIntoTarget(listed.Listing.SelectedItems)).Succeeded);
        await listed.Summon();

        Assert.AreEqual(Scope.Recent, listed.Listing.Scope, "G1: Recent after a paste");
        Assert.AreEqual("", listed.Listing.Query, "the search empty");
        CollectionAssert.AreEqual(new[] { item.Id, older.Id }, listed.Listing.Items.Select(shown => shown.Id).ToList(), "the pasted item first, its use recorded before Recent was read (G4)");
        CollectionAssert.AreEqual(new[] { item.Id }, listed.Listing.Selection.ToList(), "it alone selected");
    }

    [TestMethod]
    public async Task ASummonWhileThePasteRunsKeepsWhatThePersonDidInIt()
    {
        using var handing = await Handing.Open();
        var cat = await handing.Stash.ImportFixture("cat", "photo.jpg");
        var dog = await handing.Stash.ImportFixture("dog", "still.png");
        await handing.Listing.ShowScope(Scope.All);
        handing.Listing.Focus(cat.Id);
        var write = HoldTheWrite(handing);

        var pasting = handing.Paste([cat], RecordingTarget.Relay);
        await Handing.Eventually(() => handing.Steps.Contains("hide"));
        // The person summons the window and types while the paste runs on behind it.
        handing.TimesShown++;
        await handing.Listing.SetQuery("do");
        handing.Listing.Focus(dog.Id);
        write.SetResult();
        Assert.IsTrue((await pasting).Succeeded);

        Assert.AreEqual("do", handing.Listing.Query, "what the person typed is theirs (G1)");
        Assert.AreEqual(Scope.All, handing.Listing.LitChip, "no Recent put over it");
        CollectionAssert.AreEqual(new[] { dog.Id }, handing.Listing.Selection.ToList(), "nor the paste's selection");
        Assert.HasCount(1, handing.Uses(), "the paste's use recorded all the same (G10)");
    }

    [TestMethod]
    public async Task ASecondHandoffStartedBehindTheHiddenFlyoutWaitsAndEachTargetGetsItsOwnPicture()
    {
        using var handing = await Handing.Open("photo.jpg", "transparent.png");
        var (first, second) = (handing.Items[0], handing.Items[1]);
        var other = RecordingTarget.App("Other");
        var write = HoldTheWrite(handing);
        RecordTheWrite(handing, () => $"write after {handing.Target.Pasted.Count} keystrokes");
        var keystroke = new TaskCompletionSource();
        handing.Target.HoldPaste = keystroke.Task;

        var pastingFirst = handing.Paste([first], RecordingTarget.Relay);
        await Handing.Eventually(() => handing.Steps.Contains("hide"));
        var pastingSecond = handing.Paste([second], other);
        handing.Clipboard.Hold = null;
        write.SetResult();
        await Handing.Eventually(() => handing.Steps.Contains("activate and paste Relay"));
        handing.Target.HoldPaste = null;
        keystroke.SetResult();
        await Task.WhenAll(pastingFirst, pastingSecond);

        CollectionAssert.AreEqual(new[] { first.FilePath }, handing.Clipboard.Written[0].Content.FilePaths.ToList());
        CollectionAssert.AreEqual(new[] { second.FilePath }, handing.Clipboard.Written[1].Content.FilePaths.ToList());
        CollectionAssert.AreEqual(new[] { RecordingTarget.Relay, other }, handing.Target.Pasted.Select(paste => paste.Target).ToList());
        CollectionAssert.AreEqual(new[]
            {
                "confirm Relay", "hide", "write after 0 keystrokes", "confirm Relay", "activate and paste Relay", "pasted Relay",
                "confirm Other", "hide", "write after 1 keystrokes", "confirm Other", "activate and paste Other", "pasted Other",
            }, handing.Steps, "the second handoff waits for the first's keystroke (G8)");
    }

    [TestMethod]
    public async Task TheSearchBoxsTextCopyWaitsForThePastesKeystroke()
    {
        using var handing = await Handing.Open("photo.jpg");
        var write = HoldTheWrite(handing);
        handing.Clipboard.AtWriteText = () => handing.Steps.Add($"text after {handing.Target.Pasted.Count} keystrokes");

        var pasting = handing.Paste(handing.Items, RecordingTarget.Relay);
        await Handing.Eventually(() => handing.Steps.Contains("hide"));
        // The flyout summoned again, words selected in its search box and Ctrl+C pressed while the paste runs behind it.
        var copying = handing.Handoff.CopyText(null!, SearchBoxClipboard.CopyAction, "shrug");
        write.SetResult();
        await Task.WhenAll(pasting, copying);

        CollectionAssert.AreEqual(new[] { "confirm Relay", "hide", "confirm Relay", "activate and paste Relay", "pasted Relay", "text after 1 keystrokes" }, handing.Steps,
            "the target pastes the picture its Enter was for, never the search box's words (G8, G7)");
        CollectionAssert.AreEqual(new[] { "shrug" }, handing.Clipboard.TextsWritten);
    }

    /// <summary>Adds what <paramref name="step"/> says to the paste's steps as each clipboard write completes.</summary>
    private static void RecordTheWrite(Handing handing, Func<string> step) => handing.Clipboard.AtWrite = () =>
    {
        handing.Steps.Add(step());
        return Task.CompletedTask;
    };

    /// <summary>Holds the next clipboard write open until the returned source is set, as a large picture's decode and encode would.</summary>
    private static TaskCompletionSource HoldTheWrite(Handing handing)
    {
        var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handing.Clipboard.Hold = write.Task;
        return write;
    }

    private static async Task<IReadOnlyList<StashItem>> Recent(Handing handing) => (await handing.Stash.Store!.Recent()).Value!;
}
