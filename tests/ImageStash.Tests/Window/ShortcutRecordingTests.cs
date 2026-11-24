using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Settings' four press-to-record shortcut fields through the page's model (G32, G33, F5): a field recording releases
/// ImageStash's shortcuts, so the open shortcut pressed into it reaches it instead of summoning; a modifier alone
/// completes nothing; the first key completes the candidate, refused on the spot with the warning under its row or
/// registered and written in place of the old; every way a recording ends registers the four again; one field records
/// at a time; a row shows its field's last refusal, else why its shortcut is unavailable, the row Settings opens on.
/// </summary>
[TestClass]
public sealed class ShortcutRecordingTests
{
    private static readonly Shortcut Open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
    private static readonly Shortcut[] Defaults = [Open, Shortcut.Parse("Ctrl+PrtScn").Value!, Shortcut.Parse("Ctrl+Shift+PrtScn").Value!, Shortcut.Parse("Win+Alt+V").Value!];

    [TestMethod]
    public void TheOpenShortcutPressedIntoTheScreenshotFieldSummonsNothingAndIsRefusedAsTheOpenShortcuts()
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: scratch.Path);
        var file = started.SavedText;
        var page = started.Page();
        var field = page.ShortcutField(SettingsPage.TakeScreenshotTitle)!;

        field.Begin();

        Assert.IsTrue(field.IsRecording);
        Assert.IsEmpty(started.Seam.Registered, "released, so the press reaches the field (G32)");
        Assert.IsFalse(started.Seam.Press(Open), "the open shortcut is not there to summon");
        field.Press(Key.LeftCtrl, KeyModifiers.Control);
        field.Press(Key.LeftAlt, KeyModifiers.Control | KeyModifiers.Alt);
        Assert.IsTrue(field.IsRecording, "a modifier alone completes nothing");
        field.Press(Key.Space, KeyModifiers.Control | KeyModifiers.Alt);

        Assert.AreEqual(0, started.Summoned);
        Assert.IsTrue(field.IsRecording, "the field still records: the next press is the retry (G32)");
        Assert.AreEqual("Open ImageStash already uses Ctrl+Alt+Space.", page.WarningOf(SettingsPage.TakeScreenshotTitle),
            "on the spot, under its row, while the field still records (G32, G35)");
        Assert.AreEqual("Ctrl+PrtScn", field.Text, "the field shows its shortcut as it was");
        Assert.IsEmpty(started.Seam.Registered, "the four still released");

        // The retry of the same combination: refused again in its words, and the window is not summoned.
        Assert.IsFalse(started.Seam.Press(Open), "the open shortcut is still not there to summon");
        field.Press(Key.Space, KeyModifiers.Control | KeyModifiers.Alt);
        Assert.AreEqual(0, started.Summoned);
        Assert.AreEqual("Open ImageStash already uses Ctrl+Alt+Space.", page.WarningOf(SettingsPage.TakeScreenshotTitle));
        Assert.IsTrue(field.IsRecording);

        field.Cancel();
        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle), "after Esc the row says only what is still true");
        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList(), "all four registered again");
        started.Settings.Flush().Wait();
        Assert.AreEqual(file, started.SavedText, "the file unchanged");
    }

    [TestMethod]
    public void AFreeCombinationIsTakenAndShownTheRowsWarningGoneAndThePageRedrawn()
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: scratch.Path);
        var page = started.Page();
        var redrawn = 0;
        page.Changed += () => redrawn++;
        var field = page.ShortcutField(ShortcutAct.Screenshot);
        field.Begin();
        field.Press(Key.V, KeyModifiers.Control);
        Assert.AreEqual("Ctrl+V is the paste key ImageStash presses, so it can't be a shortcut.", page.WarningOf(SettingsPage.TakeScreenshotTitle));
        Assert.IsTrue(field.IsRecording, "still recording: the next press is the retry (G32)");

        field.Press(Key.F8, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.IsFalse(field.IsRecording, "a combination taken ends the recording");
        Assert.AreEqual("Ctrl+Shift+F8", field.Text);
        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle), "a combination taken after a refusal clears the row's warning");
        Assert.AreEqual(new KeyGesture(Key.F8, KeyModifiers.Control | KeyModifiers.Shift), started.Tray.Entries[1].Gesture);
        Assert.AreEqual("Ctrl+Shift+F8", started.Settings.Current.Shortcuts.Screenshot);
        CollectionAssert.AreEquivalent(new[] { Defaults[0], Shortcut.Parse("Ctrl+Shift+F8").Value!, Defaults[2], Defaults[3] }, started.Seam.Registered.ToList(), "every shortcut due registered");
        Assert.IsGreaterThanOrEqualTo(4, redrawn, "the begin, the refusal, the end and the change of the shortcuts each redraw the page");
    }

    [TestMethod]
    public void PrtScnWithCtrlIntoItsOwnFieldIsItsOwnShortcutAndChangesNothing()
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: scratch.Path);
        var written = File.GetLastWriteTimeUtc(Path.Combine(scratch.Path, "settings.json"));
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);

        field.Begin();
        field.Press(Key.PrintScreen, KeyModifiers.Control);

        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle), "no warning (F5: a PrtScn combination records)");
        CollectionAssert.AreEqual(Defaults.Concat(Defaults).ToList(), started.Seam.Asked, "nothing registered anew beyond the four again");
        started.Settings.Flush().Wait();
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(Path.Combine(scratch.Path, "settings.json")), "no write");
    }

    [TestMethod]
    public void AHeldCombinationLetGoModifiersFirstIsStillRefusedOnTheSpotAsHeldAndChangesNothing()
    {
        using var scratch = new ScratchFolder();
        var held = Shortcut.Parse("Ctrl+Shift+F9").Value!;
        var started = StartedShortcuts.Start(settingsFolder: scratch.Path, held: held);
        var file = started.SavedText;
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);

        field.Begin();
        field.PressSeenComingUp(Key.F9, [KeyModifiers.Control | KeyModifiers.Shift, KeyModifiers.None], letGoFirst: true);

        Assert.IsTrue(field.IsRecording, "a press seen only coming up keeps the field recording after its refusal, as one seen going down does (G32)");
        Assert.AreEqual("Ctrl+Shift+F9 is already used by another app. Press a different combination.", page.WarningOf(SettingsPage.TakeScreenshotTitle),
            "with a warning on the spot, whatever order the keys came up in (G32)");
        Assert.AreEqual("Ctrl+PrtScn", field.Text);
        Assert.IsEmpty(started.Seam.Registered, "the four still released while it records");
        field.Cancel();
        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList(), "the four registered again");
        started.Settings.Flush().Wait();
        Assert.AreEqual(file, started.SavedText, "the file unchanged");
    }

    [TestMethod]
    public void AFreeCombinationLetGoModifiersFirstIsNeverBoundAndItsRowSaysWhy()
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: scratch.Path);
        var file = started.SavedText;
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);
        var free = Shortcut.Parse("Ctrl+Shift+F8").Value!;

        field.Begin();
        field.PressSeenComingUp(Key.F8, [KeyModifiers.Control | KeyModifiers.Shift, KeyModifiers.None], letGoFirst: true);

        Assert.IsTrue(field.IsRecording, "still recording for the retry (G32)");
        Assert.AreEqual("Ctrl+Shift+F8 wasn't recorded because a modifier came up first. Hold Ctrl, Alt or Shift until the key is up.", page.WarningOf(SettingsPage.TakeScreenshotTitle));
        CollectionAssert.Contains(started.Seam.Asked, free, "asked, so another program's hold would have been named");
        Assert.IsEmpty(started.Seam.Registered, "let go at once: nobody may have pressed it; the four still released while it records");
        Assert.AreEqual(("Ctrl+PrtScn", new KeyGesture(Key.PrintScreen, KeyModifiers.Control)), (field.Text, started.Tray.Entries[1].Gesture));
        started.Settings.Flush().Wait();
        Assert.AreEqual(file, started.SavedText, "the file unchanged");

        field.Begin();
        field.PressSeenComingUp(Key.V, [KeyModifiers.Control, KeyModifiers.None], letGoFirst: true);
        Assert.AreEqual("Ctrl+V is the paste key ImageStash presses, so it can't be a shortcut.", page.WarningOf(SettingsPage.TakeScreenshotTitle));
        field.Begin();
        field.PressSeenComingUp(Key.V, [KeyModifiers.Meta | KeyModifiers.Alt, KeyModifiers.None], letGoFirst: true);
        Assert.AreEqual("Stash clipboard image already uses Win+Alt+V.", page.WarningOf(SettingsPage.TakeScreenshotTitle));
        field.Begin();
        field.PressSeenComingUp(Key.PrintScreen, [KeyModifiers.Control, KeyModifiers.None], letGoFirst: true);
        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle), "its own shortcut changes nothing, as pressed whole");
        field.Begin();
        field.PressSeenComingUp(Key.LeftShift, [KeyModifiers.Control, KeyModifiers.None], letGoFirst: true);
        Assert.IsTrue(field.IsRecording, "a modifier alone completes nothing");
    }

    [TestMethod]
    public void APressLetGoAfterOneOfItsModifiersIsRefusedAsTheCombinationStillHeldWhenAnotherProgramHoldsThat()
    {
        var held = Shortcut.Parse("Ctrl+F9").Value!;
        var started = StartedShortcuts.Start(held: held);
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);

        // Ctrl and Shift pressed, Shift let go, then F9, which another program's hot key took, let go with Ctrl still held.
        field.Begin();
        field.PressSeenComingUp(Key.F9, [KeyModifiers.Control | KeyModifiers.Shift, KeyModifiers.Control], letGoFirst: true);

        Assert.AreEqual("Ctrl+F9 is already used by another app. Press a different combination.", page.WarningOf(SettingsPage.TakeScreenshotTitle), "G32");
        Assert.IsEmpty(started.Seam.Registered, "the four still released while the field records");

        // Neither held: the likelier is named as not recorded, never another reason the other would be refused for.
        field.PressSeenComingUp(Key.A, [KeyModifiers.Control | KeyModifiers.Shift, KeyModifiers.None], letGoFirst: true);
        Assert.AreEqual("Ctrl+Shift+A wasn't recorded because a modifier came up first. Hold Ctrl, Alt or Shift until the key is up.", page.WarningOf(SettingsPage.TakeScreenshotTitle));
        field.Cancel();
        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList());
    }

    [TestMethod]
    public void APressLetGoAfterOneOfItsModifiersIsRefusedAsHeldWhenTheLikelierReadingIsOneOfTheFoursOwnCombinations()
    {
        var held = Shortcut.Parse("Ctrl+F9").Value!;
        var screenshot = Shortcut.Parse("Ctrl+Shift+F9").Value!;
        var started = StartedShortcuts.Start(screenshot: screenshot.Text, held: held);
        var page = started.Page();

        // Ctrl and Shift pressed, Shift let go, then F9, which another program's hot key took, let go with Ctrl still held:
        // the likelier reading is the screenshot shortcut, into its own field and into another's.
        foreach (var (act, title, name) in new[] { (ShortcutAct.Screenshot, SettingsPage.TakeScreenshotTitle, "screenshot"), (ShortcutAct.Record, SettingsPage.RecordClipTitle, "record") })
        {
            var field = page.ShortcutField(act);
            field.Begin();
            field.PressSeenComingUp(Key.F9, [KeyModifiers.Control | KeyModifiers.Shift, KeyModifiers.Control], letGoFirst: true);

            Assert.IsTrue(field.IsRecording, "still recording for the retry (G32)");
            Assert.AreEqual("Ctrl+F9 is already used by another app. Press a different combination.", page.WarningOf(title), $"{name}: with a warning on the spot (G32)");
        }

        page.CancelRecording();

        Assert.AreEqual((screenshot.Text, "Ctrl+Shift+PrtScn"), (started.Settings.Current.Shortcuts.Screenshot, started.Settings.Current.Shortcuts.Record), "nothing bound");
        CollectionAssert.AreEquivalent(new[] { Open, screenshot, Defaults[2], Defaults[3] }, started.Seam.Registered.ToList(), "the four registered again");
    }

    [TestMethod]
    [DataRow(ShortcutRefusalKind.HeldByAnotherProgram, "Ctrl+PrtScn is already used by another app. Press a different combination.", "Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.")]
    [DataRow(ShortcutRefusalKind.TypesACharacter, "Ctrl+PrtScn types a character on your keyboard layout, so it can't be a shortcut.", "Ctrl+PrtScn types a character, so Take a screenshot has no shortcut.")]
    public void UnderPauseAFieldsOwnCombinationIsJudgedAfreshAndRefusedOnTheSpot(ShortcutRefusalKind reason, string sentence, string line)
    {
        var started = StartedShortcuts.Start();
        var screenshot = Defaults[1];
        started.Shortcuts.TogglePause();
        // Released by Pause, then taken by another program, or come to type a character under the layout, meanwhile.
        started.Seam.Refuse(screenshot, new ShortcutRefusal(reason));
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);

        foreach (var press in new Action[] { () => field.Press(Key.PrintScreen, KeyModifiers.Control), () => field.PressSeenComingUp(Key.PrintScreen, [KeyModifiers.Control, KeyModifiers.None], letGoFirst: true) })
        {
            field.Begin();
            press();

            Assert.AreEqual(sentence, page.WarningOf(SettingsPage.TakeScreenshotTitle), "on the spot (G32)");
            Assert.IsTrue(field.IsRecording, "the field still records, so the next press is the retry (G32)");
            Assert.IsEmpty(started.Seam.Registered, "Pause still holds all four off (G33)");
            Assert.AreEqual(ShortcutAct.Screenshot, started.Shortcuts.FirstUnavailable, "refused at that registration, so unavailable, Settings opening on its row (G33)");
        Assert.AreEqual($"ImageStash · shortcuts paused\n{line}", started.Tray.ToolTip, "Pause is named beside G33's whole reason when there is room");
        }

        field.Cancel();
        Assert.AreEqual(sentence, page.WarningOf(SettingsPage.TakeScreenshotTitle), "after Esc the row says what is still true: why its shortcut is unavailable");
        started.Seam.Free(screenshot);
        started.Shortcuts.TogglePause();
        CollectionAssert.AreEquivalent(Defaults.Where(each => each != screenshot).ToList(), started.Seam.Registered.ToList(), "left unregistered until it is changed in Settings (G33)");

        field.Begin();
        field.Press(Key.PrintScreen, KeyModifiers.Control);
        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle), "free again, recorded there, it registers");
        Assert.IsNull(started.Shortcuts.FirstUnavailable);
        Assert.AreEqual("ImageStash · Ctrl+Alt+Space", started.Tray.ToolTip, "nothing named, Pause off: the open shortcut beside the name");
        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList());
        Assert.AreEqual("Ctrl+PrtScn", started.Settings.Current.Shortcuts.Screenshot);
    }

    [TestMethod]
    public void UnderPauseAFieldsOwnCombinationStillFreeChangesNothing()
    {
        var started = StartedShortcuts.Start();
        started.Shortcuts.TogglePause();
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);

        field.Begin();
        field.Press(Key.PrintScreen, KeyModifiers.Control);

        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle));
        Assert.IsNull(started.Shortcuts.FirstUnavailable);
        Assert.IsEmpty(started.Seam.Registered, "judged and let go again: Pause holds all four off (G33)");
        started.Shortcuts.TogglePause();
        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList());
    }

    [TestMethod]
    public void CtrlShiftVIsTakenWhereThePlainCtrlVIsRefused()
    {
        var started = StartedShortcuts.Start();
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.StashClipboardImage);

        field.Begin();
        field.Press(Key.V, KeyModifiers.Control);
        field.Begin();
        field.Press(Key.V, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.IsNull(page.WarningOf(SettingsPage.StashClipboardImageTitle));
        Assert.AreEqual("Ctrl+Shift+V", field.Text);
        CollectionAssert.DoesNotContain(started.Seam.Asked, Shortcut.Parse("Ctrl+V").Value!);
    }

    [TestMethod]
    public void EveryWayARecordingEndsUnfinishedRegistersTheFourAgainAndChangesNothing()
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: scratch.Path);
        var file = started.SavedText;
        var page = started.Page();
        var open = page.ShortcutField(ShortcutAct.Open);
        var record = page.ShortcutField(ShortcutAct.Record);

        // Each after a refusal, which leaves the field recording: Esc, the ×, and focus leaving the field (each the
        // field's Cancel), another field beginning, the window deactivated and the window closed.
        open.Begin();
        Refused(open);
        open.Cancel();
        AssertEnded("Cancel");

        open.Begin();
        Refused(open);
        record.Begin();
        Assert.IsFalse(open.IsRecording, "a second field beginning ends the first's (one at a time)");
        Assert.IsTrue(record.IsRecording);
        Assert.IsEmpty(started.Seam.Registered, "still released for the second field");
        Refused(record);
        record.Cancel();
        AssertEnded("a second field");

        record.Begin();
        Refused(record);
        page.CancelRecording();
        AssertEnded("the window deactivated");

        record.Begin();
        Refused(record);
        page.Close();
        AssertEnded("the window closed");
        started.Settings.Flush().Wait();
        Assert.AreEqual(file, started.SavedText, "nothing written");

        void Refused(ShortcutRecording field)
        {
            field.Press(Key.V, KeyModifiers.Control);
            Assert.IsTrue(field.IsRecording, "refused, it still records");
            Assert.IsEmpty(started.Seam.Registered, "the four still released");
        }

        void AssertEnded(string by)
        {
            Assert.IsFalse(page.ShortcutFields.Any(field => field.IsRecording), by);
            CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList(), $"{by}: the four registered again");
            Assert.AreEqual(("Ctrl+Alt+Space", "Ctrl+Shift+PrtScn"), (open.Text, record.Text), $"{by}: each field idle with its old text");
            Assert.IsTrue(page.Rows.All(row => page.WarningOf(row.Title) is null), $"{by}: no warning");
        }
    }

    [TestMethod]
    public void AClosedPageNoLongerFollowsTheShortcuts()
    {
        var started = StartedShortcuts.Start();
        var page = started.Page();
        var redrawn = 0;
        page.Changed += () => redrawn++;

        page.Close();
        started.Tray.Entries[4].Chosen();

        Assert.AreEqual(0, redrawn, "a closed page stays closed: nothing keeps it alive (F4)");
    }

    [TestMethod]
    public void AShortcutUnavailableAtStartIsTheRowSettingsOpensOnWithItsReasonUntilAFreeCombinationIsRecordedThere()
    {
        var started = StartedShortcuts.Start(held: Open);
        var page = started.Page();
        var unavailable = started.Shortcuts.UnavailableReason(ShortcutAct.Open)!;

        Assert.AreEqual(ShortcutAct.Open, started.Shortcuts.FirstUnavailable, "Settings opens on that row (G33)");
        Assert.AreEqual(ShortcutWords.Row(unavailable), page.WarningOf(SettingsPage.OpenTitle), "with its reason under it");
        Assert.AreEqual(ShortcutWords.ToolTipLine(unavailable!), started.Tray.ToolTip);
        Assert.IsTrue(page.Rows.Where(row => row.Title != SettingsPage.OpenTitle).All(row => page.WarningOf(row.Title) is null), "on no other row");

        var field = page.ShortcutField(ShortcutAct.Open);
        field.Begin();
        field.Press(Key.V, KeyModifiers.Control);
        Assert.AreEqual("Ctrl+V is the paste key ImageStash presses, so it can't be a shortcut.", page.WarningOf(SettingsPage.OpenTitle),
            "the field's last refusal shows in place of the reason");
        Assert.AreEqual(ShortcutWords.Row(unavailable), started.Page().WarningOf(SettingsPage.OpenTitle), "a page opened afresh shows the reason again");

        field.Begin();
        field.Press(Key.F9, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.IsNull(page.WarningOf(SettingsPage.OpenTitle));
        Assert.IsNull(started.Shortcuts.FirstUnavailable, "Settings opens at the top again");
        Assert.AreEqual("ImageStash · Ctrl+Shift+F9", started.Tray.ToolTip, "the tooltip's line gone (G33), the open shortcut named as now recorded the moment it registers");
        Assert.IsTrue(started.Seam.Press(Shortcut.Parse("Ctrl+Shift+F9").Value!));
        Assert.AreEqual(1, started.Summoned);
    }

    [TestMethod]
    public void AShortcutTheSeamReleasesForTheLayoutShowsOnItsRowOfAnOpenPage()
    {
        var started = StartedShortcuts.Start();
        var page = started.Page();
        var redrawn = 0;
        page.Changed += () => redrawn++;

        started.Seam.Release(Defaults[2], ShortcutRefusal.TypesACharacter(null));
        started.RunPosted();

        Assert.AreEqual(1, redrawn);
        Assert.AreEqual("Ctrl+Shift+PrtScn types a character on your keyboard layout, so it can't be a shortcut.", page.WarningOf(SettingsPage.RecordClipTitle), "G33");
        var field = page.ShortcutField(ShortcutAct.Record);
        field.Begin();
        field.Press(Key.F8, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.IsNull(page.WarningOf(SettingsPage.RecordClipTitle));
        CollectionAssert.Contains(started.Seam.Registered.ToList(), Shortcut.Parse("Ctrl+Shift+F8").Value!);
    }
}
