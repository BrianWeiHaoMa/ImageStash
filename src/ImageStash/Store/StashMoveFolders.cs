using ImageStash.Files;

namespace ImageStash.Store;

/// <summary>
/// The folders a Move copies into and removes (State: Move): paths compared as Windows compares names, ignoring case; a
/// folder found inside another under a spelling the comparison cannot see (a short name, a link), by a marker written in
/// one and looked for under the other; and a folder emptied, store file last, only while it holds a store at its root or
/// no file at all, so a field a hand edited never empties a folder that is not a stash and no removal takes the stash.
/// </summary>
internal static class MoveFolders
{
    private const string MarkerPrefix = ".imagestash-move-";

    /// <summary>One file step, retried while the file is held as the caller's thread retries (G35).</summary>
    public delegate Outcome FileStep(string action, string path, Action step);

    /// <summary>The full path, its trailing separator trimmed (a drive's root keeps it).</summary>
    public static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool Same(string a, string b) => string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);

    public static bool Inside(string path, string folder)
    {
        var within = Full(folder);
        within = Path.EndsInDirectorySeparator(within) ? within : within + Path.DirectorySeparatorChar;
        var full = Full(path);
        return full.Length > within.Length && full.StartsWith(within, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A folder is named by its own name, a drive's root by its whole path.</summary>
    public static string Subject(string folder)
    {
        try
        {
            var full = Full(folder);
            return Path.GetFileName(full) is { Length: > 0 } name ? name : full;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return folder;
        }
    }

    /// <summary>
    /// Where <paramref name="folder"/> lies at or under <paramref name="within"/>, spelled as <paramref name="within"/>
    /// spells it, or null when it lies elsewhere: a marker file written into the folder, looked for under the other, and
    /// deleted again through <paramref name="step"/>. A folder the marker cannot be written into, or a marker that cannot
    /// be deleted, fails, naming <paramref name="action"/>; a marker left behind is the emptying's (<see cref="Empty"/>).
    /// </summary>
    public static Outcome<string?> SpelledUnder(string action, string folder, string within, FileStep step)
    {
        if (!Directory.Exists(within))
        {
            return Outcome<string?>.Of(null);
        }

        var marker = Path.Combine(folder, MarkerPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(marker, []);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Failure(action, Subject(folder), HeldFileRetry.ReasonFor(e));
        }

        Outcome<string?> found;
        try
        {
            var file = Directory.EnumerateFiles(within, Path.GetFileName(marker), SearchOption.AllDirectories).FirstOrDefault();
            found = Outcome<string?>.Of(file is null ? null : Path.GetDirectoryName(file));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            found = new Failure(action, Subject(within), HeldFileRetry.ReasonFor(e));
        }

        // A scanner may hold the fresh marker for a moment (E3). One a kill leaves is in .work\, cleared at the next
        // start, or in a chosen folder the copy in progress names, which the next start empties.
        var deleted = step(action, marker, () => File.Delete(marker));
        return deleted.Succeeded ? found : deleted.Failure;
    }

    /// <summary>A marker <see cref="SpelledUnder"/> wrote, which a kill or a held file may have left behind.</summary>
    public static bool IsMarker(string file) => Path.GetFileName(file).StartsWith(MarkerPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Empties <paramref name="folder"/>, which a Move copied into (<paramref name="partialCopy"/>: the folder itself kept,
    /// as the person chose it) or moved away from (the folder removed too, but <paramref name="stash"/> and the folders on
    /// the way to it when the stash lies inside, under any spelling): every file but the store file, then the store file,
    /// then the folders deepest first, so a folder still holding the store file is always one to finish. Done when there
    /// is nothing to remove; refused, with nothing touched, when the folder is the stash, holds it as a partial copy, lies
    /// inside it, or holds files but no store.
    /// </summary>
    public static Outcome Empty(string action, string folder, string stash, bool partialCopy, FileStep step, IProgress<MoveProgress>? progress)
    {
        var subject = Subject(folder);
        if (!Directory.Exists(folder))
        {
            return Outcome.Done;
        }

        var hidden = Directory.Exists(stash) ? StashUnder(action, folder, stash, step) : Outcome<string?>.Of(null);
        if (!hidden.Succeeded)
        {
            return hidden.Failure;
        }

        var keep = hidden.Value ?? (Inside(stash, folder) ? stash : null);
        if (Same(folder, stash) || (keep is not null && Same(keep, folder)))
        {
            return new Failure(action, subject, MoveReasons.TheStashItself);
        }

        if (keep is not null && (partialCopy || !Inside(keep, folder)))
        {
            // A partial copy never holds the stash it was copied from; the stash found beside the folder holds the folder.
            return new Failure(action, subject, partialCopy && Inside(keep, folder) ? MoveReasons.HoldsTheStash : MoveReasons.InsideTheStash);
        }

        // A partial copy may lie inside the stash it was copied from; a folder moved away from never does.
        if (!partialCopy && Inside(folder, stash))
        {
            return new Failure(action, subject, MoveReasons.InsideTheStash);
        }

        List<string> files;
        List<string> folders;
        try
        {
            files = [.. FilesIn(folder, keep).OrderBy(file => IsStoreFile(folder, file) ? 1 : 0)];
            folders = [.. FoldersIn(folder, keep).Where(inner => keep is null || !Inside(keep, inner)).OrderByDescending(inner => inner.Length)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Failure(action, subject, HeldFileRetry.ReasonFor(e));
        }

        // A marker left behind makes no folder a stash, nor keeps one from being emptied.
        if (files.Any(file => !IsMarker(file)) && !File.Exists(Path.Combine(folder, StashFolder.StoreFileName)))
        {
            return new Failure(action, subject, MoveReasons.NotAStash);
        }

        progress?.Report(new MoveProgress(MovePhase.Removing, 0, files.Count));
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var deleted = step(action, file, () =>
            {
                // A file a sync client marked read-only would otherwise refuse its deletion.
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            });
            if (!deleted.Succeeded)
            {
                return deleted;
            }

            progress?.Report(new MoveProgress(MovePhase.Removing, i + 1, files.Count));
        }

        foreach (var inner in partialCopy || keep is not null ? folders : folders.Append(folder))
        {
            var removed = step(action, inner, () => Directory.Delete(inner));
            if (!removed.Succeeded)
            {
                return removed;
            }
        }

        return Outcome.Done;
    }

    /// <summary>Every file under the folder at any depth, none inside <paramref name="skip"/>.</summary>
    public static IEnumerable<string> FilesIn(string folder, string? skip) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(file => skip is null || !Inside(file, skip));

    /// <summary>Every folder under the folder at any depth, <paramref name="skip"/> and what it holds left out.</summary>
    public static IEnumerable<string> FoldersIn(string folder, string? skip) =>
        Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).Where(inner => skip is null || !(Same(inner, skip) || Inside(inner, skip)));

    public static bool IsStoreFile(string folder, string file) => Same(file, Path.Combine(folder, StashFolder.StoreFileName));

    // The stash's root as the folder spells it, when the stash lies at or under the folder, or the folder at or under the
    // stash's .work\ (where the marker goes, the stash's own): the marker's folder is .work\, its parent the stash.
    private static Outcome<string?> StashUnder(string action, string folder, string stash, FileStep step)
    {
        var work = new StashFolder(stash).Work;
        try
        {
            Directory.CreateDirectory(work);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Failure(action, Subject(stash), HeldFileRetry.ReasonFor(e));
        }

        var found = SpelledUnder(action, work, folder, step);
        return found.Succeeded && found.Value is { } spelledWork ? Outcome<string?>.Of(Path.GetDirectoryName(spelledWork)) : found;
    }
}
