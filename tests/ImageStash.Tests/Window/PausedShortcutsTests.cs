using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

[TestClass]
public sealed class PausedShortcutsTests
{
    private static readonly Shortcut Open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
    private static readonly Shortcut Screenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;

    [TestMethod]
    public async Task SettingsResumeClearsTheFooterAndTrayAndChecksEachRegistrationAgain()
    {
        using var walked = await StashWithTray.Open();
        var footer = EmptyStashShown.Over(walked.Listed);
        var page = walked.Page();
        var seam = walked.Listed.ShortcutsSeam;
        var states = new List<bool>();
        page.Changed += () => states.Add(page.ShortcutsPaused);
        walked.Tray.Entries.Single(entry => entry.Header == TrayMenu.PauseHeader).Chosen();
        Assert.AreEqual("Shortcuts are paused", footer.ReadPausedShortcuts());
        Assert.IsTrue(page.ShortcutsPaused);
        Assert.IsEmpty(seam.Registered);
        var before = seam.Registrations;
        seam.Refuse(Screenshot, ShortcutRefusal.HeldByAnotherProgram);

        ControlThread.Run(() =>
        {
            var line = PausedShortcutsLine.ForSettings(page);
            Assert.IsTrue(line.IsVisible, "Settings opened while paused shows the action");
            CollectionAssert.AreEqual(new[] { "Shortcuts are paused", "·" },
                line.Children.OfType<TextBlock>().Select(block => block.Text).ToList());
            Assert.IsTrue(line.Resume.Focusable);
            line.Resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });

        CollectionAssert.AreEqual(new[] { true, false }, states, "the open page heard both transitions");
        Assert.AreEqual(before + 4, seam.Registrations, "every eligible combination went through registration");
        Assert.HasCount(3, seam.Registered);
        Assert.IsFalse(seam.Press(Screenshot));
        Assert.IsFalse(walked.Tray.Entries.Single(entry => entry.Header == TrayMenu.PauseHeader).Checked ?? true);
        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", walked.Tray.ToolTip);
        Assert.IsNull(footer.ReadPausedShortcuts());
        Assert.IsFalse(page.ShortcutsPaused);
        page.ResumeShortcuts();
        Assert.IsFalse(page.ShortcutsPaused, "a stale second Resume cannot switch Pause back on");
        page.Close();
    }

    [TestMethod]
    public void ResumingDuringARecordingFieldWaitsUntilTheFieldEnds()
    {
        var started = StartedShortcuts.Start();
        var page = started.Page();
        var field = page.ShortcutField(ShortcutAct.Screenshot);
        started.Tray.Entries.Single(entry => entry.Header == TrayMenu.PauseHeader).Chosen();
        field.Begin();
        var before = started.Seam.Registrations;

        page.ResumeShortcuts();

        Assert.IsFalse(page.ShortcutsPaused);
        Assert.IsTrue(field.IsRecording, "Resume alone doesn't end the field's recording");
        Assert.AreEqual(before, started.Seam.Registrations, "still suspended");
        Assert.IsFalse(started.Seam.Press(Open));
        Assert.AreEqual(0, started.Summoned);
        field.Cancel();
        Assert.HasCount(4, started.Seam.Registered);
        Assert.IsTrue(started.Seam.Press(Open));
        Assert.AreEqual(1, started.Summoned);
        page.Close();
    }

    [TestMethod]
    public async Task PauseWritesNothingAndAStartFromTheSameSettingsRegistersAgain()
    {
        using var walked = await StashWithTray.Open();
        var listed = walked.Listed;
        var settingsFile = System.IO.Path.Combine(listed.Stash.Outside("settings"), Settings.FileName);
        listed.Settings.Change(settings => settings with { AlsoCopyCapture = false });
        await listed.Settings.Flush();
        var before = File.ReadAllBytes(settingsFile);
        walked.Tray.Entries.Single(entry => entry.Header == TrayMenu.PauseHeader).Chosen();
        Assert.IsEmpty(listed.ShortcutsSeam.Registered);

        using var restarted = await listed.Restart();

        CollectionAssert.AreEqual(before, File.ReadAllBytes(settingsFile));
        Assert.IsFalse(restarted.Shortcuts.Paused);
        Assert.HasCount(4, restarted.ShortcutsSeam.Registered);
    }

    [TestMethod]
    public void FooterGivesExistingControlsFirstClaimAndShowsThePausedLineOnlyIfItFits() => ControlThread.Run(() =>
    {
        var hints = new Border { Width = 180, Height = 16 };
        var copy = new Border { IsVisible = false };
        var busy = new StackPanel { Orientation = Orientation.Horizontal, Width = 130 };
        var count = new TextBlock { Width = 52, IsVisible = false };
        var paused = new Border { Width = 160, Height = 24 };
        var footer = new FooterFit(hints, copy, paused, count, busy);

        var roomy = new Size(620, 36);
        footer.Measure(roomy);
        footer.Arrange(new Rect(roomy));
        Assert.AreEqual(160d, footer.Given.Paused);
        Assert.AreEqual(160d, paused.Bounds.Width);
        Assert.IsTrue(((Control)paused.Parent!).IsVisible);
        var narrow = new Size(460, 36);
        footer.Measure(narrow);
        footer.Arrange(new Rect(narrow));
        Assert.AreEqual(0d, footer.Given.Paused, "the paused line gives way, whole");
        Assert.IsFalse(((Control)paused.Parent!).IsVisible, "hidden, not drawn in no room");
        Assert.AreEqual(180d, footer.Given.Lead);
        Assert.AreEqual(130d, footer.Given.Busy);
    });
}
