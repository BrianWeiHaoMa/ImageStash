using System.Diagnostics;
using ImageStash.Capture;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The Settings page's rows after the shortcuts (G32), each read from the settings in effect whenever it is asked and
/// each change taking effect at once and written through the one keeper (State: Settings): Also copy and the
/// recording's cap and pointer govern the next capture (G14, G15); a length the list lacks is shown as it is (G31);
/// Theme is applied before it is written (F10); Start with Windows sets or clears the Run entry now and records the
/// choice whatever the entry answered (F1); Open folder asks for the stash folder; a failed act is named on its row and
/// a start that could not read the file is named at the top, its changes kept for the run only, as is a write refused
/// after its retry, until the next change writes again (G35).
/// </summary>
[TestClass]
public sealed class SettingsPageTests
{
    private const string Executable = @"C:\Apps\ImageStash\ImageStash.Windows.exe";
    private const string DefaultStash = @"C:\Users\someone\AppData\Local\ImageStash\stash";
    private static readonly TimeSpan ShortRetry = TimeSpan.FromMilliseconds(300);
    private static readonly TestLocations Locations = new(DefaultStash, @"C:\Users\someone\AppData\Local\ImageStash");
    private static readonly SettingsLoad Read = new(new Settings(), null, AsSaved: true);

    private readonly List<Failure> failures = [];

    [TestMethod]
    public void AtTheDefaultsTheRowsAreG32sInOrderEachInItsGroupAndEachShowsItsDefault()
    {
        using var scratch = new ScratchFolder();
        var page = Page(Keeper(scratch, TimeSpan.FromSeconds(30)));

        CollectionAssert.AreEqual(
            new[]
            {
                ("Shortcuts", "Open ImageStash"),
                ("Shortcuts", "Take a screenshot"),
                ("Shortcuts", "Record a clip"),
                ("Shortcuts", "Stash clipboard image"),
                ("After a screenshot or a recording", "Also copy it to the clipboard"),
                ("Recording", "Maximum length"),
                ("Recording", "Show the mouse pointer"),
                ("Library", "Where your stash is stored"),
                ("Library", "Export your stash"),
                ("Library", "Import a stash"),
                ("General", "Start with Windows"),
                ("General", "Theme"),
                ("General", "Thumbnail size"),
            },
            page.Rows.Select(row => (row.Group, row.Title)).ToList(),
            "G32's rows in its order, the four shortcuts first, Export stash… and Import stash… after the stash location, the thumbnail size after Theme");
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+Space", "Ctrl+PrtScn", "Ctrl+Shift+PrtScn", "Ctrl+Alt+V" }, page.ShortcutFields.Select(field => field.Text).ToList(), "F5's defaults, each in its field");
        Assert.IsFalse(page.ShortcutFields.Any(field => field.IsRecording), "none recording");
        Assert.AreEqual((true, 30, true, DefaultStash, true, Theme.System), (page.AlsoCopyCapture, page.RecordingMaxSeconds, page.RecordPointer, page.StashFolder, page.StartWithWindows, page.Theme),
            "G32's defaults: on, 30 seconds, on, the default folder (F6), on, the system's theme");
        CollectionAssert.Contains(page.RecordingLengths.ToList(), 30, "30 seconds is on the list (G32)");
        CollectionAssert.AreEqual(new[] { Theme.System, Theme.Light, Theme.Dark }, SettingsPage.Themes.Select(choice => choice.Theme).ToList(), "system, light, dark (G32)");
        Assert.AreEqual(ThumbnailSize.Medium, page.ThumbnailSize, "the grid's default size");
        CollectionAssert.AreEqual(Enum.GetValues<ThumbnailSize>(), SettingsPage.ThumbnailSizes.Select(choice => choice.Size).ToList(), "the five sizes, smallest first (G6, G32)");
        Assert.IsNull(page.ReadFailure);
        Assert.IsTrue(page.Rows.All(row => page.WarningOf(row.Title) is null), "no warning before anything was done");
    }

