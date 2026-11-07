namespace ImageStash.Seams;

/// <summary>The system's recycle bin, where every file ImageStash removes goes (G29), and the stash folder an import replaces.</summary>
public interface IRecycleBin
{
    /// <summary>
    /// Moves each file, or each folder with everything in it as one entry, to the recycle bin and returns one outcome
    /// per path, in order, so one that cannot be recycled fails alone; a file held by another program, or a folder holding
    /// one, is retried for <paramref name="heldFileRetry"/> first and then answered as held. One the system removes for
    /// good instead, because the bin cannot take it (turned off for that drive, none there, or the file over its limit),
    /// is done, with no recycled identity (G29); one left where it was is a failure.
    /// </summary>
    IReadOnlyList<Outcome<RecycledFile>> Recycle(IReadOnlyList<string> paths, TimeSpan heldFileRetry);

    /// <summary>
    /// Removes each entry from the recycle bin for good, by the identity <see cref="Recycle"/> gave back for it, and
    /// returns one outcome per identity, in order, each failing alone: one naming no entry fails naming it, one this bin
    /// did not give back is refused and nothing is touched, and an entry held by another program is retried for
    /// <paramref name="heldFileRetry"/> first. It never recycles and shows nothing, and is never asked for a null identity.
    /// </summary>
    IReadOnlyList<Outcome> Remove(IReadOnlyList<string> recycledIdentities, TimeSpan heldFileRetry);
}

/// <summary>
/// A file or folder gone from where it was: that path, and the identity the recycle bin gave it, or null when the system
/// removed it for good instead, which leaves nothing to remove (G29).
/// </summary>
public sealed record RecycledFile(string Path, string? RecycledIdentity);
