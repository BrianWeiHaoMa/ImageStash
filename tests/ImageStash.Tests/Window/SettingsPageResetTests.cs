using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// A shortcut row's Reset (F5, G32): offered exactly while the row's shortcut is not F5's default, it sets that default
/// as a recorded press of it would be, under every refusal of G32: taken, it is registered, written and shown wherever a
/// shortcut is; refused, the row says why in a recorded refusal's words and the act keeps its shortcut. However it ends,
/// no field records and every shortcut due is registered (G33).
/// </summary>
[TestClass]
public sealed class SettingsPageResetTests
{
    private const int ScreenshotEntry = 1;
    private static readonly Shortcut TakeScreenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;
    private static readonly Shortcut AltF9 = Shortcut.Parse("Alt+F9").Value!;

    [TestMethod]
    public void ResetIsOfferedExactlyWhileTheShortcutIsNotF5sDefaultFollowingEveryChange()
    {
        var started = StartedShortcuts.Start();
        var page = started.Page();
        var changes = 0;
        page.Changed += () => changes++;

        foreach (var field in page.ShortcutFields)
        {
            Assert.IsFalse(field.ResetOffered, $"{field.Title}: on its default, no Reset");
        }

        var screenshot = page.ShortcutField(ShortcutAct.Screenshot);
        screenshot.Begin();
        screenshot.Press(Key.F8, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.IsTrue(screenshot.ResetOffered, "a remap brings Reset");

        screenshot.Reset();
        Assert.IsFalse(screenshot.ResetOffered, "and its own effect takes it away");

        started.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Record = "Alt+F7", Open = "Ctrl+Nope", StashClipboardImage = "" } });
        changes = 0;
        started.Shortcuts.BindFromSettings();

        Assert.IsGreaterThan(0, changes, "the open page told of an import's settings");
        Assert.IsTrue(page.ShortcutField(ShortcutAct.Record).ResetOffered, "an import's shortcut, the page still open");
        Assert.IsTrue(page.ShortcutField(ShortcutAct.Open).ResetOffered, "a setting that is no shortcut");
        Assert.IsTrue(page.ShortcutField(ShortcutAct.StashClipboardImage).ResetOffered, "an empty setting");
        Assert.IsFalse(screenshot.ResetOffered);
    }

    [TestMethod]
    public void ResetOfferedByTheCombinationNotItsSpelling()
    {
        var started = StartedShortcuts.Start(open: "Alt+Ctrl+Space");

        Assert.IsFalse(started.Page().ShortcutField(ShortcutAct.Open).ResetOffered, "the default in another spelling is the default");
    }

    [TestMethod]
    public void ResetPutsTheDefaultBackRegisteredWrittenAndShownEverywhere()
    {
        using var folder = new ScratchFolder();
        var started = StartedShortcuts.Start(screenshot: "Alt+F9", settingsFolder: folder.Path, held: AltF9);
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);
        Assert.AreEqual("Alt+F9 is used by another app, so Take a screenshot has no shortcut.", started.Tray.ToolTip);

        field.Reset();

        Assert.IsTrue(started.Seam.Press(TakeScreenshot), "registered (G33)");
        started.RunPosted();
        Assert.AreEqual(1, started.Screenshots);
        Assert.AreEqual("Ctrl+PrtScn", Written(started).Screenshot, "written");
        Assert.AreEqual(TakeScreenshot.Gesture, started.Tray.Entries[ScreenshotEntry].Gesture, "the tray entry shows it (G34)");
        Assert.AreEqual("ImageStash · Ctrl+Alt+Space", started.Tray.ToolTip, "its line in the tooltip gone");
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Screenshot));
        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle));
        Assert.IsFalse(field.ResetOffered, "Reset leaves the row");
        Assert.IsFalse(field.IsRecording);
        Assert.HasCount(4, started.Seam.Registered, "every shortcut registered");
    }

    [TestMethod]
    public void ResetOfASettingThatIsNoShortcutRegistersTheDefault()
    {
        var started = StartedShortcuts.Start(record: "Ctrl+Nope");
        var field = started.Page().ShortcutField(ShortcutAct.Record);

        field.Reset();

        Assert.IsTrue(started.Seam.Press(Shortcut.Parse("Ctrl+Shift+PrtScn").Value!));
        Assert.AreEqual("Ctrl+Shift+PrtScn", started.Settings.Current.Shortcuts.Record);
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Record));
        Assert.IsFalse(field.ResetOffered);
    }

    [TestMethod]
    [DataRow("held", "Ctrl+PrtScn is already used by another app. Press a different combination.")]
    [DataRow("types", "Ctrl+PrtScn types ¤ on your keyboard layout, so it can't be a shortcut.")]
    [DataRow("used", "Open ImageStash already uses Ctrl+PrtScn.")]
    public void AResetRefusedSaysWhyAndTheActKeepsItsShortcutRegisteredAndWritten(string refusal, string words)
    {
        using var folder = new ScratchFolder();
        var started = StartedShortcuts.Start(open: refusal == "used" ? "Ctrl+PrtScn" : null, screenshot: "Alt+F9", settingsFolder: folder.Path);
        switch (refusal)
        {
            case "held":
                started.Seam.Refuse(TakeScreenshot, ShortcutRefusal.HeldByAnotherProgram);
                break;
            case "types":
                started.Seam.Refuse(TakeScreenshot, ShortcutRefusal.TypesACharacter("¤"));
                break;
        }

        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);

        field.Reset();

        Assert.AreEqual(words, page.WarningOf(SettingsPage.TakeScreenshotTitle), "in the words a recorded refusal gets (G32, G35)");
        Assert.IsTrue(started.Seam.Press(AltF9), "the shortcut the row had registered again");
        started.RunPosted();
        Assert.AreEqual(1, started.Screenshots);
        Assert.AreEqual("Alt+F9", Written(started).Screenshot, "and still written");
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Screenshot), "a refusal makes nothing unavailable");
        Assert.IsTrue(field.ResetOffered, "Reset still offered");
        Assert.IsFalse(field.IsRecording, "no field records");
        Assert.HasCount(4, started.Seam.Registered, "every shortcut due registered");
    }

    [TestMethod]
    public void AResetsRefusalGoesWhenTheRowIsAskedAgainOrTheDefaultIsTaken()
    {
        var started = StartedShortcuts.Start(screenshot: "Alt+F9", held: TakeScreenshot);
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);

        field.Reset();
        Assert.IsNotNull(page.WarningOf(SettingsPage.TakeScreenshotTitle));
        field.Begin();
        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle), "a new recording asks afresh");
        field.Cancel();

        field.Reset();
        started.Seam.Free(TakeScreenshot);
        field.Reset();

        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle), "taken, nothing to say");
        Assert.IsTrue(started.Seam.Press(TakeScreenshot));
    }

    [TestMethod]
    public void AResetsRefusalGoesOnceTheShortcutsChangeSoTheRowNeverSaysWhatNoLongerHolds()
    {
        var started = StartedShortcuts.Start(open: "Ctrl+PrtScn", screenshot: "Alt+F9");
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);

        field.Reset();
        Assert.AreEqual("Open ImageStash already uses Ctrl+PrtScn.", page.WarningOf(SettingsPage.TakeScreenshotTitle));

        // An import's settings give Open its default back, the screenshot shortcut kept (G36).
        started.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Open = "Ctrl+Alt+Space" } });
        started.Shortcuts.BindFromSettings();

        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle), "the refusal no longer holds, so the row says nothing (G35)");
        Assert.IsTrue(field.ResetOffered);

        field.Reset();
        Assert.IsFalse(field.ResetOffered, "and Reset now takes the default");
    }

    [TestMethod]
    public void ResetUnderPauseIsWrittenAndRegisteredOnlyAtTheToggleBack()
    {
        var started = StartedShortcuts.Start(screenshot: "Alt+F9");
        var page = started.Page();
        started.Shortcuts.TogglePause();

        page.ShortcutField(ShortcutAct.Screenshot).Reset();

        Assert.AreEqual("Ctrl+PrtScn", started.Settings.Current.Shortcuts.Screenshot, "written");
        Assert.IsEmpty(started.Seam.Registered, "left released while paused (G33)");
        Assert.AreEqual(TakeScreenshot.Gesture, started.Tray.Entries[ScreenshotEntry].Gesture, "shown");

        started.Shortcuts.TogglePause();

        Assert.IsTrue(started.Seam.Press(TakeScreenshot), "registered at the toggle back");
        Assert.IsFalse(started.Seam.Press(AltF9), "the old one gone");
        Assert.HasCount(4, started.Seam.Registered);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "the default taken")]
    [DataRow(true, DisplayName = "the default refused")]
    public void ResetWhileAnotherFieldRecordsEndsThatRecordingAndRegistersEveryShortcut(bool held)
    {
        var started = StartedShortcuts.Start(screenshot: "Alt+F9");
        if (held)
        {
            started.Seam.Refuse(TakeScreenshot, ShortcutRefusal.HeldByAnotherProgram);
        }

        var page = started.Page();
        var open = page.ShortcutField(ShortcutAct.Open);
        open.Begin();
        Assert.IsEmpty(started.Seam.Registered);

        page.ShortcutField(ShortcutAct.Screenshot).Reset();

        Assert.IsFalse(open.IsRecording, "the other recording ended first (G32: one at a time)");
        Assert.IsTrue(page.ShortcutFields.All(field => !field.IsRecording), "no field records");
        Assert.AreEqual("Ctrl+Alt+Space", started.Shortcuts.TextOf(ShortcutAct.Open), "the Reset was not read as the other field's press");
        Assert.IsNull(page.WarningOf(SettingsPage.OpenTitle));
        Assert.HasCount(4, started.Seam.Registered, "every shortcut due registered again (G33)");
        Assert.IsTrue(started.Seam.Press(Shortcut.Parse("Ctrl+Alt+Space").Value!));
        Assert.IsTrue(started.Seam.Press(held ? AltF9 : TakeScreenshot));
    }

    /// <summary>The shortcuts the settings file holds once every change so far is written.</summary>
    private static ImageStash.Store.Shortcuts Written(StartedShortcuts started)
    {
        started.Settings.Flush().Wait();
        return started.Saved;
    }
}
