using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G33's and G34's policy for F5's four shortcuts, the open shortcut, Take a screenshot's, Record a clip's and Stash
/// clipboard image's, as ImageStash binds them: each registered at start from the settings' text, in that order; one
/// that cannot be registered, or that the seam releases, named in the tooltip with the reason and never registered
/// again in the run, Pause included, while the others keep working; Pause releasing all four and its toggle
/// registering them again; the open shortcut acting at the press, the other three acts on the UI thread; a text that
/// is not a shortcut named, not thrown, while the tray still reaches every act (G31, G35).
/// </summary>
[TestClass]
public sealed class GlobalShortcutsTests
{
    private const int Record = 2;
    private const int Stash = 3;
    private const int Pause = 4;
    private const int Settings = 5;
    private const int Quit = 6;
    private static readonly Shortcut Open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
    private static readonly Shortcut TakeScreenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;
    private static readonly Shortcut RecordClip = Shortcut.Parse("Ctrl+Shift+PrtScn").Value!;
    private static readonly Shortcut StashClipboardImage = Shortcut.Parse("Ctrl+Alt+V").Value!;

    [TestMethod]
    public void AtStartAllFourShortcutsAreRegisteredInOrderAndTheMenuShowsEachAndPauseUnchecked()
    {
        var started = StartedShortcuts.Start();

        Assert.AreEqual(new KeyGesture(Key.Space, KeyModifiers.Control | KeyModifiers.Alt), started.Shortcuts.ShortcutOf(ShortcutAct.Open)!.Gesture);
        Assert.AreEqual("Ctrl+Alt+Space", started.Shortcuts.ShortcutOf(ShortcutAct.Open)!.Text);
        Assert.AreEqual(new KeyGesture(Key.PrintScreen, KeyModifiers.Control), started.Shortcuts.ShortcutOf(ShortcutAct.Screenshot)!.Gesture);
        Assert.AreEqual("Ctrl+PrtScn", started.Shortcuts.ShortcutOf(ShortcutAct.Screenshot)!.Text);
        Assert.AreEqual(new KeyGesture(Key.PrintScreen, KeyModifiers.Control | KeyModifiers.Shift), started.Shortcuts.ShortcutOf(ShortcutAct.Record)!.Gesture);
        Assert.AreEqual("Ctrl+Shift+PrtScn", started.Shortcuts.ShortcutOf(ShortcutAct.Record)!.Text);
        Assert.AreEqual(new KeyGesture(Key.V, KeyModifiers.Control | KeyModifiers.Alt), started.Shortcuts.ShortcutOf(ShortcutAct.StashClipboardImage)!.Gesture);
        CollectionAssert.AreEqual(new[] { Open, TakeScreenshot, RecordClip, StashClipboardImage }, started.Seam.Registered.ToList(), "registered in the bindings' order (F5)");
        Assert.AreEqual(4, started.Seam.Registrations);
        CollectionAssert.AreEqual(new[] { "Open ImageStash", "Take a screenshot", "Record a clip", "Stash clipboard image", "Pause shortcuts", "Settings", "Quit ImageStash" }, started.Tray.Entries.Select(e => e.Header).ToList());
        Assert.AreEqual(Open.Gesture, started.Tray.Entries[0].Gesture);
        Assert.AreEqual(TakeScreenshot.Gesture, started.Tray.Entries[1].Gesture, "each entry with its shortcut shown (G34)");
        Assert.AreEqual(RecordClip.Gesture, started.Tray.Entries[Record].Gesture);
        Assert.AreEqual(StashClipboardImage.Gesture, started.Tray.Entries[Stash].Gesture);
        Assert.IsNull(started.Tray.Entries[0].Checked, "Open ImageStash is a plain entry");
        Assert.IsNull(started.Tray.Entries[1].Checked);
        Assert.IsNull(started.Tray.Entries[Record].Checked);
        Assert.IsNull(started.Tray.Entries[Stash].Checked);
        Assert.IsFalse(started.Tray.Entries[Pause].Checked!.Value, "Pause shortcuts is a toggle, not checked");
        Assert.IsNull(started.Tray.Entries[Settings].Checked, "Settings is a plain entry");
        Assert.IsNull(started.Tray.Entries[Settings].Gesture, "with no shortcut of its own (G34)");
        Assert.IsNull(started.Tray.Entries[Quit].Checked);
        Assert.IsNull(started.Tray.Entries[Quit].Gesture);
        Assert.AreEqual("ImageStash", started.Tray.ToolTip);

        Assert.IsTrue(started.Seam.Press(Open));
        Assert.IsTrue(started.Seam.Press(TakeScreenshot));
        Assert.IsTrue(started.Seam.Press(RecordClip));
        Assert.IsTrue(started.Seam.Press(StashClipboardImage));
        started.RunPosted();
        Assert.AreEqual((1, 1, 1, 1), (started.Summoned, started.Screenshots, started.Recorded, started.Stashed), "each press runs its own act once");
        Assert.AreEqual((0, 0, 0, 0), (started.Opened, started.ScreenshotsFromTray, started.RecordedFromTray, started.StashedFromTray), "and no entry's");
    }

