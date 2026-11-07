using System.Text.Json;
using System.Text.Json.Serialization;
using ImageStash.Media;

namespace ImageStash.Store;

/// <summary>
/// A file step a change committed on its row and has not yet completed (State: Every change). The row keeps its
/// steps as a JSON list until the change clears them; the next start finishes or undoes whatever is left. A rename
/// step also keeps the item's name before the rename, the fact its undo rolls the row back to; a Save's place-render
/// step keeps the facts of the render it places, which the listed item describes once that render is on disk; an Open
/// with… adoption's place step keeps the adopted file's facts and its SHA-256, as upper-case hex, and its Undo's steps
/// keep the previous original's.
/// </summary>
internal sealed record PendingStep(string Step, string From, string To, string? PreviousName = null, MediaFacts? Facts = null, string? Sha256 = null)
{
    /// <summary>Rename the file <see cref="From"/> in <c>.work\</c> into the root as <see cref="To"/>.</summary>
    public const string PlaceInRoot = "place in root";

    /// <summary>Rename the finished picture <see cref="From"/> in the root to <see cref="To"/>, after a Rename.</summary>
    public const string RenameInRoot = "rename in root";

    /// <summary>Rename an edited item's original <see cref="From"/> in <c>originals\</c> to <see cref="To"/>, after a Rename.</summary>
    public const string RenameInOriginals = "rename in originals";

    /// <summary>Move an item's one file <see cref="From"/> in the root into <c>originals\</c> as <see cref="To"/>, at its first Save.</summary>
    public const string MoveToOriginals = "move to originals";

    /// <summary>
    /// Rename a Save's render <see cref="From"/> in <c>.work\</c> into the root as <see cref="To"/>, replacing an earlier
    /// render; <see cref="Facts"/> are the render's. Its own kind, so a second Save's one step is never taken for an import's.
    /// </summary>
    public const string PlaceRender = "place render";

    /// <summary>Move an edited item's original <see cref="From"/> in <c>originals\</c> back into the root as <see cref="To"/>, at a Revert.</summary>
    public const string MoveToRoot = "move to root";

    /// <summary>Delete the render <see cref="From"/> in the root, never recycled (G29), at a Revert; <see cref="To"/> repeats the name.</summary>
    public const string DeleteInRoot = "delete in root";

    /// <summary>
    /// Rename a Save as new item's copy of the original <see cref="From"/> in <c>.work\</c> into <c>originals\</c> as
    /// <see cref="To"/>, before its render's <see cref="PlaceInRoot"/>; the row is listed once both are done.
    /// </summary>
    public const string PlaceInOriginals = "place in originals";

    /// <summary>
    /// Move an unedited item's one file <see cref="From"/> in the root into <c>.work\</c> as <see cref="To"/>, the
    /// previous original an Open with… adoption holds for its Undo, before the adopted file is placed (G29).
    /// </summary>
    public const string HoldInWork = "hold in work";

    /// <summary>
    /// Delete an edited item's render <see cref="From"/> in the root, never recycled (G29), before an Open with…
    /// adoption places the adopted file; <see cref="To"/> names that adopted file in <c>.work\</c>.
    /// </summary>
    public const string RemoveRender = "remove render";

    /// <summary>
    /// Rename an Open with… adoption's file <see cref="From"/> in <c>.work\</c> into the root as <see cref="To"/>;
    /// <see cref="Facts"/> and <see cref="Sha256"/> are the adopted file's. Its own kind, so a row waiting on it is
    /// never finished as an import.
    /// </summary>
    public const string PlaceAdopted = "place adopted";

    /// <summary>
    /// Move the picture an Open with… adoption placed, <see cref="From"/> in the root, into <c>.work\</c> as
    /// <see cref="To"/>, first of the adoption's Undo, before the previous picture comes back; present from that Undo's
    /// commit to its last transaction.
    /// </summary>
    public const string SetAsideAdopted = "set aside adopted";

    /// <summary>
    /// Rename the previous original an Open with… adoption held, <see cref="From"/> in <c>.work\</c>, back into the root
    /// as <see cref="To"/>, at the adoption's Undo; <see cref="Facts"/> and <see cref="Sha256"/> are that original's.
    /// </summary>
    public const string RestoreHeld = "restore held";

