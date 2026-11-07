using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using ImageStash.Files;
using static ImageStash.Store.MoveFolders;

namespace ImageStash.Store;

/// <summary>
/// Export stash… (G36, State: Export stash…): the whole stash (every root and <c>originals\</c> file and the store) and
/// the settings that travel written into one archive (<see cref="StashArchive"/>) beside the chosen path and renamed over
/// it (R2), on the store's thread between transactions, the queued reads answered between chunks (G1). Nothing in the
/// stash changes but a change of an item's files a live failure stopped, finished first, or removed as the next start
/// would when no query lists its row; the store stays open and live whatever happens; a failure or a kill leaves no
/// partial file at the chosen path, and a file already there is replaced only whole.
/// </summary>
public static class StashExport
{
    public const string Action = "Export the stash";

    /// <summary>The one reason everything that would begin while an export runs is refused with.</summary>
    public const string InProgress = "your stash is being exported";

    public const string Extension = ".zip";

    // How much of a file is copied between two turns at the queued reads, so a large picture holds no listing back (G1).
    private const int Chunk = 1 << 20;

    /// <summary>The file name the save picker suggests, named for the moment as an auto name is (F12).</summary>
    public static string SuggestedFileName(DateTimeOffset localNow) =>
        "ImageStash export " + localNow.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture) + Extension;

    /// <summary>The whole export on the store's thread; a failure names the action and the archive's file name (G35).</summary>
    internal static Outcome Export(StoreContext store, SettingsKeeper keeper, string archivePath, IProgress<TransferProgress>? progress)
    {
        var subject = StashArchive.Subject(archivePath);
        if (store.MoveRefusal(Action, subject) is { } refused)
        {
            return refused;
        }

        var judged = Judge(store, archivePath, subject);
        if (!judged.Succeeded)
        {
            return judged.Failure;
        }

        var archive = judged.Value!;
        if (FinishStoppedChanges(store) is { } unfinished)
        {
            return unfinished;
        }

        var facts = store.Read(Action, () => (
            Version: (long)store.Scalar("PRAGMA user_version")!,
            // The items the stash lists, as its listing reads them: a row whose file has yet to reach the root is not one,
            // and the import's open drops it, so the summary offers what the import brings (G36).
            Items: (long)StashItem.CountListed(store)));
        if (!facts.Succeeded)
        {
            return facts.Failure;
        }

        List<(string Source, string Entry)> files;
        try
        {
            files = Files(store.Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Failure(Action, Subject(store.Folder.Root), HeldFileRetry.ReasonFor(e));
        }

        // Beside the chosen path, on its volume, so the rename over it is whole (S1); a name no file there has.
        string Partial() => Path.Combine(Path.GetDirectoryName(archive)!, "." + Path.GetFileName(archive) + "." + Guid.NewGuid().ToString("N")[..12] + ".partial");
        var partial = Partial();
        var manifest = new StashArchive.Manifest(StashArchive.Format, StashArchive.FormatVersion, facts.Value.Version, StashArchive.Build, (int)facts.Value.Items);
        // A file another program holds part way through is retried into a fresh partial archive, the last one deleted, for
        // the held-file duration (G35).
        var written = HeldFileRetry.RunWhileHeld(
            store.RetryDuration,
            () =>
            {
                if (File.Exists(partial))
                {
                    store.RetryHeldFile(Action, partial, () => File.Delete(partial));
                    partial = Partial();
                }

                return Write(store, partial, manifest, TravellingSettings.Of(keeper.Current), files, subject, progress);
            },
            attempt => attempt.Failure?.Reason == HeldFileRetry.HeldReason,
            store.Thread.WaitAnsweringReads);
        if (written.Succeeded)
        {
            written = store.RetryHeldFile(Action, archive, () => File.Move(partial, archive, overwrite: true), replaced: archive);
        }

        if (!written.Succeeded && File.Exists(partial))
        {
            store.RetryHeldFile(Action, partial, () => File.Delete(partial));
        }

        return written;
    }

    // A change of an item's files a live failure stopped (a Save, Revert, Save as new item, Rename, Open with… adoption or
    // its Undo) may have left a file the row names in .work\, which the archive leaves out: each is finished first, as a
    // later change of the item's files does, and a row no query lists (an import or a Save as new item whose file never
    // reached the root) is removed as the next start would; the export is refused by name, naming the item, only while
    // one still cannot be (State: Export stash…, A live failure).
    private static Failure? FinishStoppedChanges(StoreContext store)
    {
        var stopped = Stopped(store);
        if (!stopped.Succeeded)
        {
            return stopped.Failure;
        }

        foreach (var (files, name) in stopped.Value!)
        {
            if (PendingStepRecovery.FinishBeforeChange(store, files, name, Action) is { } unfinished)
            {
                return unfinished;
            }
        }

        // What a finish leaves pending is a row no later change can reach.
        var left = Stopped(store);
        if (!left.Succeeded)
        {
            return left.Failure;
        }

        foreach (var (files, name) in left.Value!)
        {
            if (PendingStepRecovery.UndoUnlisted(store, files) is { } kept)
            {
                return new Failure(Action, ItemQueries.Named(name, files.FinishedFile), kept.Reason);
            }
        }

        return null;
    }

    private static Outcome<List<(StoredFiles Files, string Name)>> Stopped(StoreContext store) =>
        store.Read(Action, () => store.Query(
            $"SELECT {StoredFiles.Columns}, i.name FROM items i WHERE {StashItem.NotDeleted} AND i.pending_steps IS NOT NULL ORDER BY i.id",
            r => (Files: StoredFiles.FromRow(r, 0), Name: r.GetString(StoredFiles.ColumnCount))));

    // The chosen path as its full path, or why an archive cannot be written there, judged before anything is written: a
    // folder that is not there, or a place inside the stash folder under any spelling (a marker looked for under it).
    private static Outcome<string> Judge(StoreContext store, string archivePath, string subject)
    {
        string archive;
        try
        {
            archive = Full(archivePath);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new Failure(Action, subject, HeldFileRetry.FolderMissingReason);
        }

        var folder = Path.GetDirectoryName(archive);
        if (folder is null || Path.GetFileName(archive).Length == 0 || !Directory.Exists(folder))
        {
            return new Failure(Action, subject, HeldFileRetry.FolderMissingReason);
        }

        if (Inside(archive, store.Folder.Root))
        {
            return new Failure(Action, subject, MoveReasons.InsideTheStash);
        }

        var spelled = SpelledUnder(Action, folder, store.Folder.Root, (action, path, step) => store.RetryHeldFile(action, path, step));
        if (!spelled.Succeeded)
        {
            return new Failure(Action, subject, spelled.Failure.Reason);
        }

        return spelled.Value is null ? Outcome<string>.Of(archive) : new Failure(Action, subject, MoveReasons.InsideTheStash);
    }

    // Every file the archive carries, by its path on disk and its entry's name: the store first, then the root's files
    // under their names and originals\' as originals/<name>; never the store's journal, .work\, or a marker a kill left.
    private static List<(string Source, string Entry)> Files(StashFolder folder)
    {
        bool Carried(string file) =>
            !StashArchive.IsStoreFileName(Path.GetFileName(file)) && !(IsMarker(file) && new FileInfo(file).Length == 0) && !StashImportFolders.IsMarker(file);
        return
        [
            (folder.StoreFile, StashArchive.StoreEntry),
            .. Directory.EnumerateFiles(folder.Root).Where(Carried).Select(file => (file, Path.GetFileName(file))),
            .. (Directory.Exists(folder.Originals) ? Directory.EnumerateFiles(folder.Originals) : [])
                .Select(file => (file, StashArchive.OriginalsFolder + "/" + Path.GetFileName(file))),
        ];
    }

    // The archive written at its partial path, one entry at a time, each source opened as a held file is retried and
    // copied in chunks between answered reads; anything refused names the archive (G35).
    private static Outcome Write(
        StoreContext store, string partial, StashArchive.Manifest manifest, TravellingSettings settings, List<(string Source, string Entry)> files, string subject, IProgress<TransferProgress>? progress)
    {
        try
        {
            using var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            WriteJson(zip, StashArchive.ManifestEntry, manifest);
            WriteJson(zip, StashArchive.SettingsEntry, settings);
            progress?.Report(new TransferProgress(TransferPhase.Writing, 0, files.Count));
            for (var i = 0; i < files.Count; i++)
            {
                var (source, name) = files[i];
                var opened = store.RetryHeldFile(Action, source, () => new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                if (!opened.Succeeded)
                {
                    return opened.Failure;
                }

                // Pictures are compressed already; the store compresses well.
                var level = name == StashArchive.StoreEntry ? CompressionLevel.Optimal : CompressionLevel.NoCompression;
                using (var from = opened.Value!)
                using (var to = zip.CreateEntry(name, level).Open())
                {
                    var chunk = new byte[Chunk];
                    int length;
                    while ((length = from.Read(chunk)) > 0)
                    {
                        to.Write(chunk, 0, length);
                        store.Thread.AnswerQueuedReads();
                    }
                }

                progress?.Report(new TransferProgress(TransferPhase.Writing, i + 1, files.Count));
            }

            return Outcome.Done;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Failure(Action, subject, HeldFileRetry.IsHeldRefusal(e) ? HeldFileRetry.HeldReason : HeldFileRetry.ReasonFor(e));
        }
    }

    private static void WriteJson<T>(ZipArchive zip, string name, T value)
    {
        using var to = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        JsonSerializer.Serialize(to, value, Settings.Json);
    }
}