    [TestMethod]
    public void TheOpenShortcutActsAtThePressAndTheOthersActsArePostedToTheUiThread()
    {
        var started = StartedShortcuts.Start();

        Assert.IsTrue(started.Seam.Press(TakeScreenshot));

        Assert.AreEqual(0, started.Screenshots, "the press returns on the seam's thread before the act runs (How G33 holds)");
        Assert.HasCount(1, started.Posted);
        started.RunPosted();
        Assert.AreEqual(1, started.Screenshots, "the act, once, on the UI thread");

        Assert.IsTrue(started.Seam.Press(RecordClip));

        Assert.AreEqual(0, started.Recorded, "the press returns on the seam's thread before the act runs");
        Assert.HasCount(1, started.Posted);
        started.RunPosted();
        Assert.AreEqual(1, started.Recorded, "the act, once, on the UI thread");

        Assert.IsTrue(started.Seam.Press(StashClipboardImage));

        Assert.AreEqual(0, started.Stashed, "the press returns on the seam's thread before the act runs");
        Assert.HasCount(1, started.Posted);
        started.RunPosted();
        Assert.AreEqual(1, started.Stashed, "the act, once, on the UI thread");

        Assert.IsTrue(started.Seam.Press(Open));

        Assert.AreEqual(1, started.Summoned, "the open shortcut reads the window in front at the press itself (G1, G8)");
        Assert.IsEmpty(started.Posted);
    }

    [TestMethod]
    public void AShortcutAnotherProgramHoldsIsNamedAndNeverRegisteredAgainWhileTheTrayStillOpensTheWindow()
    {
        var started = StartedShortcuts.Start(held: Open);

        Assert.AreEqual("Register the open shortcut failed: Ctrl+Alt+Space, another program holds it", started.Tray.ToolTip);
        Assert.AreEqual(Open.Gesture, started.Tray.Entries[0].Gesture, "the menu still shows the shortcut");
        CollectionAssert.AreEquivalent(new[] { TakeScreenshot, RecordClip, StashClipboardImage }, started.Seam.Registered.ToList(), "the other shortcuts are registered (G33: every other function working)");
        started.Tray.Entries[0].Chosen();
        Assert.AreEqual(1, started.Opened, "Open ImageStash still summons");
        Assert.IsTrue(started.Seam.Press(StashClipboardImage));
        started.RunPosted();
        Assert.AreEqual(1, started.Stashed);

        started.Tray.Entries[Pause].Chosen();
        started.Tray.Entries[Pause].Chosen();

        Assert.AreEqual(7, started.Seam.Registrations, "the toggle back registers the other three alone; neither Pause nor its toggle tries the held one again (G33: until it is changed in Settings)");
        CollectionAssert.AreEquivalent(new[] { TakeScreenshot, RecordClip, StashClipboardImage }, started.Seam.Registered.ToList());
        Assert.IsFalse(started.Seam.Press(Open));
        Assert.IsTrue(started.Seam.Press(StashClipboardImage));
    }

