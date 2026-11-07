using Avalonia.Controls;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Export stash… and Import stash… from the Settings page (G36, G32, State: Export stash…, Import stash…), the next
/// whole-stash act beside <see cref="Moving"/>, whose wait and reopen it shares. Export: the save picker with a suggested
/// name and the export's one file type; from the moment a path is chosen until the export has ended every beginning of a
/// change is refused by name (<see cref="Listing.WholeStashActAsked"/>); the wait as Move waits; Store's export, its busy
/// state naming the export and the path; where the file is said, on the row and in a notice, only once Store has renamed
/// it into place (R9). Import: the one-file picker; the file's summary read off the UI thread, a file that is not an
/// export or a newer build's refused by name; then, blocking nothing, the confirmation offered inside the Import row (F4):
/// its count and that the stash and the settings that travel will be replaced. From the confirmation nothing else starts;
/// the wait; the listing withdrawn, so nothing of the stash is listed and no file of it is held open (a held file would
/// stop the folder's rename); Store's import; then, once its record has been written, what the window remembered of the
/// replaced items by identity forgotten, the imported settings put in effect at once, and the stash at the same folder
/// opened again and listed before "Imported" is said (G36, G6, G19, G26, G27, R9). An import that changed nothing lists
/// the same stash again; one whose step after the record failed runs the start's finishing once, and opens the stash only
/// once that has finished it, else names it unfinished and opens nothing until a start finishes it (G28, G35).
/// </summary>
public sealed class Transferring
{
    /// <summary>The save picker's title (G36).</summary>
    public const string ExportPickerTitle = "Export your stash";

    /// <summary>The one-file picker's title (G36).</summary>
    public const string ImportPickerTitle = "Choose an ImageStash export to import";

    /// <summary>What a refusal before the picker names: there is no chosen file yet.</summary>
    private const string StashSubject = "the stash";

    /// <summary>Why an export or an import is refused while a file is still being chosen for one.</summary>
    private const string ChoosingReason = "a file is still being chosen for an export or an import";

    /// <summary>The export's one file type, in both pickers (G36).</summary>
    public static readonly PickerFilter FileType = new("ImageStash export", ["*" + StashExport.Extension]);

    private readonly Listing listing;
    private readonly Notices notices;
    private readonly SettingsKeeper keeper;
    private readonly IDialogsAndExplorer dialogs;
    private readonly IRecycleBin recycleBin;
    private readonly Handoff handoff;
    private readonly OpenWith openWith;
    private readonly Moving moving;
    private readonly Func<Settings, Outcome> applyImported;
    private readonly TimeProvider clock;
    private readonly TimeSpan heldFileRetry;

    // A picker is up, or the chosen file's summary is being read.
    private bool picking;

    // The InProgress words of the export or the import asked, from the asking until it has ended.
    private string? asked;
    private bool exporting;
    private bool importing;
    private string? busy;
    private string? exported;
    private string? imported;

    /// <param name="notices">The stash window's notices, where every outcome is said; they run whether or not the window shows (G16).</param>
    /// <param name="keeper">The one settings keeper: the export reads the settings that travel from it, the import writes them through it.</param>
    /// <param name="recycleBin">Where the start's finishing, run once after a failure past the import's record, sends the replaced folder (G29).</param>
    /// <param name="handoff">The window's one clipboard writer, whose write of the replaced stash's items is forgotten (G26).</param>
    /// <param name="openWith">Open with…, whose adoption history of the replaced stash's items is forgotten (G27).</param>
    /// <param name="moving">Move…, whose wait and reopen the export and the import share.</param>
    /// <param name="applyImported">Puts the settings that travel in effect in the running app at once (G36); a refusal is answered to name.</param>
    /// <param name="clock">The local time the save picker's suggested name is made from (F12).</param>
    public Transferring(Listing listing, Notices notices, SettingsKeeper keeper, IDialogsAndExplorer dialogs, IRecycleBin recycleBin, Handoff handoff, OpenWith openWith,
        Moving moving, Func<Settings, Outcome> applyImported, TimeProvider clock, TimeSpan heldFileRetry)
    {
        this.listing = listing;
        this.notices = notices;
        this.keeper = keeper;
        this.dialogs = dialogs;
        this.recycleBin = recycleBin;
        this.handoff = handoff;
        this.openWith = openWith;
        this.moving = moving;
        this.applyImported = applyImported;
        this.clock = clock;
        this.heldFileRetry = heldFileRetry;
    }

    /// <summary>Raised whenever a line, the offer or <see cref="Asked"/> changes.</summary>
    public event Action? Changed;

    /// <summary>True from the moment an export has its path, or an import is confirmed, until it has ended, in success or failure.</summary>
    public bool Asked => asked is not null;

    /// <summary>The Export row's line: the export's busy state while one is asked, else where the last one wrote its file, or null.</summary>
    public string? ExportLine => exporting ? busy : exported;

    /// <summary>The Import row's line: the import's busy state while one is asked, else what the last one said on success, or null.</summary>
    public string? ImportLine => importing ? busy : imported;

    /// <summary>The import offered for confirmation inside the Import row, until it is confirmed or cancelled; null while none is.</summary>
    public ImportOffer? Offer { get; private set; }

