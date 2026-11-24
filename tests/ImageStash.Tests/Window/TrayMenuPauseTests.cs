using Avalonia.Input;
using ImageStash.Capture;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

[TestClass]
public sealed class TrayMenuPauseTests
{
    private const string PausedTip = "ImageStash · shortcuts paused";
    private static readonly Shortcut Screenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;

    [TestMethod]
    public void IconFollowsStartupFailurePauseRecordingAndRemapInPriorityOrder()
    {
        var started = StartedShortcuts.Start(held: [Screenshot]);
        var tray = started.Tray;
        var menu = started.Menu;
        Assert.AreEqual(TrayIconState.Unavailable, tray.Icon, "even the refusal at startup, where Changed is not raised");

        var pause = tray.Entries.Single(entry => entry.Header == TrayMenu.PauseHeader).Chosen;
        pause();
        Assert.AreEqual(TrayIconState.Paused, tray.Icon);
        StringAssert.Contains(tray.ToolTip, "used by another app", "the unavailability remains in the tooltip");
        menu.SetRecording(RecordingStage.Waiting);
        Assert.AreEqual(TrayIconState.Paused, tray.Icon, "waiting has no red dot");
        menu.SetRecording(RecordingStage.Sampling);
        Assert.AreEqual(TrayIconState.Recording, tray.Icon);
        pause();
        Assert.AreEqual(TrayIconState.Recording, tray.Icon, "Resume during a recording keeps its dot");
        menu.SetRecording(RecordingStage.None);
        Assert.AreEqual(TrayIconState.Unavailable, tray.Icon);

        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);
        field.Begin();
        field.Press(Key.F8, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.AreEqual(TrayIconState.Plain, tray.Icon, "the remap cleared the last unavailable shortcut");
        pause();
        Assert.AreEqual(TrayIconState.Paused, tray.Icon);
        Assert.IsNull(started.Shortcuts.FirstUnavailable, "Pause alone makes nothing unavailable");
        pause();
        Assert.AreEqual(TrayIconState.Plain, tray.Icon);
        page.Close();
    }

    [TestMethod]
    public void ALayoutReleaseInUseUpdatesTheIconWhenThePolicyHearsOfIt()
    {
        var started = StartedShortcuts.Start();
        Assert.AreEqual(TrayIconState.Plain, started.Tray.Icon);

        started.Seam.Release(Screenshot, ShortcutRefusal.TypesACharacter("@"));
        started.RunPosted();

        Assert.AreEqual(TrayIconState.Unavailable, started.Tray.Icon);
        StringAssert.Contains(started.Tray.ToolTip, "@");
    }

    [TestMethod]
    public void ReapplyingTheSameShortcutWhilePausedKeepsItsKnownReason()
    {
        var started = StartedShortcuts.Start(held: [Screenshot]);
        var reason = started.Tray.ToolTip;
        started.Shortcuts.TogglePause();
        started.Shortcuts.BindFromSettings();

        Assert.AreEqual($"{PausedTip}\n{reason}", started.Tray.ToolTip, "Pause prevents a new registration from judging the held combination");
        Assert.IsEmpty(started.Seam.Registered);
        Assert.AreEqual(ShortcutAct.Screenshot, started.Shortcuts.FirstUnavailable);
    }

    [TestMethod]
    public void AnImportedConflictTakesPrecedenceOverTheOldRegistrationRefusal()
    {
        var started = StartedShortcuts.Start(held: [Screenshot]);
        started.Shortcuts.TogglePause();
        started.Seam.Free(Screenshot);
        started.Settings.Change(settings => settings with { Shortcuts = settings.Shortcuts with { Open = "Ctrl+PrtScn" } });

        started.Shortcuts.BindFromSettings();
        started.Shortcuts.TogglePause();

        Assert.IsTrue(started.Seam.Press(Screenshot), "Open ImageStash registered the combination");
        Assert.AreEqual(1, started.Summoned);
        var conflict = started.Shortcuts.UnavailableReason(ShortcutAct.Screenshot);
        Assert.AreEqual(ShortcutReasonKind.UsedByAnother, conflict?.Reason.Kind, "the imported local conflict supersedes the old external refusal");
        Assert.AreEqual(ShortcutAct.Open, conflict?.Reason.Other);
    }

    [TestMethod]
    public void PausedWordsKeepTheOtherFailureAndTheShortcutReasonAsSpaceAllows()
    {
        var started = StartedShortcuts.Start(held: [Screenshot]);
        var unavailable = started.Tray.ToolTip!;
        var fits = new Failure("Read", "a", "held");
        started.Menu.Name(fits);
        started.Shortcuts.TogglePause();
        Assert.AreEqual($"{PausedTip}\n{fits.Sentence}\n{unavailable}", started.Tray.ToolTip);

        var longFailure = new Failure("Save", "Settings", "the folder was held open");
        var full = StartedShortcuts.Start(held: [Screenshot]);
        full.Menu.Name(longFailure);
        var withoutPause = full.Tray.ToolTip;
        StringAssert.Contains(withoutPause, unavailable, "the shortcut's whole reason fits without the paused words");
        StringAssert.Contains(withoutPause, longFailure.Sentence, "the other whole failure also fits");

        full.Shortcuts.TogglePause();
        Assert.AreEqual($"{PausedTip}\nSave failed: Settings\n{unavailable}", full.Tray.ToolTip,
            "the other failure shortens and the shortcut keeps its full reason");
    }

