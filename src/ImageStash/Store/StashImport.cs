using System.Text.Json;
using ImageStash.Files;
using ImageStash.Seams;
using static ImageStash.Store.MoveFolders;

namespace ImageStash.Store;

/// <summary>
/// Import stash… (G36, State: Import stash…): an export's archive replaces the stash at its location and the settings
/// that travel, the replaced folder going to the Recycle Bin whole (G29). The manifest is read first, everything is
/// extracted into a new folder beside the stash, and the extracted store is opened and migrated there; only then is the
/// record written, the point of no return: before it nothing has changed, after it the stash folder is renamed away,
/// the extracted one renamed in, the replaced one recycled and the settings written, and a kill at any moment is
/// finished at the next start from the names on disk (<see cref="StashImportFolders"/>, G28, G30's rule).
/// </summary>
public static class StashImport
{
    public const string Action = "Import the stash";

    public const string FinishAction = "Finish importing the stash";

    /// <summary>The one reason everything that would begin while an import runs, or on a store it spent, is refused with.</summary>
    public const string InProgress = "your stash is being imported";

    // A stash at a drive's root has no folder beside it for the extracted and the replaced ones.
    private const string AtADriveRoot = "the stash is at the root of a drive, where an import cannot replace it; move the stash into a folder first";

    // How much of a file is extracted between two turns at the queued reads (G1).
    private const int Chunk = 1 << 20;

    /// <summary>
    /// What the archive at <paramref name="archivePath"/> holds, from its manifest alone, with nothing written; or its
    /// refusal by name (G35): a file that is not an export (not a ZIP, no manifest, another format, an entry outside the
    /// stash's layout, no store), or one from a newer build (<see cref="TransferReasons.NewerBuild"/>). A held file is
    /// retried for <paramref name="heldFileRetry"/>.
    /// </summary>
    public static Outcome<ExportSummary> Summary(string archivePath, TimeSpan heldFileRetry)
    {
        var read = ReadWhileHeld(archivePath, StashArchive.Subject(archivePath), heldFileRetry, wait: null);
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        using var archive = read.Value!;
        return Outcome<ExportSummary>.Of(archive.Summary);
    }

    /// <summary>
    /// At start, before the stash at <paramref name="stashFolder"/> is opened: a recorded import finished from the step the
    /// names on disk show, or undone where only that keeps the stash (State: Import stash…); without a record, what a kill
    /// left (an extracted folder beside the stash, a marker in its root) deleted. Every failure is answered; while
    /// <see cref="Settings.ImportPending"/> still stands, nothing may open the stash folder, since opening it would make
    /// an empty stash in place of one in transit, and the next start tries again.
    /// </summary>
    public static Task<IReadOnlyList<Failure>> FinishAtStart(SettingsKeeper keeper, IRecycleBin recycleBin, string stashFolder, TimeSpan heldFileRetry) =>
        Task.Run<IReadOnlyList<Failure>>(() =>
        {
            var stash = Full(stashFolder);
            var steps = new StashImportFolders.Steps(
                (action, folder, step) => StashImportFolders.RetryFolderStep(action, folder, heldFileRetry, step),
                folder => recycleBin.Recycle([folder], heldFileRetry)[0],
                written => written.GetAwaiter().GetResult(),
                (action, file, step) => HeldFileRetry.Run(action, file, heldFileRetry, step));
            var failures = new List<Failure>();
            if (keeper.Current.ImportPending is { } record && StashImportFolders.Finish(FinishAction, keeper, record, stash, steps).Failure is { } unfinished)
            {
                failures.Add(unfinished);
            }

            if (keeper.Current.ImportPending is null)
            {
                failures.AddRange(StashImportFolders.Sweep(FinishAction, stash, steps));
            }

            return failures;
        });

