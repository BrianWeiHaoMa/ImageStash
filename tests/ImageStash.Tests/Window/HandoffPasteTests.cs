using Avalonia.Input;
using ImageStash.Capture;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using Microsoft.Data.Sqlite;

namespace ImageStash.Tests.Window;

/// <summary>
/// The handoff's paste over a real stash with real pictures: the hide, the write, the target asked again, the keystroke
/// and then the use (<c>HandoffPasteOrderTests</c> holds the write open), its fallbacks with the notification and no second
/// write or use, a clipboard that cannot be written pasting nothing, and the one-at-a-time gate held through the keystroke
/// (G8, G10, G16); and Enter's handover, a paste with a target and a copy without (G8),
/// waiting while the Enter that asked for it is held before any other app is given the front, and past the paste's
/// bound giving nothing the front at all (N3).
/// </summary>
[TestClass]
public sealed class HandoffPasteTests
{
    [TestMethod]
    public async Task APasteConfirmsHidesConfirmsAgainThenBringsTheTargetForwardRecordsTheUseAfterAndSaysNothing()
    {
        using var handing = await Handing.Open("transparent.png");
        var item = handing.Items.Single();
        handing.Target.AtConfirm = () => handing.Steps.Add($"uses {handing.Stash.Store!.Recent().Result.Value!.Count}");

        var pasted = await handing.Paste([item], RecordingTarget.Relay);

        Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { "confirm Relay", "uses 0", "hide", "confirm Relay", "uses 0", "activate and paste Relay", "pasted Relay" }, handing.Steps,
            "G8 \"hides the flyout, activates that window and sends it a paste keystroke\": the target confirmed before the hide and again before it is brought forward; State: \"a use is written after the clipboard write ... never before\", here after the keystroke");
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
    [DataRow(PasteTargetReasons.Gone, $"Couldn't paste (Relay): {PasteTargetReasons.Gone}.")]
    [DataRow(PasteTargetReasons.HigherRights, "Couldn't paste: Relay runs with higher rights than ImageStash.")]
    [DataRow(PasteTargetReasons.RightsUnreadable, "Couldn't paste: Relay's rights could not be read.")]
    public async Task ATargetThatCannotTakeThePasteGetsTheCopyTheHideAndThenTheNotification(string reason, string expectedSentence)
    {
        using var handing = await Handing.Open("animated.gif");
        handing.Clipboard.AtWrite = () =>
        {
            handing.Steps.Add("write");
            return Task.CompletedTask;
        };
        handing.Target.AtConfirm = () => handing.Steps.Add($"uses {handing.Stash.Store!.Recent().Result.Value!.Count}");
        handing.Target.Confirms = new Failure(PasteTargetReasons.Action, "Relay", reason);

        var pasted = await handing.Handoff.Paste(null!, handing.Items, RecordingTarget.Relay, new PasteHide(() =>
        {
            handing.Steps.Add($"hide with {handing.Notifications.Shown.Count} notifications");
            return true;
        }));

        Assert.AreEqual(expectedSentence, pasted.Failure?.Sentence, "named in the outcome (G35)");
        CollectionAssert.AreEqual(new[] { "write", "confirm Relay", "uses 0", "hide with 0 notifications" }, handing.Steps,
            "an animated item's write completes at once here; then the hide, before the notification, so the app is in front for its Ctrl+V, the use after the copy, and no activation or keystroke (G8, G2, G10, N3)");
        Assert.AreEqual("Copied · press Ctrl+V", handing.Notifications.Shown.Single().Heading, "G8's words");
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

        Assert.AreEqual("Couldn't paste: Relay could not be brought to the front.", pasted.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { "confirm Relay", "hide", "confirm Relay", "activate and paste Relay", "pasted Relay" }, handing.Steps);
        Assert.AreEqual(PasteFallback.Copied, handing.Notifications.Shown.Single().Heading);
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

        Assert.AreEqual($"Couldn't copy (transparent): {HeldClipboard.Reason}.", pasted.Failure?.Sentence);
        var named = handing.Notifications.Shown.Single();
        Assert.AreEqual(pasted.Failure!.Sentence, named.Text, "G8: \"When the clipboard cannot be written, nothing is pasted and the failure is named (G35)\": above the tray, the flyout having hidden");
        Assert.IsNull(named.Heading, "not \"Copied · press Ctrl+V\": nothing was copied");
        Assert.IsEmpty(handing.Notices.Shown, "not inside the hidden window");
        CollectionAssert.AreEqual(new[] { "confirm Relay", "hide" }, handing.Steps, "no second confirmation, no activation, no keystroke");
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
        CollectionAssert.AreEqual(new[] { "write True", "confirm Relay", "hide", "confirm Relay", "activate and paste Relay", "pasted Relay", "write False" }, handing.Steps,
            "the target pastes the picture the Enter was for (G8)");
    }