    [TestMethod]
    public void TheScreenshotShortcutHeldByAnotherProgramIsNamedWhileTheOtherThreeAndTheEntryWork()
    {
        var started = StartedShortcuts.Start(held: TakeScreenshot);

        Assert.AreEqual("Register the screenshot shortcut failed: Ctrl+PrtScn, another program holds it", started.Tray.ToolTip, "which shortcut is unavailable and why (G33)");
        CollectionAssert.AreEquivalent(new[] { Open, RecordClip, StashClipboardImage }, started.Seam.Registered.ToList());
        Assert.AreEqual(TakeScreenshot.Gesture, started.Tray.Entries[1].Gesture, "the entry still shows its shortcut (G34)");
        Assert.IsTrue(started.Seam.Press(Open));
        Assert.IsTrue(started.Seam.Press(RecordClip));
        Assert.IsTrue(started.Seam.Press(StashClipboardImage));
        started.RunPosted();
        Assert.AreEqual((1, 1, 1), (started.Summoned, started.Recorded, started.Stashed), "the other three fire");
        started.Tray.Entries[1].Chosen();
        Assert.AreEqual(1, started.ScreenshotsFromTray, "the entry still runs the act");
        Assert.AreEqual(0, started.Screenshots, "and the shortcut never did");

        started.Tray.Entries[Pause].Chosen();
        started.Tray.Entries[Pause].Chosen();

        Assert.AreEqual(7, started.Seam.Registrations, "only the other three are registered again");
        CollectionAssert.AreEquivalent(new[] { Open, RecordClip, StashClipboardImage }, started.Seam.Registered.ToList());
        Assert.AreEqual("Register the screenshot shortcut failed: Ctrl+PrtScn, another program holds it", started.Tray.ToolTip, "named once");
    }

    [TestMethod]
    public void TheRecordShortcutHeldByAnotherProgramIsNamedWhileTheOtherThreeFireAndTheEntryStillRecords()
    {
        var started = StartedShortcuts.Start(held: RecordClip);

        Assert.AreEqual("Register the record shortcut failed: Ctrl+Shift+PrtScn, another program holds it", started.Tray.ToolTip, "which shortcut is unavailable and why (G33)");
        CollectionAssert.AreEquivalent(new[] { Open, TakeScreenshot, StashClipboardImage }, started.Seam.Registered.ToList());
        Assert.AreEqual(RecordClip.Gesture, started.Tray.Entries[Record].Gesture, "the entry still shows its shortcut (G34)");
        Assert.IsTrue(started.Seam.Press(Open));
        Assert.IsTrue(started.Seam.Press(TakeScreenshot));
        Assert.IsTrue(started.Seam.Press(StashClipboardImage));
        Assert.IsFalse(started.Seam.Press(RecordClip), "the held combination is not ImageStash's");
        started.RunPosted();
        Assert.AreEqual((1, 1, 1), (started.Summoned, started.Screenshots, started.Stashed), "the other three fire");
        started.Tray.Entries[Record].Chosen();
        Assert.AreEqual(1, started.RecordedFromTray, "the entry still runs the act");
        Assert.AreEqual(0, started.Recorded, "and the shortcut never did");

        started.Tray.Entries[Pause].Chosen();
        started.Tray.Entries[Pause].Chosen();

        Assert.AreEqual(7, started.Seam.Registrations, "only the other three are registered again");
        CollectionAssert.AreEquivalent(new[] { Open, TakeScreenshot, StashClipboardImage }, started.Seam.Registered.ToList());
        Assert.AreEqual("Register the record shortcut failed: Ctrl+Shift+PrtScn, another program holds it", started.Tray.ToolTip, "named once");
    }

    [TestMethod]
    public void StashClipboardImagesShortcutHeldByAnotherProgramIsNamedWhileTheOtherThreeAndTheEntryWork()
    {
        var started = StartedShortcuts.Start(held: StashClipboardImage);

        Assert.AreEqual("Register the Stash clipboard image shortcut failed: Ctrl+Alt+V, another program holds it", started.Tray.ToolTip);
        CollectionAssert.AreEquivalent(new[] { Open, TakeScreenshot, RecordClip }, started.Seam.Registered.ToList());
        Assert.AreEqual(StashClipboardImage.Gesture, started.Tray.Entries[Stash].Gesture, "the entry still shows its shortcut");
        Assert.IsTrue(started.Seam.Press(Open));
        Assert.AreEqual(1, started.Summoned, "the open shortcut fires");
        started.Tray.Entries[Stash].Chosen();
        Assert.AreEqual(1, started.StashedFromTray, "the entry still runs the act");

        started.Tray.Entries[Pause].Chosen();
        started.Tray.Entries[Pause].Chosen();

        Assert.AreEqual(7, started.Seam.Registrations, "only the other three are registered again");
        CollectionAssert.AreEquivalent(new[] { Open, TakeScreenshot, RecordClip }, started.Seam.Registered.ToList());
        Assert.AreEqual("Register the Stash clipboard image shortcut failed: Ctrl+Alt+V, another program holds it", started.Tray.ToolTip, "named once");
    }

