using ImageStash.Files;

namespace ImageStash.Store;

/// <summary>
/// The folder Store keeps the Open with… copies in, outside the stash folder so nothing but ImageStash writes inside the
/// stash (Composition: Store, E2): one folder per session, and a marker file recreated at every open, so the folder is
/// never empty and no Move can choose it (G30). Nothing in it is needed to open a stash (R8): a missing root is made
/// again, and a session folder no row records is deleted at the next open.
/// </summary>
internal static class OpenWithRoot
{
    private const string FolderName = "open-with";

    public const string MarkerName = "README.txt";

    private const string MarkerText =
        "ImageStash keeps here the copies of pictures it opens in other programs with Open with…, one folder for each. "
        + "It makes this file again at every start, so this folder is never empty and is never taken for a stash folder.\r\n";

    public const string PrepareAction = "Prepare the Open with… folder";

    private const string SweepAction = "Clear the Open with… folder";

    /// <summary>The root under the folder that holds the settings file (F6).</summary>
    public static string Under(string settingsFolder) => Path.Combine(settingsFolder, FolderName);

    /// <summary>Creates the root and its marker where they are missing; a failure names the root and why.</summary>
    public static Outcome Prepare(string root)
    {
        try
        {
            if (File.Exists(root))
            {
                return new Failure(PrepareAction, root, "a file is where the folder should be");
            }

            Directory.CreateDirectory(root);
            var marker = Path.Combine(root, MarkerName);
            if (!File.Exists(marker))
            {
                File.WriteAllText(marker, MarkerText);
            }

            return Outcome.Done;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new Failure(PrepareAction, root, HeldFileRetry.ReasonFor(e));
        }
    }

    /// <summary>A new folder inside the root for one session, under a name never used before, which it answers.</summary>
    public static Outcome<string> MakeSessionFolder(string root, string action, string subject)
    {
        var folder = Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, folder));
            return Outcome<string>.Of(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Failure(action, subject, HeldFileRetry.ReasonFor(e));
        }
    }

    /// <summary>
    /// Deletes the copy of a session that has ended, then its folder when nothing else is in it (State: Open with…), each
    /// retried while held (<see cref="StoreContext.RetryHeldFile{T}"/>); a copy or folder already gone is nothing.
    /// </summary>
    public static Outcome DeleteCopy(StoreContext store, string root, string folder, string file, string action)
    {
        var copy = Path.Combine(root, folder, file);
        var deleted = store.RetryHeldFile(action, copy, () =>
        {
            if (File.Exists(copy))
            {
                File.Delete(copy);
            }
        });
        if (!deleted.Succeeded)
        {
            return deleted;
        }

        var session = Path.Combine(root, folder);
        return store.RetryHeldFile(action, session, () =>
        {
            if (Directory.Exists(session) && !Directory.EnumerateFileSystemEntries(session).Any())
            {
                Directory.Delete(session);
            }
        });
    }

    /// <summary>
    /// Deletes a session folder no row records, whatever it holds, in one attempt: one that cannot go now is left for the
    /// next open's clearing, which no failure of the caller's waits on.
    /// </summary>
    public static void DeleteUnrecorded(StoreContext store, string root, string folder) =>
        store.TryHeldFileOnce(SweepAction, Path.Combine(root, folder), () => Directory.Delete(Path.Combine(root, folder), recursive: true));

    /// <summary>
    /// At start, deletes every folder directly under the root that no row's session names (one a kill left between making
    /// a session's folder and recording it, or after recording a fresh one), keeping the marker and any other file; what
    /// cannot be deleted is named and waits for the next start (R8).
    /// </summary>
    public static IReadOnlyList<Failure> Sweep(StoreContext store, string root, IReadOnlyCollection<string> recorded)
    {
        var kept = new HashSet<string>(recorded, StringComparer.OrdinalIgnoreCase);
        var named = new List<Failure>();
        try
        {
            foreach (var folder in new DirectoryInfo(root).EnumerateDirectories())
            {
                if (kept.Contains(folder.Name))
                {
                    continue;
                }

                var deleted = store.RetryHeldFile(SweepAction, folder.FullName, () => folder.Delete(recursive: true));
                if (!deleted.Succeeded)
                {
                    named.Add(deleted.Failure);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            named.Add(new Failure(SweepAction, root, HeldFileRetry.ReasonFor(e)));
        }

        return named;
    }
}
