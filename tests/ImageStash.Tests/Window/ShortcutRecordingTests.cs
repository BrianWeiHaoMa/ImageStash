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
    private static readonly Shortcut[] Defaults = [Open, Shortcut.Parse("Ctrl+PrtScn").Value!, Shortcut.Parse("Ctrl+Shift+PrtScn").Value!, Shortcut.Parse("Ctrl+Alt+V").Value!];

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
        Assert.IsFalse(field.IsRecording);
        Assert.AreEqual("Register the screenshot shortcut failed: Ctrl+Alt+Space, the open shortcut uses it too", page.WarningOf(SettingsPage.TakeScreenshotTitle)?.Sentence,
            "on the spot, under its row (G32, G35)");
        Assert.AreEqual("Ctrl+PrtScn", field.Text, "the field shows its shortcut as it was");
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
        Assert.AreEqual($"Register the screenshot shortcut failed: Ctrl+V, {GlobalShortcuts.PasteKeystrokeReason}", page.WarningOf(SettingsPage.TakeScreenshotTitle)?.Sentence);

        field.Begin();
        field.Press(Key.F8, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.AreEqual("Ctrl+Shift+F8", field.Text);
        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle), "a rebind clears the row's warning");
        Assert.AreEqual(new KeyGesture(Key.F8, KeyModifiers.Control | KeyModifiers.Shift), started.Tray.Entries[1].Gesture);
        Assert.AreEqual("Ctrl+Shift+F8", started.Settings.Current.Shortcuts.Screenshot);
        Assert.IsGreaterThanOrEqualTo(5, redrawn, "each begin, end and change of the shortcuts redraws the page");
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

        Assert.IsFalse(field.IsRecording);
        Assert.AreEqual("Register the screenshot shortcut failed: Ctrl+Shift+F9, another program holds it", page.WarningOf(SettingsPage.TakeScreenshotTitle)?.Sentence,
            "with a warning on the spot, whatever order the keys came up in (G32)");
        Assert.AreEqual("Ctrl+PrtScn", field.Text);
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

        Assert.IsFalse(field.IsRecording);
        Assert.AreEqual($"Register the screenshot shortcut failed: Ctrl+Shift+F8, {GlobalShortcuts.ModifiersLetGoFirstReason}", page.WarningOf(SettingsPage.TakeScreenshotTitle)?.Sentence);
        CollectionAssert.Contains(started.Seam.Asked, free, "asked, so another program's hold would have been named");
        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList(), "let go at once: nobody may have pressed it");
        Assert.AreEqual(("Ctrl+PrtScn", new KeyGesture(Key.PrintScreen, KeyModifiers.Control)), (field.Text, started.Tray.Entries[1].Gesture));
        started.Settings.Flush().Wait();
        Assert.AreEqual(file, started.SavedText, "the file unchanged");

        field.Begin();
        field.PressSeenComingUp(Key.V, [KeyModifiers.Control, KeyModifiers.None], letGoFirst: true);
        Assert.AreEqual($"Register the screenshot shortcut failed: Ctrl+V, {GlobalShortcuts.PasteKeystrokeReason}", page.WarningOf(SettingsPage.TakeScreenshotTitle)?.Sentence);
        field.Begin();
        field.PressSeenComingUp(Key.V, [KeyModifiers.Control | KeyModifiers.Alt, KeyModifiers.None], letGoFirst: true);
        Assert.AreEqual("Register the screenshot shortcut failed: Ctrl+Alt+V, the Stash clipboard image shortcut uses it too", page.WarningOf(SettingsPage.TakeScreenshotTitle)?.Sentence);
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

        Assert.AreEqual("Register the screenshot shortcut failed: Ctrl+F9, another program holds it", page.WarningOf(SettingsPage.TakeScreenshotTitle)?.Sentence, "G32");
        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList());

        // Neither held: the likelier is named as not recorded, never another reason the other would be refused for.
        field.Begin();
        field.PressSeenComingUp(Key.A, [KeyModifiers.Control | KeyModifiers.Shift, KeyModifiers.None], letGoFirst: true);
        Assert.AreEqual($"Register the screenshot shortcut failed: Ctrl+Shift+A, {GlobalShortcuts.ModifiersLetGoFirstReason}", page.WarningOf(SettingsPage.TakeScreenshotTitle)?.Sentence);
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

            Assert.IsFalse(field.IsRecording);
            Assert.AreEqual($"Register the {name} shortcut failed: Ctrl+F9, another program holds it", page.WarningOf(title)?.Sentence, "with a warning on the spot (G32)");
        }

        Assert.AreEqual((screenshot.Text, "Ctrl+Shift+PrtScn"), (started.Settings.Current.Shortcuts.Screenshot, started.Settings.Current.Shortcuts.Record), "nothing bound");
        CollectionAssert.AreEquivalent(new[] { Open, screenshot, Defaults[2], Defaults[3] }, started.Seam.Registered.ToList(), "the four registered again");
    }

    [TestMethod]
    [DataRow(ShortcutReasons.HeldByAnotherProgram)]
    [DataRow(ShortcutReasons.TypesACharacter)]
    public void UnderPauseAFieldsOwnCombinationIsJudgedAfreshAndRefusedOnTheSpot(string reason)
    {
        var started = StartedShortcuts.Start();
        var screenshot = Defaults[1];
        started.Shortcuts.TogglePause();
        // Released by Pause, then taken by another program, or come to type a character under the layout, meanwhile.
        started.Seam.Refuse(screenshot, reason);
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);

        var sentence = $"Register the screenshot shortcut failed: Ctrl+PrtScn, {reason}";
        foreach (var press in new Action[] { () => field.Press(Key.PrintScreen, KeyModifiers.Control), () => field.PressSeenComingUp(Key.PrintScreen, [KeyModifiers.Control, KeyModifiers.None], letGoFirst: true) })
        {
            field.Begin();
            press();

            Assert.AreEqual(sentence, page.WarningOf(SettingsPage.TakeScreenshotTitle)?.Sentence, "on the spot (G32)");
            Assert.IsEmpty(started.Seam.Registered, "Pause still holds all four off (G33)");
            Assert.AreEqual(ShortcutAct.Screenshot, started.Shortcuts.FirstUnavailable, "refused at that registration, so unavailable, Settings opening on its row (G33)");
            Assert.AreEqual(sentence, started.Tray.ToolTip, "the tray icon says which and why (G33)");
        }

        started.Seam.Free(screenshot);
        started.Shortcuts.TogglePause();
        CollectionAssert.AreEquivalent(Defaults.Where(each => each != screenshot).ToList(), started.Seam.Registered.ToList(), "left unregistered until it is changed in Settings (G33)");

        field.Begin();
        field.Press(Key.PrintScreen, KeyModifiers.Control);
        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle), "free again, recorded there, it registers");
        Assert.IsNull(started.Shortcuts.FirstUnavailable);
        Assert.AreEqual("ImageStash", started.Tray.ToolTip);
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

        // Esc, the ×, and focus leaving the field: each the field's Cancel.
        open.Begin();
        open.Cancel();
        AssertEnded("Cancel");

        open.Begin();
        record.Begin();
        Assert.IsFalse(open.IsRecording, "a second field beginning ends the first's (one at a time)");
        Assert.IsTrue(record.IsRecording);
        Assert.IsEmpty(started.Seam.Registered, "still released for the second field");
        record.Cancel();
        AssertEnded("a second field");

        record.Begin();
        page.CancelRecording();
        AssertEnded("the window deactivated");

        record.Begin();
        page.Close();
        AssertEnded("the window closed");
        started.Settings.Flush().Wait();
        Assert.AreEqual(file, started.SavedText, "nothing written");

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
        Assert.AreEqual(unavailable, page.WarningOf(SettingsPage.OpenTitle), "with the tooltip's sentence under it");
        Assert.AreEqual(unavailable.Sentence, started.Tray.ToolTip);
        Assert.IsTrue(page.Rows.Where(row => row.Title != SettingsPage.OpenTitle).All(row => page.WarningOf(row.Title) is null), "on no other row");

        var field = page.ShortcutField(ShortcutAct.Open);
        field.Begin();
        field.Press(Key.V, KeyModifiers.Control);
        Assert.AreEqual($"Register the open shortcut failed: Ctrl+V, {GlobalShortcuts.PasteKeystrokeReason}", page.WarningOf(SettingsPage.OpenTitle)?.Sentence,
            "the field's last refusal shows in place of the reason");
        Assert.AreEqual(unavailable, started.Page().WarningOf(SettingsPage.OpenTitle), "a page opened afresh shows the reason again");

        field.Begin();
        field.Press(Key.F9, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.IsNull(page.WarningOf(SettingsPage.OpenTitle));
        Assert.IsNull(started.Shortcuts.FirstUnavailable, "Settings opens at the top again");
        Assert.AreEqual("ImageStash", started.Tray.ToolTip, "the tooltip's line gone (G33)");
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

        started.Seam.Release(Defaults[2], ShortcutReasons.TypesACharacter);
        started.RunPosted();

        Assert.AreEqual(1, redrawn);
        Assert.AreEqual("Register the record shortcut failed: Ctrl+Shift+PrtScn, it would type a character", page.WarningOf(SettingsPage.RecordClipTitle)?.Sentence, "G33");
        var field = page.ShortcutField(ShortcutAct.Record);
        field.Begin();
        field.Press(Key.F8, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.IsNull(page.WarningOf(SettingsPage.RecordClipTitle));
        CollectionAssert.Contains(started.Seam.Registered.ToList(), Shortcut.Parse("Ctrl+Shift+F8").Value!);
    }
}