    [TestMethod]
    public void TwoHeldAreBothNamedTheNewestWithItsReasonAndTheOlderByItsAction()
    {
        var started = StartedShortcuts.Start(held: [Open, StashClipboardImage]);

        CollectionAssert.AreEqual(new[] { TakeScreenshot, RecordClip }, started.Seam.Registered.ToList());
        Assert.IsLessThanOrEqualTo(127, started.Tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Register the Stash clipboard image shortcut failed: Ctrl+Alt+V, another program holds it", "Register the open shortcut failed" }, started.Tray.ToolTip.Split('\n'),
            "which shortcut is unavailable, each, and why for the newest, whose reason fits beside the older cut to its action (G33)");
        started.Tray.Clicked!();
        started.Tray.Entries[Stash].Chosen();
        Assert.AreEqual((1, 1), (started.Opened, started.StashedFromTray), "the tray still reaches every act");
    }

    [TestMethod]
    public void AllFourHeldAreEachStillNamedByTheirCombinationsWhenTheirActionsDoNotFitTogether()
    {
        var started = StartedShortcuts.Start(held: [Open, TakeScreenshot, RecordClip, StashClipboardImage]);

        Assert.IsEmpty(started.Seam.Registered);
        var byAction = string.Join("\n", "Register the Stash clipboard image shortcut failed", "Register the record shortcut failed", "Register the screenshot shortcut failed", "Register the open shortcut failed");
        Assert.IsGreaterThan(127, byAction.Length, "the four cut to their actions do not fit, and the platform would cut the oldest away");
        Assert.IsLessThanOrEqualTo(127, started.Tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+V is unavailable", "Ctrl+Shift+PrtScn is unavailable", "Ctrl+PrtScn is unavailable", "Ctrl+Alt+Space is unavailable" }, started.Tray.ToolTip.Split('\n'),
            "each unavailable shortcut still named, by its combination as the settings spell it (G33)");
        started.Tray.Entries[Record].Chosen();
        Assert.AreEqual(1, started.RecordedFromTray, "the tray still reaches every act");
    }

    [TestMethod]
    public void FourLongCombinationsAllHeldAreEachStillNamedByTheirEntriesWhenEvenTheirCombinationsDoNotFitTogether()
    {
        string[] texts = ["Ctrl+Alt+Shift+BrowserHome", "Ctrl+Alt+Shift+BrowserRefresh", "Ctrl+Alt+Shift+BrowserSearch", "Ctrl+Alt+Shift+BrowserFavorites"];
        var started = StartedShortcuts.Start(open: texts[0], screenshot: texts[1], record: texts[2], stash: texts[3], held: texts.Select(text => Shortcut.Parse(text).Value!).ToArray());

        var byCombination = string.Join("\n", "Ctrl+Alt+Shift+BrowserFavorites is unavailable", "Register the record shortcut failed", "Register the screenshot shortcut failed", "Register the open shortcut failed");
        Assert.IsGreaterThan(127, byCombination.Length, "the four by their shortest lines do not fit, and the platform would cut the oldest away");
        Assert.IsLessThanOrEqualTo(127, started.Tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Stash clipboard image unavailable", "Record a clip unavailable", "Take a screenshot unavailable", "Open ImageStash unavailable" }, started.Tray.ToolTip.Split('\n'),
            "each unavailable shortcut still named, by its entry in the menu (G33)");
    }