    /// <summary>
    /// Send the set-aside adopted picture <see cref="From"/> in <c>.work\</c> to the Recycle Bin (G29), last of the
    /// adoption's Undo, once the previous picture is back; <see cref="To"/> repeats the name.
    /// </summary>
    public const string RecycleSetAside = "recycle set aside";

    /// <summary>Every kind above, the steps a build writes: a new kind joins here, or an import refuses a store that records it.</summary>
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>
    {
        PlaceInRoot, RenameInRoot, RenameInOriginals, MoveToOriginals, PlaceRender, MoveToRoot, DeleteInRoot, PlaceInOriginals, HoldInWork, RemoveRender,
        PlaceAdopted, SetAsideAdopted, RestoreHeld, RecycleSetAside,
    };

    // A member left out when null keeps an import's step as earlier builds wrote it.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static PendingStep Place(string workFileName, string rootFileName) => new(PlaceInRoot, workFileName, rootFileName);

    [JsonIgnore]
    public bool IsRename => Step is RenameInRoot or RenameInOriginals;

    /// <summary>A step of a Save: the original's move into <c>originals\</c>, or the render's place.</summary>
    [JsonIgnore]
    public bool IsSave => Step is MoveToOriginals or PlaceRender;

    /// <summary>A step of a Revert to original: the original's move back into the root, or the render's delete.</summary>
    [JsonIgnore]
    public bool IsRevert => Step is MoveToRoot or DeleteInRoot;

    /// <summary>
    /// Whether a row waiting on this step is in the stash, done or not: a rename's, a Save's and a Revert's leave the
    /// picture on disk whole, and the row says where it is (State: A live failure).
    /// </summary>
    [JsonIgnore]
    public bool ListsWhilePending => IsRename || IsSave || IsRevert;

    /// <summary>
    /// Whether <see cref="From"/> may be a file in <c>.work\</c>, which the folder's clearing keeps while the step waits:
    /// an import's, a render's and a copy's are; a rename's, a move's and a delete's never; a kind this build does not
    /// write is kept, as its row is.
    /// </summary>
    [JsonIgnore]
    public bool NamesWorkFile => Step is not (RenameInRoot or RenameInOriginals or MoveToOriginals or MoveToRoot or DeleteInRoot or HoldInWork or RemoveRender or SetAsideAdopted);

    /// <summary>
    /// Whether the step is done on disk though not yet cleared, judged in the folder it writes into: an import's file
    /// has left <c>.work\</c> and is in the root, a Save as new item's copy in <c>originals\</c>; a rename's file carries
    /// <see cref="To"/>, in that case, and nothing is left under <see cref="From"/>. A move into <c>originals\</c> is
    /// done once its file is there, whatever the root holds under that name again; a render once it has left
    /// <c>.work\</c>; a move back once nothing is left in <c>originals\</c>; a delete once the root has no file of that
    /// name. An adoption's hold once its file is in <c>.work\</c>; its render's removal once the root has no file of that
    /// name or the adopted file has left <c>.work\</c>, since the adopted file may land under the render's own name; its
    /// place once the adopted file has left <c>.work\</c>. An Undo's restore once the held original has left
    /// <c>.work\</c>; its set aside and its recycle are judged with their row
    /// (<see cref="IsDone(StashFolder, IReadOnlyList{PendingStep})"/>), and alone a set aside is done while its file is in
    /// <c>.work\</c> and a recycle never is.
    /// </summary>
    public bool IsDone(StashFolder folder) => Step switch
    {
        PlaceInRoot => !File.Exists(folder.InWork(From)) && File.Exists(folder.InRoot(To)),
        PlaceInOriginals => !File.Exists(folder.InWork(From)) && File.Exists(folder.InOriginals(To)),
        RenameInRoot or RenameInOriginals => NameOnDisk(ToFolder(folder), To) == To
            && (string.Equals(From, To, StringComparison.OrdinalIgnoreCase) || !File.Exists(FromPath(folder))),
        MoveToOriginals => File.Exists(folder.InOriginals(To)),
        PlaceRender => !File.Exists(folder.InWork(From)),
        MoveToRoot => !File.Exists(folder.InOriginals(From)),
        DeleteInRoot => !File.Exists(folder.InRoot(From)),
        HoldInWork => File.Exists(folder.InWork(To)),
        RemoveRender => !File.Exists(folder.InRoot(From)) || !File.Exists(folder.InWork(To)),
        PlaceAdopted => !File.Exists(folder.InWork(From)),
        RestoreHeld => !File.Exists(folder.InWork(From)),
        SetAsideAdopted => File.Exists(folder.InWork(To)),
        _ => false,
    };