    [TestMethod]
    public void PauseIsNamedWithBothHeldCaptureDefaultsAndResumeKeepsTheirConflicts()
    {
        var started = StartedShortcuts.Start(held: [Screenshot, Shortcut.Parse("Ctrl+Shift+PrtScn").Value!]);
        var running = started.Tray.ToolTip;
        var pause = started.Tray.Entries.Single(entry => entry.Header == TrayMenu.PauseHeader).Chosen;

        pause();

        var paused = started.Tray.ToolTip!;
        StringAssert.StartsWith(paused, $"{PausedTip}\n");
        Assert.IsLessThanOrEqualTo(127, paused.Length);
        StringAssert.Contains(paused, "Ctrl+PrtScn is used by another app");
        StringAssert.Contains(paused, "Ctrl+Shift+PrtScn is used by another app");

        pause();

        Assert.AreEqual(running, started.Tray.ToolTip, "Resume clears the paused words and keeps the conflicts");
    }

    [TestMethod]
    public void PauseAndEveryUnavailableShortcutFitWhenAllFourShortcutsAreLong()
    {
        var bindings = new[] { "Ctrl+Alt+Shift+Win+PrtScn", "Ctrl+Alt+Shift+Win+F9", "Ctrl+Alt+Shift+Win+F10", "Ctrl+Alt+Shift+Win+F11" };
        var started = StartedShortcuts.Start(open: bindings[0], screenshot: bindings[1], record: bindings[2], stash: bindings[3],
            held: bindings.Select(binding => Shortcut.Parse(binding).Value!).ToArray());
        started.Menu.Name(new Failure("Open the stash", "stash folder", "access denied"));

        started.Tray.Entries.Single(entry => entry.Header == TrayMenu.PauseHeader).Chosen();

        var paused = started.Tray.ToolTip!;
        StringAssert.StartsWith(paused, $"{PausedTip}\n");
        Assert.IsLessThanOrEqualTo(127, paused.Length);
        StringAssert.Contains(paused, "Unavailable: ");
        foreach (var act in new[] { "Open ImageStash", "Take a screenshot", "Record a clip", "Stash clipboard image" })
        {
            StringAssert.Contains(paused, act);
        }
    }

    [TestMethod]
    public void ThePausedTooltipKeepsTheFailedActionBesideFourShorterBindings()
    {
        var started = StartedShortcuts.Start(held:
            [Shortcut.Parse("Ctrl+Alt+Space").Value!, Screenshot, Shortcut.Parse("Ctrl+Shift+PrtScn").Value!, Shortcut.Parse("Win+Alt+V").Value!]);
        started.Menu.Name(new Failure("Open the stash", "stash folder", "access denied"));

        started.Tray.Entries.Single(entry => entry.Header == TrayMenu.PauseHeader).Chosen();

        var paused = started.Tray.ToolTip!;
        StringAssert.StartsWith(paused, $"{PausedTip}\n");
        Assert.IsLessThanOrEqualTo(127, paused.Length);
        StringAssert.Contains(paused, "Open the stash failed");
        StringAssert.Contains(paused, "Unavailable: Win+Alt+V, Record a clip, Ctrl+PrtScn, Ctrl+Alt+Space");
    }

    [TestMethod]
    public void ThePausedTooltipKeepsBothReasonsBesideTheFailedAction()
    {
        var started = StartedShortcuts.Start(screenshot: "Ctrl+Alt+Q", held: Shortcut.Parse("Ctrl+Shift+PrtScn").Value!);
        started.Seam.Release(Shortcut.Parse("Ctrl+Alt+Q").Value!, ShortcutRefusal.TypesACharacter("@"));
        started.RunPosted();
        started.Menu.Name(new Failure("Stop starting with Windows", "Run entry", "access denied"));

        started.Tray.Entries.Single(entry => entry.Header == TrayMenu.PauseHeader).Chosen();

        var paused = started.Tray.ToolTip!;
        Assert.IsLessThanOrEqualTo(127, paused.Length);
        StringAssert.Contains(paused, "Ctrl+Alt+Q types @");
        StringAssert.Contains(paused, "Ctrl+Shift+PrtScn is used by another app");
        StringAssert.Contains(paused, "Stop starting with Windows failed");
    }

    [TestMethod]
    public void ThePausedTooltipNamesTheActionWithAnImportedDuplicateBinding()
    {
        var shared = Shortcut.Parse("Ctrl+F4").Value!;
        var record = Shortcut.Parse("Ctrl+Alt+Q").Value!;
        var started = StartedShortcuts.Start(open: shared.Text, stash: shared.Text, record: record.Text,
            held: [shared, Screenshot]);
        started.Seam.Release(record, ShortcutRefusal.TypesACharacter("@"));
        started.RunPosted();

        started.Tray.Entries.Single(entry => entry.Header == TrayMenu.PauseHeader).Chosen();

        var paused = started.Tray.ToolTip!;
        StringAssert.StartsWith(paused, $"{PausedTip}\n");
        Assert.IsLessThanOrEqualTo(127, paused.Length);
        StringAssert.Contains(paused, "Stash clipboard image", "the imported duplicate needs its action name to distinguish it from the other binding");
        StringAssert.Contains(paused, "Ctrl+F4");
        StringAssert.Contains(paused, "Ctrl+PrtScn");
        StringAssert.Contains(paused, "Ctrl+Alt+Q types @");
    }
}