    [TestMethod]
    public void AShortcutTheSeamReleasesForTheLayoutIsNamedAndStaysReleasedThroughPauseAndTheOthersAreUntouched()
    {
        var started = StartedShortcuts.Start();
        var pressInFlight = started.Seam.PressedCallback(StashClipboardImage);

        started.Seam.Release(StashClipboardImage, ShortcutReasons.TypesACharacter);
        pressInFlight();
        started.RunPosted();

        Assert.AreEqual("Register the Stash clipboard image shortcut failed: Ctrl+Alt+V, it would type a character", started.Tray.ToolTip);
        Assert.AreEqual(0, started.Stashed, "a released shortcut does not act");
        CollectionAssert.AreEquivalent(new[] { Open, TakeScreenshot, RecordClip }, started.Seam.Registered.ToList(), "the others are untouched");
        started.Tray.Entries[Pause].Chosen();
        started.Tray.Entries[Pause].Chosen();
        Assert.AreEqual(7, started.Seam.Registrations, "the toggle back registers the other three alone");
        CollectionAssert.AreEquivalent(new[] { Open, TakeScreenshot, RecordClip }, started.Seam.Registered.ToList());
        Assert.IsTrue(started.Seam.Press(Open));
        Assert.AreEqual(1, started.Summoned);
    }

    [TestMethod]
    public void PauseReleasesAllFourAndItsToggleRegistersAllFourAgainUnderTheSameChecks()
    {
        var started = StartedShortcuts.Start();
        var changes = 0;
        started.Shortcuts.Changed += () => changes++;

        started.Tray.Entries[Pause].Chosen();

        Assert.IsTrue(started.Shortcuts.Paused);
        Assert.IsTrue(started.Tray.Entries[Pause].Checked!.Value, "the toggle shows checked");
        Assert.IsEmpty(started.Seam.Registered);
        Assert.IsFalse(started.Seam.Press(Open));
        Assert.IsFalse(started.Seam.Press(TakeScreenshot));
        Assert.IsFalse(started.Seam.Press(RecordClip));
        Assert.IsFalse(started.Seam.Press(StashClipboardImage));
        started.RunPosted();
        Assert.AreEqual((0, 0, 0, 0), (started.Summoned, started.Screenshots, started.Recorded, started.Stashed), "none fires");

        started.Tray.Entries[Pause].Chosen();

        Assert.IsFalse(started.Shortcuts.Paused);
        Assert.IsFalse(started.Tray.Entries[Pause].Checked!.Value);
        Assert.AreEqual(8, started.Seam.Registrations, "all four registered again through the seam, under its checks");
        CollectionAssert.AreEquivalent(new[] { Open, TakeScreenshot, RecordClip, StashClipboardImage }, started.Seam.Registered.ToList());
        Assert.IsTrue(started.Seam.Press(Open));
        Assert.IsTrue(started.Seam.Press(TakeScreenshot));
        Assert.IsTrue(started.Seam.Press(RecordClip));
        Assert.IsTrue(started.Seam.Press(StashClipboardImage));
        started.RunPosted();
        Assert.AreEqual((1, 1, 1, 1), (started.Summoned, started.Screenshots, started.Recorded, started.Stashed), "all fire");
        Assert.AreEqual(2, changes);
    }

    [TestMethod]
    public void APressRunAfterPauseLetGoOfItsShortcutDoesNotAct()
    {
        var started = StartedShortcuts.Start();
        var stashInFlight = started.Seam.PressedCallback(StashClipboardImage);
        var recordInFlight = started.Seam.PressedCallback(RecordClip);
        var screenshotInFlight = started.Seam.PressedCallback(TakeScreenshot);
        var openInFlight = started.Seam.PressedCallback(Open);

        started.Tray.Entries[Pause].Chosen();
        stashInFlight();
        recordInFlight();
        screenshotInFlight();
        openInFlight();
        started.RunPosted();

        Assert.AreEqual((0, 0, 0, 0), (started.Summoned, started.Screenshots, started.Recorded, started.Stashed));
    }

    [TestMethod]
    public void ALayoutReleaseReportedWhilePauseLetsGoIsNamedAndTheToggleBackLeavesItReleasedAndRegistersTheOthers()
    {
        var started = StartedShortcuts.Start();
        // The seam's own release, for the layout, arrives on its thread while Pause is letting the shortcut go.
        started.Seam.DuringRelease = shortcut =>
        {
            if (shortcut == StashClipboardImage)
            {
                started.Seam.Release(shortcut, ShortcutReasons.TypesACharacter);
            }
        };

        started.Tray.Entries[Pause].Chosen();
        started.Tray.Entries[Pause].Chosen();
        started.RunPosted();

        Assert.AreEqual($"Register the Stash clipboard image shortcut failed: Ctrl+Alt+V, {ShortcutReasons.TypesACharacter}", started.Tray.ToolTip);
        Assert.AreEqual(7, started.Seam.Registrations, "the toggle back does not revive it (G33: until it is changed in Settings)");
        CollectionAssert.AreEquivalent(new[] { Open, TakeScreenshot, RecordClip }, started.Seam.Registered.ToList());
    }

