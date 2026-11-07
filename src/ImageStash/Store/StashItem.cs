using ImageStash.Layers;
using ImageStash.Media;
using Microsoft.Data.Sqlite;

namespace ImageStash.Store;

/// <summary>
/// An item as the store records it: its identity, which the store assigns once and never reuses; its name, tags and
/// favorite flag (F9); when it was added; its finished picture's file in the stash folder and that picture's facts,
/// which are the original's until the item is edited; the layer revision its finished picture was rendered for,
/// which changes whenever the picture does; and whether it is edited, its committed list of layers not empty, which the
/// menus' Revert to original and Save as new item and the preview's "Edited here" follow (G19).
/// </summary>
public sealed record StashItem(
    long Id,
    string Name,
    IReadOnlyList<string> Tags,
    bool Favorite,
    DateTimeOffset AddedAt,
    string FileName,
    string FilePath,
    MediaFacts Facts,
    long RenderedRevision,
    bool Edited)
{
    /// <summary>
    /// The store's <see cref="StoreContext.Commits"/> when this item was read, or after the commit that returned it: while
    /// the store has committed nothing since, no other item can have come to its file name, so its file is where it was
    /// read, or gone (a pending rename finished since, its clearing refused); never another item's.
    /// </summary>
    internal long CommitsWhenRead { get; init; }

    /// <summary>
    /// The store's <see cref="StoreContext.FileSteps"/> when this item was read, or after the step that placed it: a file
    /// step may move its file after the commit with no commit after it, its clearing refused (a change waiting on a held
    /// file while reads are answered, or one finishing a pending step), so its file is where it was read only while this
    /// still equals it too (G8).
    /// </summary>
    internal long FileStepsWhenRead { get; init; }

    /// <summary>
    /// Whether its row had file steps pending when this item was read. Every file step of a row follows a commit of
    /// those steps on that row (State: Every change), so only an item read with steps pending can have its file moved
    /// with no commit since; for any other, <see cref="FileStepsWhenRead"/> does not decide, and another item's step
    /// leaves it handed in (G35, "leaves unrelated items usable").
    /// </summary>
    internal bool StepsPendingWhenRead { get; init; }

    /// <summary>
    /// The columns <see cref="ListedFromRow"/> reads, in its order, for a query over <c>items</c> as <c>i</c>. Whether the
    /// item is edited is the committed list compared as text with the empty list's, so no listing parses a list: the store
    /// writes the empty list as <c>[]</c> alone, the column's default (<see cref="LayerList.ToJson"/>).
    /// </summary>
    internal const string Columns =
        "i.id, i.name, i.favorite, i.added_at, i.finished_file, i.format, i.width, i.height, i.animated, i.frame_count, i.duration_ms, i.rendered_revision, i.pending_steps, " +
        "i.finished_format, i.finished_width, i.finished_height, i.finished_animated, i.finished_frame_count, i.finished_duration_ms, i.layer_revision, i.layers <> '[]'";

    internal const int ColumnCount = 21;

    private const int OriginalFactsColumn = 5;

    private const int FinishedFactsColumn = 13;

    private const int LayerRevisionColumn = 19;

    private const int EditedColumn = 20;

    /// <summary>The rows a query may return; which of them are in the stash is <see cref="IsListed"/>'s.</summary>
    internal const string NotDeleted = "i.deleted = 0";

    /// <summary>
    /// Whether a row not marked deleted is in the stash: one with no pending step is, and one waiting only on steps
    /// already done on disk is too, because a live failure leaves the grid showing the picture on disk and the next
    /// start only clears the step (State: A live failure). So is one waiting on a rename, a Save or a Revert, done or
    /// not: its picture is whole on disk, and <see cref="FilePath"/> says where; and one waiting on an Open with…
    /// adoption or its Undo except while its picture is in <c>.work\</c> (<see cref="AdoptionSteps.ListsWhilePending"/>).
    /// Anything else waits for the next start.
    /// </summary>
    internal static bool IsListed(StashFolder folder, string? pendingSteps) =>
        pendingSteps is null || IsListed(folder, PendingStep.Decode(pendingSteps));

    internal static bool IsListed(StashFolder folder, IReadOnlyList<PendingStep> steps) =>
        AdoptionSteps.ListsWhilePending(folder, steps) ?? steps.All(step => step.ListsWhilePending || step.IsDone(folder));

    /// <summary>
    /// The item a row selected with <see cref="Columns"/> describes, stamped with the store's commits and file steps as they
    /// stand inside the read's transaction; or null when it is not <see cref="IsListed"/>.
    /// </summary>
    internal static StashItem? ListedFromRow(SqliteDataReader row, StoreContext store, IReadOnlyList<string> tags)
    {
        var steps = row.IsDBNull(12) ? [] : PendingStep.Decode(row.GetString(12));
        return IsListed(store.Folder, steps)
            ? FromRow(row, store.Folder, tags, steps) with { CommitsWhenRead = store.Commits, FileStepsWhenRead = store.FileSteps, StepsPendingWhenRead = steps.Count > 0 }
            : null;
    }

    private static StashItem FromRow(SqliteDataReader row, StashFolder folder, IReadOnlyList<string> tags, IReadOnlyList<PendingStep> steps)
    {
        var finishedFile = row.GetString(4);
        var picture = StoredFiles.PicturePath(folder, finishedFile, steps);
        // While a Save, a Revert, an adoption or its Undo waits the item is the picture on disk (State: A live failure):
        // once the change's own picture is there, the render, the adopted file or the restored one its step describes, or
        // the original back; before, the picture it had.
        var changed = StoredFiles.ShowsChangedPicture(folder, steps);
        return new(
            row.GetInt64(0),
            row.GetString(1),
            tags,
            row.GetBoolean(2),
            DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(3)),
            changed is null ? finishedFile : Path.GetFileName(picture),
            picture,
            changed is true
                ? steps.FirstOrDefault(step => step.Step is PendingStep.PlaceRender or PendingStep.PlaceAdopted or PendingStep.RestoreHeld)?.Facts ?? OriginalFacts(row)
                // The facts of the file handed over (G6, G8, G19): the render's once the item is edited, else its one file's.
                : row.IsDBNull(FinishedFactsColumn) ? OriginalFacts(row) : FactsFrom(row, FinishedFactsColumn),
            changed is true ? row.GetInt64(LayerRevisionColumn) : row.GetInt64(11),
            row.GetBoolean(EditedColumn));
    }

    /// <summary>
    /// The original's record in a row selected with <see cref="Columns"/>: what Media read from the file as it came in,
    /// which F3's rule for the finished picture reads (<see cref="FinishedFormat"/>).
    /// </summary>
    internal static MediaFacts OriginalFacts(SqliteDataReader row) => FactsFrom(row, OriginalFactsColumn);

    /// <summary>The committed list's revision in a row selected with <see cref="Columns"/>, as the column holds it.</summary>
    internal static long RecordedLayerRevision(SqliteDataReader row) => row.GetInt64(LayerRevisionColumn);

    /// <summary>The revision the finished file was last rendered for, as the column holds it, whatever is pending.</summary>
    internal static long RecordedRenderedRevision(SqliteDataReader row) => row.GetInt64(11);

    private static MediaFacts FactsFrom(SqliteDataReader row, int first) =>
        new(Enum.Parse<ImageFormat>(row.GetString(first)), row.GetInt32(first + 1), row.GetInt32(first + 2), row.GetBoolean(first + 3), row.GetInt32(first + 4), row.GetInt64(first + 5));

    /// <summary>The tags of every item, in the order they were added.</summary>
    internal static Dictionary<long, List<string>> AllTags(StoreContext store)
    {
        var tags = new Dictionary<long, List<string>>();
        foreach (var (id, tag) in store.Query("SELECT item_id, tag FROM tags ORDER BY rowid", r => (r.GetInt64(0), r.GetString(1))))
        {
            if (!tags.TryGetValue(id, out var list))
            {
                tags[id] = list = [];
            }

            list.Add(tag);
        }

        return tags;
    }

    /// <summary>Reads the listed items <paramref name="sql"/> selects, which names <see cref="Columns"/> first, in its order.</summary>
    internal static List<StashItem> Load(StoreContext store, string sql, params (string Name, object? Value)[] parameters)
    {
        var tags = AllTags(store);
        return store.Query(sql, r => ListedFromRow(r, store, tags.GetValueOrDefault(r.GetInt64(0)) ?? []), parameters).OfType<StashItem>().ToList();
    }

    /// <summary>How many items the stash lists (<see cref="IsListed"/>), read as its listing reads them, inside the caller's read.</summary>
    internal static int CountListed(StoreContext store) => Load(store, $"SELECT {Columns} FROM items i WHERE {NotDeleted}").Count;
}