    /// <summary>
    /// Runs the whole import on the store's thread (State: Import stash…): a failure before the record leaves the stash,
    /// the settings and this store as they were, the extracted folder deleted; once the record has landed this store is
    /// spent and the answer carries the stash folder, a failure then leaving the record for <see cref="FinishAtStart"/>.
    /// </summary>
    internal static Outcome<string> Import(StoreContext store, SettingsKeeper keeper, string archivePath, IProgress<TransferProgress>? progress)
    {
        var subject = StashArchive.Subject(archivePath);
        if (store.MoveRefusal(Action, subject) is { } refused)
        {
            return refused;
        }

        string archive;
        string stash;
        try
        {
            archive = Full(archivePath);
            stash = Full(store.Folder.Root);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new Failure(Action, subject, HeldFileRetry.MissingReason);
        }

        if (Same(archive, stash) || Inside(archive, stash))
        {
            return new Failure(Action, subject, MoveReasons.InsideTheStash);
        }

        if (Path.GetDirectoryName(stash) is null)
        {
            return new Failure(Action, subject, AtADriveRoot);
        }

        // An old folder a Move still has to remove may hold this stash, and so the folders made beside it: removed first,
        // as a Move removes it before its own copy, or this import is refused with what keeps it.
        var leftOver = StashMove.FinishLeftOver(keeper, stash, (action, path, step) => store.RetryHeldFile(action, path, step), written => StashMove.WaitAnsweringReads(store, written));
        if (leftOver.FirstOrDefault() is { } unfinished)
        {
            return unfinished;
        }

        var read = ReadWhileHeld(archive, subject, store.RetryDuration, store.Thread.WaitAnsweringReads);
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        ImportPending record;
        using (var archiveRead = read.Value!)
        {
            record = StashImportFolders.Record(stash, archiveRead.Settings);
            // An archive another program takes hold of part way through is extracted again, from an empty folder, for the
            // held-file duration (G35).
            var staged = HeldFileRetry.RunWhileHeld(
                store.RetryDuration,
                () =>
                {
                    Discard(store, record.ExtractedFolder);
                    return Stage(store, archiveRead, record, subject, progress);
                },
                attempt => attempt.Failure?.Reason == HeldFileRetry.HeldReason,
                store.Thread.WaitAnsweringReads);
            if (!staged.Succeeded)
            {
                Discard(store, record.ExtractedFolder);
                return staged.Failure;
            }
        }

        // The point of no return, landed before the stash is touched: from here the next start finishes the import.
        var recorded = StashMove.WaitAnsweringReads(store, keeper.ChangeNow(s => s with { ImportPending = record }));
        if (!recorded.Succeeded)
        {
            Discard(store, record.ExtractedFolder);
            return recorded.Failure;
        }

        // From here this store is not the record: nothing it is asked runs, and the folder holding its file is renamed away.
        store.ImportSpent = true;
        store.Connection.Dispose();
        var steps = new StashImportFolders.Steps(
            (action, folder, step) => StashImportFolders.RetryFolderStep(action, folder, store.RetryDuration, step, store.Thread.WaitAnsweringReads),
            folder => store.Recycle([folder])[0],
            written => StashMove.WaitAnsweringReads(store, written),
            (action, file, step) => store.RetryHeldFile(action, file, step));
        var finished = StashImportFolders.Finish(Action, keeper, record, stash, steps, progress);
        return finished.Succeeded ? Outcome<string>.Of(store.Folder.Root) : Outcome<string>.FailAfterCommit(finished.Failure, store.Folder.Root);
    }