    [TestMethod]
    public void AToggleBackThatTheSeamRefusesForOneIsNamedTheOthersAreRegisteredAndTheNextPauseLeavesTheRefusedAlone()
    {
        var started = StartedShortcuts.Start();
        started.Tray.Entries[Pause].Chosen();
        // The toggle back registers in the order of the bindings: the open shortcut first.
        started.Seam.RefuseNext(ShortcutReasons.TypesACharacter);

        started.Tray.Entries[Pause].Chosen();

        Assert.AreEqual("Register the open shortcut failed: Ctrl+Alt+Space, it would type a character", started.Tray.ToolTip);
        CollectionAssert.AreEquivalent(new[] { TakeScreenshot, RecordClip, StashClipboardImage }, started.Seam.Registered.ToList());
        started.Tray.Entries[Pause].Chosen();
        started.Tray.Entries[Pause].Chosen();
        Assert.AreEqual(11, started.Seam.Registrations, "four at start, four at the first toggle back, three at the second");
        CollectionAssert.AreEquivalent(new[] { TakeScreenshot, RecordClip, StashClipboardImage }, started.Seam.Registered.ToList());
    }

    [TestMethod]
    [DataRow("Ctrl+Alt+")]
    [DataRow("Ctrl+Alt+Bogus")]
    [DataRow("Ctrl+Alt+Space+V")]
    [DataRow("Ctrl+Alt")]
    [DataRow("Ctrl+Alt+32")]
    public void AnOpenShortcutTextThatIsNotAShortcutIsNamedAndTheOthersAreRegistered(string text)
    {
        var started = StartedShortcuts.Start(open: text);

        Assert.AreEqual($"Register the open shortcut failed: {text}, not a shortcut ImageStash understands", started.Tray.ToolTip);
        Assert.IsNull(started.Shortcuts.ShortcutOf(ShortcutAct.Open));
        Assert.IsNull(started.Tray.Entries[0].Gesture);
        Assert.AreEqual(3, started.Seam.Registrations);
        CollectionAssert.AreEqual(new[] { TakeScreenshot, RecordClip, StashClipboardImage }, started.Seam.Registered.ToList());
        started.Tray.Clicked!();
        Assert.AreEqual(1, started.Opened, "the tray still opens the window");
    }

    [TestMethod]
    [DataRow("Ctrl+Bogus", "Ctrl+Bogus")]
    [DataRow("", "(empty)")]
    public void AScreenshotTextThatIsNotAShortcutIsNamedAndTheOthersAreRegistered(string text, string named)
    {
        var started = StartedShortcuts.Start(screenshot: text);

        Assert.AreEqual($"Register the screenshot shortcut failed: {named}, not a shortcut ImageStash understands", started.Tray.ToolTip);
        Assert.IsNull(started.Shortcuts.ShortcutOf(ShortcutAct.Screenshot));
        Assert.IsNull(started.Tray.Entries[1].Gesture, "no shortcut shown for it");
        CollectionAssert.AreEqual(new[] { Open, RecordClip, StashClipboardImage }, started.Seam.Registered.ToList());
        Assert.AreEqual(3, started.Seam.Registrations);
        started.Tray.Entries[1].Chosen();
        Assert.AreEqual(1, started.ScreenshotsFromTray, "the entry still runs the act");
    }

    [TestMethod]
    [DataRow("Ctrl+Shift+Bogus", "Ctrl+Shift+Bogus")]
    [DataRow("", "(empty)")]
    public void ARecordTextThatIsNotAShortcutIsNamedAndTheOthersAreRegistered(string text, string named)
    {
        var started = StartedShortcuts.Start(record: text);

        Assert.AreEqual($"Register the record shortcut failed: {named}, not a shortcut ImageStash understands", started.Tray.ToolTip);
        Assert.IsNull(started.Shortcuts.ShortcutOf(ShortcutAct.Record));
        Assert.IsNull(started.Tray.Entries[Record].Gesture, "no shortcut shown for it");
        CollectionAssert.AreEqual(new[] { Open, TakeScreenshot, StashClipboardImage }, started.Seam.Registered.ToList());
        Assert.AreEqual(3, started.Seam.Registrations);
        started.Tray.Entries[Record].Chosen();
        Assert.AreEqual(1, started.RecordedFromTray, "the entry still runs the act");
    }

