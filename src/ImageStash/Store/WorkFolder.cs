using ImageStash.Files;

namespace ImageStash.Store;

/// <summary>
/// <c>.work\</c> inside the stash folder (State: Files): every file that will reach the root is written here first, at
/// a path the store chose, on the same volume, so the rename that publishes it is whole or absent (S1, R2).
/// </summary>
internal static class WorkFolder
{
    private const string ClearAction = "Clear the work folder";

    /// <summary>A fresh path in <c>.work\</c> carrying <paramref name="extension"/> (lowercased), for a caller to write into.</summary>
    public static Outcome<string> Allocate(StashFolder folder, string extension)
    {
        if (extension.Length is < 2 or > 16 || extension[0] != '.' || !extension[1..].All(char.IsAsciiLetterOrDigit))
        {
            return new Failure("Make room for a new file", extension, "it is not a file extension");
        }

        return Outcome<string>.Of(folder.InWork(Guid.NewGuid().ToString("N") + extension.ToLowerInvariant()));
    }

    /// <summary>
    /// Copies <paramref name="source"/> into <c>.work\</c>, never moving or changing it (G11, R3), and returns the
    /// copy's path. A failure leaves nothing in <c>.work\</c>.
    /// </summary>
    public static Outcome<string> Stage(StoreContext store, string source) => CopyIn(store, source, "Copy into the stash");

    /// <summary>
    /// Copies <paramref name="source"/> into <c>.work\</c> at a fresh path, leaving it as it was, and returns the copy's
    /// path; a failure names <paramref name="action"/> and the source and leaves nothing in <c>.work\</c>.
    /// </summary>
    public static Outcome<string> CopyIn(StoreContext store, string source, string action)
    {
        var path = Allocate(store.Folder, Path.GetExtension(source));
        if (!path.Succeeded)
        {
            return new Failure(action, Path.GetFileName(source), "its name does not end in a file extension");
        }

        var staged = path.Value!;
        var copied = store.RetryHeldFile(action, source, () => File.Copy(source, staged, overwrite: true));
        if (copied.Succeeded)
        {
            // A copy keeps the source's attributes, and a read-only one could not be discarded or cleared from .work\.
            var plain = store.RetryHeldFile(action, staged, () => File.SetAttributes(staged, FileAttributes.Normal));
            if (!plain.Succeeded)
            {
                copied = plain.Failure with { Subject = Path.GetFileName(source) };
            }
        }

        if (!copied.Succeeded)
        {
            // A copy that failed part way leaves a partial file; one that cannot be deleted goes at the next start.
            store.RetryHeldFile("Discard a partial copy", staged, () => File.Delete(staged));
            return copied.Failure;
        }

        return Outcome<string>.Of(staged);
    }

    /// <summary>Deletes a file the caller no longer needs from <c>.work\</c>; nothing outside it.</summary>
    public static Outcome Discard(StoreContext store, string path)
    {
        if (!store.Folder.IsWorkFile(path))
        {
            return new Failure("Discard a copy", Path.GetFileName(path), "it is not in the stash's work folder");
        }

        return store.RetryHeldFile("Discard a copy", path, () => File.Delete(path));
    }

    /// <summary>
    /// After every pending step is resolved, deletes what in <c>.work\</c> no row refers to (State: Files): a pending
    /// step's file, a marked row's held copies and the previous original an Open with… adoption holds for its Undo,
    /// marked row or not, are kept. What cannot be deleted is named and waits for the next start. While a row's record
    /// of an adoption cannot be read, nothing is deleted and that is named, so a held original it may name is not lost.
    /// </summary>
    public static Outcome<IReadOnlyList<Failure>> Clean(StoreContext store)
    {
        var referenced = store.Read(StoreOpen.Action, () => store.Query(
            "SELECT pending_steps, CASE WHEN deleted = 1 THEN held_copies END, adoption_undo, name, finished_file FROM items " +
            "WHERE pending_steps IS NOT NULL OR (deleted = 1 AND held_copies IS NOT NULL) OR adoption_undo IS NOT NULL",
            r =>
            {
                var undo = r.IsDBNull(2) ? null : AdoptionUndo.Decode(r.GetString(2));
                // Each step says whether its From is here: an import's and a render's are; a rename's or a move's never.
                List<string> names =
                [
                    .. r.IsDBNull(0) ? [] : PendingStep.Decode(r.GetString(0)).Where(s => s.NamesWorkFile).Select(s => s.From),
                    .. r.IsDBNull(1) ? [] : HeldCopy.Decode(r.GetString(1)).Select(c => c.Copy),
                    .. undo?.HeldCopy is { } held ? [held] : Array.Empty<string>(),
                ];
                return (Names: names, Unreadable: !r.IsDBNull(2) && undo is null ? ItemQueries.Named(r.GetString(3), r.GetString(4)) : null);
            }));
        if (!referenced.Succeeded)
        {
            return referenced.Failure;
        }

        if (referenced.Value!.FirstOrDefault(row => row.Unreadable is not null).Unreadable is { } damaged)
        {
            return Outcome<IReadOnlyList<Failure>>.Of([new Failure(ClearAction, damaged, AdoptionUndo.Unreadable)]);
        }

        var kept = ItemFileNames.NewSet(referenced.Value!.SelectMany(row => row.Names));
        var named = new List<Failure>();
        foreach (var entry in new DirectoryInfo(store.Folder.Work).EnumerateFileSystemInfos())
        {
            if (kept.Contains(entry.Name))
            {
                continue;
            }

            var deleted = store.RetryHeldFile(ClearAction, entry.FullName, () =>
            {
                if (entry is DirectoryInfo directory)
                {
                    directory.Delete(recursive: true);
                }
                else
                {
                    entry.Delete();
                }
            });
            if (!deleted.Succeeded)
            {
                named.Add(deleted.Failure);
            }
        }

        return Outcome<IReadOnlyList<Failure>>.Of(named);
    }
}
