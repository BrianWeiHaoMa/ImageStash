using ImageStash.Files;
using static ImageStash.Store.MoveFolders;

namespace ImageStash.Store;

/// <summary>
/// The folders an import works with (State: Import stash…): the extracted folder, <c>&lt;stash&gt;.importing-&lt;id&gt;</c>,
/// and the replaced one, <c>&lt;stash&gt;.replaced-&lt;id&gt;</c>, both beside the stash folder so every rename stays on
/// one volume (S1, R2); the marker the extracted folder's root holds until the last step; the steps a recorded import
/// still owes, chosen by what the names on disk show, each one a kill in the middle of leaves for the next start to see;
/// and, without a record, what a kill left swept.
/// </summary>
internal static class StashImportFolders
{
    private const string ImportingInfix = ".importing-";

    private const string ReplacedInfix = ".replaced-";

    private const string MarkerPrefix = ".imagestash-import-";

    /// <summary>The steps of a recorded import, each retried while held as the caller's thread retries (G35).</summary>
    /// <param name="Folder">A step on a whole folder (a rename, a deletion), retried as <see cref="RetryFolderStep"/> retries.</param>
    /// <param name="Recycle">The replaced folder sent to the Recycle Bin whole, through the seam (G29).</param>
    /// <param name="Wait">A settings write waited for.</param>
    /// <param name="File">A step on one file.</param>
    public sealed record Steps(FileStep Folder, Func<string, Outcome> Recycle, Func<Task<Outcome>, Outcome> Wait, FileStep File);

    // The id an import's folders and marker carry: twelve lowercase hex digits, so nothing of anyone else's is taken for one.
    private const int IdLength = 12;

    /// <summary>The record of a new import of the stash at <paramref name="stash"/>, its folders' and marker's names made fresh.</summary>
    public static ImportPending Record(string stash, TravellingSettings settings)
    {
        var id = Guid.NewGuid().ToString("N")[..IdLength];
        return new ImportPending(stash + ImportingInfix + id, stash + ReplacedInfix + id, MarkerPrefix + id, settings);
    }

