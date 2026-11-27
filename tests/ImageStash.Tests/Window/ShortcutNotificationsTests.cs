using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The notification above the tray that a shortcut became unavailable (G33, G16), over the production policy and tray
/// menu as the app wires them: raised at start for a combination another program holds and at once when one becomes
/// unavailable later, in the tooltip's words, with Choose another asking for Settings on that shortcut's own row; once
/// while the shortcut stays unavailable for the same combination and kind of reason, however often a registration judges
/// it again, and again when the combination or the kind differs or it worked in between; two at once, two notifications.
/// </summary>
[TestClass]
public sealed class ShortcutNotificationsTests
{
    private static readonly Shortcut TakeScreenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;
    private static readonly Shortcut RecordClip = Shortcut.Parse("Ctrl+Shift+PrtScn").Value!;
    private static readonly Shortcut Open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
    private static readonly Shortcut Free = Shortcut.Parse("Ctrl+Shift+F8").Value!;

    [TestMethod]
    public void AShortcutHeldAtStartIsSaidAboveTheTrayWithChooseAnotherLeadingToItsRow()
    {
        var started = StartedShortcuts.Start(held: TakeScreenshot);

        var said = started.Notifications.Shown.Single();
        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", said.Text, "the act, the combination and why (G33)");
        Assert.AreEqual(ShortcutNotifications.ChooseAnother, said.Button?.Label);
        Assert.IsNull(said.UndoLabel, "nothing to undo");
        Assert.DoesNotContain("failed", said.Text, "guidance, never a failure's sentence");

        NoticeButton.Press(started.Notifications, said).Wait();

        CollectionAssert.AreEqual(new ShortcutAct?[] { ShortcutAct.Screenshot }, started.SettingsAskedOn, "Settings asked for on Take a screenshot's row");
        Assert.AreSame(said, started.Notifications.Shown.Single(), "the button leaves the notification showing");
    }

    [TestMethod]
    public void ItGoesThroughTheTraysNotificationsAndStaysPastItsTimeUntilItsClose()
    {
        var started = StartedShortcuts.Start(held: TakeScreenshot);
        var said = started.Notifications.Shown.Single();

        started.Clock.Advance(Notices.LastingAtMost + TimeSpan.FromMinutes(1));
        started.Notifications.Sweep();
        Assert.AreSame(said, started.Notifications.Shown.Single(), "a shortcut's problem names a failure and stays above the tray (G16)");

        started.Notifications.Close(said);
        Assert.IsEmpty(started.Notifications.Shown, "until its ×");
    }

    [TestMethod]
    public void AShortcutThatBecomesUnavailableLaterIsSaidAtOnce()
    {
        var started = StartedShortcuts.Start();
        Assert.IsEmpty(started.Notifications.Shown, "nothing to say while every shortcut works");

        started.Seam.Release(Open, ShortcutRefusal.TypesACharacter("@"));
        started.RunPosted();

        Assert.AreEqual("Ctrl+Alt+Space types @, so Open ImageStash has no shortcut.", started.Notifications.Shown.Single().Text, "the layout's character named (G33)");
    }

    [TestMethod]
    public void NothingMoreIsSaidWhileTheShortcutStaysUnavailableHoweverOftenItIsJudgedAgain()
    {
        var started = StartedShortcuts.Start(held: TakeScreenshot);
        var raised = Raised(started.Notifications);

        started.Shortcuts.TogglePause();
        started.Shortcuts.TogglePause();
        using (started.Shortcuts.Suspend())
        {
            Assert.IsNotNull(started.Shortcuts.Rebind(ShortcutAct.Screenshot, RecordClip), "another act's combination refused while the field records");
        }

        started.Shortcuts.BindFromSettings();
        started.Shortcuts.BindFromSettings();

        Assert.IsEmpty(raised, "Pause toggled back, a field's end and settings applied twice say nothing more");
        Assert.HasCount(1, started.Notifications.Shown, "the first notification still the one showing");
        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", started.Tray.ToolTip, "and the tooltip one whole line");
    }

    [TestMethod]
    public void TheLayoutTypingAnotherCharacterIsNoNewReason()
    {
        var started = StartedShortcuts.Start();
        started.Seam.Release(Open, ShortcutRefusal.TypesACharacter("@"));
        started.RunPosted();
        var raised = Raised(started.Notifications);

        started.Seam.Refuse(Open, ShortcutRefusal.TypesACharacter("€"));
        started.Shortcuts.BindFromSettings();

        Assert.IsEmpty(raised, "the character is detail, never a new reason");
        Assert.AreEqual("Ctrl+Alt+Space types €, so Open ImageStash has no shortcut.", started.Tray.ToolTip, "the tooltip names the character found now");
    }