    [TestMethod]
    public async Task HandingOverWithoutATargetCopiesAndNeitherConfirmsNorHides()
    {
        using var handing = await Handing.Open("photo.jpg");

        var handed = await handing.Handoff.HandOver(null!, handing.Items, null, new PasteHide(() =>
        {
            handing.Steps.Add("hide");
            return true;
        }));

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

        await handing.Handoff.HandOver(null!, handing.Items, RecordingTarget.Relay, new PasteHide(() =>
        {
            handing.Steps.Add("hide");
            return true;
        }));

        Assert.AreEqual(RecordingTarget.Relay, handing.Target.Pasted.Single().Target);
    }

    [TestMethod]
    public async Task APasteThatLeavesTheFlyoutHiddenLeavesRecentWithNoSearchAndThePastedItemFirstAndSelectedAlone()
    {
        using var handing = await Handing.Open();
        var (myCat, _, bore) = await Searched(handing);
        var cleared = 0;
        handing.Listing.QueryChangedByWindow += () => cleared++;

        var pasted = await handing.Paste([myCat], RecordingTarget.Relay);

        Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
        Assert.AreEqual(Scope.Recent, handing.Listing.Scope, "G1: Recent after a paste");
        Assert.AreEqual(Scope.Recent, handing.Listing.LitChip);
        Assert.AreEqual("", handing.Listing.Query, "no search");
        Assert.AreEqual(1, cleared, "the search box emptied with it");
        CollectionAssert.AreEqual(new[] { myCat.Id, bore.Id }, handing.Listing.Items.Select(item => item.Id).ToList(),
            "the item just handed over first, its use recorded before Recent was read (G4, G10)");
        CollectionAssert.AreEqual(new[] { myCat.Id }, handing.Listing.Selection.ToList(), "it alone selected");
        Assert.AreEqual(myCat.Id, handing.Listing.Focused);
    }

    [TestMethod]
    public async Task AScreenshotTakenAfterAPasteHidTheFlyoutIsFirstInRecentAtTheNextSummonAndEnterHandsOverThePastedItemStillSelected()
    {
        using var listed = await ListedStash.Open(SetClock.Ahead);
        var start = listed.Stash.Clock.Now;
        var older = await listed.Stash.ImportFixture("older", "still.png");
        Assert.IsTrue((await listed.Stash.Store!.RecordUses([older], start)).Succeeded);
        var pastedItem = await listed.Stash.ImportFixture("pasted", "transparent.png");
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.Focus(pastedItem.Id);
        listed.PasteTarget.FromPress(new ForegroundReading(false, RecordingTarget.Relay));
        listed.Stash.Clock.Now = start.AddSeconds(1);
        var pasted = await listed.Handoff.HandOver(null!, listed.Listing.SelectedItems, listed.PasteTarget.Current, new PasteHide(() => true, listed.Keys));
        Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
        listed.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, new Avalonia.PixelRect(100, 50, 30, 20));
        listed.Stash.Clock.Now = start.AddSeconds(2);

        await listed.Screenshot.Take();
        await listed.Summon();

        var shot = (await listed.Stash.All()).MaxBy(item => item.Id)!;
        Assert.AreEqual(Scope.Recent, listed.Listing.Scope, "the paste's start on Recent kept");
        CollectionAssert.AreEqual(new[] { shot.Id, pastedItem.Id, older.Id }, listed.Listing.Items.Select(item => item.Id).ToList(),
            "G4: the screenshot, never used, by when it was taken, then the pasted item and the older one by their uses");
        CollectionAssert.AreEqual(new[] { pastedItem.Id }, listed.Listing.Selection.ToList(), "G1: the selection the paste left, the capture not selected");