    [TestMethod]
    [DataRow("Ctrl+Alt+Bogus", "Ctrl+Alt+Bogus")]
    [DataRow("", "(empty)")]
    [DataRow("  ", "(empty)")]
    public void AStashClipboardImageTextThatIsNotAShortcutIsNamedAndTheOthersAreRegistered(string text, string named)
    {
        var started = StartedShortcuts.Start(stash: text);

        Assert.AreEqual($"Register the Stash clipboard image shortcut failed: {named}, not a shortcut ImageStash understands", started.Tray.ToolTip);
        Assert.IsNull(started.Tray.Entries[Stash].Gesture, "no shortcut shown for it");
        CollectionAssert.AreEqual(new[] { Open, TakeScreenshot, RecordClip }, started.Seam.Registered.ToList());
        Assert.AreEqual(3, started.Seam.Registrations);
        started.Tray.Entries[Stash].Chosen();
        Assert.AreEqual(1, started.StashedFromTray, "the entry still runs the act");
    }

    [TestMethod]
    public void AnEmptyOpenShortcutIsNamedAsEmpty()
    {
        var started = StartedShortcuts.Start(open: "");

        Assert.AreEqual("Register the open shortcut failed: (empty), not a shortcut ImageStash understands", started.Tray.ToolTip);
        Assert.AreEqual(3, started.Seam.Registrations);
    }

    [TestMethod]
    [DataRow("Ctrl+Alt+Space")]
    [DataRow("alt+ctrl+SPACE")]
    public void ATextBothSettingsShareIsRegisteredOnceAndNamedForTheOther(string stash)
    {
        var started = StartedShortcuts.Start(stash: stash);

        CollectionAssert.AreEqual(new[] { Open, TakeScreenshot, RecordClip }, started.Seam.Registered.ToList());
        Assert.AreEqual(3, started.Seam.Registrations, "never two registrations of one combination");
        Assert.AreEqual($"Register the Stash clipboard image shortcut failed: {stash}, the open shortcut uses it too", started.Tray.ToolTip);
        Assert.IsTrue(started.Seam.Press(Open));
        started.RunPosted();
        Assert.AreEqual((1, 0), (started.Summoned, started.Stashed), "the press is the open shortcut's");

        started.Tray.Entries[Pause].Chosen();
        started.Tray.Entries[Pause].Chosen();

        Assert.AreEqual(6, started.Seam.Registrations, "the toggle back registers it once again, for the open shortcut alone, beside the screenshot's and the record shortcut");
        CollectionAssert.AreEquivalent(new[] { Open, TakeScreenshot, RecordClip }, started.Seam.Registered.ToList());
    }

    [TestMethod]
    public void TheScreenshotShortcutSpellingTheOpenShortcutIsNamedForTheScreenshotAndThePressIsTheOpenShortcuts()
    {
        var started = StartedShortcuts.Start(screenshot: "ctrl+alt+space");

        CollectionAssert.AreEqual(new[] { Open, RecordClip, StashClipboardImage }, started.Seam.Registered.ToList());
        Assert.AreEqual(3, started.Seam.Registrations, "never two registrations of one combination");
        Assert.AreEqual("Register the screenshot shortcut failed: ctrl+alt+space, the open shortcut uses it too", started.Tray.ToolTip);
        Assert.IsTrue(started.Seam.Press(Open));
        started.RunPosted();
        Assert.AreEqual((1, 0), (started.Summoned, started.Screenshots), "the press is the open shortcut's");
    }

