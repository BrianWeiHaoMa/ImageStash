namespace ImageStash.Store;

/// <summary>
/// The layout of the stash folder (F6, State): the store, every item's finished picture in the root, the original of
/// an edited item in <c>originals\</c>, and <c>.work\</c> for files in flight, which nothing treats as an item.
/// </summary>
public sealed record StashFolder(string Root)
{
    public const string StoreFileName = "stash.db";

    public string Originals => Path.Combine(Root, "originals");

    public string Work => Path.Combine(Root, ".work");

    public string StoreFile => Path.Combine(Root, StoreFileName);

    public string InRoot(string fileName) => Path.Combine(Root, fileName);

    public string InOriginals(string fileName) => Path.Combine(Originals, fileName);

    public string InWork(string fileName) => Path.Combine(Work, fileName);

    /// <summary>Whether <paramref name="path"/> names a file directly inside <c>.work\</c>.</summary>
    public bool IsWorkFile(string path)
    {
        var full = Path.GetFullPath(path);
        return string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(Work), StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(full).Length > 0;
    }

    /// <summary>Whether <paramref name="path"/> names a file directly inside the root or <c>originals\</c>, where every item's files lie (F6).</summary>
    public bool IsRootOrOriginalsFile(string? path)
    {
        if (path is null || Path.GetFileName(path).Length == 0)
        {
            return false;
        }

        var folder = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return string.Equals(folder, Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root)), StringComparison.OrdinalIgnoreCase)
            || string.Equals(folder, Path.TrimEndingDirectorySeparator(Path.GetFullPath(Originals)), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Creates the folder, <c>originals\</c> and <c>.work\</c> where they are missing.</summary>
    internal Outcome Create()
    {
        try
        {
            if (File.Exists(Root))
            {
                return new Failure(StoreOpen.Action, Root, "a file is where the stash folder should be");
            }

            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Originals);
            Directory.CreateDirectory(Work);
            return Outcome.Done;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new Failure(StoreOpen.Action, Root, Files.HeldFileRetry.ReasonFor(e));
        }
    }
}
