using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Whether a global shortcut would act if pressed now (G33), the one judgment the tray's tooltip and the recording's bar
/// ask: its setting a shortcut, Pause off, and not left unregistered because another program holds it or it would type
/// a character; the moment a Settings field holds the shortcuts released while it records a press is not chased.
/// </summary>
[TestClass]
public sealed class ActingShortcutTests
{
    private static readonly Shortcut Record = Shortcut.Parse(new Shortcuts().Record).Value!;

    [TestMethod]
    public void ARegisteredShortcutActsAndPauseTakesThatAwayUntilItsToggleBack()
    {
        var started = StartedShortcuts.Start();

        Assert.AreEqual(Record, ActingShortcut.Of(started.Shortcuts, ShortcutAct.Record), "registered, it would act");
        started.Shortcuts.TogglePause();
        Assert.IsNull(ActingShortcut.Of(started.Shortcuts, ShortcutAct.Record), "paused, no press acts (G33)");
        Assert.IsNull(ActingShortcut.Of(started.Shortcuts, ShortcutAct.Open), "every shortcut alike");
        started.Shortcuts.TogglePause();
        Assert.AreEqual(Record, ActingShortcut.Of(started.Shortcuts, ShortcutAct.Record), "toggled back, it acts again");
    }

    [TestMethod]
    public void AShortcutAnotherProgramHoldsDoesNotActAndTheOthersStillDo()
    {
        var started = StartedShortcuts.Start(held: Record);

        Assert.IsNull(ActingShortcut.Of(started.Shortcuts, ShortcutAct.Record), "left unregistered at start (G33)");
        Assert.AreEqual(Shortcut.Parse(new Shortcuts().Open).Value, ActingShortcut.Of(started.Shortcuts, ShortcutAct.Open));
    }

    [TestMethod]
    public void AShortcutReleasedBecauseItWouldTypeACharacterDoesNotAct()
    {
        var started = StartedShortcuts.Start();

        started.Seam.Release(Record, ShortcutRefusal.TypesACharacter(null));
        foreach (var posted in started.Posted.ToList())
        {
            posted();
        }

        Assert.IsNull(ActingShortcut.Of(started.Shortcuts, ShortcutAct.Record), "released for the layout, until changed in Settings (G33)");
    }

    [TestMethod]
    public void ASettingThatIsNoShortcutDoesNotAct()
    {
        var started = StartedShortcuts.Start(record: "Ctrl+Shift");

        Assert.IsNull(ActingShortcut.Of(started.Shortcuts, ShortcutAct.Record));
    }

    [TestMethod]
    public void WhileASettingsFieldRecordsAPressTheShortcutStillCountsAsActing()
    {
        var started = StartedShortcuts.Start();

        using (started.Shortcuts.Suspend())
        {
            Assert.AreEqual(Record, ActingShortcut.Of(started.Shortcuts, ShortcutAct.Record), "the moment a field holds them released is not chased");
        }
    }
}
