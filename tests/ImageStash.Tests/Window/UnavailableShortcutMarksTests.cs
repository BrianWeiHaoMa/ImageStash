using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Capture;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Wherever a shortcut is shown beside its act, the tray menu's entries, the <c>+</c> menu's captures and the empty
/// stash's rows and its line on opening ImageStash, an unavailable one is still shown and said to be unavailable, by the
/// policy's one judgment, from the start, where no change is raised, until it works again (G2, G33, G34); the entry or
/// row still runs its act; Pause, which makes no shortcut unavailable, marks nothing.
/// </summary>
[TestClass]
public sealed class UnavailableShortcutMarksTests
{
    private static readonly Shortcut TakeScreenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;
    private static readonly Shortcut RecordClip = Shortcut.Parse("Ctrl+Shift+PrtScn").Value!;
    private static readonly Shortcut Open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
    private static readonly Shortcut Free = Shortcut.Parse("Ctrl+Shift+F8").Value!;

    [TestMethod]
    public void AnEntryHeldFromTheStartIsMarkedBesideItsShortcutAndStillRunsItsAct()
    {
        var started = StartedShortcuts.Start(held: TakeScreenshot);
        var tray = started.Tray;

        Assert.AreEqual("Take a screenshot (shortcut unavailable)", tray.HeaderOf(TrayMenu.TakeScreenshotHeader), "marked from the start (G33)");
        Assert.AreEqual(TakeScreenshot.Gesture, tray.Entries[1].Gesture, "its shortcut still shown (G34)");
        CollectionAssert.AreEqual(new[] { "Open ImageStash", "Record a clip", "Stash clipboard image" },
            new[] { TrayMenu.OpenHeader, TrayMenu.RecordClipHeader, TrayMenu.StashClipboardImageHeader }.Select(tray.HeaderOf).ToList(), "only that one");

        tray.Entries[1].Chosen();
        Assert.AreEqual(1, started.ScreenshotsFromTray, "the entry still takes a screenshot (G33)");
    }