    [TestMethod]
    public void StashClipboardImageSpellingTheScreenshotShortcutIsNamedForItselfAndThePressIsTheScreenshots()
    {
        var started = StartedShortcuts.Start(stash: "ctrl+prtscn");

        CollectionAssert.AreEqual(new[] { Open, TakeScreenshot, RecordClip }, started.Seam.Registered.ToList());
        Assert.AreEqual(3, started.Seam.Registrations, "never two registrations of one combination");
        Assert.AreEqual("Register the Stash clipboard image shortcut failed: ctrl+prtscn, the screenshot shortcut uses it too", started.Tray.ToolTip);
        Assert.IsTrue(started.Seam.Press(TakeScreenshot));
        started.RunPosted();
        Assert.AreEqual((1, 0), (started.Screenshots, started.Stashed), "the press is the screenshot shortcut's");
    }

    [TestMethod]
    public void TheRecordShortcutSpellingTheScreenshotShortcutIsNamedForItselfAndThePressIsTheScreenshots()
    {
        var started = StartedShortcuts.Start(record: "ctrl+prtscn");

        CollectionAssert.AreEqual(new[] { Open, TakeScreenshot, StashClipboardImage }, started.Seam.Registered.ToList());
        Assert.AreEqual(3, started.Seam.Registrations, "never two registrations of one combination");
        Assert.AreEqual("Register the record shortcut failed: ctrl+prtscn, the screenshot shortcut uses it too", started.Tray.ToolTip);
        Assert.IsTrue(started.Seam.Press(TakeScreenshot));
        started.RunPosted();
        Assert.AreEqual((1, 0), (started.Screenshots, started.Recorded), "the press is the screenshot shortcut's");
    }

    [TestMethod]
    public void StashClipboardImageSpellingTheRecordShortcutIsNamedForItselfAndThePressIsTheRecordShortcuts()
    {
        var started = StartedShortcuts.Start(stash: "shift+ctrl+prtscn");

        CollectionAssert.AreEqual(new[] { Open, TakeScreenshot, RecordClip }, started.Seam.Registered.ToList());
        Assert.AreEqual(3, started.Seam.Registrations, "never two registrations of one combination");
        Assert.AreEqual("Register the Stash clipboard image shortcut failed: shift+ctrl+prtscn, the record shortcut uses it too", started.Tray.ToolTip);
        Assert.IsTrue(started.Seam.Press(RecordClip));
        started.RunPosted();
        Assert.AreEqual((1, 0), (started.Recorded, started.Stashed), "the press is the record shortcut's");
    }

    [TestMethod]
    [DataRow("ctrl+alt+space")]
    [DataRow("Alt+Ctrl+Space")]
    [DataRow(" Ctrl + Alt + Space ")]
    public void ModifiersAreReadInAnyCaseAndOrder(string text)
    {
        var started = StartedShortcuts.Start(open: text);

        Assert.AreEqual(new KeyGesture(Key.Space, KeyModifiers.Control | KeyModifiers.Alt), started.Shortcuts.ShortcutOf(ShortcutAct.Open)!.Gesture);
        Assert.AreEqual("Ctrl+Alt+Space", started.Shortcuts.ShortcutOf(ShortcutAct.Open)!.Text);
        CollectionAssert.Contains(started.Seam.Registered.ToList(), Open);
    }

    [TestMethod]
    public void TheSpellingsOfF5AndTheDigitsAreReadAndWrittenBack()
    {
        Assert.AreEqual(new KeyGesture(Key.PrintScreen, KeyModifiers.Control), Shortcut.Parse("Ctrl+PrtScn").Value!.Gesture);
        Assert.AreEqual("Ctrl+PrtScn", Shortcut.Parse("ctrl+prtscn").Value!.Text);
        Assert.AreEqual(new KeyGesture(Key.PrintScreen, KeyModifiers.Control | KeyModifiers.Shift), Shortcut.Parse("Ctrl+Shift+PrtScn").Value!.Gesture);
        Assert.AreEqual("Ctrl+Shift+PrtScn", Shortcut.Parse("ctrl+shift+prtscn").Value!.Text);
        Assert.AreEqual("Ctrl+Alt+V", Shortcut.Parse("Ctrl+Alt+V").Value!.Text);
        Assert.AreEqual(new KeyGesture(Key.D1, KeyModifiers.Meta), Shortcut.Parse("Win+1").Value!.Gesture);
        Assert.AreEqual("Win+1", Shortcut.Parse("win+1").Value!.Text);
        Assert.AreEqual("Ctrl+F12", Shortcut.Parse("Ctrl+F12").Value!.Text);
    }
}
