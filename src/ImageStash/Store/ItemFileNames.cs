using System.Text;

namespace ImageStash.Store;

/// <summary>
/// P1 and State: File names. An item's finished picture is named after the item, made safe for a Windows file name,
/// with " 2", " 3" … while the name is taken, compared as Windows compares file names, ignoring case.
/// </summary>
internal static class ItemFileNames
{
    /// <summary>The file name an item gets when its name leaves nothing once made safe.</summary>
    public const string Unnamed = "Untitled";

    // Leaves room under NTFS's 255-character component for the counter and the extension.
    private const int MaxStemLength = 200;

    private const string Forbidden = "<>:\"/\\|?*";

    private static readonly HashSet<string> DeviceNames = new(
        ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
         .. Enumerable.Range(0, 10).SelectMany(n => new[] { $"COM{n}", $"LPT{n}" }),
         "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The file name in the root for a new item named <paramref name="name"/> with <paramref name="extension"/>, not among
    /// <paramref name="taken"/>, which holds the names on disk and those every row reserves, nor in hand.
    /// </summary>
    public static string Choose(StoreContext store, string name, string extension, IReadOnlySet<string> taken) =>
        ChooseStem(store, null, name, [(extension, taken)]) + extension;

    /// <summary>
    /// <see cref="ChooseStem(string, IReadOnlyList{ValueTuple{string, string, IReadOnlySet{string}}})"/> for the row
    /// <paramref name="own"/> describes (none for a new item), the first of <paramref name="places"/> being the root and
    /// the second, when there is one, <c>originals\</c>; in each, a name another row's copy, paste or drag-out holds in
    /// hand there is taken too, and the file names chosen are recorded as chosen now (<see cref="NamesInHand"/>). Every
    /// file name the store gives an item in the root or <c>originals\</c> comes from here.
    /// </summary>
    public static string ChooseStem(StoreContext store, StoredFiles? own, string name, IReadOnlyList<(string Extension, IReadOnlySet<string> Taken)> places)
    {
        string[] folders = [store.Folder.Root, store.Folder.Originals];
        IReadOnlyList<string> ownFiles = own is null ? []
            : own.OriginalInOriginals ? [store.Folder.InRoot(own.FinishedFile), store.Folder.InOriginals(own.OriginalFile)]
            : [store.Folder.InRoot(own.FinishedFile)];
        return store.InHand.Choose(own?.Id, ownFiles, store.Commits, inHand =>
        {
            var inFolders = places.Select((place, i) => (place.Extension, Folder: folders[i], (IReadOnlySet<string>)NewSet(place.Taken.Concat(NamesInHand.In(inHand, folders[i]))))).ToList();
            var stem = ChooseStem(name, inFolders);
            return (stem, [.. inFolders.Select(place => Path.Combine(place.Folder, stem + place.Extension))]);
        });
    }

    /// <summary>
    /// The stem, with its counter, that names an item called <paramref name="name"/> in every one of
    /// <paramref name="places"/> at once: an edited item's finished picture and its original share it (State: File names).
    /// </summary>
    private static string ChooseStem(string name, IReadOnlyList<(string Extension, string Folder, IReadOnlySet<string> Taken)> places)
    {
        var stem = SafeStem(name);
        var candidate = stem;
        for (var counter = 2; places.Any(place => IsTaken(place.Folder, candidate + place.Extension, place.Taken)); counter++)
        {
            candidate = $"{stem} {counter}";
        }

        return candidate;
    }

    /// <summary>
    /// Whether <paramref name="fileName"/> cannot be given in <paramref name="folder"/>: a name on disk, reserved or in
    /// hand there (<paramref name="taken"/>), or one Windows answers, or may come to answer, as another file. Windows
    /// answers a file's short (8.3) name, LONGRE~1.PNG for "long reaction picture.png", as that file, and gives a file
    /// such a name when it gets its own, so a step waiting to bring a file under such a name could replace, move or
    /// delete another file in its place (G28, G29). On NTFS every name Windows makes so is an 8.3 name whose stem ends
    /// in a tilde and a number, which is taken by its shape, a counter's space keeping the next candidate out of it; a
    /// short name of another shape is caught when the disk answers it.
    /// </summary>
    private static bool IsTaken(string folder, string fileName, IReadOnlySet<string> taken)
    {
        if (taken.Contains(fileName) || MayBeAShortName(fileName))
        {
            return true;
        }

        var path = Path.Combine(folder, fileName);
        return (File.Exists(path) || Directory.Exists(path)) && PendingStep.NameOnDisk(folder, fileName) is null;
    }

    private static bool MayBeAShortName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var tilde = stem.LastIndexOf('~');
        return tilde >= 0 && tilde < stem.Length - 1 && stem[(tilde + 1)..].All(char.IsAsciiDigit)
            && stem.Length <= 8 && !stem.Contains('.') && !stem.Contains(' ') && Path.GetExtension(fileName).Length <= 4;
    }

