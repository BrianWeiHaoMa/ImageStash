using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The remap behind Settings' shortcut fields (G32, G33, G34, F5, State: Settings): while a field records, all four
/// shortcuts are released and registered again when it ends; a candidate is refused, nothing changed, when it is the
/// plain Ctrl+V the handoff sends, another shortcut's combination whether registered or not, or what the seam refuses,
/// each named with which shortcut, the combination and why; otherwise it is registered in place of the old, written to
/// the settings, shown in the tray menu, and no longer unavailable, the tooltip's line for it gone; Pause holds a
/// rebound shortcut off like the others until its toggle back.
/// </summary>
[TestClass]
public sealed class GlobalShortcutsRebindTests
{
    private const int Screenshot = 1;
    private const int Record = 2;
    private const int Pause = 4;
    private static readonly Shortcut Open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
    private static readonly Shortcut TakeScreenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;
    private static readonly Shortcut RecordClip = Shortcut.Parse("Ctrl+Shift+PrtScn").Value!;
    private static readonly Shortcut StashClipboardImage = Shortcut.Parse("Ctrl+Alt+V").Value!;
    private static readonly Shortcut Free = Shortcut.Parse("Ctrl+Shift+F8").Value!;
    private static readonly Shortcut[] Defaults = [Open, TakeScreenshot, RecordClip, StashClipboardImage];

