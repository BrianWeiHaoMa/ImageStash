using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests;

/// <summary>
/// F1: start registers ImageStash while Start with Windows is on, removes it while off, and leaves it alone while the
/// setting is unknown. G35: a stash that cannot be opened at start is named in the tray tooltip; a settings write refused
/// after its retry is named in the stash window's notices, and on the Settings page while it is open.
/// </summary>
[TestClass]
public sealed class AppTests
{
    private const string Executable = @"C:\Apps\ImageStash\ImageStash.Windows.exe";
    private static readonly TimeSpan ShortRetry = TimeSpan.FromMilliseconds(300);

    [TestMethod]
    public void ARefusedSettingsWriteWithNoSettingsPageOpenIsNamedInTheNoticesAlone()
    {
        var notices = new Notices(new SetClock());
        var refused = new Failure("Save settings", Settings.FileName, "the file is held by another program");

        App.SettingsWriteFailed(refused, notices, openPage: null);

        CollectionAssert.AreEqual(new[] { refused.Sentence }, notices.Shown.Select(notice => notice.Text).ToList());
    }

    [TestMethod]
    public async Task AStartThatFinishesAKilledImportPutsItsThemeShortcutsAndStartWithWindowsInEffect()
    {
        using var listed = await ListedStash.Open();
        var imported = new Shortcuts { Open = "Ctrl+Alt+F2", Screenshot = "Ctrl+Shift+F6", Record = "Ctrl+Shift+F7", StashClipboardImage = "Ctrl+Alt+F3" };
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), s => s with { Shortcuts = imported, Theme = Theme.Dark, StartWithWindows = false }, "one");
        // The replaced folder's recycle refused now and by the recovery after it: the record stands for the next start.
        listed.Stash.RecycleBin.Refusing = _ => true;
        Assert.IsFalse((await listed.ImportStash(archive)).Succeeded);
        await listed.Settings.Flush();
        listed.Stash.RecycleBin.Refusing = _ => false;
        var (keeper, shortcuts, seam, run, themes) = Started(listed.Stash);
        var applied = TravellingSettings.Of(keeper.Current);
        Assert.IsNotNull(keeper.Current.ImportPending, "a record on disk at the start");

        CollectionAssert.AreEqual(Array.Empty<Failure>(), (await StashImport.FinishAtStart(keeper, listed.Stash.RecycleBin, listed.Stash.Folder.Root, ShortRetry)).ToList());
        var outcome = App.ApplyFinishedImport(applied, keeper.Current, settings => App.ApplyTravelling(settings, themes.Add, shortcuts, run, Executable));

        Assert.IsTrue(outcome.Succeeded, outcome.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { Theme.Dark }, themes, "the imported theme in effect (G36, F10)");
        CollectionAssert.AreEquivalent(new[] { "Ctrl+Alt+F2", "Ctrl+Shift+F6", "Ctrl+Shift+F7", "Ctrl+Alt+F3" }, seam.Registered.Select(shortcut => shortcut.Text).ToList(),
            "the imported four registered, the four the start registered from the file read before released (G33)");
        CollectionAssert.AreEqual(new[] { "clear" }, run.Calls, "the Run entry cleared per the import (F1)");
    }

    [TestMethod]
    public async Task AStartWithNoImportToFinishAppliesNothingAgain()
    {
        using var listed = await ListedStash.Open();
        await listed.Settings.Flush();
        var (keeper, shortcuts, seam, run, themes) = Started(listed.Stash);
        var applied = TravellingSettings.Of(keeper.Current);
        var registrations = seam.Registrations;

        await StashImport.FinishAtStart(keeper, listed.Stash.RecycleBin, listed.Stash.Folder.Root, ShortRetry);
        var outcome = App.ApplyFinishedImport(applied, keeper.Current, settings => App.ApplyTravelling(settings, themes.Add, shortcuts, run, Executable));

        Assert.IsTrue(outcome.Succeeded);
        Assert.IsEmpty(themes);
        Assert.IsEmpty(run.Calls);
        Assert.AreEqual(registrations, seam.Registrations, "nothing registered again");
    }

    /// <summary>What the start has made before it finishes an import: the settings read from the file beside the stash, and the four shortcuts registered from them.</summary>
    private static (SettingsKeeper Keeper, GlobalShortcuts Shortcuts, RecordingShortcuts Seam, RecordingStartWithWindows Run, List<Theme> Themes) Started(TestStash stash)
    {
        var keeper = MovingStash.Keeper(stash, []);
        var seam = new RecordingShortcuts();
        var shortcuts = new GlobalShortcuts(keeper, GlobalShortcuts.Bindings(() => { }, () => { }, () => { }, () => { }), seam, _ => { }, action => action());
        shortcuts.Start();
        return (keeper, shortcuts, seam, new RecordingStartWithWindows(), []);
    }

    [TestMethod]
    public void OnRegistersTheRunningExecutable()
    {
        var seam = new RecordingStartWithWindows();

        App.ApplyStartWithWindows(new SettingsLoad(new Settings(), null, AsSaved: true), seam, Executable);

        CollectionAssert.AreEqual(new[] { $"set {Executable}" }, seam.Calls);
    }

    [TestMethod]
    public void OffRemovesTheRegistration()
    {
        var seam = new RecordingStartWithWindows();

        App.ApplyStartWithWindows(new SettingsLoad(new Settings { StartWithWindows = false }, null, AsSaved: true), seam, Executable);

        CollectionAssert.AreEqual(new[] { "clear" }, seam.Calls);
    }

    [TestMethod]
    public void AFirstRunRegisters()
    {
        using var scratch = new ScratchFolder();
        var seam = new RecordingStartWithWindows();

        App.ApplyStartWithWindows(Settings.Load(Path.Combine(scratch.Path, "ImageStash"), ShortRetry), seam, Executable);

        CollectionAssert.AreEqual(new[] { $"set {Executable}" }, seam.Calls);
    }

    [TestMethod]
    public void ASettingsFileHeldPastTheRetryLeavesTheRegistrationAsItIs()
    {
        using var scratch = new ScratchFolder();
        Settings.Write(scratch.Path, new Settings { StartWithWindows = false }, ShortRetry);
        var seam = new RecordingStartWithWindows();

        SettingsLoad load;
        using (new FileStream(scratch.File(Settings.FileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            load = Settings.Load(scratch.Path, ShortRetry);
        }

        App.ApplyStartWithWindows(load, seam, Executable);

        Assert.IsNotNull(load.Failure);
        Assert.IsEmpty(seam.Calls);
    }

    [TestMethod]
    public void ASettingsFileThatDoesNotParseLeavesTheRegistrationAsItIs()
    {
        using var scratch = new ScratchFolder();
        File.WriteAllText(scratch.File(Settings.FileName), """{"startWithWindows":fal""");
        var seam = new RecordingStartWithWindows();

        App.ApplyStartWithWindows(Settings.Load(scratch.Path, ShortRetry), seam, Executable);

        Assert.IsEmpty(seam.Calls);
    }

    [TestMethod]
    public void TheStashIsWhereSettingsSaysOrAtTheDefault()
    {
        var locations = new TestLocations(@"C:\Default\stash", @"C:\Default");

        var atDefault = App.StashLocation(new SettingsLoad(new Settings(), null, AsSaved: true), locations);
        var moved = App.StashLocation(new SettingsLoad(new Settings { StashFolder = @"D:\Moved" }, null, AsSaved: true), locations);

        Assert.AreEqual(@"C:\Default\stash", atDefault.Value);
        Assert.AreEqual(@"D:\Moved", moved.Value);
    }

    [TestMethod]
    public void WhileTheSettingsCannotBeReadNoStashIsOpened()
    {
        var unread = new SettingsLoad(new Settings(), new Failure("Read settings", Settings.FileName, "held"), AsSaved: false);

        var location = App.StashLocation(unread, new TestLocations(@"C:\Default\stash", @"C:\Default"));

        Assert.IsFalse(location.Succeeded);
        Assert.AreEqual("Open the stash", location.Failure.Action);
    }

    [TestMethod]
    public async Task AStoreThatCannotOpenIsNamedInTheTooltip()
    {
        using var scratch = new ScratchFolder();
        var notAFolder = scratch.File("stash");
        File.WriteAllText(notAFolder, "a file where the stash folder should be");
        var tray = new RecordingTray(toolTipLength: 1000);
        var trayMenu = new TrayMenu(tray, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        trayMenu.Show(new GlobalShortcuts(StartedShortcuts.SettingsInMemory(), GlobalShortcuts.Bindings(() => { }, () => { }, () => { }, () => { }), new RecordingShortcuts(), trayMenu.Name, action => action()));

        var opening = await StashStore.Open(notAFolder, new TestRecycleBin(scratch.File("recycled")), ShortRetry, new SetClock());
        var store = App.NameOpening(opening, trayMenu);

        Assert.IsNull(store);
        Assert.StartsWith($"Open the stash failed: {notAFolder}, ", tray.ToolTip);
        Assert.AreEqual(TrayMenu.QuitHeader, tray.Entries[^1].Header);
    }

    [TestMethod]
    public async Task TheFirstRunWithAnEmptyStashOpensTheWindowPinnedWhateverWasTypedMeanwhile()
    {
        using var scratch = new ScratchFolder();
        using var stash = new TestStash();
        var firstRun = Settings.Load(scratch.File("ImageStash"), ShortRetry);
        var listing = new Listing(_ => { });
        await listing.SetQuery("cat");
        var store = await stash.OpenCleanly();
        await listing.StashOpened(store);

        Assert.IsTrue(firstRun.FirstRun);
        Assert.IsTrue(listing.NoMatch, "the query typed while the stash opened is what the grid shows");
        Assert.IsTrue(await App.OpensPinnedAtStart(firstRun, store));
    }

    [TestMethod]
    public async Task ALaterStartOrAStashWithItemsOpensNoWindow()
    {
        using var scratch = new ScratchFolder();
        using var stash = new TestStash();
        Settings.Load(scratch.Path, ShortRetry);
        var later = Settings.Load(scratch.Path, ShortRetry);
        var store = await stash.OpenCleanly();

        Assert.IsFalse(later.FirstRun);
        Assert.IsFalse(await App.OpensPinnedAtStart(later, store));
        await stash.Import("cat");
        Assert.IsFalse(await App.OpensPinnedAtStart(later with { FirstRun = true }, store));
    }

    [TestMethod]
    public void TheThemeFollowsTheSystemUnlessSettingsChoseOne()
    {
        Assert.AreEqual(Avalonia.Styling.ThemeVariant.Default, App.ThemeVariantOf(Theme.System));
        Assert.AreEqual(Avalonia.Styling.ThemeVariant.Light, App.ThemeVariantOf(Theme.Light));
        Assert.AreEqual(Avalonia.Styling.ThemeVariant.Dark, App.ThemeVariantOf(Theme.Dark));
    }
}