    /// <summary>
    /// <see cref="IsDone(StashFolder)"/>, judged with the steps of the row it is one of (<paramref name="row"/>), which an
    /// adoption's Undo needs: its set aside is done once its file is in <c>.work\</c>, or once the previous picture has
    /// landed in the root, since the recycle then takes that file away; its recycle once the previous picture has landed
    /// and the set-aside file has left <c>.work\</c>, recycled or, where the shell deleted it for good, gone. Every
    /// other kind as alone.
    /// </summary>
    public bool IsDone(StashFolder folder, IReadOnlyList<PendingStep> row) => Step switch
    {
        SetAsideAdopted => IsDone(folder) || Restoring(row)?.IsDone(folder) == true,
        RecycleSetAside => !File.Exists(folder.InWork(From)) && Restoring(row)?.IsDone(folder) == true,
        _ => IsDone(folder),
    };

    /// <summary>The step of an Undo's row that brings the previous picture back into the root: the held original's restore, or the fresh render's place.</summary>
    public static PendingStep? Restoring(IReadOnlyList<PendingStep> row) =>
        row.Any(step => step.Step == SetAsideAdopted) ? row.FirstOrDefault(step => step.Step is RestoreHeld or PlaceRender) : null;

    /// <summary>
    /// Where a rename's file is now: under <see cref="To"/> once a file of that name exists, else under
    /// <see cref="From"/> while it is there, else under <see cref="To"/>.
    /// </summary>
    public string Located(StashFolder folder) =>
        File.Exists(ToPath(folder)) || !File.Exists(FromPath(folder)) ? ToPath(folder) : FromPath(folder);

    public string FromPath(StashFolder folder) => Path.Combine(FromFolder(folder), From);

    public string ToPath(StashFolder folder) => Path.Combine(ToFolder(folder), To);

    /// <summary>The names this step needs in <paramref name="place"/> (the root or <c>originals\</c>), which no other file may take while it waits.</summary>
    public IEnumerable<string> NamesIn(StashFolder folder, string place)
    {
        if (FromFolder(folder) == place)
        {
            yield return From;
        }

        if (ToFolder(folder) == place)
        {
            yield return To;
        }
    }

    public static string Encode(IReadOnlyList<PendingStep> steps) => JsonSerializer.Serialize(steps, Json);

    public static IReadOnlyList<PendingStep> Decode(string json) => JsonSerializer.Deserialize<List<PendingStep>>(json, Json) ?? [];

    private string FromFolder(StashFolder folder) => Step switch
    {
        PlaceInRoot or PlaceRender or PlaceInOriginals or PlaceAdopted or RestoreHeld or RecycleSetAside => folder.Work,
        RenameInOriginals or MoveToRoot => folder.Originals,
        _ => folder.Root,
    };

    private string ToFolder(StashFolder folder) => Step switch
    {
        RenameInOriginals or MoveToOriginals or PlaceInOriginals => folder.Originals,
        HoldInWork or RemoveRender or SetAsideAdopted or RecycleSetAside => folder.Work,
        _ => folder.Root,
    };

    // The name a file has on disk, as Windows spells it, so a rename that changes only the case is told done or not, and
    // a short name told from the name it stands for. Read without a search pattern, whose wildcards a name may hold; only
    // a start, a Rename, or a name some file already answers asks it.
    internal static string? NameOnDisk(string folder, string name) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).Select(Path.GetFileName).FirstOrDefault(found => string.Equals(found, name, StringComparison.OrdinalIgnoreCase))
            : null;
}
