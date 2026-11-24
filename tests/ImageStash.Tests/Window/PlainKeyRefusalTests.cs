using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// A Settings field refuses a key every app needs (G32): a key pressed with none of Ctrl, Alt and the Windows key held,
/// Shift admitting none, unless it is PrtScn, Pause, Scroll Lock or one of F13 to F24; on the spot, the seam never asked
/// for it, nothing written, the act keeping its shortcut, the field still recording, and nobody told but the row. The
/// rule is the field's alone: a plain key an earlier build or an import stored registers as before (G31, G33).
/// </summary>
[TestClass]
public sealed class PlainKeyRefusalTests
{
    private static readonly Shortcut TakeScreenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;

    [TestMethod]
    [DataRow("Delete", "Add Ctrl, Alt or Win: Delete alone is needed by every app.")]
    [DataRow("F2", "Add Ctrl, Alt or Win: F2 alone is needed by every app.")]
    [DataRow("Left", "Add Ctrl, Alt or Win: Left alone is needed by every app.")]
    [DataRow("Home", "Add Ctrl, Alt or Win: Home alone is needed by every app.")]
    [DataRow("End", "Add Ctrl, Alt or Win: End alone is needed by every app.")]
    [DataRow("Insert", "Add Ctrl, Alt or Win: Insert alone is needed by every app.")]
    [DataRow("PageUp", "Add Ctrl, Alt or Win: PageUp alone is needed by every app.")]
    [DataRow("F1", "Add Ctrl, Alt or Win: F1 alone is needed by every app.")]
    [DataRow("F12", "Add Ctrl, Alt or Win: F12 alone is needed by every app.")]
    [DataRow("Space", "Add Ctrl, Alt or Win: Space alone is needed by every app.")]
    [DataRow("Shift+Delete", "Add Ctrl, Alt or Win: Shift+Delete is needed by every app.")]
    [DataRow("Shift+F2", "Add Ctrl, Alt or Win: Shift+F2 is needed by every app.")]
    [DataRow("Shift+F5", "Add Ctrl, Alt or Win: Shift+F5 is needed by every app.")]
    [DataRow("A", "Add Ctrl, Alt or Win: A alone is needed by every app.")]
    [DataRow("Shift+A", "Add Ctrl, Alt or Win: Shift+A is needed by every app.")]
    public void AKeyEveryAppNeedsIsRefusedOnTheSpotBeforeTheSeamIsAsked(string pressed, string words)
    {
        using var folder = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: folder.Path);
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);
        var toolTip = started.Tray.ToolTip;
        var changes = 0;
        started.Shortcuts.Changed += () => changes++;
        field.Begin();
        var asked = started.Seam.Asked.Count;

        Press(field, pressed);

        Assert.HasCount(asked, started.Seam.Asked, "the seam is never asked to register it, not even for an instant (G32)");
        Assert.AreEqual(words, page.WarningOf(SettingsPage.TakeScreenshotTitle), "the row names the key and what to add (G32, G35)");
        Assert.IsTrue(field.IsRecording, "the field still records, so the next press is the retry");
        Assert.IsEmpty(started.Seam.Registered, "every shortcut still released while it records");
        Assert.AreEqual("Ctrl+PrtScn", started.Shortcuts.TextOf(ShortcutAct.Screenshot), "the act keeps its shortcut");
        Assert.AreEqual("Ctrl+PrtScn", Written(started).Screenshot, "nothing written");
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Screenshot), "nothing made unavailable");
        Assert.AreEqual(toolTip, started.Tray.ToolTip, "told to nobody but the row");
        Assert.AreEqual(0, changes, "no change raised");

        field.Cancel();

        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle), "Esc leaves the row saying only what is still true");
        Assert.IsTrue(started.Seam.Press(TakeScreenshot), "the shortcut it had registered again");
        Assert.HasCount(4, started.Seam.Registered);
    }

    [TestMethod]
    [DataRow("Ctrl+Delete")]
    [DataRow("Alt+F2")]
    [DataRow("Win+Left")]
    [DataRow("Ctrl+Shift+F8")]
    [DataRow("PrtScn")]
    [DataRow("Shift+PrtScn")]
    [DataRow("Pause")]
    [DataRow("Scroll")]
    [DataRow("F13")]
    [DataRow("Shift+F17")]
    [DataRow("F24")]
    public void AKeyNoAppNeedsOrOneHeldWithCtrlAltOrWinIsTakenAsBefore(string pressed)
    {
        using var folder = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: folder.Path);
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);
        field.Begin();

        Press(field, pressed);

        var taken = Shortcut.Parse(pressed).Value!;
        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle), "nothing refused it");
        Assert.IsFalse(field.IsRecording, "taken, it ends the recording");
        Assert.AreEqual(taken.Text, Written(started).Screenshot, "written");
        Assert.IsTrue(started.Seam.Press(taken), "registered");
        started.RunPosted();
        Assert.AreEqual(1, started.Screenshots);
    }

    [TestMethod]
    public void EachOfF5sDefaultsIsTakenByItsOwnField()
    {
        var started = StartedShortcuts.Start(open: "Alt+F1", screenshot: "Alt+F2", record: "Alt+F3", stash: "Alt+F4");
        var page = started.Page();
        var defaults = new ImageStash.Store.Shortcuts();

        foreach (var act in Enum.GetValues<ShortcutAct>())
        {
            var field = page.ShortcutField(act);
            field.Begin();
            Press(field, GlobalShortcuts.TextIn(defaults, act));

            Assert.IsFalse(field.IsRecording, $"{act}: F5's default taken");
            Assert.AreEqual(GlobalShortcuts.TextIn(defaults, act), started.Shortcuts.TextOf(act));
        }

        Assert.HasCount(4, started.Seam.Registered);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "taken as a press")]
    [DataRow(true, DisplayName = "a modifier let go first")]
    public void APressSeenOnlyComingUpIsRefusedBeforeTheSeamIsAskedForThePlainKey(bool letGoFirst)
    {
        using var folder = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: folder.Path);
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);
        field.Begin();

        field.PressSeenComingUp(Key.Delete, [KeyModifiers.None, KeyModifiers.Shift, KeyModifiers.Control], letGoFirst);

        Assert.DoesNotContain(Shortcut.Parse("Delete").Value!, started.Seam.Asked, "the plain key never asked of the seam");
        Assert.DoesNotContain(Shortcut.Parse("Shift+Delete").Value!, started.Seam.Asked, "nor with Shift alone");
        Assert.AreEqual("Add Ctrl, Alt or Win: Delete alone is needed by every app.", page.WarningOf(SettingsPage.TakeScreenshotTitle));
        Assert.IsTrue(field.IsRecording);
        Assert.IsEmpty(started.Seam.Registered, "nothing left registered by the judgement");
        Assert.AreEqual("Ctrl+PrtScn", Written(started).Screenshot, "nothing written");
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Screenshot));
    }

    [TestMethod]
    [DataRow(true, DisplayName = "the act's own let go first")]
    [DataRow(false, DisplayName = "a free combination still held")]
    public void APressSeenOnlyComingUpThatNoOtherReadingExplainsIsRefusedAsItsPlainReading(bool letGoFirst)
    {
        // Another program took the key's press, yet none of its readings with Ctrl, Alt or Win is held: the plain reading,
        // which the seam may not be asked about, is what it took, and the field refuses it anyway (G32).
        var started = StartedShortcuts.Start(screenshot: "Ctrl+F2");
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);
        field.Begin();

        field.PressSeenComingUp(Key.F2, letGoFirst ? [KeyModifiers.Control, KeyModifiers.None] : [KeyModifiers.Alt, KeyModifiers.None], letGoFirst);

        Assert.AreEqual("Add Ctrl, Alt or Win: F2 alone is needed by every app.", page.WarningOf(SettingsPage.TakeScreenshotTitle), "refused on the spot (G32)");
        Assert.DoesNotContain(Shortcut.Parse("F2").Value!, started.Seam.Asked, "never asked of the seam");
        Assert.IsTrue(field.IsRecording, "the field still records");
        Assert.AreEqual("Ctrl+F2", started.Shortcuts.TextOf(ShortcutAct.Screenshot), "nothing bound");
    }

    [TestMethod]
    public void AStoredPlainKeyIsRegisteredAtStartAndFromAnImportsSettings()
    {
        var started = StartedShortcuts.Start(open: "Delete", screenshot: "F9");

        Assert.IsTrue(started.Seam.Press(Shortcut.Parse("Delete").Value!), "an earlier build's plain key registers at start (G31, G33)");
        Assert.IsTrue(started.Seam.Press(Shortcut.Parse("F9").Value!));
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Open));
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Screenshot));
        Assert.AreEqual("ImageStash · Delete", started.Tray.ToolTip, "no line names it");

        started.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Record = "Home" } });
        started.Shortcuts.BindFromSettings();

        Assert.IsTrue(started.Seam.Press(Shortcut.Parse("Home").Value!), "an imported plain key registers too (G36)");
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Record));
        Assert.HasCount(4, started.Seam.Registered);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "shortcuts running")]
    [DataRow(true, DisplayName = "shortcuts paused")]
    public void InItsOwnFieldAStoredPlainKeyIsRefusedYetStaysRegisteredAndWritten(bool paused)
    {
        using var folder = new ScratchFolder();
        var started = StartedShortcuts.Start(screenshot: "F9", settingsFolder: folder.Path);
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);
        var f9 = Shortcut.Parse("F9").Value!;
        if (paused)
        {
            started.Shortcuts.TogglePause();
        }

        field.Begin();
        var asked = started.Seam.Asked.Count;
        field.Press(Key.F9, KeyModifiers.None);

        Assert.AreEqual("Add Ctrl, Alt or Win: F9 alone is needed by every app.", page.WarningOf(SettingsPage.TakeScreenshotTitle), "refused like any plain key (G32)");
        Assert.IsTrue(field.IsRecording);

        field.PressSeenComingUp(Key.F9, [KeyModifiers.None], letGoFirst: true);

        Assert.AreEqual("Add Ctrl, Alt or Win: F9 alone is needed by every app.", page.WarningOf(SettingsPage.TakeScreenshotTitle), "on the route seen coming up too");
        Assert.HasCount(asked, started.Seam.Asked, "never asked of the seam");

        field.Cancel();
        if (paused)
        {
            started.Shortcuts.TogglePause();
        }

        Assert.IsTrue(started.Seam.Press(f9), "the stored one registered again (G31, G33)");
        Assert.AreEqual("F9", Written(started).Screenshot, "and still written");
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Screenshot), "never made unavailable");
    }

    /// <summary>The shortcuts the settings file holds once every change so far is written.</summary>
    private static ImageStash.Store.Shortcuts Written(StartedShortcuts started)
    {
        started.Settings.Flush().Wait();
        return started.Saved;
    }

    /// <summary>A press of <paramref name="text"/>'s key with its modifiers, as the field reads one going down.</summary>
    private static void Press(ShortcutRecording field, string text)
    {
        var gesture = Shortcut.Parse(text).Value!.Gesture;
        field.Press(gesture.Key, gesture.KeyModifiers);
    }
}