/// <summary>
/// Where an item's files are: its finished picture in the root, and its original, which is the same file until the
/// item is edited and then lies in <c>originals\</c> (F6, F7); while a rename of a file is pending, wherever that file
/// is now, under its new name or its previous one; while a Save or a Revert is pending, wherever its steps have them.
/// Every reader of a row's files asks here, never the columns.
/// </summary>
internal sealed record StoredFiles(long Id, string FinishedFile, string OriginalFile, bool OriginalInOriginals, IReadOnlyList<PendingStep> Steps)
{
    /// <summary>The columns <see cref="FromRow"/> reads, in its order, for a query over <c>items</c> as <c>i</c>.</summary>
    public const string Columns = "i.id, i.finished_file, i.original_file, i.original_in_originals, i.pending_steps";

    public const int ColumnCount = 5;

    public static StoredFiles FromRow(SqliteDataReader row, int first) =>
        new(row.GetInt64(first), row.GetString(first + 1), row.GetString(first + 2), row.GetBoolean(first + 3),
            row.IsDBNull(first + 4) ? [] : PendingStep.Decode(row.GetString(first + 4)));

    /// <summary>
    /// Where the file a row names <paramref name="fileName"/> is now, in the root for a <see cref="PendingStep.RenameInRoot"/>
    /// and in <c>originals\</c> for a <see cref="PendingStep.RenameInOriginals"/>: where a pending rename to that name has
    /// it, else under that name.
    /// </summary>
    public static string Locate(StashFolder folder, string renameStep, string fileName, IReadOnlyList<PendingStep> steps) =>
        steps.FirstOrDefault(step => step.Step == renameStep && step.To == fileName) is { } rename
            ? rename.Located(folder)
            : renameStep == PendingStep.RenameInOriginals ? folder.InOriginals(fileName) : folder.InRoot(fileName);

