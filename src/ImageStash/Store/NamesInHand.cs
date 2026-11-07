namespace ImageStash.Store;

/// <summary>
/// The files a copy, a paste or a drag-out is handing over, from the store's confirmation of its items (or the drag's
/// start) until it is done, and when each file in the root or <c>originals\</c> was last given its name. A file is
/// handed over where the item's picture is: in the root, or in <c>originals\</c> while a first Save waits to place its
/// render (State: A live failure). A name in hand is taken there (State: File names) for every row but the one holding
/// it, so no new or renamed item comes under it while the clipboard or a drop carries it, since a file name is never
/// identity (G8, G9). The store's thread chooses names and the UI thread starts drags, so every use is under one lock.
/// In memory and afresh at each open (R8).
/// </summary>
internal sealed class NamesInHand
{
    /// <summary>Why a drag-out refuses an item whose file name went to a file since the item was read.</summary>
    public const string ChangedSinceShown = "it changed in the stash since it was shown";

    private readonly object gate = new();
    private readonly List<(string Path, long Item)> held = [];
    private readonly Dictionary<string, long> chosenAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Holds the file of each item as handed over, until the returned hold is disposed: where its path is, which while a
    /// rename's step waits is the name it is moving from, not the one it is given.
    /// </summary>
    public IDisposable Hold(IEnumerable<StashItem> items)
    {
        lock (gate)
        {
            var entries = items.Select(item => (HandedOver(item), item.Id)).ToList();
            held.AddRange(entries);
            return new Release(this, entries);
        }
    }

    /// <summary>
    /// For items read earlier and not read again, as a drag-out has them: holds their files, unless one of those names
    /// has been chosen for a file since that item was read, so that it may now be another item's; that item is then
    /// refused, named as <paramref name="action"/> and nothing is held.
    /// </summary>
    public Outcome<IDisposable> HoldUnlessChosenSince(IReadOnlyList<StashItem> items, string action)
    {
        lock (gate)
        {
            if (items.FirstOrDefault(item => chosenAt.TryGetValue(HandedOver(item), out var at) && at >= item.CommitsWhenRead) is { } changed)
            {
                return new Failure(action, changed.Name.Length > 0 ? changed.Name : changed.FileName, ChangedSinceShown);
            }

            return Outcome<IDisposable>.Of(Hold(items));
        }
    }

    /// <summary>
    /// Runs <paramref name="choose"/>, the choice of file names, with the paths in hand of every row but
    /// <paramref name="own"/>, and records each path it answers as chosen at <paramref name="commits"/>, all under the
    /// lock, so a hold either is seen by this choice or sees it. A row choosing a name it has, one of
    /// <paramref name="ownFiles"/>, takes nothing new there, so nothing is recorded for it: were its change rolled back,
    /// the mark would refuse drags of the row as it still is.
    /// </summary>
    public string Choose(long? own, IReadOnlyList<string> ownFiles, long commits, Func<IReadOnlyList<string>, (string Chosen, IReadOnlyList<string> Paths)> choose)
    {
        lock (gate)
        {
            var (chosen, paths) = choose(held.Where(entry => entry.Item != own).Select(entry => entry.Path).ToList());
            var owned = ownFiles.Select(Full).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths.Select(Full).Where(path => !owned.Contains(path)))
            {
                chosenAt[path] = commits;
            }

            return chosen;
        }
    }

    /// <summary>The names of the paths in <paramref name="paths"/> that lie directly in <paramref name="folder"/>.</summary>
    public static IEnumerable<string> In(IEnumerable<string> paths, string folder)
    {
        var inside = Path.TrimEndingDirectorySeparator(Full(folder));
        return paths.Where(path => string.Equals(Path.GetDirectoryName(path), inside, StringComparison.OrdinalIgnoreCase)).Select(path => Path.GetFileName(path));
    }

    private static string HandedOver(StashItem item) => Full(item.FilePath);

    private static string Full(string path) => Path.GetFullPath(path);

    private sealed class Release(NamesInHand names, List<(string Path, long Item)> entries) : IDisposable
    {
        private bool released;

        public void Dispose()
        {
            lock (names.gate)
            {
                if (!released)
                {
                    released = true;
                    entries.ForEach(entry => names.held.Remove(entry));
                }
            }
        }
    }
}

/// <summary>
/// A handoff's items as the store confirmed them, one outcome per item handed in, in order; the file names of those it
/// answered stay in hand (<see cref="NamesInHand"/>) until this is disposed, once the handoff is done.
/// </summary>
public sealed class Confirmation(IReadOnlyList<Outcome<StashItem>> items, IDisposable held) : IDisposable
{
    public IReadOnlyList<Outcome<StashItem>> Items { get; } = items;

    public void Dispose() => held.Dispose();
}