    /// <summary>
    /// Export stash… (G36): refused while the stash cannot take a change or another whole-stash act is asked; then the
    /// save picker owned by <paramref name="owner"/>, whose cancel changes nothing; then the wait and Store's export.
    /// Answers the outcome for the page's row, a refusal before the picker named there alone, every later one also in
    /// the notices.
    /// </summary>
    public async Task<Outcome> Export(TopLevel owner)
    {
        if (CannotBeginReason is { } refused)
        {
            return new Failure(StashExport.Action, StashSubject, refused);
        }

        exported = null;
        string? chosen;
        picking = true;
        try
        {
            chosen = await dialogs.SaveFile(owner, ExportPickerTitle, StashExport.SuggestedFileName(clock.GetLocalNow()), FileType);
        }
        catch (Exception e)
        {
            // The platform's dialog failing is a failure to name, not a crash (G35).
            return Named(new Failure(StashExport.Action, "the save picker", e.Message));
        }
        finally
        {
            picking = false;
        }

        if (chosen is null)
        {
            Changed?.Invoke();
            return Outcome.Done;
        }

        // The stash may have stopped taking changes while the picker was up.
        if (listing.CannotStartReason is not null || listing.Store is not { } store)
        {
            return Named(new Failure(StashExport.Action, Path.GetFileName(chosen), listing.CannotStartReason!));
        }

        Begin(StashExport.InProgress, export: true, ExportingWords(chosen));
        try
        {
            await moving.WaitUntilNothingIsInTheWay(words => SetBusy($"Waiting for {words} before exporting your stash…"));
            // In the same turn as the last reading found nothing in the way: from here the export is Store's (State: Move).
            listing.WholeStashActBegun = true;
            SetBusy(ExportingWords(chosen));
            var written = await Reporting(progress => store.Export(keeper, chosen, progress), told => ExportProgress(chosen, told));
            if (!written.Succeeded)
            {
                return Named(written.Failure);
            }

            exported = ExportedWords(chosen);
            notices.Raise(exported);
            return Outcome.Done;
        }
        finally
        {
            End();
            // Whatever was put off meanwhile asks again, Open with…'s copies among them.
            _ = listing.Refresh();
        }
    }

    /// <summary>
    /// Import stash… (G36): refused while the stash cannot take a change or a whole-stash act is asked; then the one-file
    /// picker owned by <paramref name="owner"/>, whose cancel changes nothing; then the file's summary, read off the UI
    /// thread, its refusal named (a file that is not an export, or a newer build's); otherwise the offer inside the
    /// Import row, which blocks nothing until it is confirmed (<see cref="ConfirmImport"/>).
    /// </summary>
    public async Task<Outcome> ChooseArchive(TopLevel owner)
    {
        if (CannotBeginReason is { } refused)
        {
            return new Failure(StashImport.Action, StashSubject, refused);
        }

        imported = null;
        Offer = null;
        picking = true;
        try
        {
            string? chosen;
            try
            {
                chosen = await dialogs.OpenFile(owner, ImportPickerTitle, [FileType]);
            }
            catch (Exception e)
            {
                return Named(new Failure(StashImport.Action, "the file picker", e.Message));
            }

            if (chosen is null)
            {
                return Outcome.Done;
            }

            var summary = await Task.Run(() => StashImport.Summary(chosen, heldFileRetry));
            if (!summary.Succeeded)
            {
                return Named(summary.Failure);
            }

            Offer = new ImportOffer(chosen, summary.Value!);
            return Outcome.Done;
        }
        finally
        {
            picking = false;
            Changed?.Invoke();
        }
    }