    /// <summary>
    /// While a Save, a Revert, an adoption or its Undo waits, whether the picture it makes is the one on disk: an adopted
    /// file once it has landed; an Undo's previous picture once it has landed, before the Save's rule, since an edited
    /// one's render is placed as a Save's is; a Save's render once it is placed and the original, when the Save moves it,
    /// is in <c>originals\</c> (only the start's undo, which moves the original back first, separates the two); a
    /// Revert's original once it is back in the root. Null with none of them.
    /// </summary>
    public static bool? ShowsChangedPicture(StashFolder folder, IReadOnlyList<PendingStep> steps)
    {
        if (AdoptionSteps.IsUndo(steps))
        {
            return AdoptionSteps.Restored(folder, steps) is not null;
        }

        if (steps.FirstOrDefault(step => step.Step == PendingStep.PlaceAdopted) is { } adopted)
        {
            return adopted.IsDone(folder);
        }

        if (steps.FirstOrDefault(step => step.Step == PendingStep.PlaceRender) is { } place)
        {
            return place.IsDone(folder) && steps.All(step => step.Step != PendingStep.MoveToOriginals || step.IsDone(folder));
        }

        return steps.FirstOrDefault(step => step.Step == PendingStep.MoveToRoot)?.IsDone(folder);
    }

    /// <summary>
    /// The finished picture on disk, the file every handoff, copy and reveal uses (G8, G9): <paramref name="finishedFile"/>
    /// in the root, where a pending rename has it; while a Save's render is not the picture yet, the earlier render, or
    /// at a first Save the original where its move has it; while a Revert's original is not back, the render it deletes;
    /// once an adoption's file, or an Undo's previous picture, has landed, that file, which the row names only from its
    /// last transaction on.
    /// </summary>
    public static string PicturePath(StashFolder folder, string finishedFile, IReadOnlyList<PendingStep> steps)
    {
        if ((AdoptionSteps.Landed(folder, steps) ?? AdoptionSteps.Restored(folder, steps)) is { } landed)
        {
            return landed.ToPath(folder);
        }

        if (ShowsChangedPicture(folder, steps) is false)
        {
            if (steps.FirstOrDefault(step => step.Step == PendingStep.MoveToOriginals) is { } move)
            {
                return move.IsDone(folder) ? move.ToPath(folder) : move.FromPath(folder);
            }

            if (steps.FirstOrDefault(step => step.Step == PendingStep.DeleteInRoot) is { } delete)
            {
                return delete.FromPath(folder);
            }
        }

        return Locate(folder, PendingStep.RenameInRoot, finishedFile, steps);
    }

    /// <summary>
    /// Where the original is now: in the root while its first Save's move into <c>originals\</c> is not done, or once a
    /// Revert's move back is; the adopted file once an adoption's has landed, and the previous original where an Undo's
    /// steps have it once its picture has; otherwise where the row, and a pending rename, put it.
    /// </summary>
    public string OriginalPath(StashFolder folder)
    {
        if (AdoptionSteps.OriginalOnDisk(folder, Steps) is { } original)
        {
            return AdoptionSteps.PathOf(folder, original);
        }

        if (Steps.FirstOrDefault(step => step.Step is PendingStep.MoveToOriginals or PendingStep.MoveToRoot) is { } move)
        {
            return move.IsDone(folder) ? move.ToPath(folder) : move.FromPath(folder);
        }

        return OriginalInOriginals ? Locate(folder, PendingStep.RenameInOriginals, OriginalFile, Steps) : Locate(folder, PendingStep.RenameInRoot, OriginalFile, Steps);
    }

    /// <summary>
    /// Every file the item has on disk, each once with the column naming it: the finished picture, then the original
    /// when it is another file. One while unedited, and while a first Save or a Revert has the original as the picture;
    /// the finished picture and the original once edited. A render a Revert has yet to delete is no longer the item's.
    /// </summary>
    public IReadOnlyList<StoredFile> Files(StashFolder folder)
    {
        var picture = PicturePath(folder, FinishedFile, Steps);
        var original = OriginalPath(folder);
        return string.Equals(picture, original, StringComparison.OrdinalIgnoreCase)
            ? [new("finished_file", picture)]
            : [new("finished_file", picture), new("original_file", original)];
    }

    /// <summary>The paths of <see cref="Files"/>, in its order.</summary>
    public IReadOnlyList<string> Paths(StashFolder folder) => [.. Files(folder).Select(file => file.Path)];
}

/// <summary>One of an item's files: the column of <c>items</c> that names it, and where it belongs on disk.</summary>
internal sealed record StoredFile(string Column, string Path);
