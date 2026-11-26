using Avalonia.Input;
using ImageStash.Capture;
using ImageStash.Files;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G34's Open ImageStash with its shortcut, Take a screenshot, Record a clip (Stop recording while a recording samples),
/// Stash clipboard image, Pause shortcuts, Settings, left click and Quit, and G35's failures named in the tooltip, where a
/// start failure is named (F4).
/// </summary>
[TestClass]
public sealed class TrayMenuTests
{
    [TestMethod]
    public void TheMenuHoldsOpenScreenshotRecordStashPauseSettingsThenQuitAndALeftClickOpensAsOpenDoes()
    {
        var tray = new RecordingTray();
        var (opened, screenshots, recorded, stashed, settings, quit) = (0, 0, 0, 0, 0, false);
        new TrayMenu(tray, () => opened++, () => screenshots++, () => recorded++, () => stashed++, () => settings++, () => quit = true).Show(Shortcuts());

        CollectionAssert.AreEqual(new[] { "Open ImageStash", "Take a screenshot", "Record a clip", "Stash clipboard image", "Pause shortcuts", "Settings", "Quit ImageStash" }, tray.Entries.Select(e => e.Header).ToList(),
            "G34's order, Settings before Quit");
        tray.Entries[0].Chosen();
        Assert.AreEqual(1, opened);
        Assert.IsFalse(quit);
        tray.Clicked!();
        Assert.AreEqual(2, opened);
        Assert.AreEqual((0, 0, 0), (screenshots, recorded, stashed), "neither Open ImageStash nor a left-click captures or stashes anything");
        tray.Entries[1].Chosen();
        Assert.AreEqual((2, 1, 0, 0), (opened, screenshots, recorded, stashed), "Take a screenshot runs its act and no other (G14)");
        Assert.IsNull(tray.Entries[1].Checked, "a plain entry");
        tray.Entries[2].Chosen();
        Assert.AreEqual((2, 1, 1, 0), (opened, screenshots, recorded, stashed), "Record a clip runs its act and no other (G15)");
        Assert.IsNull(tray.Entries[2].Checked, "a plain entry");
        Assert.AreEqual(new KeyGesture(Key.PrintScreen, KeyModifiers.Control | KeyModifiers.Shift), tray.Entries[2].Gesture, "with its shortcut (G34)");
        tray.Entries[3].Chosen();
        Assert.AreEqual((2, 1, 1, 1), (opened, screenshots, recorded, stashed), "Stash clipboard image runs its act and opens nothing (G13)");
        Assert.IsNull(tray.Entries[3].Checked, "a plain entry");
        tray.Entries[4].Chosen();
        Assert.AreEqual(0, settings, "Pause shortcuts opens nothing");
        tray.Entries[5].Chosen();
        Assert.AreEqual(1, settings, "Settings opens the page (F4, G32)");
        Assert.IsNull(tray.Entries[5].Checked, "a plain entry");
        Assert.IsNull(tray.Entries[5].Gesture, "with no shortcut of its own: the specifications give it none (G34)");
        Assert.IsFalse(quit);
        Assert.AreEqual((2, 1, 1, 1), (opened, screenshots, recorded, stashed), "Settings runs no other act");
        tray.Entries[6].Chosen();
        Assert.IsTrue(quit);
        Assert.AreEqual((2, 1, 1, 1, 1), (opened, screenshots, recorded, stashed, settings), "no other entry runs an act");
    }

    [TestMethod]
    public void AnOutcomeThatFailedIsNamedInTheTooltipAndOneThatSucceededIsNot()
    {
        var tray = new RecordingTray(toolTipLength: 1000);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        var before = tray.ToolTip;

        menu.NameIfFailed(Outcome.Done);
        Assert.AreEqual(before, tray.ToolTip, "nothing named for a success");
        var refused = new Failure("Start with Windows", "Run entry ImageStash", "Windows refused the change");
        menu.NameIfFailed(refused);

        Assert.AreEqual(refused.Sentence, tray.ToolTip, "the failure named as Name names it (G35)");
    }