    /// <summary>The offer's Cancel: the Import row at rest, nothing changed.</summary>
    public void CancelImport()
    {
        Offer = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// The offer's Import, the user's confirmation, from which nothing else starts until the import has ended (G36):
    /// the wait, the listing withdrawn, Store's import, and the stash listed again, as the type's summary says. Answers
    /// the outcome for the page's row; every failure is also named in the notices.
    /// </summary>
    public async Task<Outcome> ConfirmImport()
    {
        if (Offer is not { } offer)
        {
            return Outcome.Done;
        }

        Offer = null;
        var subject = Path.GetFileName(offer.Path);
        if (CannotBeginReason is not null || listing.Store is not { } store)
        {
            return Named(new Failure(StashImport.Action, subject, CannotBeginReason!));
        }

        Begin(StashImport.InProgress, export: false, ImportingWords(subject));
        try
        {
            await moving.WaitUntilNothingIsInTheWay(words => SetBusy($"Waiting for {words} before importing {subject}…"));
            listing.WholeStashActBegun = true;
            SetBusy(ImportingWords(subject));
            await listing.Withdraw();
            var replaced = await Reporting(progress => store.Import(keeper, offer.Path, progress), told => ImportProgress(subject, told));
            if (replaced.Value is not { } folder)
            {
                // Nothing changed: the same stash is listed again (G36, G35).
                await listing.StashOpened(store);
                return Named(replaced.Failure!);
            }

            // Past the record the replaced stash is gone, or goes at the next start, and the imported store gives its
            // items' identities to others: nothing the window keeps by identity stands (G26, G27).
            listing.StashReplaced();
            handoff.ForgetClipboardWrite();
            openWith.ForgetReplacedStash();
            if (!replaced.Succeeded && !await FinishedAfterFailure(store, folder, replaced.Failure))
            {
                return replaced.Failure;
            }

            if (applyImported(keeper.Current) is { Succeeded: false } notApplied)
            {
                notices.Raise(notApplied.Failure);
            }

            var opened = await moving.OpenAgain(store, folder, answered: () => { });
            if (!opened.Succeeded)
            {
                return opened.Failure;
            }

            imported = ImportedWords(subject);
            notices.Raise(imported);
            return Outcome.Done;
        }
        finally
        {
            End();
        }
    }

    /// <summary>What an export that has ended in success says (R9).</summary>
    public static string ExportedWords(string archivePath) => $"Exported your stash to {archivePath}.";

    /// <summary>What an import that has ended in success says, once the imported stash is listed (R9).</summary>
    public static string ImportedWords(string archiveName) => $"Imported your stash from {archiveName}.";

    /// <summary>The offer's sentence: the file's count, and that the stash and the settings that travel will be replaced by it (G36).</summary>
    public static string OfferWords(ImportOffer offer) =>
        $"{Path.GetFileName(offer.Path)} holds {(offer.Summary.Items == 1 ? "1 item" : $"{offer.Summary.Items} items")}. "
        + "Importing it replaces your stash and these settings with its own: the four shortcuts, Also copy, the recording's maximum length "
        + "and pointer, Start with Windows and Theme. What it replaces goes to the Recycle Bin.";

    /// <summary>Why neither may begin now: one is asked or being chosen, or the stash cannot take a change; null when one may.</summary>
    private string? CannotBeginReason => asked ?? (picking ? ChoosingReason : null) ?? listing.CannotStartReason;

    /// <summary>
    /// A step after the import's record was refused (State: Import stash…): named, then the start's finishing run once,
    /// its failures named; true once that has finished the import, its record cleared, so the stash may open, as the
    /// start opens it then whatever leftover of an earlier import it could not sweep (G35). Otherwise the listing names it
    /// unfinished where it names a stash that did not open, and nothing opens, since opening the folder would make an
    /// empty stash in the place of one in transit; the next start finishes it (G28, G35).
    /// </summary>
    private async Task<bool> FinishedAfterFailure(StashStore spent, string folder, Failure failure)
    {
        notices.Raise(failure);
        foreach (var each in await StashImport.FinishAtStart(keeper, recycleBin, folder, heldFileRetry))
        {
            notices.Raise(each);
        }

        if (keeper.Current.ImportPending is null)
        {
            return true;
        }

        moving.LetGo(spent, new Failure(StashImport.FinishAction, Path.GetFileName(folder), TransferReasons.Unfinished));
        return false;
    }

    /// <summary>Store's run with its progress told on the UI thread, where a report posted after it has answered is no longer shown.</summary>
    private async Task<T> Reporting<T>(Func<IProgress<TransferProgress>, Task<T>> run, Func<TransferProgress, string> words)
    {
        var live = true;
        var progress = new Progress<TransferProgress>(told =>
        {
            if (live)
            {
                SetBusy(words(told));
            }
        });
        try
        {
            return await run(progress);
        }
        finally
        {
            live = false;
        }
    }

    private static string ExportingWords(string archivePath) => $"Exporting your stash to {archivePath}…";

    private static string ImportingWords(string archiveName) => $"Importing {archiveName}…";

    private static string ExportProgress(string archivePath, TransferProgress told) => told.Phase == TransferPhase.Writing
        ? $"Exporting your stash to {archivePath}: {told.Done} of {told.Total} files written…"
        : $"Exporting your stash to {archivePath}: finishing…";

    private static string ImportProgress(string archiveName, TransferProgress told) => told.Phase switch
    {
        TransferPhase.Extracting => $"Importing {archiveName}: {told.Done} of {told.Total} files extracted…",
        TransferPhase.Replacing => $"Importing {archiveName}: replacing your stash…",
        _ => $"Importing {archiveName}: finishing…",
    };

    /// <summary>The export or the import is asked: every beginning of a change is refused with its words until it ends.</summary>
    private void Begin(string inProgress, bool export, string words)
    {
        asked = inProgress;
        exporting = export;
        importing = !export;
        listing.WholeStashActAsked = inProgress;
        SetBusy(words);
    }

    /// <summary>It has ended: changes may begin again.</summary>
    private void End()
    {
        asked = null;
        exporting = importing = false;
        listing.WholeStashActAsked = null;
        listing.WholeStashActBegun = false;
        SetBusy(null);
    }

    private Outcome Named(Failure failure)
    {
        notices.Raise(failure);
        return failure;
    }

    private void SetBusy(string? words)
    {
        busy = words;
        Changed?.Invoke();
    }
}

/// <summary>An import offered for confirmation: the chosen file, and what its manifest says it holds (G36).</summary>
public sealed record ImportOffer(string Path, ExportSummary Summary);