    [TestMethod]
    public void WhileAFieldRecordsNoneIsRegisteredAndTheEndRegistersAllFourAgainWithNothingWritten()
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: scratch.Path);
        var file = started.SavedText;

        var recording = started.Shortcuts.Suspend();

        Assert.IsEmpty(started.Seam.Registered, "a registered shortcut would swallow the press the field waits for (G32)");
        Assert.IsFalse(started.Seam.Press(Open), "the open shortcut summons nothing meanwhile");
        recording.Dispose();
        recording.Dispose();

        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList(), "all four again when it ends");
        Assert.AreEqual(8, started.Seam.Registrations, "once each at start and once each at the end, the second disposal doing nothing");
        Assert.IsTrue(started.Seam.Press(Open));
        Assert.AreEqual(1, started.Summoned);
        started.Settings.Flush().Wait();
        Assert.AreEqual(file, started.SavedText, "nothing written");
    }

    [TestMethod]
    public void AnotherShortcutsCombinationIsRefusedNamingWhichAndWhyAndNothingChanges()
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: scratch.Path);
        var file = started.SavedText;

        Outcome refused;
        using (started.Shortcuts.Suspend())
        {
            refused = started.Shortcuts.Rebind(ShortcutAct.Screenshot, Open);
        }

        Assert.AreEqual("Register the screenshot shortcut failed: Ctrl+Alt+Space, the open shortcut uses it too", refused.Failure?.Sentence,
            "which shortcut, the combination and why, not another program's hold (G32, G35)");
        CollectionAssert.AreEqual(Defaults.Concat(Defaults).ToList(), started.Seam.Asked, "the seam was never asked for it: the four at start, and again at the end");
        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList());
        Assert.AreEqual(TakeScreenshot, started.Shortcuts.ShortcutOf(ShortcutAct.Screenshot));
        Assert.AreEqual(TakeScreenshot.Gesture, started.Tray.Entries[Screenshot].Gesture);
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Screenshot), "a refusal makes nothing unavailable");
        Assert.AreEqual("ImageStash", started.Tray.ToolTip, "and names nothing in the tooltip: the field says it");
        started.Settings.Flush().Wait();
        Assert.AreEqual(file, started.SavedText, "the file unchanged");
    }

    [TestMethod]
    public void AnUnavailableShortcutsCombinationIsStillItsOwnAndRefusedAsSuch()
    {
        var started = StartedShortcuts.Start(held: Open);

        Outcome refused;
        using (started.Shortcuts.Suspend())
        {
            refused = started.Shortcuts.Rebind(ShortcutAct.Record, Open);
        }

        Assert.AreEqual("Register the record shortcut failed: Ctrl+Alt+Space, the open shortcut uses it too", refused.Failure?.Sentence,
            "the combination of another of the four, registered or not (G32)");
        CollectionAssert.DoesNotContain(started.Seam.Asked.Skip(4).ToList(), Open, "and the seam was not asked for it");
    }

    [TestMethod]
    [DataRow(ShortcutReasons.HeldByAnotherProgram, "Register the screenshot shortcut failed: Ctrl+Shift+F8, another program holds it")]
    [DataRow(ShortcutReasons.TypesACharacter, "Register the screenshot shortcut failed: Ctrl+Shift+F8, it would type a character")]
    public void WhatTheSeamRefusesIsNamedAndTheOldShortcutIsRegisteredAgainAndNotWritten(string reason, string sentence)
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: scratch.Path);
        var file = started.SavedText;
        started.Seam.Refuse(Free, reason);

        Outcome refused;
        using (started.Shortcuts.Suspend())
        {
            refused = started.Shortcuts.Rebind(ShortcutAct.Screenshot, Free);
        }

        Assert.AreEqual(sentence, refused.Failure?.Sentence, "G32's refusal with its warning's words (G35)");
        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList(), "the old one registered again");
        Assert.AreEqual(TakeScreenshot, started.Shortcuts.ShortcutOf(ShortcutAct.Screenshot), "and still the screenshot's");
        Assert.IsTrue(started.Seam.Press(TakeScreenshot));
        started.RunPosted();
        Assert.AreEqual(1, started.Screenshots);
        started.Settings.Flush().Wait();
        Assert.AreEqual(file, started.SavedText, "the file unchanged");
        Assert.AreEqual("Ctrl+PrtScn", started.Saved.Screenshot);
    }

    [TestMethod]
    public void ThePlainCtrlVIsRefusedBeforeTheSeamIsAskedWhileCtrlShiftVIsAccepted()
    {
        var started = StartedShortcuts.Start();
        var ctrlV = Shortcut.Parse("Ctrl+V").Value!;
        var ctrlShiftV = Shortcut.Parse("Ctrl+Shift+V").Value!;

        Outcome refused;
        Outcome accepted;
        using (started.Shortcuts.Suspend())
        {
            refused = started.Shortcuts.Rebind(ShortcutAct.StashClipboardImage, ctrlV);
        }

        using (started.Shortcuts.Suspend())
        {
            accepted = started.Shortcuts.Rebind(ShortcutAct.StashClipboardImage, ctrlShiftV);
        }

        Assert.AreEqual($"Register the Stash clipboard image shortcut failed: Ctrl+V, {GlobalShortcuts.PasteKeystrokeReason}", refused.Failure?.Sentence,
            "the handoff's own paste keystroke (G32, G8)");
        CollectionAssert.DoesNotContain(started.Seam.Asked, ctrlV, "never registered, even for a moment");
        Assert.IsTrue(accepted.Succeeded, "Ctrl+Shift+V is not the paste keystroke");
        CollectionAssert.Contains(started.Seam.Registered.ToList(), ctrlShiftV);
    }

    [TestMethod]
    public async Task AnAcceptedCombinationReplacesTheOldOneIsWrittenAndShownAndItsPressRunsTheAct()
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: scratch.Path, quiet: TimeSpan.FromMilliseconds(200));
        var changed = 0;
        started.Shortcuts.Changed += () => changed++;

        Outcome accepted;
        using (started.Shortcuts.Suspend())
        {
            accepted = started.Shortcuts.Rebind(ShortcutAct.Screenshot, Free);
            Assert.AreEqual(1, changed, "the tray and an open page told once");
        }

        Assert.IsTrue(accepted.Succeeded);
        CollectionAssert.AreEquivalent(new[] { Open, Free, RecordClip, StashClipboardImage }, started.Seam.Registered.ToList(), "registered in place of the old, which is not (G33)");
        Assert.AreEqual(Free, started.Shortcuts.ShortcutOf(ShortcutAct.Screenshot));
        Assert.AreEqual("Ctrl+Shift+F8", started.Shortcuts.TextOf(ShortcutAct.Screenshot));
        Assert.AreEqual(Free.Gesture, started.Tray.Entries[Screenshot].Gesture, "the tray menu shows it (G34)");
        Assert.AreEqual("Ctrl+Shift+F8", started.Settings.Current.Shortcuts.Screenshot, "in effect at once");
        Assert.IsTrue(started.Seam.Press(Free));
        Assert.IsFalse(started.Seam.Press(TakeScreenshot), "the old combination runs nothing");
        started.RunPosted();
        Assert.AreEqual((1, 0, 0, 0), (started.Screenshots, started.Summoned, started.Recorded, started.Stashed), "the screenshot, once");
        await Handing.Eventually(() => started.Saved.Screenshot == "Ctrl+Shift+F8");
        Assert.AreEqual(("Ctrl+Alt+Space", "Ctrl+Shift+PrtScn", "Ctrl+Alt+V"), (started.Saved.Open, started.Saved.Record, started.Saved.StashClipboardImage), "the others as they were");
    }

    [TestMethod]
    public async Task AnAcceptedCombinationIsWrittenAtOnceByFlushBeforeTheQuietSpellEnds()
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: scratch.Path, quiet: TimeSpan.FromMinutes(5));

        using (started.Shortcuts.Suspend())
        {
            Assert.IsTrue(started.Shortcuts.Rebind(ShortcutAct.Record, Free).Succeeded);
        }

        Assert.AreEqual("Ctrl+Shift+PrtScn", started.Saved.Record, "not yet: the keeper waits for quiet");
        await started.Settings.Flush();
        Assert.AreEqual("Ctrl+Shift+F8", started.Saved.Record, "a Quit right after keeps it");
        Assert.IsEmpty(started.WriteFailures);
    }

    [TestMethod]
    public void TheOpenShortcutRemappedStillActsAtThePress()
    {
        var started = StartedShortcuts.Start();
        var openAnew = Shortcut.Parse("Ctrl+Shift+F9").Value!;

        using (started.Shortcuts.Suspend())
        {
            Assert.IsTrue(started.Shortcuts.Rebind(ShortcutAct.Open, openAnew).Succeeded);
        }

        Assert.IsTrue(started.Seam.Press(openAnew));
        Assert.AreEqual(1, started.Summoned, "reading the window in front at the press (G1, G8)");
        Assert.IsEmpty(started.Posted);
        Assert.IsFalse(started.Seam.Press(Open));
    }

    [TestMethod]
    public void ItsOwnShortcutStillRegisteredChangesNothing()
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: scratch.Path);
        var written = File.GetLastWriteTimeUtc(Path.Combine(scratch.Path, "settings.json"));
        var changed = 0;
        started.Shortcuts.Changed += () => changed++;

        using (started.Shortcuts.Suspend())
        {
            Assert.IsTrue(started.Shortcuts.Rebind(ShortcutAct.Screenshot, TakeScreenshot).Succeeded, "no warning");
        }

        CollectionAssert.AreEqual(Defaults.Concat(Defaults).ToList(), started.Seam.Asked, "nothing registered anew beyond the four again at the end");
        Assert.AreEqual(0, changed);
        started.Settings.Flush().Wait();
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(Path.Combine(scratch.Path, "settings.json")), "and no write");
    }

    [TestMethod]
    public void AShortcutUnavailableAtStartIsRegisteredByItsOwnCombinationOnceTheHolderLetGoAndItsLineLeavesTheTooltip()
    {
        var started = StartedShortcuts.Start(held: Open);
        var unavailable = started.Shortcuts.UnavailableReason(ShortcutAct.Open);
        Assert.AreEqual("Register the open shortcut failed: Ctrl+Alt+Space, another program holds it", unavailable?.Sentence);
        Assert.AreEqual(unavailable!.Sentence, started.Tray.ToolTip, "the reason is the tooltip's line (G33)");
        Assert.AreEqual(ShortcutAct.Open, started.Shortcuts.FirstUnavailable);

        Outcome stillHeld;
        using (started.Shortcuts.Suspend())
        {
            stillHeld = started.Shortcuts.Rebind(ShortcutAct.Open, Open);
        }

        Assert.AreEqual(unavailable.Sentence, stillHeld.Failure?.Sentence, "judged again, not taken as its own while it is unavailable");
        Assert.AreEqual(unavailable, started.Shortcuts.UnavailableReason(ShortcutAct.Open), "and still unavailable, still named");
        Assert.AreEqual(unavailable.Sentence, started.Tray.ToolTip);

        started.Seam.Free(Open);
        using (started.Shortcuts.Suspend())
        {
            Assert.IsTrue(started.Shortcuts.Rebind(ShortcutAct.Open, Open).Succeeded, "until it is changed in Settings (G33)");
        }

        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList());
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Open));
        Assert.IsNull(started.Shortcuts.FirstUnavailable);
        Assert.AreEqual("ImageStash", started.Tray.ToolTip, "its line gone");
        Assert.IsTrue(started.Seam.Press(Open));
        Assert.AreEqual(1, started.Summoned);
    }

    [TestMethod]
    public void OnlyTheLineOfAShortcutRegisteredAgainLeavesTheTooltip()
    {
        var started = StartedShortcuts.Start(held: [Open, RecordClip]);
        var store = new Failure("Read settings", "settings.json", "held");
        started.Menu.Name(store);

        using (started.Shortcuts.Suspend())
        {
            Assert.IsTrue(started.Shortcuts.Rebind(ShortcutAct.Open, Free).Succeeded);
        }

        CollectionAssert.AreEqual(new[] { store.Sentence, "Register the record shortcut failed: Ctrl+Shift+PrtScn, another program holds it" }, started.Tray.ToolTip!.Split('\n'),
            "the record shortcut still unavailable and the other failure still named (G33, G35)");
        Assert.AreEqual(ShortcutAct.Record, started.Shortcuts.FirstUnavailable);
    }

    [TestMethod]
    public void AShortcutTheSeamReleasesForTheLayoutIsUnavailableOnTheUiThreadUntilARebindRegistersAnother()
    {
        var started = StartedShortcuts.Start();
        var changed = 0;
        started.Shortcuts.Changed += () => changed++;

        started.Seam.Release(RecordClip, ShortcutReasons.TypesACharacter);

        Assert.AreEqual(0, changed, "nothing on the seam's thread");
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Record));
        started.RunPosted();
        Assert.AreEqual(1, changed, "raised on the UI thread, so the tray and an open page read it (G33)");
        Assert.AreEqual("Register the record shortcut failed: Ctrl+Shift+PrtScn, it would type a character", started.Shortcuts.UnavailableReason(ShortcutAct.Record)?.Sentence);
        Assert.AreEqual(ShortcutAct.Record, started.Shortcuts.FirstUnavailable);

        using (started.Shortcuts.Suspend())
        {
            CollectionAssert.DoesNotContain(started.Seam.Registered.ToList(), RecordClip);
        }

        CollectionAssert.DoesNotContain(started.Seam.Registered.ToList(), RecordClip, "a recording's end leaves it released (G33)");
        using (started.Shortcuts.Suspend())
        {
            Assert.IsTrue(started.Shortcuts.Rebind(ShortcutAct.Record, Free).Succeeded);
        }

        CollectionAssert.AreEquivalent(new[] { Open, TakeScreenshot, Free, StashClipboardImage }, started.Seam.Registered.ToList());
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Record));
        Assert.AreEqual("ImageStash", started.Tray.ToolTip);
    }

    [TestMethod]
    public void ACandidateTheSeamReleasesTheInstantAfterRegisteringItIsNamedUnavailable()
    {
        var started = StartedShortcuts.Start();
        // As the seam's once-a-second check would, under a layout the foreground just switched to.
        started.Seam.AfterRegistering = shortcut =>
        {
            if (shortcut == Free)
            {
                started.Seam.Release(shortcut, ShortcutReasons.TypesACharacter);
            }
        };

        using (started.Shortcuts.Suspend())
        {
            Assert.IsTrue(started.Shortcuts.Rebind(ShortcutAct.Screenshot, Free).Succeeded, "the choice made and written");
        }

        started.RunPosted();

        Assert.AreEqual("Register the screenshot shortcut failed: Ctrl+Shift+F8, it would type a character", started.Shortcuts.UnavailableReason(ShortcutAct.Screenshot)?.Sentence,
            "the release is not lost: the shortcut is unavailable and says why (G33)");
        Assert.AreEqual(started.Shortcuts.UnavailableReason(ShortcutAct.Screenshot)!.Sentence, started.Tray.ToolTip);
        Assert.AreEqual(ShortcutAct.Screenshot, started.Shortcuts.FirstUnavailable, "Settings opens on its row");
        CollectionAssert.DoesNotContain(started.Seam.Registered.ToList(), Free, "and nothing registers it again behind the seam's back");
        Assert.AreEqual("Ctrl+Shift+F8", started.Settings.Current.Shortcuts.Screenshot);
    }

    [TestMethod]
    public void AReleaseNamedOnTheUiThreadAfterARebindOfTheSameShortcutNamesNothing()
    {
        var started = StartedShortcuts.Start();

        started.Seam.Release(RecordClip, ShortcutReasons.TypesACharacter);
        using (started.Shortcuts.Suspend())
        {
            Assert.IsTrue(started.Shortcuts.Rebind(ShortcutAct.Record, Free).Succeeded);
        }

        started.RunPosted();

        Assert.AreEqual("ImageStash", started.Tray.ToolTip, "the release no longer says which shortcut is unavailable (G33)");
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.Record));
        CollectionAssert.Contains(started.Seam.Registered.ToList(), Free);
    }

    [TestMethod]
    public void AReleaseNamedOnTheUiThreadAfterARefusedCandidateStillNamesTheShortcut()
    {
        var started = StartedShortcuts.Start();

        started.Seam.Release(RecordClip, ShortcutReasons.TypesACharacter);
        using (started.Shortcuts.Suspend())
        {
            Assert.IsFalse(started.Shortcuts.Rebind(ShortcutAct.Record, Shortcut.Parse("Ctrl+V").Value!).Succeeded);
        }

        started.RunPosted();

        Assert.AreEqual("Register the record shortcut failed: Ctrl+Shift+PrtScn, it would type a character", started.Tray.ToolTip,
            "a refused candidate changed nothing, so the shortcut is still unavailable and named (G33)");
        CollectionAssert.DoesNotContain(started.Seam.Registered.ToList(), RecordClip);
    }

    [TestMethod]
    public void UnderPauseAnAcceptedCombinationIsWrittenAndShownAndRegisteredOnlyByTheToggleBack()
    {
        var started = StartedShortcuts.Start();
        started.Tray.Entries[Pause].Chosen();

        using (started.Shortcuts.Suspend())
        {
            Assert.IsTrue(started.Shortcuts.Rebind(ShortcutAct.Screenshot, Free).Succeeded, "judged by the seam");
            CollectionAssert.Contains(started.Seam.Asked, Free);
            Assert.IsEmpty(started.Seam.Registered, "and released again at once: none fire under Pause (Done when 5)");
        }

        Assert.IsEmpty(started.Seam.Registered, "the recording's end registers nothing under Pause");
        Assert.AreEqual("Ctrl+Shift+F8", started.Settings.Current.Shortcuts.Screenshot, "written");
        Assert.AreEqual(Free.Gesture, started.Tray.Entries[Screenshot].Gesture, "and shown");
        started.Tray.Entries[Pause].Chosen();

        CollectionAssert.AreEquivalent(new[] { Open, Free, RecordClip, StashClipboardImage }, started.Seam.Registered.ToList(), "the toggle back registers all four, the new one among them");
        foreach (var shortcut in new[] { Open, Free, RecordClip, StashClipboardImage })
        {
            Assert.IsTrue(started.Seam.Press(shortcut));
        }

        started.RunPosted();
        Assert.AreEqual((1, 1, 1, 1), (started.Summoned, started.Screenshots, started.Recorded, started.Stashed), "all fire");
    }

    [TestMethod]
    public void PauseToggledWhileAFieldRecordsLeavesTheEndRegisteringNothingUntilTheToggleBack()
    {
        var started = StartedShortcuts.Start();

        var recording = started.Shortcuts.Suspend();
        started.Tray.Entries[Pause].Chosen();
        recording.Dispose();

        Assert.IsEmpty(started.Seam.Registered, "Pause holds them off");
        started.Tray.Entries[Pause].Chosen();
        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList());

        recording = started.Shortcuts.Suspend();
        started.Tray.Entries[Pause].Chosen();
        started.Tray.Entries[Pause].Chosen();
        Assert.IsEmpty(started.Seam.Registered, "the toggle back during a recording leaves the press to the field");
        recording.Dispose();
        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList(), "and the recording's end registers them");
    }

    [TestMethod]
    public void ARebindOutsideARecordingOrASecondRecordingIsAMistakeOfTheCaller()
    {
        var started = StartedShortcuts.Start();

        Assert.ThrowsExactly<InvalidOperationException>(() => started.Shortcuts.Rebind(ShortcutAct.Screenshot, Free));
        using (started.Shortcuts.Suspend())
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => started.Shortcuts.Suspend());
        }

        CollectionAssert.AreEquivalent(Defaults, started.Seam.Registered.ToList());
    }

    [TestMethod]
    public void ASettingThatIsNotAShortcutIsRegisteredOnceAFieldRecordsOne()
    {
        var started = StartedShortcuts.Start(record: "nonsense");
        Assert.AreEqual("nonsense", started.Shortcuts.TextOf(ShortcutAct.Record));
        Assert.IsNull(started.Tray.Entries[Record].Gesture);

        using (started.Shortcuts.Suspend())
        {
            Assert.IsTrue(started.Shortcuts.Rebind(ShortcutAct.Record, Free).Succeeded);
        }

        Assert.AreEqual("Ctrl+Shift+F8", started.Shortcuts.TextOf(ShortcutAct.Record));
        Assert.AreEqual(Free.Gesture, started.Tray.Entries[Record].Gesture);
        Assert.AreEqual("ImageStash", started.Tray.ToolTip);
        Assert.IsTrue(started.Seam.Press(Free));
        started.RunPosted();
        Assert.AreEqual(1, started.Recorded);
    }

    [TestMethod]
    public void NoReleaseIsAskedOfTheSeamWhileHoldingTheLockAPressWaitsOn()
    {
        var started = StartedShortcuts.Start();
        var pressOpen = started.Seam.PressedCallback(Open);
        var underTheLock = new List<string>();
        var released = 0;
        // A press on another thread, as the seam's own would make it, while each release runs: it must not wait on a lock
        // the caller holds, since the seam's thread is the one the caller waits for.
        started.Seam.DuringRelease = shortcut =>
        {
            released++;
            if (!Task.Run(pressOpen).Wait(TimeSpan.FromSeconds(5)))
            {
                underTheLock.Add(shortcut.Text);
            }
        };

        started.Shortcuts.Suspend().Dispose();
        started.Tray.Entries[Pause].Chosen();
        using (started.Shortcuts.Suspend())
        {
            Assert.IsTrue(started.Shortcuts.Rebind(ShortcutAct.Screenshot, Free).Succeeded);
        }

        Assert.AreEqual(9, released, "the four for the recording, the four for Pause, and the candidate judged under Pause");
        Assert.IsEmpty(underTheLock, "a seam call under the lock would freeze both threads (G33: every other function keeps working)");
    }
}