    [TestMethod]
    public void OnlyOpenImageStashAndSettingsActAgainOnceTheMenuHasClosedAndALeftClickDoesNot()
    {
        var tray = new RecordingTray();
        var (forward, pageForward) = (0, 0);
        new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }) { OpenedMenuClosed = () => forward++, SettingsMenuClosed = () => pageForward++ }.Show(Shortcuts());

        CollectionAssert.AreEqual(new[] { "Open ImageStash", "Settings" }, tray.Entries.Where(entry => entry.MenuClosed is not null).Select(entry => entry.Header).ToList(),
            "only what a menu's going can leave behind other windows (G1, G32)");
        tray.Clicked!();
        tray.Entries[0].Chosen();
        Assert.AreEqual((0, 0), (forward, pageForward), "neither a left-click nor the choice itself: the seam runs it after the menu");
        tray.Entries[0].MenuClosed!();
        Assert.AreEqual((1, 0), (forward, pageForward));
        tray.Entries.Single(entry => entry.Header == TrayMenu.SettingsHeader).MenuClosed!();
        Assert.AreEqual((1, 1), (forward, pageForward));
    }

    [TestMethod]
    public void EachEntryShowsItsShortcutAsTheSettingsReadItAndPauseIsAToggleThatFollowsThePolicy()
    {
        var tray = new RecordingTray();
        var shortcuts = Shortcuts("Ctrl+Alt+Space", "ctrl+shift+v", "shift+prtscn", "alt+prtscn");
        new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }).Show(shortcuts);

        Assert.AreEqual(new KeyGesture(Key.Space, KeyModifiers.Control | KeyModifiers.Alt), tray.Entries[0].Gesture);
        Assert.AreEqual(new KeyGesture(Key.PrintScreen, KeyModifiers.Shift), tray.Entries[1].Gesture, "Take a screenshot beside its own shortcut, registered or not (G34)");
        Assert.AreEqual(new KeyGesture(Key.PrintScreen, KeyModifiers.Alt), tray.Entries[2].Gesture, "Record a clip beside its own shortcut, registered or not (G34)");
        Assert.AreEqual(new KeyGesture(Key.V, KeyModifiers.Control | KeyModifiers.Shift), tray.Entries[3].Gesture, "Stash clipboard image beside its own shortcut, registered or not (G34)");
        var gestures = tray.Entries.Select(entry => entry.Gesture).ToList();
        Assert.IsFalse(tray.Entries[4].Checked!.Value);
        tray.Entries[4].Chosen();
        Assert.IsTrue(shortcuts.Paused);
        Assert.IsTrue(tray.Entries[4].Checked!.Value);
        CollectionAssert.AreEqual(gestures, tray.Entries.Select(entry => entry.Gesture).ToList(), "Pause releases shortcuts and leaves their menu labels visible (G34)");
        shortcuts.TogglePause();
        Assert.IsFalse(tray.Entries[4].Checked!.Value);
    }

    [TestMethod]
    public void RecordAClipReadsStartRecordingWhileTheRegionWaitsAndStopRecordingWithTheRedDotWhileItSamples()
    {
        var tray = new RecordingTray();
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        var shown = tray.Entries.Select(e => e.Header).ToList();
        Assert.AreEqual(TrayMenu.RecordClipHeader, shown[2], "the entry whose words change is listed, third (G34)");

        menu.SetRecording(RecordingStage.Waiting);
        CollectionAssert.AreEqual(new[] { ("Record a clip", "Start recording") }, tray.HeaderChanges, "the entry shown as Record a clip reads Start recording while the region waits (G34)");
        Assert.IsFalse(tray.Recording, "no red dot before the recording runs (G15)");
        menu.SetRecording(RecordingStage.Sampling);
        Assert.AreEqual(("Record a clip", "Stop recording"), tray.HeaderChanges[^1], "Stop recording while it records (G15, G34)");
        Assert.IsTrue(tray.Recording, "the icon's red dot (G15)");
        menu.SetRecording(RecordingStage.None);
        CollectionAssert.AreEqual(new[] { ("Record a clip", "Start recording"), ("Record a clip", "Stop recording"), ("Record a clip", "Record a clip") }, tray.HeaderChanges, "and Record a clip again");
        Assert.IsFalse(tray.Recording);
        menu.SetRecording(RecordingStage.Waiting);
        menu.SetRecording(RecordingStage.None);
        Assert.AreEqual(("Record a clip", "Record a clip"), tray.HeaderChanges[^1], "a wait cancelled: Record a clip again, never left reading Start recording");
        Assert.IsFalse(tray.Recording, "no red dot at any point of a cancelled wait");
        CollectionAssert.AreEqual(shown, tray.Entries.Select(e => e.Header).ToList(), "no entry added, removed or renamed by its header");
    }

    [TestMethod]
    [DataRow("Ctrl+Alt+Space", "ImageStash · Ctrl+Alt+Space")]
    [DataRow("space+alt+CTRL", "ImageStash · Ctrl+Alt+Space")]
    [DataRow("Shift+Win+F1", "ImageStash · Win+Shift+F1")]
    public void WithNothingNamedTheTooltipReadsImageStashBesideTheOpenShortcutAsTheSettingsHoldIt(string open, string expected)
    {
        var started = StartedShortcuts.Start(open: open);

        Assert.AreEqual(expected, started.Tray.ToolTip, "pointing at the icon teaches the gesture, in the words the settings' combination is shown in everywhere");
    }

    [TestMethod]
    public void UnderPauseTheTooltipNamesNoShortcutAndToggledBackItNamesTheOpenShortcutAtOnce()
    {
        var started = StartedShortcuts.Start();

        started.Tray.Entries[4].Chosen();
        Assert.IsTrue(started.Shortcuts.Paused);
        Assert.AreEqual("ImageStash · shortcuts paused", started.Tray.ToolTip, "while Pause releases all four, the tooltip says so and offers no key that does nothing (G33)");

        started.Tray.Entries[4].Chosen();
        Assert.AreEqual("ImageStash · Ctrl+Alt+Space", started.Tray.ToolTip, "toggled back, at that moment");
    }

    [TestMethod]
    public void AfterTheOpenShortcutIsRemappedTheTooltipReadsTheNewCombinationAtOnceAndUnderPauseOnlyAtTheToggleBack()
    {
        var started = StartedShortcuts.Start();
        using (started.Shortcuts.Suspend())
        {
            Assert.IsNull(started.Shortcuts.Rebind(ShortcutAct.Open, Shortcut.Parse("ctrl+shift+f9").Value!));
        }

        Assert.AreEqual("ImageStash · Ctrl+Shift+F9", started.Tray.ToolTip, "as the menu's entries follow a remap, not composed once when the icon was shown");

        started.Shortcuts.TogglePause();
        using (started.Shortcuts.Suspend())
        {
            Assert.IsNull(started.Shortcuts.Rebind(ShortcutAct.Open, Shortcut.Parse("Alt+F9").Value!));
        }

        Assert.AreEqual("ImageStash · shortcuts paused", started.Tray.ToolTip, "remapped under Pause, still released");
        started.Shortcuts.TogglePause();
        Assert.AreEqual("ImageStash · Alt+F9", started.Tray.ToolTip);
    }

    [TestMethod]
    [DataRow(ShortcutRefusalKind.HeldByAnotherProgram, "Ctrl+Alt+Space is used by another app, so Open ImageStash has no shortcut.")]
    [DataRow(ShortcutRefusalKind.TypesACharacter, "Ctrl+Alt+Space types a character, so Open ImageStash has no shortcut.")]
    public void WhileTheOpenShortcutIsUnavailableTheTooltipNamesOnlyWhyAndOnceItIsRegisteredAgainItNamesItAtOnce(ShortcutRefusalKind reason, string line)
    {
        var open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
        var started = reason == ShortcutRefusalKind.HeldByAnotherProgram ? StartedShortcuts.Start(held: open) : StartedShortcuts.Start();
        if (reason == ShortcutRefusalKind.TypesACharacter)
        {
            started.Seam.Release(open, ShortcutRefusal.TypesACharacter(null));
            started.RunPosted();
        }

        Assert.AreEqual(line, started.Tray.ToolTip, "the failure alone, as before, and no key that does nothing (G33)");

        started.Seam.Free(open);
        using (started.Shortcuts.Suspend())
        {
            Assert.IsNull(started.Shortcuts.Rebind(ShortcutAct.Open, open));
        }

        Assert.AreEqual("ImageStash · Ctrl+Alt+Space", started.Tray.ToolTip, "registered again, at that moment and not at a later remap");
    }

    [TestMethod]
    [DataRow("nonsense", "\"nonsense\" isn't a shortcut ImageStash understands, so Open ImageStash has no shortcut.")]
    [DataRow("", "Open ImageStash has no shortcut set.")]
    public void WhileTheOpenSettingIsNoShortcutTheTooltipNamesOnlyThatAndNoCombination(string open, string line)
    {
        var started = StartedShortcuts.Start(open: open);

        Assert.AreEqual(line, started.Tray.ToolTip);
    }

    [TestMethod]
    public void AnUnavailableOpenShortcutIsNeverOfferedEvenWhenItsFailureIsNamedElsewhere()
    {
        var tray = new RecordingTray();
        var seam = new RecordingShortcuts();
        seam.Refuse(Shortcut.Parse("Ctrl+Alt+Space").Value!, ShortcutRefusal.HeldByAnotherProgram);
        var shortcuts = new GlobalShortcuts(StartedShortcuts.SettingsInMemory(), GlobalShortcuts.Bindings(() => { }, () => { }, () => { }, () => { }), seam, _ => { }, action => action());
        new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }).Show(shortcuts);
        shortcuts.Start();
        shortcuts.TogglePause();
        shortcuts.TogglePause();

        Assert.IsNotNull(shortcuts.UnavailableReason(ShortcutAct.Open));
        Assert.AreEqual("ImageStash", tray.ToolTip, "the tooltip names the open shortcut only while a press of it would summon (G33)");
    }

    [TestMethod]
    public void WithAFailureNamedTheTooltipReadsTheFailuresAloneThoughTheOpenShortcutIsRegistered()
    {
        var started = StartedShortcuts.Start(held: Shortcut.Parse("Ctrl+PrtScn").Value!);

        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Open), "the open shortcut is registered");
        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", started.Tray.ToolTip,
            "exactly what it read before: the open shortcut's words never take room from a failure's why (G33)");

        started.Menu.Name(new Failure("Save settings", "settings.json", "refused"));
        CollectionAssert.AreEqual(new[] { "Couldn't save settings (settings.json): refused.", "Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut." },
            started.Tray.ToolTip!.Split('\n'));
    }

    [TestMethod]
    public void FailuresNamedBeforeAndAfterShowingAreListedNewestFirst()
    {
        var tray = new RecordingTray();
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Name(new Failure("Read settings", "settings.json", "not valid settings"));
        menu.Show(Shortcuts());
        menu.Name(new Failure("Start with Windows", "Run entry ImageStash", "refused"));

        var lines = tray.ToolTip!.Split('\n');
        Assert.HasCount(2, lines);
        Assert.IsLessThanOrEqualTo(tray.ToolTipLength, tray.ToolTip.Length);
        StringAssert.Contains(lines[0], "start with Windows");
        StringAssert.Contains(lines[0], "Run entry ImageStash");
        StringAssert.Contains(lines[0], "refused");
        StringAssert.Contains(lines[1], "read settings");
        StringAssert.Contains(lines[1], "settings.json");
        StringAssert.Contains(lines[1], "not valid settings");
    }

    [TestMethod]
    public void FailuresTooLongTogetherKeepTheNewestWholeAndTheOlderActionAndSubject()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        menu.Name(new Failure("Read settings", "settings.json", HeldFileRetry.HeldReason));
        menu.Name(new Failure("Stop starting with Windows", "Run entry ImageStash", "Windows refused the change"));

        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        var lines = tray.ToolTip.Split('\n');
        Assert.HasCount(2, lines);
        Assert.AreEqual("Couldn't stop starting with Windows (Run entry ImageStash): Windows refused the change.", lines[0]);
        Assert.AreEqual("Couldn't read settings: settings.json", lines[1]);
    }

    [TestMethod]
    public void ManyFailuresEachKeepTheirAction()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        var actions = new[] { "Read settings", "Save settings", "Start with Windows", "Open the stash", "Move to the Recycle Bin" };
        foreach (var action in actions)
        {
            menu.Name(new Failure(action, "a-rather-long-file-name-for-a-reaction.gif", HeldFileRetry.HeldReason));
        }

        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Couldn't move to the Recycle Bin, open the stash, start with Windows, save settings or read settings" }, tray.ToolTip.Split('\n'),
            "a line each would not fit: their acts share one line, newest first (G35)");
    }

    [TestMethod]
    [DataRow(ShortcutRefusalKind.HeldByAnotherProgram, "Ctrl+Alt+Space is used by another app, so Open ImageStash has no shortcut.")]
    [DataRow(ShortcutRefusalKind.TypesACharacter, "Ctrl+Alt+Space types a character, so Open ImageStash has no shortcut.")]
    public void AnUnavailableShortcutKeepsItsReasonWhileLaterFailuresAreShortened(ShortcutRefusalKind reason, string line)
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Name(new Failure("Read settings", "settings.json", HeldFileRetry.HeldReason));
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(new ShortcutProblem(ShortcutAct.Open, "Ctrl+Alt+Space", ShortcutReason.Of(new ShortcutRefusal(reason))));
        menu.Name(new Failure("Open the stash", @"C:\Users\someone\Pictures\Reaction stash", "a file is where the stash folder should be"));

        var lines = tray.ToolTip!.Split('\n');
        Assert.IsLessThanOrEqualTo(127, tray.ToolTip.Length);
        CollectionAssert.AreEqual(new[] { "Couldn't open the stash", line, "Couldn't read settings" }, lines,
            "which shortcut is unavailable and why stays whole (G33); the others keep their action (G35)");
    }

    [TestMethod]
    public void AnUnavailableShortcutsReasonGivesWayOnlyWhenTheOtherFailuresActionsLeaveItNoRoom()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Name(new Failure("Stop starting with Windows", "Run entry ImageStash", "Windows refused the change"));
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(Held(ShortcutAct.Open, "Ctrl+Alt+Space"));
        menu.Name(new Failure("Open the stash", @"C:\Users\someone\Pictures\Reaction stash", "a file is where the stash folder should be"));

        var withTheReason = string.Join("\n", "Couldn't open the stash", "Ctrl+Alt+Space is used by another app, so Open ImageStash has no shortcut.", "Couldn't stop starting with Windows");
        Assert.IsGreaterThan(127, withTheReason.Length, "the reason does not fit beside the others' actions");
        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Couldn't open the stash", "Open ImageStash: Ctrl+Alt+Space is unavailable", "Couldn't stop starting with Windows" }, tray.ToolTip.Split('\n'),
            "the why gives way only when the tooltip cannot hold it beside every other failure named there, the shortcut still named by act and combination (G33); every failure keeps its action (G35)");
    }

    [TestMethod]
    public void OfTwoUnavailableShortcutsTheOlderIsCutBeforeTheNewerLosesItsReason()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(Held(ShortcutAct.Open, "Ctrl+Alt+Space"));
        menu.NameUnavailableShortcut(Held(ShortcutAct.StashClipboardImage, "Ctrl+Alt+V"));

        var bothWithoutWhy = string.Join("\n", "Stash clipboard image: Ctrl+Alt+V is unavailable", "Open ImageStash: Ctrl+Alt+Space is unavailable");
        Assert.IsLessThanOrEqualTo(127, bothWithoutWhy.Length, "cutting both to act and combination would also fit, and lose the newer's why");
        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+V is used by another app, so Stash clipboard image has no shortcut.", "Open ImageStash: Ctrl+Alt+Space is unavailable" }, tray.ToolTip.Split('\n'),
            "the newest's why fits beside the older cut to its act and combination, which still says which shortcut (G33)");
    }

    [TestMethod]
    public void TwoUnavailableShortcutsBesideOtherFailuresAreShortenedLastTheOlderFirstEachStillSayingWhich()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Name(new Failure("Read settings", "settings.json", HeldFileRetry.HeldReason));
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(Held(ShortcutAct.Open, "Ctrl+Alt+Space"));
        menu.NameUnavailableShortcut(new ShortcutProblem(ShortcutAct.StashClipboardImage, "Ctrl+Alt+V", ShortcutReason.Of(ShortcutRefusal.TypesACharacter(null))));

        var olderWithoutWhy = string.Join("\n", "Ctrl+Alt+V types a character, so Stash clipboard image has no shortcut.", "Open ImageStash: Ctrl+Alt+Space is unavailable", "Couldn't read settings");
        Assert.IsGreaterThan(127, olderWithoutWhy.Length, "the newest's why does not fit beside the older cut only to act and combination");
        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+V types a character, so Stash clipboard image has no shortcut.", "Open ImageStash unavailable", "Couldn't read settings" }, tray.ToolTip.Split('\n'),
            "the other failure is cut to its action first, then the older shortcut to its shortest form, and the newest keeps its why (G33, G35)");

        menu.Name(new Failure("Open the stash", @"C:\Users\someone\Pictures\Reaction stash", "a file is where the stash folder should be"));

        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Couldn't open the stash", "Stash clipboard image: Ctrl+Alt+V is unavailable", "Open ImageStash unavailable", "Couldn't read settings" }, tray.ToolTip.Split('\n'),
            "every line still there, each shortcut line still saying which shortcut, the newest's why given way only when nothing else can (G33)");
    }

    [TestMethod]
    public void FourUnavailableShortcutsAreEachStillNamedByTheirShortestForm()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(Held(ShortcutAct.Open, "Ctrl+Alt+Shift+Win+PrintScreen"));
        menu.NameUnavailableShortcut(Held(ShortcutAct.Screenshot, "Ctrl+PrtScn"));
        menu.NameUnavailableShortcut(Held(ShortcutAct.Record, "Ctrl+Shift+PrtScn"));
        menu.NameUnavailableShortcut(Held(ShortcutAct.StashClipboardImage, "Ctrl+Alt+V"));

        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+V is unavailable", "Record a clip unavailable", "Take a screenshot: Ctrl+PrtScn is unavailable", "Open ImageStash unavailable" }, tray.ToolTip.Split('\n'),
            "each line by its combination or its act, whichever is shorter, so every shortcut is still named, the room left keeping one act beside its combination (G33)");
    }

    [TestMethod]
    public void AFailureBesideFourUnavailableShortcutsStaysNamedTheShortcutsSharingOneLineWhenEachLineWouldNotFit()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Name(new Failure("Finish an unfinished rename", "cat.png", HeldFileRetry.HeldReason));
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(Held(ShortcutAct.Open, "Alt+F9"));
        menu.NameUnavailableShortcut(Held(ShortcutAct.Screenshot, "Ctrl+F8"));
        menu.NameUnavailableShortcut(Held(ShortcutAct.Record, "Alt+F10"));
        menu.NameUnavailableShortcut(Held(ShortcutAct.StashClipboardImage, "Win+Alt+V"));

        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Couldn't finish an unfinished rename", "Unavailable: Win+Alt+V, Alt+F10, Ctrl+F8, Alt+F9" }, tray.ToolTip.Split('\n'),
            "a line each would leave the failure out: the shortcuts share one line, so the failure and every unavailable shortcut are still named (G33, G35)");
    }

    [TestMethod]
    public void FourLongRemappedShortcutsAreNamedByTheirActsWhichTogetherAlwaysFit()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        foreach (var act in new[] { ShortcutAct.Open, ShortcutAct.Screenshot, ShortcutAct.Record, ShortcutAct.StashClipboardImage })
        {
            menu.NameUnavailableShortcut(Held(act, "Ctrl+Alt+Shift+Win+PrintScreen"));
        }

        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Stash clipboard image unavailable", "Record a clip unavailable", "Take a screenshot unavailable", "Open ImageStash unavailable" }, tray.ToolTip.Split('\n'),
            "the platform cuts none of them away (G33)");
    }

    [TestMethod]
    public void ThreeShortcutsHeldAtStartKeepTheNewestsWhyWhereItFitsBesideTheOthersShortestForms()
    {
        var started = StartedShortcuts.Start(open: "Alt+F9", screenshot: "Alt+F8", record: "Ctrl+F9",
            held: [Shortcut.Parse("Alt+F9").Value!, Shortcut.Parse("Alt+F8").Value!, Shortcut.Parse("Ctrl+F9").Value!]);

        Assert.IsLessThanOrEqualTo(127, started.Tray.ToolTip!.Length);
        Assert.AreEqual(string.Join("\n", "Ctrl+F9 is used by another app, so Record a clip has no shortcut.", "Alt+F8 is unavailable", "Open ImageStash: Alt+F9 is unavailable"), started.Tray.ToolTip,
            "the newest's line whole, since it fits beside the other two, each still named, and the room left keeps an act beside its combination (G33)");
    }

    [TestMethod]
    public void ThreeDefaultsHeldAtStartKeepAWhyWhereOneFitsThoughTheNewestsDoesNot()
    {
        var started = StartedShortcuts.Start(held: [Shortcut.Parse("Ctrl+Alt+Space").Value!, Shortcut.Parse("Ctrl+PrtScn").Value!, Shortcut.Parse("Ctrl+Shift+PrtScn").Value!]);

        var newestWhole = string.Join("\n", "Ctrl+Shift+PrtScn is used by another app, so Record a clip has no shortcut.", "Ctrl+PrtScn is unavailable", "Open ImageStash unavailable");
        Assert.IsGreaterThan(127, newestWhole.Length, "the newest's why does not fit beside the other two");
        Assert.AreEqual(string.Join("\n", "Record a clip unavailable", "Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", "Open ImageStash unavailable"), started.Tray.ToolTip,
            "a why gives way only when the tooltip cannot hold it: the next newest's fits, so it is kept (G33)");
    }

    [TestMethod]
    public void OneUnavailableShortcutAloneIsNamedWholeForEveryDefaultAndEveryReason()
    {
        var defaults = new Shortcuts();
        var reasons = new[]
        {
            ShortcutReason.Of(ShortcutRefusal.HeldByAnotherProgram), ShortcutReason.Of(ShortcutRefusal.TypesACharacter("@")), ShortcutReason.Of(ShortcutRefusal.TypesACharacter(null)),
            ShortcutReason.Of(ShortcutRefusal.NotAKey(Avalonia.Input.Key.ImeConvert)), ShortcutReason.Of(ShortcutRefusal.Stated("The parameter is incorrect.")),
            ShortcutReason.PasteKeystroke, ShortcutReason.UsedBy(ShortcutAct.StashClipboardImage), ShortcutReason.ModifiersLetGoFirst, ShortcutReason.NotAShortcut,
        };
        foreach (var (act, text) in new[] { (ShortcutAct.Open, defaults.Open), (ShortcutAct.Screenshot, defaults.Screenshot), (ShortcutAct.Record, defaults.Record), (ShortcutAct.StashClipboardImage, defaults.StashClipboardImage) })
        {
            foreach (var reason in reasons)
            {
                var tray = new RecordingTray(toolTipLength: 127);
                var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
                menu.Show(Shortcuts());
                var unavailable = new ShortcutProblem(act, text, reason);
                menu.NameUnavailableShortcut(unavailable);

                Assert.AreEqual(ShortcutWords.ToolTipLine(unavailable), tray.ToolTip, $"{text}, {reason.Kind}: the whole sentence, its why kept (G33)");
                Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
            }
        }
    }

    [TestMethod]
    public void WhenEvenTheActionsDoNotFitTheyShareOneLineSoEveryUnavailableShortcutAndEveryActIsStillNamed()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(Held(ShortcutAct.Open, "Ctrl+Alt+Space"));
        menu.NameUnavailableShortcut(Held(ShortcutAct.StashClipboardImage, "Ctrl+Alt+V"));
        menu.Name(new Failure("Finish an unfinished save", "old meme.png", HeldFileRetry.HeldReason));
        menu.Name(new Failure("Finish an unfinished rename", "laughing cat.gif", HeldFileRetry.HeldReason));
        menu.Name(new Failure("Finish an unfinished rename", "sad dog.png", HeldFileRetry.HeldReason));

        var everyShortest = string.Join("\n", "Couldn't finish an unfinished rename", "Couldn't finish an unfinished rename", "Couldn't finish an unfinished save", "Ctrl+Alt+V is unavailable", "Open ImageStash unavailable");
        Assert.IsGreaterThan(127, everyShortest.Length, "the platform would cut the oldest line, the open shortcut's");
        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Couldn't finish an unfinished rename or finish an unfinished save", "Ctrl+Alt+V is unavailable", "Open ImageStash unavailable" }, tray.ToolTip.Split('\n'),
            "the failures' acts share one line, each once, and every unavailable shortcut is still named (G33, G35)");
    }

    [TestMethod]
    public void WhenEvenTheSharedLineDoesNotFitTheOldestOtherFailuresLeaveSoEveryUnavailableShortcutIsStillNamed()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(Held(ShortcutAct.Open, "Ctrl+Alt+Space"));
        menu.NameUnavailableShortcut(Held(ShortcutAct.StashClipboardImage, "Ctrl+Alt+V"));
        menu.Name(new Failure("Undo an unfinished undo of an adoption", "old meme.png", HeldFileRetry.HeldReason));
        menu.Name(new Failure("Finish an unfinished undo of an adoption", "laughing cat.gif", HeldFileRetry.HeldReason));
        menu.Name(new Failure("Recycle the picture an Open with… edit replaced", "sad dog.png", HeldFileRetry.HeldReason));

        var shared = string.Join("\n", "Couldn't recycle the picture an Open with… edit replaced, finish an unfinished undo of an adoption or undo an unfinished undo of an adoption", "Ctrl+Alt+V is unavailable", "Open ImageStash unavailable");
        Assert.IsGreaterThan(127, shared.Length, "the platform would cut the oldest line, the open shortcut's");
        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Couldn't recycle the picture an Open with… edit replaced", "Ctrl+Alt+V is unavailable", "Open ImageStash unavailable" }, tray.ToolTip.Split('\n'),
            "the oldest other failures leave, and every unavailable shortcut is still named (G33)");
    }

    private static ShortcutProblem Held(ShortcutAct act, string combination) => new(act, combination, ShortcutReason.Of(ShortcutRefusal.HeldByAnotherProgram));

    [TestMethod]
    public void AShortcutNamedAgainAndAgainHasOneWholeLine()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        // Each of twenty registrations names it once more, as importing the same settings again does. It once kept twenty
        // lines; G33 "the tray icon says which shortcut is unavailable and why" holds one line for each shortcut
        // unavailable now: the tooltip is no history.
        for (var i = 0; i < 20; i++)
        {
            menu.NameUnavailableShortcut(Held(ShortcutAct.Screenshot, "Ctrl+PrtScn"));
        }

        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", tray.ToolTip, "one whole line");
    }

    [TestMethod]
    public void SettingsAppliedAgainHoldingAHeldShortcutLeaveItsOneWholeLine()
    {
        var started = StartedShortcuts.Start(held: Shortcut.Parse("Ctrl+PrtScn").Value!);

        started.Shortcuts.BindFromSettings();
        started.Shortcuts.BindFromSettings();

        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", started.Tray.ToolTip, "named once, whole (G33)");
    }

    [TestMethod]
    public void ALineARemapTookAwayReturnsWhenSettingsBringTheHeldShortcutBack()
    {
        var held = Shortcut.Parse("Ctrl+PrtScn").Value!;
        var started = StartedShortcuts.Start(held: held);
        using (started.Shortcuts.Suspend())
        {
            Assert.IsNull(started.Shortcuts.Rebind(ShortcutAct.Screenshot, Shortcut.Parse("Ctrl+Shift+F8").Value!));
        }

        Assert.AreEqual("ImageStash · Ctrl+Alt+Space", started.Tray.ToolTip, "the line gone with the remap");

        started.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Screenshot = "Ctrl+PrtScn" } });
        started.Shortcuts.BindFromSettings();

        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", started.Tray.ToolTip, "unavailable now, so named now (G33)");
    }

    [TestMethod]
    public void AShortcutTheLayoutComesToTypeIsNamedWithTheCharacterTheSeamFoundThen()
    {
        var started = StartedShortcuts.Start();
        started.Seam.Release(Shortcut.Parse("Ctrl+Shift+PrtScn").Value!, ShortcutRefusal.TypesACharacter("\u00FC"));
        started.RunPosted();

        Assert.AreEqual("Ctrl+Shift+PrtScn types \u00FC, so Record a clip has no shortcut.", started.Tray.ToolTip, "the character of the judgment that released it (G33)");
        Assert.AreEqual("Ctrl+Shift+PrtScn types \u00FC on your keyboard layout, so it can't be a shortcut.", started.Page().WarningOf(SettingsPage.RecordClipTitle));
    }

    [TestMethod]
    public void AShortcutRefusedAtPausesToggleBackReadsAsOneRefusedAtStart()
    {
        var screenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;
        var atStart = StartedShortcuts.Start(held: screenshot);
        var atToggleBack = StartedShortcuts.Start();
        atToggleBack.Shortcuts.TogglePause();
        atToggleBack.Seam.Refuse(screenshot, ShortcutRefusal.HeldByAnotherProgram);
        atToggleBack.Shortcuts.TogglePause();

        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", atStart.Tray.ToolTip);
        Assert.AreEqual(atStart.Tray.ToolTip, atToggleBack.Tray.ToolTip, "the words follow the reason, not the moment (G33)");
        Assert.AreEqual(atStart.Page().WarningOf(SettingsPage.TakeScreenshotTitle), atToggleBack.Page().WarningOf(SettingsPage.TakeScreenshotTitle));
    }

    private static GlobalShortcuts Shortcuts(string openText = "Ctrl+Alt+Space", string stashText = "Win+Alt+V", string screenshotText = "Ctrl+PrtScn", string recordText = "Ctrl+Shift+PrtScn") =>
        new(StartedShortcuts.SettingsInMemory(new Shortcuts { Open = openText, Screenshot = screenshotText, Record = recordText, StashClipboardImage = stashText }),
            GlobalShortcuts.Bindings(() => { }, () => { }, () => { }, () => { }), new RecordingShortcuts(), _ => { }, action => action());
}