    /// <summary>
    /// The names a file in the root must avoid: every entry in the root, and every row's finished picture, deleted-marked
    /// rows included, with the names a pending step there moves a file from or to (a rename's, a Save's, a Revert's).
    /// The row <paramref name="own"/> describes takes nothing from its own steps: neither its names nor the files it has
    /// on disk.
    /// </summary>
    public static HashSet<string> TakenInRoot(StoreContext store, StoredFiles? own = null) =>
        Taken(store, store.Folder.Root, own?.Id, own is null ? null : own.Files(store.Folder)[0].Path,
            "SELECT id, finished_file, pending_steps FROM items");

    /// <summary>
    /// The names a file in <c>originals\</c> must avoid, as <see cref="TakenInRoot"/> has them for the root, and every
    /// previous original an Open with… adoption keeps there for its Undo, the row's own too, so no Save moves a new
    /// original over it (G29).
    /// </summary>
    public static HashSet<string> TakenInOriginals(StoreContext store, StoredFiles? own = null)
    {
        var taken = Taken(store, store.Folder.Originals, own?.Id, own is { OriginalInOriginals: true } ? own.OriginalPath(store.Folder) : null,
            // A row whose Revert is pending says its original is in the root while it is still here, under its step's name.
            "SELECT id, CASE WHEN original_in_originals = 1 THEN original_file END, pending_steps FROM items WHERE original_in_originals = 1 OR pending_steps IS NOT NULL");
        taken.UnionWith(AdoptionUndo.KeptInOriginals(store));
        return taken;
    }

    private static HashSet<string> Taken(StoreContext store, string folder, long? ownId, string? ownOnDisk, string rows)
    {
        var taken = NewSet(Directory.Exists(folder) ? new DirectoryInfo(folder).EnumerateFileSystemInfos().Select(e => e.Name) : []);
        if (ownOnDisk is not null)
        {
            taken.Remove(Path.GetFileName(ownOnDisk));
        }

        foreach (var (id, fileName, steps) in store.Query(rows, r => (r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2))))
        {
            if (id == ownId)
            {
                continue;
            }

            if (fileName is not null)
            {
                taken.Add(fileName);
            }

            if (steps is not null)
            {
                taken.UnionWith(PendingStep.Decode(steps).SelectMany(step => step.NamesIn(store.Folder, folder)));
            }
        }

        return taken;
    }

    /// <summary>
    /// <paramref name="name"/> with every character Windows forbids in a file name replaced by an underscore, cut to
    /// a length that always fits, and kept clear of the device names Windows reserves (NUL.tar.gz is NUL).
    /// </summary>
    public static string SafeStem(string name)
    {
        var safe = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            safe.Append(c < ' ' || Forbidden.Contains(c) ? '_' : c);
        }

        var stem = safe.ToString();
        if (stem.Length > MaxStemLength)
        {
            stem = stem[..(char.IsLowSurrogate(stem[MaxStemLength]) ? MaxStemLength - 1 : MaxStemLength)];
        }

        if (stem.Trim().Trim('.').Length == 0)
        {
            return Unnamed;
        }

        var firstDot = stem.IndexOf('.');
        var device = firstDot < 0 ? stem : stem[..firstDot];
        return DeviceNames.Contains(device.TrimEnd(' ')) ? device + "_" + stem[device.Length..] : stem;
    }

    /// <summary>A set that compares file names as Windows does.</summary>
    public static HashSet<string> NewSet(IEnumerable<string> names) => new(names, StringComparer.OrdinalIgnoreCase);
}