    /// <summary>A marker an import wrote: a file named so and empty, which no item's file is.</summary>
    public static bool IsMarker(string file)
    {
        if (!Path.GetFileName(file).StartsWith(MarkerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return new FileInfo(file) is { Exists: true, Length: 0 };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs a step on <paramref name="folder"/>, again while Windows refuses it as it refuses a folder holding a file another
    /// program has open (access denied, whatever that file's share mode, or a sharing violation), for at most
    /// <paramref name="duration"/>, its waits spent in <paramref name="wait"/> or asleep: <see cref="HeldFileRetry.Run"/>
    /// probes the path as a file, which a folder never answers, so it would give up after two tries. Any other refusal fails
    /// at once. A failure names <paramref name="action"/> and the folder, held when a file inside it still is.
    /// </summary>
    public static Outcome RetryFolderStep(string action, string folder, TimeSpan duration, Action step, Action<TimeSpan>? wait = null)
    {
        Exception? refused = null;
        HeldFileRetry.RunWhileHeld(
            duration,
            () =>
            {
                try
                {
                    step();
                    refused = null;
                    return false;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    refused = e;
                    return MayBeHeld(e);
                }
            },
            mayBeHeld => mayBeHeld,
            wait);
        return refused is null ? Outcome.Done : new Failure(action, Subject(folder), HeldFileRetry.HoldsAnOpenFile(folder) ? HeldFileRetry.HeldReason : HeldFileRetry.ReasonFor(refused));
    }

    /// <summary>
    /// Takes a recorded import as far as it goes, from the step the names on disk show, the marker deciding where the names
    /// cannot (State: Import stash…): the stash renamed away, the extracted folder renamed in, the
    /// replaced folder recycled, then the travelling values applied and the record cleared in one write, then the marker
    /// deleted; or, when the extracted folder vanished before it was renamed in, the record cleared; or, with the stash and
    /// the extracted folder both gone, the replaced one renamed back. Every step a kill interrupts is seen again by the
    /// names; a failure leaves the record for the next start. A record that names no folders of this stash's import is
    /// forgotten, naming it, with nothing touched.
    /// </summary>
    public static Outcome Finish(string action, SettingsKeeper keeper, ImportPending record, string stash, Steps steps, IProgress<TransferProgress>? progress = null)
    {
        // The seam and the settings writer name their own action; a step of the import is named as the import (G35).
        Outcome Named(Outcome step) => step.Succeeded ? step : new Failure(action, step.Failure.Subject, step.Failure.Reason);
        Outcome Write(Func<Settings, Settings> change) => Named(steps.Wait(keeper.ChangeNow(change)));
        if (!IsOwn(record, stash))
        {
            var forgotten = Write(s => s with { ImportPending = null });
            return forgotten.Succeeded ? new Failure(action, Subject(stash), "its record does not name the folders of an import of this stash, so it is forgotten") : forgotten;
        }

        var extracted = Full(record.ExtractedFolder);
        var replaced = Full(record.ReplacedFolder);
        var marker = Path.Combine(stash, record.Marker);
        var hasExtracted = Directory.Exists(extracted);
        var hasStash = Directory.Exists(stash);
        var hasReplaced = Directory.Exists(replaced);
        if (!hasExtracted && !hasStash && !hasReplaced)
        {
            return new Failure(action, Subject(stash), "neither the stash folder nor the folder being imported into it is there");
        }

        if (!hasExtracted && hasStash && !File.Exists(marker))
        {
            // The extracted folder went before it was renamed in: the stash is the one the import began from.
            return Write(s => s with { ImportPending = null });
        }

        if (!hasExtracted && !hasStash)
        {
            // Renamed away, then the extracted folder lost: the import undone, nothing of the stash lost.
            var back = steps.Folder(action, replaced, () => Directory.Move(replaced, stash));
            return back.Succeeded ? Write(s => s with { ImportPending = null }) : back;
        }

        progress?.Report(new TransferProgress(TransferPhase.Replacing, 0, 3));
        if (hasExtracted)
        {
            if (hasStash && hasReplaced)
            {
                return new Failure(action, Subject(stash), "the stash folder, the folder being imported into it and the replaced one are all there");
            }

            if (hasStash)
            {
                var away = steps.Folder(action, stash, () => Directory.Move(stash, replaced));
                if (!away.Succeeded)
                {
                    return away;
                }
            }

            progress?.Report(new TransferProgress(TransferPhase.Replacing, 1, 3));
            var into = steps.Folder(action, extracted, () => Directory.Move(extracted, stash));
            if (!into.Succeeded)
            {
                return into;
            }
        }

        progress?.Report(new TransferProgress(TransferPhase.Replacing, 2, 3));
        if (Directory.Exists(replaced))
        {
            var recycled = Named(steps.Recycle(replaced));
            // A folder the bin cannot take is removed for good instead, answered done with no identity (G29); one gone
            // all the same after a refusal is done too. Nothing of it is recorded: no Undo comes for a replaced stash.
            if (!recycled.Succeeded && Directory.Exists(replaced))
            {
                return recycled;
            }
        }

        progress?.Report(new TransferProgress(TransferPhase.Finishing, 0, 1));
        // One write: the travelling values and no record. Refused, the record stays in Current as on disk (ChangeNow).
        var written = Write(s => record.Settings.AppliedTo(s) with { ImportPending = null });
        if (!written.Succeeded)
        {
            return written;
        }

        // After the write, never before: a marker gone with the record still standing reads as an abandoned import. One
        // still refused stays in the root, which nothing takes for an item, and the next start deletes it.
        if (File.Exists(marker))
        {
            steps.File(action, marker, () => File.Delete(marker));
        }

        progress?.Report(new TransferProgress(TransferPhase.Finishing, 1, 1));
        return Outcome.Done;
    }

    /// <summary>
    /// With no import recorded: every extracted folder a kill before its record left beside the stash deleted, and every
    /// marker a kill after the last write left in the stash's root; the failures named, each left for the next start.
    /// </summary>
    public static List<Failure> Sweep(string action, string stash, Steps steps)
    {
        var failures = new List<Failure>();
        var parent = Path.GetDirectoryName(stash);
        try
        {
            var orphans = parent is null ? [] : Directory.EnumerateDirectories(parent, Path.GetFileName(stash) + ImportingInfix + "*")
                .Where(folder => IsId(Path.GetFileName(folder)[(Path.GetFileName(stash).Length + ImportingInfix.Length)..])).ToList();
            var markers = Directory.Exists(stash) ? Directory.EnumerateFiles(stash, MarkerPrefix + "*").Where(IsMarker).ToList() : [];
            foreach (var orphan in orphans)
            {
                AddFailure(steps.Folder(action, orphan, () => Directory.Delete(orphan, recursive: true)));
            }

            foreach (var left in markers)
            {
                AddFailure(steps.File(action, left, () => File.Delete(left)));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failures.Add(new Failure(action, Subject(stash), HeldFileRetry.ReasonFor(e)));
        }

        return failures;

        void AddFailure(Outcome outcome)
        {
            if (!outcome.Succeeded)
            {
                failures.Add(outcome.Failure);
            }
        }
    }

    // Whether the record names this stash's own import: both folders beside it under its name, and a marker's plain name,
    // so no hand-edited record renames or recycles a folder of anyone else's.
    private static bool IsOwn(ImportPending record, string stash)
    {
        // A record a hand left without a value reads with it null.
        if (record.ExtractedFolder is null || record.ReplacedFolder is null || record.Marker is null || record.Settings?.Shortcuts is null)
        {
            return false;
        }

        try
        {
            var parent = Path.GetDirectoryName(stash);
            bool Beside(string folder, string infix) =>
                parent is not null && Same(Path.GetDirectoryName(Full(folder)) ?? "", parent)
                && Path.GetFileName(Full(folder)).StartsWith(Path.GetFileName(stash) + infix, StringComparison.OrdinalIgnoreCase);
            return Beside(record.ExtractedFolder, ImportingInfix) && Beside(record.ReplacedFolder, ReplacedInfix)
                && record.Marker.StartsWith(MarkerPrefix, StringComparison.Ordinal) && Path.GetFileName(record.Marker) == record.Marker;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsId(string text) => text.Length == IdLength && text.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    // A file another program holding open inside a folder makes Windows refuse the folder's rename as access denied.
    private static bool MayBeHeld(Exception e) =>
        e is UnauthorizedAccessException || (e is IOException io && (io.HResult & 0xFFFF) is 5 or 32 or 33);
}
