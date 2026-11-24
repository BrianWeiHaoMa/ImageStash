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
    public void PausedWordsJoinWholeFailuresWhenTheyFitAndGiveWayBeforeAnyReasonDoes()
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
        Assert.AreEqual(withoutPause, full.Tray.ToolTip, "the paused words give way before either reason");
    }
}
