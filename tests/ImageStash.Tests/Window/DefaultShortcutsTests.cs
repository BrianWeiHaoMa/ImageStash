using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// F5's four defaults, which a first run registers with nothing named; and the combination an earlier build's default
/// gave Stash clipboard image, which a settings file or an export that holds it keeps, registered and shown as held,
/// until it is changed in Settings (G31, G36): only a missing value takes today's default.
/// </summary>
[TestClass]
public sealed class DefaultShortcutsTests
{
    private const string EarlierDefault = "Ctrl+Alt+V";

    [TestMethod]
    public void AFirstRunRegistersTheFourDefaultsWithNothingNamedAndSettingsShowsEach()
    {
        var started = StartedShortcuts.Start();

        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+Space", "Ctrl+PrtScn", "Ctrl+Shift+PrtScn", "Win+Alt+V" }, started.Seam.Registered.Select(shortcut => shortcut.Text).ToList(),
            "F5's four, in its order, none refused or typing a character, so none named (G33)");
        Assert.IsNull(started.Shortcuts.FirstUnavailable);
        Assert.AreEqual("ImageStash · Ctrl+Alt+Space", started.Tray.ToolTip);
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+Space", "Ctrl+PrtScn", "Ctrl+Shift+PrtScn", "Win+Alt+V" }, started.Page().ShortcutFields.Select(field => field.Text).ToList());
    }

    [TestMethod]
    public void TheEarlierDefaultASettingsFileHoldsIsRegisteredAndShownAsHeldAndTheFileKeepsIt()
    {
        using var scratch = new ScratchFolder();
        var started = StartedShortcuts.Start(stash: EarlierDefault, settingsFolder: scratch.Path, quiet: TimeSpan.Zero);

        CollectionAssert.Contains(started.Seam.Registered.ToList(), Shortcut.Parse(EarlierDefault).Value!, "the combination the file holds is the one registered");
        CollectionAssert.DoesNotContain(started.Seam.Registered.ToList(), Shortcut.Parse("Win+Alt+V").Value!, "today's default is not registered in its place");
        Assert.AreEqual(new KeyGesture(Key.V, KeyModifiers.Control | KeyModifiers.Alt), started.Tray.Entries[3].Gesture);
        Assert.AreEqual(EarlierDefault, started.Page().ShortcutField(ShortcutAct.StashClipboardImage).Text);
        Assert.AreEqual(EarlierDefault, EmptyStashWaysIn.Of(started.Settings.Current.Shortcuts, new WaysInActs(() => { }, () => { }, () => { }, () => { }, () => { }), _ => false)
            .Single(way => way.Words == "Stash the clipboard image").Shortcut);

        started.Settings.Change(settings => settings with { RecordPointer = false });
        started.Settings.Flush().Wait();
        Assert.AreEqual(EarlierDefault, started.Saved.StashClipboardImage, "kept when the file is written again");
    }

    [TestMethod]
    public async Task AnExportThatCarriesTheEarlierDefaultImportsWithItAndRegistersIt()
    {
        using var listed = await ListedStash.Open();
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), settings => settings with { Shortcuts = settings.Shortcuts with { StashClipboardImage = EarlierDefault } }, "one");
        Assert.AreEqual("Win+Alt+V", listed.Settings.Current.Shortcuts.StashClipboardImage, "this PC's stash on today's default");

        var imported = await listed.ImportStash(archive);

        Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence);
        Assert.AreEqual(EarlierDefault, listed.Settings.Current.Shortcuts.StashClipboardImage, "as it was on the PC that exported it (G36)");
        Assert.AreEqual(EarlierDefault, listed.Shortcuts.ShortcutOf(ShortcutAct.StashClipboardImage)?.Text);
        CollectionAssert.Contains(listed.ShortcutsSeam.Registered.ToList(), Shortcut.Parse(EarlierDefault).Value!);
    }
}