    [TestMethod]
    public void AnotherCombinationOrAnotherKindOfReasonIsSaidAgain()
    {
        var started = StartedShortcuts.Start(held: TakeScreenshot);
        var raised = Raised(started.Notifications);

        // Remapped by imported settings to a combination another program holds too.
        started.Seam.Refuse(Shortcut.Parse("Ctrl+F9").Value!, ShortcutRefusal.HeldByAnotherProgram);
        started.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Screenshot = "Ctrl+F9" } });
        started.Shortcuts.BindFromSettings();

        Assert.AreEqual("Ctrl+F9 is used by another app, so Take a screenshot has no shortcut.", raised.Single().Text, "the new combination said");
        Assert.AreSame(raised.Single(), started.Notifications.Shown.Single(), "in place of the earlier one of the same shortcut, which said what no longer holds (G16)");

        // Then typing rather than held, the same combination.
        started.Seam.Refuse(Shortcut.Parse("Ctrl+F9").Value!, ShortcutRefusal.TypesACharacter("x"));
        started.Shortcuts.BindFromSettings();

        Assert.HasCount(2, raised, "a new kind of reason said");
        Assert.AreEqual("Ctrl+F9 types x, so Take a screenshot has no shortcut.", raised[1].Text);
    }

    [TestMethod]
    public void AShortcutThatWorkedInBetweenIsSaidAgainWhenItIsUnavailableAgain()
    {
        var started = StartedShortcuts.Start(held: TakeScreenshot);
        var raised = Raised(started.Notifications);

        // The holder lets go and imported settings bind it again: it works.
        started.Seam.Free(TakeScreenshot);
        started.Shortcuts.BindFromSettings();
        Assert.IsTrue(started.Seam.Press(TakeScreenshot), "registered");
        Assert.IsEmpty(raised, "working says nothing");

        started.Seam.Refuse(TakeScreenshot, ShortcutRefusal.HeldByAnotherProgram);
        started.Shortcuts.BindFromSettings();

        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", raised.Single().Text, "said again: the person was told it works");
    }

    [TestMethod]
    public void ARemapToAFreeCombinationLeavesTheNotificationItsSixSeconds()
    {
        var started = StartedShortcuts.Start(held: TakeScreenshot);
        var said = started.Notifications.Shown.Single();

        using (started.Shortcuts.Suspend())
        {
            Assert.IsNull(started.Shortcuts.Rebind(ShortcutAct.Screenshot, Free));
        }

        started.Notifications.Sweep();
        Assert.AreSame(said, started.Notifications.Shown.Single(), "a notification stays its time (G16), Choose another's remap notwithstanding");
    }

    [TestMethod]
    public void UnderPauseSettingsAppliedAgainAndTheToggleBackSayNothingMore()
    {
        var started = StartedShortcuts.Start(held: TakeScreenshot);
        var raised = Raised(started.Notifications);

        started.Shortcuts.TogglePause();
        started.Shortcuts.BindFromSettings();
        started.Shortcuts.TogglePause();

        Assert.IsNotNull(started.Shortcuts.UnavailableReason(ShortcutAct.Screenshot), "judged again at the toggle back, still held");
        Assert.IsEmpty(raised, "it never worked, nor was it changed: nothing more to say");
        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", started.Tray.ToolTip, "its one line");
    }

    [TestMethod]
    public void RemappedUnderPauseThenBroughtBackByAnImportItIsSaidAgainAtTheToggleBack()
    {
        var started = StartedShortcuts.Start(held: TakeScreenshot);
        var raised = Raised(started.Notifications);
        started.Shortcuts.TogglePause();
        using (started.Shortcuts.Suspend())
        {
            Assert.IsNull(started.Shortcuts.Rebind(ShortcutAct.Screenshot, Free), "changed in Settings");
        }

        started.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Screenshot = "Ctrl+PrtScn" } });
        started.Shortcuts.BindFromSettings();
        started.Shortcuts.TogglePause();

        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", raised.Single().Text, "changed since it was said, so said again");
    }

    [TestMethod]
    public void TwoUnavailableAtOnceAreEachSaidEachLeadingToItsOwnRow()
    {
        var started = StartedShortcuts.Start(held: [TakeScreenshot, RecordClip]);

        var shown = started.Notifications.Shown;
        CollectionAssert.AreEqual(new[]
        {
            "Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.",
            "Ctrl+Shift+PrtScn is used by another app, so Record a clip has no shortcut.",
        }, shown.Select(notice => notice.Text).ToList(), "both shown above the tray, neither in the other's place");

        NoticeButton.Press(started.Notifications, shown[1]).Wait();
        NoticeButton.Press(started.Notifications, shown[0]).Wait();

        CollectionAssert.AreEqual(new ShortcutAct?[] { ShortcutAct.Record, ShortcutAct.Screenshot }, started.SettingsAskedOn, "each asks for its own act's row, not the first unavailable one");
    }

    /// <summary>Every notice raised from now on, in order, whether or not it still shows.</summary>
    private static List<Notice> Raised(Notices notifications)
    {
        var raised = new List<Notice>();
        var before = notifications.Shown.ToHashSet();
        notifications.Changed += () => raised.AddRange(notifications.Shown.Where(notice => before.Add(notice)));
        return raised;
    }
}