        var enter = listed.Press(Key.Enter, KeyModifiers.None, KeyPlace.SearchBox);
        listed.Keys.Released(Key.Enter);
        Assert.AreEqual(KeyAction.Handoff, await enter);
        CollectionAssert.AreEqual(new[] { pastedItem.FilePath }, listed.Clipboard.Written[^1].Content.FilePaths.ToList(), "Enter hands over the pasted item");
        Assert.HasCount(2, listed.Target.Pasted);
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task ThreeItemsPastedTogetherLeaveOneOfThemSelectedRecentsFirst()
    {
        using var handing = await Handing.Open();
        var (myCat, myDog, bore) = await Searched(handing);
        await handing.Listing.ShowScope(Scope.All);
        handing.Listing.SelectAll();
        var three = handing.Listing.SelectedItems;
        Assert.HasCount(3, three);

        await handing.Paste(three, RecordingTarget.Relay);

        Assert.AreEqual(Scope.Recent, handing.Listing.Scope);
        var selected = handing.Listing.Selection.Single();
        Assert.AreEqual(handing.Listing.Items[0].Id, selected, "Recent's first item, alone (G1)");
        CollectionAssert.Contains(new[] { myCat.Id, myDog.Id, bore.Id }, selected, "one of the three handed over");
    }

    [TestMethod]
    public async Task APasteWhoseTargetCannotTakeItLeavesTheHiddenFlyoutOnRecentToo()
    {
        using var handing = await Handing.Open();
        var (myCat, _, _) = await Searched(handing);
        handing.Target.Confirms = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.Gone);

        await handing.Paste([myCat], RecordingTarget.Relay);