    [TestMethod]
    public async Task AlsoCopyOffTakesEffectAtOnceSoTheNextScreenshotIsNotCopied()
    {
        using var taking = await ListedStash.Open(SetClock.Ahead);
        taking.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, new Avalonia.PixelRect(100, 50, 30, 20));
        var page = Page(taking.Settings);

        page.SetAlsoCopyCapture(false);

        Assert.IsFalse(taking.Settings.Current.AlsoCopyCapture, "in effect at once");
        Assert.IsFalse(page.AlsoCopyCapture);
        await taking.Screenshot.Take();
        Assert.HasCount(1, await taking.Stash.All(), "the screenshot saved");
        Assert.AreEqual(0, taking.Clipboard.Begun, "and not copied (G14)");
        page.SetAlsoCopyCapture(true);
        await taking.Screenshot.Take();
        Assert.AreEqual(1, taking.Clipboard.Begun, "on again, the next one is copied");
    }

    [TestMethod]
    public async Task EveryRowsChangeIsWrittenAfterTheQuietSpell()
    {
        using var scratch = new ScratchFolder();
        var page = Page(Keeper(scratch, TimeSpan.FromMilliseconds(50)));

        page.SetAlsoCopyCapture(false);
        page.SetRecordingMaxSeconds(15);
        page.SetRecordPointer(false);
        page.SetTheme(Theme.Light);
        page.SetStartWithWindows(false);
        page.SetThumbnailSize(ThumbnailSize.Huge);

        var expected = new Settings { AlsoCopyCapture = false, RecordingMaxSeconds = 15, RecordPointer = false, Theme = Theme.Light, StartWithWindows = false, ThumbnailSize = ThumbnailSize.Huge };
        var waited = Stopwatch.StartNew();
        while (Saved(scratch) != expected)
        {
            Assert.IsLessThan(5000, waited.ElapsedMilliseconds, $"never written: {Saved(scratch)}");
            await Task.Delay(20);
        }

        Assert.IsEmpty(failures);
    }

    [TestMethod]
    public async Task AChangeMadeJustBeforeQuitIsWrittenByTheFlush()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.FromSeconds(30));
        var page = Page(keeper);

        page.SetAlsoCopyCapture(false);
        Assert.IsTrue(Saved(scratch).AlsoCopyCapture, "not yet written: the quiet spell has not passed");
        await keeper.Flush();

        Assert.IsFalse(Saved(scratch).AlsoCopyCapture, "written at once by the flush at Quit (State: Settings)");
    }

    [TestMethod]
    public async Task TheCapAndThePointerChosenHereAreTheNextRecordings()
    {
        using var recording = await ListedStash.OpenRecording();
        recording.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, new Avalonia.PixelRect(100, 50, 30, 20));
        MediaFrame[] pictures = [recording.Screen.Paint(RecordingScreen.Primary, seed: 1), recording.Screen.Paint(RecordingScreen.Primary, seed: 2)];
        recording.Screen.EachGrab = index => Outcome<MediaFrame>.Of(pictures[index % 2]);
        var page = Page(recording.Settings);

        page.SetRecordingMaxSeconds(10);
        page.SetRecordPointer(false);
        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        // Past the cap at 10,000 ms with no Stop pressed, so only the cap ends the recording.
        recording.StepRecording(10_050);
        await acting.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.AreEqual(TimeSpan.FromSeconds(10), recording.Frame.Shown!.Value.Cap, "the bar shows the cap chosen on the page (G15)");
        Assert.AreEqual(10_000L, (await recording.Stash.All()).Single().Facts.DurationMs, "and the recording ended at it, not at the default 30 seconds (G15, G32)");
        Assert.IsNotEmpty(recording.Screen.PointerAsked);
        Assert.IsTrue(recording.Screen.PointerAsked.All(asked => !asked), "no pointer drawn (G15)");
    }

    [TestMethod]
    public async Task ALengthTheListLacksIsShownAsItIsAndRewrittenOnlyWhenAnotherIsPicked()
    {
        using var scratch = new ScratchFolder();
        File.WriteAllText(scratch.File(Settings.FileName), """{"version":1,"recordingMaxSeconds":45}""");
        var keeper = Keeper(scratch, TimeSpan.FromSeconds(30));
        var page = Page(keeper);

        CollectionAssert.AreEqual(new[] { 10, 15, 30, 45, 60 }, page.RecordingLengths.ToList(), "the list, and the file's own value in its place (G31, G32)");
        Assert.AreEqual(45, page.RecordingMaxSeconds, "shown as the file holds it");
        Assert.AreEqual("45 seconds", SettingsPage.LengthWords(45));
        await keeper.Flush();
        Assert.AreEqual("""{"version":1,"recordingMaxSeconds":45}""", File.ReadAllText(scratch.File(Settings.FileName)), "nothing rewritten by showing it");

        page.SetRecordingMaxSeconds(30);
        CollectionAssert.AreEqual(SettingsPage.Lengths.ToList(), page.RecordingLengths.ToList(), "once another is picked, the list alone");
        await keeper.Flush();
        Assert.AreEqual(30, Saved(scratch).RecordingMaxSeconds);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => page.SetRecordingMaxSeconds(45), "a length the page no longer offers is no choice");
        Assert.AreEqual(30, keeper.Current.RecordingMaxSeconds);
    }

    [TestMethod]
    public async Task ThemeIsAppliedToTheWindowsBeforeItIsWritten()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.FromSeconds(30));
        var applied = new List<(Theme Theme, Theme InEffect)>();
        var page = Page(keeper, applyTheme: theme => applied.Add((theme, keeper.Current.Theme)));

        page.SetTheme(Theme.Dark);
        await keeper.Flush();
        Assert.AreEqual(Theme.Dark, Saved(scratch).Theme, "written (F10)");
        page.SetTheme(Theme.System);
        await keeper.Flush();

        CollectionAssert.AreEqual(new[] { (Theme.Dark, Theme.System), (Theme.System, Theme.Dark) }, applied, "applied at once, each before the keeper holds it (F10)");
        Assert.AreEqual(Theme.System, Saved(scratch).Theme);
        Assert.AreEqual(Theme.System, page.Theme);
    }

    [TestMethod]
    public async Task StartWithWindowsOffClearsTheRunEntryNowAndOnSetsItEachChoiceWritten()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.FromSeconds(30));
        var run = new RecordingStartWithWindows();
        var page = Page(keeper, run: run);

        Assert.IsTrue(page.SetStartWithWindows(false).Succeeded);
        CollectionAssert.AreEqual(new[] { "clear" }, run.Calls, "the Run entry removed now, not at the next start (F1)");
        await keeper.Flush();
        Assert.IsFalse(Saved(scratch).StartWithWindows);

        Assert.IsTrue(page.SetStartWithWindows(true).Succeeded);
        CollectionAssert.AreEqual(new[] { "clear", $"set {Executable}" }, run.Calls, "the running executable registered now (F1)");
        await keeper.Flush();
        Assert.IsTrue(Saved(scratch).StartWithWindows);
        Assert.IsNull(page.WarningOf(SettingsPage.StartWithWindowsTitle));
    }

    [TestMethod]
    public async Task ARefusedRunChangeIsNamedOnItsRowTheChoiceStillWrittenUntilTheNextSuccessClearsIt()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.FromSeconds(30));
        var refused = new Failure("Stop starting with Windows", "Run entry ImageStash", "Windows refused the change");
        var run = new RecordingStartWithWindows { Refusal = refused };
        var page = Page(keeper, run: run);
        var changed = 0;
        page.Changed += () => changed++;

        var outcome = page.SetStartWithWindows(false);

        Assert.AreEqual(refused, outcome.Failure);
        Assert.AreEqual(refused, page.WarningOf(SettingsPage.StartWithWindowsTitle), "named on its row as the seam named it (G35)");
        Assert.AreEqual("Stop starting with Windows failed: Run entry ImageStash, Windows refused the change", page.WarningOf(SettingsPage.StartWithWindowsTitle)!.Sentence);
        Assert.IsTrue(page.Rows.Where(row => row.Title != SettingsPage.StartWithWindowsTitle).All(row => page.WarningOf(row.Title) is null), "on that row alone");
        Assert.IsFalse(page.StartWithWindows, "the choice is the record whatever the entry answered (State: Settings)");
        await keeper.Flush();
        Assert.IsFalse(Saved(scratch).StartWithWindows, "and written, so the next start applies it again");
        Assert.AreEqual(1, changed, "the window told to show it");

        run.Refusal = null;
        Assert.IsTrue(page.SetStartWithWindows(true).Succeeded);
        Assert.IsNull(page.WarningOf(SettingsPage.StartWithWindowsTitle), "cleared by the row's next success");
        Assert.AreEqual(2, changed);
        // The second choice's write lands before the scratch folder goes.
        await keeper.Flush();
    }

    [TestMethod]
    public async Task MoveWhileTheStashIsStillOpeningIsRefusedOnItsRowWithoutThePicker()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.FromSeconds(30));
        var dialogs = new RecordingDialogs { PickFolderAnswer = () => Task.FromResult<string?>(scratch.Path) };
        var page = Page(keeper, dialogs: dialogs);

        var moved = await page.Move(null!);

        Assert.AreEqual(new Failure(StashMove.Action, "the stash folder", "your stash is still opening"), moved.Failure);
        Assert.AreEqual(moved.Failure, page.WarningOf(SettingsPage.StashFolderTitle), "named on its row (G35, G32)");
        Assert.IsEmpty(dialogs.FolderPickers, "the picker not shown");
        Assert.IsFalse(page.MoveAsked);
        Assert.IsNull(page.MoveLine);
    }

    [TestMethod]
    public void OpenFolderAsksForTheFolderInUseAndARefusalIsNamedOnItsRowAlone()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.FromSeconds(30));
        var dialogs = new RecordingDialogs();
        var run = new RecordingStartWithWindows { Refusal = new Failure("Start with Windows", "Run entry ImageStash", "Windows refused the change") };
        var page = Page(keeper, run: run, dialogs: dialogs);
        page.SetStartWithWindows(true);

        Assert.IsTrue(page.OpenFolder().Succeeded);
        keeper.Change(s => s with { StashFolder = @"D:\Reactions" });
        Assert.AreEqual(@"D:\Reactions", page.StashFolder, "read when asked, as a Move would leave it");
        dialogs.OpenFolderAnswer = new Failure("Open folder", "Reactions", "The system cannot find the file specified.");
        var refused = page.OpenFolder();

        CollectionAssert.AreEqual(new[] { DefaultStash, @"D:\Reactions" }, dialogs.OpenedFolders, "the default while Settings names none, then the folder it names (F6, G32)");
        Assert.AreEqual(dialogs.OpenFolderAnswer.Failure, refused.Failure);
        Assert.AreEqual(refused.Failure, page.WarningOf(SettingsPage.StashFolderTitle), "named on its row (G35)");
        Assert.IsNotNull(page.WarningOf(SettingsPage.StartWithWindowsTitle), "another row's warning stays its own");
        Assert.IsEmpty(dialogs.Revealed, "no file in the stash folder selected");
        dialogs.OpenFolderAnswer = Outcome.Done;
        page.OpenFolder();
        Assert.IsNull(page.WarningOf(SettingsPage.StashFolderTitle), "cleared by its next success");
    }

    [TestMethod]
    public async Task WhileTheStartCouldNotReadTheFileThePageSaysSoAndItsChangesHoldForTheRunOnly()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File(Settings.FileName);
        File.WriteAllText(path, """{"version":1,"theme":""");
        var load = Settings.Load(scratch.Path, ShortRetry);
        var keeper = new SettingsKeeper(scratch.Path, load, ShortRetry, TimeSpan.Zero, failures.Add);
        var applied = new List<Theme>();
        var page = Page(keeper, load, applyTheme: applied.Add);

        Assert.AreEqual(load.Failure, page.ReadFailure, "the start's read failure named at the top (G35)");
        StringAssert.StartsWith(page.ReadFailure!.Sentence, "Read settings failed: settings.json, ");
        page.SetTheme(Theme.Dark);
        page.SetRecordPointer(false);
        await keeper.Flush();

        Assert.AreEqual((Theme.Dark, false), (keeper.Current.Theme, keeper.Current.RecordPointer), "in effect for the run");
        CollectionAssert.AreEqual(new[] { Theme.Dark }, applied);
        Assert.AreEqual("""{"version":1,"theme":""", File.ReadAllText(path), "and the file the user may still repair left as it is");
        using var firstRun = new ScratchFolder();
        Assert.IsNull(Page(Keeper(firstRun, TimeSpan.Zero), new SettingsLoad(new Settings(), new Failure("Save settings", Settings.FileName, "refused"), AsSaved: true)).ReadFailure,
            "a first run whose defaults could not be written is not a file that could not be read");
    }

    [TestMethod]
    public async Task AWriteRefusedAfterItsRetryIsShownAtTheTopUntilTheNextChangeWritesAgain()
    {
        using var scratch = new ScratchFolder();
        var notices = new Notices(new SetClock());
        SettingsPage? page = null;
        var keeper = new SettingsKeeper(scratch.Path, Settings.Load(scratch.Path, ShortRetry), ShortRetry, TimeSpan.Zero, failure => App.SettingsWriteFailed(failure, notices, page));
        page = Page(keeper);

        using (new FileStream(scratch.File(Settings.FileName), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            page.SetAlsoCopyCapture(false);
            await keeper.Flush();
        }

        Assert.AreEqual(("Save settings", Settings.FileName), (page.WriteFailure?.Action, page.WriteFailure?.Subject), "the refused write named on the page, where the change was made (G35)");
        Assert.AreEqual(page.WriteFailure!.Sentence, notices.Shown.Single().Text, "and in the stash window's notices, as before");
        Assert.IsFalse(page.AlsoCopyCapture, "the change still in effect for the run");
        page.SetRecordPointer(false);
        Assert.IsNull(page.WriteFailure, "the next change writes every setting again");
        await keeper.Flush();
        Assert.AreEqual((false, false), (Saved(scratch).AlsoCopyCapture, Saved(scratch).RecordPointer), "both written once the file is free");
        Assert.IsNull(page.WriteFailure);
    }

    [TestMethod]
    public void EachValueIsReadWhenAskedSoAPageOpenedLaterShowsWhatHoldsThen()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.FromSeconds(30));
        var first = Page(keeper);
        first.SetRecordingMaxSeconds(60);
        first.SetTheme(Theme.Light);

        keeper.Change(s => s with { StashFolder = @"E:\Moved stash", AlsoCopyCapture = false, ThumbnailSize = ThumbnailSize.ExtraLarge });
        var later = Page(keeper);

        foreach (var page in new[] { first, later })
        {
            Assert.AreEqual((60, Theme.Light, @"E:\Moved stash", false, ThumbnailSize.ExtraLarge), (page.RecordingMaxSeconds, page.Theme, page.StashFolder, page.AlsoCopyCapture, page.ThumbnailSize), "the keeper's values now, not those at construction");
        }
    }

    [TestMethod]
    public void EveryChangeTellsTheWindowToRedraw()
    {
        using var scratch = new ScratchFolder();
        var page = Page(Keeper(scratch, TimeSpan.FromSeconds(30)));
        var changed = 0;
        page.Changed += () => changed++;

        page.SetAlsoCopyCapture(false);
        page.SetRecordingMaxSeconds(10);
        page.SetRecordPointer(false);
        page.SetTheme(Theme.Dark);
        page.SetStartWithWindows(false);
        page.OpenFolder();
        page.SetThumbnailSize(ThumbnailSize.Large);

        Assert.AreEqual(7, changed);
    }

    [TestMethod]
    public void ASizeChosenHereIsTheGridsAtOnceAndWrittenWhileChoosingTheSizeInEffectChangesNothing()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.FromSeconds(30));
        var grid = new ThumbnailSizing(keeper);
        var page = Page(keeper, thumbnails: grid);
        var gridTold = new List<ThumbnailSize>();
        grid.Changed += () => gridTold.Add(grid.Current);

        page.SetThumbnailSize(ThumbnailSize.Huge);
        page.SetThumbnailSize(ThumbnailSize.Huge);

        CollectionAssert.AreEqual(new[] { ThumbnailSize.Huge }, gridTold, "the grid told once, with the size chosen, and not again for the size in effect (G32: \"which the grid takes at once\")");
        Assert.AreEqual(ThumbnailSize.Huge, keeper.Current.ThumbnailSize, "remembered (G6)");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => page.SetThumbnailSize((ThumbnailSize)5));
        Assert.AreEqual(ThumbnailSize.Huge, page.ThumbnailSize, "a size the grid lacks changes nothing");
    }

    [TestMethod]
    public void WhileOpenThePageShowsEachSizeSteppedInTheGridChangingNothingItselfAndOnceClosedHearsNoMore()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.FromSeconds(30));
        var grid = new ThumbnailSizing(keeper);
        var page = Page(keeper, thumbnails: grid);
        var shown = new List<ThumbnailSize>();
        page.Changed += () => shown.Add(page.ThumbnailSize);

        grid.Step(1);
        grid.Step(1);

        CollectionAssert.AreEqual(new[] { ThumbnailSize.Large, ThumbnailSize.ExtraLarge }, shown, "each size the grid reached, as it was reached (G32: \"the page showing a size stepped in the grid while it is open\")");
        Assert.AreEqual(ThumbnailSize.ExtraLarge, keeper.Current.ThumbnailSize, "where the grid stepped it: the page wrote nothing back");

        page.Close();
        grid.Step(-1);

        Assert.HasCount(2, shown, "a closed page is told nothing");
        Assert.AreEqual(ThumbnailSize.Large, Page(keeper, thumbnails: grid).ThumbnailSize, "a page opened again shows the size in effect");
    }

    private SettingsKeeper Keeper(ScratchFolder scratch, TimeSpan quiet) =>
        new(scratch.Path, Settings.Load(scratch.Path, ShortRetry), ShortRetry, quiet, failures.Add);

    private static Settings Saved(ScratchFolder scratch) => Settings.Load(scratch.Path, ShortRetry).Settings;

    private static SettingsPage Page(SettingsKeeper keeper, SettingsLoad? load = null, RecordingStartWithWindows? run = null, RecordingDialogs? dialogs = null, Action<Theme>? applyTheme = null, Moving? moving = null, ThumbnailSizing? thumbnails = null)
    {
        moving ??= MovingStash.NeverOpened(keeper, dialogs ?? new RecordingDialogs());
        return new(keeper, load ?? Read, new GlobalShortcuts(keeper, GlobalShortcuts.Bindings(() => { }, () => { }, () => { }, () => { }), new RecordingShortcuts(), _ => { }, action => action()),
            Locations, run ?? new RecordingStartWithWindows(), dialogs ?? new RecordingDialogs(), moving, MovingStash.NeverOpenedTransfers(keeper, dialogs ?? new RecordingDialogs(), moving),
            thumbnails ?? new ThumbnailSizing(keeper), applyTheme ?? (_ => { }), Executable);
    }
}