    [TestMethod]
    public void TheMarkGoesWithARemapAndComesBackWithSettingsThatBringTheHeldShortcutBack()
    {
        var started = StartedShortcuts.Start(held: TakeScreenshot);

        using (started.Shortcuts.Suspend())
        {
            Assert.IsNull(started.Shortcuts.Rebind(ShortcutAct.Screenshot, Free));
        }

        Assert.AreEqual("Take a screenshot", started.Tray.HeaderOf(TrayMenu.TakeScreenshotHeader), "it works: no mark");
        Assert.AreEqual(Free.Gesture, started.Tray.Entries[1].Gesture);

        started.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Screenshot = "Ctrl+PrtScn" } });
        started.Shortcuts.BindFromSettings();

        Assert.AreEqual("Take a screenshot (shortcut unavailable)", started.Tray.HeaderOf(TrayMenu.TakeScreenshotHeader), "unavailable again: marked again");
    }

    [TestMethod]
    public void AShortcutTheLayoutComesToTypeIsMarked()
    {
        var started = StartedShortcuts.Start();
        started.Seam.Release(Open, ShortcutRefusal.TypesACharacter("@"));
        started.RunPosted();

        Assert.AreEqual("Open ImageStash (shortcut unavailable)", started.Tray.HeaderOf(TrayMenu.OpenHeader));
    }

    [TestMethod]
    public void RecordAClipMarkedStillReadsStartRecordingAndStopRecording()
    {
        var started = StartedShortcuts.Start(held: RecordClip);
        var tray = started.Tray;

        started.Menu.SetRecording(RecordingStage.Waiting);
        Assert.AreEqual("Start recording (shortcut unavailable)", tray.HeaderOf(TrayMenu.RecordClipHeader), "G34's words while the region waits, marked");
        started.Menu.SetRecording(RecordingStage.Sampling);
        Assert.AreEqual("Stop recording (shortcut unavailable)", tray.HeaderOf(TrayMenu.RecordClipHeader));
        started.Menu.SetRecording(RecordingStage.None);
        Assert.AreEqual("Record a clip (shortcut unavailable)", tray.HeaderOf(TrayMenu.RecordClipHeader));
    }

    [TestMethod]
    public void PauseMarksNothingAndAHeldShortcutStaysMarkedThroughIt()
    {
        var free = StartedShortcuts.Start();
        using (free.Shortcuts.Suspend())
        {
            Assert.IsEmpty(free.Tray.HeaderChanges, "released while a field records, none is unavailable");
        }

        free.Shortcuts.TogglePause();
        Assert.IsEmpty(free.Tray.HeaderChanges, "paused, no shortcut is unavailable (G33)");

        var held = StartedShortcuts.Start(held: TakeScreenshot);
        held.Shortcuts.TogglePause();
        Assert.AreEqual("Take a screenshot (shortcut unavailable)", held.Tray.HeaderOf(TrayMenu.TakeScreenshotHeader), "paused or not");
        Assert.AreEqual("Open ImageStash", held.Tray.HeaderOf(TrayMenu.OpenHeader), "and only that one");
    }

    [TestMethod]
    public async Task TheEmptyStashSaysAHeldShortcutIsUnavailableBesideItsCapsFromTheStartUntilItWorks()
    {
        using var walked = await StashWithTray.Open(held: TakeScreenshot);
        var shown = EmptyStashShown.Over(walked.Listed);

        var screenshot = shown.ReadRows().Single(row => row.Words == "Take a screenshot");
        CollectionAssert.AreEqual(new[] { "Ctrl", "PrtScn" }, screenshot.Caps.ToList(), "the shortcut still shown (G2)");
        Assert.AreEqual(ShortcutWords.UnavailableBesideKeys, screenshot.Beside, "and said to be unavailable beside it (G33)");
        Assert.IsTrue(shown.ReadRows().Where(row => row.Words != "Take a screenshot").All(row => row.Beside is null), "only that one");

        using (walked.Listed.Shortcuts.Suspend())
        {
            Assert.IsNull(walked.Listed.Shortcuts.Rebind(ShortcutAct.Screenshot, Free));
        }

        Assert.IsNull(shown.ReadRows().Single(row => row.Words == "Take a screenshot").Beside, "at once, it works");
    }

    [TestMethod]
    public async Task TheEmptyStashsLineNeverOffersAnUnavailableOpenShortcut()
    {
        using var walked = await StashWithTray.Open(held: Open);
        var shown = EmptyStashShown.Over(walked.Listed);

        Assert.AreEqual("Open ImageStash from its icon in the tray (Ctrl+Alt+Space is unavailable). Closing this window keeps it running.", shown.ReadLines()[^1],
            "the shortcut named as unavailable, the icon's way offered (G2, G3)");
    }

    [TestMethod]
    public async Task TheEmptyStashMarksNothingUnderPause()
    {
        using var walked = await StashWithTray.Open();
        var shown = EmptyStashShown.Over(walked.Listed);

        walked.Listed.Shortcuts.TogglePause();

        Assert.IsTrue(shown.ReadRows().All(row => row.Beside is null), "paused is not unavailable");
        Assert.DoesNotContain(ShortcutWords.UnavailableBesideKeys, shown.ReadLines()[^1], "nor does the line say unavailable");
    }

    [TestMethod]
    public async Task ThePlusMenusCapturesAreMarkedBesideTheirShortcuts()
    {
        using var walked = await StashWithTray.Open(held: RecordClip);
        var listed = walked.Listed;

        var entries = PlusMenu(listed);

        Assert.AreEqual(("Take a screenshot", TakeScreenshot.Gesture), entries[2]);
        Assert.AreEqual(("Record a clip (shortcut unavailable)", RecordClip.Gesture), entries[3], "its shortcut shown and said to be unavailable (G33)");
    }

    /// <summary>The <c>+</c> menu as the window makes it over the listed stash's shortcuts: each entry's words and shortcut.</summary>
    private static List<(string Header, KeyGesture? Gesture)> PlusMenu(ListedStash listed)
    {
        List<(string, KeyGesture?)> entries = [];
        ControlThread.Run(() =>
        {
            var button = AddMenu.Button(listed.Settings, act => listed.Shortcuts.UnavailableReason(act) is not null, listed.Paste, () => { }, () => { }, () => { });
            entries = ((MenuFlyout)button.Flyout!).Items.OfType<MenuItem>().Select(entry => ((string)entry.Header!, entry.InputGesture)).ToList();
        });
        return entries;
    }
}
