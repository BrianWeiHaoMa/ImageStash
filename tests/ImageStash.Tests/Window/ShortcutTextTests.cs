using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// How a shortcut's text is written wherever it is shown (F5): Win first, then Ctrl, Alt and Shift, then the key, so
/// Stash clipboard image's default reads exactly Win+Alt+V in Settings' field and on the empty stash's row; a combination
/// stored or typed with its modifiers in any order is the same shortcut, shown in that order, and the settings file keeps
/// what it holds until the shortcut is changed in Settings (G31, G32).
/// </summary>
[TestClass]
public sealed class ShortcutTextTests
{
    [TestMethod]
    [DataRow("Win+Alt+V")]
    [DataRow("Alt+Win+V")]
    [DataRow("alt+win+v")]
    [DataRow(" WIN + ALT + V ")]
    public void WinAltVInAnyOrderOrCaseIsOneShortcutWrittenWinAltV(string text)
    {
        var shortcut = Shortcut.Parse(text).Value!;

        Assert.AreEqual(new KeyGesture(Key.V, KeyModifiers.Meta | KeyModifiers.Alt), shortcut.Gesture);
        Assert.AreEqual("Win+Alt+V", shortcut.Text, "F5's spelling");
        Assert.AreEqual(shortcut, Shortcut.Parse(shortcut.Text).Value, "its text reads back as the same shortcut");
    }

    [TestMethod]
    [DataRow("Shift+Alt+Ctrl+Win+F1", "Win+Ctrl+Alt+Shift+F1")]
    [DataRow("ctrl+win+d", "Win+Ctrl+D")]
    [DataRow("Shift+Win+S", "Win+Shift+S")]
    [DataRow("Alt+Ctrl+Space", "Ctrl+Alt+Space")]
    [DataRow("shift+ctrl+prtscn", "Ctrl+Shift+PrtScn")]
    public void EveryCombinationIsWrittenWinThenCtrlAltAndShiftThenItsKey(string text, string written)
    {
        var shortcut = Shortcut.Parse(text).Value!;

        Assert.AreEqual(written, shortcut.Text);
        Assert.AreEqual(shortcut, Shortcut.Parse(written).Value);
    }

    [TestMethod]
    public void TheDefaultReadsWinAltVInSettingsFieldAndOnTheEmptyStashsRowAndIsRegisteredAsSuch()
    {
        var started = StartedShortcuts.Start();

        Assert.AreEqual("Win+Alt+V", new Shortcuts().StashClipboardImage, "F5's default as F5 writes it");
        Assert.AreEqual("Win+Alt+V", started.Page().ShortcutField(ShortcutAct.StashClipboardImage).Text);
        Assert.AreEqual("Win+Alt+V", WayOf(new Shortcuts(), "Stash the clipboard image").Shortcut);
        Assert.AreEqual(new KeyGesture(Key.V, KeyModifiers.Meta | KeyModifiers.Alt), started.Tray.Entries[3].Gesture, "the tray menu is handed the same combination");
        CollectionAssert.Contains(started.Seam.Registered.ToList(), Shortcut.Parse("Win+Alt+V").Value!);
        Assert.IsNull(started.Shortcuts.UnavailableReason(ShortcutAct.StashClipboardImage));
    }

    [TestMethod]
    public void ACombinationStoredInAnotherOrderIsShownInTheOneOrderAndTheFileKeepsIt()
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(open: "space+alt+ctrl", stash: "alt+win+v", settingsFolder: scratch.Path, quiet: TimeSpan.Zero);
        var page = started.Page();

        Assert.AreEqual(("Ctrl+Alt+Space", "Win+Alt+V"), (page.ShortcutField(ShortcutAct.Open).Text, page.ShortcutField(ShortcutAct.StashClipboardImage).Text));
        Assert.AreEqual("Win+Alt+V", WayOf(started.Settings.Current.Shortcuts, "Stash the clipboard image").Shortcut);
        Assert.AreEqual("ImageStash · Ctrl+Alt+Space", started.Tray.ToolTip);
        CollectionAssert.Contains(started.Seam.Registered.ToList(), Shortcut.Parse("Win+Alt+V").Value!, "the same shortcut registered");

        started.Settings.Change(settings => settings with { Theme = Theme.Dark });
        started.Settings.Flush().Wait();
        Assert.AreEqual(("space+alt+ctrl", "alt+win+v"), (started.Saved.Open, started.Saved.StashClipboardImage), "not rewritten in the file by another setting's write");
    }

    [TestMethod]
    public void ACombinationRecordedWithTheWinKeyInSettingsIsShownAndWrittenWinFirst()
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(settingsFolder: scratch.Path, quiet: TimeSpan.Zero);
        var field = started.Page().ShortcutField(ShortcutAct.Record);

        field.Begin();
        field.Press(Key.F1, KeyModifiers.Shift | KeyModifiers.Meta);
        started.Settings.Flush().Wait();

        Assert.AreEqual("Win+Shift+F1", field.Text);
        Assert.AreEqual("Win+Shift+F1", started.Saved.Record);
        Assert.AreEqual(new KeyGesture(Key.F1, KeyModifiers.Shift | KeyModifiers.Meta), started.Tray.Entries[2].Gesture);
    }

    [TestMethod]
    public void AnUnavailableShortcutStoredInAnotherOrderIsNamedInTheOneOrderInTheTooltipAndOnItsRow()
    {
        var started = StartedShortcuts.Start(open: "alt+win+f9", held: Shortcut.Parse("Win+Alt+F9").Value!);
        Assert.AreEqual("Win+Alt+F9 is used by another app, so Open ImageStash has no shortcut.", started.Tray.ToolTip, "the tray icon says which shortcut in the words its field shows (G33)");
        Assert.AreEqual("Win+Alt+F9 is already used by another app. Press a different combination.", started.Page().WarningOf(SettingsPage.OpenTitle), "and so does its row");
        Assert.AreEqual("Win+Alt+F9", started.Page().ShortcutField(ShortcutAct.Open).Text);
    }

    private static WayIn WayOf(Shortcuts shortcuts, string words) =>
        EmptyStashWaysIn.Of(shortcuts, new WaysInActs(() => { }, () => { }, () => { }, () => { }, () => { }), _ => false).Single(way => way.Words == words);
}
