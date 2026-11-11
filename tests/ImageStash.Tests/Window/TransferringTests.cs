using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Export stash… and Import stash… from the Settings page over a real stash and a settings file (G36, G32, State: Export
/// stash…, Import stash…): the pickers, the export's busy state naming it and where the file is said only once written
/// (R9); the import's summary, its refusals by name, the offer inside the row blocking nothing until it is confirmed;
/// from the confirmation every beginning of a change refused by name, the wait as Move waits, Store's import, the stash
/// listed again from the same folder and the settings that travel in effect at once (Theme, the four shortcuts, Start
/// with Windows, G33, F1, F10); a failure before the record changing nothing and one after it finished by the start's
/// finishing, now or at the next start (G28, G35).
/// </summary>
[TestClass]
public sealed class TransferringTests
{
    private static readonly Shortcuts Imported = new() { Open = "Ctrl+Alt+F2", Screenshot = "Ctrl+Shift+F6", Record = "Ctrl+Shift+F7", StashClipboardImage = "Ctrl+Alt+F3" };

    [TestMethod]
    public async Task ExportShowsThePickerOnceNamesItselfAndThePathWhileWritingAndSaysWhereOnlyOnceTheFileIsThere()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        var archive = listed.Stash.Outside("mine.zip");
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(archive);
        var page = listed.Page();
        var whileWriting = new List<(string? Line, bool Asked, bool There)>();
        listed.Transferring.Changed += () => whileWriting.Add((page.ExportLine, page.WholeStashActAsked, File.Exists(archive)));
        bool? thereWhenSaid = null;
        listed.Notices.Changed += () => thereWhenSaid ??= listed.Notices.Shown.Any(notice => notice.Text.StartsWith("Exported")) ? File.Exists(archive) : null;

        var exported = await page.Export(null!);

        Assert.IsTrue(exported.Succeeded, exported.Failure?.Sentence);
        var picker = listed.Dialogs.SavePickers.Single();
        Assert.AreEqual(Transferring.ExportPickerTitle, picker.Title);
        StringAssert.StartsWith(picker.SuggestedFileName, "ImageStash export ");
        StringAssert.EndsWith(picker.SuggestedFileName, StashExport.Extension);
        Assert.AreEqual(Transferring.FileType, picker.FileType, "the export's one file type");
        Assert.IsTrue(whileWriting.Any(seen => seen.Line == $"Exporting your stash to {archive}…" && seen.Asked && !seen.There),
            "while Store writes, the row names the export and the path, and the page takes no change (G36, G35)");
        Assert.AreEqual(Transferring.ExportedWords(archive), page.ExportLine, "where the file is, on the row");
        Assert.IsTrue(thereWhenSaid, "and in a notice, only once the file is there (R9)");
        Assert.IsFalse(page.WholeStashActAsked);
        Assert.IsNull(listed.Listing.CannotStartReason, "changes may begin again");
        Assert.AreEqual(1, StashImport.Summary(archive, TestStash.ShortRetry).Value!.Items);
    }

    [TestMethod]
    public async Task ACancelledExportPickerAsksNothingOfTheStoreShowsNoBusyStateAndSaysNothing()
    {
        using var listed = await ListedStash.Open();
        var page = listed.Page();
        var lines = new List<string?>();
        listed.Transferring.Changed += () => lines.Add(page.ExportLine);

        var exported = await page.Export(null!);

        Assert.IsTrue(exported.Succeeded);
        Assert.HasCount(1, listed.Dialogs.SavePickers);
        Assert.IsTrue(lines.All(line => line is null), "no busy state");
        Assert.IsEmpty(listed.Notices.Shown, "nothing said");
        Assert.IsNull(page.WarningOf(SettingsPage.ExportTitle));
    }

    [TestMethod]
    public async Task AnExportWaitsForANoticesUndoWhichStillActsAndRunsOnceTheNoticeHasGone()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Stash.Import("dog");
        var archive = listed.Stash.Outside("mine.zip");
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(archive);
        Assert.IsTrue((await listed.Deletion.Delete([cat])).Succeeded);

        var exporting = listed.Transferring.Export(null!);
        await Handing.Eventually(() => listed.Transferring.ExportLine == "Waiting for the Undo of a notice before exporting your stash…");
        Assert.AreEqual(StashExport.InProgress, listed.Listing.CannotStartReason, "nothing else starts from the asking (G36)");
        await listed.Turns.Act(() => listed.Notices.Undo(listed.Notices.Shown.Single()));
        Assert.HasCount(2, await listed.Stash.All(), "the Undo still acted (G16, G18)");
        Assert.IsFalse(exporting.IsCompleted, "the Undo's own notice is waited for too");
        Assert.IsFalse(File.Exists(archive));

        listed.Stash.Clock.Now += Notices.Lasting;
        listed.Turns.Act(listed.Notices.Sweep);
        var exported = await exporting.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(exported.Succeeded, exported.Failure?.Sentence);
        Assert.AreEqual(2, StashImport.Summary(archive, TestStash.ShortRetry).Value!.Items, "the restored item exported with the other");
    }

    [TestMethod]
    public async Task AnExportWhoseDestinationStaysHeldIsNamedOnTheRowAndInANoticeChangesNothingAndMayBeAskedAgain()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        var archive = listed.Stash.Outside("mine.zip");
        File.WriteAllBytes(archive, [1, 2, 3]);
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(archive);
        var page = listed.Page();
        var before = MovingStash.Tree(listed.Stash.Folder.Root);

        Outcome refused;
        using (new FileStream(archive, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            refused = await page.Export(null!);
        }

        Assert.AreEqual(StashExport.Action, refused.Failure?.Action, refused.Failure?.Sentence);
        Assert.AreEqual("mine.zip", refused.Failure!.Subject);
        Assert.AreEqual(refused.Failure, page.WarningOf(SettingsPage.ExportTitle), "on the row (G35)");
        CollectionAssert.Contains(listed.Notices.Shown.Select(notice => notice.Text).ToList(), refused.Failure.Sentence, "and in a notice");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(archive), "the file there replaced only whole");
        CollectionAssert.AreEqual(before, MovingStash.Tree(listed.Stash.Folder.Root), "nothing in the stash changed (G36)");
        Assert.IsNull(listed.Listing.CannotStartReason);

        var again = await page.Export(null!);

        Assert.IsTrue(again.Succeeded, again.Failure?.Sentence);
        Assert.IsNull(page.WarningOf(SettingsPage.ExportTitle));
        Assert.AreEqual(1, StashImport.Summary(archive, TestStash.ShortRetry).Value!.Items);
    }

    [TestMethod]
    public async Task ImportShowsTheFilePickerOnceAndACancelChangesNothing()
    {
        using var listed = await ListedStash.Open();
        var page = listed.Page();

        var chosen = await page.Import(null!);

        Assert.IsTrue(chosen.Succeeded);
        var picker = listed.Dialogs.FilePickers.Single();
        Assert.AreEqual(Transferring.ImportPickerTitle, picker.Title);
        CollectionAssert.AreEqual(new[] { Transferring.FileType }, picker.Filters.ToList());
        Assert.IsNull(page.ImportOffer);
        Assert.IsNull(page.ImportLine);
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    [DataRow("no manifest", TransferReasons.NotAnExport)]
    [DataRow("newer", TransferReasons.NewerBuild)]
    [DataRow("escaping", TransferReasons.NotAnExport)]
    public async Task AFileThatIsNotAnExportOrIsANewerBuildsIsRefusedByNameOnTheRowAndInANoticeOfferingNothing(string kind, string reason)
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        var bad = listed.Stash.Outside("bad.zip");
        switch (kind)
        {
            case "no manifest":
                ExportedStash.Zip(bad, ("stash.db", [1]));
                break;
            case "newer":
                ExportedStash.Zip(bad, ("manifest.json", ExportedStash.Manifest(formatVersion: 99, items: 1)), ("stash.db", [1]));
                break;
            default:
                ExportedStash.Zip(bad, ("manifest.json", ExportedStash.Manifest(items: 1)), ("stash.db", [1]), ("../outside.png", [1]));
                break;
        }

        listed.Dialogs.OpenFileAnswer = () => Task.FromResult<string?>(bad);
        var page = listed.Page();
        var settings = listed.Settings.Current;

        var refused = await page.Import(null!);

        Assert.AreEqual(new Failure(StashImport.Action, "bad.zip", reason), refused.Failure);
        Assert.AreEqual(refused.Failure, page.WarningOf(SettingsPage.ImportTitle), "on the row (G35)");
        CollectionAssert.AreEqual(new[] { refused.Failure!.Sentence }, listed.Notices.Shown.Select(notice => notice.Text).ToList(), "and in a notice");
        Assert.IsNull(page.ImportOffer, "nothing offered for confirmation");
        Assert.IsNull(listed.Listing.CannotStartReason, "nothing blocked");
        Assert.AreEqual(settings, listed.Settings.Current, "the settings unchanged");
        CollectionAssert.AreEqual(new[] { "cat" }, (await listed.Stash.All()).Select(item => item.Name).ToList());

        var good = await Archive(listed, names: ["one"]);
        listed.Dialogs.OpenFileAnswer = () => Task.FromResult<string?>(good);
        Assert.IsTrue((await page.Import(null!)).Succeeded, "Import stash… asked again runs");
        Assert.IsNotNull(page.ImportOffer);
        Assert.IsNull(page.WarningOf(SettingsPage.ImportTitle));
    }

    [TestMethod]
    public async Task AChosenArchiveIsOfferedWithItsCountAndWhatItReplacesBlockingNothingAndItsCancelChangesNothing()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        var archive = await Archive(listed, names: ["a", "b", "c", "d", "e", "f", "g"]);
        listed.Dialogs.OpenFileAnswer = () => Task.FromResult<string?>(archive);
        var page = listed.Page();

        Assert.IsTrue((await page.Import(null!)).Succeeded);

        StringAssert.StartsWith(page.ImportOffer, "export.zip holds 7 items. ");
        StringAssert.Contains(page.ImportOffer, "replaces your stash and these settings");
        StringAssert.Contains(page.ImportOffer, "the four shortcuts, Also copy, the recording's maximum length and pointer, Start with Windows and Theme");
        Assert.IsFalse(page.WholeStashActAsked, "asked only at the confirmation");
        Assert.IsNull(listed.Listing.CannotStartReason, "nothing blocked while the offer shows");
        Assert.IsTrue((await listed.Organising.AddTag([cat], "funny")).Succeeded, "a tag change still runs");

        page.CancelImport();

        Assert.IsNull(page.ImportOffer, "the row at rest");
        Assert.IsTrue((await page.ConfirmImport()).Succeeded, "nothing left to confirm");
        CollectionAssert.AreEqual(new[] { "cat" }, (await listed.Stash.All()).Select(item => item.Name).ToList(), "nothing changed");
        Assert.AreEqual(Theme.System, listed.Settings.Current.Theme);
    }

    [TestMethod]
    public async Task AnImportConfirmedWithAnEditOpenWaitsForItThenReplacesTheStashAtItsFolderAndPutsTheImportedSettingsInEffect()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat", MediaFixtures.Bytes("still.png"));
        await listed.Stash.Import("dog");
        var folder = listed.Stash.Folder.Root;
        var archive = await Archive(listed, s => s with { Shortcuts = Imported, Theme = Theme.Dark, StartWithWindows = false, AlsoCopyCapture = false, ThumbnailSize = ThumbnailSize.Huge }, "one", "two", "three");
        listed.Dialogs.OpenFileAnswer = () => Task.FromResult<string?>(archive);
        listed.ThumbnailSizing.Choose(ThumbnailSize.ExtraLarge);
        var page = listed.Page();
        var sizesTold = 0;
        listed.ThumbnailSizing.Changed += () => sizesTold++;
        Assert.IsTrue((await page.Import(null!)).Succeeded);
        Assert.IsTrue((await listed.Editing.Open(cat, EditOrigin.Grid)).Succeeded);
        var oldShortcuts = listed.ShortcutsSeam.Registered.Select(shortcut => shortcut.Text).ToList();
        var said = new List<string?>();
        listed.Notices.Changed += () => said.Add(listed.Notices.Shown.LastOrDefault()?.Text);

        var importing = page.ConfirmImport();
        await Handing.Eventually(() => page.ImportLine == "Waiting for the edit before importing export.zip…");
        Assert.AreEqual(StashImport.InProgress, listed.Listing.CannotStartReason, "from the confirmation nothing else starts (G36)");
        Assert.IsTrue(page.WholeStashActAsked, "the page takes no change");
        await listed.Turns.Act(() => listed.Import.Drop(ListedStash.Contents([listed.Stash.Source("dropped.png", MediaFixtures.Bytes("still.png"))])));
        CollectionAssert.Contains(listed.Notices.Shown.Select(notice => notice.Text).ToList(), new Failure(Import.Action, "dropped.png", StashImport.InProgress).Sentence,
            "an import of files refused by name");
        Assert.IsTrue(listed.Turns.Act(listed.Editing.Cancel), "the edit's Cancel still works");
        listed.Stash.Clock.Now += Notices.Lasting;
        listed.Turns.Act(listed.Notices.Sweep);
        var imported = await importing.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence);
        Assert.AreEqual(folder, listed.Listing.Store!.Folder.Root, "the stash replaced at its location");
        await listed.Listing.ShowScope(Scope.All);
        CollectionAssert.AreEquivalent(new[] { "one", "two", "three" }, listed.Listing.Items.Select(item => item.Name).ToList(), "exactly the archive's items");
        Assert.AreEqual(Transferring.ImportedWords("export.zip"), page.ImportLine, "the row says so");
        CollectionAssert.Contains(said, Transferring.ImportedWords("export.zip"), "and a notice");
        Assert.AreEqual(Theme.Dark, listed.Themes.Last(), "every window takes the imported theme at once (F10)");
        CollectionAssert.AreEquivalent(new[] { "Ctrl+Alt+F2", "Ctrl+Shift+F6", "Ctrl+Shift+F7", "Ctrl+Alt+F3" }, listed.ShortcutsSeam.Registered.Select(shortcut => shortcut.Text).ToList(),
            "the imported four registered, the old ones released (G33)");
        Assert.IsFalse(oldShortcuts.Intersect(listed.ShortcutsSeam.Registered.Select(shortcut => shortcut.Text)).Any());
        Assert.AreEqual("clear", listed.StartWithWindows.Calls.Last(), "the Run entry cleared per the archive (F1)");
        Assert.AreEqual((Theme.Dark, false, false), (page.Theme, page.StartWithWindows, page.AlsoCopyCapture), "the page's rows read the imported values");
        Assert.AreEqual((ThumbnailSize.ExtraLarge, 0), (page.ThumbnailSize, sizesTold), "the thumbnail size, not carried, as it was: the grid told nothing and the page's list unchanged (G36)");
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+F2", "Ctrl+Shift+F6", "Ctrl+Shift+F7", "Ctrl+Alt+F3" }, page.ShortcutFields.Select(field => field.Text).ToList());
        Assert.IsNull(listed.Listing.CannotStartReason, "changes may begin again");
        Assert.HasCount(1, listed.Stash.RecycleBin.Asked, "the replaced folder recycled whole (G29)");
    }

    [TestMethod]
    public async Task AnImportedOpenShortcutAnotherProgramHoldsIsUnavailableAndNamedTheOtherThreeRegisteredAndTheChangeTold()
    {
        using var listed = await ListedStash.Open();
        var archive = await Archive(listed, s => s with { Shortcuts = Imported }, "one");
        listed.ShortcutsSeam.Refuse(Shortcut.Parse("Ctrl+Alt+F2").Value!, "another program holds it");
        var told = 0;
        listed.Shortcuts.Changed += () => told++;

        var imported = await listed.ImportStash(archive);

        Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence);
        Assert.AreEqual(new Failure(GlobalShortcuts.RegistrationAction(ShortcutAct.Open), "Ctrl+Alt+F2", "another program holds it"), listed.Shortcuts.UnavailableReason(ShortcutAct.Open),
            "unavailable, and why (G33)");
        Assert.AreEqual(listed.Shortcuts.UnavailableReason(ShortcutAct.Open), listed.Unavailable.Last(), "named as the tooltip names it");
        CollectionAssert.AreEquivalent(new[] { "Ctrl+Shift+F6", "Ctrl+Shift+F7", "Ctrl+Alt+F3" }, listed.ShortcutsSeam.Registered.Select(shortcut => shortcut.Text).ToList(), "the other three registered");
        Assert.IsGreaterThan(0, told, "the tray and the window's + menu read the shortcuts again (G34)");
    }

    [TestMethod]
    public async Task AHandoffAskedDuringTheWaitRunsAndRecordsItsUseAndOneAskedOnceTheImportHasBegunIsRefusedByName()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(5));
        var cat = await listed.Stash.Import("cat", MediaFixtures.Bytes("still.png"));
        var dog = await listed.Stash.Import("dog");
        var archive = await Archive(listed, names: ["one"]);
        Assert.IsTrue((await listed.Deletion.Delete([dog])).Succeeded);
        var page = await listed.Offered(archive);

        var importing = page.ConfirmImport();
        await Handing.Eventually(() => page.ImportLine == "Waiting for the Undo of a notice before importing export.zip…");
        var duringTheWait = await listed.Turns.Act(() => listed.Handoff.Copy(null!, [cat]));
        Assert.IsTrue(duringTheWait.Succeeded, duringTheWait.Failure?.Sentence);
        Assert.AreEqual(1, listed.Uses(), "the copy during the wait records its use (G10)");

        Outcome begun;
        // The import's record waits on the held settings file, so the import is Store's and not yet finished.
        using (new FileStream(MovingStash.SettingsPath(listed.Stash), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            listed.Stash.Clock.Now += Notices.Lasting;
            listed.Turns.Act(listed.Notices.Sweep);
            await Handing.Eventually(() => listed.Listing.WholeStashActBegun);
            begun = await listed.Handoff.Copy(null!, [cat]);
        }

        Assert.IsTrue((await importing.WaitAsync(TimeSpan.FromSeconds(30))).Succeeded);
        Assert.AreEqual((Handoff.Action, StashImport.InProgress), (begun.Failure?.Action, begun.Failure?.Reason), "refused by name once the import began (G10, G35)");
        Assert.HasCount(1, listed.Clipboard.Written, "nothing more written");
    }

    [TestMethod]
    public async Task ClosingTheSettingsPageDuringTheWaitLeavesTheImportToFinishAndSayItHas()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        var archive = await Archive(listed, names: ["one"]);
        Assert.IsTrue((await listed.Deletion.Delete([cat])).Succeeded);
        var page = await listed.Offered(archive);

        var importing = page.ConfirmImport();
        await Handing.Eventually(() => page.ImportLine?.StartsWith("Waiting for") == true);
        page.Close();
        listed.Stash.Clock.Now += Notices.Lasting;
        listed.Turns.Act(listed.Notices.Sweep);
        var imported = await importing.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence);
        CollectionAssert.Contains(listed.Notices.Shown.Select(notice => notice.Text).ToList(), Transferring.ImportedWords("export.zip"), "the notice says so (G2, G16)");
        CollectionAssert.AreEqual(new[] { "one" }, (await listed.Stash.All()).Select(item => item.Name).ToList());
    }

    [TestMethod]
    public async Task AnArchiveRefusedAtTheConfirmationChangesNothingListsTheSameStashAgainAndImportMayBeAskedAgain()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        await listed.Stash.Import("dog");
        await listed.Listing.ShowScope(Scope.All);
        var archive = await Archive(listed, s => s with { Theme = Theme.Dark }, "one");
        var page = await listed.Offered(archive);
        var settings = listed.Settings.Current;
        var before = MovingStash.Tree(listed.Stash.Folder.Root);
        // Changed between the offer and the confirmation: one entry now climbs out of the folder it is extracted into.
        File.Move(ExportedStash.Rewritten(archive, archive + ".rewritten", ("../outside.png", [1])), archive, overwrite: true);

        var refused = await page.ConfirmImport();

        Assert.AreEqual(StashImport.Action, refused.Failure?.Action, refused.Failure?.Sentence);
        Assert.AreEqual(TransferReasons.NotAnExport, refused.Failure!.Reason);
        Assert.AreEqual(refused.Failure, page.WarningOf(SettingsPage.ImportTitle), "on the row (G35)");
        CollectionAssert.Contains(listed.Notices.Shown.Select(notice => notice.Text).ToList(), refused.Failure.Sentence, "and in a notice");
        Assert.AreEqual(StashState.Open, listed.Listing.State);
        CollectionAssert.AreEquivalent(new[] { "cat", "dog" }, listed.Listing.Items.Select(item => item.Name).ToList(), "the same stash listed again");
        Assert.AreEqual(settings, listed.Settings.Current, "the settings unchanged (G36)");
        Assert.IsEmpty(listed.Themes, "no theme applied");
        CollectionAssert.AreEqual(before, MovingStash.Tree(listed.Stash.Folder.Root), "the stash unchanged");
        Assert.IsEmpty(ExportedStash.Beside(listed.Stash), "nothing left beside it");
        Assert.IsNull(listed.Listing.CannotStartReason, "changes may begin again");

        var again = await listed.ImportStash(await Archive(listed, names: ["one"], name: "second.zip"));
        Assert.IsTrue(again.Succeeded, again.Failure?.Sentence);
    }

    [TestMethod]
    public async Task AStepAfterTheRecordRefusedThroughTheRecoveryTooLeavesTheStashClosedNamedUnfinishedUntilAStartFinishesTheImport()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        var archive = await Archive(listed, s => s with { Theme = Theme.Light, StartWithWindows = false }, "one", "two");
        listed.Stash.RecycleBin.Refusing = _ => true;
        var page = await listed.Offered(archive);

        var failed = await page.ConfirmImport();

        Assert.AreEqual(StashImport.Action, failed.Failure?.Action, failed.Failure?.Sentence);
        CollectionAssert.Contains(listed.Notices.Shown.Select(notice => notice.Text).ToList(), failed.Failure!.Sentence, "the failure named (G35)");
        Assert.AreEqual(StashState.Failed, listed.Listing.State, "the listing names a stash that did not open");
        Assert.AreEqual(new Failure(StashImport.FinishAction, "stash", TransferReasons.Unfinished), listed.Listing.OpenFailure);
        Assert.IsNull(listed.Listing.Store, "nothing opened");
        Assert.IsEmpty(listed.Listing.Items);
        Assert.IsNotNull(listed.Settings.Current.ImportPending, "the record stands for the next start (G28)");
        Assert.IsEmpty(listed.Themes, "nothing of it applied yet");
        Assert.IsFalse(page.WholeStashActAsked, "the import has ended");

        listed.Stash.RecycleBin.Refusing = _ => false;
        using var restarted = await listed.Restart();

        Assert.IsNull(restarted.Settings.Current.ImportPending);
        await restarted.Listing.ShowScope(Scope.All);
        CollectionAssert.AreEquivalent(new[] { "one", "two" }, restarted.Listing.Items.Select(item => item.Name).ToList(), "the start finished the import whole");
        Assert.AreEqual(Theme.Light, restarted.Themes.Last(), "and put its theme in effect");
        Assert.AreEqual("clear", restarted.StartWithWindows.Calls.Last());
    }

    [TestMethod]
    public async Task AStepAfterTheRecordRefusedOnlyUntilTheRecoveryIsFinishedByItTheStashListedAndTheSettingsInEffect()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        var archive = await Archive(listed, s => s with { Shortcuts = Imported, Theme = Theme.Dark }, "one", "two");
        var refusing = true;
        listed.Stash.RecycleBin.Refusing = _ => refusing;
        // The refusal ends as the import's failure is named, before the start's finishing runs.
        listed.Notices.Changed += () => refusing &= listed.Listing.State != StashState.Replacing;
        var page = await listed.Offered(archive);

        var imported = await page.ConfirmImport();

        Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence);
        Assert.IsTrue(listed.Notices.Shown.Any(notice => notice.Text.StartsWith(StashImport.Action)), "the refused step named (G35)");
        Assert.IsNull(listed.Settings.Current.ImportPending);
        Assert.AreEqual(StashState.Open, listed.Listing.State);
        await listed.Listing.ShowScope(Scope.All);
        CollectionAssert.AreEquivalent(new[] { "one", "two" }, listed.Listing.Items.Select(item => item.Name).ToList());
        Assert.AreEqual(Theme.Dark, listed.Themes.Last());
        CollectionAssert.AreEquivalent(new[] { "Ctrl+Alt+F2", "Ctrl+Shift+F6", "Ctrl+Shift+F7", "Ctrl+Alt+F3" }, listed.ShortcutsSeam.Registered.Select(shortcut => shortcut.Text).ToList());
        Assert.AreEqual(Transferring.ImportedWords("export.zip"), page.ImportLine);
    }

    [TestMethod]
    public async Task ALeftoverOfAnEarlierImportTheRecoveryCannotSweepIsNamedAndTheImportItFinishedStillOpens()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        var archive = await Archive(listed, s => s with { Theme = Theme.Dark }, "one", "two");
        // What a kill before an earlier import's record left beside the stash, a file in it held by another program.
        var orphan = Directory.CreateDirectory(listed.Stash.Folder.Root + ".importing-0123456789ab").FullName;
        using var held = new FileStream(Path.Combine(orphan, "held.png"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var refusing = true;
        listed.Stash.RecycleBin.Refusing = _ => refusing;
        listed.Notices.Changed += () => refusing &= listed.Listing.State != StashState.Replacing;
        var page = await listed.Offered(archive);

        var imported = await page.ConfirmImport();

        Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence);
        Assert.IsTrue(listed.Notices.Shown.Any(notice => notice.Text.StartsWith(StashImport.FinishAction) && notice.Text.Contains(Path.GetFileName(orphan))),
            "the leftover's sweep named (G35): " + string.Join(" | ", listed.Notices.Shown.Select(notice => notice.Text)));
        Assert.AreEqual(StashState.Open, listed.Listing.State, "the import it finished opens, the leftover unrelated to it (G35)");
        await listed.Listing.ShowScope(Scope.All);
        CollectionAssert.AreEquivalent(new[] { "one", "two" }, listed.Listing.Items.Select(item => item.Name).ToList());
        Assert.AreEqual(Theme.Dark, listed.Themes.Last());
    }

    [TestMethod]
    public async Task ASaveACopyWhosePickerIsAnsweredAfterAnImportIsRefusedByNameAndCopiesNoPictureOfTheItemGivenItsIdentity()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportPicture("cat", "cat.png", MediaFixtures.DistinctPng(0xA0, 8, 6));
        var picker = new TaskCompletionSource<string?>();
        listed.Dialogs.SaveAnswer = () => picker.Task;
        var saving = listed.ItemMenu.SaveACopy(null!, cat, _ => { });
        var archive = await Archive(listed, names: ["dog"]);
        Assert.IsTrue((await listed.ImportStash(archive)).Succeeded, "a picker up is nothing the import waits for");
        var chosen = listed.Stash.Outside("copy.png");

        picker.SetResult(chosen);
        var saved = await saving;

        Assert.AreEqual(new Failure(ItemMenu.SaveACopyAction, "cat", Listing.ReplacedSinceReason), saved.Failure, "refused by name (G35)");
        Assert.IsFalse(File.Exists(chosen), "no picture of the imported item given cat's identity copied in its place (G9, G36)");
    }

    /// <summary>
    /// An export of another stash holding items named <paramref name="names"/>, its settings changed by
    /// <paramref name="settings"/>, written beside <paramref name="listed"/>'s stash as <paramref name="name"/>.
    /// </summary>
    private static async Task<string> Archive(ListedStash listed, Func<Settings, Settings>? settings = null, params string[] names) =>
        await Archive(listed, settings, "export.zip", names);

    private static async Task<string> Archive(ListedStash listed, string[] names, string name) => await Archive(listed, null, name, names);

    private static Task<string> Archive(ListedStash listed, Func<Settings, Settings>? settings, string name, string[] names) =>
        ExportedStash.Of(listed.Stash.Outside(name), settings, names);
}
