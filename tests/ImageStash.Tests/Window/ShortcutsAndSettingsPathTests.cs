using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// One walk through shortcuts and Settings over a real store, empty at first, with the production policy, tray menu,
/// notifications above the tray and Settings page on one recording Shortcuts seam and one recording tray, another program
/// holding the screenshot shortcut's default from before the start: each step observes, through the product's own
/// entries, what a person sees.
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
        var empty = EmptyStashShown.Over(listed);

        // (3) At that start, with no change raised, one notification says the screenshot shortcut is taken and leads to
        // its row; the tray menu's entry, the + menu's and the empty stash's row each say so beside the shortcut; settings
        // applied again while it is still held say nothing more and leave its one line in the tooltip.
        var said = listed.Notifications.Shown.Single();
        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", said.Text, "G33, G16");
        await NoticeButton.Press(listed.Notifications, said);
        CollectionAssert.AreEqual(new ShortcutAct?[] { ShortcutAct.Screenshot }, walked.SettingsAskedOn, "Choose another asks for Settings on Take a screenshot");
        Assert.AreEqual("Take a screenshot (shortcut unavailable)", walked.Tray.HeaderOf(TrayMenu.TakeScreenshotHeader), "the tray menu's entry (G34)");
        Assert.AreEqual("Take a screenshot (shortcut unavailable)", PlusMenuScreenshot(listed), "the + menu's");
        Assert.AreEqual(ShortcutWords.UnavailableBesideKeys, empty.ReadRows().Single(row => row.Words == "Take a screenshot").Beside, "the empty stash's row (G2)");

        listed.Shortcuts.BindFromSettings();

        Assert.AreSame(said, listed.Notifications.Shown.Single(), "settings applied again while it is held: no second notification");

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

        // (3) After the remap, nothing says unavailable any more, and the notification that said so is gone.
        Assert.AreEqual("Take a screenshot", walked.Tray.HeaderOf(TrayMenu.TakeScreenshotHeader), "the tray menu's entry at its next opening");
        Assert.AreEqual("Take a screenshot", PlusMenuScreenshot(listed), "the + menu's at its next opening");
        Assert.IsTrue(empty.ReadRows().All(row => row.Beside is null), "the empty stash's rows at once");
        Assert.AreSame(said, listed.Notifications.Shown.Single(), "the notification keeps its own six seconds (G16), and nothing more is said");

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

        // (4) With pictures in the stash, the page reads from Shortcuts to the group holding Export stash… and Import
        // stash…, every row of G32's list once, the running version at its foot. A chosen export is offered with the whole
        // stash's count, a search narrowing the grid notwithstanding, and the file's; Export first writes the current stash
        // to the file named and leaves the offer standing, nothing replaced; Replace then replaces the stash.
        foreach (var name in new[] { "cat", "dog", "owl" })
        {
            await listed.Stash.Import(name);
        }

        var groups = page.Rows.Select(row => row.Group).Distinct().ToList();
        Assert.AreEqual("Shortcuts", groups[0], "Shortcuts first");
        CollectionAssert.AreEqual(new[] { SettingsPage.ExportTitle, SettingsPage.ImportTitle }, page.Rows.Where(row => row.Group == groups[^1]).Select(row => row.Title).ToList(), "the stash's replacement last");
        CollectionAssert.AreEquivalent(
            new[]
            {
                SettingsPage.OpenTitle, SettingsPage.TakeScreenshotTitle, SettingsPage.RecordClipTitle, SettingsPage.StashClipboardImageTitle, SettingsPage.AlsoCopyTitle,
                SettingsPage.MaximumLengthTitle, SettingsPage.RecordPointerTitle, SettingsPage.StashFolderTitle, SettingsPage.ExportTitle, SettingsPage.ImportTitle,
                SettingsPage.StartWithWindowsTitle, SettingsPage.ThemeTitle, SettingsPage.ThumbnailSizeTitle,
            },
            page.Rows.Select(row => row.Title).ToList(), "every row of G32's list, once");
        Assert.AreEqual($"ImageStash {RunningBuild.Version}", SettingsPage.VersionLine, "the running version at its foot");

        await listed.Listing.SetQuery("owl");
        Assert.HasCount(1, listed.Listing.Items, "the search narrows the grid");
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "one", "two");
        listed.Dialogs.OpenFileAnswer = () => Task.FromResult<string?>(archive);
        Assert.IsTrue((await page.Import(null!)).Succeeded);
        var offered = page.ImportOffer;
        StringAssert.StartsWith(offered, "Replace your 3 items with the 2 in export.zip? ", "the whole stash's count and the file's (G36)");

        var before = listed.Stash.Outside("before replacing.zip");
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(before);
        var exported = await page.Export(null!);

        Assert.IsTrue(exported.Succeeded, exported.Failure?.Sentence);
        Assert.AreEqual(3, StashImport.Summary(before, TestStash.ShortRetry).Value!.Items, "Export first wrote the current stash to the file named");
        Assert.AreEqual(offered, page.ImportOffer, "the offer stands");
        Assert.HasCount(3, await listed.Stash.All(), "nothing replaced");

        var replaced = await page.ConfirmImport();

        Assert.IsTrue(replaced.Succeeded, replaced.Failure?.Sentence);
        CollectionAssert.AreEquivalent(new[] { "one", "two" }, (await listed.Stash.All()).Select(item => item.Name).ToList(), "Replace replaced the stash");
    }

    /// <summary>The <c>+</c> menu's Take a screenshot as the window makes the menu over these shortcuts, at an opening.</summary>
    private static string PlusMenuScreenshot(ListedStash listed)
    {
        var header = "";
        ControlThread.Run(() =>
        {
            var button = AddMenu.Button(listed.Settings, act => listed.Shortcuts.UnavailableReason(act) is not null, listed.Paste, () => { }, () => { }, () => { });
            header = (string)((MenuFlyout)button.Flyout!).Items.OfType<MenuItem>().ElementAt(2).Header!;
        });
        return header;
    }
}
