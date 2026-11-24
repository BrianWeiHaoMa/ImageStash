using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// One walk through shortcuts and Settings over a real store, empty at first, with the production policy, tray menu and
/// Settings page on one recording Shortcuts seam and one recording tray, another program holding the screenshot
/// shortcut's default from before the start: each step observes, through the product's own entries, what a person sees.
/// </summary>
[TestClass]
public sealed class ShortcutsAndSettingsPathTests
{
    private static readonly Shortcut TakeScreenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;
    private static readonly Shortcut StashClipboardImage = Shortcut.Parse("Win+Alt+V").Value!;

    [TestMethod]
    public async Task AWalkThroughShortcutsAndSettings()
    {
        using var walked = await StashWithTray.Open(held: TakeScreenshot);
        var listed = walked.Listed;
        Assert.IsEmpty(listed.Listing.Items, "the walk begins on an empty stash");

        // (1) The row and the tooltip say, in one sentence of guidance, which combination is taken and why, the tooltip
        // the act too; the field keeps recording through a refusal; a free combination taken clears both.
        var page = walked.Page();
        Assert.AreEqual("Ctrl+PrtScn is already used by another app. Press a different combination.", page.WarningOf(SettingsPage.TakeScreenshotTitle), "G32, G35");
        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", walked.Tray.ToolTip, "G33");
        Assert.DoesNotContain("failed", walked.Tray.ToolTip!, "guidance, never a failure's sentence");

        var field = page.ShortcutField(ShortcutAct.Screenshot);
        field.Begin();
        Assert.IsFalse(listed.ShortcutsSeam.Press(StashClipboardImage), "released while the field records: the press reaches the field");
        field.Press(Key.V, KeyModifiers.Meta | KeyModifiers.Alt);

        Assert.AreEqual("Stash clipboard image already uses Win+Alt+V.", page.WarningOf(SettingsPage.TakeScreenshotTitle), "the row says so (G32)");
        Assert.IsEmpty(listed.ShortcutActsRun, "the other act did not run");
        Assert.IsTrue(field.IsRecording, "the field still records");
        Assert.IsEmpty(listed.ShortcutsSeam.Registered, "every shortcut still released while it records");

        field.Press(Key.F8, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.IsFalse(field.IsRecording, "a free combination taken ends the recording");
        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle), "the row's warning gone");
        Assert.AreEqual("ImageStash · Ctrl+Alt+Space", walked.Tray.ToolTip, "the tooltip's line gone, the open shortcut beside the name again");
        Assert.AreEqual("Ctrl+Shift+F8", listed.Settings.Current.Shortcuts.Screenshot, "written");
        Assert.IsTrue(listed.ShortcutsSeam.Press(Shortcut.Parse("Ctrl+Shift+F8").Value!), "registered");
        Assert.IsTrue(listed.ShortcutsSeam.Press(StashClipboardImage), "and the others registered again");
        CollectionAssert.AreEqual(new[] { ShortcutAct.Screenshot, ShortcutAct.StashClipboardImage }, listed.ShortcutActsRun);

        // (2) Recording again, Delete alone is refused in its own words before anything is registered, and the field
        // still records; after Esc, with the default no longer held, the row's Reset puts it back, and leaves the row.
        Assert.IsTrue(field.ResetOffered, "a row off its default offers Reset");
        field.Begin();
        var asked = listed.ShortcutsSeam.Asked.Count;
        field.Press(Key.Delete, KeyModifiers.None);

        Assert.AreEqual("Add Ctrl, Alt or Win: Delete alone is needed by every app.", page.WarningOf(SettingsPage.TakeScreenshotTitle), "G32 as amended");
        Assert.HasCount(asked, listed.ShortcutsSeam.Asked, "nothing asked of the seam");
        Assert.IsEmpty(listed.ShortcutsSeam.Registered, "nothing registered");
        Assert.IsTrue(field.IsRecording, "the field still records");

        field.Cancel();
        listed.ShortcutsSeam.Free(TakeScreenshot);
        field.Reset();

        Assert.IsTrue(listed.ShortcutsSeam.Press(TakeScreenshot), "Ctrl+PrtScn registered");
        Assert.AreEqual("Ctrl+PrtScn", listed.Settings.Current.Shortcuts.Screenshot, "and written");
        Assert.IsFalse(field.ResetOffered, "Reset offered no more");
        Assert.IsNull(page.WarningOf(SettingsPage.TakeScreenshotTitle));
        Assert.HasCount(4, listed.ShortcutsSeam.Registered, "every shortcut registered, no field recording");
    }
}