    // The archive's files extracted into the record's folder, the queued reads answered between chunks (G1); its store
    // opened and migrated there (G31); then the marker written. Failures name the import and the archive.
    private static Outcome Stage(StoreContext store, ReadArchive archive, ImportPending record, string subject, IProgress<TransferProgress>? progress)
    {
        var folder = record.ExtractedFolder;
        try
        {
            Directory.CreateDirectory(folder);
            progress?.Report(new TransferProgress(TransferPhase.Extracting, 0, archive.Files.Count));
            for (var i = 0; i < archive.Files.Count; i++)
            {
                var (entry, path) = archive.Files[i];
                var target = Path.Combine(folder, path);
                // Every entry's name was checked to be a root file, a file of originals\ or the store; this is the last word.
                if (!Inside(target, folder))
                {
                    return new Failure(Action, subject, TransferReasons.NotAnExport);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using (var from = entry.Open())
                using (var to = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var chunk = new byte[Chunk];
                    int length;
                    while ((length = from.Read(chunk)) > 0)
                    {
                        to.Write(chunk, 0, length);
                        store.Thread.AnswerQueuedReads();
                    }
                }

                progress?.Report(new TransferProgress(TransferPhase.Extracting, i + 1, archive.Files.Count));
            }
        }
        catch (InvalidDataException e)
        {
            return new Failure(Action, subject, HeldFileRetry.IsHeldRefusal(e) ? HeldFileRetry.HeldReason : "it is damaged or incomplete");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Failure(Action, subject, HeldFileRetry.IsHeldRefusal(e) ? HeldFileRetry.HeldReason : HeldFileRetry.ReasonFor(e));
        }

        var staging = new StashFolder(folder);
        var staged = StoreOpen.OpenStaged(store, staging, TransferReasons.NotAnExport, StagedStore.Read);
        if (!staged.Succeeded)
        {
            return new Failure(Action, subject, staged.Failure.Reason);
        }

        if (!staged.Value!.AgreesWith(staging, archive.Manifest.Items))
        {
            return new Failure(Action, subject, TransferReasons.NotAnExport);
        }

        var marker = Path.Combine(folder, record.Marker);
        return store.RetryHeldFile(Action, marker, () => File.WriteAllBytes(marker, []));
    }

    // The extracted folder of an import that did not reach its record, which only this import made: deleted whole, or,
    // while a file in it is still held, left to the next start's sweep.
    private static void Discard(StoreContext store, string folder)
    {
        if (Directory.Exists(folder))
        {
            StashImportFolders.RetryFolderStep(Action, folder, store.RetryDuration, () => Directory.Delete(folder, recursive: true), store.Thread.WaitAnsweringReads);
        }
    }

    private static FileStream OpenArchive(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    // The archive opened and read, again from a fresh open while another program holds it (its open or any read of it
    // refused), for at most the held-file duration; then the refusal names it as held (G35, E3).
    private static Outcome<ReadArchive> ReadWhileHeld(string path, string subject, TimeSpan duration, Action<TimeSpan>? wait) =>
        HeldFileRetry.RunWhileHeld(
            duration,
            () =>
            {
                var opened = HeldFileRetry.Run(Action, path, TimeSpan.Zero, () => OpenArchive(path));
                return opened.Succeeded ? StashArchive.Read(opened.Value!, Action, subject) : (Outcome<ReadArchive>)opened.Failure;
            },
            read => read.Failure?.Reason == HeldFileRetry.HeldReason,
            wait);

    /// <summary>
    /// What the extracted store records, read once in the staging folder after its Open with… sessions are let go: how
    /// many items it lists, each row's file names, plain and in its steps' and records' JSON, which its open would join
    /// to the stash folder, and whether every record decodes as a build writes it.
    /// </summary>
    private sealed record StagedStore(long Listed, IReadOnlyList<StagedStore.Row> Rows, bool Decodes)
    {
        public static StagedStore Read(StoreContext staged)
        {
            // An Open with… session names a copy outside the stash, kept for an item of the PC that exported it or of the
            // stash this import replaces, whose sessions end with it (State: Open with…): the imported item has none here,
            // so no copy of another item is classified, or adopted, as its rewrite (G36, G27).
            staged.Execute("UPDATE items SET open_with_session = NULL WHERE open_with_session IS NOT NULL");
            try
            {
                var rows = staged.Query(
                    "SELECT finished_file, original_file, original_in_originals, pending_steps, deleted, held_copies, open_with_session, adoption_undo FROM items",
                    r => new Row(r.GetString(0), r.GetString(1), r.GetBoolean(2), Text(r, 3), r.GetBoolean(4), Text(r, 5), Text(r, 6), Text(r, 7)));
                if (!rows.All(row => row.Decodes()))
                {
                    return Undecodable;
                }

                // The items the stash lists, read as its listing reads them, so a table or column a listing needs that is
                // missing throws as no such table or column, and the count is the one the export's manifest gives (G36).
                var listed = StashItem.CountListed(staged);
                staged.Scalar("SELECT COUNT(item_id) + COUNT(used_at) FROM uses");
                return new(listed, rows, Decodes: true);
            }
            catch (Exception e) when (e is not Microsoft.Data.Sqlite.SqliteException)
            {
                // A value no build writes (a format, a time, a missing name) that a reader of the rows rejects by throwing:
                // refused by name as no export, never thrown (G35). SQLite's own errors stay the open's to name or retry.
                return Undecodable;
            }
        }

        private static readonly StagedStore Undecodable = new(0, [], Decodes: false);

        /// <summary>
        /// Whether this is the store of the archive that carried it (G36, G35): as many items as its manifest says, every
        /// name a row records a plain file name, so no step its open finishes reaches outside the stash folder, and every
        /// settled item's files in the folder. An archive no ImageStash wrote is refused before its record.
        /// </summary>
        public bool AgreesWith(StashFolder staging, int manifestItems)
        {
            if (!Decodes || Listed != manifestItems)
            {
                return false;
            }

            try
            {
                return Rows.All(row => row.Names().All(StashArchive.IsPlainFileName)
                    && (row.Deleted || row.PendingSteps is not null
                        || (File.Exists(staging.InRoot(row.FinishedFile)) && File.Exists(row.InOriginals ? staging.InOriginals(row.OriginalFile) : staging.InRoot(row.OriginalFile)))));
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string? Text(Microsoft.Data.Sqlite.SqliteDataReader r, int column) => r.IsDBNull(column) ? null : r.GetString(column);

        public sealed record Row(string FinishedFile, string OriginalFile, bool InOriginals, string? PendingSteps, bool Deleted, string? HeldCopies, string? OpenWith, string? AdoptionUndo)
        {
            /// <summary>
            /// Whether its steps and held copies decode as a build writes them: JSON, no entry left null, and every step of a
            /// kind a build writes with both its names, since its open would otherwise leave the row unlisted for good.
            /// </summary>
            public bool Decodes()
            {
                try
                {
                    return (PendingSteps is null || PendingStep.Decode(PendingSteps).All(step => step is { From: not null, To: not null } && PendingStep.Kinds.Contains(step.Step)))
                        && (HeldCopies is null || HeldCopy.Decode(HeldCopies).All(copy => copy is not null));
                }
                catch (JsonException)
                {
                    return false;
                }
            }

            // Every name the store joins to a folder of the stash: a column or record that comes to name a file joins here.
            // A step's previous name is the item's name, which a rename's undo puts back, never a file's.
            public IEnumerable<string?> Names()
            {
                var steps = PendingSteps is null ? [] : PendingStep.Decode(PendingSteps);
                var copies = HeldCopies is null ? [] : HeldCopy.Decode(HeldCopies);
                var session = OpenWith is null ? null : OpenWithRecord.Decode(OpenWith);
                var undo = AdoptionUndo is null ? null : Store.AdoptionUndo.Decode(AdoptionUndo);
                return
                [
                    FinishedFile, OriginalFile,
                    .. steps.SelectMany(step => new[] { step.From, step.To }),
                    .. copies.Select(copy => copy.Copy),
                    .. session is null ? [] : new[] { session.Folder, session.File },
                    .. undo is null ? [] : new[] { undo.OriginalFile, undo.FinishedFile }.Concat(undo.HeldCopy is { } held ? [held] : []),
                ];
            }
        }
    }
}