        Assert.AreEqual(PasteFallback.Copied, handing.Notifications.Shown.Single().Heading);
        Assert.AreEqual(Scope.Recent, handing.Listing.Scope, "the Copied · press Ctrl+V path hides the flyout as a paste does (G8, G1)");
        Assert.AreEqual("", handing.Listing.Query);
        CollectionAssert.AreEqual(new[] { myCat.Id }, handing.Listing.Selection.ToList());
    }

    [TestMethod]
    public async Task AWindowThatStaysAfterPasteKeepsItsRecentOrderAndRecordsTheUse()
    {
        using var handing = await Handing.Open("photo.jpg", "animated.gif");
        var (first, second) = (handing.Items[0], handing.Items[1]);
        var store = handing.Stash.Store!;
        await store.RecordUses([first], handing.Stash.Clock.Now);
        handing.Stash.Clock.Now += TimeSpan.FromSeconds(1);
        await store.RecordUses([second], handing.Stash.Clock.Now);
        await handing.Listing.ShowScope(Scope.Recent);
        handing.Listing.Focus(first.Id);
        handing.PasteHidesFlyout = false;
        handing.Stash.Clock.Now += TimeSpan.FromSeconds(1);

        Assert.IsTrue((await handing.Paste(handing.Listing.SelectedItems, RecordingTarget.Relay)).Succeeded);

        CollectionAssert.AreEqual(new[] { second.Id, first.Id }, handing.Listing.Items.Select(item => item.Id).ToArray());
        CollectionAssert.AreEqual(new[] { first.Id }, handing.Listing.Selection.ToArray());
        Assert.AreEqual(first.Id, (await store.Recent()).Value![0].Id);
    }

    [TestMethod]
    public async Task AUseTheStoreRefusesLeavesTheHandoffStandingAndTheHiddenFlyoutOnRecent()
    {
        using var handing = await Handing.Open();
        var (myCat, _, bore) = await Searched(handing);

        Outcome pasted;
        // Its writes refused while it is still read, as while another program holds a write lock on it (E3).
        using (var holder = new SqliteConnection($"Data Source={handing.Stash.Folder.StoreFile};Pooling=False"))
        {
            holder.Open();
            using var begin = holder.CreateCommand();
            begin.CommandText = "BEGIN IMMEDIATE";
            begin.ExecuteNonQuery();
            pasted = await handing.Paste([myCat], RecordingTarget.Relay);
        }

        Assert.IsTrue(pasted.Succeeded, "the handoff stands (G10)");
        Assert.AreEqual(RecordingTarget.Relay, handing.Target.Pasted.Single().Target);
        Assert.HasCount(1, handing.Notices.Shown, "the use's failure named (G35)");
        Assert.AreEqual(Scope.Recent, handing.Listing.Scope, "Recent all the same");
        Assert.AreEqual("", handing.Listing.Query);
        CollectionAssert.AreEqual(new[] { bore.Id }, handing.Listing.Items.Select(item => item.Id).ToList(), "Recent lists only what it has recorded");
        CollectionAssert.AreEqual(new[] { bore.Id }, handing.Listing.Selection.ToList(), "its first item selected alone, as after any paste");
    }

    [TestMethod]
    [DataRow("copy without a target")]
    [DataRow("Ctrl+C")]
    [DataRow("a paste the window stayed up through")]
    [DataRow("a clipboard that cannot be written")]
    public async Task OnlyAPasteThatLeavesTheFlyoutHiddenStartsTheNextSummonOnRecent(string act)
    {
        using var handing = await Handing.Open();
        var (myCat, myDog, _) = await Searched(handing);
        handing.Listing.Toggle(myDog.Id);
        CollectionAssert.AreEquivalent(new[] { myCat.Id, myDog.Id }, handing.Listing.Selection.ToList());

        switch (act)
        {
            case "copy without a target":
                await handing.Handoff.HandOver(null!, [myCat], null, new PasteHide(() => throw new AssertFailedException("a copy hides nothing (G8)")));
                break;
            case "Ctrl+C":
                await handing.Copy([myCat]);
                break;
            case "a paste the window stayed up through":
                // Pinned, or with a dialog or a drag of its own up: the paste hides nothing (G2).
                handing.PasteHidesFlyout = false;
                await handing.Paste([myCat], RecordingTarget.Relay);
                Assert.AreEqual(RecordingTarget.Relay, handing.Target.Pasted.Single().Target);
                break;
            case "a clipboard that cannot be written":
                handing.Clipboard.FailNext = RecordingClipboard.Held;
                await handing.Paste([myCat], RecordingTarget.Relay);
                break;
        }

        Assert.AreEqual("my", handing.Listing.Query, "the search as it was left (G1)");
        Assert.AreEqual(Scope.All, handing.Listing.LitChip);
        CollectionAssert.AreEquivalent(new[] { myCat.Id, myDog.Id }, handing.Listing.Selection.ToList(), "and the selection");
    }

    [TestMethod]
    [DataRow("a flyout")]
    [DataRow("a window that stays")]
    [DataRow("a target that cannot take it")]
    public async Task WithEnterHeldNothingHidesAndNoTargetComesForwardUntilEnterIsLetGo(string into)
    {
        var clock = new SteppedClock();
        using var handing = await Handing.Open(clock);
        var (myCat, _, _) = await Searched(handing);
        handing.Clipboard.AtWrite = () =>
        {
            handing.Steps.Add("write");
            return Task.CompletedTask;
        };
        handing.PasteHidesFlyout = into != "a window that stays";
        if (into == "a target that cannot take it")
        {
            handing.Target.Confirms = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.Gone);
        }

        Assert.AreEqual(KeyAction.Handoff, handing.Keys.Acting(Key.Enter, KeyAction.Handoff));
        var pasting = handing.Paste([myCat], RecordingTarget.Relay);
        await Handing.Eventually(() => clock.NextDue is not null && handing.Steps.Contains("write"));

        Assert.AreEqual(Handoff.PasteBound, clock.NextDue, "waiting within the paste's bound");
        CollectionAssert.AreEqual(new[] { "write" }, handing.Steps, "while Enter is down the write goes on, but the flyout stays and the target is not brought forward, so its repeats reach no app as Send (N3)");
        Assert.IsFalse(pasting.IsCompleted);
        clock.Pass(TimeSpan.FromMilliseconds(400));
        handing.Keys.Released(Key.Enter);
        await pasting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsNull(clock.NextDue, "the bound's wait let go");
        var after = into == "a target that cannot take it"
            ? new[] { "write", "confirm Relay", "hide" }
            : new[] { "write", "confirm Relay", "hide", "confirm Relay", "activate and paste Relay", "pasted Relay" };
        CollectionAssert.AreEqual(after, handing.Steps, "once Enter is let go: the hide, then the one paste keystroke (G8, R6)");
        Assert.HasCount(into == "a target that cannot take it" ? 0 : 1, handing.Target.Pasted);
        Assert.HasCount(into == "a target that cannot take it" ? 1 : 0, handing.Notifications.Shown);
        if (into == "a window that stays")
        {
            Assert.AreEqual("my", handing.Listing.Query, "the window stayed up: nothing starts anew (G1)");
            Assert.AreEqual(Scope.All, handing.Listing.LitChip);
            CollectionAssert.AreEqual(new[] { myCat.Id }, handing.Listing.Selection.ToList(), "and the selection as it was");
        }
        else
        {
            Assert.AreEqual(Scope.Recent, handing.Listing.Scope, "the flyout hidden: the next summon starts on Recent (G1)");
            CollectionAssert.AreEqual(new[] { myCat.Id }, handing.Listing.Selection.ToList());
        }

        Assert.HasCount(1, handing.Uses().Where(use => use.Item == myCat.Id), "one use (G10)");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EnterHeldPastThePastesBoundHidesNothingBringsNothingForwardAndSaysCopied(bool focusLeftMeanwhile)
    {
        var clock = new SteppedClock();
        using var handing = await Handing.Open(clock);
        var (myCat, myDog, _) = await Searched(handing);
        handing.Listing.Toggle(myDog.Id);
        handing.Keys.Acting(Key.Enter, KeyAction.Handoff);

        var pasting = handing.Paste([myCat], RecordingTarget.Relay);
        await Handing.Eventually(() => clock.NextDue is not null);
        if (focusLeftMeanwhile)
        {
            // The window lets go of every key as it loses focus, Enter's release unseen.
            handing.Keys.Released();
            clock.Pass(TimeSpan.FromMilliseconds(500));
            await Task.Delay(100);
            Assert.IsEmpty(handing.Steps, "an unseen release is no release: nothing comes forward while Enter may still be down (N3)");
        }

        clock.Advance(Handoff.PasteBound);
        var pasted = await pasting;

        Assert.AreEqual($"Couldn't paste (Relay): {PasteFallback.EnterHeld}.", pasted.Failure?.Sentence);
        Assert.IsEmpty(handing.Steps, "no hide, no activation, no keystroke: the target receives nothing of the press (N3, R6)");
        Assert.IsEmpty(handing.Target.Pasted);
        Assert.AreEqual(PasteFallback.Copied, handing.Notifications.Shown.Single().Heading, "it says where the picture is (G8)");
        Assert.IsEmpty(handing.Notices.Shown);
        CollectionAssert.AreEqual(new[] { myCat.FilePath }, handing.Clipboard.Written.Single().Content.FilePaths.ToList(), "the picture is on the clipboard");
        Assert.AreEqual("my", handing.Listing.Query, "the flyout still shows as it was: no start on Recent (G1)");
        Assert.AreEqual(Scope.All, handing.Listing.LitChip);
        CollectionAssert.AreEquivalent(new[] { myCat.Id, myDog.Id }, handing.Listing.Selection.ToList());
        Assert.IsNull(handing.Handoff.Busy);
        Assert.HasCount(1, handing.Uses().Where(use => use.Item == myCat.Id), "the copy completed, so its use stands (G10)");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeferredKeyboardPasteRequiresItsOwnReleaseAndNoCurrentlyHeldEnter(bool releaseLostWithFocus)
    {
        var clock = new SteppedClock();
        using var handing = await Handing.Open(clock);
        var (cat, _, _) = await Searched(handing);
        handing.Keys.Acting(Key.Enter, KeyAction.Handoff);
        var hide = new PasteHide(() => false, handing.Keys).ForEnterPress();
        handing.Keys.Released(releaseLostWithFocus ? null : Key.Enter);
        handing.Keys.Acting(Key.Enter, KeyAction.Handoff);
        if (releaseLostWithFocus)
        {
            handing.Keys.Released(Key.Enter);
        }

        var pasting = handing.Handoff.Paste(null!, [cat], RecordingTarget.Relay, hide);
        await Handing.Eventually(() => clock.NextDue is not null || pasting.IsCompleted);
        Assert.IsFalse(pasting.IsCompleted, "the release at issue is still missing");
        Assert.IsEmpty(handing.Target.Pasted);
        if (releaseLostWithFocus)
        {
            clock.Advance(Handoff.PasteBound);
            Assert.AreEqual(PasteFallback.EnterHeld, (await pasting).Failure?.Reason);
            Assert.IsEmpty(handing.Target.Pasted);
        }
        else
        {
            handing.Keys.Released(Key.Enter);
            Assert.IsTrue((await pasting).Succeeded);
            Assert.AreEqual(RecordingTarget.Relay, handing.Target.Pasted.Single().Target);
        }

        CollectionAssert.AreEqual(new[] { cat.FilePath }, handing.Clipboard.Written.Single().Content.FilePaths.ToArray());
    }

    [TestMethod]
    public async Task OpeningAnEditorDuringTheClipboardWriteLeavesTheHandoffAsACopy()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        var dog = await listed.Stash.ImportFixture("dog", "transparent.png");
        await listed.Listing.ShowScope(Scope.All);
        var clipboard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listed.Clipboard.Hold = clipboard.Task;
        var hidden = false;
        var hide = new PasteHide(() => hidden = true)
        {
            RefusalBeforePaste = () => listed.Editing.IsOpen ? PasteFallback.EditOpen : null,
        };

        var paste = listed.Handoff.Paste(null!, [cat], RecordingTarget.Relay, hide);
        try
        {
            // The flyout hides at once, the write still running; an edit then opens, as from a notification's Edit (G16).
            await Handing.Eventually(() => hidden);
            Assert.IsTrue((await listed.Editing.Open(dog, EditOrigin.Grid)).Succeeded);
        }
        finally
        {
            clipboard.SetResult();
        }

        Assert.AreEqual("an edit is open", (await paste).Failure?.Reason);
        Assert.AreEqual("Can't paste (Relay): an edit is open.", (await paste).Failure?.Sentence, "declined while an edit is open, nothing gone wrong (G35)");
        Assert.AreEqual(dog.Id, listed.Editing.Session?.Item.Id);
        Assert.IsEmpty(listed.Target.Pasted);
        Assert.AreEqual(1L, listed.Uses());
        CollectionAssert.AreEqual(new[] { cat.FilePath }, listed.Clipboard.Written.Single().Content.FilePaths.ToArray());
    }

    [TestMethod]
    public async Task WithNoEnterDownAPasteWaitsForNothing()
    {
        var clock = new SteppedClock();
        using var handing = await Handing.Open(clock, "photo.jpg");
        handing.Keys.Acting(Key.Enter, KeyAction.Handoff);
        handing.Keys.Released(Key.Enter);

        // As a double-click or the item menu's entry hands over, with Enter up.
        var pasted = await handing.Paste(handing.Items, RecordingTarget.Relay);

        Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { "confirm Relay", "hide", "confirm Relay", "activate and paste Relay", "pasted Relay" }, handing.Steps);
        Assert.IsNull(clock.NextDue, "no wait was armed");
    }

    /// <summary>
    /// "my cat" and "my dog" never used, and "bore" used a day ago, searched for <c>my</c>, with "my cat", the second
    /// match, selected; the listing reads the stash afresh first, the three having been added beside it.
    /// </summary>
    private static async Task<(StashItem MyCat, StashItem MyDog, StashItem Bore)> Searched(Handing handing)
    {
        var bore = await handing.Stash.ImportFixture("bore", "still.png");
        var myCat = await handing.Stash.ImportFixture("my cat", "photo.jpg");
        var myDog = await handing.Stash.ImportFixture("my dog", "transparent.png");
        Assert.IsTrue((await handing.Stash.Store!.RecordUses([bore], handing.Stash.Clock.Now.AddDays(-1))).Succeeded);
        await handing.Listing.SetQuery("my");
        CollectionAssert.AreEqual(new[] { myDog.Id, myCat.Id }, handing.Listing.Items.Select(item => item.Id).ToList());
        handing.Listing.Focus(myCat.Id);
        return (myCat, myDog, bore);
    }
}
