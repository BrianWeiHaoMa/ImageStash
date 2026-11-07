using Avalonia.Input;
using ImageStash.Capture;
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
        Assert.IsFalse(tray.Entries[4].Checked!.Value);
        tray.Entries[4].Chosen();
        Assert.IsTrue(shortcuts.Paused);
        Assert.IsTrue(tray.Entries[4].Checked!.Value);
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
    public void WithNothingToNameTheTooltipReadsImageStash()
    {
        var tray = new RecordingTray();
        new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }).Show(Shortcuts());

        Assert.AreEqual("ImageStash", tray.ToolTip);
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
        StringAssert.Contains(lines[0], "Start with Windows");
        StringAssert.Contains(lines[0], "Run entry ImageStash");
        StringAssert.Contains(lines[0], "refused");
        StringAssert.Contains(lines[1], "Read settings");
        StringAssert.Contains(lines[1], "settings.json");
        StringAssert.Contains(lines[1], "not valid settings");
    }

    [TestMethod]
    public void FailuresTooLongTogetherKeepTheNewestWholeAndTheOlderActionAndSubject()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        menu.Name(new Failure("Read settings", "settings.json", "another program is holding it open"));
        menu.Name(new Failure("Stop starting with Windows", "Run entry ImageStash", "Windows refused the change"));

        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        var lines = tray.ToolTip.Split('\n');
        Assert.HasCount(2, lines);
        Assert.AreEqual("Stop starting with Windows failed: Run entry ImageStash, Windows refused the change", lines[0]);
        Assert.AreEqual("Read settings failed: settings.json", lines[1]);
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
            menu.Name(new Failure(action, "a-rather-long-file-name-for-a-reaction.gif", "another program is holding it open"));
        }

        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(Enumerable.Reverse(actions).Select(a => $"{a} failed").ToArray(), tray.ToolTip.Split('\n'));
    }

    [TestMethod]
    [DataRow(ShortcutReasons.HeldByAnotherProgram)]
    [DataRow(ShortcutReasons.TypesACharacter)]
    public void AnUnavailableShortcutKeepsItsReasonWhileLaterFailuresAreShortened(string reason)
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Name(new Failure("Read settings", "settings.json", "another program is holding it open"));
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(new Failure("Register the open shortcut", "Ctrl+Alt+Space", reason));
        menu.Name(new Failure("Open the stash", @"C:\Users\someone\Pictures\Reaction stash", "a file is where the stash folder should be"));

        var lines = tray.ToolTip!.Split('\n');
        Assert.IsLessThanOrEqualTo(127, tray.ToolTip.Length);
        CollectionAssert.AreEqual(new[] { "Open the stash failed", $"Register the open shortcut failed: Ctrl+Alt+Space, {reason}", "Read settings failed" }, lines,
            "which shortcut is unavailable and why stays whole (G33); the others keep their action (G35)");
    }

    [TestMethod]
    public void AnUnavailableShortcutsReasonGivesWayOnlyWhenTheOtherFailuresActionsLeaveItNoRoom()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Name(new Failure("Stop starting with Windows", "Run entry ImageStash", "Windows refused the change"));
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(new Failure("Register the open shortcut", "Ctrl+Alt+Space", ShortcutReasons.HeldByAnotherProgram));
        menu.Name(new Failure("Open the stash", @"C:\Users\someone\Pictures\Reaction stash", "a file is where the stash folder should be"));

        var withTheReason = string.Join("\n", "Open the stash failed", $"Register the open shortcut failed: Ctrl+Alt+Space, {ShortcutReasons.HeldByAnotherProgram}", "Stop starting with Windows failed");
        Assert.IsGreaterThan(127, withTheReason.Length, "the reason does not fit beside the others' actions");
        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Open the stash failed", "Register the open shortcut failed: Ctrl+Alt+Space", "Stop starting with Windows failed" }, tray.ToolTip.Split('\n'),
            "the why gives way only when the tooltip cannot hold it beside every other failure named there, the shortcut still named (G33); every failure keeps its action (G35)");
    }

    [TestMethod]
    public void OfTwoUnavailableShortcutsTheOlderIsCutToItsActionBeforeTheNewerLosesItsReason()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(new Failure("Register the open shortcut", "Ctrl+Alt+Space", ShortcutReasons.HeldByAnotherProgram));
        menu.NameUnavailableShortcut(new Failure("Register the Stash clipboard image shortcut", "Ctrl+Alt+V", ShortcutReasons.HeldByAnotherProgram));

        var bothBySubject = string.Join("\n", "Register the Stash clipboard image shortcut failed: Ctrl+Alt+V", "Register the open shortcut failed: Ctrl+Alt+Space");
        Assert.IsLessThanOrEqualTo(127, bothBySubject.Length, "cutting both to action and subject would also fit, and lose the newer's why");
        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { $"Register the Stash clipboard image shortcut failed: Ctrl+Alt+V, {ShortcutReasons.HeldByAnotherProgram}", "Register the open shortcut failed" }, tray.ToolTip.Split('\n'),
            "the newest's why fits beside the older cut to its action, which still says which shortcut (G33)");
    }

    [TestMethod]
    public void TwoUnavailableShortcutsBesideOtherFailuresAreShortenedLastTheOlderFirstEachStillSayingWhich()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Name(new Failure("Read settings", "settings.json", "another program is holding it open"));
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(new Failure("Register the open shortcut", "Ctrl+Alt+Space", ShortcutReasons.HeldByAnotherProgram));
        menu.NameUnavailableShortcut(new Failure("Register the Stash clipboard image shortcut", "Ctrl+Alt+V", ShortcutReasons.TypesACharacter));

        var withTheReason = string.Join("\n", $"Register the Stash clipboard image shortcut failed: Ctrl+Alt+V, {ShortcutReasons.TypesACharacter}", "Register the open shortcut failed", "Read settings failed");
        Assert.IsGreaterThan(127, withTheReason.Length, "the newest's why does not fit beside the others cut to their actions");
        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Register the Stash clipboard image shortcut failed: Ctrl+Alt+V", "Register the open shortcut failed", "Read settings failed" }, tray.ToolTip.Split('\n'),
            "the other failure is cut to its action first, then the older shortcut, and only then the newest's why gives way (G33, G35)");

        menu.Name(new Failure("Open the stash", @"C:\Users\someone\Pictures\Reaction stash", "a file is where the stash folder should be"));

        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Open the stash failed", "Register the Stash clipboard image shortcut failed", "Register the open shortcut failed", "Read settings failed" }, tray.ToolTip.Split('\n'),
            "every line still there, each shortcut line still saying which shortcut, the newest's why given way only when nothing else can (G33)");
    }

    [TestMethod]
    public void AnUnavailableShortcutWhoseCombinationIsLongerThanItsActionKeepsItsActionWhenTheOthersAreCutToTheirs()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(new Failure("Register the open shortcut", "Ctrl+Alt+Shift+Win+PrintScreen", ShortcutReasons.HeldByAnotherProgram));
        menu.NameUnavailableShortcut(new Failure("Register the screenshot shortcut", "Ctrl+PrtScn", ShortcutReasons.HeldByAnotherProgram));
        menu.NameUnavailableShortcut(new Failure("Register the record shortcut", "Ctrl+Shift+PrtScn", ShortcutReasons.HeldByAnotherProgram));
        menu.NameUnavailableShortcut(new Failure("Register the Stash clipboard image shortcut", "Ctrl+Alt+V", ShortcutReasons.HeldByAnotherProgram));

        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+V is unavailable", "Ctrl+Shift+PrtScn is unavailable", "Ctrl+PrtScn is unavailable", "Register the open shortcut failed" }, tray.ToolTip.Split('\n'),
            "each line in its shorter form, so every shortcut is still named (G33)");
    }

    [TestMethod]
    public void WhenEvenTheActionsDoNotFitTheOldestOtherFailuresLeaveSoEveryUnavailableShortcutIsStillNamed()
    {
        var tray = new RecordingTray(toolTipLength: 127);
        var menu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        menu.Show(Shortcuts());
        menu.NameUnavailableShortcut(new Failure("Register the open shortcut", "Ctrl+Alt+Space", ShortcutReasons.HeldByAnotherProgram));
        menu.NameUnavailableShortcut(new Failure("Register the Stash clipboard image shortcut", "Ctrl+Alt+V", ShortcutReasons.HeldByAnotherProgram));
        menu.Name(new Failure("Finish an unfinished rename", "laughing cat.gif", "another program is holding it open"));
        menu.Name(new Failure("Finish an unfinished rename", "sad dog.png", "another program is holding it open"));

        var everyAction = string.Join("\n", "Finish an unfinished rename failed", "Finish an unfinished rename failed", "Register the Stash clipboard image shortcut failed", "Register the open shortcut failed");
        Assert.IsGreaterThan(127, everyAction.Length, "the platform would cut the oldest line, the open shortcut's");
        Assert.IsLessThanOrEqualTo(127, tray.ToolTip!.Length);
        CollectionAssert.AreEqual(new[] { "Finish an unfinished rename failed", "Register the Stash clipboard image shortcut failed", "Register the open shortcut failed" }, tray.ToolTip.Split('\n'),
            "the oldest other failure leaves, and every unavailable shortcut is still named (G33)");
    }

    private static GlobalShortcuts Shortcuts(string openText = "Ctrl+Alt+Space", string stashText = "Ctrl+Alt+V", string screenshotText = "Ctrl+PrtScn", string recordText = "Ctrl+Shift+PrtScn") =>
        new(StartedShortcuts.SettingsInMemory(new Shortcuts { Open = openText, Screenshot = screenshotText, Record = recordText, StashClipboardImage = stashText }),
            GlobalShortcuts.Bindings(() => { }, () => { }, () => { }, () => { }), new RecordingShortcuts(), _ => { }, action => action());
}
